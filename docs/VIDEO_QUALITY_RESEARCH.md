# Video kalitesi araştırması - neden yetersiz, nasıl düzelir

Tarih: 2026-10-11. Durum: **araştırma + öneri, henüz uygulanmadı.**

Karşılaştırma görselleri (yalnızca yerelde, repoya girmez): `output/research/`
- `ours-5-videos.png` - bizim son 5 videomuzdan saniye 1-7-13-19-25 kareleri
- `ref-captainworkout-*.png` - Captain Workout'un 4 videosu, saniyede bir kare
- `ref-yellowdude-frames.png` - Yellow Dude'un 12 videosundan kareler

Not: Videoların kendisini izleyemedim. YouTube'un her video için tuttuğu kare önizlemelerini
(saniyede 1 kare) ve kapak karelerini inceledim. Bu, görsel akışı görmek için yeterli; ses, müzik ve
anlatım ritmi hakkındaki çıkarımlar ise tahmin.

---

## 1. Kısa teşhis

Sorun tek bir ayar değil, **videoyu üretme sırası**. Bizim sistem önce metni yazıyor, metni sahnelere
bölüyor, sonra her sahneye "uygun bir resim" seçiyor. Yani görsel, metnin süsü oluyor: maskot ekranın
ortasında, düz önden, sahne boyunca aynı pozda duruyor; ne yapılması gerektiğini yazılar ve ses
anlatıyor. Sessiz izleyen biri hiçbir şey öğrenmiyor.

Referans kanallar tersini yapıyor: her vuruş (beat) önce bir **görsel fikir**: yanlış ile doğru yan
yana, hareketin yolunu gösteren ok, kasın yanması, ağlayan karakter, dönen çark. Metin ve ses bu
görüntüye eşlik ediyor. Bu yüzden ses kapalıyken de anlaşılıyorlar.

Çözüm: üretim sırasını **görsel-önce** yapmak ("storyboard" mantığı), buna yetecek bir **çekim
(shot) kütüphanesi** kurmak ve bu çekimlerin ihtiyaç duyduğu **görsel türlerini** (özellikle yanlış
form çizimleri ve hareket kareleri) kütüphaneye eklemek. Ayrıntılar aşağıda.

---

## 2. Referans kanallarda gördüklerim

### Captain Workout (4 video, saniye saniye)

| Teknik | Nerede, ne zaman | Ne işe yarıyor |
|---|---|---|
| **Hareketin ortasında başlama** | Her videonun 0. saniyesi: hareket bulanıklığıyla (motion blur) karakterin göğsüne/yüzüne hızlı yakınlaşma | İlk karede hareket var: göz durur |
| **Dev kelime karakterin arkasında** | "STOP", "9 MOVES", "3 SUPERSETS", "VOLUME", "CABLE CRUNCHES": soluk, ekran genişliğinde, karakterin arkasında | Konunun tek kelimesi; okumadan bile görülüyor |
| **Jestle hook** | "STOP" kelimesi + karakter kollarını X yapmış (1. sn) | Ses olmadan "dur, yanlış yapıyorsun" mesajı |
| **Kamera açısı değişimi** | 1-3 saniyede bir: aşırı yakın (gövde ekranı dolduruyor) → bel üstü → tüm vücut → uzak ve küçük → arkadan | Aynı karakter, sürekli yeni bir görüntü |
| **Karakter hareketi yapıyor** | Kablo crunch, leg raise, plank, curl: karakter gerçekten egzersiz pozunda, yandan | Ne yapılacağı görülüyor |
| **Hareket yolu çizgisi** | Curl'de barın izlediği yolu gösteren kesikli kırmızı yay (14-17. sn) | Hareketin doğrusu görülüyor |
| **Çalışan kas parlıyor** | Egzersiz sırasında hedef kas yeşil/kırmızı parlıyor | "Bu kası çalıştırır" mesajı sessizce |
| **Büyüteç (inset)** | Bileğin üstünde daire içinde yakın plan (8-10. sn) | Detayı (tutuş) gösteriyor |
| **Etiketler ve göstergeler** | "Abs activation" dolan/boşalan çubuk; "Tension" / "Stress" etiketleri; "Normal vs Valgus" iki kol yan yana, yeşil/kırmızı çizgiler | Oyunlaştırılmış bilgi, tek bakışta |
| **Düşünce balonları** | Karakterin başının üstünde tek tek beliren seçenekler (Full body / Split / PPL) | Seyircinin kafasındaki soru görselleşiyor |
| **Hayalet karakter** | Arkada yarı saydam ikinci karakter (ideal hâl / yapılan hareket) | Derinlik, karşılaştırma |
| **Komik abartı** | Plank yaparken arkasında alevler, gözünden yaşlar (11-12. sn) | Duygu: izleyiciyi güldürüp tutuyor |
| **Bilgi grafiği** | Dönen çark (program seçimi), haftalık kartlar tek tek doluyor, akış şeması | "Etkileşimli" his |
| **Kesme / ara kart** | Boş ekranda tek cümle (ör. "düzelteyim") | Ritim kırıcı (pattern interrupt) |
| **Ödül anı** | Spot ışığı / ışın patlaması önünde poz (sona doğru) | Duygusal kapanış |
| **Kapanış** | Yakın planda izleyiciyi işaret etme, ardından listeler | Doğrudan hitap + harekete çağrı |
| **Sıralama kaydırması** | Vücut yağ oranı videosu: yan yana dizilmiş karakterler, kamera soldan sağa kayıyor | "Sen hangisisin?" sorusunu görselle soruyor |

Ortak özellikler: tek renkli soluk arka plan, tek karakter, tek vurgu rengi; alt yazılar küçük,
ekranın ortasında; görüntü 1-2 saniyede bir değişiyor.

### Yellow Dude (12 videonun kareleri)

| Teknik | Örnek | Ne işe yarıyor |
|---|---|---|
| **Bölünmüş ekran: yanlış / doğru** | Üstte yanlış şınav (karakter **gri, soluk**, ✗), altta doğru şınav (karakter renkli, ✓), oklarla | Sessiz anlaşılan en güçlü kalıp |
| **Renk = doğru/yanlış** | Yanlış yapan karakter griye boyanmış, doğru yapan renkli | Etiket okumadan anlaşılıyor |
| **Yan yana: DO THIS / DON'T DO THIS** | Dikey çizgiyle ikiye bölünmüş ekran | Karşılaştırma |
| **Yıldız puanı** | Aynı hareketin iki hâli: 1 yıldız vs 3 yıldız | Kalite farkı tek bakışta |
| **Sayaçlar** | "REPS 15" kutusu, iki kronometre yan yana "00:00" | Ölçülebilirlik, oyun hissi |
| **Ağrı işareti** | Yanlış dips'te omuz kırmızı yanıyor, titreme çizgileri | "Burası sakatlanır" mesajı |
| **Komik çöküş** | Plank'ta yere yapışmış karakter, darbe çizgileri, 60 sn sayacı | Abartı + mizah |
| **Hayat sahnesi** | Masada telefon ve laptopla karakter ("21 dakika") | Hikâye bağlamı |
| **Çizgi roman kareleri** | İki çerçeve içinde yandan iki plank | Önce/sonra |
| **Mini figür ızgarası** | "Bunları yapma" (gri, x100) / "Bunları yap" (renkli) | Çok bilgiyi tek ekranda |
| **Kamera açısı** | Alttan barfiks, aşırı yakın yüz | Çeşitlilik |

Ortak: pastel tek renk arka plan (mor), tek kahraman rengi, üstte kısa italik başlık, büyük ve net
semboller (✗ ✓ ok yıldız sayaç).

---

## 3. Bizim videolarla fark

| | Referanslar | Bizim videolar |
|---|---|---|
| Görüntü değişimi | 1-2 saniyede bir | 4-5 saniyede bir (sahne başına tek görsel) |
| Kamera | Yakın / orta / uzak / arkadan / alttan | Hep aynı: tüm vücut, önden, ortada |
| Karakter | Hareketi yapıyor, yandan, farklı açılarda | Çoğunlukla önden dik duran poz |
| Yanlış vs doğru | Aynı ekranda iki hâl (gri ✗ / renkli ✓) | Tek görsel + kelime + işaret; "yanlış" çizimimiz yok |
| Hareket bilgisi | Ok, yol çizgisi, parlayan kas, büyüteç | Yok (sadece iki pozu çevirme) |
| Duygu / mizah | Ağlama, alev, çöküş, ter | Yok |
| Hook | Hareketle açılış + jest + dev kelime | Maskot + soru işareti + yazı; ilk kare durgun |
| Sessiz izlenince | Anlaşılıyor | Anlaşılmıyor |

---

## 4. Araştırma bulguları (dış kaynaklar)

Uyarı: YouTube resmî eşik yayınlamıyor; aşağıdakilerin çoğu araç sağlayıcılarının blog verisi. Yön
gösterir, kesin değildir.

- **İlk 2-3 saniye belirleyici.** Kaynaklar ilk 3 saniyede kaydırıp geçme (swipe-away) oranının
  %15-25'in altında kalmasını "sağlıklı" sayıyor. Paddy Galloway'e atfedilen 3,3 milyar Shorts
  analizinde en iyi performansın "izledi / kaydırdı" oranı %70-90 olan videolarda olduğu söyleniyor
  (asıl çalışmayı doğrulayamadım).
  ([growcreator](https://growcreator.pro/blog/youtube-shorts-retention-rate-benchmark),
  [aibrify](https://aibrify.com/blog/youtube-shorts-retention-curve-playbook),
  [opus.pro](https://www.opus.pro/blog/ideal-youtube-shorts-length-format-retention))
- **YouTube Studio'da ölçülüyor.** Bu oran Studio'da "Stayed to watch" (izlemeye devam etti) olarak
  geçiyor; dağıtımın ilk filtresi olduğu düşünülüyor. Tekrar izlemeler (loop) güçlü sinyal sayılıyor;
  ortalama izlenme oranı %100'ü geçebiliyor.
  ([creatoressentials](https://www.creatoressentials.com/glossary/stayed-to-watch/),
  [subscribr](https://subscribr.ai/p/youtube-shorts-analytics-metrics-growth),
  [coschedule](https://coschedule.com/social-media-marketing/youtube-metrics),
  [truefuturemedia](https://www.truefuturemedia.com/articles/youtube-shorts-algorithm-data-backed-guide))
- **Ses kapalı izleme yaygın.** Verizon Media anketinde ABD'li kullanıcıların %69'u kamusal alanda
  videoyu sessiz izliyor; Instagram yöneticisine göre Reels'in yaklaşık yarısı sessiz izleniyor.
  Senin "sesi duymasa bile anlamalı" kuralın doğru bir hedef.
  ([digiday](https://digiday.com/sponsored/75-percent-of-people-watch-mobile-videos-on-mute/),
  [voxbooster](https://voxbooster.com/blog/short-form-video-caption-usage-statistics-2026))
- **Seri üretim riski.** YouTube Temmuz 2025'te "tekrarlayan içerik" politikasını "özgün olmayan
  (inauthentic) içerik" olarak netleştirdi: kalıp tabanlı, tekrarlayan, toplu üretilmiş videolar para
  kazanamıyor; yapay zekâ destekli ama özgün ve değer katan içerik sorun değil. Videoların birbirine
  benzemesi sadece izleyici sorunu değil, gelir riski de.
  ([creatorhandbook](https://www.creatorhandbook.net/youtube-updates-monetization-policy-for-inauthentic-content/),
  [betanews](https://betanews.com/2025/07/10/youtube-is-fighting-ai-slop-with-new-monetization-guidelines/))

---

## 5. Kök nedenler (kodda)

1. **Metin-önce üretim.** `ScriptGenerationService` düz metin yazıyor, `NarrationSegmenter` onu
   bölüyor, `ScenePlanningService` her parçaya sahne tipi + görsel seçiyor. Görsel fikri hiçbir
   aşamada yok.
2. **Tek yerleşim.** Bütün şablonlar aynı düzeni kullanıyor: üstte duvar yazısı, ortada tam boy tek
   görsel. Kamera yok, bölünmüş ekran yok, ikinci karakter yok.
3. **Görsel kütüphanede eksik türler.** 123 görselin hepsi "doğru" form ve çoğu önden tam boy.
   **Yanlış form çizimi yok**, yakın plan yok, yandan/arkadan/alttan maskot yok, abartılı tepki
   (ağlama, çöküş) yok, hayat sahnesi yok. Bu türler olmadan sessiz anlatım imkânsız.
4. **Model kapasitesi.** llama3.2:3b senaryo yazıyor ama tutarlı bir görsel senaryo (hangi çekim,
   hangi yanlış, hangi kas) kurmakta zorlanıyor. Kahve videosunda üç mit sahnesi arka arkaya ve aynı
   yazıyla gelmesi bunun belirtisi.
5. **Hook kuralı yumuşak.** Kod ilk cümleyi denetliyor ama model başaramazsa yine de kabul ediyor;
   ilk saniyenin görüntüsü ise hiç tasarlanmıyor.

---

## 6. Önerilen çözüm

### A. Görsel-önce üretim (storyboard)

Model artık önce metin değil, **vuruş listesi** üretir. Her vuruş = bir çekim tipi + doldurulacak
alanlar + o vuruşun tek cümlelik anlatımı. Çekim tipleri ve alanları kontrollü listeden seçilir
(bugünkü güvenlik kuralı aynen kalır: model dosya adı ya da serbest talimat üretemez).

Örnek - "3 Push-Up Mistakes":
```
1 HOOK_MISTAKE     exercise=pushup mistake=hips-sag   "Your push-ups are hurting your back."
2 KINETIC_WORD     word="3 MISTAKES"                   "Here are three you probably make."
3 SPLIT_FIX        exercise=pushup mistake=hips-sag    "Number three: sagging hips. Squeeze your glutes."
4 ZOOM_DETAIL      exercise=pushup focus=elbows         "Number two: elbows flared out wide."
5 SPLIT_FIX        exercise=pushup mistake=elbows-flare "Tuck them to forty-five degrees."
6 REACTION         pose=exhausted effect=sweat          "Number one surprises everyone."
7 REP_DEMO         exercise=pushup tempo=slow muscle=chest "Half reps. Go all the way down."
8 PAYOFF           effect=spotlight pose=flex           "Fix these and your chest will grow."
9 CTA_LOOP         pose=pointing                        "Which one were you doing?"
```
Bir video, kod tarafında tanımlı **formatlardan** birine göre kurulur; model sadece boşlukları
doldurur:
- Hata geri sayımı
- Seviyeler
- Vücudun içinde
- Mit
- Yolculuk

Format, vuruşların sırasını ve hangi çekimlerin zorunlu olduğunu belirler. Böylece küçük model de
tutarlı video çıkarır. Aynı format her seferinde farklı çekim kombinasyonlarıyla doldurulur.

### B. Çekim kütüphanesi (Remotion şablonları)

Hepsi bizim görsellerimizle ve kendi tarzımızda; referansların karakterini, esprilerini ya da
cümlelerini kopyalamadan, sadece genel görsel dili (✗/✓, ok, bölünmüş ekran) kullanarak.

| Çekim | Ne gösterir | Sessiz anlaşılır mı |
|---|---|---|
| `SPLIT_FIX` | Ekran ikiye bölünür: yanlış form (gri, ✗, kırmızı ağrı noktası) / doğru form (renkli, ✓, yeşil kas) | Evet, en güçlüsü |
| `REP_DEMO` | Karakter hareketi 4 karelik döngüyle yapar, hareket yolu oku, hedef kas parlar, tekrar sayacı | Evet |
| `ZOOM_DETAIL` | Aynı çizimden yakın plan + büyüteç dairesi (dirsek açısı, tutuş, diz) | Evet |
| `HOOK_MISTAKE` | Hızlı yakınlaşmayla açılış, yanlış form + dev "STOP" + kırmızı ✗ | Evet |
| `KINETIC_WORD` | Ekranı kaplayan tek kelime/sayı karakterin arkasında ("3", "VOLUME") | Evet |
| `LINEUP` | 3-4 karakter yan yana (seviyeler/vücut tipleri), kamera kayar, etiketler | Evet |
| `METER` | Dolan çubuk / kronometre / yıldız / sayaç | Evet |
| `THOUGHT` | Düşünce balonlarında seçenekler tek tek belirir | Kısmen |
| `REACTION` | Ter, gözyaşı, alev, darbe çizgileri, yere yığılma (tepki pozu + efekt katmanı) | Evet (duygu) |
| `GHOST` | Arkada yarı saydam "ideal sen" | Kısmen |
| `PAYOFF` | Spot ışığı / ışın patlaması önünde poz | Evet (duygu) |
| `CTA_LOOP` | Yakın planda izleyiciyi işaret eder; son kare ilk kareye bağlanır (loop) | - |

Kamera bir çekimin parçası olarak gelir: yakınlaşma, kaydırma, alttan açı, hızlı geçişte hareket
bulanıklığı. Bu, beğenmediğin "jelibon sallanma" değil: karakter sabit kalır, değişen kadraj olur.
Bunu önce tek bir deneme videosunda gösterip onayını alırım.

### C. Ritim ve ses

- Her vuruş 1,5-3 saniye; anlatım cümlesi vuruşa göre yazılır (şimdi 4-5 saniyelik sahneler var).
- Her kesmede kısa "whoosh", ✗'de darbe, ✓'de "ding", sayaç tıkları. Ses efektleri zaten var;
  şimdilik plan seyrek kullanıyor.
- Alt yazı kalır ama küçülür ve ekranın ortasına iner; asıl bilgiyi görüntü taşır.

### D. Hook motoru

Hook'u modele bırakmak yerine format başına **hook kalıpları** kodda tanımlanır. Model sadece
cümleyi verir (en fazla 10 kelime, doğrulanır).

| Kalıp | İlk 1,5 saniye |
|---|---|
| Yanlışı göster | Hızlı yakınlaşma → yanlış form, kırmızı ✗, dev "STOP" |
| Hangisi doğru? | İki form yan yana, "?" (izleyici cevabı merak eder ve kalır) |
| Sen hangisisin? | Seviye dizilimi, kamera kayar |
| Şok sonuç | Yere yığılmış / ağlayan karakter + abartı efekti |
| Sayı vaadi | Dev "3" + "en sonuncusu herkesi şaşırtıyor" (açık döngü) |

Videonun sonu ilk kareye bağlanır, böylece tekrar izlenme (loop) teşvik edilir. Açılan merak
sorusu (açık döngü) sonda kapatılır.

### E. Görsel kütüphanesine eklenmesi gerekenler (senin üreteceğin)

Sayfa yöntemiyle üretilir, yaklaşık 20-25 prompt:
1. **Yanlış form çizimleri** (en önemlisi), egzersiz başına 1-3 hata. Örnekler: şınavda kalça
   düşük, dirsekler açık, yarım tekrar; squat'ta diz içe çöküyor, topuk kalkıyor; deadlift'te sırt
   yuvarlak; barfiks'te sallanma (kipping), yarım tekrar; curl'de dirsek öne kayıyor; plank'ta kalça
   yukarıda. Yaklaşık 20 görsel.
2. **4 karelik hareket döngüleri.** Gönderdiğin deneme kareleri çok tutarlı çıktı. En çok kullanılan
   10-15 egzersiz için.
3. **Maskot açıları:** yandan duruş, 3/4 açı, arkadan, yakın plan büst (yüz ifadeleri büyük), alttan
   açı. Yaklaşık 10 görsel.
4. **Abartılı tepkiler:** ağlayan, terden sırılsıklam, yere yığılmış, gururla parlayan. Yaklaşık 6.
5. **Vücut tipleri (seviyeler için):** maskotun zayıf / normal / fit / çok kaslı hâlleri.
6. **Hayat sahneleri:** masa başında, yatakta, mutfakta. Yaklaşık 4.

Efektler (ok, hareket yolu, büyüteç, ter damlası, alev, darbe çizgisi, spot ışığı) kodla çizilir,
prompt gerekmez.

### F. Model

llama3.2:3b görsel senaryo için zayıf kalabilir. Önerim: formatları kodda katı tanımlayıp modele
yalnızca boşluk doldurtmak (3B ile çalışır). Gerekirse ücretsiz `qwen2.5:7b` denenir; senin 16 GB
RAM + 4 GB ekran kartında çalışır ama video başına plan süresi yaklaşık 1-2 dakika artar. Önce 3B
ile deneyip sonucu ölçmeyi öneririm.

---

## 7. Uygulama planı

| Faz | İçerik | Senden gereken |
|---|---|---|
| **1. Temel** | Storyboard planı (vuruş listesi), 2 format (hata geri sayımı + seviyeler), hook motoru, 8 çekim tipi (SPLIT_FIX, ZOOM_DETAIL, KINETIC_WORD, METER, REACTION efektleri, PAYOFF, CTA_LOOP, kamera), yeni ritim + ses efektleri. Mevcut görsellerle çalışır; "yanlış" taraf ilk etapta gri + ✗ + ağrı işaretiyle yapılır. Deneme videosu + senin onayın. | Onay; deneme videosuna geri bildirim |
| **2. Görseller** | Yanlış form + 4 karelik döngü + açılar + tepkiler için prompt sayfaları; içe aktarma; REP_DEMO ve LINEUP çekimleri | ~20-25 sayfa prompt üretmek |
| **3. Genişleme** | Kalan formatlar (vücudun içinde, mit, yolculuk), THOUGHT/GHOST, hayat sahneleri, gerekirse 7B model denemesi | - |

**Ölçüm:**
- **Sessiz izleme testi:** her deneme videosunu sesi kapalı izleyip "ne yapmalıyım, ne yapmamalıyım"
  sorusuna cevap verebiliyor muyuz, bakarız. Plan doğrulayıcısına kural olarak da girer: her vuruşta
  görsel bir işaret (✗/✓, ok, kas, sayaç) zorunlu.
- **Yayın sonrası:** YouTube Studio'da "Stayed to watch" ve ortalama izlenme oranı takip edilir.

## 8. Senin kararın gereken noktalar

1. Bu yönü (görsel-önce storyboard + çekim kütüphanesi) onaylıyor musun?
2. Faz 1'i hangi iki formatla başlatalım? Önerim: **hata geri sayımı** (en çok izlenen tür:
   "5 push-up mistakes" 1,2 milyon) ve **seviyeler** ("Arm Levels" 513 bin, "Physique Level"
   1,3 milyon).
3. Kamera hareketi (yakınlaşma/kaydırma) denensin mi? Önce tek videoda görüp karar verebilirsin.
4. Faz 2 için prompt sayfalarını şimdi mi hazırlayayım, Faz 1 deneme videosundan sonra mı?
