using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Common;

namespace TheRealmProject.Core;

public sealed class ModpackService(RealmPaths paths, HttpClient httpClient, LauncherStateService stateService)
{
    private static readonly string[] ReplaceableEntries = ["mods", "config", "resourcepacks", "shaderpacks", "options.txt"];

    public async Task<GitHubRelease?> GetLatestReleaseAsync(RealmAppConfig config, CancellationToken cancellationToken = default)
    {
        if (config.GitHubOwner == "CHANGE-ME" || string.IsNullOrWhiteSpace(config.GitHubOwner) || string.IsNullOrWhiteSpace(config.GitHubRepository))
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{config.GitHubOwner}/{config.GitHubRepository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("TheRealmProjectLauncher/1.0");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken);
    }

    public GitHubReleaseAsset? SelectAsset(GitHubRelease release, RealmAppConfig config)
    {
        return release.Assets
            .Where(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || asset.Name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
            .Where(asset => string.IsNullOrWhiteSpace(config.GitHubAssetNameContains) || asset.Name.Contains(config.GitHubAssetNameContains, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    public async Task<bool> InstallOrUpdateLatestAsync(bool force, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        paths.Ensure();
        var config = await stateService.LoadConfigAsync(cancellationToken);
        var release = await GetLatestReleaseAsync(config, cancellationToken)
            ?? throw new InvalidOperationException("Configure GitHubOwner/GitHubRepository em appsettings.json antes de baixar o modpack.");
        var asset = SelectAsset(release, config)
            ?? throw new InvalidOperationException("A release mais recente não contém asset .zip ou .rar compatível.");
        var state = await stateService.LoadAsync(cancellationToken);

        if (!force && state.InstalledModpackRelease == release.TagName && state.InstalledModpackAsset == asset.Name)
        {
            progress?.Report("Modpack ja esta atualizado.");
            return false;
        }

        progress?.Report($"Baixando modpack {asset.Name} ({release.TagName})...");
        var archive = await DownloadAssetAsync(asset, cancellationToken);
        var hash = await HashFileAsync(archive, cancellationToken);
        await ExtractAndApplyAsync(archive, progress, cancellationToken);

        state.InstalledModpackRelease = release.TagName;
        state.InstalledModpackAsset = asset.Name;
        state.InstalledModpackHash = hash;
        state.ModpackInstalledAt = DateTimeOffset.UtcNow;
        await stateService.SaveAsync(state, cancellationToken);
        return true;
    }

    private async Task<string> DownloadAssetAsync(GitHubReleaseAsset asset, CancellationToken cancellationToken)
    {
        var target = Path.Combine(paths.Downloads, asset.Name);
        var temp = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using var source = await httpClient.GetStreamAsync(asset.BrowserDownloadUrl, cancellationToken);
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

    private Task ExtractAndApplyAsync(string archive, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var extractRoot = Path.Combine(paths.Temp, $"modpack-{Guid.NewGuid():N}");
        var stagingRoot = Path.Combine(paths.Temp, $"modpack-stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractRoot);
        Directory.CreateDirectory(stagingRoot);

        try
        {
            progress?.Report("Extraindo modpack...");
            if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ZipFile.ExtractToDirectory(archive, extractRoot);
            else
                ExtractRar(archive, extractRoot);

            var payloadRoot = ResolvePayloadRoot(extractRoot);
            ValidatePayload(payloadRoot);

            progress?.Report("Validando arquivos do modpack...");
            StageReplaceableEntries(payloadRoot, stagingRoot, cancellationToken);

            progress?.Report("Aplicando arquivos do modpack...");
            ApplyStagedEntries(stagingRoot, cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(extractRoot);
            TryDeleteDirectory(stagingRoot);
        }

        return Task.CompletedTask;
    }

    private static void ExtractRar(string archive, string extractRoot)
    {
        using var rar = RarArchive.OpenArchive(archive, new SharpCompress.Readers.ReaderOptions());
        foreach (var entry in rar.Entries.Where(entry => !entry.IsDirectory))
            entry.WriteToDirectory(extractRoot, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
    }

    private static string ResolvePayloadRoot(string extractRoot)
    {
        if (ContainsPayload(extractRoot))
            return extractRoot;

        var singleDirectory = Directory.EnumerateDirectories(extractRoot).Take(2).ToList();
        return singleDirectory.Count == 1 && ContainsPayload(singleDirectory[0]) ? singleDirectory[0] : extractRoot;
    }

    private static bool ContainsPayload(string directory)
        => ReplaceableEntries.Any(entry => File.Exists(Path.Combine(directory, entry)) || Directory.Exists(Path.Combine(directory, entry)));

    private static void ValidatePayload(string directory)
    {
        if (!ContainsPayload(directory))
            throw new InvalidOperationException("O pacote não contém mods/config/resourcepacks/shaderpacks/options.txt na raiz esperada.");
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), true);
    }

    private static void StageReplaceableEntries(string payloadRoot, string stagingRoot, CancellationToken cancellationToken)
    {
        foreach (var entry in ReplaceableEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.Combine(payloadRoot, entry);
            var target = Path.Combine(stagingRoot, entry);
            if (File.Exists(source))
                File.Copy(source, target, true);
            else if (Directory.Exists(source))
                CopyDirectory(source, target);
        }
    }

    private void ApplyStagedEntries(string stagingRoot, CancellationToken cancellationToken)
    {
        foreach (var entry in ReplaceableEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.Combine(stagingRoot, entry);
            var target = Path.Combine(paths.Minecraft, entry);
            if (!File.Exists(source) && !Directory.Exists(source))
                continue;

            if (File.Exists(target))
                File.Delete(target);
            if (Directory.Exists(target))
                Directory.Delete(target, true);

            if (File.Exists(source))
                File.Copy(source, target, true);
            else
                CopyDirectory(source, target);
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // Best effort cleanup for failed extraction/staging.
        }
    }
}
