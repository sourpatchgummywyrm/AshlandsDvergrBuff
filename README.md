# Ashlands Dvergr Buff

A Valheim BepInEx mod that makes Ashlands Dvergrs have that iron in them, featuring star-tiered visuals, boss VFX, separate mage scaling, ambient biome patrols, variety spawns, and 2-star advanced weapons.

## Features

- **0★ & 1★ Keep Default Weapons; 2★ Wield Advanced Weapons**:
  - **0-Star and 1-Star Dvergrs**: Wield their default Ashlands Dvergr weapons (e.g. `DvergerArbalest`) with standard scaled damage.
  - **2-Star Ashlord Tier Dvergrs**: Equip and wield their equivalent Ashlands Blood Weapon or fully upgraded staff with Bloodstone missing-HP scaling:
    - Crossbowmen wield the **Wound Ripper** (`CrossbowRipperBlood`).
    - Fire Mages wield the **Staff of Fracturing** (`StaffClusterbomb`).
    - Ice Mages wield **Dundr** (`StaffLightning`).
    - Support Mages wield the **Staff of Frost** (`StaffIceShards`).
- **Tuned Health & Proportions**:
  - **Warriors & Rangers**: Configurable `HealthMultiplier` (default `1.20x` = 720 HP base) and `DamageMultiplier` (default `1.25x`). Star damage scales +25% at 1★; 2★ skips default star damage scaling since advanced weapons already provide high-tier stats.
  - **Dvergr Mages** (Fire, Ice, Support): Configurable `MageHealthMultiplier` (default `1.08x` = 486 HP base) and `MageDamageMultiplier` (default `1.10x`), keeping magic damage balanced and preserving the glass-cannon fantasy.
  - Star health scaling is gentle (+25% HP per star: 0★ = 720 HP, 1★ = 900 HP, 2★ = 1080 HP for warriors; vanilla is +100% per star).
  - Subtle visual scaling preserves dwarf stature (1★ = `1.05x`, 2★ = `1.10x`).
- **Variety Dvergr Spawns in Ashlands**:
  - `VarietyDvergrSpawnChance` (default 30%): Yields an exact 70% / 10% / 10% / 10% distribution across Ashlands Crossbow Rangers, Fire Mages, Ice Mages, and Support Mages in ruins and ambient patrols.
- **Ambient Biome Spawns**:
  - Patrols spawn naturally across the Ashlands biome outside lava hazards (configurable group sizes, spawn intervals, and zone caps).
  - Spawner rates and ruin respawns are boosted.
- **Slow Passive Regeneration**:
  - Ashlands Dvergrs regenerate health passively (`PassiveHealPercentPerTick = 1.0%` every `5.0` seconds), scaling with star rank.
- **Star-Tier Visual Progression & Boss Visual Effects**:
  - **0★**: 100% original weathered ash-grey armor, blue-teal skin, cyan glowing eyes, and default arbalest (`1.0x` scale).
  - **1★ (Flameforged)**: Warm burnished bronze/copper armor, steely slate-blue skin, golden-amber glowing eyes, and default arbalest (`1.05x` scale).
  - **2★ (Ashlord / Boss Tier)**: Crimson blood-iron armor with rune vein glow, dark slate skin, purple glowing eyes, purple weapon glow, and swirling boss flames and nova spark ring wielding the Wound Ripper or upgraded Magic Staff (`1.10x` scale).

---

## Configuration

The config file is located at `BepInEx/config/com.valheim.ashlandsdvergrmod.cfg` and supports live in-game reloads:

```ini
[1. Spawning & Respawn Rates]
SpawnRateMultiplier = 3
CampRespawnIntervalMinutes = 6.67
VarietyDvergrSpawnChance = 30

[2. Ambient Spawns]
EnableAmbientAshlandsSpawns = true
AmbientMaxSpawned = 6
AmbientSpawnInterval = 100
AmbientSpawnChance = 50
AmbientGroupMin = 2
AmbientGroupMax = 4
AmbientSpawnRadiusMin = 40
AmbientSpawnRadiusMax = 80

[3. Combat Stats]
HealthMultiplier = 1.20
DamageMultiplier = 1.25
MageHealthMultiplier = 1.08
MageDamageMultiplier = 1.10
UseAshlandsBloodWeapons = true
PassiveHealPercentPerTick = 1.0
PassiveHealIntervalSeconds = 5.0

[4. Stars & Levels]
MinLevel = 1
MaxLevel = 3
LevelUpChance = 50
VisualScale1Star = 1.05
VisualScale2Star = 1.10
EnableCoolVisuals = true
Enable2StarBossEffects = true
AuraLightIntensity = 0.6
```

---

## Installation

### Mod Manager (r2modman / Thunderstore / Gale)
1. Search for **Ashlands Dvergr Buff** or install from Thunderstore.
2. Launch Valheim via the mod manager.

### Manual Installation
1. Ensure [BepInExPack Valheim](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/) is installed.
2. Extract `AshlandsDvergrMod.dll` into your `<Valheim>/BepInEx/plugins/` directory.
3. Launch Valheim.
