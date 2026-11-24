# Ashlands Dvergr Buff

A Valheim BepInEx mod that gives Ashlands and Deep North Dvergrs the stats and gear to actually hold their own. It adds star ranks, mage spawns, ambient Ashlands patrols, and Quality 2 weapons on 2-star Dvergrs.

## Ashlands

Ruin spawners run twice as fast by default (`SpawnRateMultiplier = 2.0`), and small Dvergr patrols roam the biome away from lava. Half of all spawns are crossbow rangers and half are mages (Fire and Ice are on by default; Support is off).

Rangers start at `1300` HP (`1.30x`) and mages at `747.5` HP (`1.15x`). Each star adds `+25%` HP instead of vanilla's `+100%`, and every Dvergr heals `1%` of max HP every `5` seconds. Visual size stays close to vanilla (`1.05x` at 1-star, `1.10x` at 2-star), with bronze armor on 1-stars and crimson armor plus boss aura effects on 2-stars.

Player chain lightning will not hit or aggro Dvergrs unless a player attacks them directly first.

| Rank | Scaling & Weapons |
|---|---|
| 0-star | Vanilla weapons at `1.20x` damage (rangers) or `1.10x` (mages) |
| 1-star | Vanilla weapons at `+25%` damage over 0-star |
| 2-star | Quality 2 Ashlands weapons at exact Q2 base damage (scales up to `+50%` at low HP via Bloodstone; weapons do not drop on death) |

2-star Ashlands loadouts:
- Ranger: Wound Ripper (`CrossbowRipperBlood`, `223` pierce)
- Fire Mage: Staff of Fracturing (`StaffClusterbomb`, `130` fire + `12` blunt)
- Ice Mage: Dundr (`StaffLightning`, `23` lightning x 12 pellets)
- Support Mage (if enabled): Staff of Frost (`StaffIceShards`)

## Deep North

Deep North spawns split 50/50 between Nord Crossbow rangers (`1725` base HP) and Deep North mages (`1265` base HP, no healing). Star health scaling is the same `+25%` per star.

| Type | 0-star | 1-star | 2-star (Full HP -> Low HP) |
|---|---|---|---|
| Ranger | Nord Crossbow (`1.10x`, `290.4` pierce) | Nord Crossbow (`+25%`, `363.0` pierce) | Q2 Thunderblood Crossbow (`282` pierce + `50` lightning -> up to `+50%`) |
| Mage | Staff of Protection | Staff of Protection + Northern Vengeance | Q2 Lightning Strike only (`320` lightning -> up to `480`) |

## Config & Commands

Config lives at `BepInEx/config/com.valheim.ashlandsdvergrmod.cfg`. Edits reload live without restarting, and the host syncs settings to connected clients.

In-game commands (F5 console or chat with `/`):
- `dvergrstats` prints active HP, damage, and spawn settings for Ashlands and Deep North.
- `dvergrconfig` lists all keys; `dvergrconfig <key> <value>` updates a setting live (host/admin only).

```ini
[1. Spawning]
SpawnRateMultiplier = 2.0
CampRespawnIntervalMinutes = 6.7
VarietyDvergrSpawnChance = 50
EnableFireMageSpawns = true
EnableIceMageSpawns = true
EnableSupportMageSpawns = false

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
HealthMultiplier = 1.30
DamageMultiplier = 1.20
MageHealthMultiplier = 1.15
MageDamageMultiplier = 1.10
UseAshlandsBloodWeapons = true
PreventUnprovokedLightningFriendlyFire = true
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

[5. Deep North]
EnableDeepNorthBuff = true
DeepNorthHealthMultiplier = 1.15
DeepNorthDamageMultiplier = 1.10
DeepNorthMageSpawnChance = 50
```

## Install

With a mod manager (r2modman, Thunderstore, Gale): install **Ashlands Dvergr Buff** and launch Valheim.

Manually: install [BepInExPack Valheim](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/), drop `AshlandsDvergrMod.dll` into `<Valheim>/BepInEx/plugins/`, and launch the game.
