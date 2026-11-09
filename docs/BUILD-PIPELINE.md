# Derleme pipeline'ı

Pipeline adımları sırayla çalışır. Bir adım başarısız olursa sonraki adımlar çalışmaz ve
hiçbir artefakt yayımlanmaz. Her adımın sonucu `build-report.json` içinde `Stages` listesinde yer alır.

| # | Adım | Ne yapar | Başarısızlıkta |
|---|---|---|---|
| 1 | `ValidateRequest` | İstek alanlarını doğrular (çıktı adı, hedef, timeout, koruma–sağlayıcı tutarlılığı). | `RequestInvalid` |
| 2 | `CheckSource` | Kaynak yolu var mı kontrol eder. | `SourceNotFound` |
| 3 | `CreateWorkspace` | `artifacts/<build-id>/` altında benzersiz klasörleri oluşturur; log dosyasını bağlar. | `WorkspaceFailed` |
| 4 | `Analyze` | C++: Clang `-fsyntax-only`. C#: Roslyn derleme + NativeAOT risk kuralları. Bulgular `analysis-report.json`'a yazılır. | `AnalysisFailed`, `AnalysisPolicyViolation` |
| 5 | `Compile` | C++: `clang++ -emit-llvm` → `llvm-dis` → `opt -passes=verify,default<On>,verify` → `clang++ -fuse-ld=lld-link`. C#: `dotnet publish -p:PublishAot=true -r win-x64`. | `CompilationFailed`, `ToolNotFound`, `ToolTimeout` |
| 6 | `ValidateCompilation` | Aday EXE: var mı, boş değil mi, PE mi, makine tipi AMD64 mi. | `OutputInvalid` |
| 7 | `SmokeTest` | İstenmişse EXE'yi sınırlı süre çalıştırır; çıkış kodunu ve (varsa) beklenen çıktıyı denetler. | `SmokeTestFailed` |
| 8 | `Protection` | Koruma kapalıysa atlanır (sağlayıcı çağrılmaz). Açıksa sağlayıcıyı çalıştırır ve çıktıyı yeniden doğrular. | `ProtectionProviderNotFound`, `ProtectionFailed`, `OutputInvalid` |
| 9 | `Publish` | Son dosyayı geçici ada yazar, doğrular, sonra nihai ada taşır. | `OutputInvalid`, `PublishFailed` |

## Çıktı yapısı

```text
artifacts/<yyyy-MM-dd_HHmmss_XXXXXX>/
├── workspace/       (C# için izole proje kopyası)
├── intermediate/    (.bc, .ll, .opt.bc, candidate.exe / publish/)
├── logs/build.log   (JSON satırları)
├── reports/
│   ├── build-report.json
│   └── analysis-report.json
└── output/<OutputFileName>.exe   (yalnızca başarılı derlemede)
```

Başarısız bir derleme `output/` altında hiçbir EXE bırakmaz; eski bir çıktı da yeni bir başarı gibi gösterilmez.
