using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;

namespace TheRealmProject.Core;

public sealed class NeoForgeService(RealmPaths paths, HttpClient httpClient)
{
    private static readonly Regex NeoForgeVersionPattern = new(@"^\d+\.\d+\.\d+(?:[-.][A-Za-z0-9]+)*$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<NeoForgeVersion>> GetVersionsAsync(bool includeBeta, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var metadata = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

        return metadata.Descendants("version")
            .Select(element => element.Value.Trim())
            .Where(IsNeoForgeVersion)
            .Where(version => includeBeta || !version.Contains("beta", StringComparison.OrdinalIgnoreCase))
            .Select(version => new NeoForgeVersion(InferMinecraftVersion(version), version, version.Contains("beta", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(v => VersionSortKey(v.NeoForgeVersionId))
            .ToList();
    }

    public IReadOnlyList<string> GetInstalledVersionIds()
    {
        if (!Directory.Exists(paths.Versions))
            return [];

        return Directory.EnumerateDirectories(paths.Versions)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => name!.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(name => name)
            .Cast<string>()
            .ToList();
    }

    public async Task<string> DownloadInstallerAsync(NeoForgeVersion version, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        paths.Ensure();
        var target = Path.Combine(paths.Downloads, version.InstallerFileName);
        if (File.Exists(target))
            return target;

        progress?.Report($"Baixando {version.InstallerFileName}...");
        var temp = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using var source = await httpClient.GetStreamAsync(version.InstallerUrl, cancellationToken);
            await using (var destination = File.Create(temp))
                await source.CopyToAsync(destination, cancellationToken);

            File.Move(temp, target, true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return target;
    }

    public async Task InstallClientAsync(NeoForgeVersion version, string javaPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var installer = await DownloadInstallerAsync(version, progress, cancellationToken);
        var installedIds = GetInstalledVersionIds();
        if (installedIds.Any(id => id.Contains(version.NeoForgeVersionId, StringComparison.OrdinalIgnoreCase)))
        {
            progress?.Report("NeoForge já está instalado para esta versão.");
            return;
        }

        EnsureLauncherProfilesFile();
        progress?.Report("Executando NeoForge installer em modo cliente...");
        var exitCode = await ProcessRunner.RunAsync(
            javaPath,
            $"-jar \"{installer}\" --install-client \"{paths.Minecraft}\"",
            paths.Minecraft,
            progress,
            cancellationToken);

        if (exitCode != 0)
            throw new InvalidOperationException($"NeoForge installer falhou com código {exitCode}.");
    }

    private void EnsureLauncherProfilesFile()
    {
        Directory.CreateDirectory(paths.Minecraft);
        var launcherProfiles = Path.Combine(paths.Minecraft, "launcher_profiles.json");
        if (File.Exists(launcherProfiles))
            return;

        var content = new
        {
            profiles = new Dictionary<string, object>(),
            selectedProfile = "",
            clientToken = Guid.NewGuid().ToString("N"),
            authenticationDatabase = new Dictionary<string, object>()
        };

        File.WriteAllText(launcherProfiles, JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static int RequiredJavaMajor(string minecraftVersion)
    {
        if (Version.TryParse(NormalizeMinecraftVersion(minecraftVersion), out var parsed)
            && parsed.Major == 1
            && parsed.Minor == 20
            && parsed.Build is >= 2 and <= 4)
            return 17;

        return 21;
    }

    private static bool IsNeoForgeVersion(string version)
    {
        if (version.StartsWith("0.", StringComparison.OrdinalIgnoreCase))
            return false;

        return NeoForgeVersionPattern.IsMatch(version);
    }

    private static string InferMinecraftVersion(string neoForgeVersion)
    {
        var prefix = neoForgeVersion.Split('-', 2)[0];
        var parts = prefix.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return prefix;

        return parts[0] switch
        {
            "20" => $"1.20.{parts[1]}",
            "21" => $"1.21.{parts[1]}",
            "22" => $"1.22.{parts[1]}",
            "23" => $"1.23.{parts[1]}",
            "24" => $"1.24.{parts[1]}",
            "25" => $"1.25.{parts[1]}",
            "26" => $"1.26.{parts[1]}",
            _ => $"1.{parts[0]}.{parts[1]}"
        };
    }

    private static string NormalizeMinecraftVersion(string version)
    {
        var parts = version.Split('.');
        return parts.Length == 2 ? $"{version}.0" : version;
    }

    private static string VersionSortKey(string version)
    {
        return string.Join('.', Regex.Matches(version, @"\d+").Select(m => int.Parse(m.Value).ToString("D5")));
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
