package com.mirai.therealmproject.cosmetics.mixin;

import net.minecraft.client.player.AbstractClientPlayer;
import net.minecraft.client.resources.PlayerSkin;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;
import com.mirai.therealmproject.cosmetics.LocalCosmetics;

@Mixin(AbstractClientPlayer.class)
public abstract class AbstractClientPlayerMixin {
    @Inject(method = "getSkin", at = @At("HEAD"), cancellable = true)
    private void therealmproject$useLocalSkin(CallbackInfoReturnable<PlayerSkin> callback) {
        var player = (AbstractClientPlayer) (Object) this;
        var skin = LocalCosmetics.skinFor(player.getGameProfile());
        if (skin != null) {
            callback.setReturnValue(skin);
        }
    }
}
