package com.mirai.therealmproject.cosmetics;

import com.mojang.logging.LogUtils;
import net.neoforged.api.distmarker.Dist;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.fml.common.Mod;
import net.neoforged.fml.loading.FMLEnvironment;
import org.slf4j.Logger;

@Mod(OfflineCosmeticsMod.MOD_ID)
public final class OfflineCosmeticsMod {
    public static final String MOD_ID = "therealmproject_cosmetics";
    private static final Logger LOGGER = LogUtils.getLogger();

    public OfflineCosmeticsMod(IEventBus modEventBus) {
        if (FMLEnvironment.dist == Dist.CLIENT) {
            LocalCosmetics.reload();
            var profile = OfflineProfileStore.load();
            LOGGER.info("The Realm Project cosmetics loaded for player {} with skin={} cape={}",
                    profile.playerId(), profile.skinPath(), profile.capePath());
        }
    }
}
