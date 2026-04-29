# EchoMimics

EchoMimics records short local microphone snippets when PEAK detects a clear voice peak, then replaces mushroom zombie grunts with those captured clips. Louder, shorter clips are treated as urgent calls and are weighted higher when zombies choose what to replay.

## Behavior

- Captures local microphone snippets above the configured voice threshold.
- Filters out very short noise and old clips.
- Prioritizes urgent shouts using peak volume and clip length.
- Replaces `MushroomZombie.ZombieGrunts` so zombies replay captured voices instead of their default grunt loop.
- Adds optional muffled echo processing to the zombie audio source.

## Configuration

The BepInEx config file is generated at:

```txt
BepInEx/config/aurora.aeralis.peak.echomimics.cfg
```

Useful settings:

- `Recording.Enabled`: toggle microphone capture.
- `Recording.MicrophoneDevice`: blank uses the first available microphone.
- `Recording.VoiceThreshold`: minimum RMS level needed to start a clip.
- `Recording.UrgentPeakThreshold`: peak level that marks a clip as urgent.
- `Replay.MinDelay` and `Replay.MaxDelay`: zombie mimic timing range.
- `Replay.UseHostileFilter`: toggles the low-pass and echo effect.

## Build

Copy `Config.Build.user.props.template` to `Config.Build.user.props` for local deployment, or pass `PEAKGameRootDir` and `PEAKBepInExDir` directly on the command line.

```sh
dotnet build .\EchoMimics.slnx -c Release
```
