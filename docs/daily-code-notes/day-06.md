# Day 6 — Kod Notları

30 günlük roadmap, Gün 6. Konu: orijinal bir placeholder asset kütüphanesi (maskot + egzersiz/ekipman/anatomi SVG'leri), bir JSON manifest üzerinden `IAssetRepository` soyutlaması, ve Day 5'ten kalan "planner sadece bilinen asset'leri seçebilmeli" eksiğinin tamamlanması.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. `src/ShortsGenerator.Domain/AssetOrientation.cs`, `AssetMetadata.cs`, `IAssetRepository.cs`

```csharp
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AssetOrientation { Square, Portrait, Landscape, Any }

public sealed record AssetMetadata(
    string Id,
    string FilePath,
    string Category,
    IReadOnlyList<string> Tags,
    IReadOnlyList<SceneType> CompatibleSceneTypes,
    AssetOrientation Orientation,
    string? Anchor);

public interface IAssetRepository
{
    Task<IReadOnlyList<AssetMetadata>> GetAllAsync(CancellationToken cancellationToken);
    Task<AssetMetadata?> GetByIdAsync(string id, CancellationToken cancellationToken);
}
```

**Neden:** `CLAUDE.md` §10'daki asset metadata şemasının (id, dosya yolu, kategori, etiketler, uyumlu sahne tipleri, yönelim, anchor) birebir karşılığı. `IAssetRepository`, `CLAUDE.md` §21'de **isim isim** geçen arayüzlerden biri — Day 2/3'teki `IVideoJobRepository`/`ILlmProvider` ile aynı kural: Domain sözleşmeyi tanımlar, Infrastructure (bölüm 3) dosya sisteminden nasıl okuyacağını bilir.

**Nasıl çalışıyor:** `CompatibleSceneTypes`, Day 5'in `SceneType` enum'unu doğrudan kullanıyor — iki gün önce kurulan kontrollü sözlük, bugün asset sistemiyle doğal olarak bağlanıyor. `Category`'nin geniş bir gruplama (`"mascot"`, `"exercise"`, `"equipment"`, `"anatomy"`) olmasına, `Id`'nin ise her bir pozun/varyantın kendine özgü kimliği (`"mascot-idle"`, `"pullup-top"`) olmasına bilinçli olarak karar verildi — `CLAUDE.md`'nin örnek kategori listesi (`mascot_idle`, `pullup_top` gibi) aslında bu ikisinin birleşimiydi, biz onu ayrıştırdık.

---

## 2. `src/ShortsGenerator.Domain/VideoPlanValidator.cs` — `knownAssetIds` parametresi

```diff
-    public static ValidationResult Validate(VideoPlan plan, string expectedNarrationText)
+    public static ValidationResult Validate(
+        VideoPlan plan, string expectedNarrationText, IReadOnlySet<string> knownAssetIds)
```

```csharp
foreach (var assetId in scene.AssetIds)
{
    if (!AssetIdPattern.IsMatch(assetId))
    {
        reasons.Add($"Scene '{scene.Id}' has an invalid asset id '{assetId}' (use lowercase letters, digits, '-', '_' only).");
    }
    else if (!knownAssetIds.Contains(assetId))
    {
        reasons.Add($"Scene '{scene.Id}' references unknown asset id '{assetId}'. Only asset ids from the provided catalog may be used.");
    }
}
```

**Neden:** Day 5'in notlarında açıkça yazılmıştı: "Asset IDs don't exist yet (the manifest arrives Day 6) - this only rejects obviously unsafe/malformed values." Bugün o manifest geldi, bu yüzden format kontrolü yetersiz kaldı — roadmap'in Day 6 kabul kriteri tam olarak bu: "planner can only choose known assets."

**Nasıl çalışıyor:** `else if` kullanılması bilinçli — format hatalı bir ID (`"../etc/passwd"`) için **tek bir** sebep raporlanıyor ("invalid asset id"), hem format hem "bilinmiyor" diye iki kez şikayet edilmiyor. Format geçerli ama katalogda olmayan bir ID (`"mascot-foo"`) için ise farklı, daha spesifik bir mesaj ("unknown asset id") veriliyor — LLM'e geri bildirim verirken "ID'nin şekli yanlış" ile "ID'nin kendisi uydurma" birbirinden ayrı, farklı düzeltmeler gerektiren hatalar.

---

## 3. `src/ShortsGenerator.Infrastructure/Assets/` — manifest okuyucu

```csharp
// AssetManifestOptions.cs
public sealed class AssetManifestOptions
{
    public const string SectionName = "AssetManifest";
    public string ManifestPath { get; set; } = "../../assets/manifests/assets.json";
}
```

```csharp
// JsonAssetRepository.cs (constructor)
public JsonAssetRepository(IOptions<AssetManifestOptions> options)
{
    var path = options.Value.ManifestPath;
    if (!File.Exists(path)) throw new InvalidOperationException($"Asset manifest not found at '{Path.GetFullPath(path)}'. ...");

    var json = File.ReadAllText(path);
    List<AssetMetadata>? rawAssets;
    try { rawAssets = JsonSerializer.Deserialize<List<AssetMetadata>>(json, JsonOptions); }
    catch (JsonException ex) { throw new InvalidOperationException($"Asset manifest at '{path}' is not valid JSON.", ex); }

    if (rawAssets is null || rawAssets.Count == 0)
        throw new InvalidOperationException($"Asset manifest at '{path}' is empty.");

    var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
    var assets = rawAssets
        .Select(a => a with { FilePath = Path.GetFullPath(Path.Combine(manifestDirectory, a.FilePath)) })
        .ToList();

    foreach (var asset in assets)
    {
        if (!File.Exists(asset.FilePath))
            throw new InvalidOperationException($"Asset '{asset.Id}' in the manifest points to a missing file: '{asset.FilePath}'.");
    }

    _assets = assets;
    _byId = assets.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
}
```

**Neden:** `CLAUDE.md` §16, hata örnekleri arasında açıkça "asset missing" diye bir durum sayıyor — ve bu hatanın bir kullanıcı isteğinin ortasında, belirsiz bir şekilde değil, **uygulama açılırken, net bir mesajla** ortaya çıkması gerekiyor. Bu sınıf, Day 2'nin `db.Database.Migrate()` ile aynı "fail fast" felsefesini izliyor: `Singleton` olarak kaydedildiği ve `Program.cs`'te açılışta elle çözümlendiği (bölüm 7) için, manifest bozuksa uygulama **hiç ayağa kalkmıyor**.

**Nasıl çalışıyor (yol çözümleme — bu günün en ince noktası):**
```csharp
var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
var assets = rawAssets.Select(a => a with { FilePath = Path.GetFullPath(Path.Combine(manifestDirectory, a.FilePath)) }).ToList();
```
Manifest dosyasındaki her `filePath` (örn. `"../mascot/mascot-idle.svg"`), **manifest dosyasının kendi konumuna göre** yazıldı — API'nin çalışma dizinine göre değil. Neden: `assets/` klasörü repo kökünde yaşıyor (`CLAUDE.md`'nin hedef yapısına uygun), ama API'nin çalışma dizini `src/ShortsGenerator.Api` (`dotnet run --project ...` bu projenin klasörüne "gidiyor"). Eğer manifest'teki yollar API'nin çalışma dizinine göre yazılsaydı, ileride farklı bir süreç (örneğin Day 7+'daki Remotion renderer, kendi çalışma dizininden) aynı manifest'i okuduğunda yollar **yanlış** olurdu. Bunun yerine: `Path.GetDirectoryName(Path.GetFullPath(path))` ile manifest'in **kendi** mutlak klasörünü buluyoruz, her `filePath`'i buna göre birleştirip (`Path.Combine`) tekrar mutlak hale getiriyoruz (`Path.GetFullPath`). Sonuç: `_assets` içindeki her `FilePath`, **hangi süreç tarafından okunursa okunsun** her zaman doğru, mutlak bir yol — bu, manifest'i "kendi kendine yetebilen, taşınabilir" bir açıklama haline getiriyor.

`AssetServiceCollectionExtensions.AddAssetRepository(...)`, Day 2/3/5'teki diğer `Add*` extension metotlarıyla aynı desen: `AssetManifestOptions`'ı config'den bağlıyor, `IAssetRepository`'yi `Singleton` olarak kaydediyor.

---

## 4. Orijinal placeholder SVG'ler ve manifest

```
assets/mascot/mascot-idle.svg       (ve thinking, tired, happy)
assets/exercises/pullup-top.svg     (ve pullup-bottom, pushup-top, pushup-bottom)
assets/props/dumbbell.svg           (ve pullup-bar)
assets/anatomy/biceps-highlight.svg (ve triceps-highlight)
assets/manifests/assets.json        (12 asset'in tam metadata'sı)
```

Örnek — `mascot-idle.svg` (basitleştirilmiş):
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 240">
  <rect x="64" y="96" width="72" height="88" rx="34" fill="#2DD4BF"/>
  <circle cx="100" cy="58" r="42" fill="#2DD4BF"/>
  <rect x="58" y="36" width="84" height="12" rx="6" fill="#FB923C"/>
  <circle cx="85" cy="58" r="6" fill="#134E4A"/>
  <circle cx="115" cy="58" r="6" fill="#134E4A"/>
  <path d="M90 76 Q100 82 110 76" stroke="#134E4A" stroke-width="4" stroke-linecap="round" fill="none"/>
</svg>
```

**Neden:** `CLAUDE.md` §4, orijinal bir görsel kimlik şartı koyuyor: **sarı olmayan**, başka bir maskotu taklit etmeyen, ucuza animasyonlanabilir basit bir karakter. "Spark" adını verdiğim bu maskot, teal (`#2DD4BF`) + turuncu (`#FB923C`) paletiyle, sadece daire/kapsül şekillerinden oluşuyor — hiçbir AI görsel üretim aracı kullanılmadı (`CLAUDE.md`'nin "her sahne için yeni bir AI görseli üretme" kuralına uygun), elle yazılmış düz SVG. Dört poz (idle/thinking/tired/happy), aynı temel şekillerin küçük varyasyonları (göz şekli, ağız eğrisi, kol açısı) ile elde edildi — "ucuza animasyonlanabilir" demenin somut karşılığı.

**Nasıl çalışıyor:** Her SVG `viewBox="0 0 200 240"` ile sabit bir koordinat sistemi kullanıyor; `rx`/`ry` ile yuvarlatılmış köşeler (kapsül görünümü), `transform="rotate(...)"` ile kollar farklı açılarda konumlandırılıyor (örn. "happy" pozunda kollar yukarı kalkık). Egzersiz placeholder'ları (pullup/pushup) bilinçli olarak **soyut** — gerçekçi bir insan figürü değil, basit çizgi/daire kombinasyonları; `CLAUDE.md`'nin "Do NOT block engineering progress waiting for beautiful final art" talimatına birebir uyuyor.

`assets/manifests/assets.json`'daki her giriş, bölüm 1'deki `AssetMetadata` şemasıyla birebir eşleşiyor; `compatibleSceneTypes` alanı, Day 5'in `SceneType` enum değerlerinden seçiliyor (örn. `mascot-happy` → `["Outro", "Progress"]`).

---

## 5. `ScenePlanPromptBuilder.cs` — asset kataloğu prompt'a ekleniyor

```diff
     public static string Build(
         string topic,
         string narrationText,
+        IReadOnlyList<AssetMetadata> knownAssets,
         IReadOnlyList<string>? previousFailureReasons = null)
```

```diff
-        sb.AppendLine("- \"assetIds\" may be an empty array - a real asset library does not exist yet.");
+        sb.AppendLine("- \"assetIds\" must only contain ids from the catalog below, or be an empty array if");
+        sb.AppendLine("  nothing fits. Never invent an asset id that isn't listed.");
...
+        sb.AppendLine("Asset catalog (id - category):");
+        foreach (var asset in knownAssets)
+        {
+            sb.AppendLine($"- {asset.Id} - {asset.Category}");
+        }
```

**Neden:** Day 5'te "gerçek bir asset kütüphanesi yok" dendiği için LLM'e hiç katalog sunulmuyordu. Artık gerçek bir katalog var — modelin bunu **görmeden** doğru ID seçmesi imkansız olurdu, bu yüzden hem kuralı güncelledik hem kataloğun kendisini prompt'a ekledik.

**Nasıl çalışıyor:** Katalog, her asset için sadece `id` ve `category` listeliyor (tag'ler/uyumlu sahne tipleri gibi daha ayrıntılı bilgi **bilinçli olarak** dışarıda bırakıldı) — Day 5'in deneyimi, küçük modelin zaten çok sayıda eşzamanlı kısıtlamayla (JSON şekli, birebir metin kopyalama, süre bütçesi) uğraştığını gösterdi; kataloğu mümkün olduğunca kısa/sade tutmak, prompt'un toplam karmaşıklığını artırmamak için bilinçli bir tercih.

---

## 6. `ScenePlanningService.cs` — `IAssetRepository` entegrasyonu

```diff
     public ScenePlanningService(
         ILlmProvider llmProvider,
         IVideoJobRepository repository,
         IVideoPlanStore planStore,
+        IAssetRepository assetRepository,
         TimeProvider timeProvider)
```

```csharp
private async Task<VideoPlan> GenerateValidatedPlanAsync(string topic, string narrationText, CancellationToken cancellationToken)
{
    var knownAssets = await _assetRepository.GetAllAsync(cancellationToken);
    var knownAssetIds = knownAssets.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    for (var attempt = 1; attempt <= MaxAttempts; attempt++)
    {
        var prompt = ScenePlanPromptBuilder.Build(topic, narrationText, knownAssets, failureReasons);
        ...
        var validation = VideoPlanValidator.Validate(plan, narrationText, knownAssetIds);
        ...
    }
}
```

**Neden:** Katalog, deneme döngüsü **başlamadan önce bir kez** çekiliyor (`GetAllAsync`, her denemede tekrar değil) — asset listesi bir job'ın planlama süreci boyunca değişmeyeceği için gereksiz tekrar sorgulamadan kaçınılıyor; ayrıca `JsonAssetRepository` zaten tüm veriyi bellekte tuttuğu için bu çağrı pratikte anlık.

**Nasıl çalışıyor:** `knownAssets` (tam metadata, prompt için) ile `knownAssetIds` (sadece ID'lerin `HashSet`'i, doğrulama için) ayrı ayrı tutuluyor — ikisi farklı amaçlara hizmet ediyor: biri modele "neler var" demek için, diğeri modelin döndürdüğü ID'yi `O(1)` karmaşıklıkla "gerçekten var mı" diye kontrol etmek için (`List<string>.Contains` yerine `HashSet<string>.Contains`).

---

## 7. `Program.cs` — açılışta fail-fast kontrol ve `GET /api/assets`

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShortsGeneratorDbContext>();
    db.Database.Migrate();

    // JsonAssetRepository loads/validates the manifest in its constructor; resolving it
    // here fails the app at startup with a clear error if the manifest is missing/broken.
    scope.ServiceProvider.GetRequiredService<IAssetRepository>();
}
```

```csharp
app.MapGet("/api/assets", async (IAssetRepository assets, CancellationToken ct) =>
    Results.Ok(await assets.GetAllAsync(ct)))
    .WithName("GetAssets");
```

**Neden:** `CLAUDE.md` §14 açıkça `GET /api/assets`'i hedef endpoint listesinde sayıyor. Açılıştaki `GetRequiredService<IAssetRepository>()` çağrısı ise bölüm 3'teki fail-fast tasarımın **gerçekten işlemesi** için şart — DI container'lar varsayılan olarak **tembel** (lazy) çalışır, yani bir servis hiç `GetRequiredService` ile çözümlenmezse constructor'ı hiç çalışmaz. Bu satır olmadan, manifest bozuk olsa bile uygulama sorunsuz açılır, hata ancak ilk `POST /api/videos/{id}/plan` çağrısında ortaya çıkardı — bu, roadmap'in "asset missing" hatasının "açılışta net" olması gerekliliğini ihlal ederdi.

**Nasıl çalışıyor:** `app.MapGet("/api/assets", async (IAssetRepository assets, ...) => Results.Ok(await assets.GetAllAsync(ct)))` — Day 3'teki `LlmAvailability` ve Day 5'teki `VideoPlan` ile aynı desen: bir Domain record'u (`AssetMetadata` listesi) doğrudan HTTP cevabı olarak dönüyor, ayrı bir DTO'ya ihtiyaç yok çünkü bu salt-okunur, bilgilendirici bir endpoint (mutasyon yapan `POST /api/videos` gibi değil).

---

## 8. Bulunan test izolasyon hatası — `HealthEndpointTests`

Tüm test paketi çalıştırıldığında:
```
System.InvalidOperationException : Asset manifest not found at
'...\tests\ShortsGenerator.Api.Tests\bin\assets\manifests\assets.json'.
```

**Neden:** Bu, Day 2'nin "test gerçek veritabanını kullanıyordu" hatasıyla **aynı kökten** geliyor: `WebApplicationFactory<Program>`'ın test host'u, test projesinin **kendi** `bin/` klasöründen çalışıyor, `src/ShortsGenerator.Api`'den değil. `AssetManifestOptions`'ın varsayılan `"../../assets/manifests/assets.json"` yolu, API'nin gerçek çalışma dizini için doğru, ama test host'unun çok daha derin bir `bin/Debug/net10.0/` klasöründen çalıştığı için **yanlış** bir yere işaret ediyordu.

**Düzeltme** — `HealthEndpointTests.cs`:
```csharp
Directory.CreateDirectory(_assetDirectory);
File.WriteAllText(Path.Combine(_assetDirectory, "placeholder.svg"), "<svg></svg>");
File.WriteAllText(manifestPath, /* tek asset'lik minimal manifest */);

_factory = factory.WithWebHostBuilder(builder =>
    builder.ConfigureAppConfiguration((_, configBuilder) =>
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
            ["AssetManifest:ManifestPath"] = manifestPath,
        })));
```

**Nasıl çalışıyor:** Day 2'deki SQLite düzeltmesiyle birebir aynı desen — test, gerçek repo'daki asset manifest'ine güvenmek yerine, **kendi geçici, minimal manifest'ini** (tek bir sahte SVG + tek satırlık JSON) oluşturup `AssetManifest:ManifestPath` konfigürasyonunu bununla geçersiz kılıyor. `Dispose()`'da hem geçici veritabanı dosyası hem bu geçici klasör temizleniyor. Bu, "entegrasyon testleri gerçek dış kaynaklara değil, kendi izole kopyalarına bağımlı olmalı" prensibinin üçüncü kez (Day 2 SQLite, Day 3 implicit olarak FakeHttpMessageHandler, şimdi asset manifest) uygulanması.

---

## 9. Yeni testler

- **Domain** (`VideoPlanValidatorTests.cs`, +3): bilinmeyen bir asset ID'nin reddedildiğini, bilinen bir ID'nin kabul edildiğini doğrulayan testler; mevcut tüm `Validate(...)` çağrıları yeni `knownAssetIds` parametresini (çoğunlukla boş bir `NoAssets` seti) almak üzere güncellendi.
- **Application** (`ScenePlanningServiceTests.cs`, +2; `InMemoryAssetRepository.cs`, yeni fake): `GeneratePlanAsync_SceneReferencesKnownAsset_Succeeds` ve `GeneratePlanAsync_SceneReferencesUnknownAsset_RetriesAndSucceeds` — ikincisi özellikle önemli: `FakeLlmProvider`'a önce uydurma bir asset ID'si içeren geçersiz bir plan, sonra geçerli bir plan veriliyor; test, hem tekrar deneme mekanizmasının çalıştığını hem ikinci prompt'ta "unknown asset id" geri bildiriminin gerçekten yer aldığını doğruluyor.
- **Infrastructure** (`JsonAssetRepositoryTests.cs`, yeni, 4 test): gerçek bir geçici manifest dosyasıyla başarılı yükleme + mutlak yol çözümlemesi; bilinmeyen ID için `null` dönüşü; manifest dosyası yoksa net hata; manifest bir dosyayı işaret edip o dosya yoksa net hata (asset adını da içeren bir mesajla).

---

## Doğrulanan davranış

```
dotnet build  → 0 Uyarı, 0 Hata (9 proje)
dotnet test   → Domain.Tests: 38 | Application.Tests: 35 | Infrastructure.Tests: 13 | Api.Tests: 1
                Toplam: 87 Başarısız: 0

Gerçek dotnet run (src/ShortsGenerator.Api, Ollama'ya hiç dokunmadan):
GET /api/health → 200 {"status":"ok",...}
GET /api/assets → 200, 12 asset, her biri doğru çözümlenmiş MUTLAK dosya yoluyla:
  mascot-idle, mascot-thinking, mascot-tired, mascot-happy,
  pullup-top, pullup-bottom, pushup-top, pushup-bottom,
  dumbbell, pullup-bar, biceps-highlight, triceps-highlight

Uygulama açılışı sırasında fail-fast kontrolü sorunsuz geçti (manifest'teki
12 dosyanın tamamı gerçekten diskte var) - herhangi biri eksik olsaydı
uygulama hiç ayağa kalkmazdı.
```

Bugün, sistem yükü endişesi nedeniyle (bkz. günün başındaki kullanıcı uyarısı) yeni asset-doğrulama mantığını gerçek bir Ollama çağrısıyla uçtan uca tekrar test etmedim — bunun yerine iki güvenilir kanıt kaynağına dayandım: (1) asset sistemi kodu LLM çağrı yolunun **dışında**, tamamen deterministik şekilde test edilebilir, ve 35 `FakeLlmProvider` tabanlı test (ikisi özellikle bilinen/bilinmeyen asset senaryosu için) bunu kanıtlıyor; (2) altındaki gerçek LLM boru hattı zaten Day 5'te canlı olarak kanıtlanmıştı. `GET /api/assets`'in gerçek dosyalara karşı doğru çalıştığını göstermek için tek, hafif (LLM'siz) bir `dotnet run` denemesi yeterli oldu.
