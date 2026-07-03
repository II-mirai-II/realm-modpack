package com.mirai.therealmproject.cosmetics.mixin;

import com.mirai.therealmproject.cosmetics.LocalCosmetics;
import net.minecraft.client.multiplayer.PlayerInfo;
import net.minecraft.client.resources.PlayerSkin;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(PlayerInfo.class)
public abstract class PlayerInfoMixin {
    @Inject(method = "getSkin", at = @At("HEAD"), cancellable = true)
    private void therealmproject$useLocalSkin(CallbackInfoReturnable<PlayerSkin> callback) {
        var playerInfo = (PlayerInfo) (Object) this;
        var skin = LocalCosmetics.skinFor(playerInfo.getProfile());
        if (skin != null) {
            callback.setReturnValue(skin);
        }
    }
}
