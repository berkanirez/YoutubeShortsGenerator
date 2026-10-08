# Day 7 — Kod Notları

30 günlük roadmap, Gün 7. Konu: tamamen yeni bir dünya — `renderer/` adında bağımsız bir Node.js/TypeScript/React projesi, Remotion ile frame-tabanlı render, ve gerçek bir 1080x1920 MP4'ün ilk kez üretilmesi.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. `npm create video@latest -- --yes --blank renderer --no-tailwind`

```
npm create video@latest -- --help   # önce non-interactive bayrakları keşfetmek için
npm create video@latest -- --yes --blank renderer --no-tailwind
```

**Neden:** Bugüne kadarki her gün .NET dünyasındaydı; bu ilk kez elle bir Node/TypeScript projesi kuruyoruz. Remotion'ın kendi resmi iskelet aracını (`create-video`) kullanmak — elle `package.json`/`tsconfig.json` yazmak yerine — gerçek bir Remotion projesinin gerçek hayatta nasıl başlatıldığını gösteriyor, ve sürüm uyumlu bağımlılıkları otomatik kuruyor.

**Nasıl çalışıyor:**
* `npm create <paket>@latest -- <argümanlar>` — npm'in "scaffolding tool çalıştır" kısayolu; `npx create-video@latest`'in eşdeğeri. `--` işareti, ondan sonraki argümanların `npm`'e değil, çalıştırılan `create-video` paketine gittiğini belirtiyor.
* `--yes` — etkileşimsiz (non-interactive) mod; aksi halde araç "hangi şablon", "TypeScript mi" gibi sorular sorardı (bu ortamda terminal etkileşimi çalışmaz).
* `--blank` — Remotion'ın sunduğu ~19 şablondan en sade olanı: tek bir placeholder composition, gereksiz demo içeriği yok. `--hello-world` gibi daha "gösterişli" şablonlar yerine bilinçli olarak bu seçildi — `CLAUDE.md`'nin "gereksiz karmaşıklık ekleme" ilkesine uygun.
* Kurulum ~356 paket indirdi (React, Remotion çekirdeği, ESLint/TypeScript araçları) ve "10 high severity vulnerabilities" uyarısı verdi — bölüm 2'de ele alınıyor.

---

## 2. `npm audit` — güvenlik açığı incelemesi (düzeltme değil, bilinçli karar)

```
npm audit fix --dry-run
→ (hiçbir değişiklik önerilmedi)

Kök neden: braces paketi (GHSA-vfj7-8cjw-p6xm, stack-exhaustion DoS)
→ micromatch → fast-glob → @typescript-eslint/typescript-estree
→ @remotion/eslint-config-flat
"No fix available"
```

**Neden:** Day 1'de benzer bir NuGet açığını (Microsoft.OpenApi) sürüm yükselterek düzeltmiştik; burada **aynı refleksle** önce inceledim. Ama bu sefer durum farklı: `braces`'ın düzeltmesi **henüz yayınlanmamış** (`npm audit fix --dry-run` hiçbir değişiklik önermedi — yani zorla bir düzeltme uygulamak mümkün değil, sadece paketleri rastgele kırabilecek bir `--force` downgrade'i denenebilirdi).

**Nasıl çalışıyor / karar:** Açığın zincirini takip ettiğimde, hepsinin **tek bir noktada**, `@remotion/eslint-config-flat` (sadece `eslint`/`lint` script'i çalıştırılırken kullanılan bir geliştirme bağımlılığı) üzerinden geldiğini gördüm — gerçek render koduna (üretimde çalışan hiçbir şeye) dokunmuyor. Zafiyetin kendisi de "stack-exhaustion DoS" — yani kötü niyetli bir glob deseni verilirse linter'ın çökmesi, bir veri sızıntısı ya da kod çalıştırma değil. Bu üç gerçek (düzeltme yok, sadece dev-time lint aracı, düşük gerçek risk) birleşince, zorla bir "düzeltme" uygulamak yerine (ki bu, yeni kurulan, çalışan Remotion şablonunu uyumsuz paket sürümleriyle bozma riski taşırdı) durumu olduğu gibi not almak daha doğru bir mühendislik kararı — `docs/DAY_STATE.md`'de kayıtlı, gelecekte upstream bir düzeltme çıkarsa tekrar bakılacak.

---

## 3. `src/compositions/ShortComposition.tsx`

```tsx
import React from "react";
import {
  AbsoluteFill,
  interpolate,
  spring,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";

export type ShortCompositionProps = {
  title: string;
};

export const ShortComposition: React.FC<ShortCompositionProps> = ({ title }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();

  const opacity = interpolate(frame, [0, 20], [0, 1], {
    extrapolateRight: "clamp",
  });
  const scale = spring({
    frame,
    fps,
    config: { damping: 12 },
  });

  return (
    <AbsoluteFill
      style={{
        backgroundColor: "#0F766E",
        justifyContent: "center",
        alignItems: "center",
      }}
    >
      <div
        style={{
          opacity,
          transform: `scale(${scale})`,
          color: "#FB923C",
          fontSize: 64,
          fontWeight: 700,
          fontFamily: "Arial, sans-serif",
          textAlign: "center",
          padding: "0 80px",
        }}
      >
        {title}
      </div>
    </AbsoluteFill>
  );
};
```

**Neden:** `CLAUDE.md` §11'in hedef bileşen listesindeki ilk gerçek Remotion bileşeni (`ShortComposition` ismiyle birebir). Bugünün amacı sadece "Remotion gerçekten video üretiyor mu" sorusuna cevap vermek — bu yüzden içerik kasıtlı olarak minimal: sabit bir başlık metni, basit bir fade+scale animasyonu, Day 6'nın maskot paletiyle (teal `#0F766E`, turuncu `#FB923C`) tutarlı renkler. Gerçek sahne içeriği (VideoPlan'dan gelen) Day 8'in konusu.

**Nasıl çalışıyor:**
* `export type ShortCompositionProps = { title: string };` — bu composition'ın beklediği "prop şekli". React'teki normal bir component prop tipinden hiçbir farkı yok; Remotion'a özgü olan kısım, bu prop'ların **render zamanında** (her kare hesaplanırken) sabit kalması — React'teki gibi kullanıcı etkileşimiyle değişmiyor, render başlamadan önce (CLI'dan ya da `defaultProps`'tan) bir kez belirleniyor.
* `const frame = useCurrentFrame();` — Remotion'a özgü bir React hook'u: "şu an hangi kareyi render ediyorum" bilgisini veriyor (0'dan `durationInFrames-1`'e kadar). Bu, Remotion'ın temel fikri: normal React'te "zaman" diye bir prop yoktur, Remotion'da **her şey** bu `frame` değerinin bir fonksiyonu.
* `const { fps } = useVideoConfig();` — composition'ın `Root.tsx`'te tanımlanan ayarlarına (fps, genişlik, yükseklik, toplam kare sayısı) erişim sağlayan başka bir hook.
* `interpolate(frame, [0, 20], [0, 1], { extrapolateRight: "clamp" })` — "kare 0'dan 20'ye giderken, değeri 0'dan 1'e **doğrusal olarak** değiştir" diyor (opaklık için). `extrapolateRight: "clamp"`, 20. kareden sonra değerin 1'de **sabit kalmasını** sağlıyor (aksi halde doğrusal fonksiyon 1'i aşmaya devam ederdi).
* `spring({ frame, fps, config: { damping: 12 } })` — doğrusal değil, **fizik tabanlı** bir yaylanma eğrisi üretiyor (başta hızlı büyüyüp hafifçe "zıplayan" bir ölçek efekti) — `interpolate`'in aksine, başlangıç/bitiş değerlerini elle vermek yerine bir yay sabiti (`damping`) ile doğal bir hareket tanımlıyor.
* `<AbsoluteFill style={{ ... }}>` — Remotion'ın `position: absolute; inset: 0` kısayolu; her composition'ın kök elemanı genelde budur, tüm kare alanını kaplar.
* `transform: \`scale(${scale})\`` — standart CSS; Remotion'ın özel bir "animasyon API"si yok, düz CSS/React stilleri kullanılıyor, sadece değerler (`opacity`, `scale`) her karede `frame`'e göre yeniden hesaplanıyor.

---

## 4. `src/Root.tsx` — composition'ın kaydı

```tsx
import React from "react";
import { Composition } from "remotion";
import { ShortComposition } from "./compositions/ShortComposition";

export const RemotionRoot: React.FC = () => {
  return (
    <Composition
      id="ShortComposition"
      component={ShortComposition}
      durationInFrames={90}
      fps={30}
      width={1080}
      height={1920}
      defaultProps={{
        title: "Fitness Shorts Generator — Day 7 Render Test",
      }}
    />
  );
};
```

Şablonun ürettiği placeholder `Composition.tsx` (1280x720, `null` render eden boş bir bileşen) tamamen silindi — projede artık tek, gerçek bir composition var.

**Neden:** `CLAUDE.md` §11'in video varsayılanlarının (1080x1920, 30fps) ilk somut uygulaması. `Root.tsx`, Remotion'da **hangi composition'ların var olduğunu** merkezi olarak bildiren dosya — bir uygulamanın `Program.cs`'teki DI kayıtlarına benzer bir rolü var: "bu proje şu composition'ları tanıyor, her birinin boyutu/süresi/varsayılan prop'ları bunlar."

**Nasıl çalışıyor:** `<Composition id="ShortComposition" component={ShortComposition} ... />` — `id`, `npx remotion render <id> <çıktı>` komutunda hangi composition'ın render edileceğini seçmek için kullanılıyor (bölüm 6). `durationInFrames={90}` + `fps={30}` = 3 saniyelik bir video (`90 / 30 = 3`) — Day 7 için bilinçli olarak kısa tutuldu (gerçek bir Short'un 25-45 saniyesi değil, sadece "çalışıyor mu" testi). `defaultProps`, CLI'dan ayrı bir prop verilmezse kullanılacak varsayılan `ShortCompositionProps` değeri.

---

## 5. Strict TypeScript doğrulaması

```
npx tsc --noEmit   → 0 hata
npm run lint       → eslint src && tsc → 0 hata/uyarı
```

**Neden:** `CLAUDE.md` §22 açıkça "strict mode, no `any` unless justified" diyor. `create-video`'nun ürettiği `tsconfig.json` zaten `"strict": true` ile geliyor — bunu elle kurmaya gerek kalmadı, sadece yazdığımız kodun (bölüm 3-4) bu modda gerçekten temiz derlendiğini doğruladık.

**Nasıl çalışıyor:** `tsc --noEmit`, kodu gerçek JavaScript'e derlemeden sadece **tip kontrolü** yapıyor (CI/test ortamlarında yaygın kullanılan bir desen — derleme çıktısına ihtiyaç yok, sadece "tipler tutarlı mı" sorusuna cevap isteniyor). `strict: true`, birden fazla katı kontrolü birden açıyor (`strictNullChecks`, `noImplicitAny` gibi) — örneğin `ShortCompositionProps`'taki `title: string` (opsiyonel değil) sayesinde, `defaultProps`'ta `title` unutulsaydı derleme hatası alırdık.

---

## 6. `npx remotion render ShortComposition out/test.mp4` — ilk gerçek MP4

```
Rendered 90/90
Encoded 90/90
+ out/test.mp4   241.8 kB
```

**Neden:** Roadmap'in Day 7 kabul kriteri tam olarak bu: "renderer creates a basic MP4 from static test props." Bu komut, bölüm 3-4'teki composition'ı gerçekten **kare kare render edip** (React → görüntü), sonra bu kareleri bir video dosyasında **encode edip** diske yazıyor.

**Nasıl çalışıyor:** "Rendered 90/90" — Remotion, `durationInFrames={90}` olan composition'ın her bir karesini (React component'i o karenin `frame` değeriyle render edip bir görüntüye "fotoğraflıyor") ayrı ayrı işliyor. "Encoded 90/90" — bu 90 ayrı görüntüyü, bir video codec'i (H.264) kullanarak tek bir MP4 akışına birleştiriyor. Dikkat çekici nokta: bu makinede FFmpeg **hiç kurulu değil** (Day 1'de doğrulanmıştı), ama render yine de sorunsuz tamamlandı — Remotion 4, kendi Rust tabanlı "compositor" motorunu kullanıyor ve FFmpeg'e (en azından bu temel senaryoda) bağımlı değil. Bu, roadmap'in FFmpeg'i Day 12'ye kadar gerektirmeyeceği varsayımını **gerçekten doğruluyor**.

---

## 7. Render çıktısının gerçekten doğru olduğunu kanıtlama

```js
// check-metadata.mjs (geçici, doğrulamadan sonra silindi)
import { getVideoMetadata } from "@remotion/renderer";
const metadata = await getVideoMetadata("out/test.mp4");
console.log(JSON.stringify(metadata, null, 2));
```

```json
{
  "fps": 30,
  "width": 1080,
  "height": 1920,
  "durationInSeconds": 3,
  "codec": "h264",
  "canPlayInVideoTag": true,
  "supportsSeeking": true
}
```

**Neden:** "Render komutu hata vermedi" ile "gerçekten doğru boyutta, doğru fps'te, gerçekten oynatılabilir bir video üretildi" aynı şey değil. Day 2'deki "gerçek SQLite round-trip" testinin aynısı — render log'una güvenmek yerine, **üretilen dosyanın kendisini** bağımsız bir araçla (Remotion'ın kendi `getVideoMetadata` API'si) sorgulayıp iddia edilen özelliklerin gerçekten dosyada yazılı olduğunu doğruladık.

**Nasıl çalışıyor:** `getVideoMetadata`, `@remotion/renderer` paketinin (zaten bir bağımlılığımız) sağladığı, bir video dosyasını açıp gerçek container/codec bilgisini okuyan bir fonksiyon — `ffprobe`'un yaptığı işi, FFmpeg kurulu olmadan, Remotion'ın kendi araçlarıyla yapıyor. Dönen değerler (`width: 1080, height: 1920, fps: 30`), `Root.tsx`'teki (bölüm 4) `Composition` tanımıyla **birebir** eşleşiyor — yani Remotion'a verdiğimiz ayarlar, gerçekten dosyaya işlenmiş.

---

## Doğrulanan davranış

```
npx tsc --noEmit                              → 0 hata (strict mode)
npm run lint (eslint src && tsc)              → 0 hata/uyarı
npx remotion render ShortComposition out/test.mp4
  → Rendered 90/90, Encoded 90/90, out/test.mp4 (241.8 kB)

Gerçek dosya doğrulaması (getVideoMetadata):
  width: 1080, height: 1920, fps: 30, durationInSeconds: 3,
  codec: "h264", canPlayInVideoTag: true

git add -n renderer → sadece 11 gerçek kaynak/config dosyası (node_modules/out hariç)
```

Bu çıktı, Day 7'nin asıl amacını kanıtlıyor: Remotion, ASP.NET backend'ine hiç dokunmadan, tamamen bağımsız bir Node/TypeScript projesi olarak, gerçek, oynatılabilir, tam olarak istenen boyut ve kare hızında bir MP4 üretebiliyor — FFmpeg kurulumuna bile gerek kalmadan. Day 8'den itibaren bu composition, sabit bir başlık yerine gerçek bir `VideoPlan`'ın sahnelerini render etmeye başlayacak.
