namespace TheRealmProject.Core;

public sealed class ProfileService(RealmPaths paths)
{
    public async Task<RealmProfile> LoadAsync(CancellationToken cancellationToken = default)
    {
        paths.Ensure();
        var profile = await JsonStore.LoadOrCreateAsync(paths.ProfileFile, new RealmProfile(), cancellationToken);
        await WriteCosmeticsProfileAsync(profile, cancellationToken);
        return profile;
    }

    public async Task<RealmProfile> SaveAsync(string playerId, string? skinSource, string? capeSource, CancellationToken cancellationToken = default)
    {
        paths.Ensure();
        var existing = await LoadAsync(cancellationToken);
        var profile = new RealmProfile
        {
            PlayerId = SanitizePlayerId(playerId),
            Uuid = string.IsNullOrWhiteSpace(existing.Uuid) ? Guid.NewGuid().ToString("N") : existing.Uuid,
            SkinPath = CopyCosmeticAsset(skinSource, "skin.png"),
            CapePath = CopyCosmeticAsset(capeSource, "cape.png"),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await JsonStore.SaveAsync(paths.ProfileFile, profile, cancellationToken);
        await WriteCosmeticsProfileAsync(profile, cancellationToken);
        return profile;
    }

    private async Task WriteCosmeticsProfileAsync(RealmProfile profile, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.Cosmetics);
        var modProfile = new
        {
            playerId = profile.PlayerId,
            uuid = profile.Uuid,
            skin = profile.SkinPath,
            cape = profile.CapePath,
            updatedAt = profile.UpdatedAt
        };
        await JsonStore.SaveAsync(paths.CosmeticsProfileFile, modProfile, cancellationToken);
    }

    private string? CopyCosmeticAsset(string? source, string fileName)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        if (!File.Exists(source))
            throw new FileNotFoundException("Arquivo de cosmético não encontrado.", source);

        Directory.CreateDirectory(paths.Cosmetics);
        var target = Path.Combine(paths.Cosmetics, fileName);
        var resolvedSource = Path.GetFullPath(source);
        var resolvedTarget = Path.GetFullPath(target);

        if (string.Equals(resolvedSource, resolvedTarget, StringComparison.OrdinalIgnoreCase))
            return File.Exists(resolvedTarget) ? target : throw new FileNotFoundException("Arquivo de cosmético não encontrado.", target);

        var temp = Path.Combine(paths.Cosmetics, $"{fileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(resolvedSource, temp, true);
            File.Move(temp, target, true);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw new IOException(
                $"Não foi possível salvar {fileName}. Feche qualquer editor ou visualizador que esteja usando a imagem e tente novamente.",
                ex);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static string SanitizePlayerId(string value)
    {
        var cleaned = new string(value.Where(c => char.IsLetterOrDigit(c) || c == '_').Take(16).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "mirai" : cleaned;
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
            // Best effort cleanup for failed cosmetic copies.
        }
    }
}
