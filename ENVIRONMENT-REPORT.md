# ENVIRONMENT-REPORT — Aşama 0 Geliştirme Ortamı Denetimi

**Tarih:** 2026-11-09
**Durum:** Aşama 0 çıkış kriteri KARŞILANDI (C++ → LLVM bitcode → verify → optimize → Windows x64 EXE zinciri çalıştı)

## 1. Sistem

| Öğe | Değer |
|---|---|
| İşletim sistemi | Microsoft Windows 11 Pro 10.0.26200 |
| İşlemci mimarisi | AMD64 (x64) |
| Hedef | Windows x64 / AMD64 — uyumlu |

## 2. Araç denetimi

| Gereksinim | Durum | Ayrıntı |
|---|---|---|
| .NET SDK | Kurulu | 10.0.100 (`C:\Program Files\dotnet\sdk`). Ayrıca 8.0.415, 9.0.316, 10.0.200-preview.0.26103.119 kurulu. |
| .NET varsayılan SDK | Uyarı | `dotnet --version` çıktısı `10.0.200-preview.0.26103.119`. Repo'da `global.json` yok. Aşama 1'de `global.json` ile 10.0.100 sabitlenmeli. |
| clang++ / clang | Kurulu | 22.1.8 — `C:\Users\mavij\tools\llvm-22.1.8\bin` |
| opt | Kurulu | 22.1.8 — aynı dağıtım |
| llvm-dis | Kurulu | 22.1.8 — aynı dağıtım |
| llvm-as | Kurulu | 22.1.8 — aynı dağıtım |
| lld-link | Kurulu | 22.1.8 — aynı dağıtım |
| MSVC cl.exe / link.exe | Kurulu | VS 2022 Enterprise — MSVC 14.44.35207 (`cl` 19.44.35228). `vcvars64.bat` ile çalıştı. VS 18 Insiders — MSVC 14.50.35717 de mevcut. |
| Windows SDK | Kurulu | 10.0.26100.0. `ucrt\x64\ucrt.lib` ve `um\x64\kernel32.lib` mevcut. |
| `C:\Program Files (x86)\...\VC98\bin\LINK.EXE` | Kullanılmamalı | 1998 dönemi linker; PATH'te görünüyor. Derleme MSVC `vcvars64` veya tam yolla yapılmalı. |

## 3. LLVM kurulum notları

- `winget install LLVM.LLVM --version 22.1.8` ile kurulum yapıldı. `C:\Program Files\LLVM\bin` içinde `clang++`, `clang`, `lld-link` var; **`opt`, `llvm-dis`, `llvm-as` yok** (Windows installer'da bulunmuyor).
- Eksik araçlar için aynı sürümün resmi tam arşivi indirildi (`clang+llvm-22.1.8-x86_64-pc-windows-msvc.tar.xz`, SHA-256 `d96c2cc1736f4eb7fa43cb9bbdf56d93551a9ae0a9aadb9c99c3c3b2b712a234`; GitHub release digest ile eşleşti).
- Yönetici yetkisi olmadığı için Program Files'a yazılamadı. Tam `bin` ve `lib/clang` klasörü `C:\Users\mavij\tools\llvm-22.1.8` altına çıkarıldı. Kullanılan tüm araçlar tek dağıtımdan (22.1.8) geliyor.
- PATH değiştirilmedi. Derleme scriptleri araçları tam yolla çağırıyor.

## 4. Zincir doğrulaması (Aşama 0 başarı kriteri)

Test kaynağı: `main.cpp` (`std::cout` ile "BlackCore chain OK"). Çalıştırma: `vcvars64.bat` ortamında, scratchpad altında.

| Adım | Komut (özet) | Sonuç |
|---|---|---|
| Derleme → bitcode | `clang++ -std=c++20 -O0 -emit-llvm -c main.cpp -o main.bc` | Başarılı (78 480 B) |
| IR görüntüleme | `llvm-dis main.bc -o main.ll` | Başarılı (323 041 B) |
| Doğrulama + optimizasyon | `opt -passes="verify,default<O2>,verify" main.bc -o optimized.bc` | Başarılı (78 992 B) |
| Native EXE | `clang++ -O0 optimized.bc -o BlackCoreDemo.exe` | Başarılı (227 840 B) |
| Smoke test | `BlackCoreDemo.exe` | Çıktı: `BlackCore chain OK`, exit code 0 |
| PE imzası | İlk 4 bayt | `PE\0\0` |
| Mimari | PE machine alanı | `0x8664` (AMD64) |
| SHA-256 (EXE) | — | `6FB097670749DC5CD5ED1F0F1DE31E53BEFEE04B3F7137F43119F7CA01D95575` |

Not: Link adımında optimizasyon `-O0` ile yapıldı (şartname 8.2 düzeltmesi): optimizasyon yalnızca `opt` aşamasında uygulandı.

## 5. Kalan notlar

1. **Program Files'taki winget kurulumu eksik.** `opt`/`llvm-dis` yok; Aşama 1 pipeline'ı `tools` yolunu yapılandırmadan (`blackcore.json` → `toolchains.llvmDirectory`) almalı. Kullanıcı yönetici olarak `C:\Program Files\LLVM\bin` içine kopyalamak isterse aynı üç dosya yeterli.
2. **Varsayılan .NET SDK preview.** `global.json` ile 10.0.100 sabitlenmeli (Aşama 1).
3. **VC98 linker PATH'te.** Derleme scriptleri MSVC'yi tam yolla çağırmalı.
4. **Kapsam sınırı.** Bu denetim tek bir Hello World ile yapıldı. Aşama 2'nin "verifier hatası yakalanması" ve "geçersiz C++ reddi" testleri henüz çalıştırılmadı.

## 6. Sonraki adım

Aşama 0 kapatılabilir. Aşama 1 (Engine çekirdeği) için önce şartnamedeki sözleşme eksikliklerinin (AnalysisResult, ProtectionRequest vb.) kararlaştırılması gerekiyor.
