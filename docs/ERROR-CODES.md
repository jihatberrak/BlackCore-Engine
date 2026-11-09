# BlackCore Hata Kodları

Her başarısız derleme, `build-report.json` içinde ve konsolda bir hata kodu taşır
(`BAŞARISIZ [KOD] mesaj`). Aşağıdaki tablo, kodların anlamını ve tipik çözümünü verir.
Kodlar `src/BlackCore.Engine/Models/Enums.cs` içindeki `ErrorCode` enum'undan alınmıştır.

| Kod | Anlamı | Tipik çözüm |
|---|---|---|
| `None` | Başarılı; hata yok. | — |
| `ConfigInvalid` | `blackcore.json` ayrıştırılamadı, bilinmeyen bir anahtar içeriyor veya bir değer geçersiz. | Mesajdaki alanı düzeltin; yazım hatalı anahtarlar reddedilir. |
| `ConfigVersionUnsupported` | Yapılandırma `version` değeri desteklenmiyor (şu an yalnızca `1`). | `version` değerini `1` yapın. |
| `RequestInvalid` | Derleme isteği geçersiz (ör. `.exe` ile bitmeyen çıktı adı, x64 dışı hedef, koruma açık ama sağlayıcı yok). | Mesajdaki alanı düzeltin. |
| `SourceNotFound` | Kaynak dosya/klasör/proje bulunamadı veya derlenecek kaynak (`.cpp` / `.cs`) yok. | `--project` yolunu kontrol edin. |
| `WorkspaceFailed` | Benzersiz çalışma klasörü oluşturulamadı. | `build.outputDirectory` yazılabilir mi kontrol edin (`blackcore doctor`). |
| `ComponentNotFound` | Seçilen dil için frontend veya backend kayıtlı değil (ör. henüz uygulanmamış bir dil). | Dil desteğini kontrol edin. |
| `AnalysisFailed` | Analiz (Clang sözdizimi veya Roslyn derleme) hata buldu. Ayrıntı `analysis-report.json` içinde. | Kaynak koddaki hatayı düzeltin. |
| `AnalysisPolicyViolation` | Analiz uyarısı var ve `analysis.failOnWarnings` etkin. | Uyarıları düzeltin veya `failOnWarnings` değerini `false` yapın. |
| `CompilationFailed` | Derleme veya bağlama aşaması başarısız (LLVM verifier/optimizer hatası, `dotnet publish` hatası dahil). | Mesajdaki ilk hata satırına ve `logs/build.log` dosyasına bakın. |
| `ToolNotFound` | Gerekli bir harici araç bulunamadı (LLVM, `vswhere`, MSVC, Windows SDK, `dotnet`). | `blackcore doctor` ile eksik aracı belirleyin; `toolchains` ayarlarını düzeltin. |
| `ToolchainMismatch` | LLVM araçlarının sürümleri birbirinden farklı. | Tüm LLVM araçlarını aynı dağıtımdan kurun. |
| `ToolFailed` | Harici bir araç çalıştı ama beklenmeyen biçimde yanıt verdi (ör. sürüm bilgisi okunamadı). | Aracın kurulumunu doğrulayın. |
| `ToolTimeout` | Harici bir araç `execution.toolTimeoutSeconds` süresinde bitmedi. | Süreyi artırın veya aracın takıldığını kontrol edin. |
| `OutputInvalid` | Üretilen dosya PE değil, AMD64 değil, boş veya koruma sonrası doğrulamayı geçemedi. | Derleyici/koruma çıktısını inceleyin; geçersiz dosya yayımlanmaz. |
| `SmokeTestFailed` | Üretilen EXE çalıştırılamadı, sıfırdan farklı çıkış kodu verdi, zaman aşımına uğradı veya beklenen çıktıyı üretmedi. | `expected-output.txt` ve programın davranışını kontrol edin. |
| `ProtectionProviderNotFound` | Koruma etkin ama istenen sağlayıcı kayıtlı değil. | `protection.provider` değerini kayıtlı bir sağlayıcıyla eşleştirin. |
| `ProtectionFailed` | Koruma sağlayıcısı hata verdi, istisna fırlattı veya araç bulunamadı. | Sağlayıcı günlüğünü (`build.log`) inceleyin. |
| `PublishFailed` | Son artefakt hedef klasöre yazılamadı. | Çıktı klasörünün izinlerini kontrol edin. |
| `Cancelled` | Derleme iptal edildi. | — |
| `Internal` | Beklenmeyen iç hata. Ayrıntı yalnızca `build.log` içindedir. | Log dosyasını ekleyerek hata bildirin. |

## Hata raporlama

Bir hata bildirirken `artifacts/<build-id>/reports/build-report.json` ve
`artifacts/<build-id>/logs/build.log` dosyalarını ekleyin. Log, iç ayrıntıları içerir; rapor ise
hata kodu, aşama sonuçları ve süreleri içerir.
