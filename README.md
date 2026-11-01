# Ashlands Dvergr Buff

A Valheim BepInEx mod to make it so Ashlands Dvergrs have that iron in them.

## Features

- **Spawn Rate & Density (3x by default)**:
  - Triples group spawn caps, spawn chance, and spawner weights for all Ashlands Dvergr spawners (`Spawner_DvergerAshlands`) and ruins.
  - Divides respawn delays by 3 and enables respawning on ruin spawners (every ~6.7 minutes).
  - Adds **ambient Dvergr patrol spawns** throughout the Ashlands biome (customizable group size, interval, and cap), outside lava zones.
- **Stars & Levels (Up to 2-Star / Level 3)**:
  - Enables 1-star and 2-star Dvergrs in the Ashlands with a configurable 50% level-up chance per roll.
  - Dynamically injects `LevelEffects` scale and tint setups so 1-star and 2-star Dvergrs visibly scale up (1.1x / 1.2x) alongside their HUD stars.
- **Health (2x base, scaling to 6x at 2-Star)**:
  - Doubles the base health of `DvergerAshlands` from 600 to 1200 HP by default.
  - Automatically scales max and current health with star rank (Level 1: 1200 HP, Level 2: 2400 HP, Level 3: 3600 HP).
- **Damage (2x base multiplier)**:
  - Doubles outgoing damage across melee strikes, Arbalest bolts, and mage spells/AOEs, stacking with the vanilla +50% per star level bonus.
- **Slow Passive Healing**:
  - Ashlands Dvergrs slowly regenerate health out of or between skirmishes (default `2.0%` max HP every `5` seconds).
- **Flameforged Visuals**:
  - Star-ranked Ashlands Dvergrs scale up (`1.12x` / `1.25x`), gain crimson-bronze / obsidian-gold armor tinting, glowing molten runes/trim, and a flickering volcanic forge-ember aura light.
- **Fully Configurable (GUI & Text File)**:
  - All multipliers, spawn parameters, and star chances can be edited in real time via **BepInEx Configuration Manager** (press `F1` in-game) OR directly by editing the text configuration file.
  - Editing and saving the `.cfg` text file in Notepad / any text editor reloads settings live without needing to restart the game.

---

## Configuration

A config text file is automatically generated at `BepInEx/config/com.valheim.ashlandsdvergrmod.cfg` upon launching the game:

```ini
[1. Spawning]

## Global multiplier for Ashlands Dvergr spawn rate, spawn chance, group sizes, and spawner weights.
## Default: 3.0 (3x faster spawns, 3x capacity).
## Range: 0.5 to 10.0
# Setting type: Single
# Default value: 3
# Acceptable value range: From 0.5 to 10
SpawnRateMultiplier = 3

## Base respawn interval in minutes for Ashlands Dvergr ruin/camp spawners (divided by SpawnRateMultiplier).
## Default: 6.7 minutes.
## Range: 0.5 to 60.0
# Setting type: Single
# Default value: 6.7
# Acceptable value range: From 0.5 to 60
CampRespawnIntervalMinutes = 6.7

[2. Ambient Spawns]

## Add ambient Dvergr patrol spawns throughout the Ashlands biome in addition to camp/ruin spawners.
## Spawns are server/zone-owner authoritative outside lava.
## Default: true
# Setting type: Boolean
# Default value: true
EnableAmbientAshlandsSpawns = true

## Maximum active ambient Dvergrs allowed per Ashlands sector.
## Default: 6.
## Range: 1 to 30
# Setting type: Int32
# Default value: 6
# Acceptable value range: From 1 to 30
AmbientMaxSpawned = 6

## Base seconds between ambient spawn checks (divided by SpawnRateMultiplier).
## Default: 100 seconds.
## Range: 10.0 to 600.0
# Setting type: Single
# Default value: 100
# Acceptable value range: From 10 to 600
AmbientSpawnInterval = 100

## Percentage chance (0-100) to spawn ambient Dvergrs each interval check.
## Default: 50%.
## Range: 1.0 to 100.0
# Setting type: Single
# Default value: 50
# Acceptable value range: From 1 to 100
AmbientSpawnChance = 50

## Minimum group size for ambient Ashlands Dvergr patrols.
## Default: 2.
## Range: 1 to 10
# Setting type: Int32
# Default value: 2
# Acceptable value range: From 1 to 10
AmbientGroupMin = 2

## Maximum group size for ambient Ashlands Dvergr patrols.
## Default: 4.
## Range: 1 to 15
# Setting type: Int32
# Default value: 4
# Acceptable value range: From 1 to 15
AmbientGroupMax = 4

## Minimum distance in meters from players to spawn ambient Dvergrs.
## Default: 40 meters.
## Range: 10.0 to 100.0
# Setting type: Single
# Default value: 40
# Acceptable value range: From 10 to 100
AmbientSpawnRadiusMin = 40

## Maximum distance in meters from players to spawn ambient Dvergrs.
## Default: 80 meters.
## Range: 40.0 to 200.0
# Setting type: Single
# Default value: 80
# Acceptable value range: From 40 to 200
AmbientSpawnRadiusMax = 80

[3. Combat Stats]

## Base health multiplier for Dvergrs in the Ashlands (default: 2.0 = 2x base HP: 600 -> 1200).
## Higher star levels scale up from this base.
## Range: 0.5 to 10.0
# Setting type: Single
# Default value: 2
# Acceptable value range: From 0.5 to 10
HealthMultiplier = 2

## Outgoing damage multiplier for Ashlands Dvergrs (melee strikes, arbalest bolts, and magic AOEs).
## Default: 2.0 (2x damage, plus vanilla +50% per star level).
## Range: 0.5 to 10.0
# Setting type: Single
# Default value: 2
# Acceptable value range: From 0.5 to 10
DamageMultiplier = 2

[4. Stars & Levels]

## Minimum level for Ashlands Dvergrs (1 = 0 stars, 2 = 1 star, 3 = 2 stars).
## Default: 1 (0 stars).
## Range: 1 to 3
# Setting type: Int32
# Default value: 1
# Acceptable value range: From 1 to 3
MinLevel = 1

## Maximum level for Ashlands Dvergrs (1 = 0 stars, 2 = 1 star, 3 = 2 stars).
## Default: 3 (2 stars).
## Range: 1 to 3
# Setting type: Int32
# Default value: 3
# Acceptable value range: From 1 to 3
MaxLevel = 3

## Percentage chance (0-100) per star roll to upgrade to next rank.
## Default: 50%.
## Range: 0.0 to 100.0
# Setting type: Single
# Default value: 50
# Acceptable value range: From 0 to 100
LevelUpChance = 50

## Visual body scale multiplier for 1-star Ashlands Dvergrs.
## Default: 1.1 (10% larger).
## Range: 1.0 to 1.5
# Setting type: Single
# Default value: 1.1
# Acceptable value range: From 1 to 1.5
VisualScale1Star = 1.1

## Visual body scale multiplier for 2-star Ashlands Dvergrs.
## Default: 1.2 (20% larger).
## Range: 1.0 to 2.0
# Setting type: Single
# Default value: 1.2
# Acceptable value range: From 1 to 2
VisualScale2Star = 1.2
```

---

## Installation

### With Mod Manager (r2modman / Thunderstore / Gale)
1. Click **Install with Mod Manager**.
2. Run the game.

### Manual Installation
1. Ensure [BepInExPack Valheim](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/) is installed.
2. Extract `AshlandsDvergrMod.dll` into your `<Valheim>/BepInEx/plugins/` directory.
3. Launch the game.

---

## Multiplayer Notice

Valheim uses distributed sector authority: the player closest to an area (or the dedicated server) owns the sector and calculates creature spawns.
- For best results on a dedicated server, install this mod on the **server**.
- In peer-to-peer / co-op games, having this mod on the host / all players ensures seamless spawns and synchronized stats.
