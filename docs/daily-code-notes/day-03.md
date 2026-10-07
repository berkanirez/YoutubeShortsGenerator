# Day 3 — Kod Notları

30 günlük roadmap, Gün 3. Konu: Ollama'nın yerel makineye kurulması, `ILlmProvider` soyutlaması, `IHttpClientFactory` ile tip güvenli (typed) HTTP client, Options pattern ile konfigürasyon, ve "Ollama kapalı/model yok" durumlarında net hata üretimi.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. Donanım kontrolü ve Ollama kurulumu

```
RAM: 15.7 GB
CPU: 12th Gen Intel(R) Core(TM) i5-12450H | 8 Core | 12 Logical Processor
GPU: NVIDIA GeForce RTX 3050 Laptop GPU (4 GB VRAM), Intel UHD Graphics (1 GB)

winget search Ollama
winget install --id Ollama.Ollama -e --accept-source-agreements --accept-package-agreements
→ Downloading https://github.com/ollama/ollama/releases/download/v0.35.1/OllamaSetup.exe
→ Successfully installed

ollama pull llama3.2:3b
→ 2.0 GB indirildi
```

**Neden:** `CLAUDE.md` açıkça "varsayılan modeli seçmeden önce kullanıcının donanımını ve kurulu Ollama modellerini incele" diyor. Bu makinede Ollama hiç kurulu değildi (Day 1'de doğrulanmıştı), bu yüzden önce donanıma bakıldı: 4 GB VRAM'lik bir laptop GPU'su, 7B+ parametreli bir modeli rahat çalıştıramaz (ya tamamen CPU'ya düşer ya da çok yavaşlar); 16 GB sistem RAM'i ise CPU fallback için yeterli ama hız kritik değilse. Bu profile göre **3B parametreli** bir model (`llama3.2:3b`, ~2 GB) mantıklı bir varsayılan: GPU'ya rahat sığıyor, CPU'da bile makul hızda çalışıyor, ve script üretimi gibi görece basit bir görev için yeterli kalitede.

**Nasıl çalışıyor:**
* `winget` (Windows Package Manager), Microsoft'un Windows için paket yöneticisi — `apt`/`brew`'in Windows karşılığı. `winget search Ollama`, Ollama'nın resmi paketinin adını (`Ollama.Ollama`) buldu; `winget install -e` ("exact match") o paketi indirip kurdu. Bu, Ollama'yı hem komut satırı aracı (`ollama`) hem arka planda sürekli çalışan bir servis (tepsi simgesi + `http://localhost:11434` üzerinde dinleyen bir HTTP sunucusu) olarak kuruyor.
* `ollama pull llama3.2:3b` — modelin ağırlıklarını (weights) Meta'nın resmi deposundan indirip yerel diske kaydediyor; bundan sonra hiç internete çıkmadan bu modelle konuşulabiliyor (zero-cost prensibiyle tam uyumlu — tek seferlik indirme dışında hiçbir ücret yok).
* Kurulumdan hemen sonra aynı terminal oturumunda `ollama` komutu tanınmadı (`command not found`) — bunun sebebi, kurulumun PATH ortam değişkenine yeni bir klasör eklemiş olması, ama o an açık olan kabuğun (shell) bu değişikliği henüz okumamış olması. `ollama.exe`'yi tam yoluyla (`%LOCALAPPDATA%\Programs\Ollama\ollama.exe`) çağırmak bu sorunu aşmanın en basit yolu oldu — PATH'in yenilenmesini beklemeye gerek kalmadı.

---

## 2. Gerçek bağlantı testi (kod yazılmadan önce)

```
curl http://localhost:11434/api/generate -d '{"model":"llama3.2:3b","prompt":"Say the word OK and nothing else.","stream":false}'
→ {"model":"llama3.2:3b","response":"OK","done":true,...}
```

**Neden:** Kod yazmadan önce, Ollama'nın HTTP API'sinin gerçekten nasıl bir istek beklediğini ve nasıl bir cevap döndürdüğünü elle görmek, `OllamaLlmProvider`'ı (bölüm 8) yazarken "acaba field adı `prompt` mı `input` mı" gibi varsayımlarla değil, gerçek bir örnekle çalışmayı sağlıyor.

**Nasıl çalışıyor:** Ollama'nın `/api/generate` endpoint'i düz JSON kabul ediyor: `model` (hangi model), `prompt` (girdi metni), `stream` (`true` ise cevap parça parça/NDJSON akışı olarak gelir — biz `false` verdik, tek bir JSON nesnesi olarak tam cevabı bekledik, daha basit). Cevaptaki `response` alanı, modelin ürettiği metin.

---

## 3. `src/ShortsGenerator.Domain/ILlmProvider.cs`

```csharp
namespace ShortsGenerator.Domain;

public interface ILlmProvider
{
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken);

    Task<LlmAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);
}
```

**Neden:** `CLAUDE.md` §8 ve §21 açıkça bir `ILlmProvider` arayüzü istiyor — "uygulamayı tek bir modele/sağlayıcıya sabitleme" kuralı. Bugün Ollama kullanıyoruz, ama bu arayüz sayesinde ileride (hiç planlanmasa da) başka bir sağlayıcıya geçilmek istenirse, `Api`/`Application` katmanlarının **tek satırı bile değişmeden**, sadece yeni bir `ILlmProvider` implementasyonu DI'da kayıtlı Ollama'nınkinin yerine konur.

**Nasıl çalışıyor:**
* Bu arayüz Day 2'deki `IVideoJobRepository` ile aynı yere, **Domain** projesine kondu. İlk bakışta tuhaf gelebilir — Ollama bir veritabanı değil, dışarıdaki bir HTTP servisi. Ama projenin bugüne kadarki kuralı şu: "Domain, dışarıdan ne beklediğini tanımlar; Infrastructure bunu nasıl sağladığını bilir." Bu kural hem kalıcılık hem dış servisler için aynı şekilde geçerli — ikisi de "Domain'in kontrol edemediği, dışarıdan gelen bir yetenek". Bu tutarlılık, projede "bu sözleşme nerede tanımlanır" sorusunun her zaman aynı cevaba sahip olmasını sağlıyor.
* İki metot: `GenerateAsync` (asıl iş — bir prompt ver, metin al) ve `CheckAvailabilityAsync` (roadmap'in istediği "health check" — Ollama'ya hiç prompt göndermeden sadece "ayakta mısın, model yüklü mü" diye sormak).
* Bugün için `GenerateAsync` kasıtlı olarak çok sade: ham bir `string` prompt alıp ham bir `string` cevap döndürüyor. Day 4-5'te script/scene üretimi için gerekecek olan "sistem promptu", "JSON modu", "yapılandırılmış çıktı" gibi kavramlar bilinçli olarak bugüne eklenmedi — roadmap'in kendisi "bugün sadece küçük bir bağlantı testi, yapılandırılmış sahne üretimi yok" diyor.

---

## 4. `src/ShortsGenerator.Domain/LlmAvailability.cs`

```csharp
namespace ShortsGenerator.Domain;

public sealed record LlmAvailability(bool IsReachable, bool IsModelAvailable, string Detail);
```

**Neden:** `CheckAvailabilityAsync`'in dönüş tipi. Tek bir `bool` yeterli olmazdı çünkü iki farklı başarısızlık modu var ve bunlar **farklı eylemler** gerektiriyor: Ollama hiç ayakta değilse kullanıcı Ollama'yı başlatmalı; Ollama ayakta ama model çekilmemişse kullanıcı `ollama pull` çalıştırmalı. Bu iki durumu tek bir `bool`'a sıkıştırmak, hangi eylemin gerektiğini anlamayı zorlaştırırdı.

**Nasıl çalışıyor:** Day 2'nin `SkillSnapshot`/`HealthResponse` gibi positional record'larıyla aynı syntax — üç salt-okunur alan, otomatik `Equals`/`ToString`. `Detail`, insan tarafından okunabilir, doğrudan API cevabında veya loglarda gösterilebilecek bir açıklama metni taşıyor (örneğin "Ollama is reachable but 'llama3.2:3b' is not pulled. Run: ollama pull llama3.2:3b" — çözümü de söylüyor).

---

## 5. `src/ShortsGenerator.Domain/LlmUnavailableException.cs`

```csharp
namespace ShortsGenerator.Domain;

public sealed class LlmUnavailableException : Exception
{
    public LlmUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
```

**Neden:** `GenerateAsync` çağrılırken Ollama'ya hiç ulaşılamazsa ya da model yüklü değilse, çıplak bir `HttpRequestException` ya da `JsonException` fırlatmak yerine, **anlamlı, bu projeye özgü** bir exception tipi fırlatılıyor. Bu, `CLAUDE.md` §16'nın "Ollama not running", "model not installed" için "actionable messages rather than raw stack traces" kuralının doğrudan karşılığı. Ayrıca `Program.cs`'teki endpoint'ler (bölüm 11) bu özel tipi `catch` ederek 500 yerine anlamlı bir 503 dönebiliyor — generic bir exception'ı yakalasaydık, beklenmeyen başka hataları da (örneğin bir programlama hatası) yanlışlıkla "Ollama kapalı" gibi yorumlama riski olurdu.

**Nasıl çalışıyor:** Standart bir custom exception kalıbı — `Exception`'dan türüyor, `message` ve opsiyonel bir `innerException` (asıl, düşük seviyeli hatayı sarmalamak için — örneğin bir `HttpRequestException`'ı burada saklayıp loglarda tam yığın izini (stack trace) kaybetmemek) alıyor.

---

## 6. `src/ShortsGenerator.Infrastructure/Llm/OllamaOptions.cs`

```csharp
namespace ShortsGenerator.Infrastructure.Llm;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "llama3.2:3b";
}
```

**Neden:** `CLAUDE.md` §22 "options pattern for configuration" kuralının karşılığı — Ollama'nın adresi ve kullanılacak model adı, koda gömülmek yerine `appsettings.json`'dan (bölüm 10) okunuyor. Bu sayede model değiştirmek (örneğin daha sonra `qwen2.5:7b`'ye geçmek) tek bir config satırını değiştirmek demek, **hiçbir C# dosyasına dokunmadan**.

**Nasıl çalışıyor:**
* `public const string SectionName = "Ollama";` — `appsettings.json`'daki bölüm adını (bölüm 10'daki `"Ollama": { ... }`) burada, tek bir yerde sabitliyor; bu sabiti hem `.Configure<OllamaOptions>(...)` çağrısında (bölüm 9) hem de ileride başka bir yerde kullanırken, aynı string'i elle iki kez yazıp birini yanlış yazma riskini ortadan kaldırıyor.
* `{ get; set; }` kullanıldı (Day 2'deki `VideoJob`'ın aksine `private set` değil) — çünkü bu bir **options sınıfı**: .NET'in konfigürasyon binding mekanizması (`IConfiguration.Bind`/`Configure<T>`), bu property'leri `appsettings.json`'daki değerlerle **dışarıdan** dolduruyor; bu yüzden public setter'lara ihtiyaç var. Bu, "entity'ler kendi değişmezliğini korur, options sınıfları ise dışarıdan doldurulmak için var" ayrımının bir örneği.
* Varsayılan değerler (`= "http://localhost:11434"`, `= "llama3.2:3b"`) — `appsettings.json`'da bu bölüm hiç olmasa bile uygulamanın makul bir varsayılanla çalışabilmesini sağlıyor (savunmacı programlama değil, geliştirici deneyimini kolaylaştırma amaçlı).

---

## 7. `src/ShortsGenerator.Infrastructure/Llm/OllamaLlmProvider.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Llm;

public sealed class OllamaLlmProvider : ILlmProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;

    public OllamaLlmProvider(HttpClient httpClient, IOptions<OllamaOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(prompt));
        }

        var request = new OllamaGenerateRequest(_options.Model, prompt, Stream: false);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("/api/generate", request, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmUnavailableException(
                $"Could not reach Ollama at {_httpClient.BaseAddress}. Is it running? " +
                "Start it from the Ollama app, or run 'ollama serve'.",
                ex);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new LlmUnavailableException(
                $"Ollama model '{_options.Model}' is not available. Pull it first with " +
                $"'ollama pull {_options.Model}'. Ollama said: {errorBody}");
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions, cancellationToken);
        return payload?.Response ?? string.Empty;
    }

    public async Task<LlmAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        OllamaTagsResponse? tags;
        try
        {
            tags = await _httpClient.GetFromJsonAsync<OllamaTagsResponse>("/api/tags", JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return new LlmAvailability(
                IsReachable: false,
                IsModelAvailable: false,
                Detail: $"Could not reach Ollama at {_httpClient.BaseAddress}: {ex.Message}");
        }

        var models = tags?.Models ?? [];
        var isModelAvailable = models.Any(m =>
            m.Name.Equals(_options.Model, StringComparison.OrdinalIgnoreCase) ||
            m.Name.StartsWith(_options.Model + ":", StringComparison.OrdinalIgnoreCase));

        return isModelAvailable
            ? new LlmAvailability(true, true, $"Ollama is reachable and '{_options.Model}' is pulled.")
            : new LlmAvailability(true, false, $"Ollama is reachable but '{_options.Model}' is not pulled. Run: ollama pull {_options.Model}");
    }
}

internal sealed record OllamaGenerateRequest(string Model, string Prompt, bool Stream);
internal sealed record OllamaGenerateResponse(string Response);
internal sealed record OllamaTagsResponse(List<OllamaModelInfo> Models);
internal sealed record OllamaModelInfo(string Name);
```

**Neden:** Bu, `ILlmProvider`'ın Ollama'ya özel gerçek implementasyonu — bugünün en önemli dosyası. İki metodun da amacı net: `GenerateAsync` gerçek işi yapıyor, `CheckAvailabilityAsync` roadmap'in istediği health-check'i sağlıyor. Hata yönetimi bilinçli olarak iki ayrı, spesifik senaryoya ayrıldı (bağlantı hatası vs. model bulunamadı) çünkü ikisinin kullanıcıya söyleyeceği şey farklı.

**Nasıl çalışıyor:**
* `private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);` — `JsonSerializerDefaults.Web`, hem camelCase isimlendirme (`Model` → `model`, Ollama'nın beklediği format) hem de büyük/küçük harf duyarsız okuma davranışını tek seferde açıyor. `static readonly` olması, her çağrıda yeniden oluşturulmasını önlemek için (küçük ama gerçek bir performans detayı — `JsonSerializerOptions` nesneleri önbelleğe alınmak üzere tasarlanmıştır).
* `public OllamaLlmProvider(HttpClient httpClient, IOptions<OllamaOptions> options)` — **iki farklı DI deseni** aynı constructor'da: `HttpClient`, bölüm 9'daki `AddHttpClient<ILlmProvider, OllamaLlmProvider>(...)` çağrısı sayesinde DI container tarafından **otomatik olarak, doğru `BaseAddress` ve `Timeout` önceden ayarlanmış şekilde** enjekte ediliyor (typed client deseni — `new HttpClient()` hiçbir yerde yazılmadı). `IOptions<OllamaOptions>` ise Options pattern'in standart enjeksiyon şekli; `.Value` ile içindeki gerçek `OllamaOptions` nesnesine erişiliyor.
* ```csharp
  try
  {
      response = await _httpClient.PostAsJsonAsync("/api/generate", request, JsonOptions, cancellationToken);
  }
  catch (HttpRequestException ex)
  {
      throw new LlmUnavailableException(...);
  }
  ```
  `PostAsJsonAsync`, `System.Net.Http.Json`'dan gelen bir extension metodu: verilen nesneyi (`request`) otomatik olarak JSON'a serileştirip POST isteğinin gövdesine koyuyor — elle `JsonSerializer.Serialize(...)` + `StringContent` yazmaya gerek yok. `HttpRequestException`, Ollama'nın portu hiç dinlemiyorsa (servis kapalı) .NET'in fırlattığı exception — bu yakalanıp bölüm 5'teki `LlmUnavailableException`'a **sarmalanıyor** (`ex` ikinci parametre olarak `innerException`'a veriliyor, orijinal hata bilgisi kaybolmuyor).
  * `if (response.StatusCode == HttpStatusCode.NotFound) { ... }` — Ollama, istenen model hiç çekilmemişse `/api/generate`'e 404 döndürüyor (gövdesinde `{"error": "model '...' not found, try pulling it first"}` gibi bir JSON ile). Bu, "bağlantı var ama model yok" durumunun ayırt edici işareti; `errorBody` okunup kullanıcıya hem bizim ürettiğimiz net mesaj hem Ollama'nın kendi orijinal hata metni birlikte veriliyor.
  * `response.EnsureSuccessStatusCode();` — yukarıdaki iki özel durumun (bağlantı hatası, 404) dışında kalan başka bir hata kodu (örneğin 500) gelirse, burada genel bir `HttpRequestException` fırlatılıyor — bunu özel olarak ele almadık çünkü "Ollama'nın kendi iç hatası" gibi öngörülemeyen durumlar için generic bir hata, sessizce yutmaktan daha doğru.
  * `var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(...); return payload?.Response ?? string.Empty;` — cevap gövdesini otomatik olarak `OllamaGenerateResponse` tipine deserialize ediyor, `Response` alanını döndürüyor. `?? string.Empty` — teorik olarak `payload` `null` gelirse (boş gövde), çağıran tarafı `null` kontrolü yapmaya zorlamak yerine boş string ile devam ediyoruz (bu metodun imzası zaten `Task<string>`, `Task<string?>` değil).
* `CheckAvailabilityAsync` — `/api/tags` endpoint'ini çağırıyor (Ollama'nın "kurulu modelleri listele" endpoint'i, prompt göndermeden). `models.Any(m => m.Name.Equals(_options.Model, ...) || m.Name.StartsWith(_options.Model + ":", ...))` — iki koşul var çünkü Ollama'daki model adları bazen tag'siz (`llama3.2`) bazen tag'li (`llama3.2:3b`) listelenebiliyor; `StartsWith` ile `"llama3.2"` konfigürasyonu `"llama3.2:3b"` kaydıyla da eşleşebilsin diye eklendi.
* `internal sealed record OllamaGenerateRequest(...)` ve diğer üç DTO — Ollama'nın HTTP sözleşmesinin **sadece bu dosyaya özel**, dışarıya hiç sızmayan ham temsilleri. `internal` erişim belirleyici bunu açıkça işaretliyor: `Api` projesi (ya da başka hiçbir proje) bu tipleri hiç göremez/kullanamaz — Ollama'nın JSON şekli, projenin geri kalanından tamamen izole.

---

## 8. `src/ShortsGenerator.Infrastructure/Llm/LlmServiceCollectionExtensions.cs` ve eksik paketler

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Llm;

public static class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddOllamaLlmProvider(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(OllamaOptions.SectionName);
        services.Configure<OllamaOptions>(section);

        var baseUrl = section[nameof(OllamaOptions.BaseUrl)] ?? new OllamaOptions().BaseUrl;

        services.AddHttpClient<ILlmProvider, OllamaLlmProvider>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            // Local, possibly CPU-only generation can be slow; don't time out too eagerly.
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        return services;
    }
}
```

İlk `dotnet build` denemesinde iki derleme hatası çıktı:

```
error CS1503: 'IConfigurationSection' öğesinden 'Action<OllamaOptions>' öğesine dönüştürülemiyor
error CS1061: 'IServiceCollection' bir 'AddHttpClient' tanımı içermiyor
```

düzeltme:

```
dotnet add src/ShortsGenerator.Infrastructure package Microsoft.Extensions.Options.ConfigurationExtensions
dotnet add src/ShortsGenerator.Infrastructure package Microsoft.Extensions.Http
```

**Neden:** Day 2'deki `AddInfrastructure`'ın aynısı — Infrastructure'a özgü üç-beş satırlık DI kaydını tek bir extension metoduna topluyoruz. Eksik paket hatası ise gerçek, öğretici bir an: bu proje (`ShortsGenerator.Infrastructure`) sade bir class library (`Microsoft.NET.Sdk`), ASP.NET Core'un "shared framework"üne (Api projesinin otomatik sahip olduğu geniş paket kümesine) dahil değil. Bu yüzden `IConfiguration`'dan bir options sınıfına **bağlama** (`Configure<T>(IConfigurationSection)`) ve `AddHttpClient` gibi ASP.NET Core dünyasında "zaten var" sanılan metotlar, aslında ayrı NuGet paketlerinden geliyor ve class library'lerde elle eklenmesi gerekiyor.

**Nasıl çalışıyor:**
* `Microsoft.Extensions.Options.ConfigurationExtensions` — `IServiceCollection.Configure<TOptions>(IConfigurationSection)` overload'ının yaşadığı paket (bu, `Configure<TOptions>(Action<TOptions>)` overload'ından farklı bir paket — biri `Microsoft.Extensions.Options`'ta, diğeri bunda).
* `Microsoft.Extensions.Http` — `IServiceCollection.AddHttpClient(...)` ve `IHttpClientFactory` altyapısının yaşadığı paket.
* `var section = configuration.GetSection(OllamaOptions.SectionName); services.Configure<OllamaOptions>(section);` — `appsettings.json`'daki `"Ollama": { ... }` bölümünü okuyup, her property ismi eşleşen `OllamaOptions` alanlarına otomatik atıyor (`BaseUrl` → `"BaseUrl"`, `Model` → `"Model"`, isim eşleşmesi case-insensitive). Bundan sonra herhangi bir yerde `IOptions<OllamaOptions>` enjekte eden bir sınıf (bölüm 7'deki `OllamaLlmProvider` gibi), bu değerlere erişebiliyor.
* `var baseUrl = section[nameof(OllamaOptions.BaseUrl)] ?? new OllamaOptions().BaseUrl;` — `HttpClient.BaseAddress`'i, DI container tam kurulmadan (yani `IOptions<OllamaOptions>` henüz enjekte edilemeden), **şimdi, bu extension metodu çalışırken** okumak gerekiyor çünkü `AddHttpClient`'ın yapılandırma lambda'sı DI container inşa edilme sürecinin bir parçası. Bu yüzden `IConfiguration`'dan doğrudan okundu; `?? new OllamaOptions().BaseUrl` ile `appsettings.json`'da hiç `BaseUrl` yazılmamışsa bile `OllamaOptions`'ın kendi varsayılanı (bölüm 6) kullanılıyor — iki farklı yerde "varsayılan URL" tekrar yazılmak yerine tek kaynaktan (`OllamaOptions`'ın kendisi) alınıyor.
* `services.AddHttpClient<ILlmProvider, OllamaLlmProvider>(client => { ... });` — bu, **typed client** deseni: `ILlmProvider` arayüzü istenen her yerde, DI container `OllamaLlmProvider`'ı üretip, içine burada yapılandırılmış (doğru `BaseAddress`/`Timeout`) bir `HttpClient` zaten enjekte edilmiş halde veriyor. Bunun klasik `new HttpClient()` yazmaya göre asıl avantajı: `IHttpClientFactory`, alttaki TCP bağlantılarını **havuzlayıp yeniden kullanıyor** ve DNS değişikliklerine karşı periyodik olarak bağlantıları tazeliyor — `HttpClient`'ı doğrudan `new`'lemek (özellikle `using` ile her istekte) uzun vadede soket tükenmesine (socket exhaustion) yol açabilen, .NET'te iyi bilinen bir tuzak.
* `client.Timeout = TimeSpan.FromMinutes(2);` — `HttpClient`'ın varsayılan timeout'u 100 saniye; yerel, CPU üzerinde çalışan bir model (GPU'ya sığmayan daha büyük bir prompt/model durumunda) bundan daha uzun sürebilir. Bu, "zero-cost yerel çalışma, hız garantisi değil" gerçeğine karşı bilinçli bir tolerans.

---

## 9. `src/ShortsGenerator.Api/appsettings.json` — Ollama bölümü

```json
{
  "ConnectionStrings": { "Default": "Data Source=shortsgenerator.db" },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3.2:3b"
  }
}
```

**Neden/Nasıl çalışıyor:** Bölüm 6-8'deki her şeyin okuduğu gerçek değerler burada. `BaseUrl`, Ollama'nın varsayılan portu (11434); `Model`, bölüm 1'de donanıma göre seçilen `llama3.2:3b`. Modeli değiştirmek isteyen biri (örneğin daha güçlü bir makineye geçilirse) sadece bu dosyadaki tek bir string'i değiştirir — hiçbir C# dosyasına dokunmaz.

---

## 10. `src/ShortsGenerator.Api/Program.cs` — DI kaydı ve iki yeni endpoint

```csharp
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Llm;
// ...
builder.Services.AddOllamaLlmProvider(builder.Configuration);
// ...
app.MapGet("/api/llm/health", async (ILlmProvider llm, CancellationToken ct) =>
{
    var availability = await llm.CheckAvailabilityAsync(ct);
    return availability is { IsReachable: true, IsModelAvailable: true }
        ? Results.Ok(availability)
        : Results.Json(availability, statusCode: StatusCodes.Status503ServiceUnavailable);
})
    .WithName("GetLlmHealth");

app.MapPost("/api/llm/ping", async (ILlmProvider llm, CancellationToken ct) =>
{
    try
    {
        var text = await llm.GenerateAsync("Reply with exactly one word: OK.", ct);
        return Results.Ok(new { response = text });
    }
    catch (LlmUnavailableException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
    .WithName("PingLlm");
```

**Neden:** Roadmap'in Day 3 kabul kriteri ("API, local Ollama'ya zararsız bir prompt gönderip metin alabilsin") ve "health check" maddesi, Swagger üzerinden elle görülebilir, gerçek iki endpoint olarak hayata geçti.

**Nasıl çalışıyor:**
* `availability is { IsReachable: true, IsModelAvailable: true }` — **property pattern matching**: `LlmAvailability` record'unun her iki alanının da `true` olup olmadığını, `availability.IsReachable && availability.IsModelAvailable` yazmaya göre daha deyimsel (idiomatic) bir şekilde kontrol ediyor. İkisi de doğruysa 200, değilse (hangisi yanlışsa fark etmez) 503 + detayları içeren gövde dönüyor.
* `Results.Json(availability, statusCode: StatusCodes.Status503ServiceUnavailable)` — `Results.Ok`'un aksine, burada hem özel bir durum kodu (503 — "servis şu an kullanılamıyor", bir istemci hatası olan 400'den farklı, "benim tarafımda değil, bağımlı olduğum bir şey çalışmıyor" anlamı) hem de gövde aynı anda veriliyor.
* `catch (LlmUnavailableException ex)` — bölüm 5'teki özel exception tipi burada yakalanıyor; `GenerateAsync` içindeki `ArgumentException` (boş prompt) bilerek yakalanmadı çünkü bu endpoint'te prompt sabit ve asla boş değil — o kontrol ileride (Day 4'te kullanıcıdan gelen promptlar için) devreye girecek.

---

## 11. `tests/ShortsGenerator.Infrastructure.Tests/FakeHttpMessageHandler.cs`

```csharp
namespace ShortsGenerator.Infrastructure.Tests;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(_responder(request));
}
```

**Neden:** `OllamaLlmProvider`'ı test etmek için gerçek bir Ollama sunucusuna ihtiyaç duymak istemedik — testler o zaman hem yavaş olurdu hem de Ollama kurulu olmayan başka bir makinede (örneğin ileride bir CI sunucusunda) kırmızı olurdu. Üçüncü parti bir mocking kütüphanesi (Moq, NSubstitute) eklemek yerine (zero-cost/sade bağımlılık ilkesiyle uyumlu, ve zaten sadece tek bir sınıf için gerekiyor), `HttpMessageHandler`'ın **kendisini** taklit eden çok küçük, elle yazılmış bir sınıf yeterli oldu.

**Nasıl çalışıyor:** `HttpClient`, gerçek ağ çağrılarını aslında kendisi yapmaz — çağrıyı içindeki bir `HttpMessageHandler`'a devreder (varsayılan olarak gerçek ağa çıkan `SocketsHttpHandler`). Bu sınıf, o handler'ın yerine geçiyor: `SendAsync` çağrıldığında gerçek bir ağ isteği atmak yerine, test tarafından verilen `_responder` fonksiyonunu çalıştırıp onun döndürdüğü (ya da fırlattığı) sonucu veriyor. `new HttpClient(new FakeHttpMessageHandler(...))` (bölüm 12'de kullanılıyor) ile oluşturulan bir `HttpClient`, dışarıdan bakıldığında normal bir `HttpClient` gibi davranıyor ama hiçbir paket ağa çıkmıyor.

---

## 12. `tests/ShortsGenerator.Infrastructure.Tests/OllamaLlmProviderTests.cs`

```csharp
public class OllamaLlmProviderTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsResponseText_OnSuccess()
    {
        var provider = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, new { response = "OK" }));
        var result = await provider.GenerateAsync("Say OK", CancellationToken.None);
        Assert.Equal("OK", result);
    }

    [Fact]
    public async Task GenerateAsync_Throws_WhenOllamaUnreachable()
    {
        var provider = CreateProvider(_ => throw new HttpRequestException("Connection refused"));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => provider.GenerateAsync("Say OK", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_Throws_WhenModelNotFound()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"error\":\"model 'llama3.2:3b' not found\"}", Encoding.UTF8, "application/json"),
        });

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => provider.GenerateAsync("Say OK", CancellationToken.None));
        Assert.Contains("pull", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static OllamaLlmProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new FakeHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("http://localhost:11434"),
        };
        var options = Options.Create(new OllamaOptions { Model = "llama3.2:3b" });
        return new OllamaLlmProvider(httpClient, options);
    }
}
```

(Dosyanın tamamı 6 test içeriyor; `CheckAvailabilityAsync` için model-mevcut/model-eksik/erişilemez senaryoları da aynı desenle devam ediyor.)

**Neden:** Bölüm 7'deki `OllamaLlmProvider`'ın üç ayrı davranışını (başarı, bağlantı hatası, model bulunamadı) gerçek bir Ollama'ya dokunmadan, deterministik olarak kanıtlıyor. `GenerateAsync_Throws_WhenModelNotFound`, özellikle bölüm 7'deki 404 özel durumunun gerçekten `LlmUnavailableException`'a, "pull" kelimesini içeren bir mesajla çevrildiğini doğruluyor.

**Nasıl çalışıyor:**
* `CreateProvider(_ => throw new HttpRequestException("Connection refused"));` — `_responder` fonksiyonu burada bir değer döndürmek yerine **doğrudan fırlatıyor**; bölüm 11'deki `FakeHttpMessageHandler.SendAsync` bu fonksiyonu çağırdığı an exception senkron olarak fırlatılıyor, bu da gerçek bir ağ hatasının `HttpClient` tarafında nasıl göründüğünü birebir taklit ediyor — `OllamaLlmProvider`'ın `catch (HttpRequestException ex)` bloğu bunu gerçek bir bağlantı kopukluğundan ayırt edemiyor (ayırt etmesine de gerek yok).
* `Options.Create(new OllamaOptions { Model = "llama3.2:3b" })` — `IOptions<T>`'i elle, DI container kurmadan üretmenin standart test yolu; `Options.Create(...)`, verilen nesneyi doğrudan `.Value` olarak saran minimal bir `IOptions<T>` implementasyonu döndürüyor.
* Üç testin üçü de aynı `CreateProvider` yardımcı metodunu, sadece farklı bir `responder` lambda'sıyla çağırıyor — Day 2'nin `AdvanceTo` yardımcı metodundaki gibi, test kurulumundaki tekrarı tek bir yere topluyor.

---

## Doğrulanan davranış

```
dotnet build  → 0 Uyarı, 0 Hata (7 proje)
dotnet test   → Domain.Tests: 13 Başarılı | Infrastructure.Tests: 9 Başarılı | Api.Tests: 1 Başarılı
                Toplam: 23 Başarısız: 0

Gerçek, çalışan Ollama + llama3.2:3b'ye karşı manuel doğrulama (dotnet run):
GET  /api/llm/health
  → 200 {"isReachable":true,"isModelAvailable":true,
          "detail":"Ollama is reachable and 'llama3.2:3b' is pulled."}
POST /api/llm/ping
  → 200 {"response":"OK."}

Ollama süreçleri durdurulup (Stop-Process) AYNI iki endpoint tekrar çağrıldı:
GET  /api/llm/health → 503 {"isReachable":false,"isModelAvailable":false,
                             "detail":"Could not reach Ollama at http://localhost:11434/: ..."}
POST /api/llm/ping   → 503 {"error":"Could not reach Ollama at http://localhost:11434/. Is it
                             running? Start it from the Ollama app, or run 'ollama serve'."}

Ollama 'ollama.exe serve' ile yeniden başlatılıp erişilebilir olduğu doğrulandı.
```

Bu çıktı iki ayrı şeyi kanıtlıyor: (1) `OllamaLlmProvider`'ın **gerçek** bir Ollama sürecine karşı, unit testlerde sahte `HttpMessageHandler` ile doğrulanan davranışın birebir aynısını üretmesi — yani sahte testler gerçeği doğru taklit etmiş; (2) Ollama bilerek kapatıldığında API'nin çökmek ya da anlamsız bir 500/stack trace dönmek yerine, roadmap'in istediği gibi **net, eyleme geçirilebilir** bir 503 hata mesajı vermesi — `CLAUDE.md` §16'nın "Ollama not running" senaryosu bugün gerçekten tetiklenip doğru şekilde ele alındı.
