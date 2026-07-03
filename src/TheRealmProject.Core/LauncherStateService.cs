namespace TheRealmProject.Core;

public sealed class LauncherStateService(RealmPaths paths)
{
    public Task<LauncherState> LoadAsync(CancellationToken cancellationToken = default)
        => JsonStore.LoadOrCreateAsync(paths.LauncherStateFile, new LauncherState(), cancellationToken);

    public Task SaveAsync(LauncherState state, CancellationToken cancellationToken = default)
        => JsonStore.SaveAsync(paths.LauncherStateFile, state, cancellationToken);

    public Task<RealmAppConfig> LoadConfigAsync(CancellationToken cancellationToken = default)
        => JsonStore.LoadOrCreateAsync(paths.AppConfigFile, new RealmAppConfig(), cancellationToken);

    public Task SaveConfigAsync(RealmAppConfig config, CancellationToken cancellationToken = default)
        => JsonStore.SaveAsync(paths.AppConfigFile, config, cancellationToken);
}
