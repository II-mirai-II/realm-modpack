using TheRealmProject.Core;
using Xunit;

namespace TheRealmProject.Core.Tests;

public sealed class ProfileServiceTests
{
    [Fact]
    public async Task SaveAsync_KeepsExistingSkinWhenSourceIsAlreadyTarget()
    {
        using var temp = new TempRealm();
        var service = new ProfileService(temp.Paths);
        var externalSkin = temp.WriteFile("external-skin.png", "skin");

        var first = await service.SaveAsync("mirai", externalSkin, null);
        var second = await service.SaveAsync("mirai", first.SkinPath, null);

        Assert.Equal(first.SkinPath, second.SkinPath);
        Assert.True(File.Exists(second.SkinPath));
        Assert.Equal(first.Uuid, second.Uuid);
    }

    [Fact]
    public async Task SaveAsync_KeepsExistingCapeWhenSourceIsAlreadyTarget()
    {
        using var temp = new TempRealm();
        var service = new ProfileService(temp.Paths);
        var externalCape = temp.WriteFile("external-cape.png", "cape");

        var first = await service.SaveAsync("mirai", null, externalCape);
        var second = await service.SaveAsync("mirai", null, first.CapePath);

        Assert.Equal(first.CapePath, second.CapePath);
        Assert.True(File.Exists(second.CapePath));
        Assert.Equal(first.Uuid, second.Uuid);
    }

    [Fact]
    public async Task SaveAsync_CopiesExternalSkinToCosmeticsFolder()
    {
        using var temp = new TempRealm();
        var service = new ProfileService(temp.Paths);
        var externalSkin = temp.WriteFile("skin-source.png", "new skin");

        var profile = await service.SaveAsync("mirai", externalSkin, null);

        Assert.Equal(Path.Combine(temp.Paths.Cosmetics, "skin.png"), profile.SkinPath);
        Assert.NotNull(profile.SkinPath);
        Assert.Equal("new skin", await File.ReadAllTextAsync(profile.SkinPath));
    }

    [Fact]
    public async Task SaveAsync_ThrowsWhenSkinSourceDoesNotExist()
    {
        using var temp = new TempRealm();
        var service = new ProfileService(temp.Paths);
        var missing = Path.Combine(temp.Root, "missing.png");

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.SaveAsync("mirai", missing, null));
    }

    [Fact]
    public async Task SaveAsync_SanitizesPlayerId()
    {
        using var temp = new TempRealm();
        var service = new ProfileService(temp.Paths);

        var profile = await service.SaveAsync("m!r@a#i_123456789012345", null, null);

        Assert.Equal("mrai_12345678901", profile.PlayerId);
    }

    private sealed class TempRealm : IDisposable
    {
        public TempRealm()
        {
            Root = Path.Combine(Path.GetTempPath(), $"realm-tests-{Guid.NewGuid():N}");
            Paths = new RealmPaths(Root);
            Paths.Ensure();
        }

        public string Root { get; }
        public RealmPaths Paths { get; }

        public string WriteFile(string name, string content)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
    }
}
