namespace BlackCore.Infrastructure.Projects;

/// <summary>C# proje dosyasını ve kaynaklarını bulur. Çıktı klasörleri (bin/obj) kaynak sayımına dahil edilmez.</summary>
public static class CSharpProjects
{
    public sealed record ProjectInfo(string Directory, string ProjectFile);

    /// <summary>Kaynak bir .csproj dosyası veya onu içeren klasör olabilir.</summary>
    public static ProjectInfo? Find(string sourcePath)
    {
        if (File.Exists(sourcePath) && sourcePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectInfo(Path.GetDirectoryName(Path.GetFullPath(sourcePath))!, Path.GetFullPath(sourcePath));
        }

        if (Directory.Exists(sourcePath))
        {
            var projects = Directory.GetFiles(sourcePath, "*.csproj", SearchOption.TopDirectoryOnly);
            if (projects.Length == 1)
            {
                return new ProjectInfo(Path.GetFullPath(sourcePath), Path.GetFullPath(projects[0]));
            }
        }

        return null;
    }

    /// <summary>Projenin kendi *.cs dosyaları (bin, obj ve gizli klasörler hariç), sıralı.</summary>
    public static IReadOnlyList<string> SourceFiles(string projectDirectory)
    {
        return Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(projectDirectory, f))
            .Select(Path.GetFullPath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Derleme çıktısını (bin/obj) ve gizli klasörleri dışarıda bırakarak proje ağacını kopyalar.</summary>
    public static void CopyProject(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(sourceDirectory, file))
            {
                continue;
            }

            var relative = Path.GetRelativePath(sourceDirectory, file);
            var target = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool IsBuildOutput(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        return relative.Split(Path.DirectorySeparatorChar)
            .Take(relative.Split(Path.DirectorySeparatorChar).Length - 1)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || segment.StartsWith('.'));
    }
}
