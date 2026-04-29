# Peak All Biome Route

Forces PEAK runs into a fixed all-biome route:

- Shore
- Tropics
- Roots
- Alpine
- Mesa
- Caldera
- Kiln

The mod patches the daily generated level selection and rebuilds the active `MapHandler` route from the island scene's base and variant biome segments. It prefers a generated level that exposes Tropics and Alpine as the base route, then inserts Roots and Mesa variants when they are present.

If PEAK changes its generated scene layout or a scene does not expose a requested variant segment, the mod keeps the route playable with the segments it can safely find and logs the missing biome in BepInEx.

## Installation

Install with BepInEx for PEAK and place `PeakAllBiomeRoute.dll` in `BepInEx/plugins/`.

## Build

Create a local `Config.Build.user.props` from the template or pass your PEAK path directly:

```powershell
dotnet build .\PeakAllBiomeRoute.slnx -c Release -p:PEAKGameRootDir="E:\SteamLibrary\steamapps\common\PEAK" -v minimal
```
