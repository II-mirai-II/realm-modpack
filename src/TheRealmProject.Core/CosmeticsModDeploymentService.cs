namespace TheRealmProject.Core;

public sealed class CosmeticsModDeploymentService(RealmPaths paths)
{
    public void DeployIfBuilt(IProgress<string>? progress = null)
    {
        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        if (repoRoot is null)
            return;

        var libs = Path.Combine(repoRoot, "src", "TheRealmProject.OfflineCosmeticsMod", "build", "libs");
        if (!Directory.Exists(libs))
            return;

        var jar = Directory.EnumerateFiles(libs, "*.jar")
            .Where(path => !Path.GetFileName(path).Contains("sources", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (jar is null)
            return;

        Directory.CreateDirectory(paths.Mods);
        var target = Path.Combine(paths.Mods, Path.GetFileName(jar));
        File.Copy(jar, target, true);
        progress?.Report($"Mod de cosméticos copiado para mods/: {Path.GetFileName(jar)}");
    }

    private static string? FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TheRealmProject.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }
}
