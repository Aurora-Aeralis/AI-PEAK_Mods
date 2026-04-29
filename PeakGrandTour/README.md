# Peak Grand Tour

Forces PEAK runs into a fixed all-biomes route:

- Shore
- Tropics
- Roots
- Alpine
- Mesa
- Caldera
- Kiln

The mod patches the daily generated level selection and rebuilds the active `MapHandler` route from the scene's base and variant biome segments. If a generated scene does not expose one of the requested variant segments, the mod keeps the route playable with the segments it can find and logs the missing biome in BepInEx.

## Installation

Install with BepInEx for PEAK and place `PeakGrandTour.dll` in `BepInEx/plugins/`.

## Build

```powershell
dotnet build -c Release -v d
```
