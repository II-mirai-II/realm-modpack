using System.Text.Json.Serialization;

namespace TheRealmProject.Core;

public sealed record NeoForgeVersion(string MinecraftVersion, string NeoForgeVersionId, bool IsBeta)
{
    public string InstallerFileName => $"neoforge-{NeoForgeVersionId}-installer.jar";
    public string InstallerUrl => $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{NeoForgeVersionId}/{InstallerFileName}";
    public string DisplayName => $"{MinecraftVersion} / NeoForge {NeoForgeVersionId}";

    public override string ToString() => NeoForgeVersionId;
}

public sealed class RealmProfile
{
    public string PlayerId { get; set; } = "mirai";
    public string Uuid { get; set; } = Guid.NewGuid().ToString("N");
    public string? SkinPath { get; set; }
    public string? CapePath { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LauncherState
{
    public string? SelectedMinecraftVersion { get; set; }
    public string? SelectedNeoForgeVersion { get; set; }
    public string? InstalledModpackRelease { get; set; }
    public string? InstalledModpackAsset { get; set; }
    public string? InstalledModpackHash { get; set; }
    public DateTimeOffset? ModpackInstalledAt { get; set; }
}

public sealed class RealmAppConfig
{
    public string GitHubOwner { get; set; } = "CHANGE-ME";
    public string GitHubRepository { get; set; } = "realm-modpack";
    public string GitHubAssetNameContains { get; set; } = "realm";
    public int MaximumRamMb { get; set; } = 4096;
    public int MinimumRamMb { get; set; } = 1024;
    public bool IncludeBetaNeoForgeVersions { get; set; } = true;
}

public enum InstalledGameVersionKind
{
    Minecraft,
    NeoForge
}

public sealed record InstalledGameVersion(
    string Id,
    string DisplayName,
    InstalledGameVersionKind Kind,
    string Path)
{
    public string KindLabel => Kind == InstalledGameVersionKind.NeoForge ? "NeoForge" : "Minecraft";
}

public sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; set; } = [];
}
