# Scoutmaster Group Pressure

Scoutmaster Group Pressure lets the scoutmaster punish two-scout runaway groups in PEAK. Vanilla scoutmaster targeting compares the highest living scout against the second-highest living scout; this mod keeps that rule, and when at least four scouts are alive it can also compare a leading scout against the third-highest living scout.

By default, the group rule compares the second-highest living scout to the third-highest living scout and requires 150% of the vanilla scoutmaster height gap. When the rule triggers, the scoutmaster targets the highest living scout.

## Config

- `Trigger.MinimumLivingScouts`: living scouts required before the group runaway rule is active. Default: `4`.
- `Trigger.GroupReferenceRank`: leading scout rank compared to the third-highest living scout. Default: `2`.
- `Trigger.GroupThresholdMultiplier`: multiplier applied to the vanilla height gap for the group rule. Default: `1.5`.

## Installation

Install BepInExPack for PEAK, then place `ScoutmasterGroupPressure.dll` in `BepInEx/plugins/`.

## Build

```sh
dotnet build .\ScoutmasterGroupPressure.slnx -c Release -p:PEAKGameRootDir="E:\SteamLibrary\steamapps\common\PEAK" -p:RunThunderPipePackAfterBuild=false
```
