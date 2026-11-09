namespace BlackCore.Frontend.Cpp;

/// <summary>Derleme girdisindeki C++ kaynak dosyalarını bulur.</summary>
public static class CppSources
{
    /// <summary>
    /// Dosya verilirse yalnızca o dosya; klasör verilirse klasörün doğrudan altındaki *.cpp dosyaları (sıralı).
    /// Bulunamazsa boş liste döner.
    /// </summary>
    public static IReadOnlyList<string> Enumerate(string sourcePath)
    {
        if (File.Exists(sourcePath))
        {
            return new[] { Path.GetFullPath(sourcePath) };
        }

        if (Directory.Exists(sourcePath))
        {
            return Directory.GetFiles(sourcePath, "*.cpp", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFullPath)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return Array.Empty<string>();
    }
}
