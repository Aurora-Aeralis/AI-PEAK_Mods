# Seed Duel

Seed Duel lets PEAK racers run separate games on a shared race code.

Both players install the mod, set the same `RaceCode` in the BepInEx config, and start their own run. The mod forces PEAK's map seed ID from that code, locks the selected map level index, and wraps known map and loot roll methods in deterministic Unity RNG scopes so matching clients get matching route and loot rolls.

This is a local/manual race implementation. It does not provide an external ranked queue, backend matchmaking, or automatic cross-client lobby splitting.

## Config

The config file is generated at:

```txt
BepInEx/config/aeralisfoundation.peak.seedduel.cfg
```

Important settings:

- `RaceCode`: shared code for the race. Both players should use the exact same value.
- `UseDailyCodeWhenBlank`: when `RaceCode` is blank, both players use the UTC daily code.
- `SeedSalt`: optional ruleset salt. Change only when both players agree.
- `ForceMapSeed`: forces PEAK's map seed ID from the active race code.
- `LockLevelSelection`: chooses PEAK's map level index from the active race code.
- `DeterministicMapRng`: wraps known map generation methods in deterministic RNG scopes.
- `DeterministicLoot`: wraps known loot and item roll methods in deterministic RNG scopes.
- `CopyCodeKey`: defaults to `F8` and copies the active race code to the clipboard.

## Building

```sh
dotnet build -c Release
```

If PEAK is not installed in the default Steam location, pass `PEAKGameRootDir`:

```sh
dotnet build -c Release -p:PEAKGameRootDir="E:/SteamLibrary/steamapps/common/PEAK"
```
