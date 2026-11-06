# Ashlands Dvergr Buff

A Valheim BepInEx mod to make it so Ashlands Dvergrs have that iron in them.

## Features

- **Spawn Rate, Density & Variety (3x by default + Mage/Rogue Variety)**:
  - Triples group spawn caps, spawn chance, and spawner weights for all Ashlands Dvergr spawners (`Spawner_DvergerAshlands`) and ruins.
  - Divides respawn delays by 3 and enables respawning on ruin spawners (every ~6.7 minutes).
  - Adds **ambient Dvergr patrol spawns** throughout the Ashlands biome (customizable group size, interval, and cap), outside lava zones.
  - **Variety Dvergr Spawns (`VarietyDvergrSpawnChance = 20%`)**: Configurable chance for Ashlands Dvergr spawns to include Fire Mages (`DvergerMageFire`), Ice Mages (`DvergerMageIce`), Support Mages (`DvergerMageSupport`), and classic `Dverger` variants scaled to Ashlands health standards.
- **Stars & Levels (Up to 2-Star / Level 3)**:
  - Enables 1-star and 2-star Dvergrs in the Ashlands with a configurable 50% level-up chance per roll.
  - Dynamically injects `LevelEffects` scale and tint setups so 1-star and 2-star Dvergrs visibly scale up (1.1x / 1.2x) alongside their HUD stars.
- **Health (1.35x base, +40% per star)**:
  - Increases the base health of `DvergerAshlands` from 600 to 810 HP by default (configurable `0.5` to `3.0`).
  - Scales max and current health smoothly with star rank (`+40%` per star: Level 1: 810 HP, Level 2: 1134 HP, Level 3: 1458 HP).
- **Ashlands Blood Weapons & Death Drop (`UseAshlandsBloodWeapons = true`)**:
  - Instead of flat damage scaling via `DamageMultiplier`, equips Ashlands Dvergrs with their equivalent **Ashlands Blood Weapon**:
    - **Crossbow wielders** (`DvergerAshlands`, `Dverger`): **Wound Ripper** (`CrossbowRipperBlood`)
    - **Fire Mages** (`DvergerMageFire`): **Staff of Fracturing** (`StaffClusterbomb`)
    - **Ice Mages** (`DvergerMageIce`): **Dundr** (`StaffLightning`)
    - **Support Mages** (`DvergerMageSupport`): **Trollstav** (`StaffRedTroll`)
  - Applies Ashlands Blood Weapon base damage (`1.35x`) plus the **Bloodstone missing-HP bonus** (`+0.5%` damage per `1%` missing HP, up to `+50%` at low health) and `+25%` per star.
  - **Drops on Death**: Ashlands Dvergrs drop their equipped Ashlands Blood Weapon on death (`BloodWeaponDropChance = 100%` by default, quality matching their star level 1–3).
  - If `UseAshlandsBloodWeapons = false`, falls back to `DamageMultiplier` (`1.25x` base, `+25%` per star).
- **Slow Passive Healing**:
  - Ashlands Dvergrs slowly regenerate health out of or between skirmishes (default `1.0%` max HP every `5` seconds).
- **Star-Tiered & 2-Star Boss Visuals (`Enable2StarBossEffects = true`)**:
  - **0-Star**: Original weathered tunic, cyan eyes, and equipped Blood Weapon.
  - **1-Star**: `1.1x` scale, burnished bronze/copper tunic, and golden-amber eyes.
  - **2-Star Boss Effects**: `1.2x` scale, volcanic crimson tunic with glowing boss veins (`_NoiseGlowEnabled`), vivid royal purple eyes, purple-arcane weapon glow, and **2-star Boss VFX** (crimson-violet blood-fire particles on hands & chest plus an arcane boss ring aura).
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
# Default value: 2
# Acceptable value range: From 0.5 to 10
SpawnRateMultiplier = 2

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
# Default value: 25
# Acceptable value range: From 1 to 100
AmbientSpawnChance = 25

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

## Base health multiplier for Dvergrs in the Ashlands (default: 1.35 = +35% base HP: 600 -> 810).
## Star levels add +40% HP per star (0-star: 810 HP, 1-star: 1134 HP, 2-star: 1458 HP).
## Range: 0.5 to 3.0
# Setting type: Single
# Default value: 1.35
# Acceptable value range: From 0.5 to 3
HealthMultiplier = 1.35

## Outgoing damage multiplier for Ashlands Dvergrs (melee strikes, arbalest bolts, and magic AOEs).
## Default: 1.25 (+25% base damage, with +25% per star level: 1.25x / 1.56x / 1.88x).
## Range: 0.5 to 3.0
# Setting type: Single
# Default value: 1.25
# Acceptable value range: From 0.5 to 3
DamageMultiplier = 1.25

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
# Default value: 20
# Acceptable value range: From 0 to 100
LevelUpChance = 20

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
# Default value: 1.15
# Acceptable value range: From 1 to 2
VisualScale2Star = 1.15
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
