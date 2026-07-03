using System.Text.Json;
using System.Text.Json.Nodes;

namespace TheRealmProject.Core;

public sealed class InstallationService(RealmPaths paths)
{
    public IReadOnlyList<InstalledGameVersion> GetInstalledVersions()
    {
        if (!Directory.Exists(paths.Versions))
            return [];

        return Directory.EnumerateDirectories(paths.Versions)
            .Select(CreateInstalledVersion)
            .Where(version => version is not null)
            .Cast<InstalledGameVersion>()
            .OrderBy(version => version.Kind)
            .ThenByDescending(version => version.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Uninstall(InstalledGameVersion version)
    {
        var resolvedVersionsRoot = Path.GetFullPath(paths.Versions);
        var resolvedTarget = Path.GetFullPath(version.Path);
        if (!resolvedTarget.StartsWith(resolvedVersionsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Caminho de versão inválido para desinstalação.");

        if (Directory.Exists(resolvedTarget))
            Directory.Delete(resolvedTarget, true);

        if (version.Kind == InstalledGameVersionKind.NeoForge)
            RemoveLauncherProfile(version.Id);
    }

    private InstalledGameVersion? CreateInstalledVersion(string directory)
    {
        var id = Path.GetFileName(directory);
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var json = Path.Combine(directory, $"{id}.json");
        var jar = Path.Combine(directory, $"{id}.jar");
        if (!File.Exists(json) && !File.Exists(jar))
            return null;

        var kind = id.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase)
            ? InstalledGameVersionKind.NeoForge
            : InstalledGameVersionKind.Minecraft;

        var display = kind == InstalledGameVersionKind.NeoForge
            ? id.Replace("neoforge-", "NeoForge ", StringComparison.OrdinalIgnoreCase)
            : $"Minecraft {id}";

        return new InstalledGameVersion(id, display, kind, directory);
    }

    private void RemoveLauncherProfile(string versionId)
    {
        var launcherProfiles = Path.Combine(paths.Minecraft, "launcher_profiles.json");
        if (!File.Exists(launcherProfiles))
            return;

        JsonNode? root;
        using (var stream = File.OpenRead(launcherProfiles))
            root = JsonNode.Parse(stream);

        if (root?["profiles"] is not JsonObject profiles)
            return;

        var removeKeys = profiles
            .Where(pair => IsProfileForVersion(pair.Key, pair.Value, versionId))
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in removeKeys)
            profiles.Remove(key);

        File.WriteAllText(launcherProfiles, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool IsProfileForVersion(string key, JsonNode? profile, string versionId)
    {
        if (key.Contains(versionId, StringComparison.OrdinalIgnoreCase))
            return true;

        var lastVersionId = profile?["lastVersionId"]?.GetValue<string>();
        return string.Equals(lastVersionId, versionId, StringComparison.OrdinalIgnoreCase);
    }
}
