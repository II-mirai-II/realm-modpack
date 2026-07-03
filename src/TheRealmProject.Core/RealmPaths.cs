namespace TheRealmProject.Core;

public sealed class RealmPaths
{
    public RealmPaths(string? root = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Root = root ?? Path.Combine(appData, "The Realm Project");
        Instance = Path.Combine(Root, "instances", "default");
        Minecraft = Instance;
        Versions = Path.Combine(Minecraft, "versions");
        Mods = Path.Combine(Minecraft, "mods");
        Runtime = Path.Combine(Root, "runtime");
        Downloads = Path.Combine(Root, "downloads");
        Temp = Path.Combine(Root, "temp");
        Config = Path.Combine(Root, "config");
        Assets = Path.Combine(Root, "assets");
        Cosmetics = Path.Combine(Assets, "cosmetics");
        Logs = Path.Combine(Root, "logs");
        ProfileFile = Path.Combine(Config, "profile.json");
        LauncherStateFile = Path.Combine(Config, "launcher-state.json");
        AppConfigFile = Path.Combine(Config, "appsettings.json");
        CosmeticsProfileFile = Path.Combine(Cosmetics, "profile.json");
    }

    public string Root { get; }
    public string Instance { get; }
    public string Minecraft { get; }
    public string Versions { get; }
    public string Mods { get; }
    public string Runtime { get; }
    public string Downloads { get; }
    public string Temp { get; }
    public string Config { get; }
    public string Assets { get; }
    public string Cosmetics { get; }
    public string Logs { get; }
    public string ProfileFile { get; }
    public string LauncherStateFile { get; }
    public string AppConfigFile { get; }
    public string CosmeticsProfileFile { get; }

    public void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Instance);
        Directory.CreateDirectory(Minecraft);
        Directory.CreateDirectory(Versions);
        Directory.CreateDirectory(Mods);
        Directory.CreateDirectory(Runtime);
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(Temp);
        Directory.CreateDirectory(Config);
        Directory.CreateDirectory(Assets);
        Directory.CreateDirectory(Cosmetics);
        Directory.CreateDirectory(Logs);
    }
}
