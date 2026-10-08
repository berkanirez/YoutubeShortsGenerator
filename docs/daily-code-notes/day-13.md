# Day 13 — Kod Notları

30 günlük roadmap, Gün 13. Konu: ASP.NET backend'in Remotion renderer'ı kendisinin çalıştırması. `IRenderer` arayüzü, güvenli process argümanları (shell ve `.cmd` yok), job dosyalarının renderer'a hazırlanması (staging), stdout/stderr'den anlaşılır hata çıkarma, timeout/iptal ve `output/<job-id>/final.mp4`.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. Domain: `IRenderer`, istek/sonuç tipleri, hatalar

```csharp
public interface IRenderer
{
    Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken);
    Task<RendererAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);
}

public sealed record RenderRequest(Guid JobId, string PlanPath, string? NarrationPath, string OutputPath);
public sealed record RenderResult(string OutputPath, long FileSizeBytes, TimeSpan Elapsed);
public sealed record RendererAvailability(bool IsReady, string Engine, IReadOnlyList<string> Problems, string? SetupHint = null);

public sealed class RendererUnavailableException : Exception { ... }   // Node/npm install eksik -> 503
public sealed class RenderFailedException : Exception { ... }          // render bu job için başarısız -> 422
```

**Neden:** `ILlmProvider` (Day 3) ve `ITtsProvider` (Day 11) ile aynı desen, `CLAUDE.md` §21'deki dördüncü değiştirilebilir arayüz. Uygulamanın geri kalanı "Remotion", "Node", "Chrome" kelimelerini bilmiyor, sadece "plan + ses gir, MP4 çık" diyor. İleride bulut renderer'a geçmek yeni bir adapter yazmak demek. Hata türleri yine ikiye ayrılmış: makine kurulumu eksik / bu iş başarısız.

---

## 2. `VideoJob.MarkRendering` — tamamlanmış işi yeniden render (Day 2 dosyası, değişti)

```diff
-    public void MarkRendering(DateTimeOffset now) =>
-        TransitionTo(VideoJobStatus.Rendering, now, VideoJobStatus.Narrating);
+    public void MarkRendering(DateTimeOffset now) =>
+        TransitionTo(VideoJobStatus.Rendering, now, VideoJobStatus.Narrating, VideoJobStatus.Completed);
```

**Neden:** `CLAUDE.md` §14 `POST /api/videos/{id}/render` için "manually trigger/re-trigger" diyor. Renderer'da görsel bir iyileştirme yapıldığında (Day 16 gibi), bitmiş bir videoyu yeniden almak için script'i ve sesi baştan üretmek gereksiz. Önceki `FinalVideoPath` yeni render tamamlanana kadar korunuyor (test edildi).

---

## 3. `Rendering/RemotionOptions`

```csharp
public string NodePath { get; set; } = "node";
public string RendererDirectory { get; set; } = "../../renderer";   // content root'a göre (Day 11)
public string EntryPoint { get; set; } = "src/index.ts";
public string CompositionId { get; set; } = "ShortComposition";
public int TimeoutSeconds { get; set; } = 900;
```

**Neden:** Yollar ve süreler kodda değil konfigürasyonda (`appsettings.json` → `"Remotion"`). `RendererDirectory`, Day 11'deki `ContentRootPath` + `PostConfigure` ile mutlak yola çevriliyor.

---

## 4. `Rendering/RemotionRenderer` — adapter

```csharp
// 1. sync:    node scripts/sync-public-assets.mjs         (assets/ -> renderer/public/assets)
// 2. staging: renderer/public/jobs/<job-id>/{plan.json, narration.wav, render-props.json}
// 3. render:  node node_modules/@remotion/cli/remotion-cli.js render src/index.ts ShortComposition
//                  <output>/final.rendering.mp4 --props <staging>/render-props.json --log warn
// 4. final.rendering.mp4 -> final.mp4 (File.Move overwrite), staging klasörü silinir (finally)
```

### 4a. `npx` değil, `node` + CLI dosyası

**Neden:** Elle render ederken `npx remotion render ...` kullanıyorduk. Ama Windows'ta `npx` ve `npm` aslında `.cmd` dosyaları. Bir `.cmd` dosyasını başlatmak araya `cmd.exe`'yi sokar ve `cmd.exe` argümanları kendi kurallarıyla yeniden ayrıştırır: `&`, `|`, `%` karakterleri yeniden anlam kazanır. Bu, `ArgumentList` kullansanız bile argüman güvenliğini delebilen, bilinen bir açık sınıfı ("BatBadBut", 2024). `node`'u doğrudan başlatıp Remotion CLI'nin JavaScript dosyasını ilk argüman olarak vermek bu riski tamamen ortadan kaldırıyor: araya hiçbir shell girmiyor.

**Nasıl çalışıyor:** `node_modules/.bin/remotion` aslında `@remotion/cli/remotion-cli.js`'i çağıran bir sarmalayıcı. Biz sarmalayıcıyı atlayıp hedefi doğrudan çalıştırıyoruz. Asset senkronizasyon script'i de aynı şekilde `node scripts/sync-public-assets.mjs` ile çalıştırılıyor (`npm run sync-assets` yerine). `WorkingDirectory = renderer/` olduğu için `remotion.config.ts` (rspack, jpeg, overwrite) otomatik uygulanıyor.

### 4b. Props bir **dosya** olarak

```csharp
File.WriteAllText(propsPath, JsonSerializer.Serialize(
    new { planPath = $"jobs/{jobId}/plan.json", narrationPath = $"jobs/{jobId}/narration.wav" }, JsonOptions));
// ... "--props", propsPath
```

**Neden:** Day 9-12'de props'u komut satırına JSON olarak yazıyorduk (`--props='{"planPath":...}'`). Tırnak kaçışı her shell'de farklı ve komut satırının uzunluk sınırı var. Remotion `--props`'a bir dosya yolu da kabul ediyor. Dosya: kaçış derdi yok, sınır yok.

### 4c. Staging — neden dosyaları kopyalıyoruz?

**Neden:** Day 8'de öğrendik: Remotion'ın composition kodu headless Chrome içinde çalışıyor ve dosyaları sadece `renderer/public/` altından `staticFile()` + `fetch()` ile okuyabiliyor. Job'ın dosyaları ise `output/<job-id>/` altında. Bu yüzden render süresince `public/jobs/<job-id>/` altına kopyalanıp iş bitince (başarı **veya** hata, `finally` bloğu) siliniyor.

**Güvenlik (`CLAUDE.md` §18):** staging klasörünün adı job'ın `Guid`'i, kopyaların adları sabit (`plan.json`, `narration.wav`). Plandan veya LLM'den gelen hiçbir değer dosya yoluna dönüşmüyor.

### 4d. Atomik çıktı

```csharp
var temporaryOutput = Path.ChangeExtension(request.OutputPath, ".rendering.mp4");   // final.rendering.mp4
...
if (!File.Exists(temporaryOutput) || new FileInfo(temporaryOutput).Length == 0) throw new RenderFailedException(...);
File.Move(temporaryOutput, request.OutputPath, overwrite: true);
```

**Neden:** Remotion yarıda çökerse geride yarım bir `final.mp4` kalmamalı. Kullanıcı o dosyayı açıp "video bozuk" sanabilir, ya da yeniden render'da önceki iyi video kaybolabilir. Day 11'deki TTS script'inin `.tmp.wav` + `os.replace` deseninin aynısı. "Başarılı çıktı (exit 0) ama dosya yok/boş" durumu da ayrıca yakalanıyor.

### 4e. Kullanılabilirlik kontrolü

```csharp
if (!File.Exists(CliPath)) problems.Add("Remotion is not installed (renderer/node_modules is missing)");
var node = await _processRunner.RunAsync(new ProcessSpec(_options.NodePath, ["--version"], ...));
// -> SetupHint: "Install Node.js, then run: cd renderer && npm install"
```

**Neden:** `node`, PATH'te aranan bir komut. `File.Exists` ile kontrol edilemez, bu yüzden gerçekten `node --version` çalıştırılıyor (~50 ms). `node_modules` eksikse (repo yeni klonlandı, `npm install` yapılmadı) hata mesajı ne yapılacağını söylüyor.

---

## 5. Application: `RenderService`

```csharp
if (job.Status is not (VideoJobStatus.Narrating or VideoJobStatus.Completed)) throw new InvalidOperationException(...);
if (!File.Exists(planPath) || !File.Exists(narrationPath)) throw new InvalidOperationException("... is missing narration.wav ...");

var availability = await _renderer.CheckAvailabilityAsync(ct);
if (!availability.IsReady) throw new RendererUnavailableException(...);   // job'a dokunmadan

job.MarkRendering(now); await _repository.UpdateAsync(job, ct);
try
{
    result = await _renderer.RenderAsync(new RenderRequest(jobId, planPath, narrationPath, finalPath), ct);
}
catch (Exception ex) when (ex is RendererUnavailableException or RenderFailedException or OperationCanceledException)
{
    var reason = ex is OperationCanceledException ? "Rendering was cancelled before it finished." : ex.Message;
    job.MarkFailed(reason, now);
    await _repository.UpdateAsync(job, CancellationToken.None);
    throw;
}
job.MarkCompleted(result.OutputPath, now);
```

**Neden:**
* Sıra kontrolü ve kurulum kontrolü, Day 11'deki `NarrationService` ile aynı mantıkta: kurulum eksikliği job'ı yakmaz.
* **İptal durumu**: render HTTP isteğinin `CancellationToken`'ı ile çalışıyor. İstemci bağlantıyı keserse `ProcessRunner` Chrome/Node'u öldürür ve `OperationCanceledException` fırlar. Bunu yakalamasaydık job sonsuza kadar `Rendering`'de kalırdı ve onu oradan çıkaracak hiçbir kod yolu olmazdı. Kaydetmede `CancellationToken.None` kullanılıyor, çünkü isteğin token'ı zaten iptal edilmiş. Onu kullanırsak "başarısız oldu" bilgisi de kaydedilemezdi. (Day 14'te render zaten HTTP isteğinden ayrılacak, ama bu koruma orada da gerekli.)
* İlk yazdığım versiyonda hata yolu `try/catch` sonrasında koşullu bir `throw` ile dolambaçlıydı. `NarrationService`'deki "yakala → Failed işaretle → kaydet → tekrar fırlat" desenine çevrildi.

---

## 6. API: `POST /api/videos/{id}/render`, `GET /api/renderer/health`

```csharp
app.MapPost("/api/videos/{id:guid}/render", async (Guid id, RenderService service, CancellationToken ct) => { ... })
// 200 job (Completed, finalVideoPath) | 404 | 409 sıra | 503 kurulum | 422 render hatası
```

**Neden:** Day 11 endpoint'leriyle aynı HTTP eşlemesi. Bugün istek render bitene kadar bekliyor (senkron). Roadmap'e göre bu Day 14'ün konusu (arka plan kuyruğu).

---

## 7. Hata/düzeltme: render hatası okunamıyordu

İlk gerçek hata testi (izin listesinde olmayan `"animation": "explode"` içeren bir plan) şunu döndürdü:

```
{"error":"Remotion render failed (exit code 1): Was not able to close puppeteer page ProtocolError: ...
 | at createProtocolError (...Connection.js:227:25) | at #onMessage ... | ... | Definition  ZodError"}
```

İki ayrı kök neden vardı. İkisi de gerçek CLI çıktısını ayrı ayrı (stdout/stderr) yakalayarak bulundu:

**Kök neden 1 — Remotion hatanın içeriğini düşürüyor.** Renderer, Zod'un `parse()` fonksiyonunun fırlattığı `ZodError`'u CLI'ye sadece sınıf adıyla (`ZodError`) iletiyordu. Ayrıca bir hata mesajının **yalnızca ilk satırını** basıyor. `--log info` ile de sonuç aynıydı, yani sorun log seviyesi değildi.

```ts
// renderer/src/schemas/videoPlan.ts
export function parseVideoPlan(json: unknown, source: string): VideoPlan {
  const result = VideoPlanSchema.safeParse(json);
  if (!result.success) {
    const issues = result.error.issues.map((issue) => `${formatPath(issue.path)}: ${issue.message}`).join("; ");
    throw new Error(`Invalid plan JSON (${source}): ${issues}`);
  }
  return result.data;
}
// ["scenes", 1, "animation"] -> "scenes[1].animation"
```

`safeParse` exception fırlatmak yerine `{success, data | error}` döndürür. Hatayı kendimiz **düz bir `Error`** olarak, bütün sorunları **tek satırda** birleştirerek fırlatıyoruz. `formatPath`, Zod'un yol dizisini okunur hale getiriyor. Fonksiyon composition'dan ayrıldı, böylece test edilebiliyor (3 yeni Vitest testi: örnek plan geçerli; hata düz `Error`, alanı ve izin verilen değerleri söylüyor, satır sonu içermiyor; birden fazla sorunun hepsi raporlanıyor).

**Kök neden 2 — .NET özeti yanlış satırları seçiyordu.** Gerçek hata satırı (` Error  Error: Invalid plan JSON...`) stderr'de, Remotion'ın Chrome'u kapatırken bastığı 15 satırlık zararsız bir puppeteer stack trace'inin **ardından** geliyordu. "Son 15 satır" özetinin neredeyse tamamı bu gürültüden oluşuyordu.

```csharp
private static string SummarizeFailure(ProcessResult result)
{
    var lines = (result.StandardError + "\n" + result.StandardOutput).Split('\n', ...);
    var errorLine = lines.LastOrDefault(line =>
        ErrorLinePattern().IsMatch(line) &&                 // "...Error: mesaj"
        !line.StartsWith("at ") &&                          // stack satırı değil
        !line.Contains("puppeteer", OrdinalIgnoreCase) &&   // kapanış gürültüsü değil
        !line.Contains("ProtocolError"));
    if (errorLine is not null) return ErrorLinePattern().Match(errorLine).Groups["message"].Value.Trim();
    return string.Join(" | ", lines.TakeLast(15));         // tanınmayan durumda eski davranış
}

[GeneratedRegex(@"(?<message>\b[A-Za-z]*Error: .+)$")]
private static partial Regex ErrorLinePattern();
```

Bu, yakalanan **gerçek** stderr'in kopyasıyla test ediliyor (`RenderAsync_RealRemotionFailureOutput_ReportsTheRealErrorNotThePuppeteerNoise`). Düzeltmeden sonra aynı senaryo:

```
{"error":"Remotion render failed (exit code 1): Error: Invalid plan JSON (jobs/0592daa9-.../plan.json):
 scenes[1].animation: Invalid option: expected one of \"fade\"|\"slide\"|\"pop\"|\"punchZoom\"|...;
 scenes[2].animation: Invalid option: expected one of ..."}  [HTTP 422]
```

Mesaj artık hangi sahnenin hangi alanının yanlış olduğunu ve izin verilen değerleri söylüyor (`CLAUDE.md` §16: "stack trace yerine eyleme dönük mesaj").

---

## 8. Testler

* `RemotionRendererTests` (10, sahte `ScriptedRunner` ile, Node/Chrome gerekmiyor): `node` + CLI yolu + `ArgumentList` + props **dosyası** ve içeriği; staging dosyalarının render sırasında var olup sonra silinmesi, `final.rendering.mp4` → `final.mp4`; asset senkronizasyonunun render'dan önce çalışması; sıfır olmayan çıkışta stderr'in son anlamlı satırı + yarım dosya kalmaması + staging temizliği; gerçek Remotion hata çıktısından doğru mesajın çıkarılması; başarı + dosya yok; timeout → `Remotion:TimeoutSeconds` ipucu; Node yok → Unavailable ve render hiç başlamıyor; `node_modules` yok → `npm install` ipucu; göreli veya `.mp4` olmayan çıktı yolu reddi.
* `RenderServiceTests` (7): mutlu yol (job klasöründeki yollar, `Completed`, `FinalVideoPath`); tamamlanmış işin yeniden render'ı; narration yok → 409 eksik dosyanın adıyla; `Planning`'deki iş → 409; renderer kurulu değil → job dokunulmadan `Narrating`; render hatası → `Failed` + sebep; **iptal** → job `Rendering`'de takılı kalmıyor.
* `VideoJobTests` (+1): `Completed` → `Rendering` → `Completed`, önceki video yolu korunuyor.
* Renderer `videoPlan.test.ts` (3, bkz. bölüm 7).

---

## Doğrulanan davranış

```
$ dotnet test
Başarılı! - Başarısız: 0, Başarılı: 48 - ShortsGenerator.Domain.Tests.dll
Başarılı! - Başarısız: 0, Başarılı: 51 - ShortsGenerator.Application.Tests.dll
Başarılı! - Başarısız: 0, Başarılı: 52 - ShortsGenerator.Infrastructure.Tests.dll
Başarılı! - Başarısız: 0, Başarılı:  1 - ShortsGenerator.Api.Tests.dll
(toplam 152; Day 12 sonunda 134)

$ cd renderer; npm test   ->  Tests 26 passed (26)
$ npm run lint            ->  temiz
```

Gerçek API (Day 11/12'deki job, Ollama kullanılmadı):
```
GET  /api/renderer/health  -> {"isReady":true,"engine":"remotion","problems":[],"setupHint":null}
POST /api/videos/64fe09b8-.../render  -> 15 saniye sonra [HTTP 200]
  {"status":"Completed", ...,
   "finalVideoPath":"C:\\...\\YoutubeShortsGenerator\\output\\64fe09b8-...\\final.mp4"}

output/64fe09b8-.../
  final.mp4      1,850,195
  narration.wav    811,436
  plan.json          3,260
renderer/public/jobs/64fe09b8-... kaldı mı: False   (staging temizlendi)

getVideoMetadata(final.mp4) -> {"w":1080,"h":1920,"fps":30,"dur":16.917,"v":"h264","a":"aac"}
```

Hata yolu (bozuk animasyon id'li plan): `422` + bölüm 7'deki okunur mesaj + job `Failed`. Test için açılan iki job'ın çıktı klasörleri sonradan silindi.

Bu çıktılar şunu kanıtlıyor: API → servis → renderer process'i → MP4 zinciri uçtan uca çalışıyor (roadmap kabul kriteri). Process argümanları shell'siz ve dosya tabanlı, çıktı atomik, staging her durumda temizleniyor. Hata, kullanıcıya nedenini söyleyen tek bir cümle olarak ulaşıyor.
