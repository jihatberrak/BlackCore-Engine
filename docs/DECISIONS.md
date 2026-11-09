# Karar kaydı

Her karar: bağlam, seçim ve gerekçe. Şartnameden sapmalar **[Sapma]** etiketiyle işaretlidir.
Şartnameye göre değişiklik ancak gerekçe ve etkiler belgelenerek yapılabilir (şartname 2. bölüm).

## D-01 Ana dil ve framework
C#, .NET 10. Şartname kararı. `global.json` ile SDK **10.0.100**'e sabitlendi (`rollForward: latestPatch`);
varsayılan SDK bir preview olduğu için bu gerekliydi.

## D-02 Derleyici ve araç seti
Clang frontend, LLVM **22.1.8** (tam dağıtım). Tüm araçlar tek sürümde olmalı; `ToolchainMismatch` bunu zorlar.
Windows kurulumu `opt` ve `llvm-dis` içermediği için tam arşivden `bin` klasörü ayrı bir dizine çıkarıldı.

## D-03 Bağlayıcı: `lld-link` [Sapma]
Şartname 8.1 hem MSVC linker'ını hem LLVM bağlayıcısını kabul ediyor. Clang'in `-fuse-ld=lld-link` ile LLVM bağlayıcısı
seçildi: aynı dağıtımdan geldiği için sürüm uyumu korunur ve PATH'teki eski `LINK.EXE` (VC98) sorunu yaşanmaz.
Etki: MSVC kütüphaneleri hâlâ kullanılır; yalnızca bağlama aracı LLVM'dir.

## D-04 Çift optimizasyon engellendi
Şartnamenin ilk taslağında hem `opt -O2` hem `clang++ -O2` bulunuyordu. Bağlama adımı `-O0` ile çalışır; optimizasyon
yalnızca `opt` aşamasında yapılır. Böylece raporlanan profil gerçekte uygulanan profille aynıdır.

## D-05 Yapılandırma: camelCase JSON, bilinmeyen anahtar reddi
Şartname 12. bölümdeki anahtar adları birebir kullanılır. `UnmappedMemberHandling.Disallow` ile yazım hatası sessizce
yok sayılmaz. Gerekçe: şartname 12 "bilinmeyen kritik ayarlar sessizce yok sayılmamalı" diyor.

## D-06 Backend imzasına `BuildWorkspace` eklendi [Sapma]
Şartname 6.2'deki `CompileAsync(request, analysis, ct)` imzasına çalışma klasörü parametresi eklendi. Backend'in
çıktısını nereye yazacağını bilmesi gerekiyor. Şartname 6.x bölümü imza genişlemesine izin verir.

## D-07 Yayımlama yeri
Son artefakt `artifacts/<build-id>/output/` altına yazılır; ayrı bir "hedef klasör" yoktur. Şartnamenin "atomik
aktarım" gereksinimi, geçici ad + doğrulama + yeniden adlandırma ile bu klasör içinde karşılanır. Gerekçe: her
derleme kendi klasöründe izole kalır ve eski çıktı üzerine yazılmaz.

## D-08 Roslyn sürümü ve referanslar
`Microsoft.CodeAnalysis.CSharp` **5.0.0** sabitlendi. Derleme referansları çalışan runtime'ın yönetilen
assembly'lerinden alınır (yönetilmeyen DLL'ler `AssemblyName.GetAssemblyName` ile süzülür). Gerekçe: ayrı bir
referans paketi indirmeden, derleyiciyle aynı runtime'a karşı analiz.

## D-09 NativeAOT kuralları Roslyn üzerinde [Sapma-kapsam]
Şartname 9.1 "yapılandırılmış analyzer kuralları" istiyor. MVP'de yalnızca tek kural var (`NATIVEAOT001`: reflection
ve kod üretimi API'leri). Gerekçe: tam kural seti ayrı bir tasarım gerektirir; kural, uyarı olarak raporlanır ve
build'i durdurmaz. Genişletme için kural listesi `CSharpFrontend.NativeAotRiskWalker` içinde.

## D-10 NativeAOT için kopya ve PATH
Proje çalışma klasörüne kopyalanır (bin/obj hariç), böylece kaynak ağacı değişmez. `vswhere.exe` PATH'te olmadığı için
yayım sürecinin PATH'ine yalnızca Installer klasörü eklenir; kullanıcının PATH'i değişmez.

## D-11 MSVC ortamı: `VCToolsInstallDir` ve boş `INCLUDE`
Delta-debugging ile clang'in MSVC başlıklarını ve kütüphanelerini bulmak için gereken tek değişkenin
`VCToolsInstallDir` olduğu gösterildi. Ancak miras alınan `INCLUDE` (ör. VC98 başlıkları) derlemeyi bozar; bu yüzden
boşaltılır. `LIB` ise clang onu tanımlı görürse kütüphane yollarını yalnızca ondan aldığı için açıkça verilir.

## D-12 Smoke test pre-protection
Smoke test, koruma adımından **önce** çalışır. Gerekçe: koruma sonrası ikili, sağlayıcıya bağlı olarak çalışmayabilir
ve test sonucu korumayla karışır. Koruma sonrası yalnızca PE/AMD64 doğrulaması yapılır (şartname 16.4).

## D-13 Test sağlayıcısı açıkça işaretli
`blackcore-test-provider` gerçek koruma yapmaz; çıktıya işaretleyici ekler ve sonuç mesajında `[TEST]` yazar.
Gerekçe: şartname 16.4 "bu provider yalnızca test akışını doğrular; gerçek koruma yaptığı iddia edilmez" diyor.

## D-14 Gerçek koruma aracı entegre edilmedi
Şartname 11.2: "Themida entegrasyonu ancak resmi/kurulu sürümün belgelenmiş komut satırı arayüzü doğrulandıktan sonra
uygulanmalı. Parametreler tahmin edilmemeli." Parametreler doğrulanamadığı için entegrasyon yapılmadı.
Harici araç adaptör iskeleti (`ExternalToolProtectionProvider`) hazır.

## D-15 Süreç çalıştırıcı: sınırlı çıktı ve ağaç sonlandırma
Şartname 15: çıktı sınırsız belleğe yüklenmemeli, iptalde alt süreçler sonlandırılmalı. `ProcessRunner` 1 000 000
karakterle sınırlar ve `Kill(entireProcessTree: true)` kullanır.

## D-16 Build raporu ve analiz raporu ayrı dosyalar
`build-report.json` özet ve aşama sonuçlarını, `analysis-report.json` tüm diagnostic'leri tutar. Gerekçe: analiz
çıktısı büyük olabilir; ana rapor okunabilir kalır. Ana rapor analiz sayılarını içerir.

## D-17 Tekrarlanabilir derleme için `PathMap`
`Directory.Build.props` içinde `PathMap` ve `Deterministic`. Gerekçe: aynı kaynağın farklı klasörlerde aynı DLL'leri
üretmesi (şartname 1. bölüm, tekrarlanabilirlik ilkesi).

## D-18 Komut satırı ayrıştırıcısı: elle
`System.CommandLine` prerelease sürümüydü; API'si değişkenlik gösterdiği için kullanılmadı. Komutlar az ve basit.

## D-19 Test çatısı
xUnit 2.9.3, `Microsoft.NET.Test.Sdk` 17.14.1, `xunit.runner.visualstudio` 3.1.4, `Xunit.SkippableFact` 1.5.23.
Entegrasyon testleri araç yoksa **atlanır**, başarılı sayılmaz (şartname 18.7).

## D-20 Açık konular
- `blackcore.json` içindeki LLVM yolu kişisel bir yoldur; ortama göre düzenlenmelidir.
- C# optimizasyon profili NativeAOT'a eşlenmedi (D-09 ile birlikte gözden geçirilmeli).
- Temiz makine kurulumu henüz test edilmedi; yalnızca aynı makinede temiz kopya denendi.
- Git deposu yok; sürüm etiketi ve commit henüz oluşturulmadı.
