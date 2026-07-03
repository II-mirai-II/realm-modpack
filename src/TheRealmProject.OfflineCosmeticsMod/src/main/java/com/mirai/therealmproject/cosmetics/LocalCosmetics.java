package com.mirai.therealmproject.cosmetics;

import com.mojang.authlib.GameProfile;
import com.mojang.blaze3d.platform.NativeImage;
import com.mojang.logging.LogUtils;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.client.resources.PlayerSkin;
import net.minecraft.resources.ResourceLocation;
import org.slf4j.Logger;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

public final class LocalCosmetics {
    private static final Logger LOGGER = LogUtils.getLogger();
    private static final ResourceLocation SKIN_TEXTURE = ResourceLocation.fromNamespaceAndPath(OfflineCosmeticsMod.MOD_ID, "local_skin");
    private static final ResourceLocation CAPE_TEXTURE = ResourceLocation.fromNamespaceAndPath(OfflineCosmeticsMod.MOD_ID, "local_cape");

    private static OfflineCosmeticsProfile profile = OfflineCosmeticsProfile.empty();
    private static PlayerSkin playerSkin;
    private static String loadedSkinPath = "";
    private static String loadedCapePath = "";

    private LocalCosmetics() {
    }

    public static void reload() {
        profile = OfflineProfileStore.load();
        playerSkin = null;
        loadedSkinPath = "";
        loadedCapePath = "";
        LOGGER.info("The Realm Project cosmetics profile loaded for {}", profile.playerId());
    }

    public static PlayerSkin skinFor(GameProfile gameProfile) {
        if (!matches(gameProfile) || profile.skinPath().isBlank()) {
            return null;
        }

        var skinPath = Path.of(profile.skinPath());
        if (!Files.isRegularFile(skinPath)) {
            return null;
        }

        var capeLocation = registerCape(profile.capePath());
        if (playerSkin == null || !loadedSkinPath.equals(profile.skinPath()) || !loadedCapePath.equals(profile.capePath())) {
            if (!registerTexture(SKIN_TEXTURE, skinPath)) {
                return null;
            }

            loadedSkinPath = profile.skinPath();
            loadedCapePath = profile.capePath();
            playerSkin = new PlayerSkin(SKIN_TEXTURE, "", capeLocation, capeLocation, PlayerSkin.Model.WIDE, false);
        }

        return playerSkin;
    }

    private static boolean matches(GameProfile gameProfile) {
        if (gameProfile == null) {
            return false;
        }

        if (!profile.uuid().isBlank() && gameProfile.getId() != null) {
            var compactUuid = gameProfile.getId().toString().replace("-", "");
            if (compactUuid.equalsIgnoreCase(profile.uuid())) {
                return true;
            }
        }

        return gameProfile.getName() != null && gameProfile.getName().equalsIgnoreCase(profile.playerId());
    }

    private static ResourceLocation registerCape(String capePath) {
        if (capePath == null || capePath.isBlank()) {
            return null;
        }

        var path = Path.of(capePath);
        return registerTexture(CAPE_TEXTURE, path) ? CAPE_TEXTURE : null;
    }

    private static boolean registerTexture(ResourceLocation location, Path path) {
        if (!Files.isRegularFile(path)) {
            return false;
        }

        try (var input = Files.newInputStream(path)) {
            var image = NativeImage.read(input);
            Minecraft.getInstance().getTextureManager().register(location, new DynamicTexture(image));
            return true;
        } catch (IOException | RuntimeException ex) {
            LOGGER.error("Failed to register The Realm Project cosmetic texture {}", path, ex);
            return false;
        }
    }
}
