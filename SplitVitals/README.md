# SplitVitals

SplitVitals separates PEAK's status pressure from stamina. Afflictions still build a health-style danger bar and still cause pass-out when they max out, but they no longer shrink the player's stamina capacity.

## Features

- Keeps stamina capacity independent from injury, hunger, cold, poison, weight, and other status pressure.
- Adds a separate health/status bar that fills with the same affliction pressure that previously consumed the stamina bar.
- Optionally hides the old status chunks from the vanilla stamina bar so the vanilla bar reads as stamina only.

## Configuration

The BepInEx config file is generated after first launch. You can toggle the mod, hide/show the added bar, keep or restore vanilla status chunks on the stamina bar, and adjust the health bar position/size.

## Build

Build from this folder with a PEAK game path if PEAK is not installed in the default Steam location:

```sh
dotnet build .\SplitVitals.slnx -c Release -p:PEAKGameRootDir="E:\SteamLibrary\steamapps\common\PEAK" -p:RunThunderPipePackAfterBuild=false
```
