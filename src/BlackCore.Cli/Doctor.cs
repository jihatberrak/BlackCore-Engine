using System.Runtime.Versioning;
using BlackCore.Engine.Configuration;
using BlackCore.Engine.Models;
using BlackCore.Infrastructure.Processes;
using BlackCore.Infrastructure.Toolchains;

namespace BlackCore.Cli;

/// <summary>
/// <c>blackcore doctor</c>: gereksinimleri tek tek kontrol eder ve eksikleri açıkça raporlar (şartname 13).
/// Hiçbir araç kurmaz, PATH veya ayar değiştirmez.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Doctor
{
    public static async Task<bool> RunAsync(BlackCoreConfig config, CancellationToken ct)
    {
        var runner = new ProcessRunner();
        var allOk = true;

        allOk &= await CheckAsync(".NET SDK", async () =>
        {
            var r = await ToolInvocation.RunAsync(runner, config.Toolchains.DotnetExecutable, new[] { "--version" },
                TimeSpan.FromSeconds(60), null, null, ct);
            return ToolInvocation.Classify(r, "dotnet", ErrorCode.ToolFailed) is null
                ? $"sürüm {r.StandardOutput.Trim()}"
                : null;
        });

        allOk &= await CheckAsync("LLVM araçları (clang++, opt, llvm-dis, lld-link)", async () =>
        {
            var llvm = await LlvmToolchainLocator.ResolveAsync(config.Toolchains.LlvmDirectory, runner, ct);
            return $"sürüm {llvm.Version} ({llvm.Root})";
        });

        allOk &= await CheckAsync("MSVC Build Tools ve Windows SDK", async () =>
        {
            var env = await MsvcEnvironment.DiscoverAsync(runner, ct);
            return $"MSVC {Path.GetFileName(env["VCToolsInstallDir"].TrimEnd('\\'))}, SDK {env["WindowsSDKVersion"].TrimEnd('\\')}";
        });

        allOk &= Check("Çalışma klasörü yazılabilir", () =>
        {
            var root = Path.GetFullPath(config.Build.OutputDirectory);
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, ".doctor-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return root;
        });

        Console.WriteLine(allOk ? "Tüm gereksinimler karşılandı." : "Eksik gereksinimler var; yukarıdaki satırlara bakın.");
        return allOk;
    }

    private static async Task<bool> CheckAsync(string name, Func<Task<string?>> probe)
    {
        try
        {
            var detail = await probe();
            if (detail is null)
            {
                Console.WriteLine($"[EKSİK] {name}");
                return false;
            }

            Console.WriteLine($"[TAMAM] {name}: {detail}");
            return true;
        }
        catch (BlackCoreException ex)
        {
            Console.WriteLine($"[EKSİK] {name}: {ex.Message}");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[EKSİK] {name}: {ex.Message}");
            return false;
        }
    }

    private static bool Check(string name, Func<string> probe)
    {
        try
        {
            Console.WriteLine($"[TAMAM] {name}: {probe()}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[EKSİK] {name}: {ex.Message}");
            return false;
        }
    }
}
