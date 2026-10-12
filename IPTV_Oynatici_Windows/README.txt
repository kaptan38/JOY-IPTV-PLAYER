JOY IPTV Player - Windows 10/11 x64

AKILLI YARDIMCI: Araclar > Akilli yardimci ve oneriler.
Birden cok kelimeyle arama: aksiyon 2024 4k; kelimeler baslik,
kategori ve icerik turunde birlikte aranir. Turkce karakter ve
uzun kelimelerde yazim hatasi toleransi vardir; sayilarda yoktur.
UHD ve 4K etiketleri birlikte degerlendirilir. Etiketi olmayan bir
icerigin cozunurlugu veya turu tahmin edilmez.
Oneriler yerel izleme gecmisi ve favorilerden siralanir; nedenleri
yardimci panelinde gosterilir. Yayin aciklamasi VLC durumuna dayanir.
Bu surum uretken yapay zeka / bulut modeli baglantisi kullanmaz.
Yeni panel hesap bilgilerini dis servise gondermez, API anahtari istemez.
Windows derlemesi bu paket hazirlanirken calistirilamamistir.

YENI MENU: Canli TV, Film ve Dizi kutulari kaynak sayilarini gosterir.
Liste, cok sayida yayinda toplu guncellenir; yukleme suresi bilgisayar
ve liste boyutuna baglidir. Yayin bilgisi kisa sureli ve engelsiz paneldir.
IMDb aramasi tarayicida acilir; otomatik afis/meta tarama hizmeti degildir.
Yerel kayitli kaynaklar profil olarak acilabilir; bulut senkronizasyonu yoktur.
Kanal degistirme suresi ve 4K/8K oynatma kaynak, ag, kodek ve GPU'ya baglidir.

1. Windows bilgisayarda .NET 8 SDK kurulu olmalidir.
   https://dotnet.microsoft.com/download/dotnet/8.0
2. ZIP'i cikarin, Derle.cmd dosyasini calistirin. Ilk derlemede
   internet gerekir; VLC kitapliklari NuGet uzerinden indirilir.
3. yayin klasorundeki JOY_IPTV_Player.exe dosyasini acin.
   yayin/libvlc/win-x64 klasorunu EXE'den ayirmayin; oynatma
   kutuphaneleri ve eklentileri bu klasordedir.
4. M3U dosyasi, M3U URL, Xtream veya Stalker secip kendi kaynaginizi girin.
5. Kanala cift tiklayarak oynatin. Kaynak bilgileri yalnizca
   "Kaynagi kaydet" secilirse sifrelenerek diske kaydedilir.

Stalker icin varsayilan giris portal URL + MAC adresidir.
Canli kanallar ilk asamada gorunur; film/dizi kataloglari arka planda eklenir.
Ust menudeki Ayarlar dugmesi goruntu, ag ve hava durumu seceneklerini acar.
Araclar > Kaynak DNS / ag tanisi; DNS, TCP ve HTTP yanitini ayri gosterir.
Windows VPN ayarlari sistem penceresini acar; program VPN hizmeti sunmaz.
Portal kullanicisi oynatma hakkina sahip olmalidir. Portallar farkli
endpoint veya token davranisi kullanabilir; calismayan bir portal icin
hata mesajini paylasin, ilgili baglanti akisini duzeltelim.

M3U ve Xtream'de canli yayin, film ve dizi listeleri desteklenir.
Buyuk M3U URL listeleri akis halinde indirilir; indirme ilerlemesi gosterilir.
Hesap suresi sorgusu, listeyi goruntulemeyi bekletmeden arkada tamamlanir.
Stalker'da canli yayin, portalin sundugu film ve dizi kataloglari
ve dizilerin bolum listeleri desteklenir. Film/dizi icin portalin
ilgili API isteklerine yanit vermesi gerekir.
DRM korumali akislari veya saglayici kisitlarini asmaz.

TOPLU GELISTIRME: Kaynak turu ve kategori secimi, adla arama,
favoriler (yalnizca kayit ozetleri yerel olarak saklanir),
onceki/sonraki yayin, duraklat/surdur, ses seviyesi, yayinin
sundugu ses ve altyazi izlerini secme, tam ekran. Xtream ve
Stalker kategori adlari destekleniyorsa API'den okunur.
M3U gruplari film/dizi/canli TV olarak siniflandirilir.
Xtream canli kanallarda EPG dugmesi kisa program listesini gosterir.

GORUNTU VE SIRALAMA SURUMU:
- Tam ekran modunda yalnizca yayin gorunur. F11 veya Esc ile cikis;
  video uzerinde sag tik menusu de kullanilabilir.
- M3U dosyasi yayincinin satir sirasinda kalir. Xtream kategori API
  sirasi ve varsa kanal numarasi; Stalker tur API sirasi ve varsa
  kanal numarasi kullanilir. Kategori kutusu alfabetik siraya gecmez.
- Goruntu: Kaynaga sadik varsayilandir. Taramali yayin secenegi
  taramali goruntulerde yadif filtresi uygular (CPU kullanimi artabilir).
  Akici secenegi tamponu buyutur; gecikme artabilir. Bu ayarlar
  yayinin gercek cozunurlugunu veya bit hizini artiramaz.

YENI TOPLU SURUM:
- Sol taraftaki tur seciminden Canli TV, Film, Dizi, Bolum ve Tumu
  gorunumleri arasinda gecin. Buket/kategori listesi ture gore ayrilir.
- Diziye cift tiklayin: bolum listesi acilir. Onceki dizi listesine
  donmek icin Yukle'ye basin.
- Film ve bolumlerde ilerleme cubugundan ileri/geri sarin. Izleme
  konumu yerel olarak kaydedilir ve tekrar acinca devam eder.
- Kaynagi kaydet dugmesi hesap ve URL'yi Windows CurrentUser DPAPI ile
  sifreleyerek kaydeder; sadece ayni Windows kullanicisi acabilir.
  Kaynagi ac/sil dugmeleriyle kaynaklar arasinda gecin.
- M3U'da #EXTM3U satirindaki url-tvg/x-tvg-url varsa otomatik okunur.
  Yoksa XMLTV EPG URL girin ve EPG yukle'ye basin. tvg-id eslesen
  kanallarda simdi/siradaki program kanal listesinde gorunur.
  Xtream icin XMLTV adresi otomatik denenir; EPG dugmesi kisa EPG'ye
  geri donebilir. Stalker'da secilen kanalin EPG'si destekleniyorsa
  portaldan okunur. XMLTV 64 MB ile sinirlidir.
- Baglanti tanila dugmesi sunucuya TCP baglantisini ve oynatici
  durumunu gosterir; yayin yetkisi veya codec testini garanti etmez.
- Video odaktayken sol/sag canli kanali degistirir, film/bolumlerde
  10 saniye sarar. Bosluk duraklatir; F11 ve Esc tam ekrani yonetir.

AKILLI KULLANIM SURUMU:
- Arama Turkce karakter farklarini ve kucuk yazim hatalarini tolere
  eder. Arama yokken yayincinin sirasi korunur; aramada en ilgili
  eslesmeler basta gorunur.
- Onerilen gorunumu 30 saniye veya daha fazla izlenen kanallarin,
  filmlerin ve dizilerin tur/kategorisine gore yerel siralama yapar.
  Bu islev kural tabanlidir; yeni bir yapay zeka modeli indirmez.
  Izleme gecmisi adlar veya hesap bilgileri yerine kayit ozetleri tutar.
- Basarili liste yuklemeleri Windows kullanici sifrelemesiyle korunmus
  onbellege kaydedilir. Ag baglantisi koparsa son liste goruntulenir;
  yayinlarin acilmasi hala saglayicinin erisilebilir olmasina baglidir.
- Baglanti tanila butonu TCP'ye ek olarak mevcut yayin icin kisa
  HTTP yanitini kontrol eder; adres/parola hata penceresine yazilmaz.
- Gercek zamanli goruntu modeli eklenmedi; GPU ve model testi gerekir.
  Istege bagli yerel altyazi yolu asagida aciklanir.

TEMA VE ALTYAZI SURUMU:
- Tema kutusunda OLED Siyah, Okyanus ve Acik gorunum secilebilir.
  Tema secimi sonraki acilista hatirlanir. Tam ekran yine sadece
  video gosterir.
- Altyazi dosyasi dugmesi acik yayina .srt/.vtt/.ass ekler.
- AI altyazi (10 dk) secili film/dizi bolumunun oynadigi noktadan
  10 dakikalik sesi isleyip SRT ekler. Bunun icin kullanici kendi
  bilgisayarindaki ffmpeg.exe, whisper-cli.exe ve uyumlu .bin modelini
  AI araclarini sec dugmesinden bir kez tanitmalidir.
  Araclar pakete dahil DEGILDIR. Yerel ses gecici WAV dosyasina
  yazilir ve islem bitince silinir; SRT yerelde saklanir.
  Bu islem saglayicida ikinci bir akis acabilir. Bazi saglayicilar
  buna izin vermeyebilir. Canli TV icin AI altyazi desteklenmez.
- Goruntu bilgisi dugmesi yayinin gercek cozunurluk ve FPS bilgilerini
  gosterir. Gercek zamanli AI video yukseltme dahil degildir;
  ayri model/GPU olmadan bunu vaat etmek dogru olmaz.

GORUNTU VE COKLU KAYIT:
- Renk kutusundan Orijinal, Dengeli veya Canli goruntuyu secin. Dengeli
  ve Canli goruntude kontrast ve doygunluk ayari uygular. Taramali
  yayinlarda mevcut Taramali goruntu secenegi kullanilabilir. Bu
  ayarlar yayinin gercek cozunurlugunu arttirmaz.
- Listeden bir canli kanal secip Kayda al'a basin. Ilk kayitta kendi
  ffmpeg.exe dosyanizi ve kayit klasorunu secin. Kayıtlar dugmesinden
  ayni anda en fazla dort kanalin kaydini gorun ve tek tek durdurun.
  Kayitlar her kanal icin ayri .mkv dosyasina aktarilir; uygulamadan
  cikarken kayitlar kapatilir. Yayincinin eszamanli baglanti siniri
  birden cok kaydi kisitlayabilir. FFmpeg bu pakete dahil degildir.

AYARLAR, HIZ TESTI VE HAVA DURUMU:
- Ana menudeki Ayarlar penceresinde goruntu modu, renk profili, tema,
  goruntu bilgisi ve yerel altyazi araclari bulunur. XMLTV EPG adresi,
  EPG yukle ve baglanti tanila secenekleri de buraya tasinmistir.
- Baglanti ve EPG sekmesindeki hiz testi Cloudflare uzerinden 8 MB
  indirerek yaklasik tek baglanti indirme hizini gosterir. Bu sonuc
  IPTV sunucusunun hizini veya yukleme hizini olcmez.
- Hava durumu sekmesinde sehir adini yazip Ara'ya basin, dogru sehri
  listeden secin. Sicaklik, hissedilen sicaklik, nem ve ruzgar
  Open-Meteo servisinden alinir; secilen arama metni hatirlanir.
  Bu iki ozellik icin internet gerekir.

CANLI YAYIN KARARLILIGI:
- Ayarlar > Baglanti ve EPG > Canli yayin tamponu seceneginde Hizli
  (1,5 sn), Dengeli (3 sn) veya Kararli (6 sn) secilebilir. Daha buyuk
  tampon gecikmeyi artirir, kisa sureli ag dalgalanmasini yumusatir.
- HTTP baglantisi kesildiginde VLC yeniden baglanmayi dener. Yayinda
  oynatma hatasi veya yayin sonu algilanirsa uygulama canli kanala
  2, 4 ve 8 saniye arayla en fazla uc kez tekrar baglanir. Kanal
  degistirilirse bekleyen denemeler iptal edilir. Stalker baglantisinda
  yayin adresi yeniden alinir. Film/dizi bolumleri otomatik yenilenmez.
- Sunucunun akisi kesmesi, yetersiz ag hizi veya saglayici sinirlari
  durumunda kesintisiz oynatma garanti edilemez.

STALKER VOD VE KAYNAK SURELERI:
- Stalker film/dizi kategorileri ve sayfali listeler okunur. Diziye
  cift tiklayinca sezon/bölüm listesi; bolume cift tiklayinca portalin
  verdigi oynatma adresi acilir. Saglayiciya gore VOD arayuzleri
  degisebilir. Her iki katalogdan biri hata verirse canli TV kalir.
  Dizilere don dugmesi katalogu yeniden indirmeden geri getirir.
- Birden cok M3U, Xtream ve Stalker kaynagi Kaynagi kaydet ile Windows
  kullanicisina bagli sifrelenmis kayitlar halinde saklanir. Kaynagi
  ac/sil ile aralarinda gecilebilir.
- Kaynak suresi dugmesi baslangic, bitis ve kalan gunu gosterir.
  Xtream ve uygun get.php M3U kaynaklarinda hesap API'si, Stalker'da
  profil/hesap bilgileri kullanilir. Saglayici tarih belirtmemisse
  program tarih uydurmaz. Kalan gun cihazdaki yerel tarihle hesaplanir.
- Ayarlar > Baglanti ve EPG > Internet indirme hizini olc dugmesi
  indirme hizini ve ilk HTTP yanit suresini gosterir. Sonuc IPTV
  sunucusundaki yayin hizini ifade etmez.
- Yayinin acilisi 25 saniyeden uzun sure Buffering/Opening durumunda
  kalirsa uygulama baglantiyi sinirli sayida yeniden dener. Sunucu
  erisim/abonelik kisitlamalarini asamaz.
- Stalker portal erisimi 401/403 donerse token bir kez yenilenerek
  istek tekrar denenir. Kaynak yuklenirken film/dizi sayfa ilerlemesi
  durum satirinda gorunur; ayni anda ikinci bir yukleme baslatilmaz.
- Yeni kaynak kaydi "Yeni kaynak" adi ile acilir. Mevcut bir adi
  girerseniz ustune yazmadan once ayrica onay ister.

YENI MENU VE KAYIT DUZELTMELERI:
- Ust menude Kaynaklar, Oynatma, Kayit ve altyazi, Araclar bolumleri
  bulunur. Kaynak girisi, Yukle, Duraklat, Kayda al, Tam ekran ve
  Ayarlar gibi sik kullanilan komutlarin hizli dugmeleri gorunur kalir.
  Tema renkleri menuye de uygulanir. Tam ekranda menu ve alttaki
  oynatma cubugu da gizlenir; yalnizca yayin gorunur.
- Eszamanli baslatilan kayitlar da dort kayit sinirina dahildir.
  Ayni isimli/yaklasik ayni anda baslatilan kayitlar ayri dosyaya
  yazilir. Kaydi durdururken FFmpeg surecinin kapanis durumu izlenir.

SON SURUM: KAYNAK, REHBER, DEVAM VE ZAMANLAYICI:
- Kaydedilmis son kaynak program acilinca otomatik acilir ve listesi
  yuklenir. Yalnizca "Kaynagi kaydet" ile kaydedilmis kaynaklar
  otomatik acilir. Ag yoksa onceki sifreli liste denenir.
- Oynatma > EPG icinde ara: indirilen XMLTV program rehberinde
  kanal ve program adiyla arayin. Arama sonraki 48 saati gosterir.
  Sonuca cift tiklamak kanali acar. Stalker portalinin tek kanallik
  rehberi toplu XMLTV aramasina dahil degildir.
- Oynatma > Izlemeye devam et: film ve bolumleri son izleme
  konumuyla listeler. Liste yalnizca acik kaynak icindir; kayit
  Windows kullanicisi icin sifrelenir. Bitirilen icerik listeden
  cikar. Secili ogeyi listeden manuel kaldirmak da mumkundur.
- Kayit ve altyazi > Zamanlanmis kayit ekle: canli kanal secin,
  baslangic ve sure girin. Bilgisayar ve oynatici acik olmali,
  dogru kaynak yuklu kalmalidir. Ilk planlamada ffmpeg.exe ve
  klasor secilir. 4 eszamanli kayit siniri gecerli; kayit zamani
  5 dakikadan fazla kacirilirsa plan atlanir. Planlar sifreli
  kaydedilir, program yeniden acilinca geri gelir.
- Mevcut uc temaya Gece Moru, Orman, Gun Batimi ve Komur Altin
  eklendi. Tema secimi Ayarlar > Goruntu ve tema icinde kalir.

Yerel araclarin resmi kaynaklari:
https://ffmpeg.org/download.html
https://github.com/ggml-org/whisper.cpp
Kaynak adresi ve giris bilgileri uygulama tarafindan saklanmaz.

Bu paket kaynak koddur. Hazir EXE icermez. Windows icin derlenmis
EXE'yi burada calistirip dogrulayamadigimizdan, derleme ve oynatma
sonucunu Windows bilgisayarda test etmek gerekir.

VLC BASLATMA DUZELTMESI: Derle.ps1 NuGet paketindeki x64 yerel VLC
dosyalarini yayin/libvlc/win-x64 klasorune kopyalar. Tek basina EXE'yi
baska bir klasore tasimak VLC dosyalarini geride birakir.

ACILIS TANILAMA SURUMU: EXE acilmazsa Masaustu'nde
JOY_IPTV_acilis_hatasi.txt olusur. Icini paylasin. Kayit olusmazsa
Windows Olay Goruntuleyicisi > Windows Gunlukleri > Uygulama kaydina bakin.
