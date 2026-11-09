using System.Text.Json;
using BlackCore.Engine.Models;

namespace BlackCore.Engine.Configuration;

public static class ConfigLoader
{
    public const int SupportedVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        // Bilinmeyen (ör. yazım hatalı) ayarlar sessizce yok sayılmaz.
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        // Şartnamedeki anahtarlar camelCase (ör. "version", "outputDirectory"); eşleme birebir yapılır.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
    };

    public static BlackCoreConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new BlackCoreException(ErrorCode.ConfigInvalid, $"Yapılandırma dosyası bulunamadı: {path}");
        }

        return Parse(File.ReadAllText(path));
    }

    public static BlackCoreConfig Parse(string json)
    {
        BlackCoreConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<BlackCoreConfig>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new BlackCoreException(ErrorCode.ConfigInvalid, $"Yapılandırma ayrıştırılamadı: {ex.Message}");
        }

        if (config is null)
        {
            throw new BlackCoreException(ErrorCode.ConfigInvalid, "Yapılandırma boş.");
        }

        Validate(config);
        return config;
    }

    public static void Validate(BlackCoreConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Version != SupportedVersion)
        {
            throw new BlackCoreException(
                ErrorCode.ConfigVersionUnsupported,
                $"Desteklenmeyen yapılandırma sürümü: {config.Version} (beklenen {SupportedVersion}).");
        }

        if (!string.Equals(config.Target.OperatingSystem, "windows", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("target.operatingSystem yalnızca 'windows' olabilir.");
        }

        if (!string.Equals(config.Target.Architecture, "x64", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("target.architecture yalnızca 'x64' olabilir.");
        }

        if (!Enum.TryParse<BuildConfiguration>(config.Build.Configuration, ignoreCase: true, out var buildConfiguration)
            || !Enum.IsDefined(buildConfiguration))
        {
            throw Invalid($"build.configuration geçersiz: {config.Build.Configuration}");
        }

        if (!Enum.TryParse<OptimizationProfile>(config.Build.Optimization, ignoreCase: true, out var optimization)
            || !Enum.IsDefined(optimization))
        {
            throw Invalid($"build.optimization geçersiz: {config.Build.Optimization}");
        }

        if (string.IsNullOrWhiteSpace(config.Build.OutputDirectory))
        {
            throw Invalid("build.outputDirectory boş olamaz.");
        }

        if (config.Execution.ToolTimeoutSeconds <= 0 || config.Execution.SmokeTestTimeoutSeconds <= 0)
        {
            throw Invalid("execution timeout değerleri pozitif olmalı.");
        }

        if (config.Protection.Enabled && string.IsNullOrWhiteSpace(config.Protection.Provider))
        {
            throw Invalid("protection.enabled true iken protection.provider zorunludur.");
        }
    }

    private static BlackCoreException Invalid(string message) => new(ErrorCode.ConfigInvalid, message);
}
