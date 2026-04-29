# Ghost Assist Vending

Ghost Assist Vending gives dead scouts a small way to stay involved without removing PEAK's pressure. Ghosts earn Soul Points over time, can earn extra points through a small jump mini-game, and can spend those points on limited assists for living scouts.

## Behavior

- Dead or fully passed-out local scouts earn Soul Points passively.
- `F7` opens the ghost vending window.
- Rift Hop is a small obstacle mini-game inside the vending window. Clearing an obstacle awards extra Soul Points.
- Purchases are fulfilled by the Photon master client when in a multiplayer room.
- Stage limits reset when the current map segment changes at the next bonfire.

## Vending Options

- Apple Blind Box: 75% Green Apple when a matching item exists, otherwise another apple fallback.
- Mushroom Blind Box: random non-spring mushroom fallbacking to any mushroom.
- Tick: attaches a short minor effect to a living target.
- Specialty Food: Cactus, Chili, Coconut, Orange, or rare Scout Cookie.
- Utility Prop: Rope, Pitons, Spring Mushroom, Balloons, or rare Bundle of Balloons.
- Respawn: expensive revive at the current bonfire.

## Default Balance

- Passive income: 1 Soul Point per second.
- Respawn cost: 600 Soul Points.
- Boxes and ticks: 2 uses each per stage.
- Food and props: 1 use each per stage.

Costs and keybinds are configurable through the generated BepInEx config file after first launch.

## Build

Configure `PEAKGameRootDir` if PEAK is not in the default Steam path, then build:

```sh
dotnet build -c Release -p:PEAKGameRootDir="E:\SteamLibrary\steamapps\common\PEAK" -p:DeployModFiles=false
```

The plugin DLL is produced under `artifacts/bin/GhostAssistVending/release/`.
