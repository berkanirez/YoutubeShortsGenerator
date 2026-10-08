# Day 14 — Kod Notları

30 günlük roadmap, Gün 14. Konu: video üretimini HTTP isteğinden ayırmak. Bellek içi bir kuyruk (`Channel<T>`), tek tüketicili bir `BackgroundService` worker'ı, her iş için ayrı DI scope, ve job'ı kaldığı yerden devam ettiren bir pipeline.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. Problem: istek/cevap ile uzun süren iş

Day 13'te `POST /render` 15 saniye boyunca açık kaldı. Tam bir üretim (LLM script + LLM plan + TTS + render) birkaç dakika sürebilir. HTTP isteğini bu kadar uzun tutmanın üç sorunu var:
1. İstemciler (tarayıcı, `curl`, reverse proxy) zaman aşımına uğrar.
2. Bağlantı koparsa `CancellationToken` iptal olur ve iş yarıda öldürülür (Day 13'te bunun için özel bir koruma yazmak zorunda kaldık).
3. İki istek aynı anda gelirse iki render paralel çalışır, CPU'yu ikiye böler ve `renderer/public/assets` senkronizasyonunda yarışır.

Çözüm: istek sadece "işi kaydet ve kuyruğa at" der ve hemen döner. İşi ayrı, tek bir worker sırayla yapar.

---

## 2. `Application/VideoJobQueue.cs` — sözleşmeler

```csharp
public enum VideoJobWorkKind { Generate, Render }
public sealed record VideoJobWorkItem(Guid JobId, VideoJobWorkKind Kind);

public interface IVideoJobQueue
{
    bool TryEnqueue(VideoJobWorkItem item);                       // asla beklemez
    ValueTask<VideoJobWorkItem> DequeueAsync(CancellationToken ct);
    bool IsQueued(Guid jobId);
}

public interface IVideoJobProcessor
{
    Task ProcessAsync(VideoJobWorkItem item, CancellationToken ct);
}
```

**Neden:**
* İki tür iş var: `Generate` (kalan bütün aşamalar) ve `Render` (sadece render/yeniden render).
* `TryEnqueue` `bool` döndürüyor ve **beklemiyor**: kuyruk doluysa HTTP isteği bloklanmaz, hemen 503 döner.
* `IVideoJobProcessor` ayrı bir arayüz: worker "bir işi nasıl işleyeceğini" bilmiyor, sadece sırayla dağıtıyor. Böylece worker ve API testleri Ollama/Kokoro/Remotion olmadan çalışabiliyor (bölüm 9).
* Arayüzler Application'da, implementasyon Infrastructure'da. Bunun için Infrastructure → Application proje referansı eklendi. Clean Architecture'da normal yön budur: dış katman, iç katmanın tanımladığı arayüzleri uygular. Day 2'deki "Domain arayüzü, Infrastructure implementasyonu" (`IVideoJobRepository`) deseninin aynısı.

---

## 3. `Infrastructure/Jobs/ChannelVideoJobQueue.cs`

```csharp
_channel = Channel.CreateBounded<VideoJobWorkItem>(new BoundedChannelOptions(capacity)   // 100
{
    SingleReader = true,
    FullMode = BoundedChannelFullMode.Wait,
});
private readonly ConcurrentDictionary<Guid, byte> _waiting = new();

public bool TryEnqueue(VideoJobWorkItem item)
{
    if (!_waiting.TryAdd(item.JobId, 0)) return false;             // zaten kuyrukta
    if (_channel.Writer.TryWrite(item)) return true;
    _waiting.TryRemove(item.JobId, out _);                          // dolu: kaydı geri al
    return false;
}

public async ValueTask<VideoJobWorkItem> DequeueAsync(CancellationToken ct)
{
    var item = await _channel.Reader.ReadAsync(ct);
    _waiting.TryRemove(item.JobId, out _);
    return item;
}
```

**Neden:**
* **`Channel<T>`**: .NET'in yerleşik, thread-safe, async üretici/tüketici kuyruğu. `ReadAsync`, kuyruk boşken thread bloklamadan bekler (CPU harcamaz). Redis/RabbitMQ'nun tek process'lik, ücretsiz ve sıfır kurulumlu karşılığı (`CLAUDE.md` §15).
* **Bounded (sınırlı)**: sınırsız bir kuyruk, bir döngüde istek atan hatalı bir istemciyle belleği doldurabilir.
* **`FullMode.Wait` + `TryWrite`**: `Wait` modunda `WriteAsync` yer açılana kadar beklerdi. Biz `TryWrite` kullanıyoruz, o da dolu kuyrukta beklemek yerine anında `false` döndürüyor.
* **Tekrarı engelleme**: "Generate" butonuna iki kez basılırsa aynı job iki kez işlenip aynı dosyalar ve durum üzerinde kendisiyle yarışırdı. `_waiting` sözlüğü, kuyrukta **bekleyen** id'leri tutuyor. İş alındıktan sonra aynı job yeniden kuyruğa atılabilir (ör. bittikten sonra yeniden render).
* `SingleReader = true`: tek worker tasarımını kanala bildiriyor, kanal daha ucuz bir iç yapı kullanabiliyor.

---

## 4. `Infrastructure/Jobs/VideoJobWorker.cs` — `BackgroundService`

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        VideoJobWorkItem item;
        try { item = await _queue.DequeueAsync(stoppingToken); }
        catch (OperationCanceledException) { break; }

        CurrentJobId = item.JobId;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<IVideoJobProcessor>();
            await processor.ProcessAsync(item, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        catch (Exception ex) { _logger.LogError(ex, "Worker failed while processing job {JobId}", item.JobId); }
        finally { CurrentJobId = null; }
    }
}
```

**Neden / Nasıl çalışıyor:**
* **`BackgroundService`**: ASP.NET Core host'u uygulama açılınca `ExecuteAsync`'i başlatır, kapanırken `stoppingToken`'ı iptal eder. Uygulamayla birlikte yaşayıp ölen bir arka plan döngüsü.
* **Tek tüketici = bir seferde bir iş**: ayrı bir kilit (lock) gerekmiyor, döngünün kendisi sıralı. Render CPU-yoğun bir iş ve `public/assets` senkronizasyonu paylaşılan bir kaynak. Bu tasarımla ikisi de güvende.
* **Her iş için yeni bir DI scope**: bu, bugünün en önemli .NET dersi. `DbContext` ve repository'ler **scoped** olarak kayıtlı: bir iş birimi (normalde bir HTTP isteği) boyunca yaşamak üzere tasarlandılar. `BackgroundService` ise **singleton**, uygulama boyunca yaşıyor. Singleton bir scoped servisi doğrudan tutsaydı ("captive dependency"), tek bir `DbContext` saatlerce yaşar, takip ettiği entity'ler birikir, eski veri görür ve thread-safe olmadığı için bozulurdu. `IServiceScopeFactory.CreateAsyncScope()` her iş için tıpkı bir HTTP isteği gibi yeni bir scope açıyor. `await using` iş bitince scope'u (ve `DbContext`'i) dispose ediyor.
* **Hata izolasyonu**: `ProcessAsync` job hatalarını kendisi kaydediyor. Buraya kadar ulaşan bir exception bir bug demek. Loglanıyor ve döngü devam ediyor: kötü bir iş, arkasındaki bütün işleri durdurmamalı (test edildi).
* `JobServiceCollectionExtensions`: worker önce singleton olarak kaydediliyor, sonra `AddHostedService(provider => provider.GetRequiredService<VideoJobWorker>())` ile **aynı örnek** host'a veriliyor. Böylece `CurrentJobId` ileride bir dashboard'da (Day 21) gösterilebilir.

---

## 5. `Application/VideoGenerationPipeline.cs` — kaldığı yerden devam eden pipeline

```csharp
private async Task<Stage?> NextStageAsync(VideoJob job, CancellationToken ct) => job.Status switch
{
    VideoJobStatus.Created => Stage.Script,
    VideoJobStatus.Planning when job.GeneratedScript is null => Stage.Script,
    VideoJobStatus.Planning => await _planStore.LoadAsync(job.Id, ct) is null ? Stage.Plan : Stage.Narration,
    VideoJobStatus.Narrating => Stage.Render,
    _ => null,   // Rendering (başka yerde yarıda kalmış), Completed, Failed
};

// ProcessAsync:
for (var i = 0; i < 4; i++)
{
    var job = await _repository.GetByIdAsync(item.JobId, ct);
    var next = await NextStageAsync(job, ct);
    if (next is null) return;
    await RunStageAsync(next.Value, job.Id, ct);   // Script/Plan/Narration/Render servisleri
}
```

**Neden:**
* **Sıradaki aşama her seferinde kaydedilmiş durumdan belirleniyor**, sabit bir "1-2-3-4" listesinden değil. Bu sayede pipeline **devam ettirilebilir** (resumable): `autoGenerate=false` ile açılıp bazı aşamaları elle yapılmış bir job, ya da narration'ı elle yeniden üretilmiş bir job, kaldığı yerden devam ediyor. Day 26'da (yeniden başlatma sonrası kurtarma) bu özellik doğrudan kullanılacak.
* Pipeline yeni iş mantığı içermiyor: Day 4/5/11/13'te yazılan servisleri sırayla çağıran ince bir orkestratör. Her servis kendi doğrulamasını ve hata kaydını zaten yapıyor.
* `for` döngüsünün 4 ile sınırlı olması, aşama seçiminde ileride bir bug olursa sonsuz döngüye karşı bir emniyet.

```csharp
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
catch (Exception ex) when (IsExpectedStageFailure(ex))
{
    await FailIfStillRunningAsync(item.JobId, ex.Message);
}
catch (Exception ex)
{
    _logger.LogError(ex, ...);
    await FailIfStillRunningAsync(item.JobId, $"Unexpected error: {ex.Message}");
}
```

**Neden — bilinçli bir davranış farkı:** Day 11/13'te elle çağrılan endpoint'ler "motor kurulu değil" durumunda job'ı `Failed` yapmıyordu, çünkü kullanıcı ekranın başında, hatayı görüyor ve kurulumu yapıp devam edebiliyor. Arka planda ise kimse izlemiyor. Job `Narrating`'de sessizce kalsaydı kullanıcı sonsuza kadar bekleyen bir iş görürdü. Bu yüzden pipeline'da aynı eyleme dönük mesajla (`... Run: powershell ... setup.ps1`) `Failed` yapılıyor. Beklenmeyen exception'lar (bug) da job'ı `Failed` yapıyor ve worker'a kadar yükselmiyor. Uygulama kapanırken gelen iptal ise yukarı fırlatılıyor ki worker durabilsin.

`FailIfStillRunningAsync` job'ı **yeniden okuyor** (servis onu zaten `Failed` yapmış olabilir) ve `CancellationToken.None` kullanıyor: hatanın kaydı, hataya yol açan iptalden etkilenmemeli.

---

## 6. API değişiklikleri (`Program.cs`, `VideoJobContracts.cs`)

```csharp
public sealed record CreateVideoJobRequest(string Topic, string? SourceScript, bool AutoGenerate = true);
// VideoJobResponse'a yeni alan: bool IsQueued
```

| Endpoint | Önce | Şimdi |
|---|---|---|
| `POST /api/videos` | `201`, sadece kayıt | `202 Accepted` + kuyruğa `Generate` (`autoGenerate:false` → eski `201` davranışı) |
| `GET /api/videos` | yok | son işler, yeniden eskiye (`?take=`, en fazla 100) |
| `POST /api/videos/{id}/generate` | yok | job'ı kaldığı yerden worker'da sürdür (`202`) |
| `POST /api/videos/{id}/render` | senkron, 15 sn+ | ön kontrol (`409`) + kuyruğa `Render` (`202`) |

**Neden:**
* **`202 Accepted`**: HTTP'nin "isteği aldım, işlemi daha sonra yapacağım" kodu. `Location` başlığı, durumun izleneceği adresi (`/api/videos/{id}`) gösteriyor. İstemci bu adresi sorgulayarak ilerlemeyi takip ediyor (polling).
* **Render da kuyruktan geçiyor**: "bir seferde tek render" garantisi, render'ın bir yolunun kuyruğu atlayabildiği durumda anlamsız olurdu. Bariz sıra hataları (narration yokken render) yine senkron olarak `409` ile reddediliyor. Kullanıcı hatayı hemen görüyor, sonradan `Failed` olan bir job'la karşılaşmıyor.
* Aynı iş zaten kuyruktaysa `409 "already queued"`, kuyruk doluysa `503`.
* `script`/`plan`/`narration` endpoint'leri senkron kalıyor: hata ayıklama ve elle müdahale için. Aynı job'ı hem elle hem worker'da aynı anda çalıştırmak kullanıcının sorumluluğunda. README'de belirtildi.

---

## 7. Test yardımcıları (değişti)

* `InMemoryVideoPlanStore` artık isteğe bağlı bir `IJobOutputLocator` alıyor ve verildiğinde gerçek bir `plan.json` dosyası yazıyor. `FakeTtsProvider.WriteOutputFile` da `narration.wav` yazıyor. Sebebi: `RenderService`, gerçek bir render için gereken dosyaların diskte olup olmadığını kontrol ediyor. Uçtan uca pipeline testi bu kontrolden geçebilmeli.
* `FakeRenderer` `RenderServiceTests` içinden ortak bir dosyaya taşındı. Yeni `TempJobOutputLocator`, her test sınıfı için gerçek, boş bir geçici klasör sağlıyor.

---

## 8. Testler

* `VideoGenerationPipelineTests` (7): yeni job'ın gerçek servislerle (sadece motorlar sahte) baştan `Completed`'a gitmesi (2 LLM çağrısı, 1 TTS, 1 render); elle 3 aşaması yapılmış job'da **sadece render** çalışması (LLM/TTS'e hiç dokunulmaması); arka planda motor yokken job'ın kurulum ipucuyla `Failed` olması; script hatasında sonraki aşamaların çalışmaması; beklenmeyen exception'ın `Unexpected error: boom` olarak kaydedilmesi; tamamlanmış job'da `Generate`'in hiçbir şey yapmaması ve `Render`'ın yeniden render etmesi; kapanış iptalinin yukarı iletilmesi.
* `VideoJobQueueAndWorkerTests` (7): FIFO + bekleyen takibi; aynı job'ın iki kez kuyruğa girememesi; alındıktan sonra tekrar girebilmesi; dolu kuyrukta anında `false` + reddedilen kaydın temizlenmesi; worker'ın 4 işi sırayla ve **en fazla 1 eşzamanlılıkla** işlemesi (her iş 50 ms bekliyor ve eşzamanlılık ölçülüyor); hata fırlatan işten sonra worker'ın devam etmesi; her iş için **farklı** bir DI scope kullanılması.
* `VideoJobEndpointTests` (6, `WebApplicationFactory`, gerçek worker + kaydeden sahte processor): `POST /api/videos` → `202` + `Location` + worker'ın işi alması; `autoGenerate:false` → `201` ve kuyruğa girmeme; narration'sız job'a render → `409`; `/generate` ile elle açılmış job'ın kuyruğa girmesi; listenin yeniden eskiye sıralı olması; boş konu → `400`.

---

## Doğrulanan davranış

```
$ dotnet test
Başarılı! - Başarısız: 0, Başarılı: 48 - ShortsGenerator.Domain.Tests.dll
Başarılı! - Başarısız: 0, Başarılı: 58 - ShortsGenerator.Application.Tests.dll
Başarılı! - Başarısız: 0, Başarılı: 59 - ShortsGenerator.Infrastructure.Tests.dll
Başarılı! - Başarısız: 0, Başarılı:  7 - ShortsGenerator.Api.Tests.dll
(toplam 172; Day 13 sonunda 152)
```

Gerçek API + gerçek Kokoro + gerçek Remotion (Ollama'yı çalıştırmamak için plan aşaması örnek planla geçildi: `autoGenerate:false` → kaynak script → `samples/sample-plan.json` → `POST /generate`):

```
POST /generate -> HTTP 202 in 23 ms, Location=/api/videos/4fd8384d-...
   0.0s  Planning   queued=False
   0.6s  Narrating  queued=False      (worker işi aldı, Kokoro çalışıyor)
   7.2s  Rendering  queued=False      (narration + zamanlama bitti, Remotion çalışıyor)
  22.0s  Completed  queued=False

output/4fd8384d-.../  final.mp4 1,850,195 | narration.wav 811,436 | plan.json 3,260
```

Bu çıktılar şunu kanıtlıyor: POST işi hızlıca oluşturuyor (23 ms) ve arka plan worker'ı onu bitiriyor (roadmap kabul kriteri). Durumlar SQLite'a yazılıyor ve sorgulanabiliyor. İşler tek tek işleniyor, bir işin hatası diğerlerini durdurmuyor.

**Bilinçli olarak sonraya bırakılanlar:** kuyruk bellek içi. Uygulama kapanırsa bekleyen işler kaybolur ve yarıda kalan bir iş `Planning`/`Narrating`/`Rendering`'de kalır. `CLAUDE.md` §15 ve roadmap Day 26 ("startup handling of interrupted jobs") bunu ele alacak.
