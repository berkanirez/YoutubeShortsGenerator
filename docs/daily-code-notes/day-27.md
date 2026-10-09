# Day 27 — Kod Notları

30 günlük roadmap, Gün 27. Konu: performans geçişi. Kural: **önce ölç**. Bir videonun her aşaması ayrı ayrı ölçüldü, en büyük kalemlere bakıldı, her değişiklik tekrar ölçüldü. Bir hipotez ölçümle çürütülüp geri alındı, bir paralelleştirme fikri de maliyet/fayda yüzünden bilinçli olarak yapılmadı. Kalite değişmedi: aynı model, aynı ses, aynı çözünürlük ve aynı render ayarları.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 0. Taban çizgi (gerçek video, "How to Breathe During Squats")

```
Script (LLM)   10.7s   = Ollama 8.6s (model YÜKLEME 6.6s, cevap 1.9s) + ~2s açıklanamayan
Plan (LLM)     11.7s   = 514 token cevap 11.0s
Ses (Kokoro)   10.4s   (~2s model yükleme)
Render         24.0s   (asset senkronu 0.1s, bundle 1.8s, kareler + encode ~22s)
TOPLAM         57.6s
```

Ölçüm araçları (tekrar kullanılabilir): `tools/perf/measure-render.ps1` (render'ın alt adımları ve concurrency) ve `tools/perf/measure-planning.ps1` (gerçek konularda plan deneme sayısı ve süresi). Ayrıca kalıcı teşhis bilgisi: Ollama'nın kendi zamanlaması artık her çağrıda loglanıyor (bölüm 1).

---

## 1. Ollama'nın kendi zamanlamasını loglamak (`OllamaLlmProvider`)

```csharp
internal sealed record OllamaGenerateResponse(string Response,
    [property: JsonPropertyName("total_duration")] long TotalDuration = 0,  // nanosaniye
    [property: JsonPropertyName("load_duration")] long LoadDuration = 0, ... prompt_eval_*, eval_*);

_logger.LogInformation("Ollama {Model}: {Total:0.0}s total (load {Load:0.0}s, prompt {PromptTokens} tokens in {Prompt:0.0}s, " +
                       "answer {AnswerTokens} tokens in {Answer:0.0}s = {Rate:0.0} tokens/s)", ...);
```

**Neden:** "LLM 11 saniye sürdü" bilgisi tek başına işe yaramaz. Süre modelin belleğe yüklenmesinde mi, prompt'un okunmasında mı, cevabın yazılmasında mı geçti? Ollama bu üçünü zaten her cevapta veriyor; okumak bedava. Taban çizgideki **6.6 sn'lik model yükleme** bu satır sayesinde görüldü. Satır `job.log`'a da düşüyor (Day 26).

---

## 2. Ölçüm: render'ın içi (`tools/perf/measure-render.ps1`)

```
asset sync (sync-public-assets.mjs)               0,1s
render from source (bundle + render, Day 26)     23,7s
bundle only (remotion bundle)                     1,8s
render from pre-built bundle                     22,5s
render from bundle, --concurrency 2              33,9s
render from bundle, --concurrency 4              26,1s
render from bundle, --concurrency 6              22,7s   <- Remotion varsayılanı (12 mantıksal çekirdeğin yarısı)
render from bundle, --concurrency 8              22,1s
render from bundle, --concurrency 12             21,6s
x264 preset medium / veryfast                    22,4s / 21,8s (iki tur, aynı sonuç)
```

**Sonuç:** İlk tahminim (her render'da yeniden bundle almak) **yanlıştı**: webpack önbelleği `node_modules/.cache`'te kalıcı, sıcak bundle 1.8 sn. Encode ayarı 0.7 sn fark ediyor (Remotion kareleri çizerken aynı anda encode ediyor; stitch sadece 0.56 sn). Zamanın neredeyse tamamı Chrome'un kareleri çizmesinde (saniyede yaklaşık 42 kare), ve 6 paralel işin üstünde kazanç %5'ten az. Render tarafında kaliteyi bozmadan kesilecek büyük bir parça yok.

---

## 3. Bilinçli olarak yapılmayan: sesi planla paralel üretmek

Kokoro'nun konuşacağı cümleler, LLM planından **önce** kodda belirleniyor (Day 15, `NarrationSegmenter`). Yani ses, planın görsel seçimlerini beklemek zorunda değil. Ölçüm:
```
LLM alone: 9,2s (500 tokens)
TTS alone: 10,4s
together: wall 14,3s (LLM 11,3s, TTS 13,8s)     -> 19.6s yerine 14.3s, ~5s (~%9) kazanç
```
**Karar: yapılmadı.** İkisi de CPU kullandığı için aynı anda çalışınca ikisi de yavaşlıyor. Gerçek kazanç videonun %9'u. Karşılığında durum makinesinin (Planning → Narrating) ve Day 23'ün yeniden seslendirme akışının yeniden yapılandırılması gerekiyor. "Önce ölç" kuralı bazen "yapma" demek de olabilir.

---

## 4. Değişiklik 1: Ollama'ya `localhost` değil `127.0.0.1` (`OllamaOptions.BaseUrl`, `appsettings.json`)

Script aşaması 4.5 sn sürüyordu ama Ollama'nın kendi ölçümü 2.4 sn'ydi. Plan aşamasında böyle bir fark yoktu.
```
netstat:   TCP 127.0.0.1:11434  LISTENING              <- Ollama yalnızca IPv4 dinliyor
curl:      localhost  connect 0.21s | 127.0.0.1 connect 0.001s
```
**Neden (kök sebep):** `localhost` hem `::1` (IPv6) hem `127.0.0.1` (IPv4) adresine çözülüyor. .NET, Windows'ta adresleri **sırayla** deniyor ve önce `::1`'i deniyor. Windows reddedilen bir bağlantıyı vazgeçmeden önce yaklaşık 2 sn tekrar deniyor. curl "happy eyeballs" ile 200 ms sonra IPv4'e geçtiği için onda kayıp daha küçük. Her işin ilk çağrısı yeni bir bağlantı açtığı için her videonun başında **~2 sn** gidiyordu (aynı işin plan çağrısı bağlantıyı yeniden kullandığı için onda fark yoktu). Day 26'daki "bağlantı reddi" denemesinin 2 değil 4.4 sn sürmesi de aynı sebepten.

---

## 5. Değişiklik 2: `keep_alive` (`OllamaOptions.KeepAlive = "15m"`)

```csharp
internal sealed record OllamaGenerateRequest(..., [property: JsonPropertyName("keep_alive")] string? KeepAlive = null);
```

**Neden:** Ollama modeli bir çağrıdan sonra varsayılan olarak 5 dk bellekte tutuyor. Videolar 5 dakikadan uzun aralıklarla yapılınca her video yeniden 6.6 sn yükleme ödüyordu. 15 dakika, normal bir çalışma oturumunu kapsıyor. Bedeli: model (~2.3 GB) bu sürede RAM'de kalıyor (16 GB'lık makinede kabul edilebilir). `"0"` her çağrıdan sonra boşaltıyor. Doğrulama (Ollama'nın kendi API'si, `/api/ps`):
```
11:30:49 son çağrı  ->  llama3.2:3b expires_at 11:45:49   (varsayılanla 11:35:49 olurdu)
```

---

## 6. Değişiklik 3: değişmemiş re-render'ı atlamak (`RemotionRenderer.FingerprintAsync`, `final.inputs.sha256`)

```csharp
// plan.json + narration.wav + renderer/src/** + renderer/package-lock.json + assets/**  -> SHA-256
if (fingerprint matches final.inputs.sha256 && final.mp4 exists) { touch final.mp4; return new RenderResult(..., Skipped: true); }
...
File.Move(temporaryOutput, request.OutputPath); File.WriteAllText(fingerprintPath, fingerprint);   // yalnızca başarıdan sonra
```

**Neden:** Roadmap açıkça istiyor ("skip unchanged stages on rerender"). Bir render 24 sn tam CPU demek. Düzenleyip geri alınan bir sahne, Retry, ya da yanlışlıkla iki kez basılan bir düğme bu maliyeti boşa ödüyordu.

**Nasıl çalışıyor:**
* **Parmak izi** (fingerprint): videonun piksellerini ve sesini belirleyen her şeyin içerik hash'i. Tarih yerine içerik kullanılıyor, çünkü dosya tarihleri güvenilmez: Day 22'nin "güncel değil" kontrolü planı kaydetmenin tarihi güncellediğini zaten gösterdi. Bir byte değişirse hash değişiyor ve render yapılıyor. **Önbellek geçersizleştirme** (cache invalidation) burada en kritik nokta: renderer kodu veya bir asset çizimi değişirse aynı plan bile yeniden render edilmeli (test edildi). Dosya isimleri de hash'e giriyor, böylece bir dosyanın yeniden adlandırılması da fark ediliyor.
* Hash yalnızca **başarılı** bir render'dan sonra yazılıyor. Başarısız bir render eski videoyu ve eski hash'i bozmadan bırakıyor, ikisi tutarlı kalıyor.
* Atlanınca `final.mp4`'ün tarihi güncelleniyor; yoksa dashboard'un "plan videodan yeni" kontrolü gereksiz yere "güncel değil" uyarısı gösterirdi.
* `Remotion:SkipUnchangedRenders=false` ile kapatılabiliyor.

Ölçüm (gerçek): `re-render #1: 24,2s` (parmak izi yoktu, oluştu) → `re-render #2: 0,5s` (`... unchanged since the last render; reusing ...`).

---

## 7. Değişiklik 4: `Remotion:Concurrency` (isteğe bağlı)

Varsayılan `null`, yani Remotion'ın kendi seçimi (6). Bölüm 2'deki tablo, hızdan çok **gürültü** için bir düğme olduğunu gösteriyor: 4 değeri %15 daha yavaş ama fan daha az çalışıyor. (Sen Day 9 civarında bilgisayarın çok ses çıkardığını söylemiştin; bu ayar o takası senin seçimine bırakıyor.)

---

## 8. Plan denemeleri: bir hipotez çürüdü, kök sebep başka çıktı (`ScenePlanningService.LoadEarlierPlansAsync`)

Bir gerçek video (A) 79.4 sn sürdü; plan 3 denemede kabul edildi (3 × 12 sn). Reddetme nedeni Day 19'un çeşitlilik kuralıydı: "2–4. sahneler önceki bir videoyla aynı sırada". `measure-planning.ps1` ile 6'şar gerçek konu:

| Ölçüm | Ortalama deneme | Ortalama planlama |
|---|---|---|
| Taban çizgi | 1.50 | 18.5 sn |
| **Hipotez**: kaçınılacak dizileri prompt'a önceden yaz | **2.83** | 38.2 sn |
| Kontrol (hipotez geri alındı, geçmiş +12 plan) | 2.00 | 28.2 sn |
| **Düzeltme**: yalnızca tamamlanmış videolarla karşılaştır | **1.00** | **12.8 sn** |

**Hipotez çürüdü:** uzun bir "bunlardan kaçın" listesi küçük modeli (3B) şaşırttı. Liste diğer kuralları da bozdu (aynı tip/asset art arda) ve prompt'u uzattı. Değişiklik geri alındı.

**Asıl kök sebep:** kontrol ölçümü, geçmiş büyüdükçe deneme sayısının arttığını gösterdi. Planlar **son 20 işin** planlarıyla karşılaştırılıyordu, ve bunlara hiç video olmamış taslaklar da dahildi (Day 19'un 10 deneme planı, bugünkü ölçüm işleri, terk edilmiş işler). Çeşitlilik kuralının amacı kanalda **yayınlanan** videoların birbirine benzememesi:

```diff
-foreach (var job in await _repository.ListRecentAsync(EarlierPlansToCompare, cancellationToken))
+var recent = await _repository.ListRecentAsync(EarlierPlansToCompare * 5, cancellationToken);
+foreach (var job in recent.Where(j => j.Status is VideoJobStatus.Completed).Take(EarlierPlansToCompare))
```

Kural gevşetilmedi: tamamlanmış son 20 video hâlâ karşılaştırılıyor. Sadece görünmeyen taslaklar artık engel değil. Yeni test: `GeneratePlanAsync_ADraftThatWasNeverMadeIntoAVideo_IsNotAnEarlierVideo`. Eski bir test (`TakeawayRepeatingAnEarlierVideo_IsFlagged`) önceki işi tamamlanmamış bir taslak olarak kuruyordu ve bu değişiklikle kırmızıya döndü. Test, önceki işi gerçekten tamamlanmış hale getirecek şekilde güncellendi.

---

## 9. Ölçüm sırasında bulunan: bilgisayar uyku moduna girdi

İlk "sonra" ölçümü (B videosu) 40 dakika sürdü. Windows olay günlüğü (Kernel-Power):
```
11:35:27  Sistem modern bekleme konumuna giriyor
12:12:24  Sistem modern bekleme konumundan çıkıyor
```
Önceki oturumda başlatılan "uyanık tut" süreci oturum kapanınca durmuştu. Uyku, Ollama'nın 15 dakikalık sayacını da doldurdu (model boşaldı). Ölçüm geçersiz sayıldı, süreç yeniden başlatıldı ve ölçüm tekrarlandı (bölüm 10).

---

## Doğrulanan davranış

```
$ dotnet test   -> Domain 116, Application 129, Infrastructure 116, Api 26   (toplam 387; Day 26: 377)
```
Yeni testler: keep_alive gönderiliyor (1), render atlama (aynı girdi → süreç yok; plan, ses, renderer kodu veya asset değişince yeniden render (4); video silinmişse render; ayar kapalıysa hep render; concurrency argümanı), taslaklar önceki video sayılmıyor (1).

Gerçek "sonra" ölçümü: **aynı konu** ("How to Breathe During Squats"), son Ollama çağrısından **7 dakika** sonra (Ollama varsayılanında model 5. dakikada boşalırdı), bilgisayar uyanık:
```
Ollama models loaded at start (12:20): llama3.2:3b until 12:27:48       <- keep_alive çalışıyor
09:20:03.828Z running stage Script
09:20:06.248Z Ollama llama3.2:3b: 2.4s total (load 0.0s, ... answer 107 tokens in 1.9s)
09:20:17.975Z Ollama llama3.2:3b: 11.7s total (load 0.0s, prompt 1407 tokens in 0.6s, answer 512 tokens in 11.0s)
09:20:17.977Z scene plan accepted on attempt 1 (7 scenes, 0 review warnings)
09:20:28.414Z Kokoro synthesized 30.96s of audio (7 segments) with voice af_heart in 10.4s
09:20:51.799Z Rendered ... (4927 kB) in 23.3s
video C: 48,0s -> Completed
```

| Aşama | Önce | Sonra | Neden |
|---|---|---|---|
| Script | 10.7 sn | **2.4 sn** | model zaten yüklü (keep_alive) + bağlantıda 2 sn IPv6 kaybı yok |
| Plan | 11.7 sn (1 deneme) | 11.7 sn | aynı; taslaklarla karşılaştırma kalktığı için tekrar deneme olasılığı düştü (1.5–2.0 → 1.0 deneme) |
| Ses | 10.4 sn | 10.4 sn | değişmedi (ölçüldü, kazanç küçük) |
| Render | 24.0 sn | 23.3 sn | ilk render aynı; **değişmemiş re-render 24.2 → 0.5 sn** |
| **Toplam** | **57.6 sn** | **48.0 sn** | **−%17** (tek deneme). Taslaklar yüzünden 3 deneme süren bir planda (A videosu, 79.4 sn) fark ~36 sn daha |

Kalite aynı: model, ses, çözünürlük ve render ayarlarının hiçbiri değişmedi; çeşitlilik kuralı yayınlanan videolar arasında hâlâ geçerli.

Roadmap kabul kriteri ("record baseline and improved generation/render timing") karşılandı: taban çizgi ve iyileşmiş süreler, ölçüm araçları ve gerçek loglarla kaydedildi.

## Bilinçli olarak bırakılanlar
* Plan (11.7 sn) ve render (23 sn) artık sürenin %73'ü, ve ikisi de işin kendisi: modelin 500 token yazması ve Chrome'un 900 kare çizmesi. Daha hızlısı daha küçük/hızlı bir model ya da GPU demek. Bu bir donanım/kalite kararı, Phase 2 adayı.
* Ses ile planın paralel üretilmesi (~5 sn) bölüm 3'teki gerekçeyle yapılmadı.
