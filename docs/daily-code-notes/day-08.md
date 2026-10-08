# Day 8 — Kod Notları

30 günlük roadmap, Gün 8. Konu: gerçek bir `VideoPlan` JSON'unu Remotion'da çok sahneli bir videoya çevirmek — paylaşılan render şeması (Zod), `SceneRenderer`, güvenli asset yükleme, ve Remotion'ın render mimarisi hakkında bugün çıkardığım gerçek bir ders (neden `fs` kullanılamıyor).

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. `samples/sample-plan.json` — elle yazılmış, şemaya uygun test verisi

```json
{
  "title": "Why Your Pull-Ups Aren't Increasing",
  "hook": "Stuck at the same pull-up count for months?",
  "targetDurationSeconds": 30,
  "narrationText": "If you're stuck at the same pull-up count...",
  "scenes": [
    { "id": "scene-1", "order": 1, "type": "Hook", "durationSeconds": 4,
      "narration": "...", "onScreenText": "Stuck at the same number?",
      "assetIds": ["mascot-thinking"], "animation": "punchZoom", ... },
    ...
  ]
}
```

**Neden:** Day 5'in canlı testleri sırasında, küçük modelin (3B parametre) hem JSON şeklini hem birebir metin kopyalamasını aynı anda tutturması zorlu çıkmıştı — bu yüzden gerçekten başarılı bir `plan.json` diskte hiç oluşmadı (`find src/ShortsGenerator.Api/output -name plan.json` boş döndü). Roadmap'in kendisi de Day 8 için "sample plan JSON" diyor — LLM'e bağımlı olmadan, renderer'ın **sözleşmeye** (contract) doğru tepki verip vermediğini test etmek bugünün amacı. `CLAUDE.md` §6'daki `samples/` klasörü tam olarak bu iş için var.

**Nasıl çalışıyor:** Day 6'nın gerçek asset ID'leri (`mascot-thinking`, `pullup-bottom`, `pullup-top`, `biceps-highlight`, `dumbbell`, `mascot-happy`) kullanıldı — uydurma ID'ler değil, gerçekten `assets/manifests/assets.json`'da kayıtlı olanlar. Altı sahnenin süreleri (4+5+5+5+6+5=30) `targetDurationSeconds: 30` ile birebir toplanıyor — Day 5'in "süreyi modelden değil hesaptan al" prensibini, bu sefer biz elle yazarken de tutarlı tuttuk.

---

## 2. `renderer/src/schemas/videoPlan.ts` — Zod şeması

```ts
import { z } from "zod";

export const SceneTypeSchema = z.enum([
  "Hook", "ExerciseDemo", "Comparison", "MythVsFact", "AnatomyHighlight",
  "Progress", "List", "Recovery", "Outro",
]);

export const AnimationIdSchema = z.enum([
  "fade", "slide", "pop", "punchZoom", "shake", "bounce", "pulse", "counter", "highlight",
]);

export const SceneSchema = z.object({
  id: z.string().min(1),
  order: z.number().int().positive(),
  type: SceneTypeSchema,
  durationSeconds: z.number().positive(),
  narration: z.string().nullable(),
  onScreenText: z.string().nullable(),
  assetIds: z.array(z.string()),
  animation: AnimationIdSchema,
  sfx: SfxIdSchema.nullable(),
  background: z.string().nullable(),
  emphasis: z.string().nullable(),
});

export const VideoPlanSchema = z.object({
  title: z.string().min(1),
  hook: z.string().min(1),
  targetDurationSeconds: z.number().positive(),
  narrationText: z.string().min(1),
  scenes: z.array(SceneSchema).min(1),
  style: z.string().nullable(),
  captionStyle: z.string().nullable(),
  musicMood: z.string().nullable(),
});

export type VideoPlan = z.infer<typeof VideoPlanSchema>;
```

**Neden:** `CLAUDE.md` §22 açıkça "runtime validation for incoming render JSON" istiyor. TypeScript'in `type VideoPlan = {...}` gibi bir tanımı sadece **derleme zamanında** işe yarar — bir dosyadan `JSON.parse(...)` ile okunan ham veri, derleyicinin gözünde `any`'dir, gerçek şekli hiç kontrol edilmez. Zod, bu kontrolü **çalışma zamanında** yapıyor: `.parse(raw)` çağrısı, `raw`'ın gerçekten bu şekle uyup uymadığını kontrol edip ya doğrulanmış bir nesne ya da açıklayıcı bir hata döndürüyor.

**Nasıl çalışıyor:**
* `z.enum([...])` — Day 5/6'daki C# enum'larının (`SceneType`, `AnimationId`, `SfxId`) birebir karşılığı; listelenmeyen bir string gelirse `.parse()` hata fırlatıyor.
* **Kritik detay:** `SceneTypeSchema`'nın değerleri PascalCase (`"Hook"`, `"MythVsFact"`), ama `AnimationIdSchema`'nınkiler camelCase (`"punchZoom"`, `"fade"`). Bu tesadüf değil — Day 5/6'da yazdığımız `JsonCamelCaseEnumConverterAttribute`, `AnimationId`/`SfxId`'yi camelCase, `SceneType`'ı ise varsayılan (PascalCase) JSON'a çeviriyordu. Bu şema, **gerçek backend davranışını** yansıtıyor, varsayım değil — Day 5/6'nın kod notlarına geri dönüp doğru JSON ismini teyit ederek yazıldı.
* `z.infer<typeof VideoPlanSchema>` — Zod'un en güçlü özelliklerinden biri: TypeScript **tipini** şemadan otomatik türetiyor, aynı şekli iki kez (bir Zod şeması, bir de ayrı bir `interface`) yazmaya gerek kalmıyor. Şema tek kaynak, tip ondan türüyor.

---

## 3. `renderer/src/assets/assetManifest.ts` (ilk hali — Node `fs` ile) ve Hata #1

```ts
// İLK (başarısız) deneme:
import fs from "node:fs";
import path from "node:path";
// ... fs.readFileSync(manifestPath, "utf-8") ile manifest'i oku ...
```

Render denendiğinde:
```
Error: Module build failed:
  × Reading from "node:fs" is not handled by plugins (Unhandled scheme).
```

**Neden (kök neden):** Remotion'ın composition kodu — `calculateMetadata` dahil — **gerçek Node.js sürecinde değil**, Remotion'ın yönettiği **headless bir Chrome örneğinin içinde** çalışıyor. Bu, hem Remotion Studio'daki canlı önizleme hem `remotion render` için geçerli — "render zamanında çalışıyor, o yüzden Node tarafında olmalı" varsayımım yanlıştı. Chrome'un içinde `fs`/`path` gibi Node yerleşik modülleri **hiç yok**; Rspack (Remotion'ın bundler'ı) bunu derleme zamanında fark edip net bir hatayla durduruyor.

**Düzeltme:** Remotion'ın resmi desteklediği yol — `staticFile()` + `fetch()`. Bu ikisi, hem Studio önizlemesinde hem render sırasında Remotion'ın `public/` klasörünü sunan **yerel bir HTTP sunucusu** üzerinden çalışıyor; `fetch`, tarayıcı/Chrome bağlamında zaten var olan standart bir API, Node'a özgü bir şey değil.

---

## 4. `renderer/scripts/sync-public-assets.mjs` — gerçek asset'leri `public/`'e aynalama

```js
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

function syncDir(name) {
  const source = path.join(repoRoot, name);
  const destination = path.join(rendererRoot, "public", name);
  fs.rmSync(destination, { recursive: true, force: true });
  fs.cpSync(source, destination, { recursive: true });
}

syncDir("assets");
syncDir("samples");
```

**Neden:** Bölüm 3'ün düzeltmesinin gerektirdiği şey: `staticFile()`, sadece `renderer/public/` **içindeki** dosyaları sunabiliyor. Ama gerçek asset'lerimiz (`assets/`) ve örnek plan'ımız (`samples/`) repo **kökünde** — hem backend (.NET) hem renderer'ın aynı tek kaynaktan okuyabilmesi için. Bu script, tek doğruluk kaynağını (repo kökü) bozmadan, renderer'ın ihtiyaç duyduğu "yerel sunucudan erişilebilir kopya"yı üretiyor.

**Nasıl çalışıyor:** Bu script **gerçekten Node.js'te** çalışıyor — çünkü bu bir `npm run` script'i, Remotion'ın bundle'ladığı composition kodunun bir parçası değil; `fs`/`path` burada sorunsuz. `fs.rmSync(destination, { recursive: true, force: true })` önce eski kopyayı temizliyor (güncel olmayan dosyaların kalmasını önlemek için), `fs.cpSync(..., { recursive: true })` tüm klasör ağacını kopyalıyor. `renderer/.gitignore`'a `public/assets` ve `public/samples` eklendi — bu kopyalar **türetilmiş** (derived) veri, `bin/`/`obj/` gibi, asla commit edilmiyor. `package.json`'da `predev`/`prerender` script'leri (`npm run dev`/`npm run render` çalıştırıldığında npm'in otomatik tetiklediği "pre-hook" kuralı) bu senkronizasyonu elle hatırlamaya gerek kalmadan çalıştırıyor.

---

## 5. Hata #2 — aynı sorun, bu sefer `node:path`

Bölüm 3'ün düzeltmesinden sonra render tekrar denendi, bu sefer `assetManifest.ts`'in relative path hesaplaması için kullandığı `path.posix.join`/`normalize` yüzünden **aynı hata** (`node:path`) tekrar çıktı.

**Düzeltme** — küçük, bağımlılıksız bir fonksiyon:
```ts
function joinAndNormalizePosix(baseDir: string, relativePath: string): string {
  const stack: string[] = [];
  for (const part of `${baseDir}/${relativePath}`.split("/")) {
    if (part === "" || part === ".") continue;
    if (part === "..") {
      if (stack.length === 0) {
        throw new Error(`Path '${relativePath}' escapes the '${baseDir}' directory.`);
      }
      stack.pop();
    } else {
      stack.push(part);
    }
  }
  return stack.join("/");
}
```

**Neden:** Bu, "bir kütüphane kurmadan önce gerçekten ihtiyacın var mı" sorusunun gerçek bir örneği — `path.posix` sadece string birleştirme/normalleştirme yapıyor, dosya sistemine hiç dokunmuyor, yani **mantığı** platform bağımsız (tarayıcıda da çalışabilir), sadece Node'un **modül sistemi** (`node:` önekiyle import) tarayıcıda yok. Bu yüzden `path` kütüphanesinin kendisini taklit eden bir paket aramak yerine, ihtiyacımız olan ~15 satırlık mantığı elle yazmak hem daha basit hem sıfır bağımlılık.

**Nasıl çalışıyor:** Klasik bir "stack tabanlı path normalleştirme" algoritması — `baseDir/relativePath`'i `/` ile parçalara ayırıyor, her `..` gördüğünde stack'ten bir öğe çıkarıyor (bir üst klasöre çık), her normal parçayı stack'e ekliyor. `stack.length === 0` iken bir `..` gelirse (yani `assets/` kök dizininin **dışına** çıkmaya çalışıyorsa) hata fırlatıyor — bu, Day 6'daki .NET tarafının `GetFullPath(...).StartsWith(ASSETS_ROOT)` kontrolünün TypeScript/tarayıcı tarafındaki karşılığı, aynı "path traversal'a izin verme" disiplini.

---

## 6. `renderer/src/assets/assetManifest.ts` (son hali) ve `scenes/` bileşenleri

```ts
export async function resolveAssetSvgs(assetIds: readonly string[]): Promise<Record<string, string>> {
  const svgById = await loadManifest(); // staticFile() + fetch() ile manifest + her SVG okunuyor
  const result: Record<string, string> = {};
  for (const id of assetIds) {
    const svg = svgById.get(id);
    if (svg === undefined) {
      throw new Error(`Unknown asset id '${id}' - it is not in the asset manifest.`);
    }
    result[id] = svg;
  }
  return result;
}
```

```ts
// scenes/timing.ts
export function secondsToFrames(seconds: number, fps: number): number {
  return Math.round(seconds * fps);
}
```

```tsx
// scenes/SceneRenderer.tsx
export const SceneRenderer: React.FC<SceneRendererProps> = ({ scenes, assetSvgById }) => {
  const { fps } = useVideoConfig();
  const orderedScenes = [...scenes].sort((a, b) => a.order - b.order);

  return (
    <Series>
      {orderedScenes.map((scene) => (
        <Series.Sequence key={scene.id} durationInFrames={secondsToFrames(scene.durationSeconds, fps)}>
          <SceneView scene={scene} assetSvgById={assetSvgById} />
        </Series.Sequence>
      ))}
    </Series>
  );
};
```

**Neden:** Roadmap'in "deterministic scene order/timing" maddesi. Burada bilinçli bir tercih var: "şu an hangi kare için hangi sahne aktif" hesaplamasını **elle** yazmak yerine (`frame >= cumulativeStart && frame < cumulativeEnd` gibi bir döngü), Remotion'ın **kendi hazır bileşenini** (`<Series>`) kullandık. Bu hem daha az kod hem daha az hata riski — Remotion'ın kendi test ettiği bir mekanizmaya güveniyoruz.

**Nasıl çalışıyor:**
* `[...scenes].sort((a, b) => a.order - b.order)` — sahneleri `order` alanına göre sıralıyor; JSON'da sıra karışık gelse bile (örneğin LLM `order: 3` sahneyi dizinin başına koymuş olsa bile) render her zaman doğru sırada oluyor — "deterministic" kelimesinin tam karşılığı.
* `<Series>` + `<Series.Sequence durationInFrames={...}>` — Remotion'ın yerleşik "bunları sırayla, her biri kendi süresi kadar göster, asla üst üste bindirme" bileşeni. Her `Series.Sequence`, kendi içindeki `<SceneView>`'a, o alt-aralık içindeyken `useCurrentFrame()`'in **0'dan başlayarak** sayması gibi bir kolaylık da sağlıyor (bu Day 9'da animasyon eklerken işimize yarayacak).
* `secondsToFrames(scene.durationSeconds, fps)` — `Scene.DurationSeconds` (saniye, .NET tarafının birimi) ile Remotion'ın `durationInFrames` (kare, Remotion'ın birimi) arasındaki **tek** dönüşüm noktası; `Math.round(seconds * fps)` — örneğin 4 saniye × 30 fps = 120 kare.

`SceneView.tsx` ise "basic scene layouts" maddesinin karşılığı — her sahne tipi için ayrı bir bileşen yazmak yerine (bu Day 16/18'in işi), **tek** bir genel bileşen, sahne tipine göre sadece arka plan rengini değiştiriyor (`BACKGROUND_BY_SCENE_TYPE: Record<SceneType, string>`), üstte küçük bir "HOOK" / "EXERCISEDEMO" etiketi (geliştirici için, sahnenin doğru sırada geldiğini görsel olarak doğrulamak amaçlı), ortada varsa asset SVG'si (`dangerouslySetInnerHTML` ile — güvenli, çünkü bu markup LLM'den değil, kendi elle yazdığımız SVG dosyalarından geliyor), ve `onScreenText`.

---

## 7. `ShortComposition.tsx` (son hali) — hepsi bir araya geliyor

```tsx
export const calculateMetadata: CalculateMetadataFunction<ShortCompositionProps> = async ({ props }) => {
  const planUrl = staticFile(props.planPath);
  const response = await fetch(planUrl);
  if (!response.ok) {
    throw new Error(`Plan JSON not found at '${planUrl}' ... Did you run 'npm run sync-assets'?`);
  }

  const plan = VideoPlanSchema.parse(await response.json());
  const allAssetIds = plan.scenes.flatMap((scene) => scene.assetIds);
  const assetSvgById = await resolveAssetSvgs(allAssetIds);

  const durationInFrames = plan.scenes.reduce(
    (total, scene) => total + secondsToFrames(scene.durationSeconds, FPS), 0);

  return { durationInFrames, props: { ...props, plan, assetSvgById } };
};

export const ShortComposition: React.FC<ShortCompositionProps> = ({ plan, assetSvgById }) => {
  return <SceneRenderer scenes={plan.scenes} assetSvgById={assetSvgById} />;
};
```

**Neden:** Bu, "backend görsel implementasyonun sahibi olmamalı" prensibinin TypeScript tarafındaki tam karşılığı — `ShortComposition` bileşeninin kendisi (asıl React component) **hiçbir** dosya okuma/doğrulama/ağ isteği içermiyor, sadece zaten doğrulanmış `plan`/`assetSvgById` prop'larını `SceneRenderer`'a aktarıyor. Tüm "kirli iş" (fetch, Zod doğrulama, asset çözümleme) `calculateMetadata`'da, Remotion'ın bunun için özel olarak tasarladığı kancada toplanıyor.

**Nasıl çalışıyor:**
* `CalculateMetadataFunction<ShortCompositionProps>` — Remotion'a özgü bir tip: render başlamadan **önce** çalışan, `props`'u dönüştürebilen ve `durationInFrames`/`fps`/`width`/`height`'i **dinamik olarak** belirleyebilen bir fonksiyon imzası. Day 7'de `durationInFrames={90}` sabitti; bugün bu sayı **gerçek plan verisinden** hesaplanıyor — bu, "gerçek içerik, hardcode edilmiş varsayımları ortadan kaldırır" prensibinin doğal bir sonucu.
* `return { durationInFrames, props: { ...props, plan, assetSvgById } };` — dönen `props` nesnesi, bileşenin **asıl** alacağı veri; `...props` (orijinal `planPath`) korunuyor, `plan` ve `assetSvgById` ekleniyor. `Root.tsx`'teki `defaultProps`'un `plan`/`assetSvgById` için bir `PLACEHOLDER_PLAN`/boş nesne içermesi gerekiyordu (TypeScript, `defaultProps`'un component'in **tam** prop tipini karşılamasını istiyor) — `calculateMetadata` her zaman bunların üzerine gerçek veriyle yazıyor, placeholder hiçbir zaman gerçekten render edilmiyor.

---

## Doğrulanan davranış

```
npx tsc --noEmit   → 0 hata (strict mode, her düzeltmeden sonra tekrar koşuldu)
npm run lint       → 0 hata/uyarı

npx remotion render ShortComposition out/sample.mp4
  → Rendered 900/900, Encoded 900/900, out/sample.mp4 (192 kB)

getVideoMetadata (bağımsız doğrulama):
  { width: 1080, height: 1920, fps: 30, durationInSeconds: 30 }
  (30s = 4+5+5+5+6+5 saniye, sample-plan.json'daki 6 sahnenin TAM toplamı -
   calculateMetadata'nın hesapladığı durationInFrames rastgele değil, doğru)

remotion still ile 3 farklı kareden görsel doğrulama:
  kare 10  (Hook)        → teal arka plan, "mascot-thinking" SVG'si, "Stuck at the same number?"
  kare 250 (ExerciseDemo)→ koyu lacivert arka plan, "pullup-bottom" SVG'si, "No swinging"
  kare 870 (Outro)       → teal arka plan, "mascot-happy" SVG'si, "Track every week"
  → üçü de sample-plan.json'daki kaynak sahneyle birebir eşleşiyor
```

Bu üç kare karşılaştırması bugünün en güçlü kanıtı: sadece "render hatasız bitti" değil, **gerçekten doğru sahne, doğru sırada, doğru içerikle, doğru asset'le** render edildi — görsel olarak teyit edilebilir, tahmine dayanmayan bir sonuç.
