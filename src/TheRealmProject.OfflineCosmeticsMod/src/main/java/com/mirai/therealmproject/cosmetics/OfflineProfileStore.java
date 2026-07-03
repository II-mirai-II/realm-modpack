package com.mirai.therealmproject.cosmetics;

import com.google.gson.Gson;
import com.google.gson.JsonObject;
import com.mojang.logging.LogUtils;
import org.slf4j.Logger;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

public final class OfflineProfileStore {
    private static final Logger LOGGER = LogUtils.getLogger();
    private static final Gson GSON = new Gson();

    private OfflineProfileStore() {
    }

    public static OfflineCosmeticsProfile load() {
        var profilePath = resolveProfilePath();
        if (!Files.isRegularFile(profilePath)) {
            LOGGER.warn("The Realm Project cosmetics profile was not found at {}", profilePath);
            return OfflineCosmeticsProfile.empty();
        }

        try (var reader = Files.newBufferedReader(profilePath)) {
            var root = GSON.fromJson(reader, JsonObject.class);
            return new OfflineCosmeticsProfile(
                    string(root, "playerId", "mirai"),
                    string(root, "uuid", ""),
                    string(root, "skin", ""),
                    string(root, "cape", ""));
        } catch (IOException ex) {
            LOGGER.error("Failed to read The Realm Project cosmetics profile", ex);
            return OfflineCosmeticsProfile.empty();
        }
    }

    private static Path resolveProfilePath() {
        var appData = System.getenv("APPDATA");
        if (appData == null || appData.isBlank()) {
            return Path.of("config", "the-realm-project", "profile.json");
        }

        return Path.of(appData, "The Realm Project", "assets", "cosmetics", "profile.json");
    }

    private static String string(JsonObject root, String key, String fallback) {
        if (root == null || !root.has(key) || root.get(key).isJsonNull()) {
            return fallback;
        }

        return root.get(key).getAsString();
    }
}
