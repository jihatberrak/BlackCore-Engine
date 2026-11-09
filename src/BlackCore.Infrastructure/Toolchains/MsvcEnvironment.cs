using System.Runtime.Versioning;
using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Models;
using Microsoft.Win32;

namespace BlackCore.Infrastructure.Toolchains;

/// <summary>
/// MSVC ve Windows SDK'yı bulur. Clang'in MSVC hedefi <c>VCToolsInstallDir</c> ve SDK değişkenlerinden
/// başlık ve kütüphane yollarını kendisi çözer; biz yalnızca doğru sürümü sabitleyip bu değişkenleri veririz.
/// Ortam yalnızca alt süreçlere verilir; kullanıcının PATH/INCLUDE/LIB değerleri değiştirilmez.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MsvcEnvironment
{
    private static readonly TimeSpan VsWhereTimeout = TimeSpan.FromSeconds(60);

    public static async Task<IReadOnlyDictionary<string, string>> DiscoverAsync(
        IExternalToolRunner runner,
        CancellationToken cancellationToken)
    {
        var installation = await FindVisualStudioAsync(runner, cancellationToken);

        var msvcRoot = Path.Combine(installation, "VC", "Tools", "MSVC");
        var msvcVersion = HighestVersionDirectory(msvcRoot)
            ?? throw new BlackCoreException(ErrorCode.ToolNotFound, $"MSVC araç seti bulunamadı: {msvcRoot}");
        var msvcDir = Path.Combine(msvcRoot, msvcVersion);

        var kitsRoot = FindWindowsKitsRoot()
            ?? throw new BlackCoreException(ErrorCode.ToolNotFound, "Windows SDK kök dizini bulunamadı.");
        var sdkVersion = HighestVersionDirectory(Path.Combine(kitsRoot, "Include"))
            ?? throw new BlackCoreException(ErrorCode.ToolNotFound, "Windows SDK include sürümü bulunamadı.");

        var required = new[]
        {
            Path.Combine(msvcDir, "include"),
            Path.Combine(msvcDir, "lib", "x64"),
            Path.Combine(kitsRoot, "Include", sdkVersion, "ucrt"),
            Path.Combine(kitsRoot, "Lib", sdkVersion, "um", "x64"),
        };

        foreach (var dir in required)
        {
            if (!Directory.Exists(dir))
            {
                throw new BlackCoreException(ErrorCode.ToolNotFound, $"Beklenen MSVC/SDK klasörü yok: {dir}");
            }
        }

        var libDirectories = new[]
        {
            Path.Combine(msvcDir, "lib", "x64"),
            Path.Combine(kitsRoot, "Lib", sdkVersion, "ucrt", "x64"),
            Path.Combine(kitsRoot, "Lib", sdkVersion, "um", "x64"),
        };

        foreach (var dir in libDirectories)
        {
            if (!Directory.Exists(dir))
            {
                throw new BlackCoreException(ErrorCode.ToolNotFound, $"Beklenen kütüphane klasörü yok: {dir}");
            }
        }

        return new Dictionary<string, string>
        {
            ["VCToolsInstallDir"] = msvcDir + Path.DirectorySeparatorChar,
            ["WindowsSdkDir"] = kitsRoot + Path.DirectorySeparatorChar,
            ["WindowsSDKVersion"] = sdkVersion + Path.DirectorySeparatorChar,
            ["UCRTVersion"] = sdkVersion,
            // Miras alınan INCLUDE değerleri (ör. VC98 başlıkları) clang'i bozar; boşaltılır.
            // LIB ise açıkça verilir: clang LIB tanımlıysa kütüphane yollarını yalnızca ondan alır.
            ["INCLUDE"] = string.Empty,
            ["LIB"] = string.Join(';', libDirectories),
        };
    }

    /// <summary>
    /// NativeAOT bağlama adımı vswhere.exe'yi PATH üzerinden arar; Installer klasörü PATH'te olmayabilir.
    /// Yalnızca alt süreç için bu klasör PATH'in başına eklenir.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ToolPathOverride()
    {
        var installer = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer");
        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return new Dictionary<string, string> { ["PATH"] = installer + ";" + current };
    }

    private static async Task<string> FindVisualStudioAsync(IExternalToolRunner runner, CancellationToken ct)
    {
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");

        if (!File.Exists(vswhere))
        {
            throw new BlackCoreException(ErrorCode.ToolNotFound, "vswhere.exe bulunamadı; Visual Studio / Build Tools kurulu değil.");
        }

        var result = await runner.RunAsync(
            new ProcessRequest(
                vswhere,
                new[]
                {
                    "-latest", "-products", "*",
                    "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
                    "-property", "installationPath",
                },
                VsWhereTimeout),
            ct);

        var path = result.StandardOutput.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (!result.Succeeded || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            throw new BlackCoreException(ErrorCode.ToolNotFound, "C++ araç seti (VC Tools) içeren bir Visual Studio kurulumu bulunamadı.");
        }

        return path;
    }

    private static string? FindWindowsKitsRoot()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Kits\Installed Roots");
        var root = key?.GetValue("KitsRoot10") as string;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10");
        }

        root = root.TrimEnd('\\');
        return Directory.Exists(root) ? root : null;
    }

    private static string? HighestVersionDirectory(string parent)
    {
        if (!Directory.Exists(parent))
        {
            return null;
        }

        return Directory.GetDirectories(parent)
            .Select(Path.GetFileName)
            .Where(name => name is not null && Version.TryParse(name, out _))
            .OrderByDescending(name => Version.Parse(name!))
            .FirstOrDefault();
    }
}
