# Day 19 — Kod Notları

30 günlük roadmap, Gün 19. Konu: tekrar karşıtı ve özgünlük kontrolleri. Otomasyonun "düşük değerli şablon spam'ine" dönüşmemesi için: video içi sahne/asset tekrar sınırları, videolar arası açılış karşılaştırması, değişen Hook yerleşimleri, önceki script'lerle metin benzerliği kontrolü, benzersiz bir "çıkarılacak ders" (takeaway) ve her videonun yanına bir insan inceleme listesi. Ölçüm yapıldı, kalıp bulundu, düzeltildi, yeniden ölçüldü.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. Sert ve yumuşak kurallar

Bugüne kadarki bütün kurallar **sert** kurallardı (`VideoPlanValidator`): ihlal edilirse plan render edilemez (yanlış sahne sayısı, bilinmeyen asset, değişmiş narration). Bugünün kuralları farklı bir türde: "aynı tip üç kez" kuralını ihlal eden bir plan **doğru render edilir**, sadece daha tekdüzedir. Bu yüzden ayrı ele alınıyor:

* **Sert kural** → plan reddedilir, LLM yeniden dener, 5 denemede olmazsa job `Failed`.
* **Yumuşak kural** → ilk 2 denemede LLM'e sebep söylenip yeniden denetilir. 3. denemeden itibaren plan **kabul edilir** ve sorunlar `ReviewWarnings` olarak insan incelemesine bırakılır.

**Neden:** Bir videonun "biraz tekdüze" olduğu için tamamen başarısız sayılması, kullanıcı açısından tekdüze bir videodan daha kötü bir sonuç. Sorunu görünür kılmak ve kararı insana bırakmak doğru denge.

---

## 2. `Domain/PlanVariationRules.cs` — video içi çeşitlilik

```csharp
public const int MaxScenesPerType = 2;
public const int MaxUsesPerAsset = 2;
public const int MinDistinctMiddleTypes = 3;

public static IReadOnlyList<string> Check(VideoPlan plan)
//  - Hook/Outro dışındaki bir tip 2'den fazla kullanılmış mı?
//  - (4+ ara sahnede) en az 3 farklı tip var mı?
//  - art arda iki sahne aynı tipte mi?
//  - bir asset 2'den fazla sahnede mi?
//  - art arda iki sahne aynı asset'i mi gösteriyor?
```

**Neden / Nasıl çalışıyor:** Hook ve Outro yapı gereği her videoda var, çeşitlilik **aradaki** sahnelerle ilgili. Mesajlar LLM'e doğrudan geri gönderildiği için eyleme dönük yazıldı: *"Scenes 3 and 4 both show 'pullup-top'; pick a different asset for one of them."* Asset'i olmayan sahneler (renderer yedek maskot gösterir) tekrar sayılmıyor (test edildi).

---

## 3. `Domain/TextSimilarity.cs` — shingle benzerliği

```csharp
public static IReadOnlySet<string> Shingles(string text, int size)    // n kelimelik ardışık kalıplar
public static double Overlap(string candidate, string existing, int size)
    => candidateShingles.Count(existingShingles.Contains) / (double)candidateShingles.Count;
[GeneratedRegex(@"[a-z0-9]+(?:['\-][a-z0-9]+)*")]   // "aren't", "pull-ups" tek kelime; noktalama atılır
```

**Neden:** Arama motorlarının "neredeyse aynı sayfa" tespitinde kullandığı klasik teknik. Bütün metni karşılaştırmak, yeni bir metnin içine kopyalanmış tek bir cümleyi kaçırır. Tek tek kelimeleri karşılaştırmak ise "your", "pull-ups" gibi ortak kelime haznesini benzerlik sanır. 5 kelimelik kalıplar ikisinin arasında doğru noktada (testler: ortak kelime haznesi < %5; yeni metne gömülü tek cümle %20–60; birebir aynı metin %100).

---

## 4. Script benzerlik kontrolü (`ScriptGenerationService`, değişti)

```csharp
internal const double MaxPhraseOverlapWithEarlierScripts = 0.25;   // 5 kelimelik kalıplar
var earlierScripts = await LoadEarlierScriptsAsync(ct);            // son 30 job'ın script'leri
...
var duplicate = FindNearDuplicate(cleaned, earlierScripts);
if (duplicate is null) return cleaned;
failureReasons = [duplicate];   // "Too similar to an earlier video ("Pull-up basics"): 100% of its phrases repeat. Write it fresh..."
```

**Neden:** `CLAUDE.md` §4: "her Short'un ayrı bir script'i olmalı". Benzer konular sorulan küçük bir model bütün cümleleri tekrar kullanmaya meyilli. Kalıplarının %25'inden fazlası önceki bir videoyla ortaksa, model metni baştan yazmaya yönlendiriliyor. Kullanıcının kendi verdiği `sourceScript` kontrol edilmiyor: o bilinçli bir seçim.

### Bulunan ve düzeltilen: kültüre bağlı yüzde biçimi

LLM'e giden mesajda `{overlap:P0}` kullanmıştım. Bu Türkçe makinede `%25`, `InvariantCulture`'da ise `25 %` (boşluklu) üretiyor. İkisi de test beklentisini tutturmadı. Yüzde artık açıkça yazılıyor: `{overlap * 100:0}%`. (Aynı dosyada `string.Create(culture, $"..." + "...")` da derlenmedi: interpolated string handler, `+` ile birleştirilmiş iki parça yerine **tek** bir interpolated string literal istiyor.)

---

## 5. Takeaway ve videolar arası kontrol (`ScenePlanningService`, değişti)

```csharp
// VideoPlan'a iki opsiyonel alan:
string? Takeaway = null,                        // videonun tek ana dersi (LLM şemasında zorunlu)
IReadOnlyList<string>? ReviewWarnings = null    // kabul edilen yumuşak sorunlar

var earlierPlans = await LoadEarlierPlansAsync(jobId, ct);           // son 20 job'ın plan.json'ları
var softIssues = PlanVariationRules.Check(plan)
    .Concat(PlanVariationRules.CheckAgainstEarlier(plan, earlierPlans))
    .Concat(CheckTakeaway(plan.Takeaway, earlierTakeaways))
    .ToList();
if (softIssues.Count == 0 || attempt >= SoftRuleAttempts)   // 3
    return softIssues.Count == 0 ? plan : plan with { ReviewWarnings = softIssues };
```

**Neden:**
* **Takeaway**: "Bu video ne öğretiyor?" sorusunun tek cümlelik cevabı. Önceki videolarınkiyle 3 kelimelik kalıplarda %60+ örtüşüyorsa (ya da normalize edilince birebir aynıysa) uyarı veriliyor. Kısa cümlelerde 5 kelimelik kalıp neredeyse hiç eşleşmez, bu yüzden 3 kullanılıyor. İki video aynı dersi farklı kelimelerle veriyorsa, bu da tekrardır.
* `plan with { ReviewWarnings = ... }`: record'larda `with` ifadesi, sadece verilen alanı değiştirip geri kalanını kopyalayan yeni bir nesne üretir (immutable güncelleme).

---

## 6. Ölçüm 1: beş gerçek plan ve bulunan kalıp

5 farklı konu için gerçek LLM ile script + plan üretildi (TTS/render yok, bilgisayar daha az yorulsun diye). Analiz script'i (`analyze-plans.mjs`, scratchpad) tip dizilerini, asset'leri, Hook varyantını ve script'ler arası benzerliği karşılaştırdı:

```
#1 Stop Swinging on Pull-Ups   types: Hook > ExerciseDemo > MythVsFact > AnatomyHighlight > Comparison > Progress > Outro
#2 Why Squat Depth Matters     types: Hook > MythVsFact > AnatomyHighlight > Progress > MythVsFact > Comparison > Outro
#3 Is Cardio Bad for Muscle... types: Hook > MythVsFact > AnatomyHighlight > ExerciseDemo > Progress > Comparison > Outro
#4 Rest Between Sets           types: Hook > MythVsFact > AnatomyHighlight > List > Progress > Recovery > Outro
#5 Why Grip Strength Limits... types: Hook > MythVsFact > AnatomyHighlight > Comparison > Progress > Outro
distinct type sequences: 5/5 | scene types used: 9 | max script phrase overlap: 0%
```

Kâğıt üzerinde kabul kriteri sağlanıyordu (5/5 farklı dizi, 9 tipin hepsi, script'ler arası %0 benzerlik). Ama **sahne 3 beş videonun beşinde `AnatomyHighlight`**, sahne 2 dördünde `MythVsFact` idi. Her video kendi içinde çeşitliydi, ama bir kanalda art arda izlenince aynı iskelet. Sebep: küçük model, prompt'taki şablon açıklamalarının **sırasını** takip ediyor (pozisyon yanlılığı).

---

## 7. Düzeltme: karıştırılan prompt + videolar arası açılış kuralı

```csharp
// ScenePlanPromptBuilder: tip açıklamaları ve tip listesi job'a göre döndürülüyor
var variationSeed = jobId.GetHashCode() & int.MaxValue;   // aynı job -> aynı sıra (deterministik)
foreach (var guide in Rotate(TypeGuides, variationSeed)) sb.AppendLine(guide);
sb.AppendLine("  (this list is in no particular order - do not just follow it):");

// PlanVariationRules.CheckAgainstEarlier: sahne 2-4'ün tip sırası önceki bir videoyla birebir aynıysa
"Scenes 2-4 use the same types in the same order (MythVsFact, AnatomyHighlight, Progress) as an earlier video
 ("Why Your Squat Depth Matters"); start this one differently."
```

**Neden / Nasıl çalışıyor:**
* **Döndürme (rotation)**: aynı liste, job'a göre farklı bir başlangıç noktasından. `Guid.GetHashCode()` .NET'te bayt içeriğinden hesaplanır (string hash'lerinin aksine process'e göre rastgele değildir), yani aynı job aynı prompt'u alır. `& int.MaxValue` işaret bitini temizleyip negatif olmamasını sağlıyor.
* **Videolar arası kural**: yanlılık karıştırmayla sadece **azaltılabiliyor**, bu yüzden bir de kontrol var. Son 20 planın açılışıyla birebir eşleşme, yumuşak bir sorun olarak bildiriliyor.
* AnatomyHighlight açıklamasına *"sadece sahne hangi kasın çalıştığıyla ilgiliyse ve o kası gösteren bir anatomi asset'i varsa"* şartı eklendi.

---

## 8. Ölçüm 2: beş yeni konu

```
#1 Mastering the Muscle-Up      Hook > List > ExerciseDemo > Comparison > AnatomyHighlight > ExerciseDemo > Outro
#2 Push-Up Form Matters         Hook > AnatomyHighlight > ExerciseDemo > MythVsFact > Comparison > Progress > Outro
#3 Does Stretching Prevent...   Hook > AnatomyHighlight > MythVsFact > ExerciseDemo > Recovery > Comparison > Outro
#4 Breathe Right: Heavy Lifts   Hook > ExerciseDemo > AnatomyHighlight > ExerciseDemo > Recovery > MythVsFact > Progress > Outro
#5 Sleep for Stronger Muscles   Hook > MythVsFact > AnatomyHighlight > Progress > Recovery > Comparison > ExerciseDemo > Outro  (calm)
distinct type sequences: 5/5 | scene types used: 9 | hook layouts: 2/3 | max script phrase overlap: 0%
```

| | Ölçüm 1 | Ölçüm 2 |
|---|---|---|
| Sahne 2 | MythVsFact 4/5 | List, AnatomyHighlight ×2, ExerciseDemo, MythVsFact |
| Sahne 3 | **AnatomyHighlight 5/5** | ExerciseDemo ×2, MythVsFact, AnatomyHighlight ×2 |

Loglar kuralların gerçekte çalıştığını gösteriyor:
```
c603d5ff: attempt 1 rejected: Scenes 3 and 4 both show 'pullup-top'          -> accepted on attempt 2
a5d51fb2: attempts 1-2 rejected: consecutive 'mascot-tired' / 'mascot-happy' -> accepted on attempt 3 (1 review warning)
aa94fc2b: attempt 1 rejected: Scenes 2-4 ... same order ... as "Why Your Squat Depth Matters"
          -> accepted on attempt 3 (1 review warning)
```
Model ısrar ettiğinde plan **başarısız sayılmadı**, uyarıyla kabul edildi. 10 planın 10'u da tamamlandı.

---

## 9. Hook varyasyonları (renderer)

```ts
// scenes/templates/variant.ts — FNV-1a hash
export function planSeed(title, narrationText): number   // aynı plan -> aynı sayı
export function pickVariant(seed, count): number

// scenes/templates/HookTemplate.tsx
//   0 - burst: büyük başlık + vurgu patlaması (Day 16)
//   1 - card:  başlık, vurgu renginde eğik bir kartın üzerinde "çarparak" geliyor, görsel altta
//   2 - split: dev, sola yaslı başlık + üstte dolan bir çizgi, görsel köşede eğik
```

**Neden:** Hook, bir kanalda **en çok tekrar eden** an: her videonun ilk saniyesi. Tekdüzelik ilk orada fark edilir. Varyant plandan hesaplanıyor, rastgele değil: aynı plan her render'da aynı açılışı üretiyor (deterministik render, Day 9 kuralı), farklı planlar ise üç yerleşime yayılıyor (30 planla test edildi, üçü de çıkıyor). `variantSeed` prop'u composition → `SceneRenderer` → `SceneView` → şablon zinciriyle iletiliyor.

---

## 10. İnsan incelemesi: `review.md` (`ReviewChecklist`, `IJobArtifactWriter`)

```csharp
public static string Build(VideoJob job, VideoPlan plan, DateTimeOffset renderedAt)
// # Review before publishing: <title>
// - Topic / Job / Rendered / Length / Takeaway
// ## Automatic checks   -> geçenler + "⚠ " ile ReviewWarnings
// ## Scenes             -> tip / ekrandaki metin / asset tablosu
// ## Your checklist (V1 never uploads - publishing is your decision)
//   - [ ] Every claim is accurate and cautiously worded - no medical or treatment claims.
//   - [ ] The narration sounds natural ...   - [ ] Captions match the voice ...   ...
// ## Script
```
```csharp
// Infrastructure/Storage/FileJobArtifactWriter
[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]*\.[A-Za-z0-9]{1,8}$")]   // sadece düz dosya adı
```

**Neden:** `CLAUDE.md` §4: "yayından önce bir insan inceleme adımı, V1 otomatik yüklemez". `review.md`, otomasyonun neyi kontrol ettiğini, neden emin olmadığını (uyarılar) ve **sadece bir insanın** karar verebileceği şeyleri (bilgi doğruluğu, telaffuz, sahne-görüntü uyumu) tek sayfada topluyor. Render tamamlanınca `RenderService` tarafından yazılıyor. Yazma "en iyi çaba" ile yapılıyor: kontrol listesi yazılamazsa video yine `Completed` olur, çünkü bitmiş ve geçerli bir videoyu bu yüzden `Failed` yapmak yanlış olurdu. Dosya yazıcı sadece düz bir dosya adı kabul ediyor: `../`, alt klasör, mutlak yol, uzantısız ad, nokta ile başlayan ad reddediliyor (`CLAUDE.md` §18). İçerik BOM'suz UTF-8 (`⚠`, Türkçe karakterler test edildi).

---

## Doğrulanan davranış

```
$ dotnet test   -> Domain 71, Application 91, Infrastructure 68, Api 7   (toplam 237; Day 18'de 208)
$ cd renderer; npm test -> 40 passed;  npm run lint -> temiz
```

Gerçek çalıştırmalar: bölüm 6 ve 8 (10 plan, 10'u tamamlandı). Bu çıktılar şunu kanıtlıyor: beş gerçek plan anlamlı yapısal çeşitlilik gösteriyor (5/5 farklı dizi, 9 tipin hepsi, açılış sahneleri artık videodan videoya değişiyor, script'ler arası %0 ortak kalıp; roadmap kabul kriteri). Kurallar LLM'i gerçekten düzeltmeye yönlendiriyor, ısrar ettiğinde de videoyu düşürmek yerine kararı insana bırakıyor.

**Bilinçli olarak bırakılanlar:**
* `biceps-highlight` neredeyse her videoda: katalogda sadece iki kas şeması var. Squat videosu bile biceps gösterdi. Bu bir asset kıtlığı (Day 20).
* Bazı takeaway'ler hâlâ genel ("build strength and power"). Benzerlik kontrolü tekrarı yakalıyor ama genelliği değil. İnceleme listesi bunu insana soruyor.
* Hook varyantı ilk 10 planda 2/3 çıktı (hash dağılımının küçük örneklemdeki doğal sonucu). 30 planlık testte üçü de çıkıyor.
