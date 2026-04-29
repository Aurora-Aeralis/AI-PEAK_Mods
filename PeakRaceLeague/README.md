# PeakRaceLeague

PeakRaceLeague adds a lightweight speedrunner race layer to PEAK. It provides a ranked race timer, local rating/divisions, personal best tracking, a compact in-game HUD, and a shareable matchmaking card so runners can group by route, rank, and queue window.

## Features

- Manual race start, finish, cancel, and queue controls.
- Local ranked rating with Rookie, Bronze, Silver, Gold, Platinum, Diamond, and Master divisions.
- Route-specific personal best storage.
- Matchmaking codes based on route, rank band, queue window, and optional seed.
- In-game HUD with run timer, current rank, queue card, and recent run history.

## Controls

- `F8`: Toggle HUD.
- `F7`: Queue or unqueue for a ranked race.
- `F6`: Start a race.
- `F5`: Finish the active race.
- `F9`: Cancel the active race.
- `F10`: Copy the current matchmaking code.

Controls, route name, par time, queue window, runner name, and auto-finish scene tokens can be changed in the generated BepInEx config file after first launch.

## Matchmaking

PeakRaceLeague does not run an external matchmaking server. Queueing generates a deterministic race card and queue code that runners can share in Discord, Steam chat, or another community space. Runners with the same route, rank band, queue window, and seed receive the same matchmaking code.

## Building

Build with:

```sh
dotnet build PeakRaceLeague.slnx
```
