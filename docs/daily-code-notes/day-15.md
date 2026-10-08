# Day 15 — Kod Notları

30 günlük roadmap, Gün 15. Konu: ilk tam otomatik Short. Sadece bir konu verildi (`Why Your Pull-Ups Aren't Increasing`) ve sistem script → sahne planı → asset seçimi → TTS → zamanlama → render zincirini kendi başına tamamladı. İlk deneme başarısız oldu. Bu doküman büyük ölçüde o başarısızlığın teşhisi ve kalıcı çözümü hakkında.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. İlk gerçek deneme: başarısız (teşhis)

```
POST /api/videos {"topic":"Why Your Pull-Ups Aren't Increasing"}   -> 202
   0.4s  Created
  16.5s  Planning        (script hazır: 89 kelime, 5 cümle)
 112.9s  Failed
error: Could not produce a valid scene plan ... after 5 attempts. Last issues: Your output was not valid JSON ...
       There is an open JSON object or array that should be closed. Path: $.scenes[9] | LineNumber: 124
```

Çıkarımlar:
* Script üretimi sorunsuz (16 sn, uzunluk/güvenlik kurallarına uygun).
* Hata **sahne planlamada**. Son denemenin JSON'u 124. satırda, 10. sahnenin ortasında yarıda kesilmiş. Bu, Ollama'nın çıktı token sınırına (`num_predict` = 2048) takıldığının işareti.
* İlk 4 denemenin neden reddedildiği **bilinmiyor**: servis deneme bazında hiçbir şey loglamıyordu. Teşhis tahmine kalıyordu. → Bölüm 5'te düzeltildi.

**Kök neden: tasarım.** Script 89 kelime ama sadece 5 cümle. Plan en az 6 sahne istiyor. Modelden aynı anda (1) metni **kelimesi kelimesine** kopyalamasını, (2) bir cümleyi bölmesini ve (3) sahne başına 11 alanlı uzun bir JSON yazmasını istiyorduk. 3B'lik bir model için bu üçü birlikte fazla. Ya metni değiştiriyor (Day 5'ten bilinen paraphrase sorunu), ya da çıktı o kadar uzuyor ki token sınırında kesiliyor.

Day 5'teki ilke "bildiğin veriyi LLM'e tekrar yazdırma" idi (`NarrationText` ve toplam süre o yüzden koddan türetiliyordu). Bugün bu ilkenin mantıksal sonucuna gidiyoruz: **metni sahnelere bölme işi saf bir metin işleme problemi, bunu kod yapsın.** LLM sadece yaratıcı kısmı, yani her sahnenin **nasıl görüneceğini** seçsin.

---

## 2. `Domain/NarrationSegmenter.cs` — deterministik bölme

```csharp
public const int TargetWordsPerScene = 13;   // ~4 sn konuşma (Kokoro ~3.5 kelime/sn)

public static IReadOnlyList<string> Split(string narration, int minScenes, int maxScenes)
{
    var words = narration.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    var pieces = SplitIntoSentences(words);                                   // . ! ? ile biten kelimelerde
    var target = Math.Clamp((int)Math.Round(words.Length / 13.0, AwayFromZero), minScenes, maxScenes);

    while (pieces.Count < target && TrySplitLongest(pieces)) { }              // en uzun parçayı böl
    while (pieces.Count > target) MergeShortestNeighbours(pieces);            // en kısa komşuları birleştir

    return pieces.Select(piece => string.Join(' ', piece)).ToList();
}
```

**Neden:** Bölme artık %100 tekrarlanabilir ve metni **asla** değiştirmiyor. Parçaların birleşimi her zaman girdinin kendisi. `VideoPlanValidator`'ın "narration birebir yeniden üretilmeli" kuralı artık yapısı gereği her zaman sağlanıyor. Bir hata sınıfı tamamen ortadan kalktı.

**Nasıl çalışıyor:**
* **Hedef sahne sayısı kelime sayısından**: 89 kelime / 13 ≈ 7 sahne, `[6, 10]` aralığına sıkıştırılıyor. Sahne başına ~4 saniye: ekran metnini okumaya yetecek, görüntüyü hareketli tutacak kadar kısa.
* **Önce cümleler**, sonra eksikse **en uzun parçayı böl**. Bölme yeri tercihen ortaya en yakın yan cümle sınırı (`,` `;` `:` `—`), yoksa ortadaki kelime. Her iki yarıda en az 3 kelime kalması şartı tek kelimelik sahneleri önlüyor.
* Fazlaysa **en kısa komşu çifti birleştir**.
* Kelime listeleri üzerinde çalışıp tek boşlukla birleştirildiği için çift boşluk/satır sonu normalize ediliyor (validator da aynı normalizasyonu yapıyor).
* Gerçek script'teki sonuç (test edildi): 5 cümle → yan cümle sınırlarından bölünerek 7 parça, her parça bir noktalama işaretinde bitiyor.

---

## 3. `ScenePlanPromptBuilder` — LLM'e sadece görsel kararlar (Day 5 dosyası, yeniden yazıldı)

```
The narration is already split into 7 scenes, in order:
1. "If you're finding that your pull-ups aren't increasing, ..."
...
Decide how each of the 7 scenes looks. Respond with ONLY one JSON object ...
{ "title": "...", "hook": "...",
  "scenes": [ { "scene": 1, "type": "Hook", "onScreenText": "max 5 words", "assetIds": [],
                "animation": "punchZoom", "sfx": null, "emphasis": "key words from this scene" } ] }
Rules:
- "scenes" must contain exactly 7 entries, numbered 1 to 7.
- Scene 1 is type "Hook" and scene 7 is type "Outro". Every other scene uses one of: ExerciseDemo, Comparison, ...
- "emphasis": 1-3 words copied exactly from THAT scene's narration (the key idea), or null.
- "assetIds": 0 or 1 ids from this catalog only - never invent one:
  - mascot-idle (mascot: mascot, idle, ...)
```

**Neden:** Prompt'ta artık `narration` ve `durationSeconds` alanları yok. Model metni yazmıyor, süre tahmin etmiyor. Çıktı sahne başına ~6 kısa alan, öncekinin kabaca dörtte biri. Asset kataloğu artık etiketleriyle (tags) listeleniyor, böylece model "dumbbell" ile "pullup-bar" arasında bilgiye dayalı seçim yapabiliyor.

---

## 4. `ScenePlanningService` — planı kod kuruyor (Day 5 dosyası, büyük değişiklik)

```csharp
var segments = NarrationSegmenter.Split(narrationText, MinScenes, MaxScenes);
if (segments.Count < MinScenes) throw new VideoPlanGenerationException("... too short ...");   // LLM'e gitmeden

var schema = VisualPlanSchema.Build(segments.Count, knownAssets.Select(a => a.Id));           // bölüm 6
for (attempt 1..5)
{
    var raw = await _llmProvider.GenerateJsonAsync(prompt, schema, ct);
    var (plan, reasons) = TryBuildPlan(PlanJsonExtractor.Extract(raw), segments, narrationText, knownAssetIds);
    if (plan is not null) return plan;
    _logger.LogWarning("Job {JobId}: scene plan attempt {Attempt}/{MaxAttempts} rejected: {Reasons}", ...);
}

// TryBuildPlan: her sahne için
new Scene(
    Id: $"scene-{i + 1}", Order: i + 1,
    Type: ResolveType(visual.Type, i, segments.Count),           // 1. = Hook, son = Outro, kod zorlar
    DurationSeconds: EstimateSeconds(segments[i]),               // kelime / 3.0, [1, 12]
    Narration: segments[i],                                      // koddan, LLM'den değil
    OnScreenText: ..., AssetIds: ..., Animation: visual.Animation ?? Fade, Sfx: ...,
    Emphasis: ResolveEmphasis(visual.Emphasis, segments[i]));    // sahnede geçmiyorsa null
```

**Neden / Nasıl çalışıyor:**
* **Kod ne biliyorsa onu yazıyor**: id, sıra, narration, süre tahmini. Model sadece tip, ekran metni, asset, animasyon, ses efekti ve vurguyu seçiyor.
* **Hook ilk, Outro son: yapı kuralı, yaratıcılık değil.** Model ilk sahneye "List" dediyse bunun için bir deneme harcamak yerine kod "Hook" yapıyor. Ortaya konmuş bir Hook/Outro da nötr bir `ExerciseDemo`'ya çevriliyor.
* **Vurgu doğrulaması**: vurgu, sahnenin narration'ında gerçekten geçen kelimeler olmalı. Yoksa altyazıda işaretlenecek bir şey yok demektir. Kozmetik olduğu için deneme harcanmıyor, sadece `null` yapılıyor.
* **Süre tahmini**: kelime / 3.0 (gerçek ses ~3.5 kelime/sn, tahmin biraz cömert). Narration'dan sonra bu süreler zaten gerçek sesle değişiyor (Day 12). Tahminin tek işi, validator'ın toplam süre kontrolünden makul şekilde geçmek.
* **Sahne sayısı yanlışsa** açık bir sebep: `"You returned 5 scenes; return exactly 7, ..."`.
* **Çok kısa metin**: 6 sahneye bölünemeyecek bir metin için LLM'e hiç gidilmiyor. Hiçbir prompt bunu düzeltemez, hemen anlaşılır bir hatayla bitiyor.
* `ILogger` opsiyonel bir constructor parametresi (`= null` → `NullLogger`): DI gerçek logger'ı veriyor, mevcut testler değişmeden derleniyor.

---

## 5. Deneme bazında loglama

```csharp
_logger.LogWarning("Job {JobId}: scene plan attempt {Attempt}/{MaxAttempts} rejected: {Reasons}", ...);
_logger.LogInformation("Job {JobId}: scene plan accepted on attempt {Attempt} ({SceneCount} scenes)", ...);
```

**Neden:** İlk başarısızlıkta sadece 5. denemenin sebebi görülebiliyordu. Bu satırlar sayesinde ikinci gerçek denemede sorunun ne olduğu **hemen** görüldü (bölüm 7). "Log yoksa teşhis tahmindir."

---

## 6. Structured outputs: `ILlmProvider.GenerateJsonAsync` + `VisualPlanSchema`

İkinci gerçek denemenin (bölüm 7) logları yeni bir sorun gösterdi: model 3 kez **1 sahne** döndürdü (prompt'taki örnek JSON'da tek sahne var ve küçük model onu taklit ediyor), bir kez de bozuk JSON üretti. Prompt'u iyileştirmek yerine sorunu kökten çözen bir yöntem var: **constrained decoding**.

```csharp
// Domain/ILlmProvider.cs
Task<string> GenerateJsonAsync(string prompt, string jsonSchema, CancellationToken cancellationToken);

// Infrastructure/Llm/OllamaLlmProvider.cs
public Task<string> GenerateJsonAsync(string prompt, string jsonSchema, CancellationToken ct)
{
    using var schema = JsonDocument.Parse(jsonSchema);
    return SendAsync(prompt, schema.RootElement.Clone(), ct);     // istekte "format": <şema>
}
internal sealed record OllamaGenerateRequest(..., [property: JsonIgnore(Condition = WhenWritingNull)] JsonElement? Format = null);
```
```csharp
// Application/VisualPlanSchema.cs (özet)
"scenes":   { "type": "array", "minItems": 7, "maxItems": 7, "items": {...} }
"type":      { "enum": ["Hook", "ExerciseDemo", ..., "Outro"] }
"animation": { "enum": ["fade", "slide", ..., "highlight"] }
"sfx":       { "enum": ["pop", ..., "click", null] }
"assetIds":  { "type": "array", "maxItems": 1, "items": { "enum": [<katalogdaki gerçek id'ler>] } }
```

**Neden:** Ollama'nın (0.5+, makinedeki 0.40.1) "structured outputs" özelliği verilen JSON Schema'yı bir **gramere** derliyor ve model token üretirken sadece bu grameri sağlayan token'ları seçebiliyor. Yani şema prompt'taki bir **rica** değil, üretim anındaki bir **kısıt**:
* `minItems = maxItems = 7` → model 1 sahnelik bir dizi üretemez.
* Asset id'leri bir `enum` → model katalogda olmayan bir id'yi **yazamaz** (Day 6'nın "planner sadece bilinen asset'leri seçebilir" kuralı artık üretim anında garanti).
* Her zaman geçerli JSON.

**Nasıl çalışıyor:**
* Şema `System.Text.Json.Nodes` (`JsonObject`/`JsonArray`) ile koddan üretiliyor. Enum değerleri doğrudan C# enum'larından (`SceneType`, `AnimationId`, `SfxId`) alındığı için, yeni bir animasyon eklendiğinde şema otomatik güncelleniyor.
* **Boş katalog koruması**: `enum: []` sağlanamaz bir gramer olurdu. Katalog boşsa `assetIds` için `maxItems: 0` kullanılıyor.
* `Format` alanı null iken istekten tamamen çıkarılıyor (`JsonIgnore(WhenWritingNull)`), böylece düz metin üretimi (`GenerateAsync`, script için) değişmedi.
* **Şema yine de doğrulamanın yerini tutmuyor**: "vurgu o sahnede geçmeli" gibi kurallar şemayla ifade edilemez. `VideoPlanValidator` ve `TryBuildPlan` aynen çalışmaya devam ediyor.
* Arayüz Ollama'ya özgü değil: ücretli API'lerin çoğu da JSON Schema destekliyor. İleride sağlayıcı değişirse yeni adapter aynı metodu uygulayacak.

---

## 7. Script uzunluğu gerçek sese göre (Day 4 dosyaları, değişti)

```diff
-    public const int MinWords = 40;
-    public const int MaxWords = 130;
+    public const int MinWords = 65;
+    public const int MaxWords = 140;
+    public const int TargetMinWords = 85;
+    public const int TargetMaxWords = 120;
```
```diff
-        sb.AppendLine($"Write {ScriptValidator.MinWords}-{ScriptValidator.MaxWords} words of plain spoken English.");
+        sb.AppendLine($"Write {TargetMinWords}-{TargetMaxWords} words of plain spoken English " +
+                      $"(never fewer than {MinWords} or more than {MaxWords}).");
+        sb.AppendLine("Use short, complete sentences - one idea per sentence.");
```

**Neden:** Day 12'de ölçüldü: Kokoro saniyede ~3.5 kelime okuyor. Eski 40–130 aralığı ~11–37 saniyelik videolar demekti. Hedef 85–120 kelime ≈ 25–35 sn (`CLAUDE.md` 25–45 sn). Sert sınırlar (65–140) modele birkaç kelimelik pay bırakıyor, böylece 2 kelime için bir deneme harcanmıyor. "Kısa cümleler" talimatı bölücünün işini kolaylaştırıyor. Sonuç: üçüncü denemede ses 34.7 saniye.

---

## 8. Testler

* `NarrationSegmenterTests` (9): birleşim her zaman girdinin kendisi; gerçek 5 cümlelik script → 7 parça, her biri ≥3 kelime; parçalar cümle/yan cümle sınırında bitiyor; çok kısa cümleler kelime hedefine birleştiriliyor; çok uzun metin 10'da kırpılıyor; 6 kısa cümle → cümle başına bir sahne; noktalamasız uzun cümle ortadan bölünüyor; çok kısa metin elden geldiğince; boşluk normalizasyonu. *(Testlerden biri ilk yazıldığında yanlış bir beklentiyle başarısız oldu: 120 kelime / 13 = 9 sahne, 10 değil. Kod doğruydu, test düzeltildi.)*
* `ScenePlanningServiceTests` (+5): narration'ın birebir korunması + sürelerin [1,12]'de olması; yanlış sahne sayısında kesin sayıyı söyleyen tekrar; Hook/Outro'nun kodla zorlanması; sahnede geçmeyen vurgunun düşürülmesi; çok kısa metinde LLM'e hiç gidilmemesi.
* `VisualPlanSchemaTests` (5): sahne sayısı sabitleniyor; sadece katalog id'leri ve kontrollü sözlükler; sfx null olabiliyor; boş katalogda `maxItems: 0`; servis, bölünmüş sahne sayısına uyan bir şema gönderiyor.
* `OllamaLlmProviderTests` (+2): şema `format` olarak gidiyor; düz metin isteğinde `format` hiç yok.
* `ScenePlanPromptBuilderTests` yeni imzaya göre yeniden yazıldı (6). Test narration'ı gerçekçi hale getirildi: eski 44 kelimelik metin yeni sınırların altında kalıyordu. Artık ilk gerçek denemenin 89 kelimelik script'i ortak bir sabit (`TestNarrations.PullUps`). `VisualPlanJson` test yardımcısı, iyi davranan bir LLM'in cevabını üretiyor.

---

## Doğrulanan davranış

```
$ dotnet test
Başarılı! - Başarısız: 0, Başarılı: 57 - ShortsGenerator.Domain.Tests.dll
Başarılı! - Başarısız: 0, Başarılı: 69 - ShortsGenerator.Application.Tests.dll
Başarılı! - Başarısız: 0, Başarılı: 61 - ShortsGenerator.Infrastructure.Tests.dll
Başarılı! - Başarısız: 0, Başarılı:  7 - ShortsGenerator.Api.Tests.dll
(toplam 194; Day 14 sonunda 172)
```

Aynı konu için üç gerçek uçtan uca çalıştırma (gerçek Ollama `llama3.2:3b` + Kokoro + Remotion, sadece `POST /api/videos {"topic": ...}`):

| Deneme | Değişiklik | Sonuç | Plan denemesi | Toplam süre | Video |
|---|---|---|---|---|---|
| 1 | — (Day 14 hali) | **Failed**: JSON token sınırında kesildi | 5/5 başarısız | 113 sn | — |
| 2 | Kod bölüyor, LLM sadece görsel seçiyor | **Completed** | 5. denemede (3× "1 sahne", 1× bozuk JSON) | 93 sn | 29.0 sn, 2.7 MB |
| 3 | + JSON Schema (structured outputs) | **Completed** | **1. denemede** | **53 sn** | **34.7 sn**, 3.2 MB |

3. denemenin logları:
```
Job a903be2c-...: scene plan accepted on attempt 1 (8 scenes)
Kokoro synthesized 34.66s of audio (8 segments) with voice af_heart in 11.9s
Rendered job a903be2c-... to ...\output\a903be2c-...\final.mp4 (3215 kB) in 24.5s

title: Fix Your Pull-Ups | hook: stagnation is holding you back
1 Hook             punchZoom  assets=[]                  text='Plateau'
2 ExerciseDemo     highlight  assets=[biceps-highlight]  text='Proper Form'     emph='form'
3 AnatomyHighlight slide      assets=[mascot-thinking]   text='Engage Core'     emph='core'
4 Comparison       fade       assets=[dumbbell]          text='Hand Placement'
5 MythVsFact       counter    assets=[triceps-highlight] text='Wrong Muscle'
6 List             bounce     assets=[mascot-happy]      text='Variations'      emph='variations'
7 Recovery         slide      assets=[mascot-tired]      text='Adjust Strategy' emph='adjust'
8 Outro            highlight  assets=[mascot-happy]      text='New Heights'     emph='new heights'
```

Videodan alınan kareler görsel olarak kontrol edildi: Hook kartı, `Comparison` sahnesi (dumbbell + "Hand Placement" + altyazı), Outro (mutlu maskot + parlama + turuncu vurgulu `REACH NEW` altyazısı). İki karede altyazı soluk göründü. 6 kare sonrasına bakılınca tamamen opak olduğu görüldü, yani sadece giriş animasyonunun ortasına denk gelinmişti, hata değil.

Bu çıktılar şunu kanıtlıyor: **oynatılabilir bir `final.mp4`, sadece bir konudan, tamamen yerelde ve ücretsiz olarak otomatik üretildi** (roadmap kabul kriteri ve `CLAUDE.md` §19 V1 tanımının çekirdeği). Planlama artık ilk denemede geçiyor ve süre hedef aralığın ortasında.

**Görülen kalite sorunları (bilinçli olarak sonraki günlere):**
* Hook sahnesinde asset yok → ilk saniyeler boş bir ekran. En dikkat çeken sahnenin en boş sahne olması kötü (Day 16: görsel kalite).
* Asset seçimleri anlamsal olarak zayıf: "Proper Form" için biceps şeması, "Wrong Muscle" için triceps. Sadece 12 asset var (Day 18 şablonları, Day 20 asset genişletme).
* `counter` animasyonu sayı içermeyen bir metne ("Wrong Muscle") verilmiş, etkisi yok (Day 16/18).
* Sol üstteki geliştirici etiketi hâlâ duruyor (Day 16).
