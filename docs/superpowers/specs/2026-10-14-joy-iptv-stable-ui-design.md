# JOY IPTV — Kararlı ve Akıcı Görsel Paket
Tarih: 14 Ekim 2026
Temel sürüm: c99859bbe556ce95a907ac4ef5c98578316ed17b

## Amaç ve kapsam
Windows için mevcut .NET/LibVLC oynatıcıyı büyük kütüphanelerde akıcı, bağlantı kesilmelerinde anlaşılır ve görsel olarak kullanışlı hale getirmek. Teslimat gömülü VLC içeren kurulumsuz EXE paketidir. M3U, Xtream ve Stalker; yayıncı buket sırası; film/dizi ayrımı; tam ekran ve kayıt akışları doğrulanacaktır.

## Mevcut durum
PosterGallery görünür kartları çiziyor, en fazla dört afiş indiriyor ve 128 resmi bellekte tutuyor. Disk afiş önbelleği yok. Canlı kanal listesi ListBox'a tüm satırları ekliyor. ApplyFilter ana pencere üzerinde çalışıyor. Stability üç denemeli yeniden bağlanma ve seçilebilir tampon içeriyor. Continue izlemeye devam et verilerini saklıyor; ana sayfa şu anda düz bir katalog görünümü.

## Tasarım kararları
1. Kanal listesi, PosterGallery yaklaşımını izleyen sanal bir liste kontrolüne taşınır. Kontrol veri dizisinin yalnızca görünür bölümünü çizer. Seçim, çift tıklama, klavye, kaydırma ve kategori başına konum davranışları korunur. Satır metni görünür satırlarda üretilir.
2. Arama 180 ms yazma beklemesiyle arka planda çalışır. Her iş katalog ve arama sürümünü taşır; eski sonuçlar yayımlanmaz. Kaynak, kategori veya favori değişince bekleyen arama iptal edilir. GUI'ye tek seferde tutarlı görünür liste ve seçim uygulanır.
3. Afişler URL hash'iyle yerel diskte saklanır. Ölçeklenmiş resimler kullanılır; tazelik süresi 7 gün, toplam disk bütçesi 256 MB'dır. Okuma, çözme ve temizlik arka planda yapılır. Mevcut 4 eşzamanlı indirme, 3 MB yanıt ve 20 megapiksel sınırları uygulanır. Bozuk önbellek atılır; ağ yoksa geçerli eski afiş gösterilir. Görünür kartlar önceliklidir.
4. Mevcut üç denemeli yeniden bağlanma geliştirilir. Kaynak/kanal değişiminde eski denemeler iptal edilir. Otomatik tampon seçeneği, tekrarlanan tamponlama gözlemlerine göre yeniden bağlantıda tamponu artırır; kullanıcıdaki sabit seçenekler desteklenir. Sonsuz tekrar yapılmaz. Tanı kaydı döndürülür: en fazla 5 dosya, dosya başına 2 MB. Parola, token, MAC, sorgu ve hesap içeren URL yolları kayda yazılmaz.
5. Menüye daraltılabilir sol sütun ve küçük/orta/büyük afiş seçimi eklenir. Tercihler kaydedilir. 100%, 125%, 150% ekran ölçeklerinde kontroller kırpılmadan kullanılmalıdır. Menü hareketleri kısa ve isteğe bağlıdır; arka planda devamlı animasyon çalıştırılmaz.
6. Ana sayfada son izlenenler, izlemeye devam et ve favoriler ayrı bölümlerde sunulur. Mevcut geçmiş ve devam verileri kullanılır. İçerik yalnızca yüklü kaynaktan eşleştirilir; afiş veya özet uydurulmaz. Diğer kaynağın kaydına tıklanınca kaynak adıyla seçim istenir.
7. EXE'de sürüm numarası ve değişiklik özeti gösterilir. Ayar yedeği Windows hesabına bağlı şifreli paket olarak dışa alınır; geri yükleme öncesi otomatik yedek alınır. EXE değiştirme kullanıcı eylemiyle yapılır; önceki EXE tutulur. Güncelleme başka dosyaları çalıştırmaz. Eski EXE'ye dönüş ile ayar yedeği geri yükleme ayrı işlemler olarak gösterilir.

## Veri ve eşzamanlılık
PlayerForm kaynak/katalog durumunun sahibi kalır. Sanal liste ve afiş galerisi görünüm kontrolleridir. Arama, afiş diski ve tanı kaydı ayrı yardımcılar olur. Arka plan işleri GUI kontrollerine erişmez. Sonuçlar GUI iş parçacığında sürüm kontrolüyle uygulanır. Katalog sırası arama dışındaki görünümlerde değişmez; arama mevcut alaka sırasını kullanır.

## Doğrulama ve tamamlanma ölçütleri
- Mevcut kaynak, kategori, önbellek, iptal, VLC açılış ve tam ekran testleri geçer.
- 100.000 satırlı sanal listede başlangıç/orta/son kaydırma ve seçim doğrulanır; çizilen satır sayısı görünür satırlarla sınırlıdır.
- Hızlı ardışık arama ve kaynak değişiminde son istek kazanır; GUI zamanlayıcısı çalışmaya devam eder.
- Yerel test sunucusunda afiş ilk indirilir, ikinci çalışmada diskten okunur; bozuk kayıt ve 256 MB sınırı sınanır.
- Yeniden bağlanma üçüncü başarısız denemede durur ve kanal değişiminden sonra eski yayına dönmez.
- Ana sayfa kaynak ayrımını ve izleme konumlarını korur; sahte içerik eklemez.
- Şifreli ayar yedeği geri yüklenir; bozuk yedek mevcut ayarları değiştirmez. Tanı kayıtlarında test parolası/token bulunmaz.
- Windows üzerinde derleme, taze/önbellekli VLC açılışı ve 3 ekran ölçeğinde arayüz kontrolü yapılır.
- Örnek içerikle gerçek menü önizlemeleri ve hash/CRC kontrolünden geçen tek EXE ZIP'i teslim edilir.

## Uygulama sınırları
Sunucunun indirme hızına veya yayının kesintisizliğine mutlak süre garantisi verilmez. Görsel düzenleme akışın gerçek çözünürlüğünü artırmış gibi sunulmaz. Kullanıcı, kaynak ve ayar verileri uzak senkronizasyona gönderilmez.

## Sonraki aşama
Bu yazılı tasarımın onayından sonra uygulama planı hazırlanır ve gözden geçirilir; ardından kod, regresyon testleri ve Windows EXE teslimi tamamlanır.
