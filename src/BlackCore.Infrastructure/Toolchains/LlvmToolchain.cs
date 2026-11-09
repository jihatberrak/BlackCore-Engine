using System.Text.RegularExpressions;
using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Models;

namespace BlackCore.Infrastructure.Toolchains;

/// <summary>Doğrulanmış LLVM araç seti. Tüm araçlar aynı sürümde olmak zorundadır (şartname 8.1).</summary>
public sealed record LlvmToolchain(
    string Root,
    string Clang,
    string Opt,
    string LlvmDis,
    string LldLink,
    string Version);

/// <summary>LLVM araçlarını bulur ve sürüm uyumunu kontrol eder.</summary>
public static partial class LlvmToolchainLocator
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] RequiredTools = { "clang++.exe", "opt.exe", "llvm-dis.exe", "lld-link.exe" };

    public static async Task<LlvmToolchain> ResolveAsync(
        string? configuredRoot,
        IExternalToolRunner runner,
        CancellationToken cancellationToken)
    {
        var binDirectory = FindBinDirectory(configuredRoot)
            ?? throw new BlackCoreException(
                ErrorCode.ToolNotFound,
                "clang++ bulunamadı. blackcore.json içinde toolchains.llvmDirectory ayarlayın veya LLVM'i PATH'e ekleyin.");

        var missing = RequiredTools.Where(t => !File.Exists(Path.Combine(binDirectory, t))).ToList();
        if (missing.Count > 0)
        {
            throw new BlackCoreException(
                ErrorCode.ToolNotFound,
                $"LLVM dağıtımında eksik araçlar: {string.Join(", ", missing)} ({binDirectory}).");
        }

        var clang = Path.Combine(binDirectory, "clang++.exe");
        var opt = Path.Combine(binDirectory, "opt.exe");
        var llvmDis = Path.Combine(binDirectory, "llvm-dis.exe");
        var lldLink = Path.Combine(binDirectory, "lld-link.exe");

        var versions = new Dictionary<string, string>();
        foreach (var tool in new[] { clang, opt, llvmDis, lldLink })
        {
            versions[Path.GetFileName(tool)] = await ProbeVersionAsync(tool, runner, cancellationToken);
        }

        var distinct = versions.Values.Distinct().ToList();
        if (distinct.Count != 1)
        {
            var details = string.Join(", ", versions.Select(kv => $"{kv.Key}={kv.Value}"));
            throw new BlackCoreException(
                ErrorCode.ToolchainMismatch,
                $"LLVM araç sürümleri uyuşmuyor: {details}. Tüm araçlar aynı dağıtımdan olmalı.");
        }

        return new LlvmToolchain(
            Root: Path.GetDirectoryName(binDirectory)!,
            Clang: clang,
            Opt: opt,
            LlvmDis: llvmDis,
            LldLink: lldLink,
            Version: distinct[0]);
    }

    private static async Task<string> ProbeVersionAsync(string tool, IExternalToolRunner runner, CancellationToken ct)
    {
        var result = await runner.RunAsync(new ProcessRequest(tool, new[] { "--version" }, ProbeTimeout), ct);

        if (result.TimedOut)
        {
            throw new BlackCoreException(ErrorCode.ToolTimeout, $"{Path.GetFileName(tool)} --version zaman aşımına uğradı.");
        }

        if (!result.Started || !result.Succeeded)
        {
            throw new BlackCoreException(ErrorCode.ToolFailed, $"{Path.GetFileName(tool)} --version çalıştırılamadı.");
        }

        var match = VersionPattern().Match(result.StandardOutput);
        if (!match.Success)
        {
            throw new BlackCoreException(ErrorCode.ToolFailed, $"{Path.GetFileName(tool)} sürüm bilgisi okunamadı.");
        }

        return match.Value;
    }

    private static string? FindBinDirectory(string? configuredRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            foreach (var candidate in new[] { Path.Combine(configuredRoot, "bin"), configuredRoot })
            {
                if (File.Exists(Path.Combine(candidate, "clang++.exe")))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            return null;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(dir, "clang++.exe")))
                {
                    return Path.GetFullPath(dir);
                }
            }
            catch (ArgumentException)
            {
                // Geçersiz PATH girdisi atlanır.
            }
        }

        return null;
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex VersionPattern();
}
