# JOY IPTV Kararlı ve Akıcı Görsel Paket Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Büyük kütüphanelerde akıcı, kesilmelerde anlaşılır ve görsel olarak kullanışlı Windows oynatıcıyı kurulumsuz EXE olarak teslim etmek.
**Architecture:** PlayerForm kaynak ve katalog durumunun sahibi kalır. Sanal liste, sürümlü arama, disk afiş önbelleği, yeniden bağlanma politikası ve yedekleme ayrı yardımcılar olur. GUI işleri GUI iş parçacığında; işleme ve disk işleri arka planda yürür.
**Tech Stack:** .NET 8 Windows Forms, mevcut LibVLCSharp/VideoLAN paketleri, System.Text.Json, SHA256, Windows ProtectedData.
**Spec:** `docs/superpowers/specs/2026-10-14-joy-iptv-stable-ui-design.md`
**Execution base:** `stable-premium-design` dalı; onaydan sonra izole çalışma dalında uygulanır.

## Global Constraints
- Teslimat gömülü VLC içeren kurulumsuz EXE paketidir.
- M3U, Xtream ve Stalker; yayıncı buket sırası; film/dizi ayrımı; tam ekran ve kayıt akışları doğrulanacaktır.
- Arama 180 ms yazma beklemesiyle arka planda çalışır.
- Arka plan işleri GUI kontrollerine erişmez. Sonuçlar GUI iş parçacığında sürüm kontrolüyle uygulanır.
- Afiş tazelik süresi 7 gün, toplam disk bütçesi 256 MB'dır.
- Mevcut 4 eşzamanlı indirme, 3 MB yanıt ve 20 megapiksel sınırları uygulanır.
- Mevcut üç denemeli yeniden bağlanma geliştirilir. Sonsuz tekrar yapılmaz.
- Tanı kaydı: en fazla 5 dosya, dosya başına 2 MB.
- Parola, token, MAC, sorgu ve hesap içeren URL yolları kayda yazılmaz.
- 100%, 125%, 150% ekran ölçeklerinde kontroller kırpılmadan kullanılmalıdır.
- Kullanıcı, kaynak ve ayar verileri uzak senkronizasyona gönderilmez.
- EXE değiştirme kullanıcı eylemiyle yapılır; önceki EXE tutulur. Güncelleme başka dosyaları çalıştırmaz.
- Sunucunun indirme hızına veya yayının kesintisizliğine mutlak süre garantisi verilmez.

## Review Focus
1. Arama sürerken kaynak değiştirme: eski kaynağın sonuçları yeni katalogda görünmemeli — görev 2.
2. Afiş indirirken pencereyi kapatma veya aynı afişin eşzamanlı istenmesi: kaynaklar güvenle bırakılmalı, tek indirme yapılmalı — görev 3.
3. Yeniden bağlantı beklerken kanal değiştirme: eski kanala dönülmemeli — görev 4.
4. Büyük yazı ölçeği, dar pencere ve uzun Türkçe adlar: menü ve seçim erişilebilir kalmalı — görev 5.
5. Bozuk, başka Windows hesabına ait veya yol kaçıran yedek: mevcut ayarlar değişmemeli — görev 7.

## Dosya haritası
- `VirtualChannelList.cs`: görünür satır çizimi, seçim ve kaydırma.
- `LibrarySearch.cs`, `LibraryFiltering.cs`: saf arama hesaplaması ve PlayerForm'a atomik sonuç uygulama.
- `PosterDiskCache.cs`: hash anahtarlı, bütçeli disk önbelleği.
- `RecoveryPolicy.cs`, `DiagnosticLog.cs`: yeniden bağlantı kararı, tampon ve temizlenmiş kayıt.
- `CinemaPreferences.cs`, `HomeSections.cs`: görünüm tercihleri ve mevcut verilerden ana sayfa.
- `SettingsBackup.cs`, `ReleaseInfo.cs`, `ReleaseTools.cs`: şifreli ayar yedeği, sürüm bilgisi ve elle güncelleme hazırlığı.
- Mevcut `Program.cs`, `CinemaShell.cs`, `PosterGallery.cs`, `Visuals.cs`, `Navigation.cs`, `Continue.cs`, `Stability.cs`, `Settings.cs`: ilgili entegrasyon noktaları; kapsam dışı yeniden düzenleme yapılmaz.
- `Tests/PremiumRegression.csproj`, `Tests/PremiumProgram.cs`, `Tests/UiHarness.cs` ve aşağıdaki test dosyaları: konsol çıkış koduyla Windows üzerinde doğrulama.
- Mevcut `Tests/SourceRegression.csproj` ve `.github/workflows/windows-portable.yml`: bütün regresyon ve paket kontrolleri.

## Ortak test çalıştırma sözleşmesi
Yeni test projesi net8.0-windows, WinForms, win-x64 ve self-contained olur; AssemblyName `JOY_IPTV_Premium_Tests` olarak atanır. Üretim derlemesine yalnızca InternalsVisibleTo bu ad için eklenir. Tests altındaki her csproj yalnızca kendi Program dosyasını ve gerekli test dosyalarını derler; iki Main giriş noktası aynı projeye girmez.
`PremiumProgram.Main(string[] args)` STA çalışır; ilk argüman native VLC dizini, `--case` isteğe bağlı test grubu seçer. UiHarness zaman aşımıyla mesajları pompalar; başarısız koşul konsola yazılır ve çıkış kodu 1 olur.
Komut: `dotnet run --project Tests/PremiumRegression.csproj -c Release -- <nativeVlcDirectory> --case <grup>`.
Her görevde önce belirtilen davranış testi yazılıp mevcut kodda başarısızlığı görülür; yalnızca eksik üyeden kaynaklanan derleme hatası gözlenirse test önce davranışı mevcut yüzeyden çağıracak biçimde düzeltilir. Görev testleri ve tüm mevcut testler yeşil olduktan sonra görev commit edilir.

### Görev 1: Sanal kanal listesi
**Files:** Create VirtualChannelList.cs, Tests/VirtualListTests.cs ve ortak test çalıştırıcısı; modify Program.cs, Visuals.cs, Navigation.cs, CinemaShell.cs.
**Interfaces:** `VirtualChannelList : ScrollableControl`; `SetItems(IReadOnlyList<Channel> items, int selectedIndex)`; `SelectedIndex`, `TopIndex`, `ItemHeight`; SelectionChanged, ItemActivated, DrawItem olayları. DrawItem, mevcut satır çizimini çağırmak için DrawItemEventArgs sağlar. `IndexFromPoint(Point)` boş alanda -1 döndürür.
- [ ] 100.000 kanal testini yaz: baş/orta/son seçimi, Up/Down/PageUp/PageDown/Home/End/Enter ve boş alan çift tıklaması doğrulansın; bir çizimde satır sayısı görünür satırlar + 2'yi aşmasın.
- [ ] `--case virtual-list` çalıştır; mevcut tüm satırları ekleyen bağlamanın testte başarısızlığını kaydet.
- [ ] Kontrolü uygula; tüm list.Items kullanım noktalarını katalog dizisi ve kontrol API'sine geçir. EPG, favori işareti ve metin görünür satır çiziminde üretilsin; seçim iki galeri arasında aynı görünür indeksle eşleşsin.
- [ ] Grup değiştirip geri dönme konum testi ve bütün SourceRegression/UI testlerini çalıştır; PASS beklenir.
- [ ] Değişiklikleri `feat: virtualize live channel list` commit'iyle kaydet.

### Görev 2: Sürümlü arka plan araması
**Files:** Create LibrarySearch.cs, LibraryFiltering.cs, Tests/SearchTests.cs; modify Program.cs, Smart.cs ve görev 1 bağlaması.
**Interfaces:** `LibrarySnapshot(Channel[] Items, string FavoritePrefix, HashSet<string> FavoriteIds, Dictionary<string,int> WatchCounts, int CatalogVersion)`; `LibraryQuery(string Text, string Kind, string Group, bool FavoritesOnly)`; `LibrarySearch.Filter(LibrarySnapshot snapshot, LibraryQuery query, CancellationToken token) -> List<Channel>`; `RequestLibraryFilterAsync(bool debounce) -> Task`.
- [ ] Aynı anda A/B/C aramaları, 180 ms bekleme, kaynak değişimi ve kapanış testlerini yaz; yalnızca son sürümün sonuçları uygulanmalı. Arama dışı sıralama kaynak dizisinin alt sırası olmalı.
- [ ] `--case search` ile ana pencere üzerinde hesaplama/eski sonuç sorununun başarısızlığını doğrula.
- [ ] FavoriteKey hash sözleşmesini ortak saf yardımcıya çıkar. GUI'den yalnızca veri snapshot'ı al; Task.Run üzerinde mevcut SmartSearchScore/öneri davranışını kullan. Yeni istek önceki token'ı iptal etsin; katalog ve arama sürümü eşleşince SetItems atomik uygulansın.
- [ ] 100.000 kayıt ve GUI heartbeat testi; hızlı kategori/favori değişimi ve mevcut kategori/önbellek testleri PASS olmalı. Synchronous ApplyFilter bekleyen eski testler sonuç sürümünü UiHarness ile beklesin.
- [ ] `feat: move library search off UI thread` commit'i.

### Görev 3: Disk afiş önbelleği
**Files:** Create PosterDiskCache.cs, Tests/PosterCacheTests.cs; modify PosterGallery.cs, CinemaShell.cs, Settings.cs.
**Interfaces:** `PosterDiskCache(string directory)`; `ReadAsync(string url, CancellationToken) -> Task<PosterCacheEntry?>`; `WriteAsync(string url, byte[] scaledImage, CancellationToken) -> Task`; `TrimAsync(CancellationToken) -> Task`; PosterCacheEntry byte verisi, kayıt zamanı ve IsStale taşır.
- [ ] Yerel HTTP test sunucusunda ilk/ikinci çalışma, 7 gün geçmiş kayıt, bozuk resim, 3 MB aşımı ve 20 MP aşımı testlerini yaz. İkinci çalışma sunucudan tekrar indirmemeli; bütçe temizliğinden sonra disk <= 256 MB olmalı.
- [ ] `--case poster-cache` ile disk önbelleği yokken beklenen başarısızlığı göster.
- [ ] Hash isimlerini kullan; dosya okuma/yazma ve resim çözmeyi worker'a taşı. En fazla 4 indirme; aynı URL için tek iş. Görünür istekler bekleyen görünmez işlerin önüne geçsin. Geçerli eski resim ağ hatasında kullanılabilir. Bitmap GUI'ye tek sahiplikle aktarılır; kapanışta token iptal edilir.
- [ ] Kapanış sırasında indirme ve aynı URL'ye iki istek testleri PASS; eski galeri/arama testleri PASS.
- [ ] `feat: cache posters on disk` commit'i.

### Görev 4: Kararlı oynatma ve tanı
**Files:** Create RecoveryPolicy.cs, DiagnosticLog.cs, Tests/RecoveryTests.cs, Tests/LogTests.cs; modify Stability.cs, Program.cs ve Settings.cs.
**Interfaces:** `RecoveryPolicy.TrySchedule(int generation) -> RecoveryAttempt?`; `Reset(int generation)`; `ObserveBuffering(TimeSpan duration)`; `AutomaticCacheMs`. RecoveryAttempt deneme numarası ve gecikmeyi taşır. `DiagnosticLog.WriteAsync(string eventName, string message, IReadOnlyList<string> secrets, CancellationToken) -> Task`.
- [ ] Üç denemede durma, 2/4/8 saniye mevcut gecikme sırası, kanal değişiminde eski girişimin iptali testlerini yaz. Otomatik tampon 1500/3000/5000/9000 ms basamaklarını aşmasın; 60 saniye kararlı durumda ilk basamağa dönsün.
- [ ] Otomatik tampon ve gizli veri kaydı testlerinin mevcut kodda başarısızlığını `--case recovery` ve `--case logs` ile göster.
- [ ] Otomatik seçeneği sabit ayarlara ekle; tampon değişimi yeni bağlantıda uygulanır. Bekleyen recovery işi kaynak/kanal değişiminde iptal edilir. Log worker'ı tüm URL'leri [adres] ile, bilinen parola/token/MAC değerlerini [gizli] ile değiştirir; 5 dosya ve 2 MB bütçesini uygular.
- [ ] Kayıtlarda test sırları ve query/path credentials bulunmadığını, büyük tek kaydın bütçeyi aşmadığını doğrula. Native VLC açılışı ve kaynak iptal testleri PASS.
- [ ] `feat: harden playback recovery and diagnostics` commit'i.

### Görev 5: Menü, afiş ölçüsü ve ekran ölçeği
**Files:** Create CinemaPreferences.cs, Tests/MenuLayoutTests.cs; modify CinemaShell.cs, PosterGallery.cs, Settings.cs, Visuals.cs.
**Interfaces:** `CinemaPreferences(bool CompactRail, PosterSize PosterSize, bool Animations)`; PosterSize = Small/Medium/Large; `Load(string directory)`, `SaveAsync(string directory, CancellationToken)`. PosterGallery.PosterSize bu enum'u kabul eder.
- [ ] Daraltılmış/geniş menü ve üç afiş boyutuyla uzun Türkçe ad, en küçük pencere, 100/125/150% ölçek testlerini yaz. Ana düğmeler ve seçili kart görünür olmalı.
- [ ] `--case menu-layout` ile olmayan seçeneklerin davranış başarısızlığını doğrula.
- [ ] Menü düğmeleri ve erişilebilir adlar ekle; küçük afişte daha fazla görünür kart göster. Tercihleri kaydet. Hareket isteğe bağlı, tek seferlik ve <= 120 ms olsun; Animations=false hareketi tamamen kapatsın.
- [ ] 3 ölçek x 3 boyutta yerleşim; tam ekran giriş/dönüş ve kayıt/ayar pencerelerine erişim PASS. Örnek içerikle gerçek kontrol bitmap'leri üret.
- [ ] `feat: add responsive cinema layout preferences` commit'i.

### Görev 6: Kullanışlı ana sayfa
**Files:** Create HomeSections.cs, Tests/HomeTests.cs; modify CinemaShell.cs, Continue.cs, Smart.cs.
**Interfaces:** `HomeItem(Channel Item, string SourceKey, DateTime Watched, long ResumeMs)`; `HomeSection(string Title, IReadOnlyList<HomeItem> Items)`; `BuildHomeSections() -> IReadOnlyList<HomeSection>`.
- [ ] Son izlenen, devam ve favori verileri olan iki kaynakla test yaz. Yüklü kaynak için doğru içerik/konum, boş kaynak için karşılama paneli, farklı kaynak için adla seçim isteği doğrulansın.
- [ ] `--case home` ile mevcut düz katalog görünümünün başarısızlığını göster.
- [ ] Mevcut ContinueEntry/resumePositions/watchCounts/favorites verilerini kullan. Dizilerde doğrudan M3U bölüm URL'si ile API seri kaydı ayrı tutulur. Bölümleri mevcut galeri bileşenleriyle göster; metadata yalnızca sağlayıcı verisinden gelir.
- [ ] Favoriden çıkarma ve kaynak değişimi testleri; film/dizi kategori ve yayıncı sırası regresyonları PASS.
- [ ] `feat: add recent and continue home sections` commit'i.

### Görev 7: Sürüm ve şifreli yedekleme
**Files:** Create SettingsBackup.cs, ReleaseInfo.cs, ReleaseTools.cs, Tests/BackupTests.cs; modify Settings.cs, Navigation.cs, IPTVPlayer.csproj, README.txt.
**Interfaces:** `ExportAsync(string settingsDirectory, string outputPath, CancellationToken) -> Task`; `RestoreAsync(string archivePath, string settingsDirectory, CancellationToken) -> Task<string>` otomatik önceki yedek yolunu döndürür; `BackupCurrentExecutableAsync(string directory, CancellationToken) -> Task<string>`. ReleaseInfo Version=2.0.0 ve Changes içerir.
- [ ] Şifreli round-trip, bozuk/başka hesaba ait veri, ../ ve mutlak/ADS zip yolları, başarısız geri yükleme testlerini yaz. Her başarısızlıkta mevcut ayarlar aynı kalmalı.
- [ ] `--case backup` ile eksik yedek/restore davranışını doğrula.
- [ ] Sadece settingsDir üst dizinindeki .dat/.json/.txt ayar dosyalarını al; cache, log, native runtime ve .tmp dosyaları dışarıda kalır. Manifestte isim/boyut/SHA256; toplam 16 MB sınırı. Paketi Windows CurrentUser ProtectedData ile koru. Restore tüm girdileri önce doğrular, otomatik şifreli yedek alır, aşamalı uygular; hata halinde geri alır.
- [ ] Restore sırasında yayın/kayıt/yükleme aktifse işlemi reddet; kullanıcıya önce durdurmasını söyle. Başarılı restore öncesi yeniden başlatma bilgisini onaylat; yalnızca mevcut uygulamanın kendi yolu kullanılır. Güncelleme ekranı resmî repo sayfasını açar ve eski EXE'nin kopyasını alır; indirilen/seçilen EXE'yi başlatmaz veya çalışan EXE'yi değiştirmez. Kullanıcı kapattıktan sonra elle değiştirir; eski EXE ile ayar yedeğinden dönüş ayrı açıklanır.
- [ ] Round-trip ve kötü yedek testleri PASS. Mevcut kaynak kaydı/oynatma ve snapshot metadata testleri PASS; sürüm 2.0.0 görünür.
- [ ] `feat: add versioned encrypted settings backup` commit'i.

### Görev 8: Windows bütünleştirme ve teslim
**Files:** Modify .github/workflows/windows-portable.yml, Tests/SourceRegression.csproj, Derle.ps1, README.txt; test tüm eski/yeni testler.
**Interfaces:** Tek EXE ZIP'i, örnek içerikli gerçek menü PNG'leri, SHA256 ve değişiklik özeti.
- [ ] CI'ya iki test projesinin tüm gruplarını ve UI ölçek kontrollerini ekle. Ölçek kontrolleri --verify-ui sürecine seçilen DPI ayarıyla başlatılır; ölçekleme helper'ı gerçek kontrol boyutlarını raporlar. Sadece bitmap'i büyütmek ölçek testi sayılmaz.
- [ ] Taze ve önbellekli --verify-native, kaynak/cancel/kategori/sıra/arama/önbellek/backup testlerini çalıştır; tüm görev grupları ve çıkış kodları başarılı olmalı.
- [ ] Şube farkını tasarımla karşılaştır; seçilen yöntem doğrultusunda son kod incelemesi yap. Yeni hatada ilgili testi önce başarısızlaştırıp düzelt.
- [ ] Gösterilen PNG'leri gözle kontrol et; örnek veri olduğunu belirt. Paket ZIP CRC'si, beklenen EXE sayısı=1, MZ başlığı ve SHA256 doğrulansın.
- [ ] Başarılı CI sonucunu GitHub ana dala, EXE/önizleme teslimlerini mevcut dosya kimliklerine sürümleyerek kaydet. Kullanıcıya kurulumsuz dosyayı, yapılanları, test sonucunu ve sağlayıcı hızının sınırını kısa biçimde ilet.
- [ ] `release: JOY IPTV stable visual 2.0.0` commit'iyle tamamla.

## Öz inceleme
Her tasarım kararı görev 1–7'ye; teslim ölçütleri görev 8'e bağlıdır. Review Focus'un beş durumu görev testlerinde yer alır. Seçim API'si görev 1'de, sorgu snapshot'ı görev 2'de, afiş boyutu enum'u görev 5'te tanımlıdır. Kaynak verileri uzak sunucuya gönderilmez. Henüz ürün kodu yazılmamıştır.

## Yürütme seçimi
Öneri: Native — uygulamayı bu oturumda benim tamamlamam. Liste, arama ve ana sayfa aynı durum akışına bağlı olduğundan tek yürütücü ile entegrasyon takibi daha sade olur.
Alternatif: ayrı uygulayıcı ve inceleyici ajanlarla görev bazında ilerleme; daha fazla bağımsız kontrol, daha fazla bağlam/iş yükü.
Plan incelemesi ve yürütme seçiminin onayından sonra kodlama başlar.
