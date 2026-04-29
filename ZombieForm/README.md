# Zombie Form

Transform your local PEAK climber into the game's synced mushroom zombie form with a configurable hotkey.

## Behavior

- Press `F8` in a run to request PEAK's built-in `RPCA_Zombify` flow for your local character.
- The master client spawns PEAK's built-in `MushroomZombie_Player` prefab, so other players see the zombie through the game's normal networking.
- The transform uses PEAK's original zombification behavior: your normal climber is marked dead/zombified and drops items.

## Configuration

The config is generated at `BepInEx/config/aeralisfoundation.peak.zombieform.cfg` after the first launch.

- `Controls.TransformKey`: hotkey used to transform. Default: `F8`.
- `Safety.AllowPassedOutTransform`: allows transforming while passed out but not dead. Default: `false`.

## Build

```sh
dotnet build -c Release -p:PEAKGameRootDir="E:\SteamLibrary\steamapps\common\PEAK" -p:RunThunderPipePackAfterBuild=false
```
