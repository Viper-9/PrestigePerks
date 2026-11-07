# Sonar Pulse shaders

Source of the two shaders that the Sonar Pulse active perk draws with. The client embeds the built bundle
(`client/PrestigePerks.Client/Assets/Shaders/prestigeperks_shaders.bundle`) as a DLL resource, so a normal
client build does not need Unity. Rebuild the bundle only after changing a shader here.

| File | Shader name | Use |
|---|---|---|
| `SonarEcho.shader` | `PrestigePerks/SonarEcho` | Echo of a target, drawn through walls (rim brighter at the edges) |
| `SonarRing.shader` | `PrestigePerks/SonarRing` | Wave ring at foot height, hidden by walls and ground |
| `PrestigePerksBundles.cs` | | Editor script that builds the bundle |

## Build

Needs Unity 2022.3.43f1 (the game's version) and any empty Unity project (3D, built-in render pipeline).

1. Copy `SonarEcho.shader` and `SonarRing.shader` to `<project>\Assets\PrestigePerksShaders\`.
2. Copy `PrestigePerksBundles.cs` to `<project>\Assets\Editor\`.
3. Run:

   ```
   "C:\Program Files\Unity\Hub\Editor\2022.3.43f1\Editor\Unity.exe" -batchmode -quit -nographics -projectPath <project> -executeMethod PrestigePerksBundles.Build -logFile build.log
   ```

   `build.log` ends with `PRESTIGEPERKS BUNDLE BUILD OK`. A licensing warning at the start does not stop the build.
4. Copy `<project>\Build\prestigeperks_shaders.bundle` to `client/PrestigePerks.Client/Assets/Shaders/`.

In game, the developer mode check `sonar.shaders` in the BepInEx log lists the shader names found in the bundle.
