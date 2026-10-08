# Day 12 — Kod Notları

30 günlük roadmap, Gün 12. Konu: görüntüyü sese senkronlamak. Narration sahne sahne sentezleniyor, her sahnenin sesinin tam olarak nerede başladığı ölçülüyor ve sahne süreleri bu ölçüme göre, kare sınırlarına hizalı ve kaymasız (drift'siz) hesaplanıyor. Ses dosyasının süresi WAV başlığından doğrulanıyor ve render'a sesin kendisi ekleniyor.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. Problem: Day 11'in sonundaki uyumsuzluk

Day 11 sonunda gerçek ses **16.7 saniye** çıktı. Plan ise sahnelere toplam **30 saniye** veriyordu (LLM'in tahmini). Bu haliyle render edilseydi, video 13 saniye boyunca sessiz devam ederdi ve her sahnenin görüntüsü, sesteki ilgili cümleden giderek daha fazla kopardı.

Çözümün özü: **ses doğruluk kaynağıdır (source of truth)**. LLM'in tahmin ettiği süreler, narration üretildikten sonra gerçek ölçümlerle değiştiriliyor.

---

## 2. `tools/tts/kokoro_tts.py` — segment bazlı sentez (Day 11 dosyası, değişti)

```diff
- text = sys.stdin.buffer.read().decode("utf-8-sig").strip()
+ # stdin: {"segments": ["First scene's narration.", "Second scene's narration."]}
+ segments = read_segments()
...
+ parser.add_argument("--pause", type=float, default=0.3, ...)
...
+ for text in segments:
+     samples, rate = kokoro.create(text, voice=args.voice, speed=args.speed, lang=args.lang)
+     pause = np.zeros(int(round(args.pause * rate)), dtype=samples.dtype)
+     timings.append({"startSeconds": round(cursor / rate, 4),
+                     "durationSeconds": round(len(samples) / rate, 4)})
+     clips.extend([samples, pause])
+     cursor += len(samples) + len(pause)
+ audio = np.concatenate(clips)
...
  print(json.dumps({"durationSeconds": ..., "sampleRate": ..., "voice": ...,
+                   "segments": timings}))
```

**Neden:** Tek bir metin olarak sentezlenen bir sesin içinde "üçüncü sahnenin cümlesi kaçıncı saniyede başlıyor?" sorusunun cevabı yok. Kokoro kelime zaman damgası (word timestamp) vermiyor. Ama her sahnenin metnini **ayrı ayrı** sentezleyip uç uca eklersek, her parçanın başlangıcını ve süresini **örnek sayısından** tam olarak biliyoruz. Tahmin yok, ölçüm var.

**Nasıl çalışıyor:**
* `cursor`, örnek (sample) cinsinden "şu ana kadar ne kadar ses yazıldı" sayacı. Her segment için başlangıç `cursor / rate` saniye.
* Her segmentten sonra `--pause` saniyelik sessizlik (`np.zeros`) ekleniyor: sahneler arasında doğal bir nefes, sahne geçişinin gerçekleştiği an.
* Girdi artık JSON: segment listesi tek bir metinden daha yapısal. Boş/geçersiz segmentler çıkış kodu 2 ile reddediliyor. stdin kullanımı (komut satırı yerine) aynen korunuyor.

Doğrudan test:
```
{"segments":["Keep your core tight and avoid swinging.","Pull your chest towards the bar."]}
-> {"durationSeconds": 4.2693, ..., "segments": [{"startSeconds": 0.0, "durationSeconds": 2.0053},
                                                 {"startSeconds": 2.3053, "durationSeconds": 1.664}]}
```
`2.0053 + 0.3 = 2.3053`: ikinci segment tam olarak birincinin sesi + duraklama kadar sonra başlıyor.

---

## 3. `Media/WavFileReader` — FFmpeg olmadan medya metadata'sı

```csharp
public sealed record WavInfo(int SampleRate, int Channels, int BitsPerSample, long DataBytes)
{
    public double DurationSeconds => (double)DataBytes / (SampleRate * Channels * (BitsPerSample / 8));
}

public static WavInfo Read(string path)
{
    // "RIFF" <size> "WAVE"
    // sonra parçalar (chunk): [4 bayt id][4 bayt little-endian boyut][gövde]
    while (stream.Read(chunkHeader) == 8)
    {
        var id = Encoding.ASCII.GetString(chunkHeader[..4]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..8]);
        if (id == "fmt ")      { channels = ..; sampleRate = ..; bitsPerSample = ..; }
        else if (id == "data") { return new WavInfo(..., Math.Min(size, stream.Length - stream.Position)); }
        else                   { stream.Seek(size + (size % 2), SeekOrigin.Current); }   // LIST vb. atla
    }
}
```

**Neden:** Roadmap bugün "medya metadata'sını incele" diyor. FFmpeg/ffprobe kurulu değil ve sadece "bu ses kaç saniye?" sorusu için bağımlılık eklemeye değmez. WAV formatı basit: başlığı okumak, gerçek bir medya formatını byte seviyesinde anlamak için iyi bir örnek.

**Nasıl çalışıyor:**
* **Süre = veri byte'ı / (örnekleme hızı × kanal × örnek başına byte)**. 24000 Hz, mono, 16-bit için saniyede 48000 byte.
* **Little-endian**: WAV sayıları düşük byte önce yazar. `BinaryPrimitives.ReadUInt32LittleEndian` makinenin byte sırasından bağımsız doğru okur.
* **Chunk atlama**: `fmt ` ve `data` dışında `LIST` (metadata) gibi parçalar olabilir. Boyutları okunup atlanıyor. Tek sayılı boyutlu parçalardan sonra 1 byte dolgu (padding) var: `size % 2`.
* **Başlıktaki boyuta körü körüne güvenme**: dosya yarıda kesilmişse başlık "2 saniye" der ama dosyada 1.5 saniye vardır. `Math.Min(size, kalan)` gerçekte olanı döndürüyor (bunun için ayrı bir test var).
* `stackalloc` tamponları döngü dışında: derleyici uyarısı **CA2014** (döngüdeki `stackalloc` her turda stack'ten yeni yer ayırır, taşma riski). Uyarı bugün yakalandı ve düzeltildi.

---

## 4. Domain: `double` süreler, `SpokenDurationSeconds`, `VideoFormat`

```diff
 public sealed record Scene(
     ...
-    int DurationSeconds,
+    double DurationSeconds,
     ...
     string? Emphasis,
+    double? SpokenDurationSeconds = null);

 public sealed record VideoPlan(
     ...
-    int TargetDurationSeconds,
+    double TargetDurationSeconds,
```
```csharp
public static class VideoFormat { public const int Width = 1080; public const int Height = 1920; public const int Fps = 30; }
```

**Neden:**
* Gerçek ses süreleri kesirli (3.6667 saniye). `int` ile saniye altı hassasiyet kaybolurdu. LLM hâlâ tam sayı üretiyor, `5` değeri `double`'a sorunsuz parse ediliyor. Mevcut testler ve LLM sözleşmesi değişmeden çalışmaya devam etti.
* `SpokenDurationSeconds`: sahnenin başından itibaren **ne kadarının konuşma** olduğu (kalanı sonraki sahneden önceki duraklama). Altyazılar bu aralığa yayılıyor (bölüm 9). Sondaki varsayılan `= null` sayesinde LLM'den gelen JSON'da bu alan olmasa da plan geçerli.
* `VideoFormat.Fps`: backend'in zamanlama matematiği, renderer'ın kaydettiği fps ile **aynı** sayıyı kullanmak zorunda. Yoksa sahne sınırları karelerin arasına düşer. Sabitin tek bir yerde yaşaması bu bağı görünür kılıyor.

---

## 5. `SceneTimingCalculator` — saniyeden kareye, kaymadan

```csharp
public static VideoPlan Apply(VideoPlan plan, IReadOnlyList<NarrationSegmentTiming> segments,
                              double audioDurationSeconds, int fps)
{
    // sınırlar (saniye): [0, seg2.start, seg3.start, ..., sesin sonu, (+ sessiz outro)]
    var boundaries = new List<double> { 0 };
    for (var i = 1; i < segments.Count; i++) boundaries.Add(segments[i].StartSeconds);
    boundaries.Add(Math.Max(audioDurationSeconds, lastSegment.Start + lastSegment.Duration));
    foreach (var silent in scenes.Skip(narrated.Count))
        boundaries.Add(silentEnd += Math.Clamp(silent.DurationSeconds, 1.5, 4.0));

    // SINIRLARI kareye yuvarla, sonra farkları al
    var frames = boundaries.Select(s => (int)Math.Round(s * fps, MidpointRounding.AwayFromZero)).ToList();
    for (var i = 0; i < scenes.Count; i++)
        timedScenes.Add(scenes[i] with
        {
            DurationSeconds = (double)Math.Max(1, frames[i + 1] - frames[i]) / fps,
            SpokenDurationSeconds = i < segments.Count ? segments[i].DurationSeconds : null,
        });
    return plan with { Scenes = timedScenes, TargetDurationSeconds = timedScenes.Sum(s => s.DurationSeconds) };
}
```

**Neden:** Bugünün en önemli dersi. Renderer saniyeleri kareye çeviriyor (`Math.round(seconds * 30)`). Her sahnenin süresi ayrı ayrı yuvarlanırsa, her sahnede yarım kareye kadar hata oluşur ve bu hatalar **birikir**. 10 sahnelik bir videoda son sahne sesten 5 kare (~0.17 sn) kayabilir. Bu fark gözle görülür.

**Nasıl çalışıyor:**
* **Sahne sınırı = sesin başlangıcı**: sahne N, kendi cümlesi başladığı anda başlıyor ve bir sonraki cümle başlayana kadar sürüyor. Cümleden sonraki duraklama, o cümleyi söyleyen sahneye ait.
* **İlk sahne 0'dan başlıyor**: ilk cümle 0.2 saniyede başlasa bile, Hook'tan önce boş kare olmasın diye.
* **Sınırları yuvarla, süreleri değil**: Day 10'daki altyazı zamanlamasının aynı fikri, sahne seviyesinde. Her sınır, sesteki gerçek zamanına en yakın kareye oturuyor. Süreler bu karelerin farkı olduğu için toplam asla kaymıyor ve her süre tam sayı kare (`frames / 30`) oluyor. Renderer `Math.round(saniye × 30)` yaptığında tam olarak aynı kareyi geri alıyor.
* **`MidpointRounding.AwayFromZero`**: .NET'in varsayılanı "banker's rounding"dir (2.5 → 2). JavaScript'in `Math.round`'u ise 2.5'i 3'e yuvarlar. Backend ile renderer'ın aynı yuvarlamayı yapması için bu ayar gerekli.
* **Sessiz Outro**: narration'ı olmayan kapanış kartı sesten sonra geliyor. Süresi okunabilir bir aralığa sıkıştırılıyor (1.5–4 sn), çünkü LLM'in 10 saniyelik sessiz bir kapanış kartı önermesi izleyiciyi kaybettirir.
* **Koruma kontrolleri**: segment sayısı ile narration'lı sahne sayısı eşleşmezse veya sessiz bir sahne konuşmalı bir sahneden önce gelirse (ses dosyasına sessizlik eklenmesi gerekirdi) açık bir hata fırlatılıyor.

---

## 6. `ITtsProvider` sözleşmesi segment bazlı (Day 11 dosyası, değişti)

```diff
-public sealed record TtsRequest(string Text, string OutputPath, string? Voice = null);
+public sealed record TtsRequest(IReadOnlyList<string> Segments, string OutputPath, string? Voice = null);

-public sealed record TtsResult(string AudioPath, double DurationSeconds, int SampleRate, string Voice);
+public sealed record TtsResult(string AudioPath, double DurationSeconds, int SampleRate, string Voice,
+                               IReadOnlyList<NarrationSegmentTiming> Segments);
```

**Neden:** Segment bilgisi artık sözleşmenin parçası. İleride premium bir TTS servisine geçilirse, o servisin adapter'ı da segment başına zamanlama döndürmek zorunda (çoğu ticari TTS kelime/cümle zaman damgası veriyor, bu yüzden makul bir beklenti).

---

## 7. `KokoroTtsProvider` — JSON stdin, `--pause`, WAV doğrulaması (Day 11 dosyası, değişti)

```diff
-            StandardInput: text,
+            StandardInput: JsonSerializer.Serialize(new { segments }, JsonOptions),
...
+                "--pause", _options.PauseSeconds.ToString(CultureInfo.InvariantCulture),
...
-        var output = ParseOutput(result.StandardOutput);
+        var output = ParseOutput(result.StandardOutput, request.Segments.Count);
+        VerifyAudioFile(request.OutputPath, output.DurationSeconds);
```
```csharp
private static void VerifyAudioFile(string path, double reportedDurationSeconds)
{
    if (!File.Exists(path)) throw new TtsGenerationException("... reported success but '...' was not created.");
    var info = WavFileReader.Read(path);
    if (Math.Abs(info.DurationSeconds - reportedDurationSeconds) > 0.05)
        throw new TtsGenerationException($"Audio file is {info.DurationSeconds:0.000}s long but the TTS engine reported ...");
}
```

**Neden:** Python'un JSON'u ne yazmak **istediğini** söylüyor, WAV başlığı ise diskte gerçekte **ne olduğunu**. Bundan sonraki her karar (sahne uzunlukları, altyazılar, video süresi) bu süreye dayanıyor. İkisi uyuşmazsa sessizce devam etmek yerine sentez başarısız sayılıyor. Segment sayısı da kontrol ediliyor: 6 segment gönderip 5 zamanlama geri almak, zamanlamanın yanlış sahnelere dağıtılması demek olurdu.

**Nasıl çalışıyor:** `ParseOutput` artık ayrı bir `ParsedOutput` record'u döndürüyor. Gelen JSON'da `Segments` opsiyonel (`?`), ama doğrulamadan geçtikten sonra null olamaz. Tip bunu ifade ediyor ve derleyicinin null uyarısı (CS8602) bu sayede ortadan kalktı. Yeni ayar: `KokoroTts:PauseSeconds` (varsayılan 0.3).

---

## 8. `NarrationService` — zamanlanmış planı kaydetmek (Day 11 dosyası, değişti)

```diff
+        var segments = plan.Scenes.OrderBy(s => s.Order)
+            .Where(s => !string.IsNullOrWhiteSpace(s.Narration))
+            .Select(s => s.Narration!).ToList();
-            return await _ttsProvider.SynthesizeAsync(new TtsRequest(plan.NarrationText, outputPath), ct);
+            audio = await _ttsProvider.SynthesizeAsync(new TtsRequest(segments, outputPath), ct);
...
+        var timedPlan = SceneTimingCalculator.Apply(plan, audio.Segments, audio.DurationSeconds, VideoFormat.Fps);
+        await _planStore.SaveAsync(jobId, timedPlan, cancellationToken);
+        return new NarrationOutcome(audio, timedPlan);
```

**Neden:** Segmentlerin birleşimi tam olarak plan metni (`VideoPlanValidator` bunu Day 5'ten beri garanti ediyor), ama sahne sınırlarından bölünmüş halde. Zamanlanmış plan `plan.json`'ın **üzerine** yazılıyor, çünkü renderer'ın oynatacağı plan bu. Narration tekrar üretilirse (ör. ses değişti) süreler yeniden hesaplanıyor. Endpoint artık hem ses sonucunu hem zamanlanmış planı döndürüyor.

---

## 9. Renderer: schema, altyazılar ve ses

```ts
// schemas/videoPlan.ts
spokenDurationSeconds: z.number().positive().nullable().optional(),
```
```ts
// captions/captionCues.ts
const speechFrames = Math.min(durationInFrames,
  spokenFrames !== undefined && spokenFrames > 0 ? spokenFrames : durationInFrames);
...
const endFrame = index === chunks.length - 1
  ? durationInFrames                                            // son altyazı sahne sonuna kadar kalır
  : Math.round((speechFrames * cumulativeWeight) / totalWeight); // diğerleri konuşma süresine yayılır
```
```tsx
// compositions/ShortComposition.tsx
export type ShortCompositionInputProps = { planPath: string; narrationPath: string | null };
...
{narrationPath ? <Html5Audio src={staticFile(narrationPath)} /> : null}
```

**Neden / Nasıl çalışıyor:**
* **Schema**: alan `optional()`, çünkü `samples/` altındaki el yazımı planlarda yok. Eski planlar geçerliliğini koruyor.
* **Altyazılar**: Day 10'da bir sahnenin altyazıları sahnenin tamamına yayılıyordu. Artık konuşma 2 saniye, sahne 2.3 saniye ise altyazılar 2 saniyeye yayılıyor (son kelime, söylendiği anda ekranda). Son parça duraklama boyunca ekranda kalıyor, böylece sahne ortasında boş ekran olmuyor. Zamanlama verisi yoksa davranış Day 10'dakiyle birebir aynı (test edildi).
* **Ses**: tek, kesintisiz bir ses izi 0. kareden başlıyor. Sahne süreleri aynı dosyadan hesaplandığı için ekstra kaydırma (offset) gerekmiyor. `Html5Audio` kullanıldı, çünkü bu Remotion sürümünde (4.0.534) `Audio` bileşeni deprecated olmuş ve `Html5Audio` olarak yeniden adlandırılmış.
* **`renderer/public/jobs/`**: bir job'ın `plan.json` + `narration.wav` dosyaları render için oraya kopyalanıyor (Day 8: Remotion sadece `public/` altındaki dosyaları okuyabiliyor). `.gitignore`'a eklendi. Kopyalamayı Day 13'te backend otomatik yapacak, bugün elle yapıldı.

---

## 10. Testler

* `SceneTimingCalculatorTests` (9): sınırların segmentleri takip etmesi (69/54/99 kare); ilk sahnenin 0'dan başlaması; konuşma sürelerinin aktarılması; sessiz Outro'nun sesten sonra gelip 4 saniyeye sıkıştırılması; **drift testi**: sınırları kare arasına düşen 10 sahnede her sahnenin tam kendi ses karesinde başlaması; sürelerin tam kare olması; segment sayısı uyuşmazlığı; sessiz sahnenin araya girmesi; `Order`'a göre sıralama.
* `WavFileReaderTests` (4): format + kesin süre; bilinmeyen `LIST` chunk'ının atlanması; WAV olmayan dosya; kesilmiş dosyada gerçek süre. Testlerdeki WAV dosyaları `TestWav` yardımcısıyla byte byte yazılıyor, yani okuyucu kendisine karşı değil gerçek formata karşı test ediliyor.
* `KokoroTtsProviderTests` (+4, güncellendi): stdin'in JSON segment listesi olması ve tırnakların bozulmadan gitmesi (string olarak değil, **parse edilerek** karşılaştırılıyor; bkz. bölüm 11); `--pause 0.3`; WAV süresi uyuşmazlığı; başarı bildirip dosya oluşturmama; segment sayısı uyuşmazlığı; boş segment. Sahte runner artık gerçek bir WAV dosyası yazıyor.
* `NarrationServiceTests` (+1, güncellendi): sahne başına bir segment; sesten zamanlanmış planın hesaplanıp kaydedilmesi (2.3 / 2.3 / 3.0 sn).
* Renderer `captionCues.test.ts` (+2): konuşma süresine yayılma + son altyazının sahne sonuna kadar kalması; geçersiz/eksik konuşma süresinde Day 10 davranışı.

---

## 11. Hata/düzeltme: kırılgan bir test

```
Assert.Equal() Failure: Strings differ
Expected: ···"gments":["Keep your "core" tight;","avoid"···
Actual:   ···"gments":["Keep your \\u0022core\\u0022 tigh"···
```

**Düzeltme:** stdin'i string olarak karşılaştırmak yerine `JsonDocument.Parse` ile parse edip segment değerlerini karşılaştırmak.

**Neden:** `System.Text.Json` güvenlik için `"` karakterini varsayılan olarak `"` şeklinde kaçışlıyor. Bu geçerli bir JSON ve Python aynı metni geri okuyor. Test, sözleşmeyi (Python'un okuyacağı metin) değil bir uygulama detayını (kaçış biçimi) kontrol ediyordu.

**Nasıl çalışıyor:** Parse edilmiş karşılaştırma, serializer ayarı değişse bile yalnızca anlam değişirse bozuluyor. İyi bir testin amacı da bu.

---

## Doğrulanan davranış

```
$ dotnet test
Başarılı! - Başarısız: 0, Başarılı: 47 - ShortsGenerator.Domain.Tests.dll
Başarılı! - Başarısız: 0, Başarılı: 44 - ShortsGenerator.Application.Tests.dll
Başarılı! - Başarısız: 0, Başarılı: 42 - ShortsGenerator.Infrastructure.Tests.dll
Başarılı! - Başarısız: 0, Başarılı:  1 - ShortsGenerator.Api.Tests.dll
(toplam 134; Day 11 sonunda 116)

$ cd renderer; npm test     ->  Tests 23 passed (23)
$ npm run lint              ->  eslint + tsc temiz
```

Gerçek API, Day 11'deki job (`POST /api/videos/64fe09b8-.../narration`, Ollama kullanılmadı, 7 saniye sürdü):
```
audio=16.904s; target=16.9s
segments:                          scenes (zamanlanmış):
  start=0        dur=3.3707          scene-1  Hook              dur=3.6667  spoken=3.3707
  start=3.6707   dur=2.0053          scene-2  ExerciseDemo      dur=2.3     spoken=2.0053
  start=5.976    dur=2.688           scene-3  ExerciseDemo      dur=3       spoken=2.688
  start=8.964    dur=2.7947          scene-4  AnatomyHighlight  dur=3.1     spoken=2.7947
  start=12.0587  dur=2.2187          scene-5  Progress          dur=2.5     spoken=2.2187
  start=14.5773  dur=2.0267          scene-6  Outro             dur=2.3333  spoken=2.0267
```
Her sahne, cümlesinin başladığı karede başlıyor: 3.6707 sn × 30 = 110.1 → kare 110 = 3.6667 sn. Toplam 507 kare = 16.9 sn = sesin süresi. LLM'in tahmin ettiği 30 saniyelik plan, sesin gerçek 16.9 saniyesine indi.

Sesli render (job dosyaları `renderer/public/jobs/<id>/` altına kopyalanarak):
```
npx remotion render ShortComposition out/day12-synced.mp4 --props='{"planPath":"jobs/<id>/plan.json","narrationPath":"jobs/<id>/narration.wav"}'
Encoded 507/507   out/day12-synced.mp4 1.9 MB
getVideoMetadata -> {"fps":30,"width":1080,"height":1920,"durationInSeconds":16.917,"codec":"h264","audioCodec":"aac",...}
```
Video süresi (16.917 sn) ses süresiyle (16.904 sn) örtüşüyor, aradaki 13 ms AAC kodlayıcısının çerçeve dolgusu. 175. kare (sahne 2'nin konuşma sonrası duraklaması) görsel olarak kontrol edildi: son altyazı parçası (`SWINGING.`) ekranda, boşluk yok.

Bu çıktılar şunu kanıtlıyor: görsel zaman çizelgesinin uzunluğu narration ile uyuşuyor (roadmap kabul kriteri), sahne sınırları kaymıyor (birim testleri + gerçek ölçüm) ve aynı ses her zaman aynı kare sınırlarını üretiyor (deterministik).

**Not (sonraki günler için):** örnek metin (58 kelime) sadece 16.9 saniye sürdü. `af_heart` sesi saniyede ~3.5 kelime okuyor, yani `ScriptValidator`'ın 40–130 kelime aralığı ~11–37 saniyeye denk geliyor. `CLAUDE.md`'nin 25–45 saniye hedefi için script uzunluğu Day 15/29'da gerçek üretimlerle birlikte ayarlanacak.
