# JOY IPTV Player 2.0.0 — doğrulama ve teslim notları

Windows x64 kurulumsuz tek EXE; .NET çalışma zamanı ve LibVLC motoru pakete gömülüdür. İlk açılışta gömülü motor kullanıcı klasörüne çıkarılır. Ayrı VLC kurulumu gerekmez.

## Değişiklikler
- 100.000 öğede yalnızca görünen canlı kanal satırlarını çizen liste.
- Arka planda, 180 ms beklemeli arama; eski kaynak ve sorgu sonuçları uygulanmaz.
- Afişlerde 7 günlük, 256 MB disk önbelleği; çevrimdışı kullanım ve bozuk dosya denetimi.
- Üç denemeli 2/4/8 saniyelik yeniden bağlantı, otomatik tampon ve kanal değişiminde eski denemenin iptali.
- Daraltılabilir menü, üç afiş boyutu, kaydedilen tercihler ve isteğe bağlı kısa menü hareketi.
- Kaynağa göre son izlenenler, izlemeye devam ve favoriler; hesaplara göre ayrı izleme konumları ve eski devam kayıtlarının taşınması.
- Windows kullanıcısına bağlı şifreli ayar yedeği, bütünlük denetimi, geri yükleme öncesi otomatik yedek ve hata durumunda değiştirilen dosyaların geri alınması.
- Sürüm ekranı, önceki EXE kopyası ve resmi depo üzerinden elle güncelleme akışı.

## Test kapsamı
Kaynak regresyonları: URL/dosya modu, 60.000 öğe yükleme ve önbellek, UI zamanlayıcısı, indirme iptali, film/dizi sınıflandırması ve yayıncı sırası.
Premium regresyonları: sanal liste ve klavye, 100.000 öğede arama, kaynak yarışı, çevrimdışı afiş, TTL/boyut/bozuk önbellek, sınırlı toparlanma, gizli verilerden arındırılmış dönen kayıtlar, menü tercihleri, kaynak ayrımı ve şifreli yedek.
Son inceleme testleri: salt okunur dosya hatasında tam geri alma, aynı sunucudaki ayrı hesaplar, eski devam kaydı taşıma ve ikinci yükleme, kanal adresi çözülürken eski hata olayının reddi, kaynak yenilerken oynatmanın durdurulması ve kategoriye geri dönüş.
EXE doğrulaması: Windows dosya imzası MZ ve sürüm 2.0.0.0, gömülü VLC ilk/önbellekli açılışı, 100/125/150 kontrol ve yazı ölçeğinde gerçek arayüz görüntüleri.

## Uygulama kararları ve sınırlar
1. Bulut yardımcı betiklerine erişilemediği için aynı kayıt ve kanıt düzeni elle tutuldu; maliyeti CI sonuçlarının elle izlenmesidir.
2. Bağımsız başarısızlık testleri Windows kurulum süresini azaltmak için birlikte çalıştırıldı; maliyeti tamamlanana kadar gelecek özellik testlerinin ayrı tutulmasıydı. Son pakette bütün gruplar birlikte çalışır.
3. CI tek fiziksel ekran kullandığından 100/125/150 kontrol ve yazı ölçekleri sınandı; bunlar yeniden boyutlandırılmış ekran görüntüleri değildir. Farklı fiziksel ekranlar arasında DPI geçişi kullanıcı bilgisayarında kontrol edilmelidir.
4. Sağlayıcıya özgü native olay zamanlaması ve gerçek kullanıcı oturumunda yeniden başlatma yerelde sınanamadı; bekleyen işlem durumları ile Windows motor/arayüz testleri kullanıldı. Sağlayıcıya ve ekrana özel davranışlar kullanıcı ortamında denenmelidir.

Yayın sunucusunun hızı, bağlantı limiti veya kesintisizliği garanti edilemez. Bu paket yeni bir AI süper çözünürlük motoru içermez; mevcut görüntü profilleri gerçek kaynak çözünürlüğünü değiştirmiş gibi sunulmaz. Önizlemeler örnek içerikle çalışan gerçek uygulamadan alınır.

## İnceleme
Bir bağımsız son inceleme: 0 kritik, 5 önemli, 0 küçük bulgu. Beş önemli bulgu için önce hatayı üreten testler çalıştırıldı (run 38627637037), ardından tek düzeltme geçişi uygulandı.

Son Windows doğrulama sonucu: BAŞARILI (run 38628275856). Üretim commit: 4c4dfe4d7f5531fb285b2ec062d6bcc00bf99f06.

[Windows test ve derleme kanıtı](https://github.com/kaptan38/JOY-IPTV-PLAYER/actions/runs/38628275856).

EXE ZIP SHA-256: d6db101470b2d4bf5a19c7fc47f05060ba180ca092d98b924d53a0ffe19a4cb9.
