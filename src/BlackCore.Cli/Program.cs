using BlackCore.Cli;
using BlackCore.Backend.LLVM;
using BlackCore.Backend.NativeAot;
using BlackCore.Frontend.CSharp;
using BlackCore.Protection;
using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Artifacts;
using BlackCore.Engine.Configuration;
using BlackCore.Engine.Models;
using BlackCore.Engine.Pipeline;
using BlackCore.Frontend.Cpp;
using BlackCore.Infrastructure.Processes;
using BlackCore.Infrastructure.Toolchains;

const int ExitSuccess = 0;
const int ExitFailure = 1;
const int ExitUsage = 2;

if (args.Length == 0)
{
    PrintUsage();
    return ExitUsage;
}

var command = args[0].ToLowerInvariant();
var options = ParseOptions(args.Skip(1).ToArray());
if (options is null)
{
    PrintUsage();
    return ExitUsage;
}

switch (command)
{
    case "info":
        return Info();

    case "build":
        return await BuildAsync(options, CancellationToken.None);

    case "validate":
        return Validate(options);

    case "doctor":
        return await DoctorAsync(options);

    default:
        PrintUsage();
        return ExitUsage;
}

static int Info()
{
    Console.WriteLine("BlackCore Engine — MVP (Aşama 2)");
    Console.WriteLine("  Hedef: Windows x64 (AMD64)");
    Console.WriteLine("  C++:   Clang frontend → LLVM bitcode → opt → lld-link (çalışır)");
    Console.WriteLine("  C#:    Roslyn analizi → .NET NativeAOT publish (win-x64)");
    Console.WriteLine("  Koruma: sağlayıcı arayüzü hazır; kayıtlı sağlayıcı yok (varsayılan kapalı)");
    return ExitSuccess;
}

static async Task<int> BuildAsync(Dictionary<string, string> options, CancellationToken cancellationToken)
{
    if (!options.TryGetValue("--project", out var projectArg))
    {
        Console.Error.WriteLine("Eksik argüman: --project <klasör|dosya>");
        return ExitUsage;
    }

    var project = Path.GetFullPath(projectArg);
    if (!File.Exists(project) && !Directory.Exists(project))
    {
        Console.Error.WriteLine($"Proje bulunamadı: {project}");
        return ExitFailure;
    }

    var configPath = options.GetValueOrDefault("--config", "blackcore.json");
    BlackCoreConfig config;
    try
    {
        if (File.Exists(configPath))
        {
            config = ConfigLoader.Load(configPath);
        }
        else
        {
            Console.WriteLine($"Uyarı: {configPath} bulunamadı; varsayılan ayarlar kullanılıyor.");
            config = new BlackCoreConfig();
        }
    }
    catch (BlackCoreException ex)
    {
        Console.Error.WriteLine($"HATA [{ex.ErrorCode}] {ex.Message}");
        return ExitFailure;
    }

    var language = DetectLanguage(project);
    if (language is null)
    {
        Console.Error.WriteLine("Dil algılanamadı: klasörde .cpp veya .csproj dosyası bulunmalı.");
        return ExitFailure;
    }

    var runner = new ProcessRunner();
    var toolchains = new CppToolchainProvider(config.Toolchains.LlvmDirectory, runner);
    var engine = new BuildEngine(
        new ILanguageFrontend[] { new CppFrontend(toolchains, runner), new CSharpFrontend() },
        new ICompilerBackend[] { new LlvmBackend(toolchains, runner), new NativeAotBackend(runner, config.Toolchains.DotnetExecutable) },
        // Yalnızca test sağlayıcısı kayıtlıdır; gerçek bir koruma aracı entegre edilmemiştir (şartname 11.2).
        new IProtectionProvider[] { new TestProtectionProvider() },
        runner);

    var projectDirectory = Directory.Exists(project) ? project : Path.GetDirectoryName(project)!;
    var defaultName = (Directory.Exists(project) ? Path.GetFileName(project.TrimEnd(Path.DirectorySeparatorChar)) : Path.GetFileNameWithoutExtension(project)) + ".exe";
    var outputName = options.GetValueOrDefault("--name", defaultName);

    var expectedFile = Path.Combine(projectDirectory, "expected-output.txt");
    var request = new BuildRequest(
        SourcePath: project,
        Language: language.Value,
        TargetOs: TargetOperatingSystem.Windows,
        TargetArch: TargetArchitecture.X64,
        Configuration: ParseEnum<BuildConfiguration>(config.Build.Configuration),
        Optimization: ParseEnum<OptimizationProfile>(config.Build.Optimization),
        ArtifactsRoot: Path.GetFullPath(config.Build.OutputDirectory),
        OutputFileName: outputName,
        ProtectionEnabled: config.Protection.Enabled,
        ProtectionProviderId: config.Protection.Provider,
        Timeout: TimeSpan.FromSeconds(config.Execution.ToolTimeoutSeconds),
        RunSmokeTest: true,
        ExpectedSmokeOutput: File.Exists(expectedFile) ? File.ReadAllText(expectedFile) : null);

    Console.WriteLine($"Derleniyor: {project}");
    var outcome = await engine.BuildAsync(request, config, cancellationToken);

    if (outcome.Succeeded)
    {
        Console.WriteLine($"BAŞARILI: {outcome.OutputPath}");
    }
    else
    {
        Console.Error.WriteLine($"BAŞARISIZ [{outcome.ErrorCode}] {outcome.Message}");
    }

    if (outcome.ReportPath is not null)
    {
        Console.WriteLine($"Rapor: {outcome.ReportPath}");
    }

    return outcome.Succeeded ? ExitSuccess : ExitFailure;
}

static async Task<int> DoctorAsync(Dictionary<string, string> options)
{
    var configPath = options.GetValueOrDefault("--config", "blackcore.json");
    BlackCoreConfig config;
    try
    {
        config = File.Exists(configPath) ? ConfigLoader.Load(configPath) : new BlackCoreConfig();
    }
    catch (BlackCoreException ex)
    {
        Console.Error.WriteLine($"HATA [{ex.ErrorCode}] {ex.Message}");
        return ExitFailure;
    }

    var ok = await Doctor.RunAsync(config, CancellationToken.None);
    return ok ? ExitSuccess : ExitFailure;
}

static int Validate(Dictionary<string, string> options)
{
    if (!options.TryGetValue("--file", out var file))
    {
        Console.Error.WriteLine("Eksik argüman: --file <exe>");
        return ExitUsage;
    }

    var result = ArtifactValidator.ValidatePortableExecutable(Path.GetFullPath(file), TargetArchitecture.X64);
    Console.WriteLine(result.Valid ? $"GEÇERLİ: {result.Message} ({result.SizeBytes} bayt)" : $"GEÇERSİZ: {result.Message}");
    return result.Valid ? ExitSuccess : ExitFailure;
}

static SourceLanguage? DetectLanguage(string project)
{
    if (File.Exists(project))
    {
        var extension = Path.GetExtension(project).ToLowerInvariant();
        return extension switch
        {
            ".cpp" or ".cc" or ".cxx" => SourceLanguage.Cpp,
            ".csproj" => SourceLanguage.CSharp,
            _ => null,
        };
    }

    if (Directory.GetFiles(project, "*.cpp").Length > 0)
    {
        return SourceLanguage.Cpp;
    }

    if (Directory.GetFiles(project, "*.csproj").Length > 0)
    {
        return SourceLanguage.CSharp;
    }

    return null;
}

static T ParseEnum<T>(string value) where T : struct, Enum =>
    Enum.TryParse<T>(value, ignoreCase: true, out var parsed)
        ? parsed
        : throw new BlackCoreException(ErrorCode.ConfigInvalid, $"Geçersiz değer: {value}");

static Dictionary<string, string>? ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i += 2)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
        {
            return null;
        }

        result[args[i]] = args[i + 1];
    }

    return result;
}

static void PrintUsage()
{
    Console.WriteLine("Kullanım:");
    Console.WriteLine("  blackcore info");
    Console.WriteLine("  blackcore build --project <klasör|dosya> [--config blackcore.json] [--name HelloBlackCore.exe]");
    Console.WriteLine("  blackcore validate --file <exe>");
    Console.WriteLine("  blackcore doctor [--config blackcore.json]");
}
