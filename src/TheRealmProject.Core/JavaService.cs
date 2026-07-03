using System.IO.Compression;
using System.Text.RegularExpressions;

namespace TheRealmProject.Core;

public sealed class JavaService(RealmPaths paths, HttpClient httpClient)
{
    public async Task<string> EnsureJavaAsync(int requiredMajor, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        paths.Ensure();

        var bundled = FindBundledJava(requiredMajor);
        if (bundled is not null)
            return bundled;

        var system = await FindSystemJavaAsync(requiredMajor, cancellationToken);
        if (system is not null)
            return system;

        return await DownloadAdoptiumAsync(requiredMajor, progress, cancellationToken);
    }

    private string? FindBundledJava(int requiredMajor)
    {
        var root = Path.Combine(paths.Runtime, $"jdk-{requiredMajor}");
        if (!Directory.Exists(root))
            return null;

        return Directory.EnumerateFiles(root, "java.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string?> FindSystemJavaAsync(int requiredMajor, CancellationToken cancellationToken)
    {
        try
        {
            var java = "java";
            var output = new List<string>();
            var progress = new Progress<string>(line => output.Add(line));
            var exit = await ProcessRunner.RunAsync(java, "-version", Environment.CurrentDirectory, progress, cancellationToken);
            if (exit != 0)
                return null;

            var major = ParseMajorVersion(string.Join('\n', output));
            return major >= requiredMajor ? java : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> DownloadAdoptiumAsync(int major, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var installRoot = Path.Combine(paths.Runtime, $"jdk-{major}");
        Directory.CreateDirectory(installRoot);

        var archive = Path.Combine(paths.Downloads, $"adoptium-jdk-{major}-windows-x64.zip");
        var tempArchive = $"{archive}.{Guid.NewGuid():N}.tmp";
        var url = $"https://api.adoptium.net/v3/binary/latest/{major}/ga/windows/x64/jdk/hotspot/normal/eclipse?project=jdk";

        progress?.Report($"Baixando Java {major} do Eclipse Adoptium...");
        try
        {
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = File.Create(tempArchive))
                await source.CopyToAsync(destination, cancellationToken);

            File.Move(tempArchive, archive, true);
        }
        catch
        {
            TryDelete(tempArchive);
            throw;
        }

        progress?.Report($"Extraindo Java {major}...");
        if (Directory.Exists(installRoot))
            Directory.Delete(installRoot, true);
        Directory.CreateDirectory(installRoot);
        ZipFile.ExtractToDirectory(archive, installRoot);

        return FindBundledJava(major) ?? throw new InvalidOperationException($"Java {major} foi baixado, mas java.exe não foi encontrado.");
    }

    private static int ParseMajorVersion(string text)
    {
        var match = Regex.Match(text, "version\\s+\"(?<version>\\d+)(?:\\.|\"|\\+)");
        return match.Success ? int.Parse(match.Groups["version"].Value) : 0;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup for failed downloads.
        }
    }
}
