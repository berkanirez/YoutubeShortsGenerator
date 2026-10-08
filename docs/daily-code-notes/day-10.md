# Day 10 — Kod Notları

30 günlük roadmap, Gün 10. Konu: altyazı sistemi — narration'ı kısa, okunaklı parçalara bölmek, her parçayı sahne içinde zamanlamak (ifade/phrase seviyesinde), Shorts arayüzünün kapattığı alanlardan uzak tutmak, anahtar ifadeyi vurgulamak ve stili plandan seçilebilir yapmak.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. `renderer/src/captions/chunking.ts` — kelimelere bölme ve vurgu

```ts
export type CaptionWord = { text: string; emphasized: boolean };
export type ChunkingRules = { maxWordsPerChunk: number; maxCharsPerChunk: number };

export const CLAUSE_END = /[.,;:!?—–]$/;

export function splitWords(narration: string): string[] {
  return narration.trim().split(/\s+/).filter((word) => word.length > 0);
}

export function normalizeWord(word: string): string {
  return word.toLowerCase().replace(/^[^a-z0-9]+|[^a-z0-9]+$/g, "");
}

export function markEmphasis(words: readonly string[], emphasis: string | null): CaptionWord[] {
  const result = words.map((text) => ({ text, emphasized: false }));
  if (!emphasis) return result;
  const target = splitWords(emphasis).map(normalizeWord).filter((w) => w.length > 0);
  if (target.length === 0) return result;
  const normalized = words.map(normalizeWord);
  for (let start = 0; start + target.length <= normalized.length; start++) {
    if (target.every((word, offset) => normalized[start + offset] === word)) {
      for (let offset = 0; offset < target.length; offset++) {
        result[start + offset] = { ...result[start + offset], emphasized: true };
      }
      break;
    }
  }
  return result;
}
```

**Neden:** Altyazıyı göstermeden önce iki şeyi bilmemiz gerekiyor: narration hangi kelimelerden oluşuyor ve bunlardan hangileri vurgulanacak. Planda `emphasis` alanı serbest metin (örn. `"core tight"`), narration'da ise aynı kelimeler noktalama ve büyük/küçük harf farkıyla geçebiliyor (`"Core,"`). Bu yüzden karşılaştırmayı "normalize edilmiş" kelimeler üzerinden yapıyoruz.

**Nasıl çalışıyor:**
* `splitWords`: `\s+` (bir veya daha fazla boşluk) ile böler. Narration'daki çift boşluk veya satır sonu boş kelime üretmez.
* `normalizeWord`: regex'teki `^[^a-z0-9]+` baştaki, `[^a-z0-9]+$` sondaki harf/rakam olmayan karakterleri siler. Ortadakilere dokunmaz: `you're`, `pull-up` aynen kalır.
* `markEmphasis`: basit bir "alt dizi arama". Vurgu ifadesinin ilk geçtiği yer işaretlenir (`break`). Vurgu **parçalara bölmeden önce** işaretleniyor; böylece ifade iki altyazı satırına bölünse bile iki satırda da renkli görünüyor.
* Vurgu ifadesi narration'da hiç yoksa hata fırlatılmıyor, sadece görmezden geliniyor. Vurgu kozmetik bir ipucu; LLM yanlış bir ifade yazdı diye bütün render'ı düşürmek yanlış olur.

---

## 2. `chunkWords` — okunabilirlik kuralları

```ts
export function chunkWords(words: readonly CaptionWord[], rules: ChunkingRules): CaptionWord[][] {
  const chunks: CaptionWord[][] = [];
  let current: CaptionWord[] = [];
  let currentLength = 0;
  const flush = () => { if (current.length > 0) { chunks.push(current); current = []; currentLength = 0; } };

  for (const word of words) {
    const lengthIfAdded = current.length === 0 ? word.text.length : currentLength + 1 + word.text.length;
    if (current.length > 0 &&
        (current.length >= rules.maxWordsPerChunk || lengthIfAdded > rules.maxCharsPerChunk)) {
      flush();
    }
    currentLength = current.length === 0 ? word.text.length : currentLength + 1 + word.text.length;
    current.push(word);
    if (CLAUSE_END.test(word.text)) flush();
  }
  flush();

  // yetim kelime kuralı
  if (chunks.length >= 2) {
    const last = chunks[chunks.length - 1];
    const previous = chunks[chunks.length - 2];
    const previousEndsClause = CLAUSE_END.test(previous[previous.length - 1].text);
    const mergedLength = chunkLength(previous) + 1 + chunkLength(last);
    if (last.length === 1 && !previousEndsClause && mergedLength <= rules.maxCharsPerChunk) {
      chunks.splice(chunks.length - 2, 2, [...previous, ...last]);
    }
  }
  return chunks;
}
```

**Neden:** `CLAUDE.md` §12: "kısa parçalar, minik paragraf altyazı yok". Telefonda okunabilir altyazının üç pratik kuralı var:
1. Bir satıra sığmalı: en fazla N kelime / N karakter.
2. Konuşan kişinin durakladığı yerde (virgül, nokta) değişmeli. Cümlenin ortasında anlamsız yerden bölünen altyazı göz yorar.
3. Sona tek başına "yetim" bir kelime kalmamalı (`"weekly"` tek başına yarım saniye ekranda kalırsa garip durur).

**Nasıl çalışıyor:**
* **Greedy (açgözlü) algoritma**: kelimeleri sırayla mevcut parçaya ekler. Ekleyince limit aşılacaksa önce mevcut parçayı kapatır (`flush`).
* `current.length > 0` koşulu önemli: limitten uzun **tek** bir kelime (örn. 34 harfli bir kelime) sonsuz döngüye ya da kaybolmaya yol açmaz, kendi parçasında tek başına durur.
* Kelime noktalama ile bitiyorsa (`CLAUSE_END`) parça hemen kapatılır.
* Yetim kelime birleştirme yalnızca önceki parça bir cümle/yan cümle sonu **değilse** yapılır. `"Rest well. Recover."` iki ayrı cümle; onları birleştirmek kural 2'yi bozardı.
* `chunks.splice(i, 2, merged)`: son iki parçayı silip yerine birleşmiş tek parçayı koyar.

---

## 3. `renderer/src/captions/captionCues.ts` — zamanlama (sesten önce)

```ts
export type CaptionCue = { startFrame: number; endFrame: number; words: CaptionWord[] };

const PAUSE_WEIGHT = 4;

function chunkWeight(chunk: readonly CaptionWord[]): number {
  const letters = chunk.reduce((t, w) => t + w.text.replace(/[^A-Za-z0-9]/g, "").length, 0);
  const lastWord = chunk[chunk.length - 1].text;
  return Math.max(1, letters) + (CLAUSE_END.test(lastWord) ? PAUSE_WEIGHT : 0);
}

export function buildCaptionCues(narration, emphasis, durationInFrames, rules): CaptionCue[] {
  if (!narration || durationInFrames <= 0) return [];
  const words = markEmphasis(splitWords(narration), emphasis);
  const chunks = chunkWords(words, rules);
  if (chunks.length === 0) return [];

  const weights = chunks.map(chunkWeight);
  const totalWeight = weights.reduce((t, w) => t + w, 0);

  const cues: CaptionCue[] = [];
  let cumulativeWeight = 0;
  let startFrame = 0;
  chunks.forEach((chunk, index) => {
    cumulativeWeight += weights[index];
    const endFrame = index === chunks.length - 1
      ? durationInFrames
      : Math.round((durationInFrames * cumulativeWeight) / totalWeight);
    if (endFrame > startFrame) {
      cues.push({ startFrame, endFrame, words: chunk });
      startFrame = endFrame;
    }
  });
  return cues;
}
```

**Neden:** Henüz ses yok (TTS Day 11'de, sesle zamanlama Day 12'de). Elimizdeki en iyi tahmin şu: sahnenin narration'ı sahnenin tamamını dolduruyor ve her parçayı söylemek, parçanın uzunluğuyla orantılı zaman alıyor. Roadmap'in de dediği gibi bu, V1 için **kabul edilebilir**. Kelime kelime "karaoke" zamanlama gerçek ses ve hizalama (alignment) verisi ister; onu beklemek bütün projeyi geciktirirdi. Day 12'de sahne süreleri gerçek ses süresinden hesaplandığında bu fonksiyon değişmeden daha doğru sonuç verecek, çünkü girdi olarak sahne süresini alıyor.

**Nasıl çalışıyor:**
* **Ağırlık = harf sayısı (+ duraklama payı)**: "Go." ile "Keep your shoulders completely down." aynı süreyi almamalı. Noktalama ile biten parçaya +4 ekleniyor, çünkü konuşmacı orada nefes alıyor.
* **Kümülatif yuvarlama**: her parçanın süresini ayrı ayrı yuvarlasaydık (örn. 23.4 → 23, 23.4 → 23, ...) hatalar birikirdi ve son altyazı sahneden birkaç kare önce biterdi ya da sonraki sahneye taşardı. Bunun yerine her **sınırı** toplam üzerinden hesaplıyoruz: `round(süre × şimdiye kadarki ağırlık / toplam ağırlık)`. Sonuçta ilk cue her zaman 0'da başlıyor, son cue her zaman tam `durationInFrames`'de bitiyor, cue'lar arasında boşluk ya da üst üste binme olmuyor. (Gerçek hayattaki karşılığı: 100 TL'yi 3 kişiye bölerken kuruşların kaybolmaması.)
* `endFrame > startFrame` kontrolü: sahnede kareden çok parça varsa (pratikte olmaz, ama 1 karelik bir sahne LLM'den gelebilir) sıfır uzunlukta cue üretmek yerine o parçayı atlıyor. Remotion'un `<Sequence>`'ı 0 süreyi kabul etmiyor.

---

## 4. `renderer/src/captions/captionStyles.ts` — safe area ve stil önayarları

```ts
export const SHORTS_SAFE_AREA = { top: 220, bottom: 480, left: 120, right: 160 } as const;

export type CaptionStyle = {
  chunking: ChunkingRules;
  fontFamily: string; fontSize: number; fontWeight: number;
  textTransform: "none" | "uppercase";
  color: string; emphasisColor: string;
  outlineColor: string; outlineWidth: number;
  boxColor: string | null;
};

export const CAPTION_STYLES = {
  bold:  { chunking: { maxWordsPerChunk: 4, maxCharsPerChunk: 15 }, fontFamily: "Arial Black, Arial, sans-serif",
           fontSize: 72, fontWeight: 900, textTransform: "uppercase", color: "#FFFFFF",
           emphasisColor: "#FB923C", outlineColor: "#000000", outlineWidth: 6, boxColor: null },
  boxed: { chunking: { maxWordsPerChunk: 5, maxCharsPerChunk: 20 }, fontFamily: "Arial, sans-serif",
           fontSize: 64, fontWeight: 700, textTransform: "none", color: "#FFFFFF",
           emphasisColor: "#2DD4BF", outlineColor: "#000000", outlineWidth: 0,
           boxColor: "rgba(15, 23, 42, 0.78)" },
} as const satisfies Record<string, CaptionStyle>;

export function resolveCaptionStyle(id: string | null): CaptionStyle {
  return id && isCaptionStyleId(id) ? CAPTION_STYLES[id] : CAPTION_STYLES[DEFAULT_CAPTION_STYLE];
}
```

**Neden:**
* **Safe area**: YouTube Shorts'ta ekranın alt kısmını başlık, kanal adı ve açıklama, sağ kenarını da beğen/yorum/paylaş butonları kaplıyor. Altyazı oraya düşerse telefonda okunamaz. Altyazıyı alttan 480px yukarıda ve sağdan 160px içeride tutuyoruz.
* **Yapılandırılabilir stil**: Plan'daki `captionStyle` alanı stil **adı** veriyor (`"bold"`, `"boxed"`), asla ham CSS değil. Animasyon id'lerinde olduğu gibi: LLM'in veya planın sayfaya keyfi stil enjekte etmesine izin vermiyoruz (`CLAUDE.md` §18 whitelist).
* Bilinmeyen/boş stil adı varsayılana düşüyor. Altyazı zorunlu, ama tam olarak hangi stilde olduğu kritik değil.

**Nasıl çalışıyor:**
* `as const satisfies Record<string, CaptionStyle>`: `satisfies` her önayarın `CaptionStyle` şekline uyduğunu **kontrol eder** ama tipi genişletmez. Böylece `keyof typeof CAPTION_STYLES` tam olarak `"bold" | "boxed"` olur ve `CaptionStyleId` tipi kendiliğinden oluşur.
* `isCaptionStyleId` içinde `hasOwnProperty` kullanılıyor, `id in CAPTION_STYLES` değil: `"toString"` gibi bir değer `in` ile `true` dönerdi (prototype'tan miras). Bunun için ayrı bir test var.
* Karakter limitleri piksel ölçümünden geliyor (bkz. bölüm 8).

---

## 5. `renderer/src/captions/CaptionRenderer.tsx` — çizim

```tsx
const CAPTION_ENTRANCE_FRAMES = 5;

function outlineStyle(width: number, color: string): React.CSSProperties {
  if (width <= 0) return {};
  return { WebkitTextStroke: `${width * 2}px ${color}`, paintOrder: "stroke fill" };
}

const CaptionChunk = ({ cue, captionStyle }) => {
  const frame = useCurrentFrame();
  const entrance = interpolate(frame, [0, CAPTION_ENTRANCE_FRAMES], [0, 1], { ...clamp, easing: Easing.out(Easing.cubic) });
  const emphasisScale = interpolate(frame, [0, CAPTION_ENTRANCE_FRAMES * 2], [1.15, 1], { ...clamp, easing: Easing.out(Easing.cubic) });
  return (
    <div style={{ display: "flex", flexWrap: "wrap", justifyContent: "center", columnGap: "0.28em",
                  ...fontAndColorFrom(captionStyle), ...outlineStyle(...),
                  opacity: entrance, transform: `scale(${0.85 + 0.15 * entrance})` }}>
      {cue.words.map((word, index) => (
        <span key={`${index}-${word.text}`}
              style={word.emphasized ? { color: captionStyle.emphasisColor, display: "inline-block",
                                         margin: "0 0.06em", transform: `scale(${emphasisScale})` } : undefined}>
          {word.text}
        </span>
      ))}
    </div>
  );
};

export const CaptionRenderer = ({ narration, emphasis, durationInFrames, captionStyle }) => {
  const cues = buildCaptionCues(narration, emphasis, durationInFrames, captionStyle.chunking);
  return (
    <AbsoluteFill style={{ pointerEvents: "none" }}>
      {cues.map((cue) => (
        <Sequence key={cue.startFrame} from={cue.startFrame}
                  durationInFrames={cue.endFrame - cue.startFrame} layout="none">
          <div style={{ position: "absolute", left: SHORTS_SAFE_AREA.left, right: SHORTS_SAFE_AREA.right,
                        bottom: SHORTS_SAFE_AREA.bottom, display: "flex", justifyContent: "center" }}>
            <CaptionChunk cue={cue} captionStyle={captionStyle} />
          </div>
        </Sequence>
      ))}
    </AbsoluteFill>
  );
};
```
(Kısaltılmış; tam hali dosyada.)

**Neden:** Hesaplama (`buildCaptionCues`) ile çizimi ayırdık. Hesaplama saf bir fonksiyon, test edilebiliyor. Bileşen sadece "bu cue'yu şu stille çiz" diyor.

**Nasıl çalışıyor:**
* **Her cue bir `<Sequence>`**: Day 8'de sahneler için `<Series>` kullandığımız gibi, hangi altyazının görüneceğine Remotion karar veriyor. "Şu an hangi cue aktif?" diye elle `if` yazmıyoruz. `<Sequence>` içinde `useCurrentFrame()` yine 0'dan başladığı için giriş animasyonu her altyazı için baştan oynuyor.
* `layout="none"`: Sequence normalde kendi `AbsoluteFill`'ini oluşturur. Biz konumlandırmayı kendimiz yaptığımız için bunu kapatıyoruz.
* **Giriş animasyonu**: 5 kare (~0.17 sn), opaklık 0→1 ve ölçek 0.85→1. Okumayı geciktirmeyecek kadar hızlı, "yeni satır geldi" sinyalini verecek kadar belirgin. Day 9'daki ölçülü hareket (restraint) ilkesinin devamı.
* **Vurgu animasyonu**: vurgulu kelime 1.15 ölçekle girip 1.0'a oturuyor. Kalıcı vurgu renkle sağlanıyor (turuncu/teal).
* **Kontur**: `-webkit-text-stroke` konturu glifin kenarının tam ortasına çizer, yani yarısı harfin içine taşar. `paint-order: stroke fill` konturu dolgunun **altına** çizdirir. Genişliği 2 katı verince sadece dış yarısı görünüyor ve harf şekli bozulmuyor.
* `flexWrap: "wrap"`: karakter limiti tahmini, gerçek piksel genişliği değil. Çok geniş harflerden (W, M) oluşan bir parça sığmazsa taşmak yerine ikinci satıra kırılıyor. Bu son çare olan bir güvenlik ağı.

---

## 6. `renderer/src/scenes/SceneRenderer.tsx` — entegrasyon (Day 8 dosyası, değişti)

```diff
 export type SceneRendererProps = {
   scenes: readonly Scene[];
   assetSvgById: Record<string, string>;
+  captionStyle: CaptionStyle;
 };
 ...
           <Series.Sequence key={scene.id} durationInFrames={durationInFrames}>
             <SceneView scene={scene} assetSvgById={assetSvgById} durationInFrames={durationInFrames} />
+            <CaptionRenderer
+              narration={scene.narration}
+              emphasis={scene.emphasis}
+              durationInFrames={durationInFrames}
+              captionStyle={captionStyle}
+            />
           </Series.Sequence>
```

**Neden:** Altyazıyı `SceneView`'ın **içine** koysaydık, sahnenin `shake`/`slide` animasyonu altyazıyı da sallardı. Hareket eden altyazı okunmaz. Bu yüzden altyazı `SceneView`'ın kardeşi (sibling) olan ayrı bir katman. Ama aynı `<Series.Sequence>` içinde duruyor, böylece zamanlaması sahne sınırlarına bağlı ve bir sonraki sahneye taşamıyor.

**Nasıl çalışıyor:** `<Series.Sequence>` çocuklarını üst üste koyar. Sonra yazılan (`CaptionRenderer`) üstte çizilir.

---

## 7. `ShortComposition.tsx` ve `samples/sample-plan.json` (değişti)

```diff
+import { resolveCaptionStyle } from "../captions/captionStyles";
 ...
-  return <SceneRenderer scenes={plan.scenes} assetSvgById={assetSvgById} />;
+  return (
+    <SceneRenderer
+      scenes={plan.scenes}
+      assetSvgById={assetSvgById}
+      captionStyle={resolveCaptionStyle(plan.captionStyle)}
+    />
+  );
```

```diff
-      "emphasis": null
+      "emphasis": "core tight"      (sahne 2; diğerleri: "same pull-up count", "chest", "lats", "ten clean reps", "every single week")
```

**Neden:** Stil plan seviyesinde seçiliyor (bütün video tek stil), vurgu ise sahne seviyesinde. Örnek plandaki vurgular, ilgili sahnenin narration'ında birebir geçen ifadeler. Böylece vurgunun gerçekten göründüğünü render'da kontrol edebiliyoruz.

---

## 8. Hata/düzeltme: altyazı iki satıra kırılıyordu

İlk render'da 20. kare:

```
IF YOU'RE STUCK
       AT
```

**Düzeltme:** `bold` stilinde `fontSize: 76 → 72`, `maxCharsPerChunk: 18 → 15`. Aynı kontrol `boxed` stilinde de yapıldı (`"Keep your core tight / and"` iki satıra kırılıyordu): `24 → 20`.

**Neden:** Karakter limiti, yazı tipinin gerçek genişliğinden bağımsız bir tahmindi. Büyük harf Arial Black çok geniş: render'da "KEEP YOUR CORE" (14 karakter) 76px'te ~755px tuttu, yani karakter başına ~54px. Safe area'nın genişliği ~800px olduğu için 18 karakter sığmıyordu.

**Nasıl çalışıyor:** Limit artık ölçülmüş genişlikten hesaplanıyor: 72px'te ~51px/karakter × 15 ≈ 765px < 800px. `boxed` stilinde kutunun 34px'lik iç dolgusu (padding) da hesaba katıldı.

---

## 9. Hata/düzeltme: kontur köşelerde kırık, vurgulu kelimeler birbirine yapışık

Belirti 1: 8 yöne `text-shadow` ile yapılan konturda harf köşelerinde (C, R) küçük çentikler vardı.
Belirti 2: kalıcı `scale(1.08)` ile vurgulanan `"core tight"`, ekranda `"coretight"` gibi okunuyordu.

**Düzeltme:** kontur için `-webkit-text-stroke` + `paint-order` kullanıldı (bölüm 5). Vurgu ölçeği girişten sonra 1.0'a dönüyor, vurgulu kelimelere `margin: 0 0.06em` eklendi.

**Neden:** `text-shadow` 8 ayrı kaydırılmış kopya çizer; köşegenler arasında boşluk kalır. `transform: scale` ise elemanı görsel olarak büyütür ama **layout'taki kutusunu büyütmez**. Büyüyen kelime, yanındaki boşluğun üstüne taşar.

**Nasıl çalışıyor:** Stroke tek ve sürekli bir kontur çizer. Vurgu artık kalıcı olarak renkle veriliyor, ölçek sadece kısa bir giriş "vuruşu" (ve margin, o vuruş sırasında bile kelimelerin değmesini engelliyor).

---

## 10. Vitest — renderer'daki ilk birim testleri

```bash
npm install --save-dev --save-exact vitest      # 5.0.3
```
```json
"test": "vitest run"
```

`chunking.test.ts` (11 test), `captionCues.test.ts` (7 test), `captionStyles.test.ts` (3 test). Örnekler:

```ts
it("ends exactly on the scene's last frame for awkward durations (no rounding drift)", () => {
  for (const duration of [31, 97, 151, 179]) {
    const cues = buildCaptionCues(NARRATION, null, duration, RULES);
    expect(cues[cues.length - 1].endFrame).toBe(duration);
    const total = cues.reduce((sum, cue) => sum + (cue.endFrame - cue.startFrame), 0);
    expect(total).toBe(duration);
  }
});
```

**Neden:** Roadmap renderer testlerini Day 25'e koyuyor. Ama altyazı zamanlaması, `CLAUDE.md` §17'nin "hatası pahalı" dediği türden bir mantık ("duration calculations"): bir yuvarlama hatası her videoyu sessizce senkronsuz yapar ve gözle fark etmek zor. Saf fonksiyonlar olduğu için test etmek de çok ucuz. Day 25'te bu altyapının üzerine schema/composition testleri eklenecek.

**Nasıl çalışıyor:** Vitest, Vite tabanlı bir test çalıştırıcı. TypeScript'i ekstra ayar gerektirmeden çalıştırıyor, `describe`/`it`/`expect` xUnit'teki sınıf/`[Fact]`/`Assert` karşılıkları. `vitest run` tek seferlik çalışır (izleme/watch modu değil). Test dosyaları `src/` altında ama hiçbir composition tarafından import edilmediği için Remotion bundle'ına girmiyor. `npm audit` sonucu Day 7'deki 10 bulguyla aynı kaldı (Vitest yeni açık eklemedi).

---

## Doğrulanan davranış

```
> renderer@1.0.0 test
> vitest run
 Test Files  3 passed (3)
      Tests  21 passed (21)

> renderer@1.0.0 lint
> eslint src && tsc
(0 hata / 0 uyarı)

npx remotion render ShortComposition out/sample.mp4
Encoded 900/900
○ out/sample.mp4 1.3 MB
getVideoMetadata → {"width":1080,"height":1920,"fps":30,"durationInSeconds":30,"codec":"h264"}

dotnet test → Domain 38, Application 35, Infrastructure 13, Api 1 — 87/87 başarılı
```

Görsel doğrulama (`npx remotion still`, bir kez bundle alınarak):
* **Kare 20** (Hook): `IF YOU'RE STUCK` tek satır, kontur temiz, Shorts'un alt arayüz bandının (alt 480px) üstünde.
* **Kare 122** (sahne 2'nin 2. karesi): altyazı giriş animasyonunun ortasında (yarı opak, hafif küçük), "CORE" turuncu. Sahne içeriği kendi `slide` animasyonuyla hareket ederken altyazı yerinde duruyor, yani katmanlar gerçekten bağımsız.
* **Kare 450** (AnatomyHighlight): `YOUR LATS,` — virgülde bölünmüş, "LATS," vurgulu, kelime aralığı doğru.
* **Kare 640** (Progress): `YOU CAN DO TEN` — "ten clean reps" vurgusu iki parçaya bölünmesine rağmen ilk parçada da renkli.
* **`boxed` stili** (geçici bir plan kopyasıyla, `captionStyle: "boxed"`): `Keep your core tight` tek satırda, koyu kutu içinde, vurgu teal renkte. Geçici plan dosyası test sonrası silindi.

Bu çıktılar şunu kanıtlıyor: altyazılar örnek video üzerinde doğru render ediliyor (roadmap kabul kriteri). Zamanlama sahne sınırları içinde kalıyor ve kaymıyor (birim testleri). Stil plandan seçilebiliyor ve bilinmeyen stil adları güvenli şekilde varsayılana düşüyor.
