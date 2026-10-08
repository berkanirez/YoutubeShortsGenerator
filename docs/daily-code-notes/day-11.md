# Day 11 — Kod Notları

30 günlük roadmap, Gün 11. Konu: narration metninden bilgisayarda, ücretsiz ve çevrimdışı bir WAV dosyası üretmek. Kokoro TTS izole bir Python ortamında çalışıyor, .NET onu `ITtsProvider` arayüzünün arkasından **child process** olarak çağırıyor.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. İzole Python ortamı ve Kokoro kurulumu

```powershell
py -3.11 -m venv tools/tts/.venv
tools/tts/.venv/Scripts/python.exe -m pip install kokoro-onnx soundfile
# model dosyaları (Apache-2.0):
curl -L -o tools/tts/models/kokoro-v1.0.onnx  .../model-files-v1.0/kokoro-v1.0.onnx   # ~325 MB
curl -L -o tools/tts/models/voices-v1.0.bin   .../model-files-v1.0/voices-v1.0.bin    # ~28 MB
```

**Neden:**
* **Kokoro-82M**: küçük (82M parametre), CPU'da çalışabilen, Apache-2.0 lisanslı ve kalitesi iyi bir İngilizce TTS modeli. Ücretli API yok, internet sadece kurulumda gerekiyor.
* **`kokoro-onnx`, `kokoro` değil**: resmi `kokoro` paketi PyTorch istiyor (~2 GB indirme). `kokoro-onnx` aynı modelin ONNX Runtime ile çalışan sürümü: çok daha hafif ve sadece CPU yeterli.
* **venv (sanal ortam)**: paketler global Python'a değil `tools/tts/.venv` içine kuruluyor. Makinedeki başka Python projeleriyle sürüm çakışması olmuyor. `.venv` klasörünü silmek her şeyi geri alıyor. NuGet paketlerinin projeye özel olmasının Python'daki karşılığı.
* **Python 3.11, 3.14 değil**: makinede ikisi de var. ONNX Runtime gibi native (C++) paketler genelde en yeni Python sürümünün gerisinden geliyor. 3.11 bilinen ve güvenli seçim.

**Nasıl çalışıyor:** `py -3.11` Windows'un Python launcher'ı ile belirli bir sürüm seçiyor. Kurulan paket sürümleri `pip freeze` ile `tools/tts/requirements.txt` dosyasına sabitlendi (lock dosyası mantığı). Model ağırlıkları `.gitignore`'a eklendi (`tools/tts/models/`), çünkü 350 MB'lık binary dosyaların repoda yeri yok.

---

## 2. `tools/tts/kokoro_tts.py` — .NET ile Python arasındaki sözleşme

```python
EXIT_BAD_INPUT = 2
EXIT_MODEL_MISSING = 3
EXIT_SYNTHESIS_FAILED = 4

def main() -> None:
    args = parse_args()        # --output --model --voices --voice --speed --lang
    text = sys.stdin.buffer.read().decode("utf-8-sig").strip()
    if not text: fail(EXIT_BAD_INPUT, "No narration text was provided on stdin.")
    ...
    for label, path in (("Model", args.model), ("Voices", args.voices)):
        if not os.path.isfile(path):
            fail(EXIT_MODEL_MISSING, f"{label} file not found at '{path}'. Run tools/tts/setup.ps1 first.")

    import soundfile as sf
    from kokoro_onnx import Kokoro
    try:
        kokoro = Kokoro(args.model, args.voices)
        if args.voice not in kokoro.get_voices(): fail(EXIT_BAD_INPUT, f"Unknown Kokoro voice '{args.voice}'.")
        samples, sample_rate = kokoro.create(text, voice=args.voice, speed=args.speed, lang=args.lang)
    except SystemExit: raise
    except Exception as error:
        fail(EXIT_SYNTHESIS_FAILED, f"Kokoro synthesis failed: {type(error).__name__}: {error}")

    temp_path = args.output + ".tmp.wav"
    sf.write(temp_path, samples, sample_rate, subtype="PCM_16")
    os.replace(temp_path, args.output)
    print(json.dumps({"durationSeconds": round(len(samples) / sample_rate, 3),
                      "sampleRate": sample_rate, "voice": args.voice}))
```

**Neden:** İki farklı dilde yazılmış iki program konuşacak. Aralarındaki "API" çok net olmalı:
* **Girdi**: metin **stdin**'den geliyor, komut satırı argümanından değil. Narration'da tırnak, noktalı virgül, satır sonu olabilir. Bunları argüman olarak geçmek kaçış (escape) hatalarına ve en kötü durumda komut enjeksiyonuna kapı açar. stdin'den gelen veri hiçbir zaman komut olarak yorumlanmaz.
* **Çıktı**: başarıda stdout'a **tek satır JSON**. Ses süresi oradan okunuyor (Day 12'de zamanlama için kullanılacak).
* **Hata**: stderr'e tek satır insan-okunur sebep, ve **çıkış koduyla** hata türü: 2 = istek hatalı, 3 = model kurulu değil, 4 = sentez çöktü. .NET tarafı bunları farklı HTTP kodlarına çeviriyor.
* **Atomik yazma**: önce `.tmp.wav` dosyasına yazılıyor, başarılı olunca `os.replace` ile asıl isme taşınıyor. İşlem yarıda çökerse geride yarım bir `narration.wav` kalmıyor.

**Nasıl çalışıyor:**
* `len(samples) / sample_rate`: Kokoro bir sayı dizisi (ses örnekleri) döndürüyor. 24000 Hz örnekleme hızında 24000 örnek = 1 saniye. Süre, ayrı bir araca (ffprobe vb.) gerek kalmadan buradan hesaplanıyor.
* Ağır `import`'lar (onnxruntime) argüman kontrollerinden **sonra** yapılıyor: "model dosyası yok" gibi hatalar 1 saniyelik kütüphane yükleme maliyeti olmadan anında raporlanıyor.
* `except SystemExit: raise`: `fail()` içindeki `sys.exit` bir exception fırlatır. Genel `except Exception` onu yutmasın diye önce yakalanıp tekrar fırlatılıyor.

---

## 3. Hata/düzeltme: boş metin "boş" sayılmıyordu

Test: `"" | python kokoro_tts.py --model a ...` → beklenen `No narration text` (çıkış 2), gelen `Model file not found at 'a'` (çıkış 3).

**Düzeltme:** `decode("utf-8")` → `decode("utf-8-sig")`.

**Neden:** Windows PowerShell, native programa pipe ederken metnin başına UTF-8 **BOM** (`\ufeff`, görünmez "byte order mark") ekliyor. `str.strip()` BOM'u boşluk saymadığı için "boş" metin tek karakterlik bir metin gibi görünüyordu.

**Nasıl çalışıyor:** `utf-8-sig` codec'i baştaki BOM varsa atıyor, yoksa normal UTF-8 gibi davranıyor. Aynı sorunun .NET tarafındaki karşılığı bölüm 7'de: oradan da BOM gönderilmiyor.

---

## 4. `tools/tts/setup.ps1` — tekrarlanabilir kurulum

```powershell
if (-not (Test-Path $python)) {
    $launcher = Get-Command py -ErrorAction SilentlyContinue
    if ($launcher) { & py -3.11 -m venv $venv } else { & python -m venv $venv }
}
& $python -m pip install --disable-pip-version-check -r (Join-Path $here "requirements.txt")
foreach ($file in "kokoro-v1.0.onnx", "voices-v1.0.bin") {
    if (-not (Test-Path $target)) {
        curl.exe -sSL --fail -o "$target.part" "$base/$file"
        Move-Item -Force "$target.part" $target
    }
}
```

**Neden:** Kurulum adımları sadece bu sohbette yaşarsa, yeni bir bilgisayarda veya `.venv` silindiğinde kimse hatırlamaz. Script **idempotent**: tekrar çalıştırmak zarar vermiyor, var olanı yeniden kullanıyor (ikinci çalıştırmada "Requirement already satisfied" ile bitti).

**Nasıl çalışıyor:** `--fail` sayesinde curl, HTTP hatasında 404 sayfasını model dosyası diye kaydetmek yerine hata veriyor. `.part` dosyasına indirip sonra yeniden adlandırmak, yarıda kesilen bir indirmenin "tamamlanmış" sanılmasını önlüyor (Python script'indeki atomik yazmanın aynısı).

---

## 5. Domain: `ITtsProvider`, `TtsRequest/TtsResult/TtsAvailability`, istisnalar

```csharp
public interface ITtsProvider
{
    Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken cancellationToken);
    Task<TtsAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);
}

public sealed record TtsRequest(string Text, string OutputPath, string? Voice = null);
public sealed record TtsResult(string AudioPath, double DurationSeconds, int SampleRate, string Voice);
public sealed record TtsAvailability(bool IsReady, string Engine, IReadOnlyList<string> Problems, string? SetupHint = null);

public sealed class TtsUnavailableException : Exception { ... }   // motor kurulu değil
public sealed class TtsGenerationException  : Exception { ... }   // motor var ama bu metinde başarısız oldu
```

**Neden:** Day 3'teki `ILlmProvider` ile aynı desen. Uygulamanın geri kalanı "Kokoro", "Python", "ONNX" kelimelerini hiç bilmiyor, sadece "metni sese çevir" diyor. İleride premium bir TTS'e geçmek, bu arayüzü uygulayan yeni bir sınıf yazmak demek (`CLAUDE.md` §9, §21).

İki ayrı exception, çünkü iki **farklı problem** var ve kullanıcının yapacağı şey farklı:
* `TtsUnavailableException` → makine kurulumu eksik → bir kez `setup.ps1` çalıştır (HTTP 503).
* `TtsGenerationException` → bu istek başarısız oldu → metne/loglara bak (HTTP 422).

---

## 6. `IJobOutputLocator` + `ContentRootPath` — dosyalar nereye yazılıyor?

```csharp
// Domain
public interface IJobOutputLocator { string GetJobDirectory(Guid jobId); }

// Infrastructure
internal static class ContentRootPath
{
    public static string Resolve(IHostEnvironment environment, string path) =>
        Path.GetFullPath(path, environment.ContentRootPath);
}

services.AddOptions<OutputStorageOptions>()
    .Bind(configuration.GetSection(OutputStorageOptions.SectionName))
    .PostConfigure<IHostEnvironment>((options, environment) =>
        options.RootDirectory = ContentRootPath.Resolve(environment, options.RootDirectory));
```

**Neden (bugün bulunan bir hata):** `appsettings.json`'daki göreli yollar (`"output"`, `"../../assets/..."`) process'in **o anki çalışma dizinine** göre çözülüyordu. README API'yi repo kökünden `dotnet run --project src/ShortsGenerator.Api` ile başlatmayı söylüyor. O durumda `../../assets` repo'nun iki üst klasörünü gösteriyor ve uygulama açılmıyordu. Aynı konfigürasyon, komutun hangi klasörde yazıldığına göre farklı yerleri gösteriyordu. TTS de `tools/tts/...` yollarına ihtiyaç duyduğu için bu bugün çözülmesi gereken bir problemdi.

**Nasıl çalışıyor:**
* **Content root**: ASP.NET Core'un "uygulamanın kendi klasörü" kavramı (`src/ShortsGenerator.Api`), nereden başlatıldığından bağımsız.
* `Path.GetFullPath(path, basePath)`: göreli yolu verilen tabana göre mutlak yola çevirir. Yol zaten mutlaksa (testlerdeki geçici dosyalar gibi) dokunmaz.
* `PostConfigure<IHostEnvironment>`: Options pattern'in "bind edildikten **sonra** son bir düzeltme yap" kancası. İkinci tip parametresi DI'dan otomatik olarak enjekte edilir. Böylece `JobOutputLocator`, `JsonAssetRepository` ve `KokoroTtsProvider` her zaman mutlak yollar görüyor.
* `OutputStorage:RootDirectory` artık `"../../output"`: `CLAUDE.md`'nin hedeflediği repo kökündeki `output/<job-id>/` klasörü.
* `IJobOutputLocator`: `plan.json`, `narration.wav` ve ileride `final.mp4` aynı klasöre gitsin diye "job klasörü nerede?" sorusunun **tek** cevabı. `FileVideoPlanStore` da artık bunu kullanıyor (kendi `Path.Combine`'ını bıraktı, diff aşağıda).
* Infrastructure projesine `Microsoft.Extensions.Hosting.Abstractions` ve `Microsoft.Extensions.Logging.Abstractions` (10.0.12) paketleri eklendi.

```diff
-    private readonly string _rootDirectory;
-    public FileVideoPlanStore(IOptions<OutputStorageOptions> options)
-    {
-        _rootDirectory = options.Value.RootDirectory;
-    }
+    private readonly IJobOutputLocator _locator;
+    public FileVideoPlanStore(IJobOutputLocator locator)
+    {
+        _locator = locator;
+    }
```

Ayrıca `JsonAssetRepository`'nin hata mesajındaki yanlış anahtar adı düzeltildi: `OutputStorage:ManifestPath` → `AssetManifest:ManifestPath`.

---

## 7. `Processes/IProcessRunner` + `ProcessRunner` — güvenli child process

```csharp
public sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments,
                                 string? StandardInput, TimeSpan Timeout, string? WorkingDirectory = null);
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Elapsed);

var startInfo = new ProcessStartInfo
{
    FileName = spec.FileName,
    UseShellExecute = false,
    RedirectStandardInput = spec.StandardInput is not null,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    ...
};
if (spec.StandardInput is not null) startInfo.StandardInputEncoding = Utf8NoBom;
foreach (var argument in spec.Arguments) startInfo.ArgumentList.Add(argument);

process.Start();   // Win32Exception -> ProcessLaunchException
var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

using var timeoutCts = new CancellationTokenSource(spec.Timeout);
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
try
{
    await process.StandardInput.WriteAsync(...); process.StandardInput.Close();
    await process.WaitForExitAsync(linkedCts.Token);
}
catch (OperationCanceledException or IOException)
{
    ... KillTree(process); cancellationToken.ThrowIfCancellationRequested(); throw new TimeoutException(...);
}
```

**Neden:** Bu, roadmap'in bugün öğretmek istediği asıl konu: **process entegrasyonu**. Day 13'te Remotion (Node.js) de aynı şekilde çağrılacak, o yüzden Kokoro'ya özel değil, genel bir sınıf olarak yazıldı.

**Nasıl çalışıyor:**
* **`UseShellExecute = false` + `ArgumentList`**: shell (cmd.exe) hiç devreye girmiyor ve her argüman ayrı ayrı geçiliyor. Tek bir string'i birleştirip vermek (`"--voice " + voice`) yerine bu yöntemle, bir değerin içindeki boşluk/tırnak/`&` asla yeni bir argüman ya da komut olamıyor (`CLAUDE.md` §18).
* **stdout ve stderr aynı anda okunuyor**: işletim sisteminin pipe tamponları sınırlı (birkaç KB). Sadece stdout'u okurken çocuk process stderr'e çok yazarsa tampon dolar. Çocuk yazmak için bizi, biz de çıkması için çocuğu bekleriz: **deadlock**. İki okuma başta başlatılıyor.
* **BOM'suz UTF-8 stdin**: bölüm 3'teki problemin .NET tarafı. `StandardInputEncoding` verilmezse .NET BOM ekleyebiliyor.
* **Timeout + iptal**: `CreateLinkedTokenSource` iki iptal kaynağını birleştiriyor: HTTP isteğinin iptali **veya** zaman aşımı. Hangisi olursa olsun process **ağacıyla birlikte** öldürülüyor (`Kill(entireProcessTree: true)`; Python/Node kendi alt process'lerini açabiliyor). Sonra hangisinin tetiklendiğine bakılıp `OperationCanceledException` veya `TimeoutException` fırlatılıyor.
* **Sıfır olmayan çıkış kodu exception değil**: runner sadece "process'i çalıştır, sonucu getir" diyor. Çıkış kodu 3'ün ne anlama geldiğini bilen, adapter (`KokoroTtsProvider`).
* `IProcessRunner` arayüzü, adapter testlerinin Python kurulu olmadan çalışabilmesi için var (bölüm 11).

---

## 8. `Tts/KokoroTtsOptions` + `KokoroTtsProvider` — adapter

```csharp
public sealed class KokoroTtsOptions
{
    public string PythonPath { get; set; } = "../../tools/tts/.venv/Scripts/python.exe";
    public string ScriptPath { get; set; } = "../../tools/tts/kokoro_tts.py";
    public string ModelPath  { get; set; } = "../../tools/tts/models/kokoro-v1.0.onnx";
    public string VoicesPath { get; set; } = "../../tools/tts/models/voices-v1.0.bin";
    public string Voice { get; set; } = "af_heart";
    public double Speed { get; set; } = 1.0;
    public string Language { get; set; } = "en-us";
    public int TimeoutSeconds { get; set; } = 300;
}
```

```csharp
public async Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken ct)
{
    // 1. girdi doğrulama: boş metin, mutlak .wav yolu, ses id'si ^[a-z]{1,4}_[a-z]{1,32}$
    // 2. CheckAvailabilityAsync -> eksik dosya varsa TtsUnavailableException (+ setup ipucu)
    // 3. process: python kokoro_tts.py --output .. --model .. --voices .. --voice .. --speed 1.0 --lang en-us  (metin stdin'den)
    // 4. çıkış kodu: 0 ok | 3 -> Unavailable | 2 -> Generation("rejected") | diğer -> Generation + log
    // 5. stdout'un SON satırını JSON olarak parse et -> TtsResult
}
```

**Neden / Nasıl çalışıyor:**
* **Adapter deseni**: uygulamanın istediği şekil (`ITtsProvider`) ile aracın gerçek şekli (Python CLI + çıkış kodları) arasında çeviri yapıyor. Day 3'teki Ollama adapter'ının HTTP yerine process kullanan versiyonu.
* **Ses id'si regex'i**: `ArgumentList` zaten enjeksiyonu engelliyor. Regex "derinlemesine savunma" (defense in depth): ses id'si her zaman `af_heart` gibi küçük harf + alt çizgi. Başka bir şey gelirse process hiç başlatılmıyor (testlerde `"af_heart --model evil.onnx"`, `"../../secrets"`).
* **`Speed.ToString(CultureInfo.InvariantCulture)`**: bu makine Türkçe locale'de. `1.1.ToString()` burada `"1,1"` üretir ve Python `float("1,1")`'i parse edemez. Bunun için ayrı bir test var (tr-TR kültürüyle).
* **Son satır kuralı**: ONNX Runtime gibi kütüphaneler stdout'a uyarı basabiliyor. Sözleşme "son boş olmayan satır JSON'dur".
* `af_heart`: Kokoro'nun en yüksek puanlı İngilizce sesi. Gerçek bir kişinin klonu değil (`CLAUDE.md` §9). `appsettings.json` → `KokoroTts:Voice` ile değiştirilebilir, istek bazında da `TtsRequest.Voice` ile geçersiz kılınabilir.
* `TtsServiceCollectionExtensions` içinde `TryAddSingleton<IProcessRunner, ProcessRunner>`: Day 13'te renderer da aynı runner'ı kaydedecek. `TryAdd` ikinci kaydın birinciyi ezmemesini sağlıyor.

---

## 9. Application: `NarrationService`

```csharp
public async Task<TtsResult> GenerateNarrationAsync(Guid jobId, CancellationToken ct)
{
    var job = ... ?? throw new KeyNotFoundException(...);
    if (job.Status is not (VideoJobStatus.Planning or VideoJobStatus.Narrating)) throw new InvalidOperationException(...);
    var plan = await _planStore.LoadAsync(jobId, ct) ?? throw new InvalidOperationException("... no scene plan yet ...");

    var availability = await _ttsProvider.CheckAvailabilityAsync(ct);
    if (!availability.IsReady) throw new TtsUnavailableException($"... {problems}. {availability.SetupHint}");

    if (job.Status == VideoJobStatus.Planning) { job.MarkNarrating(now); await _repository.UpdateAsync(job, ct); }

    var outputPath = Path.Combine(_outputLocator.GetJobDirectory(jobId), "narration.wav");
    try { return await _ttsProvider.SynthesizeAsync(new TtsRequest(plan.NarrationText, outputPath), ct); }
    catch (Exception ex) when (ex is TtsUnavailableException or TtsGenerationException)
    { job.MarkFailed(ex.Message, now); await _repository.UpdateAsync(job, ct); throw; }
}
```

**Neden:**
* **`plan.NarrationText`, `job.GeneratedScript` değil**: render edilecek olan plan. `VideoPlanValidator` (Day 5) sahne narration'larının birleşiminin tam olarak bu metin olduğunu zaten garanti ediyor.
* **Kurulum kontrolü job'a dokunmadan önce**: "Kokoro kurulu değil" bu job'ın suçu değil, makinenin. Kontrol önce yapılmazsa her deneme bir job'ı kalıcı olarak `Failed` yapardı. Şimdi kullanıcı `setup.ps1`'i çalıştırıp aynı job'la devam edebiliyor (API'de doğrulandı, aşağıda).
* **`Narrating` → tekrar üretim serbest**: ses ayarı değiştirildiğinde bütün job'ı baştan başlatmadan narration yeniden üretilebiliyor.
* Day 2'den beri "Day 11 için ayrılmış" olarak bekleyen `MarkNarrating` ilk kez gerçek bir iş için kullanılıyor.

---

## 10. Önceden kalan bir eksik: `sourceScript` hiç kullanılmıyordu

```diff
-            var narration = await GenerateValidatedNarrationAsync(job.Topic, cancellationToken);
+            var narration = job.SourceScript is not null
+                ? UseSourceScript(job.SourceScript)
+                : await GenerateValidatedNarrationAsync(job.Topic, cancellationToken);

+    private static string UseSourceScript(string sourceScript)
+    {
+        var cleaned = ScriptTextCleaner.Clean(sourceScript);
+        var validation = ScriptValidator.Validate(cleaned);
+        if (!validation.IsValid)
+            throw new ScriptGenerationException($"The supplied script can't be used: {string.Join("; ", validation.Reasons)}");
+        return cleaned;
+    }
```

**Neden:** Day 1'den beri `POST /api/videos` bir `sourceScript` alıyor ve veritabanına kaydediyordu, ama Day 4'teki script servisi onu **görmezden gelip** her zaman LLM'den yeni metin üretiyordu. Ürün tanımı "bir konu **veya** kısa bir script ver" diyor. Bugün LLM'siz bir uçtan uca test yapmaya çalışırken fark edildi.

**Nasıl çalışıyor:** Kullanıcının script'i LLM'e "iyileştirilsin" diye gönderilmiyor, kelimeleri kullanıcı seçti. Ama üretilen script'lerle **aynı** güvenlik/uzunluk kurallarından geçiyor: kullanıcının yazdığı metin de tıbbi iddia kontrolünden muaf değil. Geçersizse job `Failed` oluyor ve sebep söyleniyor. İki yeni test: geçerli script'te LLM hiç çağrılmıyor; geçersiz script'te LLM'e "düzelt" diye gidilmiyor.

---

## 11. API: `POST /api/videos/{id}/narration`, `GET /api/tts/health`

```csharp
app.MapPost("/api/videos/{id:guid}/narration", async (Guid id, NarrationService service, CancellationToken ct) =>
{
    try { return Results.Ok(await service.GenerateNarrationAsync(id, ct)); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }          // 409
    catch (TtsUnavailableException ex) { return Results.Json(new { error = ex.Message }, statusCode: 503); }
    catch (TtsGenerationException ex) { return Results.UnprocessableEntity(new { error = ex.Message }); }  // 422
});
```

**Neden:** Day 3-5 endpoint'leriyle aynı HTTP kod eşlemesi. 409 "sıra yanlış" (önce plan), 503 "bağımlı servis/kurulum yok", 422 "istek işlendi ama bu içerikle başarısız". `GET /api/tts/health`, `GET /api/llm/health`'in TTS karşılığı.

---

## 12. Testler

* `KokoroTtsProviderTests` (15, `FakeProcessRunner` ile, Python gerekmiyor): argümanların tam listesi ve metnin argümanlarda **değil** stdin'de olması; Türkçe kültürde `--speed 1.1`; kütüphane gürültüsünden sonraki JSON satırı; istek sesinin varsayılanı ezmesi; geçersiz ses id'lerinin process başlamadan reddi (3 vaka); göreli çıktı yolunun reddi; eksik model → Unavailable + `setup.ps1` ipucu + process başlatılmaz; çıkış 3 → Unavailable; çıkış 4 → Generation + son stderr satırı; bozuk stdout → Generation; Python başlatılamaz → Unavailable; timeout → Generation + `TimeoutSeconds` ipucu; eksik parçaların hepsinin listelenmesi.
* `ProcessRunnerTests` (6, **gerçek** process'ler): çıkış kodu + stdout (`dotnet --version`); sıfır olmayan çıkışın exception değil sonuç olarak dönmesi; olmayan program → `ProcessLaunchException`; stdin'in BOM'suz UTF-8 olarak ulaşması (`findstr "^"`, tırnaklı metinle); timeout'ta process'in öldürülmesi (30 saniyelik `ping` 0.5 saniyede kesiliyor); iptalde process'in öldürülmesi.
* `NarrationServiceTests` (6): mutlu yol (plan metni, job klasörü, `Narrating`); tekrar üretim; plan yok → 409 sebebi; bilinmeyen job; motor yok → ipuçlu mesaj + job **dokunulmadan** `Planning`; sentez hatası → `Failed` + sebep.
* `ScriptGenerationServiceTests` (+2): kaynak script ile LLM'siz yol; geçersiz kaynak script.

---

## Doğrulanan davranış

```
$ dotnet test
Başarılı! - Başarısız: 0, Başarılı: 38 - ShortsGenerator.Domain.Tests.dll
Başarılı! - Başarısız: 0, Başarılı: 43 - ShortsGenerator.Application.Tests.dll
Başarılı! - Başarısız: 0, Başarılı: 34 - ShortsGenerator.Infrastructure.Tests.dll
Başarılı! - Başarısız: 0, Başarılı:  1 - ShortsGenerator.Api.Tests.dll
(toplam 116; Day 10 sonunda 87)
```

Python script'i doğrudan:
```
{"durationSeconds": 5.781, "sampleRate": 24000, "voice": "af_heart"}   exit=0  (17.4s, model yükleme dahil)
"" | ...                      -> No narration text was provided on stdin.          exit=2
--model missing.onnx          -> Model file not found at 'missing.onnx'. Run tools/tts/setup.ps1 first.   exit=3
--voice zz_nobody             -> Unknown Kokoro voice 'zz_nobody'.                 exit=2
```

Gerçek API (repo **kökünden** `dotnet run --project src/ShortsGenerator.Api`; yol düzeltmesinin kanıtı), Ollama kullanılmadan:
```
GET  /api/tts/health                -> {"isReady":true,"engine":"kokoro","problems":[]}
POST /api/videos  (topic + sourceScript = örnek planın narration'ı)
POST /api/videos/{id}/script        -> status: Planning   (kaynak script kullanıldı, LLM çağrılmadı)
(samples/sample-plan.json -> output/{id}/plan.json   — Ollama gerektiren /plan adımının yerine)
POST /api/videos/{id}/narration     -> 20s sonra:
  {"audioPath":"C:\\...\\YoutubeShortsGenerator\\output\\64fe09b8-...\\narration.wav",
   "durationSeconds":16.725,"sampleRate":24000,"voice":"af_heart"}
GET  /api/videos/{id}               -> status: Narrating
narration.wav: 802,860 bayt
```

Eksik motor (model yolu ortam değişkeniyle `KokoroTts__ModelPath=.../not-there.onnx` yapılarak):
```
GET  /api/tts/health            -> 503 {"isReady":false,...,"problems":["Kokoro model not found at '...not-there.onnx'"]}
POST /api/videos/{id}/narration -> 503 {"error":"Local TTS (kokoro) is not ready: Kokoro model not found at '...'. Run: powershell -ExecutionPolicy Bypass -File tools/tts/setup.ps1"}
GET  /api/videos/{id}           -> status: Narrating   (job bozulmadı)
```

Bu çıktılar şunu kanıtlıyor: narration metni ücretli hiçbir servis olmadan yerel bir ses dosyasına dönüşüyor (roadmap kabul kriteri). Ses ayarlanabilir, eksik motor durumunda kullanıcı ne yapacağını söyleyen bir mesaj alıyor ve job kaybolmuyor.

Dikkat çekici bulgu: gerçek ses **16.7 saniye**, örnek plan ise sahnelere toplam **30 saniye** veriyor. Görüntü ile ses arasındaki bu fark tam olarak Day 12'nin (ses süresine göre zamanlama) çözeceği problem.
