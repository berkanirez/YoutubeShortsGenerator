# Day 1 — Kod Notları

30 günlük roadmap, Gün 1. Konu: geliştirme ortamının doğrulanması, Git deposunun başlatılması, .NET çözüm/proje yapısı (solution/project), ilk ASP.NET Core Web API iskeleti ve xUnit ile ilk otomatik test.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem, ne için kullanılacağı) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. Ortam doğrulama

```
git --version              → git version 2.54.0.windows.1
dotnet --version           → 10.0.301
dotnet --list-sdks         → 10.0.301 [C:\Program Files\dotnet\sdk]
node --version              → v24.15.0
npm --version                → 11.12.1
python --version            → Python 3.14.5
ffmpeg -version              → bulunamadı
ollama --version             → bulunamadı
```

**Neden:** Herhangi bir kod yazmadan önce, projenin ihtiyaç duyacağı araçların makinede gerçekten kurulu olup olmadığını kontrol etmek gerekiyor. `CLAUDE.md`'deki proje kuralları .NET 10 LTS, Node/Remotion (ileride), Python (ileride TTS için) ve FFmpeg/Ollama (ileride LLM ve medya işleme için) öngörüyor. Bunları Day 1'de kontrol etmek, "üç hafta sonra meğer Ollama hiç kurulu değilmiş" gibi sürprizleri en baştan önlüyor.

**Nasıl çalışıyor:**
* `dotnet --version`, o an **aktif** olan .NET SDK sürümünü yazdırır; `dotnet --list-sdks` ise makinede kurulu **tüm** SDK'ları listeler. SDK (derleme/araç seti) ile runtime (sadece çalıştırma) farklı şeylerdir — SDK, runtime'ı da içinde barındırır.
* FFmpeg ve Ollama kontrolü hem Bash hem PowerShell üzerinden tekrarlandı, çünkü Windows'ta bir programın PATH'te görünür olması kullanılan kabuğa (shell) göre değişebilir. İkisinde de bulunamadı — yani eksiklik bir PATH/kabuk sorunu değil, gerçekten kurulu olmamaları. Bu ikisi Day 3 (Ollama) ve Day 12'ye (FFmpeg) kadar gerekmediği için bugün engel değil, sadece not edildi.

---

## 2. `git init`

```
git init
```

**Neden:** Proje klasörü daha önce bir Git deposu değildi. Kod değişikliklerini anlamlı commit'ler halinde takip edebilmek, ileride bir hatayı "hangi günün değişikliği bunu bozdu" diye geriye sarıp bulabilmek için sürüm kontrolü şart.

**Nasıl çalışıyor:** `git init`, mevcut klasörün içine gizli bir `.git` klasörü oluşturur; bu klasör tüm commit geçmişini, branch'leri ve Git'in iç veritabanını tutar. Bu komuttan sonra klasör bir Git deposu olur ama henüz hiçbir dosya takip edilmiyor (commit edilmiş hiçbir şey yok).

---

## 3. `.gitignore`

```gitignore
# .NET
bin/
obj/
*.user
*.suo
.vs/

# Node / Remotion (renderer, added later)
node_modules/
renderer/dist/
renderer/out/

# Python (local TTS tooling, added later)
__pycache__/
*.pyc
.venv/
venv/

# Local generated content (never commit rendered/generated media)
output/
samples/*.mp4
samples/*.wav

# Local databases
*.db
*.db-shm
*.db-wal
*.sqlite
*.sqlite3

# Local secrets/config overrides
appsettings.*.local.json
.env
.env.*

# Editor / OS
.idea/
.DS_Store
Thumbs.db

# Logs
*.log
```

**Neden:** `.NET` derlemesi her projede `bin/` ve `obj/` klasörlerini üretir — bunlar kaynak kod değil, **üretilen** dosyalardır ve her makinede yeniden oluşturulabilir. Bunları commit'lemek depoyu şişirir ve "gerçek kaynak kod değişikliği neydi" sorusunu gereksiz yere zorlaştırır. Aynı mantık `node_modules/` (Day 7+), Python sanal ortamları, SQLite veritabanı dosyaları (Day 2+) ve render edilmiş `output/` videoları için de geçerli — hepsi yerel olarak üretilebilir, depoya ait değil.

**Nasıl çalışıyor:** Git, bir klasördeki her dosyayı otomatik olarak takip etmeye çalışır; `.gitignore` içindeki her satır bir **desen** (pattern) tanımlar ve eşleşen dosya/klasörleri Git'in göz ardı etmesini sağlar. `bin/` sonundaki `/` "bu bir klasördür" anlamına gelir; `*.db` gibi joker karakterli (`*`) desenler ise uzantıya göre eşleşir. Bu dosya henüz yokken bile ileride (Day 2'de SQLite, Day 7'de Node) karşılaşılacak dosya türleri şimdiden düşünülüp eklendi — "neden henüz var olmayan bir şeyi ignore ediyoruz" sorusunun cevabı bu: bu desenler sonradan eklenmeyi beklemek yerine, roadmap bilinip baştan yazıldı.

---

## 4. `dotnet new sln -n ShortsGenerator` → `ShortsGenerator.slnx`

```
dotnet new sln -n ShortsGenerator
```

**Neden:** Projede ileride birden fazla .NET projesi olacak (Api, Domain, Application, Infrastructure — bkz. Day 2). Bunları tek tek `dotnet build`/`dotnet test` ile uğraşmak yerine hepsini bir arada derleyip test edebilmek için bir "solution" (çözüm) dosyasına ihtiyaç var.

**Nasıl çalışıyor:** Solution, projelerin kendisi değil, "bu projeler birbiriyle ilişkili, bir arada ele alınsın" diyen bir **grup tanımı**dır — gerçek kod içermez. .NET 10 SDK ile `dotnet new sln` artık klasik XML tabanlı `.sln` yerine yeni, daha sade **`.slnx`** formatını üretiyor (bu nedenle dosya `ShortsGenerator.slnx` olarak oluştu, `ShortsGenerator.sln` değil). İçerik, projelerin göreli yollarını listeleyen basit bir XML:

```xml
<Solution>
  <Project Path="src/ShortsGenerator.Api/ShortsGenerator.Api.csproj" />
  ...
</Solution>
```

`dotnet sln add <proje>` komutu, projeleri bu listeye ekler; `dotnet build`/`dotnet test` komutları bir solution dosyasının bulunduğu klasörde çalıştırıldığında, içindeki tüm projeleri sırayla derler/test eder.

---

## 5. `dotnet new webapi` → `ShortsGenerator.Api.csproj` ve şablon `Program.cs`

```
dotnet new webapi -n ShortsGenerator.Api -o src/ShortsGenerator.Api --use-controllers false
```

Üretilen `src/ShortsGenerator.Api/ShortsGenerator.Api.csproj` (ilk hali):

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.9" />
  </ItemGroup>

</Project>
```

Üretilen `Program.cs` (şablonun varsayılan demo kodu, henüz elle değiştirilmedi):

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
```

**Neden:** `--use-controllers false` bilinçli bir seçim: proje henüz Razor/MVC controller'larına ihtiyaç duymuyor, sadece birkaç küçük JSON endpoint'i olacak (Day 2'de `/api/videos`). "Minimal API" tarzı, bu ölçekte controller sınıfları yazmaktan daha az ortam gürültüsü (boilerplate) anlamına geliyor. Şablonun ürettiği `/weatherforecast` demo kodu, gerçek işimizle alakasız olduğu için birazdan silinip yerine `/api/health` endpoint'i yazıldı (bkz. bölüm 7).

**Nasıl çalışıyor (`.csproj`):**
* `<Project Sdk="Microsoft.NET.Sdk.Web">` — bu bir **SDK-style** proje dosyası; `Microsoft.NET.Sdk.Web`, ASP.NET Core'a özgü derleme kurallarını (statik dosyalar, Kestrel sunucusu vb.) devreye sokuyor. npm'deki `package.json`'ın "bu proje hangi araçlarla nasıl derlenir/çalışır" tanımına benzetilebilir.
* `<TargetFramework>net10.0</TargetFramework>` — projenin hangi .NET sürümüne karşı derleneceğini belirtiyor; Day 1'de kontrol ettiğimiz kurulu SDK (10.0.301) ile uyumlu.
* `<Nullable>enable</Nullable>` — derleyiciye, `string` ile `string?` arasındaki farkı ciddiye almasını ve olası null referans kullanımlarında **uyarı** vermesini söylüyor (TypeScript'teki `strictNullChecks`'in C# karşılığı).
* `<ImplicitUsings>enable</ImplicitUsings>` — `System`, `System.Linq` gibi en sık kullanılan namespace'leri her dosyanın başına tek tek `using` yazmadan otomatik ekliyor.
* `<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.9" />` — npm'deki bir `dependencies` satırının karşılığı; OpenAPI (Swagger) şema üretimini sağlayan paket.

**Nasıl çalışıyor (`Program.cs`):**
* `var builder = WebApplication.CreateBuilder(args);` — uygulamanın "inşaatçısını" oluşturuyor; konfigürasyon (appsettings.json), loglama ve DI (dependency injection) container'ı burada hazırlanmaya başlıyor. Bu dosyanın tamamı **top-level statements** — yani `class Program { static void Main(...) { ... } }` sarmalayıcısı olmadan, dosyanın kendisi doğrudan giriş noktası (.NET 6+ ile gelen bir kısayol).
* `builder.Services.AddOpenApi();` — DI container'a, OpenAPI JSON şemasını üretebilecek servisleri kaydediyor.
* `var app = builder.Build();` — yukarıdaki tüm kayıtlardan gerçek, çalıştırılabilir bir `WebApplication` nesnesi üretiyor. Bu satırdan sonra artık yeni servis **kaydı** yapılmaz, sadece HTTP pipeline'ı kurulur.
* `if (app.Environment.IsDevelopment()) { app.MapOpenApi(); }` — OpenAPI JSON endpoint'ini (`/openapi/v1.json`) sadece geliştirme ortamında açıyor; production'da bu bilgiyi dışarı sızdırmamak için bilinçli bir kısıtlama.
* `app.UseHttpsRedirection();` — bu bir **middleware**: gelen her HTTP isteğini otomatik olarak HTTPS'e yönlendiriyor. Middleware'ler, bir isteğin uygulamaya girişinden cevabın çıkışına kadar sırayla işlendiği bir "boru hattı" (pipeline) oluşturur; sıraları önemlidir.
* `app.MapGet("/weatherforecast", () => { ... }).WithName("GetWeatherForecast");` — minimal API syntax'ı: bir route'u (`/weatherforecast`), o route'a GET isteği geldiğinde çalışacak bir lambda'ya bağlıyor. `.WithName(...)` bu endpoint'e OpenAPI şemasında/iç yönlendirmede kullanılacak bir isim veriyor.
* `app.Run();` — Kestrel (ASP.NET Core'un yerleşik web sunucusu) burada gerçekten dinlemeye başlıyor; bu satırdan sonrası, uygulama kapatılana kadar çalışmaz (bloklayan bir çağrı).
* `record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary) { ... }` — şablonun ürettiği örnek bir positional record; `TemperatureF` expression-bodied computed property ile Fahrenheit'e çeviriyor. Bu tip ve `/weatherforecast` endpoint'i birazdan tamamen silindi.

---

## 6. Güvenlik uyarısı ve paket güncellemesi

```
dotnet build
→ warning NU1903: 'Microsoft.OpenApi' 2.0.0 paketinde önem derecesi yüksek
  olan bilinen bir güvenlik açığı var (GHSA-v5pm-xwqc-g5wc)

dotnet list package --outdated
→ Microsoft.AspNetCore.OpenApi   10.0.9 (istenen) → 10.0.12 (en son)

dotnet add package Microsoft.AspNetCore.OpenApi --version 10.0.12
```

**Neden:** Şablonun getirdiği `Microsoft.AspNetCore.OpenApi 10.0.9` paketi, arka planda `Microsoft.OpenApi 2.0.0`'a bağımlı; NuGet bu sürümde **yüksek önem dereceli** bilinen bir güvenlik açığı olduğunu otomatik olarak tespit edip derleme sırasında uyarı veriyor. "Şablon böyle verdi, dokunmayayım" demek yerine, bu uyarıyı görür görmez düzeltmek gerekiyor — ileride gerçek bir projede de code review'da bu tür uyarılar mutlaka ele alınır.

**Nasıl çalışıyor:** `dotnet build` ve `dotnet restore`, paketleri indirirken arka planda NuGet'in halka açık güvenlik açığı veritabanını (`https://api.nuget.org/v3/vulnerabilities/...`) kontrol eder — bu, `npm audit`'in .NET dünyasındaki karşılığı. `dotnet list package --outdated`, kurulu sürüm ile NuGet'teki en güncel sürümü karşılaştırıyor. `dotnet add package <isim> --version <sürüm>`, `.csproj` içindeki `<PackageReference>` satırını yeni sürüme güncelliyor ve paketi indirip `~/.nuget/packages` önbelleğine yüklüyor. Güncelleme sonrası `dotnet build` **0 uyarı** verdi — açığın geldiği eski `Microsoft.OpenApi 2.0.0` transitive bağımlılığı, düzeltilmiş `2.12.0`'a yükseldi.

---

## 7. `src/ShortsGenerator.Api/Program.cs` — şablonun yerine gerçek `/api/health` endpoint'i

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", DateTimeOffset.UtcNow)))
    .WithName("GetHealth");

app.Run();

public record HealthResponse(string Status, DateTimeOffset TimestampUtc);

public partial class Program
{
}
```

**Neden:** Şablonun `/weatherforecast` demo kodu tamamen kaldırıldı — projeyle hiçbir ilgisi yok. Yerine gerçek, anlamlı bir endpoint geldi: `/api/health`. Bunun amacı basit ama gerçek bir ihtiyaç: uygulama ayağa kalktı mı, canlı mı diye dışarıdan (ileride belki bir izleme aracından, şimdilik elle `curl` ile) sorgulanabilmesi. `public partial class Program {}` satırı ise tamamen test altyapısı için eklendi (aşağıda açıklanıyor).

**Nasıl çalışıyor:**
* `app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", DateTimeOffset.UtcNow)))` — `Results.Ok(...)`, HTTP 200 durum koduyla birlikte, parametre olarak verilen nesneyi JSON'a çevirip gövdeye (body) yazan bir yardımcı metot. `new HealthResponse("ok", DateTimeOffset.UtcNow)`, aşağıda tanımlanan record'dan bir örnek oluşturuyor.
* `public record HealthResponse(string Status, DateTimeOffset TimestampUtc);` — Day 2'nin `SkillSnapshot`'ına benzer bir **positional record**: iki tane salt-okunur property (`Status`, `TimestampUtc`) ve bunları JSON'a serileştirirken kullanılacak bir şekil (shape) tanımlıyor. Minimal API'ler, dönüş değeri olarak verilen nesneleri otomatik olarak JSON'a çevirir (`camelCase` property isimleriyle: `status`, `timestampUtc`).
* `public partial class Program { }` — top-level statements kullanıldığında, derleyici arka planda otomatik olarak gizli (implicitly `internal`) bir `Program` sınıfı üretir. Bu sınıf varsayılan olarak **assembly dışından erişilemez**. Ama Day 1'in sonunda yazılan test projesi, uygulamayı bellekte ayağa kaldırmak için `WebApplicationFactory<Program>` kullanıyor (bkz. bölüm 9) — bu da `Program` tipine **dışarıdan** (test projesinden) erişebilmeyi gerektiriyor. Bu satır, derleyicinin ürettiği gizli sınıfı `partial` olarak genişletip `public` yapıyor; böylece hem top-level statements'ın kısalığı korunuyor hem de test projesi `Program`'a erişebiliyor.

---

## 8. `tests/ShortsGenerator.Api.Tests` — xUnit test projesinin kurulması

```
dotnet new xunit -n ShortsGenerator.Api.Tests -o tests/ShortsGenerator.Api.Tests
dotnet sln add src/ShortsGenerator.Api/ShortsGenerator.Api.csproj tests/ShortsGenerator.Api.Tests/ShortsGenerator.Api.Tests.csproj
dotnet add tests/ShortsGenerator.Api.Tests/ShortsGenerator.Api.Tests.csproj reference src/ShortsGenerator.Api/ShortsGenerator.Api.csproj
dotnet add tests/ShortsGenerator.Api.Tests/ShortsGenerator.Api.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
```

**Neden:** "Çalışıyor gibi görünüyor, elle denedim" yeterli değil — otomatik bir test, her değişiklikten sonra `/api/health`'in gerçekten 200 döndürdüğünü saniyeler içinde doğrulayabilmeli. `Microsoft.AspNetCore.Mvc.Testing` paketi özellikle gerekli, çünkü asıl ihtiyaç duyulan `WebApplicationFactory<T>` sınıfı bu pakette yaşıyor.

**Nasıl çalışıyor:**
* `dotnet new xunit` — xUnit (.NET dünyasının en yaygın test framework'ü) için hazır bir proje şablonu üretiyor: `[Fact]`/`[Theory]` attribute'larını tanıyan bir test çalıştırıcısı (test runner) ile birlikte gelen bir class library.
* `dotnet sln add ...` — hem Api hem Api.Tests projelerini solution dosyasına ekliyor (bu ana kadar sadece `ShortsGenerator.slnx` dosyası vardı, içi boştu).
* `dotnet add ... reference ...` — test projesinin `.csproj`'una bir `<ProjectReference>` ekliyor; bu sayede test projesi, Api projesindeki `public` tipleri (örneğin yukarıdaki `Program`) görebiliyor. npm'de bir paketi `devDependencies`'e eklemenin yakın karşılığı, ama burada iki yerel proje birbirine bağlanıyor.
* `dotnet add ... package Microsoft.AspNetCore.Mvc.Testing` — `WebApplicationFactory<T>` sınıfını (ve ihtiyaç duyduğu `Microsoft.AspNetCore.TestHost` gibi transitive bağımlılıkları) projeye kazandırıyor.

---

## 9. `tests/ShortsGenerator.Api.Tests/HealthEndpointTests.cs`

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ShortsGenerator.Api.Tests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealth_ReturnsOkWithStatusOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body);
    }
}
```

**Neden:** Şablonun ürettiği boş `UnitTest1.cs` silinip yerine gerçek bir **entegrasyon testi** yazıldı: `/api/health`'e gerçek bir HTTP isteği atıp gerçek bir HTTP cevabı doğruluyor — tek bir metodu izole çağırıp dönüş değerine bakan bir "unit test" değil, uygulamanın uçtan uca (routing, middleware, JSON serileştirme dahil) doğru çalıştığını kanıtlayan bir test.

**Nasıl çalışıyor:**
* `IClassFixture<WebApplicationFactory<Program>>` — xUnit'e "bu test sınıfındaki tüm testler aynı `WebApplicationFactory<Program>` örneğini paylaşsın" diyor. `WebApplicationFactory`, gerçek bir ağ portu açmadan, **bellekte** çalışan sahte bir sunucu ayağa kaldırıyor — `Program.cs`'teki tüm DI kayıtları, middleware'ler ve route'lar gerçek bir `dotnet run` gibi devreye giriyor, ama test çok daha hızlı çalışıyor çünkü gerçek ağ/port açılmıyor.
* Constructor injection (`public HealthEndpointTests(WebApplicationFactory<Program> factory)`) — xUnit, `IClassFixture<T>` kullanıldığında bu `T` örneğini otomatik olarak constructor'a enjekte ediyor; Day 2'de göreceğimiz ASP.NET Core DI'ın aynı mantığı, test tarafında.
* `[Fact]` — "bu metot parametre almayan, tek senaryolu bir testtir" diyen xUnit attribute'u; test çalıştırıcısı bu attribute'u taşıyan her metodu otomatik keşfedip çalıştırır.
* `public async Task GetHealth_ReturnsOkWithStatusOk()` — `async`/`Task` imzası, metodun içinde `await` kullanılacağının işareti; test çalıştırıcısı bu `Task`'ın tamamlanmasını bekler.
* `_factory.CreateClient()` — bellekteki sahte sunucuya karşı gerçek bir `HttpClient` üretiyor; `GetAsync("/api/health")` bu client üzerinden gerçek bir GET isteği gönderiyor (ama ağ üzerinden değil, doğrudan bellek içi pipeline üzerinden).
* `Assert.Equal(HttpStatusCode.OK, response.StatusCode)` — beklenen durum kodu ile gerçek durum kodunu karşılaştırıyor; eşleşmezse test kırmızı (failed) olur.
* `Assert.Contains("\"status\":\"ok\"", body)` — JSON gövdesini tam bir nesneye deserialize etmek yerine, basitçe beklenen alt metnin içinde olup olmadığına bakıyor — bu ölçekte bir test için yeterli ve daha az kırılgan (property sırası değişse bile çalışır).

---

## Doğrulanan davranış

```
dotnet build  → 0 Uyarı, 0 Hata (2 proje: Api, Api.Tests)
dotnet test   → Başarılı: 0 Başarısız, 1 Başarılı, 1 Toplam

Gerçek dotnet run ile manuel doğrulama (ASPNETCORE_ENVIRONMENT=Development):
GET /api/health          → 200, {"status":"ok","timestampUtc":"2026-09-15T16:49:45..."}
GET /openapi/v1.json     → 200
```

İlk denemede `dotnet run --no-launch-profile` ortam değişkeni belirtilmeden çalıştırıldığında `ASPNETCORE_ENVIRONMENT` varsayılan olarak `Production` kaldı ve `/openapi/v1.json` beklenen şekilde **404** döndü (`app.MapOpenApi()` sadece `IsDevelopment()` iken çalışıyor — bölüm 5/7'de görülen kasıtlı davranış). `ASPNETCORE_ENVIRONMENT=Development` ortam değişkeni eklenip süreç yeniden başlatıldıktan sonra her iki endpoint de beklendiği gibi 200 döndürdü. Bu, "neden 404 aldım" sorusunun ortam (environment) kaynaklı olabileceğinin ilk elden gözlemlenmiş bir örneği.
