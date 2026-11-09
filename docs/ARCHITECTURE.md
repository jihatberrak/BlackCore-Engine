# BlackCore Engine — Mimari

Bu belge, kod tabanının mevcut durumunu anlatır. Hedef tasarım ve kabul kriterleri için bkz.
[şartname](../BlackCore-Engine-Architecture.md).

## Katmanlar ve bağımlılık yönü

```text
BlackCore.Cli  ──►  Frontend.Cpp  ──┐
     │         ──►  Frontend.CSharp ┤
     │         ──►  Backend.LLVM   ─┼──►  Infrastructure  ──►  Engine (çekirdek)
     │         ──►  Backend.NativeAot┤
     └─────────►  Protection ───────┘
```

| Proje | Sorumluluk | Dış bağımlılık |
|---|---|---|
| `BlackCore.Engine` | Modeller, arayüzler, yapılandırma, workspace, log, rapor, pipeline, yayımlama, artefakt doğrulama | Yok (BCL) |
| `BlackCore.Infrastructure` | Süreç çalıştırıcı (`ProcessRunner`), LLVM ve MSVC keşfi, C# proje yardımcıları | Yok (BCL + `Microsoft.Win32`) |
| `BlackCore.Frontend.Cpp` | Clang ile sözdizimi analizi, diagnostic ayrıştırma | Clang (harici araç) |
| `BlackCore.Frontend.CSharp` | Roslyn ile derleme ve NativeAOT risk kuralları | `Microsoft.CodeAnalysis.CSharp` 5.0.0 |
| `BlackCore.Backend.LLVM` | bitcode, `llvm-dis`, `opt`, `lld-link` ile bağlama | LLVM (harici araç) |
| `BlackCore.Backend.NativeAot` | `dotnet publish -p:PublishAot=true` | .NET SDK (harici araç) |
| `BlackCore.Protection` | Sağlayıcılar: test sağlayıcısı ve harici araç adaptörü iskeleti | Yok |
| `BlackCore.Cli` | Komut satırı: `info`, `build`, `validate`, `doctor` | Yok |

Kural: `Engine` somut Clang veya Roslyn tiplerini bilmez; yalnızca `Engine.Abstractions` içindeki arayüzleri kullanır.
Frontend ve backend'ler ise `Infrastructure` üzerinden harici araçları çağırır.

## Çekirdek sözleşmeler (`Engine.Abstractions`)

| Arayüz | Uygulayan | Not |
|---|---|---|
| `ILanguageFrontend` | `CppFrontend` (`cpp`), `CSharpFrontend` (`csharp`) | `AnalyzeAsync(request)` → `AnalysisResult` |
| `ICompilerBackend` | `LlvmBackend` (`llvm`), `NativeAotBackend` (`nativeaot`) | `CompileAsync(request, analysis, workspace)` → `CompilationResult`. Şartnameden tek genişletme: `BuildWorkspace` parametresi. |
| `IProtectionProvider` | `TestProtectionProvider`, `ExternalToolProtectionProvider` (taban) | Koruma kapalıysa hiç çağrılmaz. |
| `IExternalToolRunner` | `ProcessRunner` | Tüm harici çağrılar bu arayüz üzerinden geçer; testlerde sahte uygulama verilir. |

Dil ve backend eşlemesi `BuildEngine` içinde sabittir: `Cpp → cpp/llvm`, `CSharp → csharp/nativeaot`.

## Pipeline

`BuildEngine.BuildAsync` bir `BuildContext` oluşturur ve `BuildPipeline`'ı çalıştırır. Adımlar sırayla:

`ValidateRequest → CheckSource → CreateWorkspace → Analyze → Compile → ValidateCompilation → SmokeTest → Protection → Publish`

- Bir adım başarısız olursa sonrakiler çalışmaz (`BuildPipeline.RunAsync`).
- Adım istisnası yakalanır: `BlackCoreException` kendi hata kodunu taşır, diğer istisnalar `Internal` olur ve ayrıntısı yalnızca logda kalır.
- Her adım `StageReport` (ad, sonuç, hata kodu, süre) üretir; atlanan adım `Skipped` olarak işaretlenir.
- Pipeline bitince (başarılı da olsa başarısız da olsa) `build-report.json` ve `analysis-report.json` yazılır.

Ayrıntılı adım tablosu için bkz. [BUILD-PIPELINE.md](BUILD-PIPELINE.md).

## Çalışma klasörü ve çıktı

`BuildWorkspace.Create` her build için `artifacts/<yyyy-MM-dd_HHmmss_XXXXXX>/` oluşturur. Rastgele kısım
`RandomNumberGenerator` ile üretilir ve aynı ad varsa yeniden denenir. Alt klasörler: `workspace`, `intermediate`,
`logs`, `reports`, `output`.

Yayımlama (`PublishStep`) son dosyayı önce `*.tmp` adıyla yazar, PE doğrulamasından geçirir, sonra nihai ada taşır.
Başarısız derlemede `output/` altına hiçbir dosya yazılmaz.

## Yapılandırma

`ConfigLoader` `blackcore.json` dosyasını şu kurallarla okur:

- Anahtarlar camelCase ile birebir eşlenir; bilinmeyen anahtar `ConfigInvalid` hatasıdır (`UnmappedMemberHandling.Disallow`).
- `version` yalnızca `1` olabilir.
- Enum değerleri (`configuration`, `optimization`) doğrulanır; `protection.enabled` iken `provider` zorunludur.

## Harici araç çağrıları

- Her çağrı `ProcessRequest` ile yapılır: çalıştırılabilir yol, argüman listesi, timeout, çalışma klasörü, ortam.
- `ProcessRunner` argümanları `ProcessStartInfo.ArgumentList` ile geçirir; hiçbir yerde shell string birleştirilmez.
- stdout ve stderr ayrı okunur; çıktı 1 000 000 karakterle sınırlanır (`OutputTruncated` bayrağı).
- Timeout veya iptalde süreç ağacı `Kill(entireProcessTree: true)` ile sonlandırılır.
- Araç çıktısından hata satırı `ToolInvocation.FirstErrorLine` ile çıkarılır.

### MSVC ortamı

Clang'in MSVC hedefi `VCToolsInstallDir`, `WindowsSdkDir`, `WindowsSDKVersion`, `UCRTVersion` değişkenlerinden başlık ve
kütüphane yollarını kendi bulur. `MsvcEnvironment` bu değerleri vswhere ve kayıt defterinden hesaplar. Ek olarak
`INCLUDE` boşaltılır (miras alınan değerler clang'i bozabilir) ve `LIB` açıkça verilir. Ortam yalnızca alt süreçlere
geçer; kullanıcının PATH'i değişmez.

## Tekrarlanabilirlik

`Directory.Build.props` `Deterministic=true` ve `PathMap` ile kaynak kök yolunu sabitler. Bu olmadan aynı kaynak farklı
klasörlerde farklı DLL'ler üretir (PDB yolu gömülür). Doğrulama: iki farklı kök klasörden Release build, sekiz DLL'in
SHA-256 değerleri aynı.

## Test stratejisi

| Katman | Proje | Kapsam |
|---|---|---|
| Birim | `BlackCore.Engine.Tests` | Pipeline sırası ve durdurma, yapılandırma, workspace, PE doğrulama, süreç çalıştırıcı, koruma akışları, Roslyn kuralları, NativeAOT komut argümanları |
| Entegrasyon | `BlackCore.Cpp.IntegrationTests` | Gerçek LLVM/MSVC ile C++ örneği, gerçek `dotnet publish` ile C# örneği, hatalı kaynak senaryoları |

Entegrasyon testleri araç yoksa `SkippableFact` ile **atlanır**; başarılı sayılmazlar.
