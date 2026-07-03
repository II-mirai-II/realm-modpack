package com.mirai.therealmproject.cosmetics;

public record OfflineCosmeticsProfile(
        String playerId,
        String uuid,
        String skinPath,
        String capePath) {

    public static OfflineCosmeticsProfile empty() {
        return new OfflineCosmeticsProfile("mirai", "", "", "");
    }
}
