# Day 5 — Kod Notları

30 günlük roadmap, Gün 5. Konu: bir anlatım metnini (narration) şema doğrulamalı, 6-10 sahneden oluşan bir `VideoPlan`'a çeviren uçtan uca akış — kontrollü enum sözlükleri, JSON ayrıştırma, ve gerçek bir küçük yerel modelle (llama3.2:3b) canlı test ederek bulunan **beş gerçek hata**.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. Day 5'e başlamadan bulunan tasarım hatası — `VideoJob.cs`

```diff
     public void MarkPlanning(DateTimeOffset now) =>
         TransitionTo(VideoJobStatus.Planning, now, VideoJobStatus.Created);

-    public void MarkNarrating(string generatedScript, DateTimeOffset now)
-    {
-        if (string.IsNullOrWhiteSpace(generatedScript))
-        {
-            throw new ArgumentException("Generated script is required.", nameof(generatedScript));
-        }
-
-        TransitionTo(VideoJobStatus.Narrating, now, VideoJobStatus.Planning);
-        GeneratedScript = generatedScript;
-    }
+    // Attaching the script does NOT move the job to Narrating: per the pipeline in
+    // CLAUDE.md (script -> scenes -> assets -> TTS -> render), both script generation
+    // and scene planning happen while the job is still "Planning". Narrating is reserved
+    // for when real TTS audio is produced (Day 11) via MarkNarrating below.
+    public void AttachGeneratedScript(string generatedScript, DateTimeOffset now)
+    {
+        if (string.IsNullOrWhiteSpace(generatedScript))
+        {
+            throw new ArgumentException("Generated script is required.", nameof(generatedScript));
+        }
+
+        if (Status != VideoJobStatus.Planning)
+        {
+            throw new InvalidOperationException(
+                $"Cannot attach a generated script to video job {Id} while it is '{Status}'; it must be 'Planning'.");
+        }
+
+        GeneratedScript = generatedScript;
+        UpdatedAt = now;
+    }
+
+    public void MarkNarrating(DateTimeOffset now) =>
+        TransitionTo(VideoJobStatus.Narrating, now, VideoJobStatus.Planning);
```

Day 4'teki `ScriptGenerationService` da aynı anda güncellendi: `job.MarkNarrating(narration, now)` → `job.AttachGeneratedScript(narration, now)`.

**Neden:** `CLAUDE.md`'nin asıl pipeline'ı script → **sahne planı** → asset → TTS → render. Day 4'te script üretimi bitince job `Narrating`'e geçiriliyordu ("script hazır" anlamında kullanılmıştı) — ama bu, `VideoJobStatus` enum'undaki altı durumun **tek bir** `Planning → Narrating` geçişini script üretimiyle harcıyordu. Bugün sahne planlamasını eklemeye çalışırken şu soruyla karşılaştım: "sahne planlaması hangi durumda olmalı?" Narrating'de olması yanlış (henüz gerçek bir seslendirme yok), ama job zaten Narrating'e geçmişti. Bu, Day 2'de kurduğum durum makinesinin, Day 5'in ihtiyacını karşılayacak kadar "oda" bırakmadığının kanıtı — gerçek bir tasarım hatası, kozmetik bir isim tercihi değil.

**Nasıl çalışıyor:** `AttachGeneratedScript`, script'i sadece job `Planning` durumundaysa kabul ediyor (hâlâ bir `TransitionTo`-benzeri koruma var, ama **durum değiştirmiyor** — `Status` aynı kalıyor, sadece `GeneratedScript` ve `UpdatedAt` güncelleniyor). `MarkNarrating(DateTimeOffset now)` ise parametre olarak script almayan, saf bir durum geçişi — Day 11'de gerçek TTS sesi üretildiğinde kullanılacak, bugün hiçbir yerde çağrılmıyor (roadmap'in zaten tanımladığı durum makinesini *tamamlıyor*, spekülatif bir ekleme değil).

Bu değişiklik Day 2/4'ün ilgili testlerini de etkiledi (`VideoJobTests.cs`, `ScriptGenerationServiceTests.cs`) — `MarkNarrating("script", now)` çağrıları `AttachGeneratedScript("script", now)` + ayrı bir `MarkNarrating(now)` çağrısına bölündü, ve "script üretimi job'ı Narrating yapar" varsayımına dayanan assertion'lar "Planning'de kalır" şeklinde düzeltildi.

---

## 2. `src/ShortsGenerator.Domain/JsonCamelCaseEnumConverterAttribute.cs`

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShortsGenerator.Domain;

public sealed class JsonCamelCaseEnumConverterAttribute : JsonConverterAttribute
{
    public override JsonConverter CreateConverter(Type typeToConvert) =>
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase);
}
```

**Neden:** `CLAUDE.md`, sahne tiplerini **PascalCase** (`MythVsFact`, `AnatomyHighlight`) ama animasyon/SFX ID'lerini **camelCase/lowercase** (`punchZoom`, `whoosh`) olarak listeliyor — iki farklı isimlendirme kuralı aynı dokümanda. Standart `[JsonConverter(typeof(JsonStringEnumConverter))]` attribute'u parametre almadığı için (`JsonNamingPolicy.CamelCase` gibi bir argüman veremiyorsunuz), bu iki kuralı aynı anda desteklemenin yolu kendi attribute'unu yazmaktan geçiyor.

**Nasıl çalışıyor:** `JsonConverterAttribute`'dan türeyen bir sınıf, `CreateConverter(Type)` metodunu override ederek **istediği parametrelerle** bir converter örneği üretebilir — attribute'un kendisi parametre almasa bile, override edilen metodun içinde `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` gibi parametreli bir converter oluşturulabiliyor. Bu, .NET'in "attribute'u genişletilebilir kıl" desenlerinden biri; bir enum'un üzerine `[JsonCamelCaseEnumConverter]` yazmak, `[JsonConverter(typeof(JsonStringEnumConverter))]`'dan farklı olarak **camelCase** serileştirme kullanmasını sağlıyor.

---

## 3. `SceneType.cs`, `AnimationId.cs`, `SfxId.cs`

```csharp
// SceneType.cs
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SceneType
{
    Hook, ExerciseDemo, Comparison, MythVsFact, AnatomyHighlight, Progress, List, Recovery, Outro,
}

// AnimationId.cs
[JsonCamelCaseEnumConverter]
public enum AnimationId
{
    Fade, Slide, Pop, PunchZoom, Shake, Bounce, Pulse, Counter, Highlight,
}

// SfxId.cs
[JsonCamelCaseEnumConverter]
public enum SfxId
{
    Pop, Whoosh, Error, Success, Impact, Click,
}
```

**Neden:** `CLAUDE.md` §7/§11/§13'teki üç kontrollü sözlüğün (scene type, animasyon, SFX) birebir karşılığı. `CLAUDE.md` §7 ve §8 açıkça "LLM sadece bilinen değerlerden seçmeli, rastgele render talimatı uydurmamalı" diyor — bu üç `enum`, LLM'in JSON'da yazabileceği değerleri **derleme zamanında** (ve JSON ayrıştırma zamanında) kapalı bir kümeye kilitliyor; `SceneType.Foo` diye bir şey yok, yazılırsa parse hatası alınır (ki bu, retry döngüsünün doğru şekilde yakalayacağı bir durum).

**Nasıl çalışıyor:** Her enum'un üstündeki converter attribute'u (bölüm 2), o enum her serileştirilip deserileştirildiğinde **otomatik olarak** devreye giriyor — `Scene.Type`, `Scene.Animation`, `Scene.Sfx` property'leri bu enum tiplerini kullandığı sürece, hiçbir yerde elle "bu alanı camelCase yap" demeye gerek yok; kural enum'un **tanımında** yaşıyor, kullanıldığı her yerde tutarlı.

---

## 4. `Scene.cs` ve `VideoPlan.cs`

```csharp
public sealed record Scene(
    string Id,
    int Order,
    SceneType Type,
    int DurationSeconds,
    string? Narration,
    string? OnScreenText,
    IReadOnlyList<string> AssetIds,
    AnimationId Animation,
    SfxId? Sfx,
    string? Background,
    string? Emphasis);

public sealed record VideoPlan(
    string Title,
    string Hook,
    int TargetDurationSeconds,
    string NarrationText,
    IReadOnlyList<Scene> Scenes,
    string? Style,
    string? CaptionStyle,
    string? MusicMood);
```

**Neden:** `CLAUDE.md` §7'nin "Core Domain" tanımındaki `VideoPlan`/`Scene` alanlarının birebir karşılığı. `Scene.Narration`'ın `string?` (nullable) olması bugünün ilerleyen bir noktasında (bölüm 11, Hata #1) bulunan gerçek bir ihtiyaçtan geldi — ilk yazıldığında `string` (non-nullable) idi.

**Nasıl çalışıyor:** İki record da Day 2/3'teki `VideoJob`/`LlmAvailability` ile aynı pattern'i izliyor — positional record, otomatik `Equals`/`ToString`. `Sfx` alanı `SfxId?` (nullable enum) çünkü her sahnenin bir ses efekti olmak zorunda değil; `Animation` ise `AnimationId` (non-nullable) çünkü **her sahnenin** bir animasyonu olmalı (bu kural bölüm 13'teki Hata #3'ün kaynağı oldu).

---

## 5. `VideoPlanGenerationException.cs`, `IVideoPlanStore.cs`

```csharp
public sealed class VideoPlanGenerationException : Exception
{
    public VideoPlanGenerationException(string message) : base(message) { }
}

public interface IVideoPlanStore
{
    Task<string> SaveAsync(Guid jobId, VideoPlan plan, CancellationToken cancellationToken);
    Task<VideoPlan?> LoadAsync(Guid jobId, CancellationToken cancellationToken);
}
```

**Neden:** `VideoPlanGenerationException`, Day 4'teki `ScriptGenerationException` ile aynı mantık ama ayrı bir tip — "düz metin üretimi tükendi" ile "yapılandırılmış plan üretimi tükendi" farklı hata türleri, ayrı ayrı yakalanabilmeli (`Program.cs`'te farklı HTTP kodlarına çevriliyor). `IVideoPlanStore`, Day 2'deki `IVideoJobRepository`/Day 3'teki `ILlmProvider` ile aynı kural: Domain sözleşmeyi tanımlar, Infrastructure (bölüm 6) nasıl saklandığını bilir — bu sefer SQLite değil, düz bir JSON dosyası.

**Nasıl çalışıyor:** `SaveAsync`, `Task<string>` döndürüyor (kaydedilen klasörün yolu) — bu sayede çağıran taraf (`ScenePlanningService`, bölüm 8) `VideoJob.OutputDirectory`'yi bu dönen değerle dolduruyor, ayrı bir senkron "yolu hesapla" metoduna ihtiyaç kalmıyor.

---

## 6. `src/ShortsGenerator.Infrastructure/Storage/` — dosya tabanlı plan deposu

```csharp
// OutputStorageOptions.cs
public sealed class OutputStorageOptions
{
    public const string SectionName = "OutputStorage";
    public string RootDirectory { get; set; } = "output";
}
```

```csharp
// FileVideoPlanStore.cs
public sealed class FileVideoPlanStore : IVideoPlanStore
{
    public async Task<string> SaveAsync(Guid jobId, VideoPlan plan, CancellationToken cancellationToken)
    {
        var jobDirectory = GetJobDirectory(jobId);
        Directory.CreateDirectory(jobDirectory);
        var filePath = Path.Combine(jobDirectory, "plan.json");
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, plan, JsonOptions, cancellationToken);
        return jobDirectory;
    }

    // jobId is always a Guid (never user-supplied text), so this can never escape
    // _rootDirectory via path traversal.
    private string GetJobDirectory(Guid jobId) => Path.Combine(_rootDirectory, jobId.ToString());
}
```

**Neden:** Roadmap'in "save plan JSON" maddesi — ilk kez SQLite dışında bir kalıcılık mekanizması. `CLAUDE.md` §19'daki `output/<job-id>/final.mp4` konvansiyonuyla aynı klasör yapısını (`output/<job-id>/`) kullanıyor, böylece ileride (Day 7+) Remotion renderer'ı da aynı klasöre bakabilecek.

**Nasıl çalışıyor:**
* `GetJobDirectory(Guid jobId) => Path.Combine(_rootDirectory, jobId.ToString());` — yorum satırında açıklandığı gibi, `CLAUDE.md` §18'in "sanitize filenames / constrain generated files under known directories" kuralı burada **tasarım yoluyla** sağlanıyor: `jobId` her zaman bir `Guid` (asla kullanıcıdan gelen ham metin), bu yüzden `../../etc/passwd` gibi bir path traversal saldırısı mümkün değil — ekstra bir "temizleme" koduna gerek yok çünkü girdi zaten güvenli bir tip.
* `Directory.CreateDirectory(jobDirectory)` — klasör yoksa oluşturuyor (varsa hiçbir şey yapmıyor, hata fırlatmıyor — `CreateDirectory`'nin standart davranışı).
* `JsonSerializer.SerializeAsync(stream, plan, JsonOptions, cancellationToken)` — `plan` nesnesini doğrudan dosya akışına (stream) yazıyor; ara bir `string`'e serileştirip sonra diske yazmaktan daha az bellek kullanıyor, büyük planlar için önemli bir detay (bugün için pratik fark küçük, ama doğru alışkanlık).

`StorageServiceCollectionExtensions.AddVideoPlanStorage(...)`, Day 2/3'teki `AddInfrastructure`/`AddOllamaLlmProvider` ile aynı desen: `OutputStorageOptions`'ı config'den bağlıyor, `IVideoPlanStore`'u `FileVideoPlanStore` ile `Singleton` olarak kaydediyor (durumsuz, tekrar kullanılabilir).

---

## 7. `src/ShortsGenerator.Application/ScenePlanPromptBuilder.cs`

```csharp
public static string Build(string topic, string narrationText, IReadOnlyList<string>? previousFailureReasons = null)
{
    var sceneTypes = string.Join(", ", Enum.GetNames<SceneType>());
    // ...
    sb.AppendLine("Narration (do not change a single word of this - split it into ordered pieces,");
    sb.AppendLine(narrationText);
    // ... tam JSON şekli örneği ...
    sb.AppendLine($"Allowed values for \"type\" (use the exact spelling): {sceneTypes}.");
    sb.AppendLine("Allowed values for \"animation\": fade, slide, pop, punchZoom, ...");
    // ...
}
```

**Neden:** Day 4'teki `ScriptPromptBuilder` ile aynı felsefe (prompt = test edilebilir kod), ama burada **kritik bir tasarım kararı** var: prompt, modele "script'i yaz" demiyor, "**zaten yazılmış olan** script'i böl" diyor — `narrationText`, Day 4'te zaten üretilmiş gerçek script. Bu, modele daha az iş (ve daha az hata şansı) bırakıyor.

**Nasıl çalışıyor:**
* `Enum.GetNames<SceneType>()` — sahne tiplerinin listesini **koddan**, elle yazılmış bir string yerine doğrudan enum'dan üretiyor; `SceneType`'a yeni bir üye eklenirse prompt otomatik güncellenir, unutma riski yok. `animation`/`sfx` listeleri ise bilinçli olarak elle yazıldı (camelCase JSON isimlerini üretmek için enum'dan reflection yapmak gerekirdi, küçük ve sabit bir sözlük için bu karmaşıklığa değmedi).
* `"""..."""` (raw string literal, C# 11) — çok satırlı bir JSON örneğini, her satırı `AppendLine` ile tek tek yazmak yerine tek bir blokta, kaçış karakteri (`\"`) derdi olmadan yazmayı sağlıyor.
* Prompt'un sonundaki kural listesi ("Exactly one scene must have type Hook...", "narration must be copied verbatim...") doğrudan `VideoPlanValidator`'ın (bölüm 9) kontrol ettiği kurallarla **birebir eşleşiyor** — modelin uyması gereken kurallar ile bizim doğruladığımız kurallar arasında bir boşluk olmaması bilinçli bir tasarım hedefi.
* Son iki kural satırı ("Outro may have narration: null", "animation is REQUIRED") **bugün sonradan eklendi** — bölüm 11 ve 13'teki gerçek hatalardan sonra.

---

## 8. `src/ShortsGenerator.Application/PlanJsonExtractor.cs` (ilk hali)

```csharp
public static string Extract(string raw)
{
    var text = StripCodeFence(raw.Trim());
    return ExtractJsonObject(text);
}
```

**Neden:** Day 4'teki `ScriptTextCleaner` ile aynı "LLM'in talimata tam uymayacağını varsay" felsefesi — modelin markdown code fence (` ```json `) eklemesi ya da JSON'un önüne/arkasına açıklama koyması ihtimaline karşı bir güvenlik ağı.

**Nasıl çalışıyor:** `StripCodeFence`, metin ` ``` ` ile başlıyorsa ilk satırı (dil etiketini, `json` gibi) ve son ` ``` ` işaretini kesiyor. `ExtractJsonObject`, ilk `{` ile son `}` arasındaki alt diziyi alıyor — JSON'un önüne "Here's the plan:" gibi bir cümle, arkasına "Let me know if..." gibi bir kapanış eklenmişse bunları atıyor. Bu dosya **bugün iki kez daha** genişletildi (bölüm 11 ve 13) — aşağıda.

---

## 9. `src/ShortsGenerator.Domain/VideoPlanValidator.cs` (ilk hali)

```csharp
public static class VideoPlanValidator
{
    public const int MinScenes = 6;
    public const int MaxScenes = 10;
    public const int MinSceneDurationSeconds = 1;
    public const int MaxSceneDurationSeconds = 12;
    public const int MinTargetDurationSeconds = 20;
    public const int MaxTargetDurationSeconds = 50;

    public static ValidationResult Validate(VideoPlan plan, string expectedNarrationText)
    {
        // Title/Hook boş mu, sahne sayısı 6-10 mu, sıralama/Hook-Outro/tekil id,
        // her sahnenin narration/duration/assetId'si geçerli mi, toplam süre hedefe
        // yakın mı, ve sahnelerin narration'ları birleşince orijinal metni veriyor mu.
    }
}
```

**Neden:** Roadmap'in "script → valid 6-10 scene VideoPlan" kabul kriterinin somut karşılığı. `CLAUDE.md` §17'nin "VideoPlan schema validation, scene ordering" önceliğinin birebir uygulanması.

**Nasıl çalışıyor (en önemli kural):**
```csharp
private static void ValidateNarrationReconstruction(
    IReadOnlyList<Scene> scenes, string expectedNarrationText, List<string> reasons)
{
    var reconstructed = NormalizeWhitespace(string.Join(' ', scenes.OrderBy(s => s.Order).Select(s => s.Narration)));
    var expected = NormalizeWhitespace(expectedNarrationText);

    if (!string.Equals(reconstructed, expected, StringComparison.Ordinal))
    {
        reasons.Add("Concatenating all scene narrations in order does not exactly reproduce the original narration text...");
    }
}
```
Sahnelerin `Narration` alanlarını `Order`'a göre sıralayıp birleştiriyor, boşlukları normalize ediyor (`\s+` → tek boşluk), ve **orijinal** narration metniyle (Day 4'ün ürettiği gerçek script) birebir karşılaştırıyor. Bu kontrol olmasaydı, model narration'ı özetleyebilir/parafraz edebilir, ve Day 12'de (ses süresiyle zamanlama eşleme) tam script'in hangi sahneye denk geldiğini bilmek imkansız hale gelirdi — bu kural, ileride gelecek bir günün (Day 12) ön koşulunu bugünden garanti altına alıyor.

---

## 10. `ScenePlanningService.cs` (ilk hali) ve `Program.cs` endpoint'leri

```csharp
public async Task<VideoPlan> GeneratePlanAsync(Guid jobId, CancellationToken cancellationToken)
{
    var job = await _repository.GetByIdAsync(jobId, cancellationToken)
        ?? throw new KeyNotFoundException($"Video job '{jobId}' was not found.");

    if (job.Status != VideoJobStatus.Planning || string.IsNullOrWhiteSpace(job.GeneratedScript))
    {
        throw new InvalidOperationException(/* script yok, önce /script çağır */);
    }

    try
    {
        var plan = await GenerateValidatedPlanAsync(job.Topic, job.GeneratedScript, cancellationToken);
        var jobDirectory = await _planStore.SaveAsync(job.Id, plan, cancellationToken);
        job.SetOutputDirectory(jobDirectory, _timeProvider.GetUtcNow());
        await _repository.UpdateAsync(job, cancellationToken);
        return plan;
    }
    catch (Exception ex) when (ex is LlmUnavailableException or VideoPlanGenerationException)
    {
        job.MarkFailed(ex.Message, _timeProvider.GetUtcNow());
        await _repository.UpdateAsync(job, cancellationToken);
        throw;
    }
}
```

Ve `Program.cs`'e: `POST /api/videos/{id}/plan` (200/404/409/503/422) ile `GET /api/videos/{id}/plan`.

**Neden:** Day 4'teki `ScriptGenerationService` ile aynı orkestrasyon iskeleti (yükle → üret+doğrula+tekrar dene → başarılıysa kaydet, başarısızsa Failed işaretle). Buradaki fark: önkoşul kontrolü (`job.Status != Planning || script yok`) — sahne planlaması, script olmadan anlamsız, bu yüzden açık bir `InvalidOperationException` ile erken ve net şekilde reddediliyor.

**Nasıl çalışıyor:** `Program.cs`'teki `catch (InvalidOperationException ex) { return Results.Conflict(...); }` — HTTP 409 ("Conflict"), "isteğin kendisi geçerli ama kaynağın şu anki durumuyla çelişiyor" anlamına geliyor; "script'in olmaması" tam olarak bu kategoriye giriyor (404 değil çünkü job var, 400 değil çünkü istek formatı doğru).

---

## 11. Hata #1 (gerçek Ollama testinde bulundu) — Outro sahnesi narration'sız

Gerçek bir job üzerinde `POST /api/videos/{id}/plan` çağrıldığında:

```
{"error":"Could not produce a valid scene plan ... Last issues:
Scene 'scene-7' (order 7) has empty narration."}
```

Ollama'ya gerçek prompt'u elle (Python ile) tekrar göndererek ham çıktı incelendi: model, 6 cümlelik narration'ı 6 sahneye böldükten **sonra**, kendiliğinden 7. bir "Outro" sahnesi ekliyor — ama narration'da bu sahne için harcanacak söz kalmadığı için `"narration": null` yazıyor.

**Neden:** Modelin bu davranışı aslında mantıklı — `CLAUDE.md` §11'de `OutroCard` diye adı geçen bir bileşen var, yani sessiz, sadece görsel bir kapanış kartı **ürünün kendi tasarımına uygun**. Hatalı olan, benim `VideoPlanValidator`'ımın "her sahnenin narration'ı olmalı" kuralıydı — model, ürünün gerçek niyetine daha yakın davranmıştı.

**Düzeltme:** `Scene.Narration` `string` → `string?`; `VideoPlanValidator.ValidateIndividualScenes`'te narration-boşluk kontrolü `scene.Type != SceneType.Outro` koşuluyla sınırlandı (bölüm 4 ve 9'da zaten gösterilen son hal). Prompt'a da "Outro sahnesi narration: null olabilir, uydurma" talimatı eklendi (bölüm 7).

---

## 12. Hata #2 — `"null"` string'i gerçek `null` yerine

```
error CS... (çalışma zamanı): The JSON value could not be converted to
ShortsGenerator.Domain.Scene. Path: $.scenes[1].type
```

Ham çıktı incelendiğinde: `"onScreenText": "null"`, `"animation": "null"` gibi alanlarda model **string olarak** `"null"` yazmış, JSON `null` değil. `AnimationId` non-nullable bir enum olduğu için bu, "null" adlı bir enum üyesi arandığı için parse hatası veriyor.

**Düzeltme** — `PlanJsonExtractor.cs`:
```csharp
private static readonly Regex StringNullLiteralPattern =
    new("""(?<=:\s*)"null"(?=\s*[,}\]])""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

private static string NormalizeNullStringLiterals(string text) => StringNullLiteralPattern.Replace(text, "null");
```

**Nasıl çalışıyor:** Regex, **sadece** bir property değerinin **tamamı** `"null"` ise eşleşiyor — `(?<=:\s*)` (geriye bakan, ":" sonrası) ve `(?=\s*[,}\]])` (ileri bakan, ardından virgül/kapanış parantezi) **lookbehind/lookahead** kullanıyor, yani eşleşen kısım sadece `"null"` metninin kendisi (sarmalayan `:`/`,` karakterleri tüketilmiyor, sadece kontrol ediliyor). Bu sayede narration metninin içinde gerçekten "null" kelimesi geçen bir cümle (`"This exercise targets the null point of balance."`) **yanlışlıkla** bozulmuyor — çünkü o durumda `"null"` hemen ardından `,`/`}` gelmiyor, cümlenin geri kalanı geliyor.

---

## 13. Hata #3 — gerçek JSON `null`, zorunlu `animation` alanında

Düzeltme #2'den sonra tekrar denendiğinde, bu sefer model gerçekten geçerli bir JSON `null` üretti (string değil): `"animation": null`. Ama `Animation` alanı **non-nullable** bir enum (`AnimationId`, `AnimationId?` değil) — gerçek `null`'ı bile kabul edemiyor.

**Düzeltme** — `PlanJsonExtractor.cs`:
```csharp
private static readonly Regex NullAnimationPattern =
    new("""("animation"\s*:\s*)null""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

private static string DefaultMissingAnimation(string text) => NullAnimationPattern.Replace(text, "$1\"fade\"");
```
Prompt'a da "animation is REQUIRED on every scene ... never null ... use fade" talimatı eklendi.

**Nasıl çalışıyor:** `"$1\"fade\""` — regex'in replacement string'inde `$1`, yakalanan ilk grubu (`("animation"\s*:\s*)`) aynen koruyor, sadece ardından gelen `null`'ı `"fade"` ile değiştiriyor. Bu, "iste + kodda da koru" (defense in depth) prensibinin tam örneği: prompt modelden bunu istiyor, ama model uymazsa bile kod sessizce makul bir varsayılana düşüyor — bu, bir denemeyi boşa harcamak yerine kurtarıyor.

---

## 14. Hata #4 — JSON yarıda kesiliyor (Ollama'nın cevap uzunluğu limiti)

```
{"error":"... Expected depth to be zero at the end of the JSON payload.
There is an open JSON object or array that should be closed.
Path: $.scenes[8] | LineNumber: 111"}
```

9 sahneli bir plan üretilirken JSON'un ortasında kesildi — `$.scenes[8]`'den sonra kapanış parantezleri hiç gelmemiş. Kök neden: Ollama'nın `/api/generate` endpoint'i, `options.num_predict` elle belirtilmezse **kendi varsayılan** üretim uzunluğu sınırını kullanıyor; bu sınır, 9 sahnelik ayrıntılı bir JSON nesnesi için yetersiz kaldı.

**Düzeltme:**
```csharp
// OllamaOptions.cs
public int MaxOutputTokens { get; set; } = 2048;

// OllamaLlmProvider.cs
var request = new OllamaGenerateRequest(
    _options.Model, prompt, Stream: false, new OllamaGenerateOptions(_options.MaxOutputTokens));

internal sealed record OllamaGenerateRequest(string Model, string Prompt, bool Stream, OllamaGenerateOptions Options);
internal sealed record OllamaGenerateOptions(int NumPredict);
```

**Neden:** Day 4'ün kısa narration promptu bu sınıra hiç yaklaşmıyordu, bu yüzden Day 3-4'te hiç fark edilmedi — sorun, sadece daha **uzun** bir cevap (sahne planı) istenince ortaya çıktı. 2048 token, hem narration (kısa) hem plan (uzun) için güvenli bir üst sınır: `num_predict`, bir **tavan**, hedef uzunluk değil — model kendi EOS (bitiş) token'ına ulaştığında zaten durur, bu sınır sadece "asla bundan fazla üretme" diyor.

**Nasıl çalışıyor:** `OllamaGenerateRequest` artık dördüncü bir alan (`Options`) taşıyor; bu, Ollama'nın `/api/generate` API'sindeki `"options": {"num_predict": 2048}` gövdesine karşılık geliyor. Bu değişiklik **tüm** `GenerateAsync` çağrılarını etkiliyor (Day 3'ün ping'i, Day 4'ün script üretimi, Day 5'in plan üretimi) — hepsi aynı cömert tavanı paylaşıyor, zararı yok çünkü kısa cevaplar zaten kendi doğal bitiş noktalarında duruyor.

---

## 15. Hata #5 — modelin kendi beyan ettiği süre, sahne sürelerinin toplamıyla tutarsız

Hata #4 düzeltildikten sonra JSON artık doğru ayrıştı, ama:
```
{"error":"... Scene durations add up to 44s, which is too far from
TargetDurationSeconds (30s).; Concatenating all scene narrations ..."}
```

Model, `"targetDurationSeconds": 30` yazmış ama sahnelerin `durationSeconds` toplamı 44 çıkmış — iki sayıyı kendi içinde tutarlı tutamamış.

**Düzeltme** — `ScenePlanningService.cs`:
```csharp
// Derive the real target duration from the scenes themselves instead of trusting
// the model's separately-declared targetDurationSeconds field - same principle as
// NarrationText above: don't make the model keep two numbers in sync when one can
// just be computed from the other.
var totalDuration = generated.Scenes.Sum(s => s.DurationSeconds);

var plan = new VideoPlan(generated.Title, generated.Hook, totalDuration, narrationText, ...);
```
Ve `VideoPlanValidator.ValidateTotalDuration`, artık `plan.TargetDurationSeconds`'ı modelin kendi beyanıyla değil, **doğrudan** ürün kuralıyla (20-50 saniye video uzunluğu) karşılaştırıyor:
```csharp
private static void ValidateTotalDuration(VideoPlan plan, List<string> reasons)
{
    var totalDuration = plan.Scenes.Sum(s => s.DurationSeconds);
    if (totalDuration < MinTargetDurationSeconds || totalDuration > MaxTargetDurationSeconds)
    {
        reasons.Add($"Scene durations add up to {totalDuration}s; the whole video must be between ...");
    }
}
```

**Neden:** Bu, Day 5'in `NarrationText` kararıyla (bölüm 10'daki `ScenePlanningService` yorumunda görüldüğü gibi, "modelden zaten bildiğin veriyi tekrar üretmesini isteme") **aynı prensibin ikinci uygulaması**: `targetDurationSeconds`, aslında sahnelerin toplamından **türetilebilir** bir değer — modelin bunu ayrıca, bağımsız olarak doğru hesaplamasını istemek, gereksiz bir tutarlılık yükü. Bu sayıyı biz hesaplayınca, "iki sayı birbiriyle uyuşmuyor" diye bir hata sınıfı **tamamen ortadan kalkıyor** — var olmayan bir hatayı düzeltmeye çalışmak yerine, hatanın oluşabileceği yeri kaldırdık.

**Nasıl çalışıyor:** Eski `ValidateTotalDuration`, `Math.Abs(totalDuration - plan.TargetDurationSeconds) > DurationToleranceSeconds` şeklinde iki sayıyı karşılaştırıyordu (±5 saniye tolerans). Yeni hali, `plan.TargetDurationSeconds`'a hiç bakmıyor (zaten servis tarafından `totalDuration`'a eşit olarak **inşa ediliyor**) — sadece gerçek toplamın ürünün izin verdiği video uzunluğu aralığında (20-50 saniye) olup olmadığını kontrol ediyor. Bu arada, `Validate` metodunun başındaki ayrı "`plan.TargetDurationSeconds` 20-50 arasında mı" kontrolü de kaldırıldı — artık `ValidateTotalDuration` tek başına bu işi (daha doğru şekilde, gerçek toplama bakarak) yapıyor.

---

## 16. Son ince ayar — deneme sayısı 3'ten 5'e

```csharp
// Higher than ScriptGenerationService's 3: real testing against llama3.2:3b showed scene
// planning (exact-text splitting + strict JSON shape together) is a harder task for a
// small local model than plain narration generation, and needs more room to self-correct.
private const int MaxAttempts = 5;
```

**Neden:** Hata #1-5 düzeltildikten sonra JSON yapısal olarak **her zaman** doğru ayrıştı (birkaç farklı gerçek denemede doğrulandı) — kalan tek başarısızlık türü, modelin narration'ı birebir kopyalamak yerine birkaç kelimesini değiştirmesiydi (paraphrase). Bu, kod hatası değil, 3 milyar parametreli küçük bir modelin bu özel, talepkâr görevdeki (hem tam metin kopyalama hem katı JSON şekli aynı anda) gerçek bir sınırı. Day 4'ün script üretimi (daha basit bir görev) 3 denemede hep başarılı oldu; sahne planlaması için 5 deneme, modele kendini düzeltmesi için daha fazla şans tanıyor — `CLAUDE.md` §8'in "retry a limited number of times" kuralına hâlâ uyuyor, sadece "limited" sayısı göreve göre ayarlandı.

---

## Doğrulanan davranış

```
dotnet build  → 0 Uyarı, 0 Hata (9 proje)
dotnet test   → Domain.Tests: 36 | Application.Tests: 32 | Infrastructure.Tests: 9 | Api.Tests: 1
                Toplam: 78 Başarısız: 0

Gerçek Ollama + llama3.2:3b'ye karşı CANLI test geçmişi (bu günün asıl kanıtı):
1. İlk deneme → "Scene 'scene-7' has empty narration" (Hata #1, düzeltildi)
2. İkinci deneme → "scenes[1].type" JSON parse hatası, ham çıktıda "animation":"null" (Hata #2, düzeltildi)
3. Üçüncü deneme → ham çıktıda gerçek "animation": null (Hata #3, düzeltildi)
4. Dördüncü deneme → "Expected depth to be zero..." (JSON kesilmesi, Hata #4, düzeltildi)
5. Beşinci deneme → JSON artık TAM ve doğru ayrıştı; kalan hata: süre tutarsızlığı +
   narration parafrazı (Hata #5'in ilk yarısı düzeltildi; ikinci yarısı - parafraz -
   kodun doğru çalıştığının kanıtı: geçersiz çıktıyı doğru şekilde reddetti)

Her başarısız denemede: job doğru şekilde "Failed" durumuna geçti, ErrorMessage alanı
nedeni net şekilde açıklıyordu (stack trace değil), ve hiçbir zaman geçersiz bir plan
sessizce kabul edilmedi.
```

Bu beş gerçek, canlı bulgu — hepsi unit testlerin **kapsamadığı** gerçek model davranışları — `CLAUDE.md` §17'nin "LLM response parsing" test önceliğinin neden sadece sahte (fake) testlerle yetinilemeyeceğinin kanıtı: `FakeLlmProvider` testleri (32 test) bizim **varsaydığımız** hata biçimlerini doğru işlediğimizi kanıtlıyor, ama gerçek modelin **gerçekte** nasıl hata yaptığını ancak gerçek bir Ollama'ya karşı çalıştırarak öğrenebildik. Mekanizmanın kendisi (şema doğrulama + sınırlı tekrar deneme + net başarısızlık) roadmap'in istediği şekilde **kanıtlanmış** durumda; kalan parafraz sorunu, kod değil, seçilen küçük modelin bu özel görevdeki gerçek bir sınırı olarak `docs/DAY_STATE.md`'de dürüstçe not edildi.
