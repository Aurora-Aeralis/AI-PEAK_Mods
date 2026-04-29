# Chromatic Skies

Configurable sky color customization for PEAK.

Chromatic Skies generates a runtime skybox from three color bands: top sky, horizon, and lower sky. It includes vibrant presets inspired by event-style sky changes and supports custom hex colors through the BepInEx config file.

## Features

- Concert Glow, Vanilla Boost, Alien Green, Sunset Bloom, Deep Violet, Ice Blue, and Custom presets.
- Brightness, vibrance, and skybox exposure controls.
- Optional matching ambient lighting and fog color.
- Periodic sky reapply so scene changes are handled without patching game code.
- Restores previous render settings when the plugin unloads.

## Configuration

After the first launch, edit:

```text
BepInEx/config/aeralisfoundation.peak.chromaticskies.cfg
```

The main settings are:

- `Sky.Preset`
- `Sky.Brightness`
- `Sky.Vibrance`
- `Sky.Exposure`
- `Custom Colors.TopColor`
- `Custom Colors.HorizonColor`
- `Custom Colors.GroundColor`
- `Lighting.ApplyAmbientLighting`
- `Lighting.ApplyFogColor`

Use `Sky.Preset = Custom` to use the custom color values.

## Build

```sh
dotnet build -c Release
```

The Thunderstore package is generated under `artifacts/thunderstore/` when building Release.
