# Day 24 — Kod Notları

30 günlük roadmap, Gün 24. Konu: backend testleri. Hedef "daha çok test" değil, roadmap'teki 8 risk alanının (iş geçişleri, şema, bozuk Ollama cevabı, eksik asset, TTS/process hatası, renderer hatası, yol güvenliği, API doğrulaması) her birinin gerçekten korunduğunu göstermek. Bu yüzden önce mevcut 283 test risklere göre haritalandı (`docs/TESTING.md`), sonra boşluklar dolduruldu. Boşlukları doldururken **6 gerçek hata** bulunup düzeltildi.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 0. Yöntem: önce harita, sonra boşluk, sonra kırmızı

1. Her test dosyasındaki test sayısını ve adını listele, adları risk alanlarına eşle.
2. Hiçbir testin dokunmadığı yolları bul (ör. `FileVideoPlanStore` için **hiç test yoktu**).
3. Boşluk için önce testi yaz, **kırmızı** olduğunu gör, sonra kodu düzelt.
4. Testi kodu düzelttikten sonra yazdıysan, düzeltmeyi geçici olarak geri al ve testin kırmızıya döndüğünü gör (elle yapılmış bir "mutation testing").

**Neden:** Hiç kırmızı görülmemiş bir test, yanlış şeyi kontrol ediyor olabilir: her zaman geçen bir test hiçbir şeyi korumaz.

---

## 1. Durum makinesinin tamamı tek tabloda (`VideoJobTests`)

```csharp
private static readonly HashSet<(VideoJobStatus From, Transition To)> Allowed =
[
    (Created, Planning), (Planning, Narrating), (Narrating, Rendering), (Rendering, Completed),
    (Completed, Rendering),   // re-render (Day 13)
    (Completed, Narrating),   // narration edit (Day 23)
    (Created, Failed), (Planning, Failed), (Narrating, Failed), (Rendering, Failed),
];

[Theory, MemberData(nameof(EveryPair))]   // 6 durum × 5 geçiş = 30 vaka
public void StateMachine_AllowsExactlyTheDocumentedTransitions(VideoJobStatus from, Transition to)
{
    var job = JobIn(from);
    if (Allowed.Contains((from, to))) { act(); Assert.Equal(target, job.Status); }
    else { Assert.Throws<InvalidOperationException>(act); Assert.Equal(statusBefore, job.Status); }
}
```

**Neden:** Eski testler bir kişinin aklına gelen geçişleri deniyordu (15 test). Tablo ise **her** (durum, geçiş) çiftini deniyor. Day 13 ve Day 23'te iki yeni geçiş bilinçli olarak açılmıştı. Bundan sonra biri yanlışlıkla yeni bir kenar açarsa (ör. `Failed → Rendering`), tablo değiştirilmeden test geçmez. Reddedilen bir geçişin durumu **değiştirmediği** de kontrol ediliyor.

**Nasıl çalışıyor:** `MemberData`, test verisini bir metottan alıyor; `Enum.GetValues<T>()` ile iç içe iki döngü bütün kombinasyonları üretiyor. `JobIn(status)`, işi o duruma gerçek geçişlerle getiriyor (DB'ye ya da reflection'a dokunmadan). `switch` ifadesindeki `_ => throw ...` kolu, ileride yeni bir durum eklenirse testin "bu durumu buraya ekle" diye bağırmasını sağlıyor.

Sonuç: 30 vakanın hepsi ilk seferde geçti. Kod, belgelenen kurallarla birebir aynı.

---

## 2. Bulunan hata 1: Ollama HTTP 500 / bozuk gövde / zaman aşımı (`OllamaLlmProvider`)

İlk kırmızı test:
```
System.Net.Http.HttpRequestException : Response status code does not indicate success: 500 (Internal Server Error).
```

```csharp
catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
{
    throw new LlmUnavailableException($"Ollama did not answer within {_httpClient.Timeout.TotalSeconds:0} seconds. ...", ex);
}

if (!response.IsSuccessStatusCode)
    throw new LlmUnavailableException($"Ollama returned HTTP {(int)response.StatusCode}: {ErrorText(errorBody)}");

try { payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(...); }
catch (Exception ex) when (ex is JsonException or NotSupportedException)
{
    throw new LlmUnavailableException("Ollama ... sent an unexpected response that isn't its JSON format.", ex);
}
```

**Neden (kök sebep):**
* `EnsureSuccessStatusCode()` Ollama'nın kendi açıklamasını (`{"error":"model requires more system memory ..."}`) çöpe atıyordu. Kullanıcı yalnızca "500" görüyordu; asıl sorun (RAM yetmiyor, daha küçük model gerekiyor) gizli kalıyordu.
* `HttpClient.Timeout`, .NET'te `TaskCanceledException` olarak çıkıyor, yani "iptal" gibi görünüyor. Pipeline bunu kapatma sinyali sanmıyordu ama "Unexpected error" diye kaydediyordu.
* HTML dönen bir proxy `NotSupportedException` (yanlış content-type) ya da `JsonException` fırlatıyordu. İkisini de servisler "LLM hatası" olarak tanımıyordu.

**Nasıl çalışıyor:** Hepsi artık `LlmUnavailableException`. Bu tip, script/plan servislerinin zaten yakaladığı ve kullanıcıya gösterdiği tip (Day 3). `when (!cancellationToken.IsCancellationRequested)` exception filtresi iki durumu ayırıyor: kullanıcı ya da uygulama iptal ettiyse `OperationCanceledException` olduğu gibi yükseliyor (test: `GenerateAsync_CallerCancels_StillThrowsOperationCanceled`), kimse iptal etmediyse bu bir zaman aşımı. `using (response)` ise HTTP cevabını dispose ediyor; eskiden edilmiyordu.

---

## 3. Bulunan hata 2 ve 3: `plan.json` kaydetme ve okuma (`FileVideoPlanStore`)

Bu sınıfın **hiç testi yoktu**. Yeni `FileVideoPlanStoreTests` (8 test) üç kırmızı verdi:
```
System.Text.Json.JsonException : Expected end of string, but instead reached end of data. Path: $.title
Assert.Throws() Failure: No exception was thrown          <- dosya içeriği "null"
```

```csharp
// Kaydetme: önce .tmp'ye yaz, sonra üzerine taşı
await using (var stream = File.Create(tempPath)) { await JsonSerializer.SerializeAsync(stream, plan, ...); }
File.Move(tempPath, filePath, overwrite: true);

// Okuma: bozuk dosya -> ne olduğunu ve ne yapılacağını söyleyen hata
catch (JsonException ex) { throw new InvalidDataException(CorruptMessage(jobId, filePath, ex.Message), ex); }
return plan ?? throw new InvalidDataException(CorruptMessage(jobId, filePath, "it contains no plan"));
```

**Neden:**
* **Atomik yazma**: `File.Create` mevcut dosyayı önce **sıfırlıyor**, sonra yazıyor. Arada bir çökme ya da iptal olursa (Day 23'ten beri planlar elle de düzenleniyor), iyi bir plan yarım bir dosyaya dönüşür. Dashboard her 2.5 saniyede planı okuduğu için yarım dosyayı okuyabilirdi de. Aynı dosya sistemi üzerinde `File.Move` atomik: okuyan taraf ya eski dosyayı ya yeni dosyayı görür, yarımını asla görmez.
* **`"null"` sessiz hatası**: `Deserialize` `null` döndürüyordu ve store bunu "plan yok" sayıyordu. Pipeline (`NextStageAsync`) bu durumda planı **sessizce baştan üretirdi** ve elle yapılmış düzenlemeler kaybolurdu. Artık bu bir hata.
* Mesaj, işi, dosyayı ve çözümü söylüyor ("fix or delete plan.json and generate the plan again").

Test edilen diğer şeyler: iş klasörü her zaman çıktı kökünün **doğrudan** altında; göreli bir kök mutlak yola çevriliyor (çalışma dizini dosyaların nereye gideceğini değiştiremiyor); enum'lar dosyada isim olarak yazılıyor (renderer'ın Zod şeması `"MythVsFact"` bekliyor, `4` değil).

---

## 4. Bulunan hata 4: global hata işleyici yoktu (`Api/Endpoints/ErrorHandling.cs`)

```csharp
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var (status, message) = exception switch
    {
        BadHttpRequestException bad => (bad.StatusCode, bad.Message),
        InvalidDataException data   => (500, data.Message),
        _                           => (500, "Unexpected server error. The details are in the API's console log."),
    };
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { error = message });
}));
```

**Neden:** Bir endpoint'in kendisinin yakalamadığı her istisna, Development ortamında ASP.NET'in geliştirici hata sayfasına düşüyordu: HTML ve **stack trace**. CLAUDE.md §16: kullanıcıya ham stack trace değil, anlaşılır bir mesaj gösterilmeli. Düzeltmeyi geçici olarak kapatınca iki test kırmızıya döndü:
```
PostVideos_MalformedJsonBody_Is400NotAServerError [FAIL]   Assert.DoesNotContain() Failure: Sub-string found   <- "   at ..."
GetPlan_CorruptPlanFile_ReturnsAnActionableErrorWithoutAStackTrace [FAIL]   Strings differ (text/html)
```

**Nasıl çalışıyor:** `UseExceptionHandler` pipeline'ın en başına konuyor ve kendisinden sonraki her şeyi saran bir `try/catch` gibi davranıyor. Geliştirici sayfası daha dışarıda kaldığı için istisna ona hiç ulaşmıyor. İstisnanın kendisi middleware tarafından loglanıyor; detay konsolda kalıyor, istemciye gitmiyor. `BadHttpRequestException` ayrıca ele alınıyor, çünkü Development'ta minimal API'ler bozuk bir JSON gövdesi için 400'ü kendileri döndürmek yerine bu istisnayı **fırlatıyor**. Bu kol olmasaydı bozuk bir istek 500 dönerdi. Cevap biçimi diğer bütün hatalarla aynı: `{ "error": "..." }`.

---

## 5. Bulunan hata 5: "secure your grip" tıbbi iddia sayılıyordu (`ScriptValidator`)

```diff
-            if (narration.Contains(phrase, StringComparison.OrdinalIgnoreCase))
+            // Whole words only: a plain Contains("cure") also rejected "secure your grip".
+            if (Regex.IsMatch(narration, $@"\b{Regex.Escape(phrase)}\b", RegexOptions.IgnoreCase))
```

Eski kodla:
```
Validate_WordsMerelyContainingADisallowedPhrase_AreFine(sentence: "Secure your grip before every rep.") [FAIL]
Validate_WordsMerelyContainingADisallowedPhrase_AreFine(sentence: "Poor form obscures real progress.") [FAIL]
Validate_WordsMerelyContainingADisallowedPhrase_AreFine(sentence: "Procure a sturdy bar for your doorway.") [FAIL]
```

**Neden:** "cure" alt dizisi "se**cure**", "obs**cure**s" ve "pro**cure**" kelimelerinin içinde de geçiyor. Bir fitness script'i için "secure your grip" çok olası bir cümle. Bu yüzden LLM'in iyi bir script'i "tıbbi iddia" diye reddedilip boşuna yeniden yazdırılabiliyordu; Day 23'ten beri insanın düzenlemesi de reddediliyordu.

**Nasıl çalışıyor:** `\b` bir **kelime sınırı**: harf/rakam ile harf olmayan karakter arasındaki boşluk. `\bcure\b` tek başına duran "cure" kelimesini yakalıyor, "secure" içindeki "cure"u yakalamıyor ("se" ile "cure" arasında kelime sınırı yok). "cures" listede ayrı bir kelime olarak var, bu yüzden "CURES," de yakalanıyor (virgül bir kelime sınırı). `Regex.Escape`, "treatment for" gibi ifadelerin içindeki özel karakterleri güvenli hale getiriyor. Yeni test, büyük/küçük harf ve noktalama yanında hâlâ yakaladığını da kontrol ediyor (`"Stretching CURES, nothing."`).

---

## 6. Bulunan hata 6: girdi sınırları (`VideoJob`, `VideoJobService`)

```csharp
public const int MaxTopicLength = 200;   // VideoJob.Create: aşarsa ArgumentException

// VideoJobService.CreateJobAsync: kullanıcının kendi script'i iş oluşturulurken doğrulanıyor
if (!string.IsNullOrWhiteSpace(sourceScript) && !ScriptValidator.Validate(sourceScript).IsValid)
    throw new ArgumentException($"Your script can't be used: ...");
```

**Neden:**
* EF'teki `HasMaxLength(300)` SQLite'ta **uygulanmıyor** (SQLite uzunluk kısıtı tanımaz). Topic doğrudan LLM prompt'una girdiği için 10 kB'lık bir "topic" hem prompt'u şişirir hem de modele talimat sokmanın kolay bir yolu olur. Sınır artık domain'de, dashboard'daki `maxlength="200"` ile aynı.
* Kısa bir script önceden **kuyruğa giriyor**, yarım dakika sonra iş `Failed` oluyordu. Artık `POST /api/videos` hemen 400 dönüyor ve nedeni söylüyor. Script generation servisi kendi kontrolünü yine yapıyor (iki katman).
* `ArgumentException.Message`'ın sonuna eklenen `" (Parameter 'sourceScript')"` kullanıcı için gürültü; endpoint bunu siliyor (manuel testte görüldü, ardından test eklendi).

---

## 7. Eksik asset: render'dan önce, düzeltilebilir şekilde (`RenderService.FindPlanProblemsAsync`)

```csharp
public async Task<IReadOnlyList<string>> FindPlanProblemsAsync(Guid jobId, CancellationToken ct)
    => plan.Scenes.SelectMany(scene => scene.AssetIds.Where(id => !known.Contains(id)).Select(id =>
           $"Scene '{scene.Id}' uses asset '{id}', which is not in the asset catalog; pick another one with \"Change\"."));

// RenderAsync: işe dokunmadan önce  -> InvalidOperationException
// POST /render ve POST /apply-edits: kuyruğa almadan önce -> 409 + aynı mesaj
```

**Neden:** Bir plan, katalogdan sonradan çıkarılan (manifest elle düzenlenmiş) bir asset'i kullanabilir. Renderer bunu yakalıyordu ama Chrome açılıp bundle hazırlandıktan sonra, ve iş `Failed` oluyordu. `Failed` bir iş düzenlenemiyor (Day 22), yani kullanıcı tam da sorunu çözecek "Change" düğmesini kullanamıyordu. Artık kontrol milisaniyeler içinde yapılıyor, hangi sahnenin hangi asset'i olduğunu söylüyor, ve iş `Narrating`/`Completed` durumunda kalıyor.

**Nasıl çalışıyor:** `IAssetRepository`, `RenderService`'e diğerleri gibi isteğe bağlı bir constructor parametresi olarak eklendi. Microsoft DI, kayıtlı bir servisi varsayılan değerli parametreye de enjekte ediyor (API testi bunu gerçek DI ile kanıtlıyor). Kontrol hem servisin içinde (her yoldan gelen render için güvence) hem de endpoint'te (kullanıcıya anında 409) çağrılıyor.

---

## 8. `docs/TESTING.md`

Risk → test eşlemesi, iki komut ve "testi bir kez kırmızı görmeden güvenme" kuralı. Bir sonraki geliştirici (veya iş görüşmesinde sen) "neyi neden test ettin" sorusuna buradan cevap verebilir.

---

## Doğrulanan davranış

```
$ dotnet test
Başarılı! - Başarısız: 0, Başarılı: 108 - ShortsGenerator.Domain.Tests.dll          (Day 23: 72)
Başarılı! - Başarısız: 0, Başarılı: 114 - ShortsGenerator.Application.Tests.dll     (112)
Başarılı! - Başarısız: 0, Başarılı:  96 - ShortsGenerator.Infrastructure.Tests.dll  (84)
Başarılı! - Başarısız: 0, Başarılı:  24 - ShortsGenerator.Api.Tests.dll             (15)
                                                                     toplam 342     (283)
```

Gerçek API'ye karşı manuel kontrol (`dotnet run`, sonra HTTP istekleri):
```
POST /api/videos  "{ topic: "                     -> 400 {"error":"Failed to read parameter \"CreateVideoJobRequest request\" from the request body as JSON."}
POST /api/videos  sourceScript "Rest days matter." -> 400 {"error":"Your script can't be used: Too short (3 words; needs at least 65)."}
POST /api/videos  topic = 201 × "a"               -> 400 {"error":"Topic must be at most 200 characters."}
GET  /api/videos/{id}/plan  (bozuk plan.json)      -> 500 {"error":"The scene plan of video job '…' (…\\plan.json) can't be read: Expected depth to be zero … If it was edited by hand, fix or delete plan.json and generate the plan again."}
```
(İlk çalıştırmada mesajlarda `(Parameter 'sourceScript')` ve `0..` vardı; ikisi düzeltildi ve testle sabitlendi.)

Bu sonuçlar şunu kanıtlıyor: 8 risk alanının her biri en az bir test tarafından korunuyor ve yeni testlerin her biri en az bir kez kırmızı görüldü. Hata durumlarının hepsi kullanıcıya stack trace yerine ne olduğunu ve ne yapılacağını söyleyen bir mesaj gösteriyor. Roadmap kabul kriteri ("meaningful automated test suite passes") karşılandı.

## Bilinçli olarak sonraya bırakılanlar
* `Failed` bir iş düzenlenemiyor ve yeniden denenemiyor; `Completed` bir işin re-render'ı başarısız olursa iş `Failed` oluyor (iyi video diskte kalsa da). Kurtarma/yeniden deneme → **Day 26**.
* Arka planda bir `Generate` işi eksik asset kontrolüne takılırsa iş yine `Failed` oluyor (taze bir planda katalog dışı asset olamayacağı için pratikte olası değil).
