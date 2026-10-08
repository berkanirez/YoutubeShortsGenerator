# Day 9 — Kod Notları

30 günlük roadmap, Gün 9. Konu: plan JSON'undaki `animation` alanını gerçek harekete çevirmek — interpolation, easing, spring, ölçülü hareket (restraint), ve "unutulan animasyonu" derleme zamanında yakalayan bir kayıt tablosu.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. `renderer/src/animations/primitives.ts` — sözleşme (tipler)

```ts
export type AnimationContext = {
  frame: number;          // sahneye göre yerel kare (0 = sahnenin ilk karesi)
  fps: number;
  durationInFrames: number;
};

export type AnimationResult = {
  style: React.CSSProperties;
  formatText?: (text: string) => string;
};

export type AnimationFn = (context: AnimationContext) => AnimationResult;
```

**Neden:** Dokuz farklı animasyon var ve hepsinin aynı "şekle" uyması gerekiyor ki `SceneView` hangisi olduğunu bilmeden hepsini aynı şekilde uygulayabilsin. Bu, Day 3'teki `ILlmProvider` ile aynı fikir, sadece bir arayüz yerine bir **fonksiyon tipi**: "bir animasyon, bir kare bilgisi alıp bir stil döndüren şeydir."

**Nasıl çalışıyor:**
* `frame` **sahneye göre yerel**: Day 8'de kullandığımız `<Series.Sequence>`, içindeki bileşenler için `useCurrentFrame()`'i her sahnenin başında 0'dan başlatıyor. Bu sayede her animasyon "sahnenin ilk 15 karesinde şunu yap" diye yazılabiliyor; sahnenin videonun kaçıncı saniyesinde olduğunu bilmesine gerek yok.
* `formatText?` opsiyonel: sadece `counter` kullanıyor. Diğer sekiz animasyon sadece konumu/opaklığı değiştiriyor; `counter` ise metnin **ne söylediğini** değiştiriyor ("0 clean reps" → "10 clean reps"). Bu bir CSS stiliyle ifade edilemeyeceği için ayrı bir alan.

---

## 2. Animasyon fonksiyonları — interpolation, easing, spring

```ts
const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;
const ENTRANCE_FRAMES = 15;

const fadeIn = (frame: number, frames = ENTRANCE_FRAMES): number =>
  interpolate(frame, [0, frames], [0, 1], clamp);

export const fade: AnimationFn = ({ frame }) => ({
  style: { opacity: fadeIn(frame) },
});

export const slide: AnimationFn = ({ frame }) => {
  const progress = interpolate(frame, [0, ENTRANCE_FRAMES], [0, 1], {
    ...clamp,
    easing: Easing.out(Easing.cubic),
  });
  return {
    style: { opacity: progress, transform: `translateY(${(1 - progress) * 120}px)` },
  };
};

export const pop: AnimationFn = ({ frame, fps }) => {
  const progress = spring({ frame, fps, config: { damping: 10, stiffness: 180 } });
  return {
    style: {
      opacity: fadeIn(frame, 6),
      transform: `scale(${interpolate(progress, [0, 1], [0.6, 1])})`,
    },
  };
};

export const punchZoom: AnimationFn = ({ frame }) => {
  const scale = interpolate(frame, [0, 8], [1.35, 1], { ...clamp, easing: Easing.out(Easing.exp) });
  return { style: { opacity: fadeIn(frame, 4), transform: `scale(${scale})` } };
};
```

(Dosyada ayrıca `shake`, `bounce`, `pulse`, `counter`, `highlight` var — aşağıda ayrı ele alınıyor.)

**Neden:** Roadmap'in "easing, interpolation, restraint in motion design" öğretme hedefinin kendisi. `ENTRANCE_FRAMES = 15` (30 fps'te yarım saniye) bilinçli bir sınır: `CLAUDE.md` §11 "her hareket anlamayı ya da akılda kalmayı artırmalı, gereksiz/rastgele efektlerden kaçın" diyor. Giriş yarım saniyede bitiyor, sahnenin geri kalanında izleyici hareketi değil içeriği okuyor.

**Nasıl çalışıyor:**
* `interpolate(frame, [0, 15], [0, 1], clamp)` — "kare 0'dayken 0, kare 15'teyken 1, arası doğrusal" eşlemesi. `clamp`, 15. kareden sonra değerin 1'de kalmasını (ve 0'dan önce 0'da kalmasını) sağlıyor; aksi halde kare 30'da opaklık 2 olurdu. `as const`, TypeScript'e `"clamp"`'in herhangi bir string değil tam olarak `"clamp"` literal tipi olduğunu söylüyor; Remotion'ın parametresi bu literal tipi bekliyor.
* `easing: Easing.out(Easing.cubic)` — aynı 0→1 geçişinin **hız profilini** değiştiriyor. Doğrusal (linear) geçiş her kare eşit yol alır ve mekanik görünür. `Easing.out` hızlı başlayıp yerine yavaşlayarak oturur, "geldi ve durdu" gibi hissettirir. `slide` ve `punchZoom` aynı `interpolate` fonksiyonunu kullanıyor ama farklı easing'lerle (`cubic` daha yumuşak, `exp` çok sert bir başlangıç) çok farklı karakterde hareketler çıkıyor.
* `spring({ frame, fps, config: { damping: 10, stiffness: 180 } })` — başlangıç/bitiş noktası değil, fiziksel bir **yay** tarifi alıyor ve 0'dan 1'e, hedefi hafifçe aşıp geri dönen bir değer üretiyor. `damping` (sönümleme) düştükçe daha çok zıplıyor; `bounce`'ta `damping: 7` ile `pop`'tan daha "lastikli" bir his elde ediliyor.
* `interpolate(progress, [0, 1], [0.6, 1])` — `interpolate` sadece kare numaraları için değil, **herhangi** bir sayıyı başka bir aralığa eşlemek için kullanılabiliyor. Burada spring'in ürettiği 0→1 değeri, 0.6→1 ölçek aralığına çevriliyor.

---

## 3. `shake`, `bounce`, `pulse` — determinizm

```ts
export const shake: AnimationFn = ({ frame }) => {
  const decay = interpolate(frame, [0, 18], [1, 0], clamp);
  const offsetX = Math.sin(frame * 1.8) * 18 * decay;
  return { style: { opacity: fadeIn(frame, 4), transform: `translateX(${offsetX}px)` } };
};

export const pulse: AnimationFn = ({ frame, fps }) => {
  const scale = 1 + Math.sin((frame / fps) * Math.PI * 2) * 0.03;
  return { style: { opacity: fadeIn(frame), transform: `scale(${scale})` } };
};
```

**Neden:** "Titreme" efekti için ilk akla gelen genelde `Math.random()` ile rastgele bir kaydırmadır. Ama `CLAUDE.md` §22 "deterministic renders" istiyor: aynı plan iki kez render edildiğinde **birebir aynı** kareler çıkmalı. Rastgelelik buna aykırı; ayrıca Remotion kareleri paralel ve sırasız render edebiliyor, rastgele değerler kareler arasında tutarsız titremeye yol açardı.

**Nasıl çalışıyor:**
* `Math.sin(frame * 1.8)` — kare numarasının saf bir fonksiyonu; kare 7 her zaman aynı kaydırmayı verir. Sinüs -1 ile 1 arasında salındığı için sağa-sola gidip gelen bir hareket üretiyor.
* `decay` — 18 kare boyunca 1'den 0'a inen bir çarpan. Titreme güçlü başlıyor, 0.6 saniyede sönüp duruyor; sahnenin geri kalanında içerik sabit.
* `pulse` dokuz animasyon içinde **sürekli** hareket eden tek animasyon, bu yüzden en ölçülüsü: `* 0.03` ile sadece %3 büyüyüp küçülüyor, saniyede bir döngü. `sin(0) = 0` olduğu için kare 0'da ölçek tam olarak 1, ilk karede bir sıçrama olmuyor.

---

## 4. `counter` ve `highlight`

```ts
export const counter: AnimationFn = ({ frame, fps, durationInFrames }) => {
  const countFrames = Math.min(Math.round(fps * 1.2), durationInFrames);
  return {
    style: { opacity: fadeIn(frame, 6) },
    formatText: (text) =>
      text.replace(/\d+/, (match) =>
        String(Math.round(interpolate(frame, [0, countFrames], [0, Number(match)], {
          ...clamp,
          easing: Easing.out(Easing.cubic),
        }))),
      ),
  };
};

export const highlight: AnimationFn = ({ frame }) => {
  const glow = interpolate(frame, [0, 10, 25], [0, 28, 10], clamp);
  return { style: { opacity: fadeIn(frame), filter: `drop-shadow(0 0 ${glow}px #FB923C)` } };
};
```

**Neden:** Roadmap Day 9 için yedi animasyon listeliyor, ama Day 5'te yazdığımız `AnimationIdSchema` (ve backend'deki `AnimationId` enum'u) `CLAUDE.md` §11'deki dokuzunu içeriyor. Yani LLM bir plana bugün de `"counter"` yazabilir. Bu iki animasyonu "sonra yaparız" deyip atlasaydık, bu sahneler ya hata verecek ya da sessizce animasyonsuz kalacaktı. İkisini de gerçek davranışla yazdım.

**Nasıl çalışıyor:**
* `text.replace(/\d+/, (match) => ...)` — metindeki **ilk** sayıyı bulup, o sayıyı 0'dan hedefe doğru sayan bir değerle değiştiriyor. `"10 clean reps -> add weight"` metninde `10` sayılıyor, metnin geri kalanı aynen kalıyor. İçinde sayı olmayan bir metinde `replace` hiçbir şey değiştirmez; `counter` o durumda düz bir `fade` gibi davranır, hata vermez.
* `Math.min(Math.round(fps * 1.2), durationInFrames)` — sayma 1.2 saniyede bitiyor, ama 1 saniyelik çok kısa bir sahnede sahne süresini aşmasın diye sahne süresiyle sınırlanıyor. `durationInFrames`'i `AnimationContext`'e bu yüzden ekledim.
* `interpolate(frame, [0, 10, 25], [0, 28, 10], clamp)` — `interpolate` ikiden fazla nokta da alabiliyor: kare 0'da 0, kare 10'da 28 (zirve), kare 25'te 10 (yerleşik değer). Parlama bir kez dikkat çekiyor, sonra hafif bir seviyede kalıyor. `#FB923C`, Day 6 maskotunun turuncu vurgu rengi; animasyon da görsel kimlikle tutarlı.

---

## 5. `ANIMATIONS` kayıt tablosu — exhaustive `Record`

```ts
export const ANIMATIONS: Record<AnimationId, AnimationFn> = {
  fade, slide, pop, punchZoom, shake, bounce, pulse, counter, highlight,
};

export function getAnimation(id: AnimationId, context: AnimationContext): AnimationResult {
  return ANIMATIONS[id](context);
}
```

**Neden:** "ID'den davranışa" eşleme için iki yaygın yol var: bir `switch` + `default` dalı, ya da bir tablo. `switch`'in `default` dalı tehlikeli bir kolaylık sağlar: enum'a yeni bir değer eklenip kodu yazılmazsa program sessizce `default`'a düşer ve sahne animasyonsuz render edilir, kimse fark etmez. `Record<AnimationId, AnimationFn>` ise TypeScript'e "bu nesnede `AnimationId`'nin **her** değeri için bir anahtar olmak zorunda" diyor.

**Nasıl çalışıyor:** `AnimationId`, Day 8'de Zod şemasından türetilen `"fade" | "slide" | ... | "highlight"` birleşim (union) tipi. `Record<K, V>`, "K'deki her değer için V tipinde bir alan" anlamına geliyor. Bunu varsaymak yerine test ettim: `highlight`'ı tablodan geçici olarak çıkarınca derleme şu hatayla durdu:

```
error TS2741: Property 'highlight' is missing in type '{ fade: AnimationFn; ... counter: AnimationFn; }'
but required in type 'Record<"fade" | "slide" | ... | "highlight", AnimationFn>'.
```

Bu desen (strategy/registry) backend'de de çok yaygın: ödeme sağlayıcısı seçimi, bildirim kanalı seçimi gibi. Exhaustive tiple "bir durumu unutmak" çalışma zamanı hatası olmaktan çıkıp derleme hatası oluyor.

---

## 6. `SceneView.tsx` ve `SceneRenderer.tsx` — animasyonu uygulama

```tsx
const frame = useCurrentFrame();
const { fps } = useVideoConfig();
const animation = getAnimation(scene.animation, { frame, fps, durationInFrames });

const onScreenText =
  scene.onScreenText && animation.formatText
    ? animation.formatText(scene.onScreenText)
    : scene.onScreenText;

// ...
<div style={{ display: "flex", flexDirection: "column", alignItems: "center", ...animation.style }}>
  {/* asset SVG + onScreenText */}
</div>
```

```tsx
// SceneRenderer.tsx
const durationInFrames = secondsToFrames(scene.durationSeconds, fps);
return (
  <Series.Sequence key={scene.id} durationInFrames={durationInFrames}>
    <SceneView scene={scene} assetSvgById={assetSvgById} durationInFrames={durationInFrames} />
  </Series.Sequence>
);
```

**Neden:** Animasyon, asset + metin grubunu saran **tek bir** `div`'e uygulanıyor; arka plan (`AbsoluteFill`) hiç hareket etmiyor. Bu bilinçli: arka plan renginin aniden değişmesi "yeni sahne başladı" sinyali. Arka plan da hareket etseydi, sahne geçişi ile sahne-içi animasyon birbirine karışırdı.

**Nasıl çalışıyor:**
* `...animation.style` — spread operatörü, animasyonun döndürdüğü stil alanlarını (`opacity`, `transform`, `filter`) layout stillerinin üzerine ekliyor. `SceneView` hangi animasyonun hangi alanı kullandığını bilmiyor; sadece "ne döndüyse uygula" diyor.
* `durationInFrames` neden prop olarak geliyor: `useVideoConfig().durationInFrames` bir `Series.Sequence` içinde bile **tüm videonun** süresini döndürüyor, sahnenin süresini değil. Sahne süresi zaten `SceneRenderer`'da `secondsToFrames` ile hesaplanıyor, aynı değeri aşağıya geçirmek en basit ve doğru yol.
* Sol üstteki geliştirici etiketi artık `HOOK · fade` gibi hem sahne tipini hem animasyonu gösteriyor. Render edilmiş tek bir karenin hangi sahneden ve hangi animasyondan geldiği kareye bakınca anlaşılıyor (aşağıdaki doğrulamada bu çok işe yaradı). Bu etiket nihai tasarımın parçası değil; Day 16'nın görsel geçişinde kaldırılacak.

---

## 7. `samples/animation-showcase-plan.json`

```json
{
  "scenes": [
    { "order": 1, "type": "Hook",        "animation": "fade",      "onScreenText": "fade",          "durationSeconds": 4, ... },
    { "order": 2, "type": "ExerciseDemo","animation": "slide",     "onScreenText": "slide",         ... },
    ...
    { "order": 8, "type": "Progress",    "animation": "counter",   "onScreenText": "10 clean reps", ... },
    { "order": 9, "type": "Outro",       "animation": "highlight", "onScreenText": "highlight",     ... }
  ]
}
```

**Neden:** Roadmap'in kabul kriteri "JSON animation ID changes scene behavior". Bunu göstermenin en net yolu, her sahnesi farklı bir animasyon kullanan, diğer her şeyi (süre, düzen) aynı olan bir plan. Böylece sahneler arasındaki fark **sadece** animasyondan kaynaklanıyor.

**Nasıl çalışıyor:** 9 sahne × 4 saniye = 36 saniye; ilk sahne `Hook`, son sahne `Outro`. Renderer'ın Zod şeması bu kuralları kontrol etmiyor ama backend'in `VideoPlanValidator`'ı ediyor; bu örnek plan iki tarafın kurallarına da uyuyor, yani "gerçekte üretilebilecek" bir plan. Render ederken varsayılan plan yerine bunu seçmek için Remotion CLI'ın `--props` bayrağı kullanılıyor; bu, `Root.tsx`'teki `defaultProps.planPath`'in üzerine yazıyor:

```
npx remotion render ShortComposition out/animation-showcase.mp4 --props='{"planPath":"samples/animation-showcase-plan.json"}'
```

---

## 8. Doğrulama araç zinciri — bir kez bundle, çok still

```
npx remotion bundle
npx remotion still build ShortComposition out/still-fade.png --frame=4 --props=...
npx remotion still build ShortComposition out/still-slide.png --frame=124 --props=...
...
```

**Neden:** Day 8'de her `remotion still` komutu projeyi baştan bundle ediyordu. Bugün yedi kare almam gerektiği için önce `npx remotion bundle` ile tek bir `build/` klasörü ürettim, sonra her still'i doğrudan bu hazır bundle'dan aldım.

**Nasıl çalışıyor:** `remotion still` ve `remotion render`, giriş noktası olarak `src/index.ts` yerine hazır bir bundle klasörü de kabul ediyor (`build`). Kare numaraları sahne başlangıçlarına göre seçildi: her sahne 120 kare, yani 2. sahne kare 120'de, 4. sahne kare 360'ta başlıyor; `--frame=124` "slide sahnesinin 4. karesi" anlamına geliyor. `build/` yeniden üretilebilir bir çıktı olduğu için kullanımdan sonra silindi ve `renderer/.gitignore`'a eklendi.

---

## Doğrulanan davranış

```
npm run lint (eslint src && tsc)  → 0 hata/uyarı

Exhaustiveness testi:
  ANIMATIONS'tan 'highlight' geçici olarak çıkarıldı → tsc: TS2741 'highlight' is missing
  geri eklendi → tsc temiz

npx remotion render ... animation-showcase-plan.json → 1080/1080 kare (36 sn)

Aynı sahne-içi anda (4. kare) dört farklı animasyon:
  fade      → içerik yerinde, ~%25 opaklıkta
  slide     → içerik son konumunun altında, soluk
  punchZoom → zaten tam opak ve neredeyse yerine oturmuş (8 karelik bir "vuruş")
  bounce    → içerik son konumunun üstünde

counter sahnesi:
  sahne-içi kare 10 → "6 clean reps"
  sahne-içi kare 60 → "10 clean reps"

slide, sahne-içi kare 60 → tam opak, fade'in oturmuş haliyle aynı konumda

Day 8'in sample.mp4'ü yeniden render edildi → animasyonlu (192 kB → 491 kB)
```

Bu çıktı iki şeyi birlikte kanıtlıyor: (1) JSON'daki `animation` ID'sini değiştirmek sahnenin davranışını gerçekten değiştiriyor; aynı sahne-içi karede dört animasyon dört farklı görüntü üretti. (2) Animasyonlar ölçülü: hepsi sadece girişi değiştiriyor ve aynı oturmuş düzene varıyor (slide'ın 60. karesi fade'in son haliyle aynı yerde). Hareket içeriğin önüne geçmiyor.
