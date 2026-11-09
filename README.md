# BlackCore Engine

BlackCore, C++ ve C# kaynaklarını analiz eden, tanımlı kontrollerden geçiren ve **Windows x64 (AMD64) native EXE** üreten bir derleme aracıdır.

- **C++:** Clang analizi → LLVM bitcode → `opt` (verify + optimizasyon) → `clang++ -fuse-ld=lld-link`
- **C#:** Roslyn analizi (NativeAOT riskli reflection uyarıları) → `dotnet publish -p:PublishAot=true`
- Her derleme için izole çalışma klasörü, aşama bazlı log ve JSON build raporu
- Başarısız bir derleme hiçbir EXE yayımlamaz

Ayrıntılar: [Pipeline](docs/BUILD-PIPELINE.md) · [Hata kodları](docs/ERROR-CODES.md) · [Şartname](BlackCore-Engine-Architecture.md)

> **Kapsam sınırı (MVP):** Gerçek bir koruma aracı (ör. Themida) entegre edilmemiştir. Koruma akışı yalnızca
> `blackcore-test-provider` ile test edilebilir; bu sağlayıcı **gerçek koruma yapmaz**, çıktısına işaretleyici ekler.
> Linux/macOS, x86 ve ARM64 desteklenmez. C# için yalnızca NativeAOT yayımı vardır; C# → LLVM IR yoktur.

## Gereksinimler

| Bileşen | Sürüm / not |
|---|---|
| Windows | x64 (Windows 10/11) |
| .NET SDK | 10.0.100 (`global.json` ile sabitlenmiştir) |
| LLVM | 22.1.8 tam dağıtımı (`clang++`, `opt`, `llvm-dis`, `lld-link`). Tüm araçlar aynı sürümde olmalı. |
| Visual Studio / Build Tools | C++ araç seti (MSVC) ve Windows SDK. NativeAOT bağlama için de gerekir. |

Eksikleri kontrol etmek için `blackcore doctor` çalıştırın. Araç kurmaz ve PATH değiştirmez.

## Kurulum

Depo kökünde:

```bash
dotnet build BlackCore.sln -c Release
dotnet test BlackCore.sln -c Release
dotnet publish src/BlackCore.Cli -c Release -o publish/cli
```

Yayımlanan CLI: `publish/cli/BlackCore.Cli.exe`. Çalıştırmadan önce `blackcore.json` içindeki
`toolchains.llvmDirectory` değerini kendi LLVM kurulumunuza göre ayarlayın.

## Kullanım

```bash
publish/cli/BlackCore.Cli.exe info
publish/cli/BlackCore.Cli.exe doctor
publish/cli/BlackCore.Cli.exe build --project samples/cpp/HelloBlackCore
publish/cli/BlackCore.Cli.exe build --project samples/csharp/HelloBlackCore
publish/cli/BlackCore.Cli.exe build --project samples/cpp/HelloBlackCore --config configs/blackcore.protection-test.json
publish/cli/BlackCore.Cli.exe validate --file artifacts/<build-id>/output/HelloBlackCore.exe
```

| Komut | Açıklama |
|---|---|
| `info` | Sürüm ve desteklenen hedefler. |
| `doctor` | .NET SDK, LLVM, MSVC/SDK ve çıktı klasörü yazılabilirliğini kontrol eder. |
| `build --project <klasör\|dosya>` | Derler. Dil klasördeki `.cpp` veya `.csproj` dosyasından algılanır. `--name` çıktı adını, `--config` yapılandırmayı belirler. |
| `validate --file <exe>` | Bir EXE'nin PE yapısını ve AMD64 mimarisini denetler. |

Başarılı bir derlemenin çıktısı:

```text
Derleniyor: .../samples/cpp/HelloBlackCore
BAŞARILI: .../artifacts/<build-id>/output/HelloBlackCore.exe
Rapor: .../artifacts/<build-id>/reports/build-report.json
```

## Yapılandırma (`blackcore.json`)

Bilinmeyen anahtarlar reddedilir. Örnek ve alan açıklamaları için bkz. şartname 12. bölüm ve kökteki `blackcore.json`.

| Anahtar | Varsayılan | Anlamı |
|---|---|---|
| `build.configuration` | `Release` | `Debug` veya `Release`. |
| `build.optimization` | `O2` | `None`, `O1`, `O2`, `O3`, `Os`, `Oz` (C++ için `opt` pipeline'ı). |
| `build.outputDirectory` | `artifacts` | Çalışma ve çıktı kökü (göreli yol, geçerli klasöre göre). |
| `toolchains.llvmDirectory` | — | LLVM kök klasörü. Boşsa `clang++` PATH'te aranır. |
| `toolchains.dotnetExecutable` | `dotnet` | NativeAOT için `dotnet` komutu. |
| `analysis.failOnWarnings` | `false` | Analiz uyarısını hataya çevirir. |
| `protection.enabled` / `provider` | `false` / `null` | Koruma açıksa sağlayıcı zorunludur. |
| `execution.toolTimeoutSeconds` | `300` | Harici araç zaman aşımı. |
| `execution.smokeTestTimeoutSeconds` | `15` | Smoke test zaman aşımı. |

## Çıktılar

Her derleme `artifacts/<yyyy-MM-dd_HHmmss_XXXXXX>/` altında kendi klasörünü alır:

- `output/<ad>.exe`: yalnızca başarılı derlemede oluşur.
- `reports/build-report.json`: aşama sonuçları, süreler, araç sürümleri, EXE boyutu ve SHA-256 özeti, hata/uyarılar.
- `reports/analysis-report.json`: analiz bulguları (dosya, satır, kod, mesaj).
- `logs/build.log`: JSON satırları halinde yapılandırılmış log.
- `intermediate/`: ara dosyalar (`.bc`, `.ll`, `.opt.bc`, NativeAOT yayım klasörü). Release çıktısına kopyalanmaz.

## Bilinen sınırlamalar

- C# için `build.optimization` NativeAOT ayarına eşlenmez; `dotnet publish` varsayılanları kullanılır.
- Smoke test, üretilen programı izole bir kullanıcı hesabında değil, doğrudan çalıştırır. Güvenilmeyen kaynak için derlemeyi izole bir ortamda yapın.
- LLVM verifier hatasını tetikleyen bir girdi için test yoktur.
- Hata kodları ve rapor alanları [docs/ERROR-CODES.md](docs/ERROR-CODES.md) içinde belgelidir.
