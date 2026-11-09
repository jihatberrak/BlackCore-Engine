# BlackCore Engine

## Teknik Mimari, Geliştirme Şartnamesi ve Agent Uygulama Talimatı

**Belge türü:** Proje ana şartnamesi\
**Durum:** Başlangıç / MVP\
**Hedef platform:** Windows x64\
**Ana geliştirme dili:** C#\
**Hedef framework:** .NET 10\
**İlk derleme hattı:** C++ → LLVM IR/bitcode → optimize edilmiş native
Windows EXE\
**İkinci derleme hattı:** C# → Roslyn analiz → .NET NativeAOT → native
Windows EXE

------------------------------------------------------------------------

# 1. Projenin amacı

BlackCore Engine; C++ ve C# kaynak kodlarını analiz eden, tanımlanmış
kalite ve güvenlik kontrollerinden geçiren, uygun derleme arka ucuyla
native Windows uygulaması üreten ve isteğe bağlı olarak harici bir
koruma sağlayıcısına aktarabilen modüler bir derleme platformudur.

Projenin temel hedefleri:

1.  Kaynak kodu derlemeden önce analiz etmek.
2.  Hataları, uyarıları ve desteklenmeyen özellikleri açıkça raporlamak.
3.  Derleme sürecini tekrarlanabilir ve izlenebilir hâle getirmek.
4.  C++ için LLVM tabanlı native derleme hattı sağlamak.
5.  C# için ilk aşamada .NET NativeAOT kullanmak.
6.  Koruma işlemini derleme motorundan bağımsız bir eklenti/sağlayıcı
    olarak tasarlamak.
7.  Her aşamanın çıktısını, süresini, araç sürümünü ve hata bilgisini
    kaydetmek.
8.  İleride yeni diller, hedef mimariler ve koruma sağlayıcıları
    eklenebilmesini sağlamak.

BlackCore Engine bir IDE veya yeni bir programlama dili değildir. İlk
sürümde mevcut kaynak kodlarını alıp denetleyen ve derleyen bir araç
zinciridir.

# 2. Kesin teknik kararlar

Aşağıdaki kararlar MVP boyunca temel kabul edilecektir. Değişiklik ancak
teknik gerekçe ve etkileri belgelenerek yapılabilir.

  Konu                 Karar
  -------------------- ----------------------------------------
  Ana platform         Windows x64
  Ana dil              C#
  Framework            .NET 10
  Çözüm formatı        Visual Studio Solution
  C++ frontend         Clang
  C++ ara gösterimi    LLVM IR / bitcode
  C++ optimizasyonu    LLVM `opt`
  C++ native üretimi   Clang/LLVM ve MSVC linker araç zinciri
  C# analiz            Roslyn
  C# native üretimi    .NET NativeAOT
  Koruma               Bağımsız `IProtectionProvider` arayüzü
  İlk arayüz           Komut satırı (CLI)
  Yapılandırma         JSON
  Test                 xUnit
  Log                  Yapılandırılmış log
  İlk hedef mimari     x86-64 / AMD64

## 2.1. C++ ve C# derleme yolları birbirinden ayrıdır

C++ hattı:

``` text
C++ Source
   ↓
Clang frontend
   ↓
LLVM IR / Bitcode
   ↓
LLVM verification
   ↓
LLVM optimization
   ↓
Clang + Windows linker
   ↓
Native Windows PE executable
```

C# hattı:

``` text
C# Source / Project
   ↓
Roslyn parsing and diagnostics
   ↓
Roslyn analyzers and policy checks
   ↓
.NET NativeAOT publish
   ↓
Native Windows PE executable
```

**Önemli:** İlk sürümde C# kodunun LLVM IR'ye çevrildiği iddia
edilmeyecektir. NativeAOT, C# için ayrı bir native backend'dir. C# →
LLVM IR dönüşümü ileride araştırılabilecek bağımsız bir Ar-Ge konusudur.

# 3. MVP kapsamı

## 3.1. MVP içinde olacaklar

-   C++ kaynak dosyasını derleme.
-   C++ için LLVM bitcode üretme.
-   LLVM bitcode doğrulama.
-   LLVM optimizasyonu.
-   Windows x64 EXE üretme.
-   C# proje ve kaynaklarını Roslyn ile analiz etme.
-   C# projelerini NativeAOT ile yayımlama.
-   Derleme öncesi ve sonrası doğrulama.
-   JSON yapılandırma dosyası.
-   CLI üzerinden derleme başlatma.
-   Her derleme için ayrı çalışma klasörü.
-   Aşama bazlı log ve hata raporu.
-   Birim ve entegrasyon testleri.
-   Koruma sağlayıcısı arayüzü ve korumasız varsayılan akış.

## 3.2. MVP dışında tutulacaklar

Aşağıdakiler ilk çalışan sürüm tamamlanmadan geliştirilmeyecektir:

-   Kendi programlama dilimiz.
-   Kendi LLVM fork'umuz.
-   C# için özel LLVM backend.
-   Kendi linker uygulamamız.
-   Kendi packer/virtualizer motorumuz.
-   Anti-debugging ve anti-VM teknikleri.
-   Kodun çözülemez hâle getirileceği iddiası.
-   GUI arayüzü.
-   Linux ve macOS hedefleri.
-   x86 ve ARM64 hedefleri.
-   Otomatik olarak her üçüncü taraf protector'ın desteklenmesi.

# 4. Üst seviye mimari

``` text
┌────────────────────────────────────────────────────┐
│                 BlackCore CLI                       │
│          build / analyze / validate / info          │
└───────────────────────┬────────────────────────────┘
                        │
┌───────────────────────▼────────────────────────────┐
│                BlackCore Engine                     │
│                                                     │
│  Configuration   Pipeline   Diagnostics   Logging  │
│  Workspace       Artifacts  Tool Discovery          │
└───────────────┬─────────────────────┬──────────────┘
                │                     │
       ┌────────▼────────┐   ┌────────▼────────┐
       │ C++ Frontend    │   │ C# Frontend     │
       │ Clang           │   │ Roslyn          │
       └────────┬────────┘   └────────┬────────┘
                │                     │
       ┌────────▼────────┐   ┌────────▼────────┐
       │ LLVM Backend    │   │ NativeAOT       │
       │ Verify / Opt    │   │ Publish         │
       │ Native Link     │   │ Native Output   │
       └────────┬────────┘   └────────┬────────┘
                │                     │
                └──────────┬──────────┘
                           │
                ┌──────────▼──────────┐
                │ Artifact Validator  │
                │ PE / Architecture   │
                │ Dependencies        │
                │ Smoke Tests         │
                └──────────┬──────────┘
                           │
                ┌──────────▼──────────┐
                │ Protection Layer   │
                │ Optional Provider  │
                └──────────┬──────────┘
                           │
                ┌──────────▼──────────┐
                │ Final Artifact     │
                │ EXE + Build Report │
                └─────────────────────┘
```

# 5. Solution ve proje yapısı

İlk solution aşağıdaki projelerden oluşacaktır:

``` text
BlackCore/
│
├── BlackCore.sln
│
├── src/
│   ├── BlackCore.Cli/
│   │   ├── Commands/
│   │   ├── Program.cs
│   │   └── BlackCore.Cli.csproj
│   │
│   ├── BlackCore.Engine/
│   │   ├── Abstractions/
│   │   ├── Configuration/
│   │   ├── Diagnostics/
│   │   ├── Pipeline/
│   │   ├── Workspace/
│   │   ├── Artifacts/
│   │   └── BlackCore.Engine.csproj
│   │
│   ├── BlackCore.Frontend.Cpp/
│   │   ├── Analysis/
│   │   ├── Compilation/
│   │   └── BlackCore.Frontend.Cpp.csproj
│   │
│   ├── BlackCore.Frontend.CSharp/
│   │   ├── Analysis/
│   │   ├── Compilation/
│   │   └── BlackCore.Frontend.CSharp.csproj
│   │
│   ├── BlackCore.Backend.LLVM/
│   │   ├── Bitcode/
│   │   ├── Verification/
│   │   ├── Optimization/
│   │   ├── Linking/
│   │   └── BlackCore.Backend.LLVM.csproj
│   │
│   ├── BlackCore.Backend.NativeAot/
│   │   ├── Publishing/
│   │   └── BlackCore.Backend.NativeAot.csproj
│   │
│   ├── BlackCore.Protection/
│   │   ├── Abstractions/
│   │   ├── Providers/
│   │   └── BlackCore.Protection.csproj
│   │
│   └── BlackCore.Infrastructure/
│       ├── Processes/
│       ├── Toolchains/
│       ├── FileSystem/
│       └── BlackCore.Infrastructure.csproj
│
├── tests/
│   ├── BlackCore.Engine.Tests/
│   ├── BlackCore.Cpp.IntegrationTests/
│   └── BlackCore.CSharp.IntegrationTests/
│
├── samples/
│   ├── cpp/HelloBlackCore/
│   └── csharp/HelloBlackCore/
│
├── tools/
│   └── README.md
│
├── docs/
│   ├── ARCHITECTURE.md
│   ├── BUILD-PIPELINE.md
│   ├── SECURITY.md
│   └── DECISIONS.md
│
├── .gitignore
├── global.json
├── ENVIRONMENT-REPORT.md
└── README.md
```

Projeler gereksiz yere birbirine bağlanmayacaktır. `BlackCore.Engine`,
Clang veya Roslyn'ın somut sınıflarını doğrudan kullanmayacak;
soyutlamalar üzerinden çalışacaktır.

# 6. Temel arayüzler ve veri modelleri

İlk olarak aşağıdaki sözleşmeler oluşturulacaktır. İsimler ve
sorumluluklar korunmalıdır. Gerektiğinde metot imzaları uygulama
sırasında gerekçeli biçimde genişletilebilir.

## 6.1. Dil frontend'i

``` csharp
public interface ILanguageFrontend
{
    string LanguageId { get; }

    Task<AnalysisResult> AnalyzeAsync(
        BuildRequest request,
        CancellationToken cancellationToken);
}
```

## 6.2. Derleme backend'i

``` csharp
public interface ICompilerBackend
{
    string BackendId { get; }

    Task<CompilationResult> CompileAsync(
        BuildRequest request,
        AnalysisResult analysis,
        CancellationToken cancellationToken);
}
```

## 6.3. Koruma sağlayıcısı

``` csharp
public interface IProtectionProvider
{
    string ProviderId { get; }

    Task<ProtectionResult> ProtectAsync(
        ProtectionRequest request,
        CancellationToken cancellationToken);
}
```

Koruma kapalıysa bu arayüz çağrılmayacaktır. Koruma sağlayıcısı
bulunamazsa motor sessizce başka bir işlem yapmayacak; açık hata
üretecektir.

## 6.4. Araç çalıştırma soyutlaması

``` csharp
public interface IExternalToolRunner
{
    Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken);
}
```

Harici araçlar doğrudan rastgele shell komutları birleştirilerek
çalıştırılmayacaktır. Executable yolu, argümanlar, çalışma dizini,
timeout ve ortam değişkenleri ayrı alanlarda tutulacaktır.

## 6.5. Derleme isteği

`BuildRequest` en az şu bilgileri taşımalıdır:

-   Kaynak proje/dosya yolu.
-   Dil.
-   Hedef işletim sistemi.
-   Hedef mimari.
-   Yapılandırma: Debug veya Release.
-   Optimizasyon seviyesi.
-   Çıktı dizini.
-   Koruma etkin mi?
-   Kullanılacak koruma sağlayıcısı.
-   İptal ve timeout bilgileri.

## 6.6. Sonuç modelleri

`AnalysisResult`, `CompilationResult`, `ProtectionResult`,
`ProcessResult` ve `BuildReport` birbirinden ayrılmalıdır.

Her sonuçta mümkün olduğunca şunlar bulunmalıdır:

-   Başarı durumu.
-   Hata kodu.
-   İnsan tarafından okunabilir mesaj.
-   Standart çıktı.
-   Standart hata çıktısı.
-   Başlangıç/bitiş zamanı.
-   Süre.
-   Üretilen dosyalar.
-   Araç sürümü.

Başarısız bir aşama sonraki aşamaları durdurmalıdır. Başarısız
derlemenin eski bir EXE'yi yanlışlıkla başarılı çıktı olarak
göstermesine izin verilmemelidir.

# 7. Pipeline tasarımı

Pipeline aşağıdaki sıralamayı izlemelidir:

1.  Yapılandırmayı yükle ve doğrula.
2.  Kaynak dosyaların varlığını ve erişimini kontrol et.
3.  Çalışma klasörünü oluştur.
4.  Araç zincirlerini ve sürümlerini kontrol et.
5.  Dil frontend'i ile analiz yap.
6.  Analiz politikasını uygula.
7.  Derleme backend'ini çalıştır.
8.  Derleme çıktısını doğrula.
9.  Koruma etkinse koruma sağlayıcısını çalıştır.
10. Koruma sonrası dosyayı yeniden doğrula.
11. Son artefaktı hedef klasöre atomik olarak aktar.
12. Build report oluştur.

Her aşama ayrı bir pipeline step olmalıdır. Pipeline adımları test
edilebilir ve loglanabilir olmalıdır.

# 8. C++ / LLVM backend ayrıntıları

## 8.1. Araçlar

MVP için gerekli araçlar:

-   `clang++`
-   `opt`
-   `llvm-dis`
-   `llvm-as` (gerektiğinde)
-   LLVM/Clang linker entegrasyonu
-   Windows için MSVC linker ve Windows SDK

Tüm LLVM araçları aynı ana LLVM dağıtımından gelmelidir. `clang++`,
`opt` ve diğer LLVM araçlarının sürüm uyumsuzluğu build başlamadan
tespit edilmelidir.

Windows native derleme için MSVC Build Tools ve Windows SDK
gereksinimleri kontrol edilmelidir. Gerekirse Visual Studio Developer
Command Prompt ortamı kullanılmalıdır.

## 8.2. İlk derleme adımları

Örnek komutlar kavramsal referanstır. Agent, gerçek kurulu araç
sürümleriyle komutları doğrulamalı ve argümanları
`ProcessStartInfo.ArgumentList` ile ayrı ayrı geçmelidir.

``` powershell
# LLVM bitcode üret
clang++ -std=c++20 -O0 -emit-llvm -c main.cpp -o main.bc

# Bitcode'u okunabilir LLVM IR'ye dönüştür
llvm-dis main.bc -o main.ll

# IR doğrula ve optimize et
opt -passes="verify,default<O2>,verify" main.bc -o optimized.bc

# Windows native executable üret
# Optimizasyon zaten opt aşamasında yapıldı; link aşamasında tekrar
# optimizasyon uygulanmaz (çift optimizasyon ve rapor tutarsızlığını önler).
clang++ -O0 optimized.bc -o BlackCoreDemo.exe
```

Bu komutlar Windows ortamında MSVC toolchain, Windows SDK ve C++
standart kütüphanesi yapılandırmasına bağlıdır. Gerekli ortam
bulunmuyorsa motor anlaşılır bir ön kontrol hatası vermelidir.

## 8.3. C++ doğrulama kuralları

-   Clang parse/compile hataları build'i durdurur.
-   LLVM verifier hataları build'i durdurur.
-   Optimizasyon hataları build'i durdurur.
-   Linker hataları build'i durdurur.
-   Üretilen dosya PE formatında değilse build başarısız sayılır.
-   Mimari hedeflenen AMD64 değilse build başarısız sayılır.
-   Çıktı dosyası sıfır byte ise build başarısız sayılır.
-   Debug sembolleri ve ara dosyalar release çıktısına yanlışlıkla
    kopyalanmamalıdır.

## 8.4. Optimizasyon profilleri

MVP'de aşağıdaki profiller bulunmalıdır:

  Profil   Amaç
  -------- ---------------------------------------
  None     Optimizasyonsuz hata ayıklama
  O1       Hafif optimizasyon
  O2       Genel release optimizasyonu
  O3       Daha agresif performans optimizasyonu
  Os       Boyut odaklı optimizasyon
  Oz       Daha agresif boyut optimizasyonu

Kullanıcı profili seçebilir. Motor kullanılan gerçek LLVM argümanlarını
rapora yazmalıdır.

# 9. C# / Roslyn / NativeAOT backend ayrıntıları

## 9.1. Analiz

Roslyn üzerinden:

-   Syntax ve semantic diagnostics.
-   Derleyici hataları.
-   Nullable referans tipi uyarıları.
-   Yapılandırılmış analyzer kuralları.
-   Desteklenmeyen veya NativeAOT açısından riskli kullanım kontrolleri.

Roslyn analizi tek başına NativeAOT uyumluluğunu kanıtlamaz. Son karar
NativeAOT publish aşaması ve gerçek çalışma testleriyle verilir.

## 9.2. NativeAOT

C# için ilk sürümde `dotnet publish` NativeAOT özellikleriyle
kullanılmalıdır.

Örnek:

``` powershell
dotnet publish MyApp.csproj `
  -c Release `
  -r win-x64 `
  -p:PublishAot=true
```

NativeAOT gereksinimleri ve desteklenen özellikler kullanılan .NET SDK
sürümüne göre doğrulanmalıdır. Reflection, dinamik kod üretimi, COM,
P/Invoke ve trimming ile ilgili kısıtlar raporlanmalıdır.

MVP'de yalnızca desteklenen Windows x64 hedefi kabul edilir.

# 10. Native artefakt doğrulama

Üretilen EXE yalnızca dosyanın varlığına bakılarak başarılı kabul
edilmemelidir.

Asgari kontroller:

1.  Dosya mevcut mu?
2.  Dosya boyutu sıfırdan büyük mü?
3.  PE başlığı okunabiliyor mu?
4.  PE machine alanı AMD64 mü?
5.  Dosya beklenen çıktı adıyla mı üretildi?
6.  Beklenen runtime bağımlılıkları mevcut mu?
7.  Kontrollü smoke test başarılı mı?

Smoke test, derlenen programı sınırlı süre ve kontrollü ortamda
çalıştırmalıdır. Timeout, exit code, stdout ve stderr kaydedilmelidir.

Test edilen uygulamalar güvenilmeyen kaynak kod içerebileceğinden,
mümkün olduğunda ayrı kullanıcı hesabı veya izole test ortamı
kullanılmalıdır. BlackCore, güvenilmeyen kodu otomatik olarak güvenli
hâle getirdiğini iddia etmemelidir.

# 11. Koruma katmanı

Koruma, derleme backend'inin sorumluluğu değildir.

## 11.1. Çalışma sırası

``` text
Native EXE
   ↓
Pre-protection validation
   ↓
Configured protection provider
   ↓
Protected EXE
   ↓
Post-protection validation
   ↓
Final output
```

## 11.2. Sağlayıcı kuralları

-   Sağlayıcı harici bir uygulama olabilir.
-   Program yolu yapılandırmadan alınmalıdır.
-   Argümanlar sağlayıcıya özel adapter içinde oluşturulmalıdır.
-   Kullanıcı lisansı veya aktivasyonu motor tarafından
    atlatılmamalıdır.
-   Sağlayıcının başarılı çıkış kodu tek başına yeterli değildir.
-   Çıktı dosyası ayrıca doğrulanmalıdır.
-   Sağlayıcı bulunamazsa koruma adımı başarısız olmalıdır.
-   Koruma kapalıysa koruma aracı çalıştırılmamalıdır.

Themida entegrasyonu ancak resmi/kurulu sürümün belgelenmiş komut satırı
arayüzü doğrulandıktan sonra uygulanmalıdır. Komut satırı parametreleri
tahmin edilmemelidir.

Koruma; tersine mühendisliği zorlaştırabilir ancak kaynak kodun veya
program davranışının çözülemeyeceğini garanti etmez.

# 12. Yapılandırma dosyası

Örnek `blackcore.json`:

``` json
{
  "version": 1,
  "target": {
    "operatingSystem": "windows",
    "architecture": "x64"
  },
  "build": {
    "configuration": "Release",
    "optimization": "O2",
    "outputDirectory": "artifacts"
  },
  "toolchains": {
    "llvmDirectory": "C:/Program Files/LLVM",
    "dotnetExecutable": "dotnet"
  },
  "analysis": {
    "failOnWarnings": false,
    "runStaticAnalysis": true
  },
  "protection": {
    "enabled": false,
    "provider": null
  },
  "execution": {
    "toolTimeoutSeconds": 300,
    "smokeTestTimeoutSeconds": 15
  }
}
```

Yapılandırma modeli versiyonlanmalıdır. Bilinmeyen kritik ayarlar
sessizce yok sayılmamalı; doğrulama hatası verilmelidir. Hassas bilgiler
JSON içine düz metin olarak yazılmamalıdır.

# 13. CLI komut tasarımı

İlk sürümde hedef komutlar:

``` powershell
blackcore info
blackcore doctor
blackcore analyze --project samples/cpp/HelloBlackCore
blackcore build --project samples/cpp/HelloBlackCore
blackcore build --project samples/csharp/HelloBlackCore
blackcore validate --file artifacts/BlackCoreDemo.exe
```

Komutların gerçek parametreleri uygulama sırasında CLI yardım çıktısıyla
belgelenmelidir.

`doctor` komutu:

-   .NET SDK sürümünü kontrol eder.
-   LLVM araçlarını bulur.
-   LLVM araçlarının sürüm uyumunu kontrol eder.
-   MSVC Build Tools varlığını kontrol eder.
-   Windows SDK varlığını kontrol eder.
-   Gerekli çalışma dizinlerinin yazılabilirliğini kontrol eder.
-   Eksik gereksinimleri tek tek raporlar.

# 14. Build çıktıları ve raporlama

Her build için benzersiz bir çalışma dizini oluşturulmalıdır:

``` text
artifacts/
└── 2026-11-07_150000_ABC123/
    ├── workspace/
    ├── intermediate/
    │   ├── source.bc
    │   ├── source.ll
    │   └── optimized.bc
    ├── logs/
    │   └── build.log
    ├── reports/
    │   └── build-report.json
    └── output/
        └── BlackCoreDemo.exe
```

Build raporu en az şunları içermelidir:

-   Build ID.
-   Başlangıç ve bitiş zamanı.
-   Kaynak dosya/proje.
-   Dil ve hedef mimari.
-   Kullanılan araçların sürümleri.
-   Analiz sonuçları.
-   Derleme aşamalarının sonuçları ve süreleri.
-   Uygulanan optimizasyon profili.
-   Koruma etkinliği ve sağlayıcı bilgisi.
-   Son çıktı yolu, boyutu ve SHA-256 özeti.
-   Hata ve uyarılar.

# 15. Hata yönetimi ve güvenilirlik

-   Harici süreçler timeout desteklemelidir.
-   İptal token'ı tüm pipeline boyunca taşınmalıdır.
-   Süreç iptalinde alt süreçler de kontrollü biçimde
    sonlandırılmalıdır.
-   stdout ve stderr ayrı okunmalıdır.
-   Büyük çıktılar belleğe sınırsız yüklenmemelidir.
-   Dosya yolları normalize edilmeli ve doğrulanmalıdır.
-   Kullanıcı girdileri shell komutuna birleştirilmemelidir.
-   Çıktı dosyası geçici konuma yazılıp doğrulandıktan sonra nihai
    konuma taşınmalıdır.
-   Aynı build klasörüne eşzamanlı iki işlem yazmamalıdır.
-   Eski başarılı çıktı yeni başarısız build'in sonucu gibi
    gösterilmemelidir.
-   Hata mesajları teknik ayrıntı içermeli ancak gizli ortam bilgilerini
    sızdırmamalıdır.

# 16. Test stratejisi

## 16.1. Unit test

-   Yapılandırma doğrulama.
-   BuildRequest doğrulama.
-   Pipeline sıralaması.
-   Hata durumunda pipeline durması.
-   Timeout ve iptal davranışı.
-   Çıktı klasörü izolasyonu.
-   Build report oluşturma.
-   Koruma kapalıyken provider çağrılmaması.

## 16.2. C++ entegrasyon testleri

-   Geçerli Hello World derlenmeli.
-   Geçersiz C++ kodu derlenmemeli.
-   LLVM verifier hatası yakalanmalı.
-   Optimize bitcode üretilebilmeli.
-   Windows x64 PE üretilebilmeli.
-   Üretilen EXE çalıştırılabilmeli.
-   Çıktı mimarisi AMD64 olarak doğrulanmalı.

## 16.3. C# entegrasyon testleri

-   Geçerli proje NativeAOT ile yayımlanmalı.
-   Derleme hatası doğru raporlanmalı.
-   NativeAOT uyumsuzluğu hata/uyarı olarak görünmeli.
-   Üretilen EXE doğrulanmalı ve çalıştırılmalı.

## 16.4. Koruma testleri

Gerçek protector kurulu değilse sahte bir test provider kullanılabilir.
Bu provider yalnızca test akışını doğrular; gerçek koruma yaptığı iddia
edilmez.

-   Provider başarılı çıktı ürettiğinde post-validation çalışmalı.
-   Provider hata verdiğinde build başarısız olmalı.
-   Provider çıktı üretmezse build başarısız olmalı.
-   Koruma kapalıysa provider çağrılmamalı.

# 17. Geliştirme aşamaları ve tamamlanma kriterleri

## Aşama 0 --- Geliştirme ortamı

-   [ ] .NET 10 SDK doğrulandı.
-   [ ] Visual Studio ve C# geliştirme ortamı hazır.
-   [ ] LLVM/Clang kuruldu.
-   [ ] `clang++`, `opt`, `llvm-dis` sürümleri doğrulandı.
-   [ ] MSVC Build Tools ve Windows SDK doğrulandı.
-   [ ] `blackcore doctor` için gereksinimler belgelendi.

**Çıkış kriteri:** Geliştirme makinesi tüm araçları otomatik bulabiliyor
ve eksikleri açıkça raporlayabiliyor.

## Aşama 1 --- Engine çekirdeği

-   [ ] Solution ve projeler oluşturuldu.
-   [ ] Temel modeller ve arayüzler eklendi.
-   [ ] External process runner geliştirildi.
-   [ ] Workspace yönetimi geliştirildi.
-   [ ] Logging ve BuildReport eklendi.
-   [ ] Unit testler yazıldı.

**Çıkış kriteri:** Harici araç kullanmadan sahte/test backend'i ile
pipeline baştan sona test edilebiliyor.

## Aşama 2 --- İlk C++ / LLVM derlemesi

-   [ ] Clang frontend çalışıyor.
-   [ ] Bitcode üretiliyor.
-   [ ] LLVM IR görüntülenebiliyor.
-   [ ] LLVM verifier çalışıyor.
-   [ ] Optimizasyon çalışıyor.
-   [ ] Native EXE link ediliyor.
-   [ ] PE ve AMD64 doğrulanıyor.
-   [ ] Smoke test başarılı.

**Çıkış kriteri:** Örnek C++ uygulaması BlackCore CLI ile derleniyor ve
üretilen EXE beklenen çıktıyı veriyor.

## Aşama 3 --- C# / NativeAOT

-   [ ] Roslyn analizi çalışıyor.
-   [ ] Analyzer raporu üretiliyor.
-   [ ] NativeAOT publish çalışıyor.
-   [ ] Native EXE doğrulanıyor.
-   [ ] Smoke test başarılı.

**Çıkış kriteri:** Örnek C# uygulaması aynı Engine üzerinden ayrı
backend ile native yayımlanabiliyor.

## Aşama 4 --- Koruma adaptörü

-   [ ] Provider arayüzü tamamlandı.
-   [ ] Test provider ile entegrasyon test edildi.
-   [ ] Harici araç çalıştırma ve timeout yönetimi tamamlandı.
-   [ ] Koruma sonrası doğrulama çalışıyor.
-   [ ] Gerçek protector entegrasyonu yalnızca doğrulanmış resmi
    parametrelerle yapıldı.

**Çıkış kriteri:** Koruma açık/kapalı akışları güvenilir ve test
edilebilir.

## Aşama 5 --- MVP yayın adayı

-   [ ] Tüm testler geçiyor.
-   [ ] README ve kullanım örnekleri tamamlandı.
-   [ ] Hata kodları belgelendi.
-   [ ] Temiz makinede kurulum testi yapıldı.
-   [ ] Release build tekrarlanabilir.
-   [ ] Build raporu eksiksiz.

**Çıkış kriteri:** Başka bir Windows x64 geliştirme makinesinde
dokümantasyona göre kurulup örnek projeleri derleyebiliyor.

# 18. Agent çalışma talimatları

Bu belgeyi uygulayacak kodlama agent'ı aşağıdaki kurallara uymalıdır.

1.  Önce mevcut repository'yi incele. Dosya veya proje varsa üzerine
    yazmadan önce içeriğini değerlendir.
2.  Bu şartnameyi ana gereksinim kaynağı kabul et.
3.  İlk iş olarak Aşama 0 için ortamı denetle; bulunmayan araçları
    varmış gibi kabul etme.
4.  Bir aşamanın tamamlanma kriteri karşılanmadan sonraki aşamaya geçme.
5.  Her görevde önce yapılacak değişiklikleri kısa bir planla açıkla,
    sonra uygula.
6.  Yalnızca gerçekten oluşturduğun veya değiştirdiğin dosyaları
    raporla.
7.  Derleme ve test komutlarını gerçekten çalıştır. Çalıştırılamayan
    testleri başarılı gösterme.
8.  Hata aldığında hatayı gizleme; kök nedeni belirle ve düzelt.
9.  Var olmayan API, CLI parametresi veya kütüphane özelliği uydurma.
    Resmî dokümantasyonu veya kurulu aracın yardım çıktısını kontrol et.
10. Paket sürümlerini sabitle ve uyumluluklarını doğrula.
11. Harici süreçleri güvenli argüman listesiyle çalıştır; shell string
    birleştirmesi yapma.
12. Kaynak kodu analiz edilmeden release derlemesine geçme.
13. Başarısız build sonrasında eski EXE'yi yeni çıktı gibi sunma.
14. Her anlamlı aşama sonunda testleri çalıştır ve sonucu raporla.
15. Şartname değişikliği gerekiyorsa önce gerekçeyi ve etkiyi açıkla;
    sessizce kapsam değiştirme.
16. Gereksiz soyutlama ve erken optimizasyondan kaçın; ancak tanımlanan
    modüler sınırları koru.
17. Kullanıcı onayı olmadan lisans, ücretli araç veya dış servis satın
    alma/etkinleştirme işlemi yapma.
18. Güvenlik ve koruma özelliklerini olduğundan güçlü gösterme.

## Agent'ın her görev sonunda vereceği rapor

``` text
AŞAMA:
TAMAMLANAN:
OLUŞTURULAN / DEĞİŞTİRİLEN DOSYALAR:
ÇALIŞTIRILAN KOMUTLAR:
TEST SONUÇLARI:
KALAN SORUNLAR:
SONRAKİ ADIM:
```

# 19. Agent için ilk görev

**İlk görev: Yalnızca Aşama 0 --- Geliştirme ortamı denetimi.**

Bu ilk görevde henüz uygulama kodu yazma.

Yapılacaklar:

1.  İşletim sistemi ve işlemci mimarisini tespit et.
2.  .NET SDK sürümünü tespit et.
3.  Visual Studio / MSVC Build Tools kurulumunu tespit et.
4.  Windows SDK sürümünü tespit et.
5.  `clang++`, `opt`, `llvm-dis` araçlarını bul.
6.  Araçların sürümlerini karşılaştır.
7.  Basit bir C++ dosyasını LLVM bitcode'a dönüştürmeyi dene.
8.  Bitcode doğrulama ve IR disassembly denemesi yap.
9.  MSVC linker ve Windows SDK ile native EXE üretiminin mümkün olup
    olmadığını doğrula.
10. Sonuçları repo kökündeki `ENVIRONMENT-REPORT.md` dosyasına yaz.

Eksik bir araç varsa kurulum yapmadan önce kullanıcıya eksikliği ve
gerekli sürüm/kurulum seçeneğini bildir. Kullanıcının mevcut araçlarını
veya PATH ayarlarını izinsiz değiştirme.

**Aşama 0 başarı kriteri:** C++ → LLVM bitcode → verify → optimize →
Windows x64 EXE zincirinin geliştirme makinesinde gerçekten çalıştığı
kanıtlanmış olmalı.

------------------------------------------------------------------------

# 20. Teknik referanslar

Agent, uygulama sırasında sürüme duyarlı konuları resmî dokümantasyondan
doğrulamalıdır:

-   LLVM: https://llvm.org/docs/
-   Clang: https://clang.llvm.org/docs/
-   Roslyn: https://github.com/dotnet/roslyn
-   .NET NativeAOT:
    https://learn.microsoft.com/dotnet/core/deploying/native-aot/
-   .NET SDK: https://dotnet.microsoft.com/download
-   Microsoft C++ Build Tools:
    https://visualstudio.microsoft.com/visual-cpp-build-tools/

Dokümantasyon ile kurulu araç sürümü çelişirse kurulu sürümün gerçek
davranışı test edilmeli ve uyumluluk notu eklenmelidir.

------------------------------------------------------------------------

# 21. Projenin temel ilkesi

BlackCore Engine'in başarısı yalnızca EXE üretmesiyle ölçülmeyecektir.

Başarılı bir BlackCore build'i:

-   Hangi kaynak koddan üretildiği bilinen,
-   Hangi araç ve sürümlerin kullanıldığı kayıtlı,
-   Analiz ve doğrulama aşamalarından geçmiş,
-   Hedef mimarisi doğrulanmış,
-   Kontrollü çalışma testi yapılmış,
-   İsteğe bağlı koruma sonrası yeniden doğrulanmış,
-   Tekil build raporu bulunan

bir artefakt üretmelidir.

**Öncelik sırası: Doğruluk → Tekrarlanabilirlik → Güvenilirlik →
Performans → Koruma.**
