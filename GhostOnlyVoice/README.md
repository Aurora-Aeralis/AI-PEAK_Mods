# GhostOnlyVoice

Prevents living players from hearing ghost voice while allowing ghosts to hear each other.

## Behavior

- Living players do not subscribe to ghost voice interest groups.
- If a ghost voice stream is already present, living players locally mute that ghost voice.
- Ghost players keep normal voice subscriptions, so they can hear other ghosts.

Photon Voice subscriptions are controlled by each client. For reliable behavior, every client should run the mod. A host running the mod cannot force unmodded clients to stop subscribing to ghost voice.

## Build

To build against a non-default PEAK install path:

```sh
dotnet build -c Release -p:PEAKGameRootDir="E:\SteamLibrary\steamapps\common\PEAK"
```

The Thunderstore package is written under `artifacts/thunderstore/` for Release builds.
