# The Realm Project Offline Cosmetics Mod

This NeoForge client mod reads the launcher-generated profile from:

`%AppData%\The Realm Project\assets\cosmetics\profile.json`

The current implementation provides the client-side profile bridge used by the launcher. The actual skin/cape render hook should be pinned to the Minecraft/NeoForge version selected for the modpack, because Mojang client skin APIs change between Minecraft releases.

Build:

```powershell
.\gradlew build
```

Copy the generated jar from `build/libs` into the modpack `mods` folder or include it in the GitHub Release asset.
