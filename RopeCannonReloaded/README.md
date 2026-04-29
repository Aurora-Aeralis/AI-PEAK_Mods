# Rope Cannon Reloaded

Rope Cannon Reloaded lets an empty PEAK rope cannon reload by consuming a carried rope item.

## Features

- Empty rope cannons stay useful after firing.
- Press primary use with an empty rope cannon to consume one carried rope from a normal item slot.
- Reloading restores one cannon shot and syncs the loaded visual state to other clients.
- If no carried rope is available, the vanilla empty-shot behavior is preserved.

## Configuration

After the first launch, edit:

```text
BepInEx/config/aeralisfoundation.peak.ropecannonreloaded.cfg
```

Settings:

- `General.Enabled`
- `General.PlayEmptySoundWhenNoRope`

## Build

```sh
dotnet build -c Release
```

The Thunderstore package is generated under `artifacts/thunderstore/` when building Release.
