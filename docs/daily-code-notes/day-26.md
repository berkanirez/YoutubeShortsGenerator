# Day 26 — Kod Notları

30 günlük roadmap, Gün 26. Konu: loglama, yeniden deneme ve kurtarma. API durduğunda veya çöktüğünde yarıda kalan işler bir sonraki açılışta kaldıkları yerden devam ediyor; `Failed` işler "Retry" ile son sağlam aşamadan yeniden başlıyor; her işin kendi `job.log` dosyası var; Ollama'nın geçici hataları bir kez daha deneniyor. Gerçek çökme testi sırasında bir hata daha bulundu: **öksüz kalan render süreçleri**. Bu da düzeltildi.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. Kuyruğun kalıcı yarısı: `VideoJob.PendingWork` (+ `VideoJobWorkKind` Domain'e taşındı)

```csharp
public VideoJobWorkKind? PendingWork { get; private set; }   // null = bekleyen iş yok
public int InterruptionCount { get; private set; }
public const int MaxInterruptions = 2;

public void MarkQueued(VideoJobWorkKind kind, DateTimeOffset now)  { PendingWork = kind; ... }
public void MarkWorkFinished(DateTimeOffset now)                   { PendingWork = null; InterruptionCount = 0; ... }
```

Migration `20261008195130_JobPendingWork`: iki sütun (`PendingWork TEXT NULL`, `InterruptionCount INTEGER NOT NULL DEFAULT 0`).

**Neden:** Kuyruk (`Channel<T>`, Day 14) yalnızca bellekte duruyor. API kapanınca "bu işin videosu yapılacaktı" bilgisi kayboluyordu. Day 21'de yarıda kalan iki iş bu yüzden hâlâ "Paused" görünüyor. Artık niyet SQLite'ta da tutuluyor.

**Nasıl çalışıyor:** Bu, "outbox" fikrinin küçük bir akrabası: önce kalıcı kayıt, sonra bellekteki kanal. İşin türü artık kalıcı domain durumunun parçası olduğu için `VideoJobWorkKind` enum'u Application katmanından Domain'e taşındı (Domain, Application'ı tanımıyor). `HasConversion<string>()` enum'u veritabanına `"Renarrate"` gibi isimle yazıyor; bu, sayıyla yazmaktan daha okunur ve enum sırası değişince bozulmaz.

---

## 2. `VideoJobScheduler`: kuyruğa almanın tek yolu

```csharp
if (job.PendingWork is not null || _queue.IsQueued(job.Id)) return ScheduleResult.AlreadyQueued;
job.MarkQueued(kind, now);  await _repository.UpdateAsync(job, ct);   // 1) önce kaydet
if (_queue.TryEnqueue(new VideoJobWorkItem(job.Id, kind))) return ScheduleResult.Queued;  // 2) sonra kanala
job.MarkWorkFinished(now); ...; return ScheduleResult.QueueFull;     // kanal dolu: niyet geri alınır
```

**Neden:**
* **Sıra önemli**: worker işi hemen alabilir. Kayıt kanaldan sonra yapılsaydı, worker işi bitirip işareti silse bile geç gelen kayıt işareti geri yazardı ve iş bir sonraki açılışta boşuna tekrar çalışırdı. Test bunu `OnEnqueue` kancasıyla, item'ın kanala girdiği **anda** ölçüyor.
* **Bulunan yarış durumu**: `IsQueued` yalnızca *bekleyen* işleri görüyordu, *çalışan* işi görmüyordu. Bir Renarrate'in ses aşamasında (durum yalnızca `Narrating`) "Re-render"a tekrar basmak işi ikinci kez kuyruğa alıyor, plan düzenleyici de planı değiştirmeye izin veriyordu. `PendingWork` hem "bekliyor" hem "çalışıyor" demek. Scheduler, `PlanEditService` ve `apply-edits` artık buna bakıyor.
* Bütün kuyruğa alma noktaları (`POST /videos`, `/generate`, `/render`, `/apply-edits`, `/retry`) scheduler'ı kullanıyor; HTTP cevabına çevirme tek yerde yapılıyor (`Api/Endpoints/QueueResults.cs`).

---

## 3. Pipeline: işareti silmek ve "ses gerçekten bitti mi?" (`VideoGenerationPipeline`)

```csharp
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { shuttingDown = true; ...; throw; }
finally { if (!shuttingDown) await MarkWorkFinishedAsync(item.JobId); }

public static bool IsNarrated(VideoPlan plan) =>
    !plan.NarrationOutdated && plan.Scenes.Any(s => s.SpokenDurationSeconds is not null);

VideoJobStatus.Narrating => plan is { } && IsNarrated(plan) ? Stage.Render : Stage.Narration,   // eskiden: hep Render
```

**Neden:**
* İş başarıyla da bitse, bilinen bir hatayla da bitse işaret siliniyor. Yalnızca **kapanma** sırasında siliniyor, böylece iş bir sonraki açılışta devam ediyor.
* **Bulunan hata**: `Narrating` durumu ses sentezi *başlarken* yazılıyor. Sentez yarıda kalırsa iş `Narrating` ama plan zamanlanmamış oluyordu, ve pipeline doğrudan Render'a geçip eski ya da eksik sesle video üretirdi. Artık planın gerçekten sesten zamanlanıp zamanlanmadığına bakılıyor (Day 12'nin `SpokenDurationSeconds` alanı); zamanlanmamışsa ses aşaması yeniden çalışıyor.

---

## 4. Başlangıçta kurtarma (`JobRecoveryService.RecoverInterruptedAsync` + `JobRecoveryHostedService`)

```csharp
public bool RecoverAfterInterruption(DateTimeOffset now)   // VideoJob
{
    InterruptionCount++;
    if (InterruptionCount > MaxInterruptions) { PendingWork = null; MarkFailed("Stopped after being interrupted 3 times..."); return false; }
    if (Status is Rendering) Status = Narrating;   // yarım render hiçbir şey üretmedi (final.rendering.mp4 -> rename yalnızca başarıda)
    return true;
}

// servis: PendingWork != null olan her iş -> kurtar -> kanala (Completed ise kendi türüyle, yoksa Generate)
```

**Neden:**
* Sadece `PendingWork`'ü olan işler devam ettiriliyor. `autoGenerate=false` ile oluşturulup bilerek `Planning`'de bırakılan işler (Day 19'daki 10 plan denemesi gibi) ellenmiyor; onları devam ettirmek kimsenin istemediği dakikalarca CPU yükü demek olurdu.
* **Zehirli iş koruması**: bir iş API'yi her seferinde çökertiyorsa (ör. bellek), sonsuza kadar devam ettirilip her açılışı çökertmesin. Üçüncü kesintide iş `Failed` oluyor ve mesaj ne olduğunu söylüyor. Başarıyla biten her iş sayacı sıfırlıyor.
* `Generate` ile devam etmek güvenli, çünkü pipeline Day 14'ten beri "kaldığı yerden" çalışıyor (idempotent devam). `Completed` bir işin henüz başlamamış re-render/renarrate'i ise kendi türüyle devam ediyor.

**Nasıl çalışıyor:** `IHostedService.StartAsync`, `Program.cs` migration'ları uyguladıktan sonra ve sunucu istek kabul etmeye başlamadan önce bir kez çalışıyor; o anda başka hiçbir şey kuyruğa iş eklemiyor. Kurtarma bir kolaylık: hata alırsa loglanıyor ama API yine açılıyor.

---

## 5. Retry (`JobRecoveryService.RetryAsync`, `VideoJob.RetryFrom`, `POST /api/videos/{id}/retry`, dashboard "Retry")

```csharp
// diskte ne varsa ona göre en son sağlam aşama:
if (plan zamanlanmış && narration.wav var)                return Narrating;   // sadece render
if (plan var || GeneratedScript != null || SourceScript)  return Planning;    // pipeline script/plan/ses'ten uygun olanı seçer
return Created;

public void RetryFrom(VideoJobStatus resumeAt, ...)   // yalnızca Failed'dan; yalnızca Created/Planning/Narrating'e; hata temizlenir
```

**Neden:** Day 24'ten kalan borç. `Failed` bir iş çıkmaz sokaktı: düzenlenemiyor, devam ettirilemiyordu; başarısız bir re-render iyi bir videoyu bile kilitliyordu. Retry hiçbir şeyi boşa harcamıyor: ses varsa yeniden üretilmiyor, plan varsa LLM'e tekrar sorulmuyor. Domain, `Rendering` veya `Completed`'a doğrudan dönmeyi yasaklıyor (test edildi). Bu durumlar yalnızca gerçekten render edilerek elde edilebilir.

---

## 6. İş başına log: `Infrastructure/Logging/JobLogFileLoggerProvider.cs`

```csharp
public sealed class JobLogFileLoggerProvider : ILoggerProvider, ISupportExternalScope
// Log(): scope'larda "JobId" varsa -> output/<job-id>/job.log'a bir satır (+ exception)
// Microsoft.*/System.* kategorileri yalnızca Warning ve üstü (EF her SQL komutunu Information'da loglar)
services.AddSingleton<ILoggerProvider, JobLogFileLoggerProvider>();
```

**Neden:** "Bu video neden başarısız oldu?" sorusunun cevabı, artık kapanmış olabilecek bir konsolda değil, videonun klasöründe, `plan.json` ve `final.mp4`'ün yanında duruyor. Dashboard, hata mesajının altında "Details: job.log in this video's folder" diyor.

**Nasıl çalışıyor:**
* **Log scope**: pipeline her işe `BeginScope({ JobId })` açıyor (Day 14). Scope'lar `AsyncLocal` ile `await`'ler boyunca akıyor; bu yüzden Ollama, Kokoro ve Remotion adaptörlerinin logları da, kendileri JobId'yi bilmeden, doğru dosyaya düşüyor. Kurtarma ve Retry de aynı scope'u açıyor.
* `ISupportExternalScope`: LoggerFactory bütün provider'lara ortak bir scope sağlayıcısı veriyor; provider, scope'u konsol logger'ıyla paylaşıyor.
* Stack trace dosyaya yazılıyor. Dosya kullanıcının makinesinde kalıyor ve hata ayıklamak için gereken tam olarak bu. API cevabında ise asla stack trace yok (Day 24).
* Test: 4 iş paralel loglanıyor ve her dosyada yalnızca kendi işinin 20 satırı var.

---

## 7. Sınırlı yeniden deneme: Ollama (`OllamaLlmProvider`, `OllamaOptions.MaxAttempts/RetryDelaySeconds`)

```csharp
catch (HttpRequestException ex) when (!isLastAttempt) { await WaitBeforeRetryAsync(...); continue; }   // bağlantı reddi
if (response.StatusCode == ServiceUnavailable && !isLastAttempt) { ...; continue; }                    // 503 meşgul
// 404 (model yok), 500 (bellek yetmiyor), zaman aşımı -> tekrar YOK
```

**Neden:** Yeniden deneme yalnızca **geçici** hatalarda işe yarar. Ollama uygulaması yeni açılıyorsa bağlantı birkaç saniye reddedilir; 3 sn sonra tekrar denemek işi kurtarır. Kalıcı hatalar ise ikinci denemede de aynı sonucu verir: model indirilmemiş (404), model RAM'e sığmıyor (500). 2 dakikalık bir zaman aşımını tekrar etmek de kullanıcıyı 4 dakika bekletip aynı sonuca götürür. Bu ayrım (transient vs permanent) her retry politikasının temeli. LLM'in bozuk JSON'u için zaten ayrı bir yeniden deneme var (Day 15 repair prompt'u). Kokoro ve Remotion hataları ise deterministik (aynı girdi → aynı hata), bu yüzden onlar için otomatik yeniden deneme yok; kullanıcı düzeltip Retry'a basıyor.

---

## 8. Bulunan ve düzeltilen: API çökünce öksüz kalan render süreçleri (`Processes/KillOnExitJob.cs`)

Gerçek çökme testinde API render sırasında öldürüldü. Sonra:
```
ProcessId ParentProcessId Name     cmd
     6300           25116 node.exe "node" ...\@remotion\cli\remotion-cli.js render src/index.ts ShortComposition ...
```
**Neden (kök sebep):** `Process.Kill(entireProcessTree: true)` (Day 11) yalnızca **biz hayattayken** çağrılabiliyor. Ana süreç zorla öldürülünce (çökme, Görev Yöneticisi, elektrik) çocuk node ve onun Chrome'u öksüz kalıp render etmeye devam ediyor. Bu hem CPU yakıyor hem de kurtarılan yeni render'la aynı `public/jobs/<id>` klasörü için yarışıyor.

```csharp
var job = CreateJobObject(...);  SetInformationJobObject(job, ExtendedLimitInformation, { LimitFlags = KILL_ON_JOB_CLOSE });
// ProcessRunner: process.Start() sonrası -> KillOnExitJob.Add(process)  (AssignProcessToJobObject)
```

**Nasıl çalışıyor:** Windows'un **Job Object**'i bir süreç grubu. Bir sürecin çocukları (node'un açtığı Chrome, Python'un worker'ları) varsayılan olarak aynı job'a katılıyor. `KILL_ON_JOB_CLOSE`, job'a ait son handle kapandığında içindeki her şeyin işletim sistemi tarafından sonlandırılması demek. Handle'ı tutan bizim süreç ve onu hiç kapatmıyoruz; bizim süreç hangi sebeple ölürse ölsün handle kapanıyor ve çocuklar da ölüyor. Linux'taki karşılığı `prctl(PR_SET_PDEATHSIG)`; şimdilik yalnızca Windows destekleniyor, diğer işletim sistemlerinde kod hiçbir şey yapmıyor.

Doğrulama (gerçek):
```
status=Rendering; render nodes while rendering: 1
API killed
render nodes 3s after the crash: 0
```

---

## Doğrulanan davranış

```
$ dotnet test   -> Domain 116, Application 128, Infrastructure 107, Api 26   (toplam 377; Day 25: 344)
$ npm test      -> 55/55
```
Yeni testler: `JobRecoveryTests` (11: kayıt kanaldan önce, çalışan iş tekrar kuyruğa alınamaz, dolu kuyruk iz bırakmaz, sadece bekleyen işler devam eder, render Narrating'den yeniden başlar, Completed türünü korur, üçüncü kesintide vazgeçilir, Retry'ın üç devam noktası, Failed olmayan iş reddi), pipeline (+3: işaret başarıda/hatada silinir, kapanmada kalır, yarım kalan ses yeniden üretilir), Domain (+8), Ollama (+5: bağlantı reddi tekrar denenip başarılı, 503 tekrar, 404/500/zaman aşımı tekrar edilmez), `JobLogFileLoggerProviderTests` (3), repository (+1, gerçek migration ile), API (+2). En kritik iki kontrol bilerek bozuldu ve testler kırmızıya döndü (ardından geri yüklendi).

Gerçek uçtan uca senaryo (job `f7b7f83d`, kendi script'iyle "Why Rest Days Make You Stronger"):
1. **Bozuk bağımlılık**: API `Ollama__BaseUrl=http://localhost:11999` ile başlatıldı → bağlantı reddi → 3 sn sonra ikinci deneme → iş `Failed`: *"Could not reach Ollama at http://localhost:11999/. Is it running? Start it from the Ollama app, or run 'ollama serve'."*
2. Doğru ayarla yeniden başlat → **Retry** → `Planning`'den devam (script vardı, plan yoktu) → plan (22 sn), ses (15.6 sn), render başladı.
3. Render ortasında API zorla öldürüldü (1. kesinti) → yeniden başlat → kurtarma işi `Rendering`'den `Narrating`'e aldı, render yeniden başladı.
4. 2. ve 3. kesinti (aynı şekilde; arada render süreçleri Job Object sayesinde öldü) → açılışta: *"Stopped after being interrupted 3 times in a row while 'Rendering' ... See job.log, then use Retry."*
5. **Retry** → `Narrating`'den devam (zamanlanmış plan + ses vardı, yeniden üretilmedi) → 24 sn sonra `Completed`, `final.mp4` 5.3 MB.

`job.log` bu hikâyenin tamamını içeriyor (kısaltılmış):
```
07:40:43 INFORMATION VideoGenerationPipeline: Job f7b7…: running stage Plan
07:40:47 WARNING     OllamaLlmProvider: Ollama call failed (Hedef makine etkin olarak reddettiğinden bağlantı kurulamadı. (localhost:11999)); trying again in 3s (attempt 2 of 2)
07:40:54 WARNING     VideoGenerationPipeline: Job f7b7… stopped: Could not reach Ollama at http://localhost:11999/. …
07:41:13 INFORMATION VideoGenerationPipeline: Job f7b7…: running stage Plan
07:41:35 INFORMATION ScenePlanningService: Job f7b7…: scene plan accepted on attempt 1 (7 scenes, …
07:41:50 INFORMATION KokoroTtsProvider: Kokoro synthesized 30.64s of audio (7 segments) with voice af_heart in 15.6s
07:41:50 INFORMATION VideoGenerationPipeline: Job f7b7…: running stage Render
07:43:03 INFORMATION VideoGenerationPipeline: Job f7b7…: starting Generate          <- kurtarma sonrası
…
07:44:09 INFORMATION RemotionRenderer: Rendered job f7b7… to …\final.mp4
07:44:09 INFORMATION VideoGenerationPipeline: Job f7b7… finished with status Completed
```
(Kurtarma ve Retry'ın kendi satırları bu çalıştırmada konsola yazıldı ama job.log'a yazılmadı, çünkü o kod JobId scope'unun dışındaydı. İkisine de scope eklendi.)

Roadmap kabul kriteri ("intentionally failing a dependency results in useful diagnostics") karşılandı. Kullanıcı neyin bozulduğunu, nasıl düzelteceğini ve işin geçmişini görüyor, ve düzelttikten sonra tek tıkla devam edebiliyor.

## Bilinçli olarak bırakılanlar
* Day 21'den kalan iki eski iş (`Created`/`Planning`) `PendingWork` alanı eklenmeden önce yarıda kalmıştı; otomatik devam ettirilmiyorlar. Dashboard'daki "Continue" ile devam ettirilebilirler.
* Linux/macOS'ta öksüz süreç koruması yok (V1 Windows'ta çalışıyor).
