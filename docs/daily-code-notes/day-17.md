# Day 17 — Kod Notları

30 günlük roadmap, Gün 17. Konu: kontrollü ses tasarımı. Kontrollü id'lerle eşlenmiş ses efektleri, arka plan müziği, ses seviyesi varsayılanları, fade in/out. Bütün sesler projenin kendisi tarafından matematiksel olarak sentezlendi, hiçbir şey indirilmedi. Ses dengesi kulakla değil **ölçümle** doğrulandı.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. `tools/audio/generate_audio_assets.py` — sesleri sentezlemek

```python
RATE = 44100
rng = np.random.default_rng(seed=17)          # sabit tohum -> her çalıştırmada aynı dosyalar

def envelope(n, attack, release): ...         # doğrusal giriş, karesel çıkış: uçlarda "tık" yok
def lowpass(signal, width): ...               # hareketli ortalama: gürültüyü şekillendirmek için
def note(freq, duration, harmonics=(1.0, 0.35, 0.15)): ...   # temel frekans + harmonikler

def sfx_pop():     # 500 -> 1100 Hz yukarı kayan kısa sinüs ("belirdi")
def sfx_whoosh():  # parlaklığı artıp azalan filtrelenmiş gürültü ("hava geçti")
def sfx_click():   # 30 ms'lik sönümlenen gürültü patlaması
def sfx_impact():  # 120 -> 45 Hz düşen alt frekans + kısa çatırtı
def sfx_success(): # C6 -> G6 yükselen iki nota ("tamam")
def sfx_error():   # G4 -> Eb4 düşen, hafif cızırtılı iki nota ("hayır", sert olmadan)

def music_loop(bpm, progression, with_drums):
    # her ölçü: akor pad'i + her vuruşta bas (kök, bir oktav aşağı) + sekizlik arpej
    # davul: 1. ve 3. vuruşta kick, ara vuruşlarda hafif hi-hat
upbeat = music_loop(104, [C, G, Am, F] * 2, True)    # 18.5 sn
calm   = music_loop(80,  [Am, F, C, G] * 2, False)   # 24 sn
```

**Neden:** `CLAUDE.md` §13: "sadece kullanım hakkı açık olan yerel ses, ticari şarkı yok". "Ücretsiz" diye indirilen seslerin lisansı çoğu zaman belirsizdir (atıf şartı, ticari kullanım yasağı, platform kısıtı). Kullanım hakkının en açık hali **sesi kendin üretmektir**. Bu dosyalar sinüs dalgaları, filtrelenmiş gürültü ve zarflardan oluşuyor. Tamamen bu projeye ait, lisans sorusu doğmuyor.

**Nasıl çalışıyor:**
* **Sentez temelleri**: bir nota = `sin(2π·f·t)` + daha zayıf harmonikler (`2f`, `3f`), ki tını "saf bip"ten daha zengin olsun. **Zarf (envelope)**: sesin zaman içindeki şiddeti. Ani başlayan/biten ses hoparlörde "tık" yapar, bu yüzden her ses kısa bir girişle başlayıp yumuşak bir çıkışla bitiyor.
* **Frekans kayması** (`pop`, `impact`): frekans dizisinin kümülatif toplamı faz olarak kullanılıyor (`sin(2π·cumsum(f)/rate)`). Böylece frekans pürüzsüzce değişiyor.
* **Deterministik**: gürültü sabit tohumlu bir üreteçten geliyor. İki kez çalıştırıldı, `upbeat.wav`'ın hash'i aynı çıktı (`172FE616...`). Sesler, kodun kendisi gibi tekrarlanabilir.
* **Boyut**: müzik dosyaları 22.05 kHz'e indirildi (`half_rate`: önce basit low-pass, sonra her ikinci örnek). Konuşmanın altındaki yumuşak pad'lerin en üst oktava ihtiyacı yok. Boyut yarıya indi (müzik toplam ~1.8 MB, efektler ~150 KB). Efektler 44.1 kHz kaldı.
* Day 11'deki TTS venv'i kullanılıyor (NumPy + soundfile zaten kurulu). Yeni bağımlılık yok.

```
assets\sfx\pop.wav  0.12s 10 kB   whoosh 0.45s 38 kB   click 0.03s 2 kB
assets\sfx\impact   0.35s 30 kB   success 0.42s 36 kB  error 0.35s 30 kB
assets\music\upbeat.wav 18.46s 795 kB    assets\music\calm.wav 24.00s 1033 kB
```

**Sınırlama (açıkça):** sesleri dinleyemiyorum. Doğrulamam sayısal (süre, seviye, tepe, konuşma/müzik oranı, bölüm 6). Kulakla kontrol kullanıcıda.

---

## 2. `Domain/MusicMood.cs` — kontrollü müzik sözlüğü

```csharp
public static class MusicMood
{
    public const string Upbeat = "upbeat";
    public const string Calm = "calm";
    public static IReadOnlyList<string> All { get; } = [Upbeat, Calm];
    public static string Normalize(string? mood) =>
        All.FirstOrDefault(known => string.Equals(known, mood?.Trim(), OrdinalIgnoreCase)) ?? Upbeat;
}
```

**Neden:** `VideoPlan.MusicMood` Day 5'ten beri serbest bir string ve hep `null` kalıyordu. Artık her değer `assets/music/<mood>.wav` dosyasına karşılık gelen kontrollü bir sözlükten. Enum yerine sabit string kullanıldı, böylece planın JSON biçimi değişmedi.

---

## 3. Planlama: müzik ruh hali + efekt sınırı (Day 15 dosyaları, değişti)

```csharp
// VisualPlanSchema: kökte zorunlu, sadece "upbeat" | "calm"
["musicMood"] = new JsonObject { ["type"] = "string", ["enum"] = [ "upbeat", "calm" ] },

// ScenePlanningService
var sfx = visual.Sfx is not null && sfxUsed < MaxSoundEffects ? visual.Sfx : null;   // en fazla 3
...
MusicMood: MusicMood.Normalize(generated.MusicMood)
```
Prompt'a eklenen kurallar: *"Use sound effects sparingly: at most 3 scenes, only where a sound underlines the moment (e.g. "error" on a common mistake, "success" on the fix)."* ve *"musicMood: upbeat for training/performance topics, calm for recovery, sleep or mobility."*

**Neden:**
* **Seçim LLM'de, sınır kodda**: hangi sahnenin efekt alacağı yaratıcı bir karar. "Aşırıya kaçmasın" kuralı ise yapısal (roadmap kabul kriteri: "efektler bunaltıcı olmamalı"). Model 7 sahnenin hepsine efekt koysa bile sadece ilk 3'ü kalıyor.
* Şema sayesinde model `"epic-orchestral"` gibi var olmayan bir ruh hali **yazamıyor**. `Normalize` el yazımı ya da eski planlar için ikinci bir emniyet.

---

## 4. Renderer: `audio/audioLibrary.ts`

```ts
export const SFX_PATHS: Record<SfxId, string> = { pop: "assets/sfx/pop.wav", ... };

export const AUDIO_MIX = { voice: 1, music: 0.06, sfx: 0.35, musicFadeInSeconds: 1, musicFadeOutSeconds: 1.5 } as const;

export function musicVolumeAt(frame: number, fps: number, durationInFrames: number): number {
  const envelope = Math.min(
    interpolate(frame, [0, fadeIn], [0, 1], clamp),
    interpolate(frame, [durationInFrames - fadeOut, durationInFrames], [1, 0], clamp),
  );
  return AUDIO_MIX.music * envelope;
}
```

**Neden / Nasıl çalışıyor:**
* **Seviye hiyerarşisi**: ses 1.0 > efekt 0.35 > müzik 0.06. Remotion'da `volume` doğrusal kazançtır (0.06 ≈ −24 dB). Müzik değeri ölçümle ayarlandı (bölüm 6).
* **Fade**: müzik 1 saniyede girip son 1.5 saniyede çıkıyor. Video sert bir müzikle başlamıyor ve ölçünün ortasında kesilmiyor. İki `interpolate`'in **minimumu** alınıyor, böylece çok kısa bir videoda bile iki fade çakışırsa ses negatif olmuyor (test edildi).
* `Record<SfxId, ...>`: şemaya yeni bir efekt id'si eklenip burada sesi unutulursa derleme hatası (Day 9/16 deseni).
* `resolveMusicPath`: `in` yerine `hasOwnProperty`. İlk yazdığımda `"toString" in MUSIC_PATHS` miras yoluyla `true` döndürüyordu. Bunu yakalayan test vardı (Day 10'da altyazı stillerinde de aynı tuzak).

---

## 5. Renderer: müzik ve efektlerin bağlanması (değişti)

```tsx
// ShortComposition.tsx
{narrationPath ? <Html5Audio src={staticFile(narrationPath)} volume={AUDIO_MIX.voice} /> : null}
<Html5Audio src={staticFile(resolveMusicPath(plan.musicMood))} loop
            volume={(frame) => musicVolumeAt(frame, fps, durationInFrames)} />

// SceneRenderer.tsx — her sahnenin Series.Sequence'ı içinde
{scene.sfx ? <Html5Audio src={staticFile(SFX_PATHS[scene.sfx])} volume={AUDIO_MIX.sfx} /> : null}
```

**Nasıl çalışıyor:**
* **`loop`**: müzik döngüleri (18–24 sn) bir Short'tan kısa. Remotion, video bitene kadar başa sarıyor. Fade-out videonun sonuna göre hesaplandığı için döngünün nerede olduğu önemli değil.
* **`volume` callback'i**: sabit bir sayı yerine her kare için çağrılan bir fonksiyon. Fade'i Remotion'ın kendi mekanizmasıyla yapıyoruz, ses dosyasını değiştirmiyoruz.
* **Efektin zamanlaması**: efekt sahnenin `Sequence`'ının içinde olduğu için sahnenin 0. karesinde, yani ait olduğu kesmede çalıyor. Ayrı bir zaman hesabı yok (Day 8'deki "zamanlamayı `<Series>` yapsın" ilkesi).

---

## 6. Ölçümle doğrulama: `tools/audio/measure_mix.py`

```python
# konuşma pencereleri:  her sahnenin [başlangıç, başlangıç + spokenDurationSeconds]
# sadece-müzik pencereleri: cümle arası duraklamalar (ilk 0.1 sn hariç) -> RMS dBFS karşılaştırması
print(f"margin {speech_db - music_db:6.1f} dB  (speech above music)")
```
```
npx remotion ffmpeg -i output/<id>/final.mp4 -ac 1 -ar 22050 mix.wav     # Remotion'ın kendi ffmpeg'i
python tools/audio/measure_mix.py mix.wav output/<id>/plan.json
```

**Neden:** "Konuşma net, efektler bunaltıcı değil" kriterini dinleyemediğim için **ölçmem** gerekiyordu. Day 12'nin zamanlama verisi (`spokenDurationSeconds`) burada ikinci bir işe yarıyor: videonun hangi anlarında konuşma olduğunu, hangi anlarında sadece müzik çaldığını tam olarak biliyoruz.

**Nasıl çalışıyor:** RMS (root mean square), bir sinyalin ortalama "gücü". dBFS'e çevrilince iki bölümün farkı, konuşmanın müzikten kaç dB yüksek olduğunu veriyor. FFmpeg makinede kurulu değil, ama Remotion kendi içinde bir ffmpeg getiriyor (`npx remotion ffmpeg`), MP4'ün sesini WAV'a çıkarmak için o kullanıldı. Yeni kurulum yok.

**Ölçüm ve ayar:**

| Müzik seviyesi | Konuşma RMS | Sadece-müzik RMS | Fark | Tepe |
|---|---|---|---|---|
| 0.10 (ilk) | −25.3 dBFS | −37.3 dBFS | 11.9 dB | −4.0 dBFS |
| **0.06** | −25.5 dBFS | −41.7 dBFS | **16.2 dB** | −4.0 dBFS |

Konuşmanın altındaki arka plan müziği için yaygın hedef 15–20 dB. İlk değer (12 dB) telefon hoparlöründe müziği fazla öne çıkarabilirdi. 0.06 ile 16.2 dB'ye inildi, müzik duraklamalarda hâlâ duyulur seviyede. Tepe −4 dBFS: kırpılma (clipping) yok.

---

## 7. Testler

* .NET: `GeneratePlanAsync_KeepsAtMostThreeSoundEffects` (7 sahnenin hepsi `pop` isteyince 3'ü kalıyor); `GeneratePlanAsync_MusicMoodIsAlwaysAKnownTrack` (4 vaka: `calm`, `CALM`, bilinmeyen, `null`); `Build_MusicMoodIsRequiredAndLimitedToTheGeneratedTracks`. `VisualPlanJson` test yardımcısı efekt ve müzik parametreleri aldı.
* Renderer `audioLibrary.test.ts` (6): müziğin sessiz başlayıp fade-in sonunda hedef seviyeye çıkması; tam son karede sıfıra inmesi; hiçbir karede hedef seviyeyi aşmaması ve ses > efekt > müzik hiyerarşisi; iki fade'den kısa bir videoda negatif olmaması; ruh hali eşlemesi + `toString` tuzağı; her efekt id'sinin üretilen dosyaya işaret etmesi.

---

## Doğrulanan davranış

```
$ dotnet test        -> Domain 57, Application 76, Infrastructure 61, Api 7  (toplam 201; Day 16'da 195)
$ cd renderer; npm test -> 32 passed;  npm run lint -> temiz
```

Gerçek uçtan uca üretim, yeni bir konuyla (`POST /api/videos {"topic":"Why Rest Days Make You Stronger"}`):
```
Completed in 57s
Job ad6737d9-...: scene plan accepted on attempt 1 (7 scenes)
Kokoro synthesized 31.01s of audio (7 segments) with voice af_heart in 11.0s
Rendered job ad6737d9-... (5668 kB) in 24.5s
musicMood: calm | sfx: scene2=whoosh
```
Model, dinlenme konusu için kendiliğinden `calm` müziğini ve tek bir efekt seçti. Mix ölçümü bölüm 6'daki tabloda.

Bu çıktılar şunu kanıtlıyor: video artık narration + arka plan müziği + kontrollü efektler içeriyor. Konuşma ölçülebilir şekilde baskın (16 dB) ve efektler hem sayıca (≤3) hem seviyece (0.35) sınırlı (roadmap kabul kriteri). Bütün sesler projenin kendi ürettiği, lisansı açık dosyalar.
