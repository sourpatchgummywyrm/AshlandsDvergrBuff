# Changelog

## [2.0.0] - 2026-11-06
### Added
- 2★ Dvergrs equip equivalent Ashlands Blood Weapons or fully upgraded staves: Wound Ripper (`CrossbowRipperBlood`), Staff of Fracturing (`StaffClusterbomb`), Dundr (`StaffLightning`), and Staff of Frost (`StaffIceShards`).
- Removed weapon drop on death for 2★ Dvergrs (weapons stay equipped).
- Removed default star damage scaling increase from 1★ to 2★ (capped at 1★ 1.25x scaling) to prevent double-scaling with 2★ weapon buffs.
- Rebalanced spawn distribution to 70% Ashlands Crossbow Ranger, 10% Fire Mage, 10% Ice Mage, and 10% Support Mage (`VarietyDvergrSpawnChance = 30%`).
- Separate configurable mage health (`1.08x`) and damage (`1.10x`) multipliers.
- Star-tier visual progression with boss aura embers, purple eyes, and weapon glows.
- In-engine multi-figure validation render verifying all 4 active Ashlands Dvergr variants and weapon attachments.

## [1.1.3] - 2026-11-06
### Added
- Replaced 2★ Support Mage Trollstav with fully upgraded Staff of Frost (`StaffIceShards`).
- Removed weapon drop on death for all 2★ Dvergrs (weapons stay equipped).
- Default Ashlands weapons preserved for 0★ and 1★ Dvergrs.

## [1.0.0] - 2026-11-01
### Added
- Initial release.
- 3x spawn rate, spawn chance, group size caps, and weight boost for Ashlands Dvergr spawners.
- Ambient Dvergr patrol spawning across the Ashlands biome.
- Star scaling up to 2-star (Level 3) with custom `LevelEffects` visual scaling and tinting.
- 3x base health multiplier and automatic full health scaling on star roll.
- 2x damage multiplier across melee, projectile arbalests, and mage spells.
- Full BepInEx configuration file support (`com.valheim.ashlandsdvergrmod.cfg`).
- Cross-platform support for Windows, Linux, and macOS.
