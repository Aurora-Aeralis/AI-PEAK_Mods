# Scout Ledger

Scout Ledger adds a persistent in-game counter panel for PEAK actions that are not normally surfaced like the built-in peak count.

Tracked local-player counters:

- Peaks reached
- Scouts revived
- Revive chest activations
- Scouts cannibalized
- Poison hazards stepped on
- Poison, thorn, spore, and injury status gains
- Times passed out
- Deaths
- Items consumed
- Luggage opened

The overlay is shown by default, can be dragged, and can be toggled with `F8`. Counters are saved to the BepInEx config file and can be reset with `LeftCtrl + F10`.

## Installation

Install BepInExPack for PEAK, then place `ScoutLedger.dll` in `BepInEx/plugins/`.

## Build

```sh
dotnet build .\ScoutLedger.slnx -c Release -p:PEAKGameRootDir="E:\SteamLibrary\steamapps\common\PEAK" -p:RunThunderPipePackAfterBuild=false
```
