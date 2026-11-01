# Ashlands Dvergr Buff

A Valheim BepInEx mod that turns Ashlands Dvergrs into the formidable allies (or deadly adversaries) they were meant to be in the harsh southern biome.

## Features

- **Spawn Rate & Density (3x by default)**:
  - Triples group spawn caps, spawn chance, and spawner weights for all Ashlands Dvergr spawners (`Spawner_DvergerAshlands`) and ruins.
  - Divides respawn delays by 3 and enables respawning on ruin spawners (every ~6.7 minutes).
  - Adds **ambient Dvergr patrol spawns** throughout the Ashlands biome (customizable group size, interval, and cap), outside lava zones.
- **Stars & Levels (Up to 2-Star / Level 3)**:
  - Enables 1-star and 2-star Dvergrs in the Ashlands with a configurable 50% level-up chance per roll.
  - Dynamically injects `LevelEffects` scale and tint setups so 1-star and 2-star Dvergrs visibly scale up (1.1x / 1.2x) alongside their HUD stars.
- **Health (3x base, scaling to 9x at 2-Star)**:
  - Triples the base health of `DvergerAshlands` from 600 to 1800.
  - Automatically scales max and current health with star rank (Level 1: 1800 HP, Level 2: 3600 HP, Level 3: 5400 HP).
- **Damage (2x base multiplier)**:
  - Doubles outgoing damage across melee strikes, Arbalest bolts, and mage spells/AOEs, stacking with the vanilla +50% per star level bonus.
- **Fully Configurable**:
  - Every multiplier, level range, and spawn rate can be tuned in real time via configuration.

---

## Configuration

A config file is automatically generated at `BepInEx/config/com.valheim.ashlandsdvergrmod.cfg` after launching the game once:

```ini
[1. Spawning]
## Multiplier for Ashlands Dvergr spawn rate, spawn chance, and max spawn caps.
# Setting type: Single
# Default value: 3
SpawnRateMultiplier = 3

## Add ambient Dvergr spawns across the Ashlands biome in addition to camp/ruin spawners.
# Setting type: Boolean
# Default value: true
EnableAmbientAshlandsSpawns = true

## Max active ambient Dvergrs per Ashlands zone.
# Setting type: Int32
# Default value: 6
AmbientMaxSpawned = 6

## Seconds between ambient Ashlands Dvergr spawn checks (divided by SpawnRateMultiplier).
# Setting type: Single
# Default value: 100
AmbientSpawnInterval = 100

## Percent chance (0-100) to spawn ambient Dvergrs per interval.
# Setting type: Single
# Default value: 50
AmbientSpawnChance = 50

## Minimum group size for ambient Ashlands Dvergr spawns.
# Setting type: Int32
# Default value: 2
AmbientGroupMin = 2

## Maximum group size for ambient Ashlands Dvergr spawns.
# Setting type: Int32
# Default value: 4
AmbientGroupMax = 4

[2. Stats]
## Base health multiplier for Dvergrs in the Ashlands (scales further with star level).
# Setting type: Single
# Default value: 3
HealthMultiplier = 3

## Outgoing damage multiplier for Dvergrs in the Ashlands.
# Setting type: Single
# Default value: 2
DamageMultiplier = 2

[3. Stars]
## Minimum level for Ashlands Dvergrs (1 = 0 stars, 2 = 1 star, 3 = 2 stars).
# Setting type: Int32
# Default value: 1
MinLevel = 1

## Maximum level for Ashlands Dvergrs (1 = 0 stars, 2 = 1 star, 3 = 2 stars).
# Setting type: Int32
# Default value: 3
MaxLevel = 3

## Percent chance (0-100) per star level-up roll for Ashlands Dvergrs.
# Setting type: Single
# Default value: 50
LevelUpChance = 50
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

## Compatibility

- **OS**: Windows, macOS, Linux.
- **Multiplayer**: In multiplayer, this mod must be installed on the **server / world host** (or whoever holds zone ownership) to control spawning, health, and star levels. Clients without the mod will still see and fight the buffed Dvergrs synced across the network.
