# Day 2 — Kod Notları

30 günlük roadmap, Gün 2. Konu: `VideoJob` domain modeli, Domain/Application/Infrastructure katman ayrımı, EF Core + SQLite ile kalıcılık, migration'lar ve gün içinde bulunup düzeltilen üç gerçek hata.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. Üç yeni class library projesi

```
dotnet new classlib -n ShortsGenerator.Domain -o src/ShortsGenerator.Domain
dotnet new classlib -n ShortsGenerator.Application -o src/ShortsGenerator.Application
dotnet new classlib -n ShortsGenerator.Infrastructure -o src/ShortsGenerator.Infrastructure

dotnet sln add src/ShortsGenerator.Domain/ShortsGenerator.Domain.csproj ^
               src/ShortsGenerator.Application/ShortsGenerator.Application.csproj ^
               src/ShortsGenerator.Infrastructure/ShortsGenerator.Infrastructure.csproj

dotnet add src/ShortsGenerator.Application reference src/ShortsGenerator.Domain
dotnet add src/ShortsGenerator.Infrastructure reference src/ShortsGenerator.Domain
dotnet add src/ShortsGenerator.Api reference src/ShortsGenerator.Application src/ShortsGenerator.Infrastructure
```

**Neden:** Day 1'de tek bir `ShortsGenerator.Api` projesi vardı. Bugün gerçek bir domain modeli (`VideoJob`) ve bunu kalıcı hale getirecek bir veritabanı katmanı geliyor. Bunların hepsini `Api` projesinin içine yazmak yerine üç ayrı projeye bölündü, çünkü her birinin **farklı bir bağımlılık hakkı** olmalı: Domain'in ASP.NET Core'u ya da EF Core'u bilmesine hiç gerek yok (ve olmamalı); Infrastructure'ın ise HTTP'yi bilmesine gerek yok. Bu ayrım, "hangi katman neyi bilmeli" sorusunu derleme zamanında zorunlu kılıyor — birisi yanlışlıkla Domain'den `DbContext`'e referans vermeye çalışsa, proje referansı olmadığı için derleme hatası alır.

**Nasıl çalışıyor:**
* `dotnet new classlib` — ASP.NET Core'a özgü hiçbir şey içermeyen, sade bir .NET class library projesi üretir (`Microsoft.NET.Sdk`, `Microsoft.NET.Sdk.Web` değil). Yani bu projeler teorik olarak bir konsol uygulamasında, bir masaüstü uygulamasında, başka bir web framework'ünde de kullanılabilir — hiçbir ASP.NET Core bağımlılığı taşımıyorlar.
* `dotnet add <proje> reference <başka proje>` — bir `.csproj` dosyasına `<ProjectReference>` ekler. Bağımlılık **yönü** kasıtlı olarak tek yönlü kuruldu: `Application → Domain`, `Infrastructure → Domain`, `Api → Application + Infrastructure`. Domain hiçbir şeye referans vermiyor (en "saf" katman); Api ise her şeye referans veriyor (en "dış" katman, her şeyi bir araya getiren yer). Bu, klasik Clean Architecture / Dependency Inversion düzeni.
* Dört proje de aynı `ShortsGenerator.slnx` çözümüne eklendi, böylece `dotnet build`/`dotnet test` tek komutla hepsini kapsıyor.

---

## 2. `src/ShortsGenerator.Domain/VideoJobStatus.cs`

```csharp
namespace ShortsGenerator.Domain;

public enum VideoJobStatus
{
    Created,
    Planning,
    Narrating,
    Rendering,
    Completed,
    Failed,
}
```

**Neden:** Bir video üretim işinin (job) hangi aşamada olduğunu temsil eden altı durum var ve bunlar `CLAUDE.md`'deki domain tanımıyla birebir eşleşiyor. Bunu ham bir `string` (`"created"`, `"Created"`, `"CREATED"` gibi tutarsız yazımlara açık) ya da `int` yerine bir `enum` olarak modellemek, geçersiz bir durumun (`"beklemede"` gibi) daha derleme aşamasında imkansız olmasını sağlıyor.

**Nasıl çalışıyor:** Day 2'nin `SkillLevel` enum'uyla aynı mekanik: her üye arka planda bir `int`'e karşılık geliyor (burada sıra numarası `CLAUDE.md`'deki domain tanımıyla bire bir, ama sayılar herhangi bir dış sözleşmeye bağlı olmadığı için — SQLite'a `HasConversion<string>()` ile string olarak yazılacak, bkz. bölüm 6 — açıkça numaralandırmaya gerek duyulmadı.

---

## 3. `src/ShortsGenerator.Domain/VideoJob.cs`

```csharp
namespace ShortsGenerator.Domain;

public sealed class VideoJob
{
    public Guid Id { get; private set; }
    public string Topic { get; private set; } = string.Empty;
    public string? SourceScript { get; private set; }
    public string? GeneratedScript { get; private set; }
    public VideoJobStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? OutputDirectory { get; private set; }
    public string? FinalVideoPath { get; private set; }

    // Required by EF Core to materialize entities from the database.
    private VideoJob()
    {
    }

    public static VideoJob Create(string topic, string? sourceScript, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic is required.", nameof(topic));
        }

        return new VideoJob
        {
            Id = Guid.NewGuid(),
            Topic = topic.Trim(),
            SourceScript = string.IsNullOrWhiteSpace(sourceScript) ? null : sourceScript.Trim(),
            Status = VideoJobStatus.Created,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void MarkPlanning(DateTimeOffset now) =>
        TransitionTo(VideoJobStatus.Planning, now, VideoJobStatus.Created);

    public void MarkNarrating(string generatedScript, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(generatedScript))
        {
            throw new ArgumentException("Generated script is required.", nameof(generatedScript));
        }

        TransitionTo(VideoJobStatus.Narrating, now, VideoJobStatus.Planning);
        GeneratedScript = generatedScript;
    }

    public void MarkRendering(DateTimeOffset now) =>
        TransitionTo(VideoJobStatus.Rendering, now, VideoJobStatus.Narrating);

    public void MarkCompleted(string finalVideoPath, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(finalVideoPath))
        {
            throw new ArgumentException("Final video path is required.", nameof(finalVideoPath));
        }

        TransitionTo(VideoJobStatus.Completed, now, VideoJobStatus.Rendering);
        FinalVideoPath = finalVideoPath;
    }

    public void MarkFailed(string errorMessage, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Error message is required.", nameof(errorMessage));
        }

        TransitionTo(
            VideoJobStatus.Failed,
            now,
            VideoJobStatus.Created,
            VideoJobStatus.Planning,
            VideoJobStatus.Narrating,
            VideoJobStatus.Rendering);
        ErrorMessage = errorMessage;
    }

    private void TransitionTo(VideoJobStatus target, DateTimeOffset now, params VideoJobStatus[] allowedFrom)
    {
        if (Array.IndexOf(allowedFrom, Status) < 0)
        {
            throw new InvalidOperationException(
                $"Cannot move video job {Id} from status '{Status}' to '{target}'.");
        }

        Status = target;
        UpdatedAt = now;
    }
}
```

**Neden:** Bu projenin en önemli dosyası — gerçek bir "zengin domain modeli" (rich domain model) örneği. Amaç: bir `VideoJob`'ın **geçersiz bir durumda var olmasını mümkün kılmamak**. Örneğin `Rendering` durumuna, `Narrating` aşamasından geçmeden ulaşmak anlamsız (henüz seslendirme metni yokken render'a geçilemez); `Completed` bir job'ı tekrar `Failed` yapmak da anlamsız (iş zaten bitti). Bu kurallar `VideoJobService` içine ya da bir controller'a dağılmış `if` blokları olarak yazılabilirdi, ama o zaman her yeni kullanım noktasında aynı kontrolü tekrar yazmak/unutmak riski olurdu. Kuralları entity'nin kendisine kilitlemek, bu hatayı **yapmayı imkansız** hale getiriyor.

**Nasıl çalışıyor:**
```csharp
public Guid Id { get; private set; }
...
private VideoJob() { }
```
Tüm property'ler `private set` — yani sınıf dışından kimse `job.Status = VideoJobStatus.Completed;` diye doğrudan atama yapamaz, sadece bu dosyadaki metotlar üzerinden değişebilir. Parametresiz `private` constructor ise sadece EF Core'un materyalizasyon (veritabanından okunan satırı nesneye çevirme) sırasında reflection ile kullanabileceği bir "arka kapı" — uygulama kodu bunu asla çağıramaz.

```csharp
public static VideoJob Create(string topic, string? sourceScript, DateTimeOffset now)
```
**Factory method pattern**: `new VideoJob(...)` yerine, nesne oluşturmanın tek yolu bu statik metot. Böylece her yeni `VideoJob`, `Create` içindeki doğrulamadan (topic boş olamaz) geçmeden var olamıyor — "geçersiz nesne" diye bir şey mümkün değil.

```csharp
public void MarkPlanning(DateTimeOffset now) =>
    TransitionTo(VideoJobStatus.Planning, now, VideoJobStatus.Created);
```
Expression-bodied metot (Day 2'nin `IsAtTarget`'ındaki `=>` ile aynı syntax, burada bir property değil metot için). "Sadece `Created` durumundaysa `Planning`'e geçebilirsin" kuralını `TransitionTo`'ya devrediyor.

```csharp
private void TransitionTo(VideoJobStatus target, DateTimeOffset now, params VideoJobStatus[] allowedFrom)
{
    if (Array.IndexOf(allowedFrom, Status) < 0)
    {
        throw new InvalidOperationException(...);
    }

    Status = target;
    UpdatedAt = now;
}
```
Tüm `Mark*` metotlarının ortak mantığını tek bir yerde topluyor — kod tekrarını önlüyor. `params VideoJobStatus[] allowedFrom` — **params** anahtar kelimesi, çağıran tarafın `TransitionTo(x, y, new[] { a, b, c })` yerine doğrudan `TransitionTo(x, y, a, b, c)` yazabilmesini sağlıyor (virgülle ayrılmış değişken sayıda argüman, derleyici arkada otomatik olarak bir diziye paketliyor). `Array.IndexOf(allowedFrom, Status) < 0` — mevcut `Status`, izin verilen durumlar listesinde yoksa (`IndexOf` bulamadığında `-1` döner) `InvalidOperationException` fırlatılıyor. `MarkFailed`, dört farklı durumdan (`Created`, `Planning`, `Narrating`, `Rendering`) çağrılabildiği için `allowedFrom` dizisine dört eleman veriyor; diğerleri tek bir öncül durumdan geçişe izin veriyor.

```csharp
public void MarkNarrating(string generatedScript, DateTimeOffset now)
{
    if (string.IsNullOrWhiteSpace(generatedScript)) { throw ... }
    TransitionTo(VideoJobStatus.Narrating, now, VideoJobStatus.Planning);
    GeneratedScript = generatedScript;
}
```
Dikkat: `TransitionTo` **önce** çağrılıyor, veri ataması (`GeneratedScript = ...`) **sonra** yapılıyor. Böylece geçiş kuralı ihlal edilirse (`Planning`'de değilse) `GeneratedScript` hiç değişmeden exception fırlatılıyor — yarım güncellenmiş, tutarsız bir nesne durumu oluşmuyor.

---

## 4. `src/ShortsGenerator.Domain/IVideoJobRepository.cs`

```csharp
namespace ShortsGenerator.Domain;

public interface IVideoJobRepository
{
    Task AddAsync(VideoJob job, CancellationToken cancellationToken);

    Task<VideoJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<VideoJob>> ListRecentAsync(int take, CancellationToken cancellationToken);
}
```

**Neden:** Domain katmanı, `VideoJob`'ların **nasıl** saklanacağını (SQLite mi, SQL Server mı, bellekte mi) hiç bilmemeli — sadece "saklanabilir ve geri okunabilir olmalı" sözleşmesini tanımlamalı. Bu, Day 3'teki `ISkillCatalog`'un aynısı: gerçek implementasyon (`VideoJobRepository`, bkz. bölüm 9) Infrastructure katmanında yaşayacak, ama **sözleşmenin kendisi** Domain'de duruyor. Bu "Dependency Inversion Principle"ın ders kitabı örneği — normalde "Domain, Infrastructure'a bağımlı olur" beklenir (çünkü veri oradan geliyor), ama burada tam tersi: Infrastructure, Domain'in tanımladığı sözleşmeye bağımlı.

**Nasıl çalışıyor:**
* Üç metodun üçü de `Task`/`Task<T>` dönüyor ve bir `CancellationToken` parametresi alıyor — ASP.NET Core'daki her I/O işleminin (veritabanı, dosya, ağ) standart imzası. `CancellationToken`, çağıran taraf (örneğin bir HTTP isteği iptal edildiğinde ASP.NET Core) işlemi yarıda kesebilsin diye var; bugün henüz gerçek bir iptal senaryosu yok ama bu parametre en baştan doğru imzayı kurmak için eklendi.
* `Task<VideoJob?>` — dönüş tipindeki `?`, "bu id ile bir job bulunamayabilir, o zaman `null` dönebilirim" demek; çağıran tarafı (Api'deki `GetJobAsync`) null kontrolü yapmaya zorluyor.
* `Task<IReadOnlyList<VideoJob>>` — Day 3'teki `ISkillCatalog.GetAll()`'daki aynı `IReadOnlyList` tercihi: çağıran taraf listeyi okuyabilir ama üzerine ekleme/çıkarma yapamaz.

---

## 5. `src/ShortsGenerator.Application/VideoJobService.cs`

```csharp
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public sealed class VideoJobService
{
    private readonly IVideoJobRepository _repository;
    private readonly TimeProvider _timeProvider;

    public VideoJobService(IVideoJobRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<VideoJob> CreateJobAsync(string topic, string? sourceScript, CancellationToken cancellationToken)
    {
        var job = VideoJob.Create(topic, sourceScript, _timeProvider.GetUtcNow());
        await _repository.AddAsync(job, cancellationToken);
        return job;
    }

    public Task<VideoJob?> GetJobAsync(Guid id, CancellationToken cancellationToken) =>
        _repository.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<VideoJob>> ListRecentJobsAsync(int take, CancellationToken cancellationToken) =>
        _repository.ListRecentAsync(take, cancellationToken);
}
```

**Neden:** Bu, "use case" (kullanım senaryosu) katmanı — Api'nin doğrudan `IVideoJobRepository`'yi çağırması yerine, iş akışını burada topluyoruz. Bugün için akış çok basit (sadece repository'ye yönlendirme), ama ileride (Day 5+) "job oluştur **ve** LLM'den script iste **ve** scene plan'ı kaydet" gibi çok adımlı akışlar da burada, `Program.cs`'i şişirmeden toplanacak.

**Nasıl çalışıyor:**
* `TimeProvider _timeProvider` — `DateTimeOffset.UtcNow`'u doğrudan çağırmak yerine, zamanı dışarıdan enjekte edilen bir soyutlama üzerinden alıyoruz. .NET 8 ile gelen `TimeProvider`, tam olarak bunun için var: production'da `TimeProvider.System` gerçek saati verir, bir testte ise sahte/sabit bir zaman kaynağı (`FakeTimeProvider` gibi) enjekte edilip "1 Ocak 2030'da ne olurdu" senaryoları deterministik şekilde test edilebilir. Bugün için doğrudan bir test yazılmadı ama altyapı doğru kuruldu.
* `CreateJobAsync` — önce Domain katmanındaki `VideoJob.Create(...)` factory'sini çağırıp geçerli bir nesne üretiyor, sonra `_repository.AddAsync(...)` ile kalıcı hale getiriyor, son olarak oluşturulan nesneyi (artık `Id`'si de atanmış haliyle) geri döndürüyor.
* `GetJobAsync`/`ListRecentJobsAsync` — expression-bodied metotlar, doğrudan repository'ye devrediyor (bugün için ek bir iş mantığı yok, ama Api katmanının `IVideoJobRepository`'yi değil `VideoJobService`'i bilmesini sağlıyor — katman sınırı korunuyor).

---

## 6. `src/ShortsGenerator.Infrastructure/ShortsGeneratorDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure;

public sealed class ShortsGeneratorDbContext : DbContext
{
    public ShortsGeneratorDbContext(DbContextOptions<ShortsGeneratorDbContext> options)
        : base(options)
    {
    }

    public DbSet<VideoJob> VideoJobs => Set<VideoJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VideoJob>(entity =>
        {
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Topic).IsRequired().HasMaxLength(300);
            entity.Property(job => job.SourceScript);
            entity.Property(job => job.GeneratedScript);
            entity.Property(job => job.Status).HasConversion<string>().IsRequired().HasMaxLength(50);

            // SQLite has no native DateTimeOffset type and can't order by it directly;
            // store as UTC ticks so ORDER BY / comparisons work correctly.
            var dateTimeOffsetConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, long>(
                v => v.UtcTicks,
                v => new DateTimeOffset(v, TimeSpan.Zero));

            entity.Property(job => job.CreatedAt).HasConversion(dateTimeOffsetConverter).IsRequired();
            entity.Property(job => job.UpdatedAt).HasConversion(dateTimeOffsetConverter).IsRequired();
            entity.Property(job => job.ErrorMessage);
            entity.Property(job => job.OutputDirectory);
            entity.Property(job => job.FinalVideoPath);
        });
    }
}
```

Bu dosya bugün **iki aşamada** yazıldı: önce `CreatedAt`/`UpdatedAt` için düz `.IsRequired()` ile bırakıldı, testler çalıştırılınca bir hata bulundu ve `HasConversion(dateTimeOffsetConverter)` eklendi. Aşağıda önce "neden/nasıl" temel yapı, sonra bölüm 13'te bu düzeltmenin kendisi anlatılıyor.

**Neden:** `Skill`/`RoadmapOSDbContext` örneğindeki gibi, bu sınıf EF Core'a "hangi entity'ler (tablolar) var, her property SQL'de nasıl görünsün" bilgisini veren merkezi yer. `OnModelCreating` içindeki yapılandırma, C# tarafındaki private-setter'lı `VideoJob` sınıfını gerçek bir SQL şemasına çeviriyor.

**Nasıl çalışıyor:**
* `public DbSet<VideoJob> VideoJobs => Set<VideoJob>();` — Day 4'teki `RoadmapOSDbContext.Skills` ile birebir aynı syntax: "VideoJobs tablosu, `VideoJob` class'ından üretilir."
* `modelBuilder.Entity<VideoJob>(entity => { ... })` — **Fluent API** ile yapılandırma: `VideoJob` üzerinde data annotation attribute'ları (`[Required]`, `[MaxLength]` gibi) kullanmak yerine, bu yapılandırmayı tamamen `DbContext`'in içine, Domain sınıfından ayrı tutuyoruz. Böylece `VideoJob.cs` hâlâ EF Core'dan habersiz, saf bir C# sınıfı kalıyor (Infrastructure'a bağımlılığı yok).
* `entity.HasKey(job => job.Id);` — `Id` property'sini primary key olarak işaretliyor. (Day 4'teki `Skill.Id` gibi convention ile otomatik algılanmıyor çünkü burada tip `Guid`, `int` değil — `Id` isimli `int`/`Guid` alanlar genelde convention ile zaten PK sayılır, ama burada açıkça yazılması niyet okunabilirliğini artırıyor.)
* `entity.Property(job => job.Topic).IsRequired().HasMaxLength(300);` — SQL tarafında `NOT NULL` + `nvarchar(300)` benzeri bir kısıtlamaya çeviriliyor; C# tarafındaki `string` (nullable olmayan) zaten `IsRequired()`'ı ima ediyor ama `HasMaxLength(300)` EF Core'un kendiliğinden bilemeyeceği bir iş kuralı (konu başlığı makul bir uzunlukta olmalı), bu yüzden açıkça yazıldı.
* `entity.Property(job => job.Status).HasConversion<string>().IsRequired().HasMaxLength(50);` — `VideoJobStatus` enum'u veritabanında ham bir sayı (`0`, `1`, `2`...) yerine **okunabilir metin** (`"Created"`, `"Rendering"`) olarak saklanıyor. `HasConversion<string>()` bunu otomatik yapıyor. Bunun pratik faydası: ileride enum'a yeni bir üye eklense/sıralama değişse bile, veritabanındaki eski kayıtlar anlamsız sayılara dönüşmez — veri her zaman insan tarafından okunabilir kalır (`sqlite3` ile tabloya bakınca `2` değil `"Narrating"` görünüyor).

---

## 7. `src/ShortsGenerator.Infrastructure/VideoJobRepository.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure;

public sealed class VideoJobRepository : IVideoJobRepository
{
    private readonly ShortsGeneratorDbContext _dbContext;

    public VideoJobRepository(ShortsGeneratorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(VideoJob job, CancellationToken cancellationToken)
    {
        _dbContext.VideoJobs.Add(job);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<VideoJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.VideoJobs.FirstOrDefaultAsync(job => job.Id == id, cancellationToken);

    public async Task<IReadOnlyList<VideoJob>> ListRecentAsync(int take, CancellationToken cancellationToken) =>
        await _dbContext.VideoJobs
            .OrderByDescending(job => job.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
}
```

**Neden:** Bölüm 4'teki `IVideoJobRepository` sözleşmesinin SQLite/EF Core ile çalışan gerçek implementasyonu.

**Nasıl çalışıyor:**
* `_dbContext.VideoJobs.Add(job);` — yeni nesneyi `DbContext`'in **takip ettiği** (tracked) nesneler listesine ekliyor; bu satır tek başına SQL'e dokunmuyor.
* `await _dbContext.SaveChangesAsync(cancellationToken);` — takip edilen değişiklikleri (buradaki tek `INSERT`) gerçekten SQLite'a yazan asıl çağrı. Day 4'teki `context.SaveChanges()` ile aynı mekanizma, burada `async` versiyonu kullanıldı çünkü bu bir web API — I/O'yu bloklamadan (thread'i bekletmeden) beklemek önemli.
* `_dbContext.VideoJobs.FirstOrDefaultAsync(job => job.Id == id, cancellationToken)` — LINQ sorgusu; `FirstOrDefaultAsync`, eşleşen ilk satırı döndürür, hiç eşleşme yoksa (tip referans tipi olduğu için) `null` döner — tam olarak arayüzdeki `Task<VideoJob?>` imzasının beklediği davranış.
* `.OrderByDescending(job => job.CreatedAt).Take(take).ToListAsync(cancellationToken)` — LINQ zinciri: önce en yeniden en eskiye sırala, sonra ilk `take` kadarını al, sonra gerçekten çalıştırıp listeye çevir. Bu üç adım, EF Core tarafından **tek bir SQL sorgusuna** çevriliyor (`SELECT ... ORDER BY CreatedAt DESC LIMIT @take`) — ara adımlarda belleğe hiçbir şey çekilmiyor, `ToListAsync()` çağrılana kadar hiçbir SQL çalışmıyor (ertelenmiş/deferred execution).

---

## 8. `src/ShortsGenerator.Infrastructure/InfrastructureServiceCollectionExtensions.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing 'ConnectionStrings:Default' configuration value.");

        services.AddDbContext<ShortsGeneratorDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IVideoJobRepository, VideoJobRepository>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
```

**Neden:** `Program.cs`'in içine `AddDbContext<...>(...)`, `AddScoped<...>(...)` gibi üç-beş satır Infrastructure'a özgü DI kaydını tek tek yazmak yerine, bunları Infrastructure projesinin kendi içinde, tek bir metot arkasında topladık. Bu, ASP.NET Core ekosisteminde çok yaygın bir konvansiyon (`AddControllersWithViews()`, `AddAuthentication()` gibi framework'ün kendi metotları da hep bu şekilde). Fayda: `Program.cs` "hangi parçalar var" diye okunabilir kalıyor, "her parça nasıl kuruluyor" detayı kendi katmanına saklanıyor.

**Nasıl çalışıyor:**
* `public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)` — başındaki `this IServiceCollection services`, bunun bir **extension method** olduğunu belirtiyor: `IServiceCollection` tipinde her nesne (örneğin `builder.Services`), sanki kendi metoduymuş gibi `.AddInfrastructure(...)` şeklinde çağırabiliyor. Bu, .NET'in var olan bir tipi değiştirmeden ona yeni "üye" eklemenin yolu (C#'a özgü, TS'te doğrudan karşılığı yok — en yakın benzetme bir yardımcı fonksiyonu `.pipe()` zinciri gibi akıcı çağırabilmek).
* `configuration.GetConnectionString("Default") ?? throw new InvalidOperationException(...)` — `appsettings.json`'daki `ConnectionStrings:Default` değerini okuyor; `??` (null-coalescing) burada Day 2'deki gibi bir varsayılan değer vermek için değil, **hatayı erken ve net fırlatmak** için kullanıldı: connection string eksikse uygulama "bir şeyler null referans hatası verdi" diye belirsiz bir şekilde çökmek yerine, açık bir `InvalidOperationException` ile hemen ve anlaşılır şekilde patlıyor.
* `services.AddDbContext<ShortsGeneratorDbContext>(options => options.UseSqlite(connectionString));` — `DbContext`'i DI container'a kaydediyor (varsayılan olarak `Scoped` ömürle — Day 4'te SQL Server için konuştuğumuz "her HTTP isteği kendi `DbContext`'ini alsın, `DbContext`'ler thread-safe değil" kuralı burada da geçerli), `UseSqlite(...)` ile hangi veritabanı sağlayıcısının (provider) kullanılacağını belirtiyor.
* `services.AddSingleton(TimeProvider.System);` — bölüm 5'teki `VideoJobService`'in ihtiyaç duyduğu `TimeProvider`'ı, .NET'in gerçek sistem saatini kullanan hazır implementasyonuyla (`TimeProvider.System`) tek bir örnek (singleton) olarak kaydediyor — saat kaynağı, veri gibi istekten isteğe değişen bir şey değil, tüm uygulama boyunca aynı kalabilir.

---

## 9. `src/ShortsGenerator.Api/appsettings.json` — connection string

```json
{
  "Logging": { ... },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "Default": "Data Source=shortsgenerator.db"
  }
}
```

**Neden:** Bölüm 8'deki `AddInfrastructure`'ın okuduğu `ConnectionStrings:Default` değeri burada tanımlanıyor. Day 4'teki SQL Server connection string'inin (`Server=...;Database=...;Trusted_Connection=True;...`) aksine, SQLite'ın connection string'i çok daha sade: sadece bir dosya yolu.

**Nasıl çalışıyor:** `"Data Source=shortsgenerator.db"` — SQLite'a "veritabanın, uygulamanın çalıştığı klasördeki `shortsgenerator.db` dosyası olsun" diyor. Bu dosya henüz yoksa, EF Core migration'ı ilk çalıştığında (bkz. bölüm 11 ve `Program.cs`'teki `db.Database.Migrate()`) **otomatik olarak** oluşturuluyor — SQL Server'daki gibi ayrı bir sunucu sürecine, kullanıcı adına/şifreye hiç gerek yok; bu da "zero-cost, local-first" prensibine tam uyuyor.

---

## 10. `src/ShortsGenerator.Api/Contracts/VideoJobContracts.cs`

```csharp
using ShortsGenerator.Domain;

namespace ShortsGenerator.Api.Contracts;

public sealed record CreateVideoJobRequest(string Topic, string? SourceScript);

public sealed record VideoJobResponse(
    Guid Id,
    string Topic,
    string? SourceScript,
    string? GeneratedScript,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ErrorMessage,
    string? FinalVideoPath)
{
    public static VideoJobResponse FromDomain(VideoJob job) => new(
        job.Id,
        job.Topic,
        job.SourceScript,
        job.GeneratedScript,
        job.Status.ToString(),
        job.CreatedAt,
        job.UpdatedAt,
        job.ErrorMessage,
        job.FinalVideoPath);
}
```

**Neden:** `VideoJob` entity'sini doğrudan HTTP cevabı olarak dışarı vermek yerine, ayrı bir **DTO** (Data Transfer Object) tanımlandı. İki ayrı sebep var: (1) `VideoJob`'ın private setter'ları ve `Create`/`Mark*` metotları gibi iç mekanizması, HTTP sözleşmesinin bir parçası olmamalı — entity ileride değişirse (örneğin yeni bir internal alan eklense) API sözleşmesi kazara kırılmamalı; (2) `Status` enum'u response'ta ham sayı yerine okunabilir string olarak (`"Created"`) dönmeli, ki bu dönüşüm `FromDomain` içinde açıkça yapılıyor.

**Nasıl çalışıyor:**
* `public sealed record CreateVideoJobRequest(string Topic, string? SourceScript);` — minimal API'nin bir `POST` body'sini otomatik olarak deserialize edebileceği, positional bir record. `SourceScript` nullable, çünkü kullanıcı sadece bir konu (`topic`) girip script'i LLM'e bırakabilir (ileride Day 4+).
* `public static VideoJobResponse FromDomain(VideoJob job) => new(...);` — bir **mapping metodu**: Domain nesnesini DTO'ya çeviriyor. `record` içinde statik bir metot tanımlamak tamamen geçerli; burada özellikle `job.Status.ToString()` dikkat çekici — enum'u response'ta insan-okunur string'e çeviriyor (tıpkı veritabanında `HasConversion<string>()` ile yaptığımız gibi, ama bu sefer HTTP sözleşmesi için, EF Core'dan bağımsız).

---

## 11. `src/ShortsGenerator.Api/Program.cs` — DI kaydı, migration ve iki yeni endpoint

```csharp
using Microsoft.EntityFrameworkCore;
using ShortsGenerator.Api.Contracts;
using ShortsGenerator.Application;
using ShortsGenerator.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<VideoJobService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Single local user, single process: applying migrations on startup is safe
// and keeps local setup to "just run it".
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShortsGeneratorDbContext>();
    db.Database.Migrate();
}

app.UseHttpsRedirection();

app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", DateTimeOffset.UtcNow)))
    .WithName("GetHealth");

app.MapPost("/api/videos", async (CreateVideoJobRequest request, VideoJobService service, CancellationToken ct) =>
{
    try
    {
        var job = await service.CreateJobAsync(request.Topic, request.SourceScript, ct);
        return Results.Created($"/api/videos/{job.Id}", VideoJobResponse.FromDomain(job));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
    .WithName("CreateVideoJob");

app.MapGet("/api/videos/{id:guid}", async (Guid id, VideoJobService service, CancellationToken ct) =>
{
    var job = await service.GetJobAsync(id, ct);
    return job is null ? Results.NotFound() : Results.Ok(VideoJobResponse.FromDomain(job));
})
    .WithName("GetVideoJob");

app.Run();

public record HealthResponse(string Status, DateTimeOffset TimestampUtc);

public partial class Program
{
}
```

**Neden:** Day 1'in sadece `/api/health` içeren `Program.cs`'i, bugün üç yönden büyüdü: (1) Infrastructure + Application servislerinin DI kaydı, (2) uygulama açılırken veritabanı şemasının güncel olduğundan emin olmak, (3) gerçek `VideoJob` oluşturma/okuma endpoint'leri.

**Nasıl çalışıyor:**
* `builder.Services.AddInfrastructure(builder.Configuration);` — bölüm 8'de yazılan extension metodu burada çağrılıyor; `DbContext`, repository ve `TimeProvider` tek satırda kaydoluyor.
* `builder.Services.AddScoped<VideoJobService>();` — `VideoJobService`'in kendisi bir arayüz üzerinden değil, doğrudan somut tip olarak kaydedildi (henüz birden fazla implementasyonu olmayacağı için arayüze gerek görülmedi). `Scoped` seçildi çünkü içinde `Scoped` olan `IVideoJobRepository`'ye (dolayısıyla `DbContext`'e) bağımlı — bir `Scoped` servisin içine bir `Singleton`'ı koymak sorun değil, ama tersi (bir `Singleton`'ın içine `Scoped` koymak) ciddi hatalara yol açar; bu yüzden `VideoJobService`'in ömrü, bağımlı olduğu en "kısa ömürlü" servisle uyumlu seçildi.
* ```csharp
  using (var scope = app.Services.CreateScope())
  {
      var db = scope.ServiceProvider.GetRequiredService<ShortsGeneratorDbContext>();
      db.Database.Migrate();
  }
  ```
  Day 4'te elle `dotnet ef database update` çalıştırmıştık; burada aynı işi **uygulama her açıldığında otomatik** yapıyoruz. `db.Database.Migrate()`, henüz uygulanmamış migration varsa onları SQLite dosyasına uygular (veritabanı dosyası hiç yoksa önce onu da oluşturur), zaten uygulanmışsa hiçbir şey yapmaz (bkz. bölüm 13'teki `__EFMigrationsHistory` açıklaması — Day 4'te de görmüştük). Bunun güvenli olmasının sebebi koddaki yorum satırında açıklandı: tek bir yerel kullanıcı, tek bir süreç — production'da birden fazla instance aynı anda ayağa kalkıp migration'ı yarışarak çalıştırma riski burada yok.
* `app.MapPost("/api/videos", async (CreateVideoJobRequest request, VideoJobService service, CancellationToken ct) => { ... })` — minimal API'nin **parametre enjeksiyonu**: `request` gövdeden (body) JSON olarak otomatik deserialize ediliyor, `service` DI container'dan otomatik çözülüyor, `ct` ise framework'ün isteğin iptal token'ını otomatik sağladığı özel bir parametre tipi. Hiçbir yerde elle `services.GetService<VideoJobService>()` ya da JSON parse kodu yazılmadı — bunların hepsi minimal API'nin "model binding" mekanizmasıyla otomatik.
* `try { ... } catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }` — `VideoJob.Create(...)` (bölüm 3) boş `topic` için `ArgumentException` fırlatıyordu; bu endpoint o exception'ı yakalayıp kullanıcıya 500 (sunucu hatası) yerine anlamlı bir 400 (istemci hatası) + açıklama döndürüyor. `new { error = ex.Message }` — **anonymous type**: adı olmayan, sadece bu satırda var olan geçici bir nesne; minimal API'ler bunu da JSON'a çevirebiliyor.
* `app.MapPost(...).WithName("CreateVideoJob")` ve `Results.Created($"/api/videos/{job.Id}", ...)` — `Results.Created`, HTTP 201 durum kodunu, yeni oluşturulan kaynağın adresini (`Location` header) ve kaynağın kendisini birlikte döndürüyor — REST sözleşmesinde "bir şey başarıyla oluşturuldu" için standart cevap şekli.
* `app.MapGet("/api/videos/{id:guid}", async (Guid id, ...) => { ... })` — `{id:guid}` route'taki **route constraint**: bu segment sadece geçerli bir `Guid` formatındaysa eşleşiyor; geçersiz bir değer (`/api/videos/abc`) gelirse bu route hiç eşleşmeyip otomatik 404 dönüyor — elle format doğrulaması yazmaya gerek kalmadı.
* `return job is null ? Results.NotFound() : Results.Ok(...);` — `is null` pattern-matching null kontrolü (Day 2'de `other is null` ile aynı syntax), bulunamayan bir id için 404, bulunan için 200 + DTO döndürüyor.

---

## 12. İlk migration: `dotnet ef migrations add InitialCreate`

```
dotnet ef migrations add InitialCreate --project src/ShortsGenerator.Infrastructure --startup-project src/ShortsGenerator.Api --output-dir Migrations
```

Üretilen `Migrations/20260915170918_InitialCreate.cs` (ilgili kısım):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateTable(
        name: "VideoJobs",
        columns: table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            Topic = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
            SourceScript = table.Column<string>(type: "TEXT", nullable: true),
            GeneratedScript = table.Column<string>(type: "TEXT", nullable: true),
            Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
            CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
            UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
            ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
            OutputDirectory = table.Column<string>(type: "TEXT", nullable: true),
            FinalVideoPath = table.Column<string>(type: "TEXT", nullable: true)
        },
        constraints: table => { table.PrimaryKey("PK_VideoJobs", x => x.Id); });
}
```

**Neden:** Bölüm 6'daki `OnModelCreating`'in SQL şemasına dönüşmüş hali. Not: bu dosya aslında bugün **iki kez** üretildi — ilk üretimde `CreatedAt`/`UpdatedAt` `TEXT` (ham `DateTimeOffset`) olarak çıkmıştı; bölüm 13'teki hata bulunduktan sonra migration silinip (`dotnet ef migrations remove`) `ValueConverter` eklenmiş haliyle yeniden üretildi. Aşağıda görünen, **düzeltilmiş** son hali.

**Nasıl çalışıyor:**
* `--project src/ShortsGenerator.Infrastructure --startup-project src/ShortsGenerator.Api` — `dotnet ef` komutları her zaman iki proje ister: migration dosyalarının **yazılacağı** proje (`--project`, burada Infrastructure) ve uygulamayı gerçekten ayağa kaldırıp `DbContext`'i çözümleyebilecek **başlangıç** projesi (`--startup-project`, burada Api — çünkü connection string ve DI kaydı orada).
* `Id = table.Column<Guid>(type: "TEXT", nullable: false)` — SQLite'ın `GUID` diye bir native tipi yok; EF Core bunu metin (`TEXT`) olarak saklıyor (36 karakterlik standart GUID string gösterimi).
* `CreatedAt`/`UpdatedAt` → `table.Column<long>(type: "INTEGER", ...)` — bölüm 6/13'teki `ValueConverter`'ın doğrudan sonucu: C# tarafında `DateTimeOffset`, SQL tarafında `INTEGER` (UTC tick sayısı) olarak saklanıyor.
* `Status = table.Column<string>(type: "TEXT", maxLength: 50, ...)` — bölüm 6'daki `HasConversion<string>()`'in sonucu: enum, sayı değil okunabilir metin olarak saklanıyor.
* Bu dosyanın altında ayrıca bir `Down(MigrationBuilder migrationBuilder)` metodu var (`DropTable("VideoJobs")`) — migration'ı geri almak (`dotnet ef database update <önceki migration>`) istenirse ne yapılacağını tanımlıyor; bugün kullanılmadı ama EF Core her migration için bunu otomatik üretiyor.

---

## 13. Bulunan hata #1 — SQLite `DateTimeOffset`'i `ORDER BY`'da desteklemiyor

İlk haliyle `dotnet test` çalıştırıldığında `ListRecentAsync_ReturnsNewestFirst` testi şu hatayla patladı:

```
System.NotSupportedException: SQLite does not support expressions of type
'DateTimeOffset' in ORDER BY clauses. Convert the values to a supported
type, or use LINQ to Objects to order the results on the client side.
```

Düzeltme, bölüm 6'da gösterilen `ShortsGeneratorDbContext.cs`'teki şu ekleme:

```csharp
var dateTimeOffsetConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, long>(
    v => v.UtcTicks,
    v => new DateTimeOffset(v, TimeSpan.Zero));

entity.Property(job => job.CreatedAt).HasConversion(dateTimeOffsetConverter).IsRequired();
entity.Property(job => job.UpdatedAt).HasConversion(dateTimeOffsetConverter).IsRequired();
```

Bu model değişikliğinden sonra migration da yeniden üretilmesi gerekti:

```
dotnet ef migrations remove --project src/ShortsGenerator.Infrastructure --startup-project src/ShortsGenerator.Api
dotnet ef migrations add InitialCreate --project src/ShortsGenerator.Infrastructure --startup-project src/ShortsGenerator.Api --output-dir Migrations
```

**Neden:** SQL Server'ın aksine (Day 4'te `DateTimeOffset` sorunsuz çalışmıştı), SQLite'ın kendi native bir "tarih+saat dilimi" tipi yok ve EF Core'un SQLite provider'ı, `DateTimeOffset` üzerinde `ORDER BY` çevirisini desteklemiyor. Bu, `VideoJobRepository.ListRecentAsync`'in (bölüm 7) `OrderByDescending(job => job.CreatedAt)` satırında gerçek bir SQL çalıştırılmaya çalışıldığında ortaya çıkan, **gerçek bir veritabanı sağlayıcısına özgü sınırlama** — koddaki bir yazım hatası değil.

**Nasıl çalışıyor:** `ValueConverter<DateTimeOffset, long>`, EF Core'a "bu property'yi veritabanına yazarken şu fonksiyonla (`v => v.UtcTicks`) dönüştür, okurken şu fonksiyonla (`v => new DateTimeOffset(v, TimeSpan.Zero)`) geri çevir" diyen genel bir mekanizma. `UtcTicks`, bir `DateTimeOffset`'in UTC'ye göre 100 nanosaniyelik birimler cinsinden tam sayı karşılığı — iki `long` değeri karşılaştırmak/sıralamak SQLite için trivial, bu yüzden `ORDER BY` artık sorunsuz çalışıyor. Migration'ın silinip yeniden üretilmesinin sebebi: model (C# tarafı) değiştiği için üretilen SQL şeması da değişti (`TEXT` yerine `INTEGER`), ve bu migration daha önce hiçbir yerde gerçekten kullanılmadığı (sadece bu oturumda üretilip hemen test edildiği) için, üstüne ikinci bir migration eklemek yerine baştan temiz üretmek tercih edildi.

---

## 14. `tests/ShortsGenerator.Domain.Tests/VideoJobTests.cs`

```csharp
using ShortsGenerator.Domain;

namespace ShortsGenerator.Domain.Tests;

public class VideoJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidTopic_StartsInCreatedStatus()
    {
        var job = VideoJob.Create("Why Your Pull-Ups Aren't Increasing", null, Now);

        Assert.Equal(VideoJobStatus.Created, job.Status);
        Assert.Equal("Why Your Pull-Ups Aren't Increasing", job.Topic);
        Assert.Equal(Now, job.CreatedAt);
        Assert.Equal(Now, job.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithoutTopic_Throws(string? topic)
    {
        Assert.Throws<ArgumentException>(() => VideoJob.Create(topic!, null, Now));
    }

    [Fact]
    public void HappyPath_MovesThroughAllStatusesInOrder()
    {
        var job = VideoJob.Create("Topic", null, Now);

        job.MarkPlanning(Now);
        Assert.Equal(VideoJobStatus.Planning, job.Status);

        job.MarkNarrating("generated script", Now);
        Assert.Equal(VideoJobStatus.Narrating, job.Status);
        Assert.Equal("generated script", job.GeneratedScript);

        job.MarkRendering(Now);
        Assert.Equal(VideoJobStatus.Rendering, job.Status);

        job.MarkCompleted("output/job/final.mp4", Now);
        Assert.Equal(VideoJobStatus.Completed, job.Status);
        Assert.Equal("output/job/final.mp4", job.FinalVideoPath);
    }

    [Fact]
    public void MarkRendering_FromCreated_Throws()
    {
        var job = VideoJob.Create("Topic", null, Now);

        Assert.Throws<InvalidOperationException>(() => job.MarkRendering(Now));
    }

    [Theory]
    [InlineData(VideoJobStatus.Created)]
    [InlineData(VideoJobStatus.Planning)]
    [InlineData(VideoJobStatus.Narrating)]
    [InlineData(VideoJobStatus.Rendering)]
    public void MarkFailed_FromAnyNonTerminalStatus_Succeeds(VideoJobStatus startStatus)
    {
        var job = VideoJob.Create("Topic", null, Now);
        AdvanceTo(job, startStatus);

        job.MarkFailed("Ollama unavailable", Now);

        Assert.Equal(VideoJobStatus.Failed, job.Status);
        Assert.Equal("Ollama unavailable", job.ErrorMessage);
    }

    private static void AdvanceTo(VideoJob job, VideoJobStatus status)
    {
        if (status is VideoJobStatus.Created) return;
        job.MarkPlanning(Now);
        if (status is VideoJobStatus.Planning) return;
        job.MarkNarrating("script", Now);
        if (status is VideoJobStatus.Narrating) return;
        job.MarkRendering(Now);
    }
}
```

(Dosyanın tamamı 13 test içeriyor; yukarıda en öğretici olanlar seçildi — `MarkCompleted_FromNarrating_Throws`, `MarkFailed_FromCompleted_Throws`, `MarkFailed_FromFailed_Throws` benzer desenle devam ediyor.)

**Neden:** Bölüm 3'teki durum geçiş kurallarının **gerçekten** doğru çalıştığını kanıtlamak — hem "doğru yoldan geçiş çalışıyor mu" (happy path) hem "yanlış yoldan geçiş engelleniyor mu" (negative path) test edilmeden, `TransitionTo`'daki mantığın doğruluğuna güvenilemez.

**Nasıl çalışıyor:**
* `private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);` — testler için **sabit** bir zaman damgası; `DateTimeOffset.UtcNow` kullansaydık testler her çalıştırıldığında farklı (ama önemsiz) bir değerle çalışırdı. `VideoJob.Create`'in `now` parametresini dışarıdan almasının asıl faydası tam burada ortaya çıkıyor (bölüm 5'teki `TimeProvider` ile aynı motivasyon).
* `[Theory]` + `[InlineData(...)]` — `[Fact]`'ten farklı olarak **parametreli** bir test tanımlıyor: xUnit, her `InlineData` satırı için testi ayrı ayrı çalıştırıyor. `Create_WithoutTopic_Throws` üç farklı geçersiz girdiyi (`""`, `"   "`, `null`) **tek bir test metodunda** kapsıyor — üç ayrı `[Fact]` yazmaktan daha az tekrar.
* `Assert.Throws<ArgumentException>(() => VideoJob.Create(topic!, null, Now));` — bir lambda içine sarılmış çağrının, çalıştırıldığında beklenen tipte bir exception fırlattığını doğruluyor; fırlatmazsa ya da yanlış tipte bir exception fırlatırsa test kırmızı olur. `topic!` — **null-forgiving operatör** (`!`), derleyiciye "bu değerin burada null olabileceğini biliyorum, bilerek böyle çağırıyorum, uyarı verme" diyor (test bilerek geçersiz girdi veriyor).
* `MarkFailed_FromAnyNonTerminalStatus_Succeeds` + `AdvanceTo` yardımcı metodu — dört farklı başlangıç durumunu (`Created`, `Planning`, `Narrating`, `Rendering`) tek bir `[Theory]` ile kapsıyor; `AdvanceTo`, testin kurulum (arrange) kısmını job'u istenen duruma "sırayla" getirerek sadeleştiriyor — her teste aynı beş satırı kopyalamak yerine.

---

## 15. `tests/ShortsGenerator.Infrastructure.Tests/VideoJobRepositoryTests.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure;

namespace ShortsGenerator.Infrastructure.Tests;

public class VideoJobRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ShortsGeneratorDbContext _dbContext;
    private readonly VideoJobRepository _repository;

    public VideoJobRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"shortsgenerator-tests-{Guid.NewGuid()}.db");
        var options = new DbContextOptionsBuilder<ShortsGeneratorDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _dbContext = new ShortsGeneratorDbContext(options);
        _dbContext.Database.Migrate();
        _repository = new VideoJobRepository(_dbContext);
    }

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_ReturnsEquivalentJob()
    {
        var now = DateTimeOffset.UtcNow;
        var job = VideoJob.Create("Why Your Pull-Ups Aren't Increasing", "optional script", now);

        await _repository.AddAsync(job, CancellationToken.None);

        // A fresh DbContext proves the data actually round-tripped through SQLite,
        // not just EF Core's in-memory change tracker.
        await using var freshDbContext = new ShortsGeneratorDbContext(
            new DbContextOptionsBuilder<ShortsGeneratorDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var freshRepository = new VideoJobRepository(freshDbContext);

        var loaded = await freshRepository.GetByIdAsync(job.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(job.Id, loaded!.Id);
        Assert.Equal(job.Topic, loaded.Topic);
        Assert.Equal(job.SourceScript, loaded.SourceScript);
        Assert.Equal(VideoJobStatus.Created, loaded.Status);
    }

    [Fact]
    public async Task ListRecentAsync_ReturnsNewestFirst()
    {
        var older = VideoJob.Create("Older topic", null, DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = VideoJob.Create("Newer topic", null, DateTimeOffset.UtcNow);
        await _repository.AddAsync(older, CancellationToken.None);
        await _repository.AddAsync(newer, CancellationToken.None);

        var recent = await _repository.ListRecentAsync(10, CancellationToken.None);

        Assert.Equal(newer.Id, recent[0].Id);
        Assert.Equal(older.Id, recent[1].Id);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
```

**Neden:** Domain testleri (bölüm 14) `VideoJob`'ın kurallarını bellekte, hiç veritabanına dokunmadan doğruluyordu. Bu testler ise bambaşka bir şeyi kanıtlıyor: verinin **gerçekten SQLite'a yazılıp geri okunabildiğini**. `VideoJobTests` yeşil olsa bile, `VideoJobRepository`'deki bir LINQ çevirisi (tam olarak bölüm 13'te bulunan hata gibi) hâlâ production'da patlayabilirdi — bu yüzden gerçek bir SQLite dosyasına karşı çalışan ayrı bir test katmanı gerekli.

**Nasıl çalışıyor:**
* `_dbPath = Path.Combine(Path.GetTempPath(), $"shortsgenerator-tests-{Guid.NewGuid()}.db");` — her test **kendi**, rastgele isimli, geçici bir SQLite dosyası kullanıyor. `Guid.NewGuid()` her çağrıda farklı bir değer ürettiği için, testler paralel çalışsa bile birbirinin dosyasını ezme riski yok.
* Constructor'da `_dbContext.Database.Migrate();` çağrılıyor — `Program.cs`'teki startup migration'ın (bölüm 11) birebir aynısı, ama burada her test kendi boş veritabanını kendi migrate ediyor.
* `await using var freshDbContext = new ShortsGeneratorDbContext(...)` — **bilerek** ikinci, bağımsız bir `DbContext` örneği açılıyor. Sebebi yorum satırında açık: EF Core, aynı `DbContext` içinde daha önce eklenen bir nesneyi sorgularken bazen gerçek SQL'e hiç gitmeden kendi bellek-içi "change tracker"ından cevap verebilir — bu durumda test, veri gerçekten diske yazılmasa bile yanlışlıkla yeşil görünebilirdi. Taze bir `DbContext` ile okumak, bu kestirmeyi devre dışı bırakıp **gerçek round-trip**'i (yaz → kapat → başka bir bağlantıyla oku) zorluyor. `await using` — `IAsyncDisposable` uygulayan nesneler için `using`'in asenkron hali; scope sonunda `DisposeAsync()` otomatik çağrılıyor.
* `ListRecentAsync_ReturnsNewestFirst` — bölüm 13'te düzeltilen `ORDER BY` hatasının **regresyon testi**: bu test olmasaydı, biri ileride `ValueConverter`'ı yanlışlıkla kaldırırsa hata sessizce geri gelebilirdi.
* `Dispose()` + `IDisposable` — xUnit, bir test sınıfı `IDisposable` uyguluyorsa, **her testten sonra** `Dispose()`'u otomatik çağırır (constructor da her testten **önce** yeniden çalışır — yani her `[Fact]` sıfırdan bir `VideoJobRepositoryTests` örneği alır, testler birbirinin verisini görmez). Burada `Dispose()`, kullanılan geçici `.db` dosyasını siliyor — aşağıda (bölüm 16) bunun ilk halinin neden yetersiz kaldığı anlatılıyor.

---

## 16. Bulunan hata #2 — Windows'ta SQLite bağlantı havuzu dosyayı kilitli tutuyor

İlk `Dispose()` sadece şuydu:

```csharp
public void Dispose()
{
    _dbContext.Dispose();
    if (File.Exists(_dbPath)) File.Delete(_dbPath);
}
```

`dotnet test` çalıştırıldığında:

```
System.IO.IOException : The process cannot access the file
'...\shortsgenerator-tests-....db' because it is being used by another process.
```

Düzeltme (bölüm 15'teki son hal):

```csharp
public void Dispose()
{
    _dbContext.Dispose();
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    if (File.Exists(_dbPath)) File.Delete(_dbPath);
}
```

**Neden:** `Microsoft.Data.Sqlite`, performans için bağlantıları varsayılan olarak **havuzluyor** (connection pooling) — `_dbContext.Dispose()` çağrıldığında C# tarafındaki nesne serbest kalıyor gibi görünse de, işletim sistemi seviyesinde dosya tutamacı (file handle) havuzda **açık kalmaya devam ediyor** (bir sonraki bağlantı isteğinde yeniden kullanılabilsin diye). Windows, Linux'un aksine açık bir dosyanın silinmesine izin vermiyor — bu yüzden `File.Delete(_dbPath)` "dosya başka bir process tarafından kullanılıyor" hatasıyla patlıyordu.

**Nasıl çalışıyor:** `SqliteConnection.ClearAllPools()`, tüm SQLite bağlantı havuzlarını (işlemdeki tüm `DbContext`'ler için) zorla boşaltıp gerçek dosya tutamaçlarını kapatan statik bir metot. `_dbContext.Dispose()`'dan **sonra**, `File.Delete(...)`'dan **önce** çağrılması gerekiyor — sıralama önemli: önce C# nesnesi serbest bırakılıyor, sonra havuz tamamen temizleniyor, en son dosya gerçekten silinebiliyor.

---

## 17. Bulunan hata #3 — entegrasyon testi geliştiricinin gerçek veritabanını kullanıyordu

Bölüm 13'teki model değişikliğinden sonra `dotnet test` çalıştırıldığında, Day 1'den kalan `HealthEndpointTests` de kırmızı oldu:

```
Microsoft.Data.Sqlite.SqliteException : SQLite Error 1: 'table "VideoJobs" already exists'.
```

Day 1'deki hali basitçe `_factory.CreateClient()` çağırıyordu — bu da `Program.cs`'i, `appsettings.json`'daki **gerçek** `ConnectionStrings:Default` (`Data Source=shortsgenerator.db`) ile çalıştırıyordu. İlk test çalıştırmasında bu dosyaya eski (düzeltme öncesi) migration uygulanmış, sonra migration silinip yeniden üretilince (bölüm 13), EF Core bu dosyada `VideoJobs` tablosunun zaten var olduğunu ama kendi migration geçmişinde (`__EFMigrationsHistory`) bu migration'ı tanımadığını görüp yeniden `CREATE TABLE` denemiş ve çakışmıştı.

Düzeltme — `tests/ShortsGenerator.Api.Tests/HealthEndpointTests.cs`'in son hali:

```csharp
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ShortsGenerator.Api.Tests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shortsgenerator-api-tests-{Guid.NewGuid()}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        // Point this test run at its own throwaway SQLite file instead of the
        // developer's real local database, so tests never share or corrupt state.
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configBuilder) =>
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                })));
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

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
```

**Neden:** Bu, bugünün en önemli dersi: bir entegrasyon testi, geliştiricinin **gerçek** yerel verisini asla paylaşmamalı. `shortsgenerator.db`, az önce elle `dotnet run` ile denediğimiz, gerçek bir job'ın yazıldığı dosyaydı (ya da testin kendi çalışma dizinindeki bir kopyası) — testler onu kullanmaya devam etseydi hem testler arasında "kirli" paylaşılan durum oluşurdu hem de (daha kötüsü) testler, geliştirme ortamındaki gerçek veriyi bozabilirdi.

**Nasıl çalışıyor:**
* `factory.WithWebHostBuilder(builder => ...)` — `WebApplicationFactory<Program>`'ın, test için **özelleştirilmiş** bir kopyasını üreten metodu; `Program.cs`'in geri kalanına dokunmadan, sadece host kurulumunu (burada: konfigürasyonu) override etmeyi sağlıyor.
* `builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = $"Data Source={_dbPath}" }))` — ASP.NET Core'un konfigürasyon sistemi **katmanlıdır**: `appsettings.json` önce okunur, sonra sırasıyla eklenen her kaynak öncekini **üzerine yazabilir**. `AddInMemoryCollection(...)`, test zamanında eklenen bu son katman olduğu için, `appsettings.json`'daki gerçek `ConnectionStrings:Default` değerini, test çalıştığı sürece bu geçici dosya yoluyla **geçersiz kılıyor** — gerçek dosyaya hiç dokunulmuyor.
* `IDisposable` + `Dispose()` — bölüm 15/16'daki aynı desen: her test kendi geçici dosyasını temizliyor, aynı `SqliteConnection.ClearAllPools()` çözümü burada da gerekli (aynı Windows dosya kilidi sorunu).
* Bu değişiklik, önceki günün (Day 1) bir dosyasına geri dönüp onu bugünün bilgisiyle düzeltmek — tıpkı referans aldığımız formatta Day 3'ün Day 2'nin `Program.cs`'ine geri dönüp geçici bloğu kaldırması gibi: bir günün kodu, sonraki günün ihtiyaçlarıyla haklı sebeplerle değişebilir.

---

## 18. `.gitignore` güncellemesi

```diff
 # Local databases
 *.db
+*.db-shm
+*.db-wal
 *.sqlite
 *.sqlite3
```

**Neden:** `git status` çalıştırıldığında `shortsgenerator.db-shm` ve `shortsgenerator.db-wal` dosyalarının takip edilmemiş (`??`) olarak göründüğü fark edildi. Bunlar SQLite'ın **WAL (Write-Ahead Log)** modunda ürettiği yardımcı dosyalar — asıl `.db` dosyası `.gitignore`'da vardı ama bu iki uzantı unutulmuştu.

**Nasıl çalışıyor:** SQLite, her yazma işleminde doğrudan ana dosyayı kilitleyip değiştirmek yerine (varsayılan modda bu da olur, ama WAL modunda) değişiklikleri önce `-wal` uzantılı bir günlük dosyasına yazar, `-shm` ise bu günlüğün paylaşılan bellek indeksidir; belirli aralıklarla bu günlük ana `.db` dosyasına "checkpoint" edilir. İkisi de tamamen yerel, yeniden üretilebilir çalışma zamanı dosyaları — `bin/`/`obj/` ile aynı kategoride, depoya ait değiller.

---

## Doğrulanan davranış

```
dotnet build  → 0 Uyarı, 0 Hata (7 proje: Domain, Application, Infrastructure, Api,
                 Domain.Tests, Infrastructure.Tests, Api.Tests)

dotnet test   → ShortsGenerator.Domain.Tests.dll         : 13 Başarılı
                ShortsGenerator.Infrastructure.Tests.dll : 3 Başarılı
                ShortsGenerator.Api.Tests.dll             : 1 Başarılı
                Toplam: 17 Başarısız: 0

Gerçek dotnet run ile manuel doğrulama:
POST /api/videos  {"topic":"Why Your Pull-Ups Aren't Increasing"}
→ 201 Created
  {"id":"dabbe927-...","topic":"Why Your Pull-Ups Aren't Increasing","sourceScript":null,
   "generatedScript":null,"status":"Created","createdAt":"2026-09-15T17:10:36...",
   "updatedAt":"2026-09-15T17:10:36...","errorMessage":null,"finalVideoPath":null}

GET /api/videos/dabbe927-...   → 200, yukarıdakiyle birebir aynı job
GET /api/videos/00000000-0000-0000-0000-000000000000 → 404

src/ShortsGenerator.Api/shortsgenerator.db dosyası diskte oluştu (git tarafından ignore ediliyor).
```

Bu çıktı sırasıyla şunu kanıtlıyor: `VideoJob.Create` ile oluşturulan bir job gerçekten SQLite'a yazılıyor (`POST`), farklı bir HTTP isteğinde (yeni bir `DbContext`/scope ile) geri okunabiliyor (`GET`) — yani kalıcılık gerçek, sadece bellek içi bir önbellek değil — ve bilinmeyen bir id için route constraint + `is null` kontrolü doğru şekilde 404 üretiyor. 17 testin tamamının yeşil olması, hem domain kurallarının (13 test) hem gerçek SQLite round-trip'inin (3 test) hem de HTTP katmanının kendi izole veritabanıyla (1 test) doğru çalıştığını ayrı ayrı doğruluyor.
