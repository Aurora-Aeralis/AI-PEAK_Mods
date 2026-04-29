# SpectralComms

Stops living players from hearing ghost voice while preserving ghost-to-ghost voice chat.

## Behavior

- Living clients do not subscribe to ghost voice interest groups.
- Any already-active ghost voice stream is locally muted for living clients.
- Ghost clients keep normal voice routing, so ghosts can hear each other.

Photon Voice subscriptions are controlled client-side. Every client should run the mod for reliable behavior; a host running the mod cannot force unmodded clients to stop hearing ghost voice.

## Build

To build against a non-default PEAK install path:

```sh
dotnet build -c Release -p:PEAKGameRootDir="E:\SteamLibrary\steamapps\common\PEAK"
```

Release builds can produce a Thunderstore package under `artifacts/thunderstore/`.
