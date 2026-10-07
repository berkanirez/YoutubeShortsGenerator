# Day 4 — Kod Notları

30 günlük roadmap, Gün 4. Konu: bir konuyu (`topic`) gerçek, doğrulanmış bir İngilizce Shorts anlatımına çeviren uçtan uca akış — prompt'u kod olarak test etmek, LLM çıktısını doğrulamak, sınırlı sayıda tekrar deneme (bounded retry) ve bunu `VideoJob`'ın durum makinesine bağlamak.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. `src/ShortsGenerator.Domain/ScriptValidationResult.cs` ve `ScriptValidator.cs`

```csharp
namespace ShortsGenerator.Domain;

public sealed record ScriptValidationResult(bool IsValid, IReadOnlyList<string> Reasons);
```

```csharp
using System.Text.RegularExpressions;

namespace ShortsGenerator.Domain;

public static class ScriptValidator
{
    // ~25-45s of spoken narration at a natural pace (~140-150 wpm) is roughly 60-110 words.
    // 130 is a hard ceiling so a long outlier can't blow out the video duration.
    public const int MinWords = 40;
    public const int MaxWords = 130;

    private static readonly string[] DisallowedPhrases =
    [
        "cure", "cures", "cured",
        "diagnose", "diagnosis",
        "prescribe", "prescription",
        "treats disease", "treatment for",
        "guaranteed results", "guaranteed to",
    ];

    public static ScriptValidationResult Validate(string narration)
    {
        var reasons = new List<string>();

        if (string.IsNullOrWhiteSpace(narration))
        {
            reasons.Add("Narration is empty.");
            return new ScriptValidationResult(false, reasons);
        }

        var wordCount = CountWords(narration);
        if (wordCount < MinWords)
        {
            reasons.Add($"Too short ({wordCount} words; needs at least {MinWords}).");
        }
        else if (wordCount > MaxWords)
        {
            reasons.Add($"Too long ({wordCount} words; must be at most {MaxWords}).");
        }

        foreach (var phrase in DisallowedPhrases)
        {
            if (narration.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"Contains a disallowed medical-claim phrase: '{phrase}'.");
            }
        }

        return new ScriptValidationResult(reasons.Count == 0, reasons);
    }

    private static int CountWords(string text) => Regex.Matches(text, @"\S+").Count;
}
```

**Neden:** `CLAUDE.md` §4 ve §17, LLM çıktısının asla körü körüne güvenilmemesini, hem uzunluk hem güvenlik açısından doğrulanmasını istiyor. Bu, projenin **AI'ı bir sınır arkasında tutma** felsefesinin somut ilk örneği: `ILlmProvider.GenerateAsync` ham bir string döndürür, ama bu ham string hiçbir zaman doğrudan bir `VideoJob`'a yazılmaz — önce buradan geçer.

**Nasıl çalışıyor:**
* `public const int MinWords = 40; public const int MaxWords = 130;` — `public const` olmaları bilinçli: hem `ScriptPromptBuilder` (bölüm 5) hem testler (bölüm 9, 13) bu sayıları **tek bir yerden** okuyor; aynı "40" ve "130" sayılarının üç farklı dosyada elle tekrarlanıp birinin güncellenip diğerinin unutulması riski ortadan kalkıyor.
* `Regex.Matches(text, @"\S+").Count` — `\S+` ("non-whitespace dizisi") deseniyle eşleşen parça sayısını sayıyor; `text.Split(' ')`'den farklı olarak satır sonu/tab gibi her türlü boşluk karakterini doğru şekilde kelime ayracı sayıyor.
* `DisallowedPhrases` üzerinde `foreach` + `Contains(..., StringComparison.OrdinalIgnoreCase)` — basit ama gerçek bir "son çare ağı" (last-resort net): tam bir tıbbi güvenlik sınıflandırıcısı değil, sadece en bariz ihlalleri (`"cure"`, `"diagnose"`, `"guaranteed results"` gibi) yakalıyor. Bu, kapsamın bilinçli olarak dar tutulduğu bir yer — roadmap'in kendi "myth-busting/kanıta dayalı inceleme" maddesi Day 29'da.
* Fonksiyon **tek bir** `ScriptValidationResult` ile hem "geçerli mi" hem "hangi sebeplerden geçersiz" bilgisini birlikte döndürüyor — birden fazla kuralın aynı anda ihlal edilmesi durumunda (örneğin hem çok kısa hem yasaklı kelime içeren bir metin) `reasons` listesi hepsini biriktiriyor, ilk hatada durup diğerlerini gizlemiyor.

---

## 2. `src/ShortsGenerator.Domain/ScriptGenerationException.cs`

```csharp
namespace ShortsGenerator.Domain;

public sealed class ScriptGenerationException : Exception
{
    public ScriptGenerationException(string message)
        : base(message)
    {
    }
}
```

**Neden:** Day 3'teki `LlmUnavailableException` ile aynı mantık, ama farklı bir başarısızlık türü için: Ollama'ya ulaşılabiliyor ve model yüklü, ama **üretilen metin bir türlü kurallara uymuyor** (sınırlı deneme sayısı tükendi). Bu iki durumun ayrı exception tipleri olması, `Program.cs`'in (bölüm 8) ikisine farklı HTTP durum koduyla cevap verebilmesini sağlıyor: biri "servis kullanılamıyor" (503), diğeri "işlenemeyen istek" (422).

---

## 3. `src/ShortsGenerator.Domain/IVideoJobRepository.cs` — eksik bulunan `UpdateAsync`

```diff
 public interface IVideoJobRepository
 {
     Task AddAsync(VideoJob job, CancellationToken cancellationToken);

+    Task UpdateAsync(VideoJob job, CancellationToken cancellationToken);
+
     Task<VideoJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
```

**Neden:** Bu, bugün gerçek bir eksiklik olarak ortaya çıktı: Day 2'de bu arayüz yazılırken henüz hiçbir akış bir job'ı **oluşturduktan sonra güncellemiyordu** (sadece ekleme ve okuma vardı). Bugün ilk kez gerçek bir iş akışı (`ScriptGenerationService`, bölüm 6) bir job'ı yüklüyor, durumunu değiştiriyor (`MarkPlanning` → `MarkNarrating`/`MarkFailed`) ve bu değişikliği **kalıcı hale getirmesi** gerekiyor — ki arayüzde bunun için bir metot yoktu. Bu, "önce ihtiyacı olan gerçek bir senaryo yaz, arayüz eksikse genişlet" yaklaşımının doğal bir sonucu; Day 2'de bunu önceden tahmin edip eklemek, henüz kullanılmayan bir metot için spekülasyon olurdu (YAGNI).

**Nasıl çalışıyor:** Infrastructure tarafı:
```csharp
public async Task UpdateAsync(VideoJob job, CancellationToken cancellationToken)
{
    _dbContext.VideoJobs.Update(job);
    await _dbContext.SaveChangesAsync(cancellationToken);
}
```
`DbSet<T>.Update(entity)` — entity zaten bu `DbContext` tarafından takip ediliyorsa (bizim asıl senaryomuz: aynı `DbContext`/HTTP isteği kapsamında önce `GetByIdAsync` ile yüklenip sonra değiştiriliyor), bu çağrı pratikte gereksiz ama zararsız. Entity **takip edilmiyorsa** (örneğin başka bir scope'tan gelen bir nesne), `Update` onu "Modified" olarak işaretleyip tüm property'lerini güncellemeye zorluyor — daha genel, savunmacı bir davranış. `SaveChangesAsync`, Day 2'deki `AddAsync`'teki gibi, asıl `UPDATE` SQL'ini tetikleyen çağrı.

---

## 4. `src/ShortsGenerator.Application/ScriptPromptBuilder.cs`

```csharp
using System.Text;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public static class ScriptPromptBuilder
{
    public static string Build(string topic, IReadOnlyList<string>? previousFailureReasons = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are writing the spoken narration for a short (25-45 second), vertical fitness");
        sb.AppendLine("education video. The audience is general fitness enthusiasts, not medical patients.");
        sb.AppendLine();
        sb.AppendLine($"Topic: {topic}");
        sb.AppendLine();
        sb.AppendLine($"Write {ScriptValidator.MinWords}-{ScriptValidator.MaxWords} words of plain spoken English.");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Focus on exercise technique, training principles, or general training knowledge.");
        sb.AppendLine("- Never diagnose, prescribe, promise a cure, or claim to treat a disease or injury.");
        sb.AppendLine("- Use cautious wording for anything uncertain or health-sensitive.");
        sb.AppendLine("- Plain prose only: no markdown, no headings, no bullet points, no emoji, no quotation marks.");
        sb.AppendLine("- Output ONLY the narration text itself, nothing before or after it.");

        if (previousFailureReasons is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Your previous attempt was rejected for these reasons - fix all of them this time:");
            foreach (var reason in previousFailureReasons)
            {
                sb.AppendLine($"- {reason}");
            }
        }

        return sb.ToString();
    }
}
```

**Neden:** `CLAUDE.md` §23'ün "prompt as application logic" öğretisinin tam karşılığı: prompt, `ScriptGenerationService`'in içine gömülü, test edilemeyen bir string birleştirmesi değil, **kendi başına, bağımsız olarak çağrılıp test edilebilen** statik bir metot. `ScriptValidator.MinWords`/`MaxWords`'ü doğrudan kullanması (bölüm 1), kuralların iki yerde (doğrulama ve prompt) **her zaman senkron** kalmasını garantiliyor — biri değişirse diğeri otomatik güncellenir.

**Nasıl çalışıyor:**
* `public static string Build(string topic, IReadOnlyList<string>? previousFailureReasons = null)` — ikinci parametre **opsiyonel** (`= null` varsayılan değeri sayesinde): ilk deneme için `ScriptPromptBuilder.Build(topic)` yeterli, tekrar denemelerde `ScriptPromptBuilder.Build(topic, failureReasons)` ile aynı metot zenginleştirilmiş haliyle çağrılabiliyor.
* `StringBuilder` kullanımı — Day 2/3'teki `$"..."` string interpolation'dan farklı olarak, burada **çok satırlı, parça parça inşa edilen** bir metin söz konusu; her `AppendLine` çağrısı yeni bir satır ekliyor. Tek bir dev interpolated string yazmak yerine `StringBuilder` kullanmak, hem okunabilirliği artırıyor hem de koşullu blokların (aşağıdaki `if`) doğal şekilde araya girmesini sağlıyor.
* `if (previousFailureReasons is { Count: > 0 })` — Day 3'teki `availability is { IsReachable: true, ... }` ile aynı property pattern matching syntax'ı; burada hem `null` kontrolü hem "liste boş değil" kontrolü **tek bir ifadede** birleşiyor (`previousFailureReasons != null && previousFailureReasons.Count > 0` yazmaya göre daha kısa ve deyimsel).
* Prompt'un son satırı ("Output ONLY the narration text itself...") kasıtlı olarak eklendi çünkü LLM'ler sıkça "Here's your script:" gibi bir önsöz ekler; bu talimat bunu **azaltmayı** hedefliyor, ama tamamen engellemeyeceği için bölüm 5'teki `ScriptTextCleaner` de ayrıca bir güvenlik ağı olarak var — "hem iste hem doğrula" (defense in depth).

---

## 5. `src/ShortsGenerator.Application/ScriptTextCleaner.cs`

```csharp
using System.Text.RegularExpressions;

namespace ShortsGenerator.Application;

public static class ScriptTextCleaner
{
    private static readonly string[] KnownPreambles =
    [
        "here's your script:",
        "here is your script:",
        "narration:",
        "script:",
    ];

    public static string Clean(string raw)
    {
        var text = raw.Trim();
        text = StripKnownPreamble(text);
        text = StripWrappingQuotes(text);
        text = CollapseWhitespace(text);
        return text.Trim();
    }

    private static string StripKnownPreamble(string text)
    {
        foreach (var preamble in KnownPreambles)
        {
            if (text.StartsWith(preamble, StringComparison.OrdinalIgnoreCase))
            {
                return text[preamble.Length..].TrimStart();
            }
        }

        return text;
    }

    private static string StripWrappingQuotes(string text)
    {
        if (text.Length >= 2)
        {
            var first = text[0];
            var last = text[^1];
            var isMatchingQuotePair = (first == '"' && last == '"') || (first == '\'' && last == '\'');
            if (isMatchingQuotePair)
            {
                return text[1..^1].Trim();
            }
        }

        return text;
    }

    private static string CollapseWhitespace(string text) => Regex.Replace(text, @"\s+", " ");
}
```

**Neden:** Bu, bölüm 4'teki "iste ama güvenme" felsefesinin uygulama kısmı — LLM'in prompt talimatına tam uymadığı (önsöz eklediği, tırnak içine aldığı, fazladan boşluk bıraktığı) gerçek durumları ele alıyor. `ScriptValidator`'a (bölüm 1) gitmeden **önce** çalışması önemli: aksi halde `"Here's your script: ..."` gibi bir önsöz kelime sayısını şişirip, aslında geçerli olacak bir metni yanlışlıkla reddettirebilirdi.

**Nasıl çalışıyor:**
* `text[preamble.Length..]` — C# 8+ **range/index operatörü**: `preamble.Length`'ten dizinin sonuna kadar olan alt diziyi alıyor (`.Substring(preamble.Length)` ile aynı iş, daha kısa syntax).
* `text[1..^1]` — `^1`, "sondan 1. eleman" demek (index-from-end operatörü); `1..^1`, ilk ve son karakteri **atlayıp** aradaki her şeyi alıyor — tırnak işaretlerini soymanın yolu.
* `var isMatchingQuotePair = (first == '"' && last == '"') || (first == '\'' && last == '\'');` — sadece **eşleşen** bir çift tırnak varsa soyuyor (`"merhaba'` gibi yarım/uyumsuz bir durumda dokunmuyor) — yanlışlıkla metnin içindeki gerçek bir tırnağı koparmamak için.
* `Regex.Replace(text, @"\s+", " ")` — bir ya da daha fazla ardışık boşluk/satır sonu/tab karakterini **tek bir boşlukla** değiştiriyor; LLM çıktısındaki rastgele çift boşlukları veya satır sonlarını, TTS'in (Day 11) rahatça okuyabileceği düz, tek satırlık bir metne indiriyor.
* `Clean` metodunun kendisi üç adımı **sırayla** uyguluyor (önsöz → tırnak → boşluk) — sıra önemli: önce önsöz kaldırılmazsa, tırnak kontrolü "Here's your script: "..."" gibi bir durumda yanlış karakterlere bakardı.

---

## 6. `src/ShortsGenerator.Application/ScriptGenerationService.cs`

```csharp
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public sealed class ScriptGenerationService
{
    private const int MaxAttempts = 3;

    private readonly ILlmProvider _llmProvider;
    private readonly IVideoJobRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ScriptGenerationService(ILlmProvider llmProvider, IVideoJobRepository repository, TimeProvider timeProvider)
    {
        _llmProvider = llmProvider;
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<VideoJob> GenerateScriptAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _repository.GetByIdAsync(jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Video job '{jobId}' was not found.");

        job.MarkPlanning(_timeProvider.GetUtcNow());

        try
        {
            var narration = await GenerateValidatedNarrationAsync(job.Topic, cancellationToken);
            job.MarkNarrating(narration, _timeProvider.GetUtcNow());
        }
        catch (Exception ex) when (ex is LlmUnavailableException or ScriptGenerationException)
        {
            job.MarkFailed(ex.Message, _timeProvider.GetUtcNow());
            await _repository.UpdateAsync(job, cancellationToken);
            throw;
        }

        await _repository.UpdateAsync(job, cancellationToken);
        return job;
    }

    private async Task<string> GenerateValidatedNarrationAsync(string topic, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> failureReasons = [];

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var prompt = ScriptPromptBuilder.Build(topic, failureReasons);
            var raw = await _llmProvider.GenerateAsync(prompt, cancellationToken);
            var cleaned = ScriptTextCleaner.Clean(raw);
            var validation = ScriptValidator.Validate(cleaned);

            if (validation.IsValid)
            {
                return cleaned;
            }

            failureReasons = validation.Reasons;
        }

        throw new ScriptGenerationException(
            $"Could not produce a valid narration for '{topic}' after {MaxAttempts} attempts. " +
            $"Last issues: {string.Join("; ", failureReasons)}");
    }
}
```

**Neden:** Bu, bugünün ana orkestrasyon sınıfı — Day 2'nin domain modelini (`VideoJob`'ın durum geçişleri), Day 3'ün `ILlmProvider`'ını ve bugünün prompt/doğrulama/temizleme parçalarını **tek bir gerçek iş akışında** birleştiriyor. `VideoJobService`'e bu mantığı eklemek yerine ayrı bir sınıf yapılması bilinçli: `VideoJobService` CRUD'a yakın basit operasyonları tutuyor, `ScriptGenerationService` ise çok adımlı, hataya açık bir **iş akışını** yönetiyor — tek sorumluluk ilkesi.

**Nasıl çalışıyor:**
* `var job = await _repository.GetByIdAsync(jobId, cancellationToken) ?? throw new KeyNotFoundException(...);` — Day 2'deki `??`/null kontrolü deseninin burada "ya değeri al ya da hemen anlamlı bir hatayla patla" şekli; `Program.cs`'teki (bölüm 8) `catch (KeyNotFoundException)` bloğu bunu 404'e çeviriyor.
* `job.MarkPlanning(_timeProvider.GetUtcNow());` — Day 2'de yazılan durum geçiş kuralı burada ilk kez **gerçek bir senaryoda** kullanılıyor: job `Created` değilse (örneğin script daha önce zaten üretildiyse) bu satır `InvalidOperationException` fırlatır — yani bir job'ın script'i **iki kez** üretilmeye çalışılması, Day 2'nin kuralları sayesinde otomatik olarak engelleniyor, burada ek bir kontrol yazmaya gerek kalmadı.
* ```csharp
  catch (Exception ex) when (ex is LlmUnavailableException or ScriptGenerationException)
  ```
  **Exception filter** (`when` anahtar kelimesi) + **tip deseni** (`is X or Y`): sadece bu iki spesifik exception tipini yakalıyor, başka bir exception (örneğin beklenmeyen bir programlama hatası) bu `catch`'e hiç girmeden yukarı fırlamaya devam ediyor. Bu bilinçli bir tercih: beklenmeyen her hatayı sessizce "job failed" olarak işaretlemek, geliştirme sırasında gerçek bugları gizleyebilirdi.
  * `job.MarkFailed(ex.Message, ...)` + `await _repository.UpdateAsync(job, cancellationToken); throw;` — job'ı veritabanında `Failed` olarak işaretleyip **sonra** aynı exception'ı tekrar fırlatıyor (`throw;` — `throw ex;` değil, böylece orijinal stack trace korunuyor). Çağıran taraf (`Program.cs`) hem veritabanındaki güncel durumu hem de fırlatılan exception'ı kullanarak doğru HTTP cevabını üretebiliyor.
* `GenerateValidatedNarrationAsync` — asıl "üret, temizle, doğrula, gerekirse tekrar dene" döngüsü. `IReadOnlyList<string> failureReasons = [];` — **collection expression** (C# 12), boş bir salt-okunur liste oluşturmanın kısa yolu (`new List<string>()` yerine). Döngü her turda bir önceki turun `failureReasons`'ını bir sonraki `ScriptPromptBuilder.Build` çağrısına veriyor — bu, LLM'e "neyi düzeltmen gerektiğini" söyleyen, kendi kendini onaran bir geri bildirim döngüsü.
* `MaxAttempts` tükenirse `ScriptGenerationException` fırlatılıyor; mesaj hem konuyu hem deneme sayısını hem de **son** turun başarısızlık sebeplerini içeriyor — bu hem kullanıcıya (API cevabında) hem loglara gidecek, teşhis edilebilir bir mesaj.

---

## 7. `src/ShortsGenerator.Api/Program.cs` — yeni endpoint ve DI kaydı

```csharp
builder.Services.AddScoped<VideoJobService>();
builder.Services.AddScoped<ScriptGenerationService>();
// ...
app.MapPost("/api/videos/{id:guid}/script", async (Guid id, ScriptGenerationService service, CancellationToken ct) =>
{
    try
    {
        var job = await service.GenerateScriptAsync(id, ct);
        return Results.Ok(VideoJobResponse.FromDomain(job));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (LlmUnavailableException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (ScriptGenerationException ex)
    {
        return Results.UnprocessableEntity(new { error = ex.Message });
    }
})
    .WithName("GenerateVideoJobScript");
```

**Neden:** Roadmap'in "endpoint veya servis demosu" maddesi, mevcut `VideoJob` kaynağının **üzerinde bir eylem tetikleyen** bir endpoint olarak tasarlandı (`POST /api/videos/{id}/script`), yeni/bağımsız bir "prompt gönder" endpoint'i olarak değil. Bu seçim bilinçli: script üretimi her zaman bir job'ın yaşam döngüsünün parçası olmalı (`CLAUDE.md` §14'teki `POST /api/videos/{id}/render` ile aynı desen — var olan bir kaynağa eylem tetikleyen alt-route), kopuk bir "metin üret" demosu olarak değil.

**Nasıl çalışıyor:**
* Üç ayrı `catch` bloğu, bölüm 2 ve 3'teki iki farklı exception tipini **ve** bölüm 6'daki `KeyNotFoundException`'ı **üç farklı, anlamlı HTTP durum koduna** çeviriyor: 404 (kaynak yok), 503 (bağımlı servis kullanılamıyor — istemcinin hatası değil), 422 (istek geçerli ama işlenemedi — Day 1'deki 400'den farklı: 400 "senin gönderdiğin bozuk", 422 "gönderdiğin doğruydu ama sonuç üretilemedi").
* `Results.UnprocessableEntity(...)` — minimal API'nin 422 durum kodu için hazır yardımcı metodu (`Results.Ok`/`Results.NotFound` ile aynı aile).

---

## 8. `tests/ShortsGenerator.Domain.Tests/ScriptValidatorTests.cs`

```csharp
[Theory]
[InlineData("This will cure your bad knees completely.")]
[InlineData("We can diagnose your injury from this video.")]
[InlineData("This routine is a guaranteed results program.")]
public void Validate_ContainsDisallowedMedicalClaim_IsInvalid(string sentence)
{
    var narration = sentence + " " + string.Join(' ', Enumerable.Repeat("word", ScriptValidator.MinWords));

    var result = ScriptValidator.Validate(narration);

    Assert.False(result.IsValid);
    Assert.Contains(result.Reasons, r => r.Contains("disallowed medical-claim phrase", StringComparison.OrdinalIgnoreCase));
}
```

(Dosyanın tamamı 5 test içeriyor: boş metin, çok kısa, çok uzun, sınırlar içinde geçerli, ve yukarıdaki yasaklı-ifade testi.)

**Neden:** Bölüm 1'deki `ScriptValidator`'ın her kuralının (minimum/maksimum kelime, yasaklı kelimeler) ayrı ayrı doğru çalıştığını kanıtlamak.

**Nasıl çalışıyor:**
* `string.Join(' ', Enumerable.Repeat("word", ScriptValidator.MinWords))` — tam olarak `MinWords` kadar kelime içeren bir metin üretmenin pratik yolu; sabit bir sayı (`"40 kelime"`) yazmak yerine `ScriptValidator.MinWords`'ü kullanmak, ileride bu sabit değişirse testin **otomatik olarak** doğru sınırı test etmeye devam etmesini sağlıyor.
* `[Theory]` + `[InlineData(...)]` ile üç farklı yasaklı cümle, tek bir test metodunda kapsanıyor (Day 2'deki `Create_WithoutTopic_Throws` ile aynı desen).

---

## 9. İlk `ShortsGenerator.Application.Tests` projesi ve test çiftleri (fakes)

```
dotnet new xunit -n ShortsGenerator.Application.Tests -o tests/ShortsGenerator.Application.Tests
dotnet sln add tests/ShortsGenerator.Application.Tests/ShortsGenerator.Application.Tests.csproj
dotnet add tests/ShortsGenerator.Application.Tests reference src/ShortsGenerator.Application
```

```csharp
internal sealed class FakeLlmProvider : ILlmProvider
{
    private readonly Queue<object> _responses;

    public FakeLlmProvider(params object[] responses) => _responses = new Queue<object>(responses);

    public List<string> ReceivedPrompts { get; } = [];

    public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        ReceivedPrompts.Add(prompt);
        if (_responses.Count == 0) throw new InvalidOperationException("No more fake responses queued.");

        return _responses.Dequeue() switch
        {
            string text => Task.FromResult(text),
            Exception ex => Task.FromException<string>(ex),
            var other => throw new InvalidOperationException($"Unsupported fake response type: {other.GetType()}."),
        };
    }

    public Task<LlmAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new LlmAvailability(true, true, "fake"));
}
```

**Neden:** `ShortsGenerator.Application` katmanının ilk test projesi — bugüne kadar sadece Domain, Infrastructure ve Api test projeleri vardı. `ScriptGenerationService`'i test etmek için gerçek bir Ollama'ya **ve** gerçek bir SQLite'a ihtiyaç duymadan, tamamen izole, deterministik testler yazmak istedik. Day 3'teki `FakeHttpMessageHandler` felsefesinin devamı: üçüncü parti bir mocking kütüphanesi yerine, arayüzü uygulayan küçük, elle yazılmış bir sınıf.

**Nasıl çalışıyor:**
* `Queue<object> _responses` — her `GenerateAsync` çağrısında kuyruktaki bir sonraki öğeyi tüketiyor; bu sayede "ilk denemede kısa metin, ikinci denemede geçerli metin dönsün" gibi **sıralı, çok adımlı senaryolar** (bölüm 6'daki retry döngüsünü test etmek için tam ihtiyaç duyulan şey) tek bir fake ile kurulabiliyor.
* `_responses.Dequeue() switch { string text => ..., Exception ex => ..., ... }` — **tip deseni (type pattern) ile switch expression**: kuyruktaki öğe bir `string` ise başarılı bir `Task` döndürülüyor, bir `Exception` ise `Task.FromException<string>(ex)` ile **hata ile tamamlanmış** bir `Task` üretiliyor (bu, `GenerateScriptAsync`'in `await` ettiği noktada gerçek bir `throw` gibi davranıyor). Bu tek fake sınıf, hem "LLM başarılı cevap verdi" hem "LLM'e hiç ulaşılamadı" (`LlmUnavailableException` kuyruğa konarak) senaryolarını simüle edebiliyor.
* `ReceivedPrompts` — test sırasında gönderilen her prompt'u kaydediyor; bölüm 13'teki "ikinci deneme, önceki hatayı prompt'a eklemiş mi" testinin doğrulama noktası tam olarak bu liste.

`InMemoryVideoJobRepository` benzer mantıkla, `IVideoJobRepository`'yi bir `Dictionary<Guid, VideoJob>` üzerinde uyguluyor — gerçek bir veritabanına dokunmadan `AddAsync`/`GetByIdAsync`/`UpdateAsync`/`ListRecentAsync`'in beklenen sözleşmesini sağlıyor.

---

## 10. `ScriptPromptBuilderTests.cs` ve `ScriptTextCleanerTests.cs`

```csharp
[Fact]
public void Build_WithFailureReasons_IncludesThemForRepair()
{
    var prompt = ScriptPromptBuilder.Build("Running basics", ["Too short (10 words; needs at least 40)."]);

    Assert.Contains("previous attempt", prompt, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("Too short (10 words; needs at least 40).", prompt);
}
```

```csharp
[Theory]
[InlineData("Here's your script:\n\nTrain smart, not just hard.", "Train smart, not just hard.")]
[InlineData("Script: Train smart, not just hard.", "Train smart, not just hard.")]
[InlineData("Narration:\nTrain smart, not just hard.", "Train smart, not just hard.")]
public void Clean_RemovesKnownPreambles(string raw, string expected)
{
    var result = ScriptTextCleaner.Clean(raw);
    Assert.Equal(expected, result);
}
```

**Neden:** Bölüm 4 ve 5'teki iki **pure function** (dışarıdan hiçbir bağımlılığı olmayan, aynı girdi için her zaman aynı çıktıyı üreten fonksiyonlar) için fake'lere bile ihtiyaç duyulmadan, doğrudan çağrılıp test edilebiliyorlar — bu, "her şeyin DI servisi olması gerekmez" fikrinin somut kanıtı.

**Nasıl çalışıyor:** `["Too short (10 words; needs at least 40)."]` — C# 12 **collection expression**, `new List<string> { "..." }` ya da `new[] { "..." }` yazmaktan daha kısa; derleyici, hedef tipin (`IReadOnlyList<string>?`) ne olduğuna bakıp uygun koleksiyonu kendisi üretiyor.

---

## 11. `ScriptGenerationServiceTests.cs`

```csharp
[Fact]
public async Task GenerateScriptAsync_FirstAttemptTooShort_RetriesAndSucceeds()
{
    var repository = new InMemoryVideoJobRepository();
    var job = VideoJob.Create("Running basics", null, Now);
    await repository.AddAsync(job, CancellationToken.None);
    var llm = new FakeLlmProvider("too short", ValidNarration());
    var service = new ScriptGenerationService(llm, repository, TimeProvider.System);

    var result = await service.GenerateScriptAsync(job.Id, CancellationToken.None);

    Assert.Equal(VideoJobStatus.Narrating, result.Status);
    Assert.Equal(2, llm.ReceivedPrompts.Count);
    Assert.Contains("Too short", llm.ReceivedPrompts[1]);
}

[Fact]
public async Task GenerateScriptAsync_AllAttemptsInvalid_MarksJobFailedAndThrows()
{
    var repository = new InMemoryVideoJobRepository();
    var job = VideoJob.Create("Running basics", null, Now);
    await repository.AddAsync(job, CancellationToken.None);
    var llm = new FakeLlmProvider("too short", "still short", "still not enough words");
    var service = new ScriptGenerationService(llm, repository, TimeProvider.System);

    await Assert.ThrowsAsync<ScriptGenerationException>(
        () => service.GenerateScriptAsync(job.Id, CancellationToken.None));

    var persisted = await repository.GetByIdAsync(job.Id, CancellationToken.None);
    Assert.Equal(VideoJobStatus.Failed, persisted!.Status);
    Assert.Contains("after 3 attempts", persisted.ErrorMessage);
}
```

(Dosyanın tamamı 5 test: ilk denemede başarı, tekrar denemeyle başarı, tüm denemeler başarısız, Ollama erişilemez, bilinmeyen job id.)

**Neden:** Bölüm 6'daki `ScriptGenerationService`'in **tüm orkestrasyon mantığını** — happy path, retry, tükenme, dış servis hatası, bulunamayan kaynak — gerçek Ollama'ya hiç dokunmadan, saniyeler içinde kanıtlıyor.

**Nasıl çalışıyor:**
* `GenerateScriptAsync_FirstAttemptTooShort_RetriesAndSucceeds` — `FakeLlmProvider("too short", ValidNarration())` kuyruğa iki cevap koyuyor: ilki kısa (geçersiz), ikincisi geçerli. Test, hem son durumun (`Narrating`) doğru olduğunu **hem de** `ReceivedPrompts[1]`'in (ikinci prompt) içinde `"Too short"` ifadesinin geçtiğini doğruluyor — yani bölüm 6'daki `failureReasons` döngüsünün gerçekten bir sonraki prompt'a aktarıldığını, sadece sonucun doğru olduğunu değil, **mekanizmanın** doğru çalıştığını kanıtlıyor.
* `GenerateScriptAsync_AllAttemptsInvalid_MarksJobFailedAndThrows` — `Assert.ThrowsAsync<ScriptGenerationException>(() => ...)` ile exception'ın fırlatıldığını doğruladıktan **sonra**, `repository.GetByIdAsync` ile job'ı tekrar okuyup `Status`'un gerçekten `Failed`'e, `ErrorMessage`'ın da anlamlı bir mesaja güncellendiğini kontrol ediyor — sadece "exception fırlatıldı mı" değil, "yan etki (veritabanı güncellemesi) gerçekten oldu mu" sorusunu da test ediyor.
* Üç fake cevabın üçü de (`"too short"`, `"still short"`, `"still not enough words"`) `ScriptValidator.MinWords` (40) kelimenin çok altında — `MaxAttempts` (3) ile birebir eşleşen sayıda geçersiz cevap vererek döngünün tam olarak tükenme senaryosunu tetikliyor.

---

## Doğrulanan davranış

```
dotnet build  → 0 Uyarı, 0 Hata (9 proje)
dotnet test   → Domain.Tests: 20 Başarılı | Application.Tests: 16 Başarılı |
                Infrastructure.Tests: 9 Başarılı | Api.Tests: 1 Başarılı
                Toplam: 46 Başarısız: 0

Gerçek Ollama + llama3.2:3b'ye karşı uçtan uca manuel doğrulama:
POST /api/videos  {"topic":"Why Your Pull-Ups Aren't Increasing"}
  → 201, status: "Created"

POST /api/videos/{id}/script
  → 200, status: "Narrating", generatedScript (110 kelime, konuya uygun, güvenli):
    "If you're struggling to increase your pull-ups, it might be because your
     form is off. Are you swinging or using momentum to lift yourself up? ...
     focus on building your overall upper body strength through other
     exercises like push-ups and rows."
  (ilk denemede geçti — tekrar deneme döngüsü tetiklenmedi)

GET /api/videos/{id}
  → 200, aynı generatedScript — SQLite'tan gerçekten geri okundu.

POST /api/videos/00000000-0000-0000-0000-000000000000/script
  → 404
```

Bu çıktı, bugünün tüm parçalarının gerçek bir LLM'e karşı da doğru çalıştığını kanıtlıyor: Ollama'nın ürettiği gerçek metin `ScriptValidator`'dan geçti (110 kelime, 40-130 aralığında, yasaklı kelime yok), `VideoJob` doğru şekilde `Created` → `Planning` → `Narrating` geçişini yaptı, ve bu durum SQLite'a kalıcı olarak yazıldı (farklı bir HTTP isteğinde geri okunabildi).
