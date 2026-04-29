# Endless Ascent

Endless Ascent keeps successful PEAK runs going. After the end screen is closed, the mod replaces the vanilla return-to-airport transition with another island load, advances the ascent, and steps to the next generated level index.

Wipes still return to the airport. The end screen and normal win bookkeeping still run before the next island is loaded.

## Config

Generated at `BepInEx/config/com.aeralisfoundation.peak.endlessascent.cfg`.

- `General.Enabled`: enables or disables the endless continuation.
- `Run.AdvanceAscent`: raises `Ascents.currentAscent` by one before loading the next island.
- `Run.LevelStep`: how many generated level indices to advance after each successful island.

## Build

```sh
dotnet build -c Release
```
