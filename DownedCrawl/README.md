# Downed Crawl

Downed Crawl lets downed PEAK players move slowly while they are waiting for help. The crawl does not change the vanilla death timer or revive flow; it only applies local movement force while the player is passed out and not being carried.

## Configuration

The mod creates a BepInEx config file after first launch.

- `Enabled`: toggles the mod.
- `CrawlSpeed`: normal downed crawl speed, from `0` to `1` where `1` is normal walking speed.
- `SprintCrawlSpeed`: faster crawl speed after the death bar reaches the configured unlock point.
- `SprintUnlockDeathBar`: death bar fraction required before holding sprint enables the faster crawl. The default is `0.5`.
- `CrawlDrag`: extra drag while crawling to reduce sliding.

## Installation

Install with BepInExPack PEAK. Place `AeralisFoundation.DownedCrawl.dll` in `BepInEx/plugins/`, or install the Thunderstore package.

## Building

Build from the mod root:

```sh
dotnet build .\DownedCrawl.slnx -c Release
```
