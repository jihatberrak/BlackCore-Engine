# Güvenlik notları

Bu belge BlackCore'un tehdit modelini, uygulanan önlemleri ve bilinen açıkları listeler.
**BlackCore bir koruma (protection) aracı değildir.** Üretilen EXE'ler tersine mühendisliğe karşı korunmuş sayılmamalıdır.

## Tehdit modeli

| Varlık | Tehdit | Durum |
|---|---|---|
| Kaynak kod (güvenilmeyen olabilir) | Derleme sırasında kod çalıştırılması | **Açık risk.** Aşağıya bakın. |
| Üretilen EXE | Smoke test sırasında kötü amaçlı davranış | **Açık risk.** İzolasyon yok. |
| Harici araçlar (clang, opt, dotnet) | Shell enjeksiyonu, yanlış araç seçimi | Azaltıldı (aşağıda). |
| Yapılandırma | Yazım hatası nedeniyle sessiz yanlış davranış | Azaltıldı: bilinmeyen anahtarlar reddedilir. |
| Çıktı bütünlüğü | Eski veya bozuk EXE'nin başarı gibi sunulması | Azaltıldı: atomik yayım ve PE doğrulaması. |
| Loglar ve raporlar | İç ortam bilgisinin sızması | Kısmen: iç hatalar yalnızca logda, ama log tam yolları içerir. |

## Uygulanan önlemler

- **Shell yok.** Tüm harici süreçler `ProcessStartInfo.ArgumentList` ile başlatılır. Kullanıcı girdisi
  (`--project`, `--name`, yapılandırma değerleri) komut satırına birleştirilmez. Test adaptörü `cmd.exe` kullanır,
  ama argümanlar sabit bir listedir; yine de bu adaptör yalnızca test içindir.
- **Çıktı adı doğrulanır.** `OutputFileName` geçersiz dosya adı karakterleri içeremez ve `.exe` ile bitmelidir
  (`ValidateRequestStep`).
- **Zaman sınırları.** Her harici çağrının timeout'u vardır; süreç ağacı sonlandırılır.
- **Çıktı sınırı.** Harici araç çıktısı bellekte 1 MB ile sınırlıdır; fazlası atılır ve bayrak işaretlenir.
- **Atomik yayım.** Başarısız veya doğrulanmamış çıktı nihai konuma taşınmaz.
- **Ortam sınırlaması.** MSVC için yalnızca `VCToolsInstallDir`, SDK sürüm değişkenleri ve `LIB` verilir; `INCLUDE`
  boşaltılır. Ortam değişkenleri kullanıcının PATH'ini değiştirmez; yalnızca alt süreçlere geçer.
- **Doğrulama.** Koruma sağlayıcısının başarılı çıkış kodu tek başına kabul edilmez; çıktı yeniden PE/AMD64 olarak doğrulanır.
- **Sağlayıcı istisnaları** `ProtectionFailed` olur; istisna metni kullanıcıya gösterilmez.
- **Test sağlayıcısı açıkça işaretli.** `TestProtectionProvider` çıktısına bir işaretleyici ekler ve sonuç mesajında
  `[TEST] Gerçek koruma uygulanmadı` yazar.

## Açık riskler (bilinçli olarak kabul edilmiş MVP sınırları)

1. **Derleme sırasında kod çalışır.**
   - `dotnet publish` çalıştırılan projenin MSBuild hedeflerini ve NuGet paketlerini işler. Güvenilmeyen bir `.csproj`
     veya paket, derleme sırasında keyfi kod çalıştırabilir.
   - Roslyn analizi kod çalıştırmaz, ama NativeAOT yayımı çalıştırır.
   - **Öneri:** Güvenilmeyen kaynakları yalnızca izole bir kullanıcı hesabında veya sanal makinede derleyin.
2. **Smoke test izolasyonsuz çalışır.** Üretilen program, derleyen kullanıcının yetkileriyle ve ağıyla çalışır.
   Şartname 10. bölüm izole ortam tercih eder; bu MVP'de uygulanmadı.
3. **Koruma yok.** Gerçek bir koruma aracı entegre edilmedi. Test sağlayıcısının çıktısı tersine mühendisliğe karşı
   korunmuş değildir. Dağıtımda bunu varsaymayın.
4. **Artefakt imzalanmaz.** Üretilen EXE'ler dijital imzalanmaz; yalnızca SHA-256 özeti raporda bulunur.
   Bütünlük için raporu ve özeti güvenilir bir kanalla karşılaştırın.
5. **Harici araç bütünlüğü doğrulanmaz.** LLVM araçlarının sürüm uyumu kontrol edilir, ama kurulumun imzası veya
   hash'i doğrulanmaz. Sahte bir `clang++.exe` yerel yapılandırmadan kullanılabilir (`toolchains.llvmDirectory`).
6. **Loglarda yollar vardır.** Hata mesajları ve loglar kullanıcı profil yollarını içerebilir.
7. **Kaynak erişimi.** Derleme, `build.outputDirectory` ve kaynak klasöründeki tüm `.cs`/`.cpp` dosyalarını okur.
   Kaynak ağacında gizli dosya olmamalıdır.

## Güvenlik açığı bildirimi

Açık bir güvenlik açığı bulduğunuzda lütfen önce proje sahibine özel olarak bildirin. Bildirimde şunları ekleyin:
sürüm/commit bilgisi, yeniden üretim adımları ve `build-report.json` (varsa). Rapor içindeki yolları gerektiğinde düzenleyin.
