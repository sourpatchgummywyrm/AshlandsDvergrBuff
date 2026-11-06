using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SoftReferenceableAssets;
using UnityEngine;

namespace AshlandsDvergrMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class AshlandsDvergrPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.valheim.ashlandsdvergrmod";
        public const string PluginName = "Ashlands Dvergr Boost";
        public const string PluginVersion = "2.0.0";

        internal static ManualLogSource Log;

        // Configuration entries
        internal static ConfigEntry<float> SpawnRateMultiplier;
        internal static ConfigEntry<float> CampRespawnIntervalMinutes;
        internal static ConfigEntry<float> VarietyDvergrSpawnChance;

        internal static ConfigEntry<bool> EnableAmbientAshlandsSpawns;
        internal static ConfigEntry<int> AmbientMaxSpawned;
        internal static ConfigEntry<float> AmbientSpawnInterval;
        internal static ConfigEntry<float> AmbientSpawnChance;
        internal static ConfigEntry<int> AmbientGroupMin;
        internal static ConfigEntry<int> AmbientGroupMax;
        internal static ConfigEntry<float> AmbientSpawnRadiusMin;
        internal static ConfigEntry<float> AmbientSpawnRadiusMax;

        internal static ConfigEntry<float> HealthMultiplier;
        internal static ConfigEntry<float> DamageMultiplier;
        internal static ConfigEntry<float> MageHealthMultiplier;
        internal static ConfigEntry<float> MageDamageMultiplier;
        internal static ConfigEntry<bool> UseAshlandsBloodWeapons;
        internal static ConfigEntry<float> PassiveHealPercentPerTick;
        internal static ConfigEntry<float> PassiveHealIntervalSeconds;

        internal static ConfigEntry<int> MinLevel;
        internal static ConfigEntry<int> MaxLevel;
        internal static ConfigEntry<float> LevelUpChance;
        internal static ConfigEntry<float> VisualScale1Star;
        internal static ConfigEntry<float> VisualScale2Star;
        internal static ConfigEntry<bool> EnableCoolVisuals;
        internal static ConfigEntry<bool> Enable2StarBossEffects;
        internal static ConfigEntry<float> AuraLightIntensity;

        internal static readonly string[] VarietyDvergrPrefabNames =
        {
            "DvergerMageFire",
            "DvergerMageIce",
            "DvergerMageSupport"
        };

        private static readonly HashSet<int> PatchedSpawnLists = new HashSet<int>();
        private static readonly HashSet<int> PatchedCreatureSpawners = new HashSet<int>();
        private static readonly HashSet<int> PatchedSpawnAreas = new HashSet<int>();
        private static readonly Dictionary<int, float> OriginalPrefabHealth = new Dictionary<int, float>();
        private static readonly Dictionary<string, GameObject> CachedSoftRefPrefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        private static FileSystemWatcher _configFileWatcher;
        private static DateTime _lastConfigReloadTime = DateTime.MinValue;

        private void Awake()
        {
            Log = Logger;

            int order = 100;

            // 1. Spawning - Multipliers & Ruin Spawners
            SpawnRateMultiplier = Config.Bind("1. Spawning", "SpawnRateMultiplier", 3.0f,
                new ConfigDescription(
                    "Global multiplier for Ashlands Dvergr spawn rate, spawn chance, group sizes, and spawner weights.\n" +
                    "Default: 3.0 (3x faster spawns, 3x capacity).\n" +
                    "Range: 0.5 to 10.0",
                    new AcceptableValueRange<float>(0.5f, 10.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            CampRespawnIntervalMinutes = Config.Bind("1. Spawning", "CampRespawnIntervalMinutes", 6.7f,
                new ConfigDescription(
                    "Base respawn interval in minutes for Ashlands Dvergr ruin/camp spawners (divided by SpawnRateMultiplier).\n" +
                    "Default: 6.7 minutes.\n" +
                    "Range: 0.5 to 60.0",
                    new AcceptableValueRange<float>(0.5f, 60.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            VarietyDvergrSpawnChance = Config.Bind("1. Spawning", "VarietyDvergrSpawnChance", 30f,
                new ConfigDescription(
                    "Percentage chance (0-100) for Ashlands Dvergr spawns to be a variety type (Fire Mage, Ice Mage, or Support Mage) instead of standard DvergerAshlands.\n" +
                    "Default: 30% (giving a 70% / 10% / 10% / 10% split among Crossbow / Fire / Ice / Support).\n" +
                    "Range: 0.0 to 100.0",
                    new AcceptableValueRange<float>(0.0f, 100.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            // 2. Ambient Spawning
            EnableAmbientAshlandsSpawns = Config.Bind("2. Ambient Spawns", "EnableAmbientAshlandsSpawns", true,
                new ConfigDescription(
                    "Add ambient Dvergr patrol spawns throughout the Ashlands biome in addition to camp/ruin spawners.\n" +
                    "Spawns are server/zone-owner authoritative outside lava.\n" +
                    "Default: true",
                    null,
                    new ConfigurationManagerAttributes { Order = --order }));

            AmbientMaxSpawned = Config.Bind("2. Ambient Spawns", "AmbientMaxSpawned", 6,
                new ConfigDescription(
                    "Maximum active ambient Dvergrs allowed per Ashlands sector.\n" +
                    "Default: 6.\n" +
                    "Range: 1 to 30",
                    new AcceptableValueRange<int>(1, 30),
                    new ConfigurationManagerAttributes { Order = --order }));

            AmbientSpawnInterval = Config.Bind("2. Ambient Spawns", "AmbientSpawnInterval", 100f,
                new ConfigDescription(
                    "Base seconds between ambient spawn checks (divided by SpawnRateMultiplier).\n" +
                    "Default: 100 seconds.\n" +
                    "Range: 10.0 to 600.0",
                    new AcceptableValueRange<float>(10f, 600f),
                    new ConfigurationManagerAttributes { Order = --order }));

            AmbientSpawnChance = Config.Bind("2. Ambient Spawns", "AmbientSpawnChance", 50f,
                new ConfigDescription(
                    "Percentage chance (0-100) to spawn ambient Dvergrs each interval check.\n" +
                    "Default: 50%.\n" +
                    "Range: 1.0 to 100.0",
                    new AcceptableValueRange<float>(1f, 100f),
                    new ConfigurationManagerAttributes { Order = --order }));

            AmbientGroupMin = Config.Bind("2. Ambient Spawns", "AmbientGroupMin", 2,
                new ConfigDescription(
                    "Minimum group size for ambient Ashlands Dvergr patrols.\n" +
                    "Default: 2.\n" +
                    "Range: 1 to 10",
                    new AcceptableValueRange<int>(1, 10),
                    new ConfigurationManagerAttributes { Order = --order }));

            AmbientGroupMax = Config.Bind("2. Ambient Spawns", "AmbientGroupMax", 4,
                new ConfigDescription(
                    "Maximum group size for ambient Ashlands Dvergr patrols.\n" +
                    "Default: 4.\n" +
                    "Range: 1 to 15",
                    new AcceptableValueRange<int>(1, 15),
                    new ConfigurationManagerAttributes { Order = --order }));

            AmbientSpawnRadiusMin = Config.Bind("2. Ambient Spawns", "AmbientSpawnRadiusMin", 40f,
                new ConfigDescription(
                    "Minimum distance in meters from players to spawn ambient Dvergrs.\n" +
                    "Default: 40 meters.\n" +
                    "Range: 10.0 to 100.0",
                    new AcceptableValueRange<float>(10f, 100f),
                    new ConfigurationManagerAttributes { Order = --order }));

            AmbientSpawnRadiusMax = Config.Bind("2. Ambient Spawns", "AmbientSpawnRadiusMax", 80f,
                new ConfigDescription(
                    "Maximum distance in meters from players to spawn ambient Dvergrs.\n" +
                    "Default: 80 meters.\n" +
                    "Range: 40.0 to 200.0",
                    new AcceptableValueRange<float>(40f, 200f),
                    new ConfigurationManagerAttributes { Order = --order }));

            // 3. Combat Stats & Passive Healing
            HealthMultiplier = Config.Bind("3. Combat Stats", "HealthMultiplier", 1.20f,
                new ConfigDescription(
                    "Base health multiplier for Dvergrs in the Ashlands (default: 1.20 = +20% base HP: 600 -> 720).\n" +
                    "Star levels add +25% HP per star (0-star: 720 HP, 1-star: 900 HP, 2-star: 1080 HP).\n" +
                    "Range: 0.5 to 3.0",
                    new AcceptableValueRange<float>(0.5f, 3.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            DamageMultiplier = Config.Bind("3. Combat Stats", "DamageMultiplier", 1.25f,
                new ConfigDescription(
                    "Outgoing damage multiplier for Ashlands Dvergrs when UseAshlandsBloodWeapons is false (melee strikes, arbalest bolts, and magic AOEs).\n" +
                    "Default: 1.25 (+25% base damage, +25% at 1-star; 2-star skips default star damage scaling due to weapon buff).\n" +
                    "Range: 0.5 to 3.0",
                    new AcceptableValueRange<float>(0.5f, 3.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            MageHealthMultiplier = Config.Bind("3. Combat Stats", "MageHealthMultiplier", 1.08f,
                new ConfigDescription(
                    "Separate base health multiplier for Dvergr Mages in the Ashlands (Fire, Ice, Support).\n" +
                    "Set lower than warriors/crossbowmen by default to balance glass-cannon magic power (default: 1.08 = +8% base HP: 450 -> 486).\n" +
                    "Range: 0.5 to 3.0",
                    new AcceptableValueRange<float>(0.5f, 3.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            MageDamageMultiplier = Config.Bind("3. Combat Stats", "MageDamageMultiplier", 1.10f,
                new ConfigDescription(
                    "Separate outgoing damage multiplier for Dvergr Mages in the Ashlands.\n" +
                    "Sets lower spell/AOE scaling by default (default: 1.10 = +10% base damage, or baseline staff ratio modifier).\n" +
                    "Range: 0.5 to 3.0",
                    new AcceptableValueRange<float>(0.5f, 3.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            UseAshlandsBloodWeapons = Config.Bind("3. Combat Stats", "UseAshlandsBloodWeapons", true,
                new ConfigDescription(
                    "Equip 2-star Ashlands Dvergrs with their equivalent Ashlands Blood Weapon / upgraded magic staff (Wound Ripper for crossbowmen, Staff of Fracturing / Dundr / fully upgraded Staff of Frost for mages) with Bloodstone missing-HP damage scaling (0 and 1-star Dvergrs wield default weapons).\n" +
                    "Default: true",
                    null,
                    new ConfigurationManagerAttributes { Order = --order }));

            PassiveHealPercentPerTick = Config.Bind("3. Combat Stats", "PassiveHealPercentPerTick", 1.0f,
                new ConfigDescription(
                    "Percentage of max health regenerated per heal tick by Ashlands Dvergrs (0 = disabled).\n" +
                    "Default: 1.0% every 5 seconds (~8 HP/tick at 0-star, ~15 HP/tick at 2-star).\n" +
                    "Range: 0.0 to 10.0",
                    new AcceptableValueRange<float>(0.0f, 10.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            PassiveHealIntervalSeconds = Config.Bind("3. Combat Stats", "PassiveHealIntervalSeconds", 5.0f,
                new ConfigDescription(
                    "Seconds between passive regeneration ticks for Ashlands Dvergrs.\n" +
                    "Default: 5.0 seconds.\n" +
                    "Range: 1.0 to 60.0",
                    new AcceptableValueRange<float>(1.0f, 60.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            // 4. Stars, Levels & Visuals
            MinLevel = Config.Bind("4. Stars & Levels", "MinLevel", 1,
                new ConfigDescription(
                    "Minimum level for Ashlands Dvergrs (1 = 0 stars, 2 = 1 star, 3 = 2 stars).\n" +
                    "Default: 1 (0 stars).\n" +
                    "Range: 1 to 3",
                    new AcceptableValueRange<int>(1, 3),
                    new ConfigurationManagerAttributes { Order = --order }));

            MaxLevel = Config.Bind("4. Stars & Levels", "MaxLevel", 3,
                new ConfigDescription(
                    "Maximum level for Ashlands Dvergrs (1 = 0 stars, 2 = 1 star, 3 = 2 stars).\n" +
                    "Default: 3 (2 stars).\n" +
                    "Range: 1 to 3",
                    new AcceptableValueRange<int>(1, 3),
                    new ConfigurationManagerAttributes { Order = --order }));

            LevelUpChance = Config.Bind("4. Stars & Levels", "LevelUpChance", 50f,
                new ConfigDescription(
                    "Percentage chance (0-100) per star roll to upgrade to next rank.\n" +
                    "Default: 50%.\n" +
                    "Range: 0.0 to 100.0",
                    new AcceptableValueRange<float>(0f, 100f),
                    new ConfigurationManagerAttributes { Order = --order }));

            VisualScale1Star = Config.Bind("4. Stars & Levels", "VisualScale1Star", 1.05f,
                new ConfigDescription(
                    "Visual body scale multiplier for 1-star Ashlands Dvergrs.\n" +
                    "Default: 1.05 (5% larger).\n" +
                    "Range: 1.0 to 1.5",
                    new AcceptableValueRange<float>(1.0f, 1.5f),
                    new ConfigurationManagerAttributes { Order = --order }));

            VisualScale2Star = Config.Bind("4. Stars & Levels", "VisualScale2Star", 1.10f,
                new ConfigDescription(
                    "Visual body scale multiplier for 2-star Ashlands Dvergrs.\n" +
                    "Default: 1.10 (10% larger).\n" +
                    "Range: 1.0 to 2.0",
                    new AcceptableValueRange<float>(1.0f, 2.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            EnableCoolVisuals = Config.Bind("4. Stars & Levels", "EnableCoolVisuals", true,
                new ConfigDescription(
                    "Enhances starred Ashlands Dvergrs with subtle veteran armor shading and a soft warm rim light while preserving their original textures.\n" +
                    "Default: true",
                    null,
                    new ConfigurationManagerAttributes { Order = --order }));

            Enable2StarBossEffects = Config.Bind("4. Stars & Levels", "Enable2StarBossEffects", true,
                new ConfigDescription(
                    "Adds boss visual effects (glowing boss aura, hand & chest boss energy particles, and arcane boss runes) to 2-star Ashlands Dvergrs.\n" +
                    "Default: true",
                    null,
                    new ConfigurationManagerAttributes { Order = --order }));

            AuraLightIntensity = Config.Bind("4. Stars & Levels", "AuraLightIntensity", 0.6f,
                new ConfigDescription(
                    "Intensity multiplier for the subtle warm rim light on Ashlands Dvergrs (0 = disable light).\n" +
                    "Default: 0.6.\n" +
                    "Range: 0.0 to 3.0",
                    new AcceptableValueRange<float>(0.0f, 3.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            // Text-file support: Save immediately so the .cfg file exists on disk with full comments
            Config.Save();

            // Hook live changes from GUI or code
            Config.SettingChanged += OnSettingChanged;
            Config.ConfigReloaded += OnConfigReloaded;

            // Watch .cfg file on disk for manual edits in any text editor
            SetupConfigFileWatcher();

            if (Environment.GetEnvironmentVariable("RENDER_DVERGR_VALIDATION") == "1")
            {
                Runtime.MakeAllAssetsLoadable();
                StartCoroutine(RenderDvergrValidationCoroutine());
            }

            new Harmony(PluginGuid).PatchAll(typeof(AshlandsDvergrPlugin));
            Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void SetupConfigFileWatcher()
        {
            try
            {
                string configFilePath = Config.ConfigFilePath;
                string directory = Path.GetDirectoryName(configFilePath);
                string fileName = Path.GetFileName(configFilePath);

                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
                {
                    return;
                }

                _configFileWatcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                _configFileWatcher.Changed += OnConfigFileDiskChanged;
                _configFileWatcher.Created += OnConfigFileDiskChanged;
                _configFileWatcher.Renamed += OnConfigFileDiskChanged;
                Log.LogInfo($"Watching text config file for edits: {configFilePath}");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Could not set up text config file watcher: {ex.Message}");
            }
        }

        private static void OnConfigFileDiskChanged(object sender, FileSystemEventArgs e)
        {
            if ((DateTime.Now - _lastConfigReloadTime).TotalMilliseconds < 1000)
            {
                return;
            }
            _lastConfigReloadTime = DateTime.Now;

            try
            {
                Log.LogInfo("Text config file modified on disk. Reloading settings...");
                ((AshlandsDvergrPlugin)BepInEx.Bootstrap.Chainloader.PluginInfos[PluginGuid].Instance).Config.Reload();
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to reload text config file: {ex.Message}");
            }
        }

        private static void OnConfigReloaded(object sender, EventArgs e)
        {
            RefreshPrefabStats();
        }

        private static void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            RefreshPrefabStats();
        }

        private static void RefreshPrefabStats()
        {
            if (ZNetScene.instance == null)
            {
                return;
            }

            string[] dvergrPrefabsToRefresh = { "DvergerAshlands", "DvergerMageFire", "DvergerMageIce", "DvergerMageSupport", "Dverger" };
            foreach (string pName in dvergrPrefabsToRefresh)
            {
                GameObject go = ZNetScene.instance.GetPrefab(pName);
                if (go != null)
                {
                    Character c = go.GetComponent<Character>();
                    if (c != null && OriginalPrefabHealth.TryGetValue(go.GetInstanceID(), out float baseHp))
                    {
                        float hpMult = GetHealthMultiplierFor(c);
                        c.m_health = baseHp * hpMult;
                        EnsureVisualStarSetups(c);
                        Log.LogInfo($"Refreshed {pName} prefab base health to {c.m_health} (mult={hpMult}).");
                    }
                }
            }

            foreach (AshlandsDvergrEnhancer enhancer in UnityEngine.Object.FindObjectsByType<AshlandsDvergrEnhancer>(FindObjectsSortMode.None))
            {
                if (enhancer != null)
                {
                    enhancer.ApplyCoolVisuals();
                }
            }
        }

        internal static bool IsDvergrPrefab(GameObject go)
        {
            if (go == null)
            {
                return false;
            }
            Character ch = go.GetComponent<Character>();
            if (ch != null && ch.m_faction == Character.Faction.Dverger)
            {
                return true;
            }
            string name = go.name;
            return name.IndexOf("Dverger", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Dvergr", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsInAshlands(Vector3 pos)
        {
            if (WorldGenerator.IsAshlands(pos.x, pos.z))
            {
                return true;
            }
            if (WorldGenerator.instance != null && (WorldGenerator.instance.GetBiome(pos) & Heightmap.Biome.AshLands) != 0)
            {
                return true;
            }
            return false;
        }

        internal static bool IsAshlandsDvergr(Character ch)
        {
            if (ch == null || ch.IsPlayer())
            {
                return false;
            }
            bool isDvergr = ch.m_faction == Character.Faction.Dverger ||
                            ch.gameObject.name.IndexOf("Dverger", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            ch.gameObject.name.IndexOf("Dvergr", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isDvergr)
            {
                return false;
            }
            if (ch.gameObject.name.IndexOf("Ashlands", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            return IsInAshlands(ch.transform.position);
        }

        internal static bool IsDvergrMage(Character ch)
        {
            if (ch == null)
            {
                return false;
            }
            return IsDvergrMageName(ch.gameObject.name);
        }

        internal static bool IsDvergrMageName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            return name.IndexOf("Mage", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static float GetHealthMultiplierFor(Character ch)
        {
            return IsDvergrMage(ch) ? Mathf.Max(0.1f, MageHealthMultiplier.Value) : Mathf.Max(0.1f, HealthMultiplier.Value);
        }

        internal static float GetDamageMultiplierFor(Character ch)
        {
            return IsDvergrMage(ch) ? Mathf.Max(0.1f, MageDamageMultiplier.Value) : Mathf.Max(0.1f, DamageMultiplier.Value);
        }

        internal static int RollConfiguredLevel()
        {
            int min = Mathf.Max(1, MinLevel.Value);
            int max = Mathf.Max(min, MaxLevel.Value);
            float chance = Mathf.Clamp(LevelUpChance.Value, 0f, 100f);
            int level = min;
            while (level < max && UnityEngine.Random.Range(0f, 100f) <= chance)
            {
                level++;
            }
            return level;
        }

        internal static void EnsureVisualStarSetups(Character ch)
        {
            LevelEffects le = ch.GetComponentInChildren<LevelEffects>();
            if (le == null || le.m_levelSetups == null)
            {
                return;
            }
            if (le.m_mainRender == null)
            {
                le.m_mainRender = ch.GetComponentInChildren<SkinnedMeshRenderer>();
            }

            float s1 = Mathf.Max(1f, VisualScale1Star.Value);
            float s2 = Mathf.Max(s1, VisualScale2Star.Value);
            bool tint = EnableCoolVisuals.Value;

            // Keep LevelEffects scale synced with star level, but leave material styling to ApplyStyleToHierarchy
            // so skin/eyes (DvergerBody) and equipment/cloaks (VisEquipment) are styled separately without washing out textures.
            if (le.m_levelSetups.Count == 0)
            {
                le.m_levelSetups.Add(new LevelEffects.LevelSetup
                {
                    m_scale = s1,
                    m_hue = 0f,
                    m_saturation = 0f,
                    m_value = 0f,
                    m_setEmissiveColor = false
                });
            }
            else
            {
                le.m_levelSetups[0].m_scale = s1;
                le.m_levelSetups[0].m_hue = 0f;
                le.m_levelSetups[0].m_saturation = 0f;
                le.m_levelSetups[0].m_value = 0f;
                le.m_levelSetups[0].m_setEmissiveColor = false;
            }

            if (le.m_levelSetups.Count == 1)
            {
                le.m_levelSetups.Add(new LevelEffects.LevelSetup
                {
                    m_scale = s2,
                    m_hue = 0f,
                    m_saturation = 0f,
                    m_value = 0f,
                    m_setEmissiveColor = false
                });
            }
            else if (le.m_levelSetups.Count >= 2)
            {
                le.m_levelSetups[1].m_scale = s2;
                le.m_levelSetups[1].m_hue = 0f;
                le.m_levelSetups[1].m_saturation = 0f;
                le.m_levelSetups[1].m_value = 0f;
                le.m_levelSetups[1].m_setEmissiveColor = false;
            }
        }

        internal static string GetEquivalentBloodWeaponPrefabName(string characterName)
        {
            if (string.IsNullOrEmpty(characterName))
            {
                return "CrossbowRipperBlood";
            }
            if (characterName.IndexOf("MageFire", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "StaffClusterbomb";
            }
            if (characterName.IndexOf("MageIce", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "StaffLightning";
            }
            if (characterName.IndexOf("MageSupport", StringComparison.OrdinalIgnoreCase) >= 0 ||
                characterName.IndexOf("Mage", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "StaffIceShards";
            }
            if (string.Equals(characterName, "Dverger", StringComparison.OrdinalIgnoreCase) ||
                (characterName.IndexOf("Dverger", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 characterName.IndexOf("Ashlands", StringComparison.OrdinalIgnoreCase) < 0 &&
                 characterName.IndexOf("Mage", StringComparison.OrdinalIgnoreCase) < 0))
            {
                return "BattleaxeSkullSplittur";
            }
            return "CrossbowRipperBlood";
        }

        internal static GameObject ResolvePrefabByNameOrAssetId(string prefabName, string assetIdHex = null)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                return null;
            }
            if (ObjectDB.instance != null)
            {
                GameObject item = ObjectDB.instance.GetItemPrefab(prefabName);
                if (item != null)
                {
                    return item;
                }
            }
            if (ZNetScene.instance != null)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
                if (prefab != null)
                {
                    return prefab;
                }
            }
            if (CachedSoftRefPrefabs.TryGetValue(prefabName, out GameObject cached) && cached != null)
            {
                return cached;
            }
            if (string.IsNullOrEmpty(assetIdHex))
            {
                assetIdHex = prefabName switch
                {
                    "DvergerAshlands" => "2ece1cf15f2885a4da6da84af9d5c542",
                    "DvergerMageFire" => "1ed38d388ceda5f4e89f5be4d1768fab",
                    "DvergerMageIce" => "8ad104a536dd94d4bb764f8a9f3d8ee7",
                    "DvergerMageSupport" => "c0e85e3d180619047bdf97cd98271a4d",
                    "Dverger" => "b780f1078c33d2344836ccefd3bf9352",
                    "CrossbowRipperBlood" => "483a950c53776e74ea40aef310fefbb2",
                    "StaffClusterbomb" => "d4812897e4412e540b7fa649d25b99d5",
                    "StaffLightning" => "90f15fa074aa08a478750566117d84d0",
                    "StaffRedTroll" => "87eebe8749379744eacfa37ea818aeab",
                    "StaffIceShards" => "d268d47613aec2963b1878ed2a224e1e",
                    "BattleaxeSkullSplittur" => "131b6e64eaee51547a0b76f472190581",
                    "fx_fader_chestfire" => "affcfc1b631dafe4ba5eb9388ec6196a",
                    "fx_goblinking_nova_hand" => "ebf058290cad64993843155e16b4b6f6",
                    "fx_DvergerMage_Nova_ring" => "c1b9194e4cba6e648aacfd970dd12da9",
                    "vfx_crossbow_blood_fire" => "50280cda7c9d5f64b96020f59ae3f945",
                    _ => null
                };
            }
            if (!string.IsNullOrEmpty(assetIdHex) && AssetID.TryParse(assetIdHex, out AssetID assetId))
            {
                try
                {
                    SoftReference<GameObject> sr = new SoftReference<GameObject>(assetId);
                    sr.Load();
                    if (sr.Asset != null)
                    {
                        CachedSoftRefPrefabs[prefabName] = sr.Asset;
                        return sr.Asset;
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        private static void ZNetScene_Awake_Postfix(ZNetScene __instance)
        {
            if (__instance == null)
            {
                return;
            }

            string[] dvergrPrefabs = { "DvergerAshlands", "DvergerMageFire", "DvergerMageIce", "DvergerMageSupport", "Dverger" };
            foreach (string pName in dvergrPrefabs)
            {
                GameObject go = __instance.GetPrefab(pName);
                if (go != null)
                {
                    Character ch = go.GetComponent<Character>();
                    if (ch != null)
                    {
                        int id = go.GetInstanceID();
                        if (!OriginalPrefabHealth.TryGetValue(id, out float baseHp))
                        {
                            baseHp = ch.m_health;
                            OriginalPrefabHealth[id] = baseHp;
                        }
                        float hpMult = GetHealthMultiplierFor(ch);
                        ch.m_health = baseHp * hpMult;
                        EnsureVisualStarSetups(ch);
                        Log.LogInfo($"Configured {pName} prefab base health: {baseHp} -> {ch.m_health} (mult={hpMult})");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Character), "Awake")]
        [HarmonyPrefix]
        private static void Character_Awake_Prefix(Character __instance)
        {
            if (!IsAshlandsDvergr(__instance))
            {
                return;
            }
            EnsureVisualStarSetups(__instance);
            if (__instance.gameObject.name.IndexOf("Ashlands", StringComparison.OrdinalIgnoreCase) < 0)
            {
                // Elevate variety Mistlands Dvergrs (Mages, classic Dverger) spawned in Ashlands to Ashlands base HP standard before multiplier
                // Mages get base 450 (higher than vanilla 200, but lower than Ashlands ranger 600 base); warriors/rogues get 600 base
                bool isMage = IsDvergrMage(__instance);
                float baseStandard = isMage ? 450f : 600f;
                float hpMult = GetHealthMultiplierFor(__instance);
                float ashlandsBaseHp = Mathf.Max(__instance.m_health, baseStandard);
                __instance.m_health = ashlandsBaseHp * hpMult;
            }
        }

        [HarmonyPatch(typeof(Character), "Awake")]
        [HarmonyPostfix]
        private static void Character_Awake_Postfix(Character __instance, ZNetView ___m_nview)
        {
            if (!IsAshlandsDvergr(__instance))
            {
                return;
            }

            if (__instance.GetComponent<AshlandsDvergrEnhancer>() == null)
            {
                __instance.gameObject.AddComponent<AshlandsDvergrEnhancer>();
            }

            if (___m_nview == null || !___m_nview.IsValid() || !___m_nview.IsOwner())
            {
                return;
            }
            ZDO zdo = ___m_nview.GetZDO();
            if (zdo == null)
            {
                return;
            }
            const string RollKey = "AshlandsDvergrMod_Initialized";
            if (!zdo.GetBool(RollKey))
            {
                zdo.Set(RollKey, true);
                int rolledLevel = RollConfiguredLevel();
                if (rolledLevel > __instance.GetLevel())
                {
                    __instance.SetLevel(rolledLevel);
                }
                float maxHp = CalculateScaledMaxHealth(__instance);
                __instance.SetMaxHealth(maxHp);
                __instance.SetHealth(maxHp);
            }
        }

        private static float CalculateScaledMaxHealth(Character ch)
        {
            int stars = Mathf.Max(0, ch.GetLevel() - 1);
            // +25% HP per star (1.0x / 1.25x / 1.50x) instead of vanilla +100% per star (1x / 2x / 3x)
            float starMult = 1f + stars * 0.25f;
            return ch.GetMaxHealthBase() * starMult;
        }

        [HarmonyPatch(typeof(Character), "SetLevel")]
        [HarmonyPostfix]
        private static void Character_SetLevel_Postfix(Character __instance)
        {
            if (!IsAshlandsDvergr(__instance))
            {
                return;
            }
            if (__instance.IsOwner())
            {
                float maxHp = CalculateScaledMaxHealth(__instance);
                __instance.SetMaxHealth(maxHp);
                __instance.SetHealth(maxHp);
            }
            AshlandsDvergrEnhancer enhancer = __instance.GetComponent<AshlandsDvergrEnhancer>();
            if (enhancer != null)
            {
                enhancer.ApplyCoolVisuals();
            }
        }

        [HarmonyPatch(typeof(CreatureSpawner), "Awake")]
        [HarmonyPrefix]
        private static void CreatureSpawner_Awake_Prefix(CreatureSpawner __instance)
        {
            if (__instance == null || __instance.m_creaturePrefab == null)
            {
                return;
            }
            int id = __instance.GetInstanceID();
            if (PatchedCreatureSpawners.Contains(id))
            {
                return;
            }
            bool isAshlandsDvergrSpawner =
                __instance.gameObject.name.IndexOf("DvergerAshlands", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (IsDvergrPrefab(__instance.m_creaturePrefab) &&
                 (__instance.m_creaturePrefab.name.IndexOf("Ashlands", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  IsInAshlands(__instance.transform.position)));

            if (!isAshlandsDvergrSpawner)
            {
                return;
            }

            PatchedCreatureSpawners.Add(id);
            float mult = Mathf.Max(0.1f, SpawnRateMultiplier.Value);
            __instance.m_minLevel = Mathf.Max(__instance.m_minLevel, MinLevel.Value);
            __instance.m_maxLevel = Mathf.Max(__instance.m_maxLevel, MaxLevel.Value);
            __instance.m_levelupChance = Mathf.Max(__instance.m_levelupChance, LevelUpChance.Value);

            float baseInterval = Mathf.Max(0.5f, CampRespawnIntervalMinutes.Value);
            __instance.m_respawnTimeMinuts = Mathf.Max(0.2f, baseInterval / mult);

            __instance.m_spawnInterval = Mathf.Max(1, Mathf.RoundToInt(__instance.m_spawnInterval / mult));
            if (__instance.m_maxGroupSpawned > 0)
            {
                __instance.m_maxGroupSpawned = Mathf.CeilToInt(__instance.m_maxGroupSpawned * mult);
            }
            __instance.m_spawnerWeight *= mult;
        }

        [HarmonyPatch(typeof(CreatureSpawner), "Spawn")]
        [HarmonyPrefix]
        private static void CreatureSpawner_Spawn_Prefix(CreatureSpawner __instance, Location ___m_location, out GameObject __state)
        {
            __state = null;
            if (__instance == null || __instance.m_creaturePrefab == null)
            {
                return;
            }
            if (IsDvergrPrefab(__instance.m_creaturePrefab) &&
                (__instance.m_creaturePrefab.name.IndexOf("Ashlands", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 IsInAshlands(__instance.transform.position)))
            {
                if (___m_location != null)
                {
                    if (___m_location.m_enemyMinLevelOverride >= 0)
                    {
                        ___m_location.m_enemyMinLevelOverride = Mathf.Max(___m_location.m_enemyMinLevelOverride, MinLevel.Value);
                    }
                    if (___m_location.m_enemyMaxLevelOverride >= 0)
                    {
                        ___m_location.m_enemyMaxLevelOverride = Mathf.Max(___m_location.m_enemyMaxLevelOverride, MaxLevel.Value);
                    }
                    if (___m_location.m_enemyLevelUpOverride >= 0f)
                    {
                        ___m_location.m_enemyLevelUpOverride = Mathf.Max(___m_location.m_enemyLevelUpOverride, LevelUpChance.Value);
                    }
                }

                float varietyChance = Mathf.Clamp(VarietyDvergrSpawnChance.Value, 0f, 100f);
                if (varietyChance > 0f && ZNetScene.instance != null && UnityEngine.Random.Range(0f, 100f) < varietyChance)
                {
                    string pickName = VarietyDvergrPrefabNames[UnityEngine.Random.Range(0, VarietyDvergrPrefabNames.Length)];
                    GameObject altPrefab = ZNetScene.instance.GetPrefab(pickName);
                    if (altPrefab != null)
                    {
                        __state = __instance.m_creaturePrefab;
                        __instance.m_creaturePrefab = altPrefab;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(CreatureSpawner), "Spawn")]
        [HarmonyPostfix]
        private static void CreatureSpawner_Spawn_Postfix(CreatureSpawner __instance, GameObject __state)
        {
            if (__instance != null && __state != null)
            {
                __instance.m_creaturePrefab = __state;
            }
        }

        [HarmonyPatch(typeof(RandomSpawn), "Randomize")]
        [HarmonyPrefix]
        private static void RandomSpawn_Randomize_Prefix(RandomSpawn __instance, Vector3 pos)
        {
            if (__instance == null)
            {
                return;
            }
            bool hasDvergr = __instance.gameObject.name.IndexOf("Dverg", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!hasDvergr)
            {
                CreatureSpawner cs = __instance.GetComponentInChildren<CreatureSpawner>(includeInactive: true);
                if (cs != null && IsDvergrPrefab(cs.m_creaturePrefab))
                {
                    hasDvergr = true;
                }
            }
            if (hasDvergr && IsInAshlands(pos))
            {
                __instance.m_chanceToSpawn = Mathf.Min(100f, __instance.m_chanceToSpawn * Mathf.Max(1f, SpawnRateMultiplier.Value));
            }
        }

        [HarmonyPatch(typeof(SpawnArea), "Awake")]
        [HarmonyPostfix]
        private static void SpawnArea_Awake_Postfix(SpawnArea __instance)
        {
            if (__instance == null || __instance.m_prefabs == null)
            {
                return;
            }
            int id = __instance.GetInstanceID();
            if (PatchedSpawnAreas.Contains(id))
            {
                return;
            }
            bool hasAshlandsDvergr = false;
            float baseWeight = 1f;
            foreach (SpawnArea.SpawnData sd in __instance.m_prefabs)
            {
                if (sd != null && IsDvergrPrefab(sd.m_prefab) &&
                    (sd.m_prefab.name.IndexOf("Ashlands", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     IsInAshlands(__instance.transform.position)))
                {
                    hasAshlandsDvergr = true;
                    sd.m_minLevel = Mathf.Max(sd.m_minLevel, MinLevel.Value);
                    sd.m_maxLevel = Mathf.Max(sd.m_maxLevel, MaxLevel.Value);
                    sd.m_weight *= Mathf.Max(1f, SpawnRateMultiplier.Value);
                    baseWeight = Mathf.Max(baseWeight, sd.m_weight);
                }
            }
            if (hasAshlandsDvergr)
            {
                PatchedSpawnAreas.Add(id);
                float mult = Mathf.Max(0.1f, SpawnRateMultiplier.Value);
                __instance.m_levelupChance = Mathf.Max(__instance.m_levelupChance, LevelUpChance.Value);
                __instance.m_spawnIntervalSec = Mathf.Max(1f, __instance.m_spawnIntervalSec / mult);
                __instance.m_maxNear = Mathf.CeilToInt(__instance.m_maxNear * mult);
                __instance.m_maxTotal = Mathf.CeilToInt(__instance.m_maxTotal * mult);

                float varietyChance = Mathf.Clamp(VarietyDvergrSpawnChance.Value, 0f, 100f);
                if (varietyChance > 0f && ZNetScene.instance != null)
                {
                    float perVariantWeight = Mathf.Max(0.05f, (baseWeight * (varietyChance / 100f)) / VarietyDvergrPrefabNames.Length);
                    foreach (string varName in VarietyDvergrPrefabNames)
                    {
                        GameObject varPrefab = ZNetScene.instance.GetPrefab(varName);
                        if (varPrefab != null)
                        {
                            __instance.m_prefabs.Add(new SpawnArea.SpawnData
                            {
                                m_prefab = varPrefab,
                                m_weight = perVariantWeight,
                                m_minLevel = Mathf.Max(1, MinLevel.Value),
                                m_maxLevel = Mathf.Max(MinLevel.Value, MaxLevel.Value)
                            });
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(SpawnSystem), "Awake")]
        [HarmonyPostfix]
        private static void SpawnSystem_Awake_Postfix(SpawnSystem __instance)
        {
            if (__instance == null || __instance.m_spawnLists == null || ZNetScene.instance == null)
            {
                return;
            }
            float mult = Mathf.Max(0.1f, SpawnRateMultiplier.Value);
            GameObject dvergrAshlandsPrefab = ZNetScene.instance.GetPrefab("DvergerAshlands");

            foreach (SpawnSystemList list in __instance.m_spawnLists)
            {
                if (list == null || list.m_spawners == null)
                {
                    continue;
                }
                int listId = list.GetInstanceID();
                if (PatchedSpawnLists.Contains(listId))
                {
                    continue;
                }
                PatchedSpawnLists.Add(listId);

                bool foundExistingAshlandsDvergr = false;
                foreach (SpawnSystem.SpawnData sd in list.m_spawners)
                {
                    if (sd == null || sd.m_prefab == null)
                    {
                        continue;
                    }
                    if (IsDvergrPrefab(sd.m_prefab) && (sd.m_biome & Heightmap.Biome.AshLands) != 0)
                    {
                        foundExistingAshlandsDvergr = true;
                        sd.m_enabled = true;
                        sd.m_maxSpawned = Mathf.Max(1, Mathf.CeilToInt(sd.m_maxSpawned * mult));
                        sd.m_spawnInterval = Mathf.Max(5f, sd.m_spawnInterval / mult);
                        sd.m_spawnChance = Mathf.Min(100f, sd.m_spawnChance * mult);
                        sd.m_groupSizeMin = Mathf.Max(1, Mathf.CeilToInt(sd.m_groupSizeMin * mult));
                        sd.m_groupSizeMax = Mathf.Max(sd.m_groupSizeMin, Mathf.CeilToInt(sd.m_groupSizeMax * mult));
                        sd.m_minLevel = Mathf.Max(sd.m_minLevel, MinLevel.Value);
                        sd.m_maxLevel = Mathf.Max(sd.m_maxLevel, MaxLevel.Value);
                        sd.m_overrideLevelupChance = Mathf.Max(sd.m_overrideLevelupChance, LevelUpChance.Value);
                    }
                }

                if (!foundExistingAshlandsDvergr && EnableAmbientAshlandsSpawns.Value && dvergrAshlandsPrefab != null && list.m_spawners.Count > 0)
                {
                    float minRad = Mathf.Max(10f, AmbientSpawnRadiusMin.Value);
                    float maxRad = Mathf.Max(minRad + 5f, AmbientSpawnRadiusMax.Value);

                    SpawnSystem.SpawnData ambientDvergr = new SpawnSystem.SpawnData
                    {
                        m_name = "Ashlands_Dvergr_Ambient",
                        m_enabled = true,
                        m_prefab = dvergrAshlandsPrefab,
                        m_biome = Heightmap.Biome.AshLands,
                        m_biomeArea = Heightmap.BiomeArea.Everything,
                        m_maxSpawned = Mathf.Max(1, AmbientMaxSpawned.Value),
                        m_spawnInterval = Mathf.Max(5f, AmbientSpawnInterval.Value / mult),
                        m_spawnChance = Mathf.Clamp(AmbientSpawnChance.Value, 0f, 100f),
                        m_spawnDistance = 15f,
                        m_spawnRadiusMin = minRad,
                        m_spawnRadiusMax = maxRad,
                        m_groupSizeMin = Mathf.Max(1, AmbientGroupMin.Value),
                        m_groupSizeMax = Mathf.Max(AmbientGroupMin.Value, AmbientGroupMax.Value),
                        m_groupRadius = 6f,
                        m_spawnAtDay = true,
                        m_spawnAtNight = true,
                        m_minAltitude = 1f,
                        m_maxAltitude = 1000f,
                        m_minTilt = 0f,
                        m_maxTilt = 35f,
                        m_inForest = true,
                        m_outsideForest = true,
                        m_inLava = false,
                        m_outsideLava = true,
                        m_groundOffset = 0.5f,
                        m_minLevel = Mathf.Max(1, MinLevel.Value),
                        m_maxLevel = Mathf.Max(MinLevel.Value, MaxLevel.Value),
                        m_overrideLevelupChance = Mathf.Clamp(LevelUpChance.Value, 0f, 100f)
                    };
                    list.m_spawners.Add(ambientDvergr);
                    Log.LogInfo($"Added ambient DvergerAshlands SpawnData (max={ambientDvergr.m_maxSpawned}, interval={ambientDvergr.m_spawnInterval}s, chance={ambientDvergr.m_spawnChance}%, levels={ambientDvergr.m_minLevel}-{ambientDvergr.m_maxLevel}).");

                    float varietyChance = Mathf.Clamp(VarietyDvergrSpawnChance.Value, 0f, 100f);
                    if (varietyChance > 0f)
                    {
                        float varChance = Mathf.Clamp(AmbientSpawnChance.Value * (varietyChance / 100f), 1f, 100f);
                        int varMaxSpawned = Mathf.Max(1, AmbientMaxSpawned.Value / 2);
                        foreach (string varName in VarietyDvergrPrefabNames)
                        {
                            GameObject varPrefab = ZNetScene.instance.GetPrefab(varName);
                            if (varPrefab == null)
                            {
                                continue;
                            }
                            SpawnSystem.SpawnData varSpawn = new SpawnSystem.SpawnData
                            {
                                m_name = "Ashlands_Dvergr_Variety_" + varName,
                                m_enabled = true,
                                m_prefab = varPrefab,
                                m_biome = Heightmap.Biome.AshLands,
                                m_biomeArea = Heightmap.BiomeArea.Everything,
                                m_maxSpawned = varMaxSpawned,
                                m_spawnInterval = Mathf.Max(10f, (AmbientSpawnInterval.Value * 1.5f) / mult),
                                m_spawnChance = varChance,
                                m_spawnDistance = 18f,
                                m_spawnRadiusMin = minRad,
                                m_spawnRadiusMax = maxRad,
                                m_groupSizeMin = 1,
                                m_groupSizeMax = Mathf.Max(1, AmbientGroupMax.Value / 2),
                                m_groupRadius = 6f,
                                m_spawnAtDay = true,
                                m_spawnAtNight = true,
                                m_minAltitude = 1f,
                                m_maxAltitude = 1000f,
                                m_minTilt = 0f,
                                m_maxTilt = 35f,
                                m_inForest = true,
                                m_outsideForest = true,
                                m_inLava = false,
                                m_outsideLava = true,
                                m_groundOffset = 0.5f,
                                m_minLevel = Mathf.Max(1, MinLevel.Value),
                                m_maxLevel = Mathf.Max(MinLevel.Value, MaxLevel.Value),
                                m_overrideLevelupChance = Mathf.Clamp(LevelUpChance.Value, 0f, 100f)
                            };
                            list.m_spawners.Add(varSpawn);
                        }
                        Log.LogInfo($"Added Ashlands variety Dvergr ambient spawns ({string.Join(", ", VarietyDvergrPrefabNames)}) with {varietyChance}% variety ratio.");
                    }
                }
            }
        }

        private static float CalculateBloodWeaponDamageMultiplier(Character ch)
        {
            // Equivalent Ashlands Blood Weapon base ratio:
            // Crossbowmen ~1.35x, Mages ~1.20x * MageDamageMultiplier adjustment
            // plus Bloodstone effect: +0.5% damage per 1% missing health (up to +50% at low HP)
            bool isMage = IsDvergrMage(ch);
            float baseRatio = isMage ? 1.20f * (MageDamageMultiplier.Value / 1.10f) : 1.35f * (DamageMultiplier.Value / 1.25f);
            float missingHpRatio = ch != null ? Mathf.Clamp01(1f - ch.GetHealthPercentage()) : 0f;
            float bloodstoneBonus = 1f + missingHpRatio * 0.50f;
            return baseRatio * bloodstoneBonus;
        }

        [HarmonyPatch(typeof(SEMan), "ModifyAttack")]
        [HarmonyPostfix]
        private static void SEMan_ModifyAttack_Postfix(Character ___m_character, ref HitData hitData)
        {
            if (hitData != null && IsAshlandsDvergr(___m_character))
            {
                int stars = Mathf.Max(0, ___m_character.GetLevel() - 1);
                // Vanilla Attack already multiplied damage by (1 + stars * 0.50).
                // Rescale: +25% at 1-star (1.25x), but remove extra star damage scale from 1 to 2 star (caps at 1-star 1.25x)
                // because 2-star already receives the advanced blood weapon / staff buff.
                int effectiveDamageStars = Mathf.Min(stars, 1);
                float vanillaStarMult = 1f + stars * 0.50f;
                float gentleStarMult = 1f + effectiveDamageStars * 0.25f;
                float baseMult = (UseAshlandsBloodWeapons.Value && stars >= 2)
                    ? CalculateBloodWeaponDamageMultiplier(___m_character)
                    : GetDamageMultiplierFor(___m_character);
                float mult = baseMult * (gentleStarMult / vanillaStarMult);
                hitData.m_damage.Modify(mult);
            }
        }

        [HarmonyPatch(typeof(Aoe), "Setup")]
        [HarmonyPostfix]
        private static void Aoe_Setup_Postfix(Aoe __instance, Character owner, HitData hitData)
        {
            if (__instance == null || !IsAshlandsDvergr(owner))
            {
                return;
            }
            if (hitData == null || !__instance.m_useAttackSettings)
            {
                int stars = Mathf.Max(0, owner.GetLevel() - 1);
                int effectiveDamageStars = Mathf.Min(stars, 1);
                float baseMult = (UseAshlandsBloodWeapons.Value && stars >= 2)
                    ? CalculateBloodWeaponDamageMultiplier(owner)
                    : GetDamageMultiplierFor(owner);
                float mult = baseMult * (1f + effectiveDamageStars * 0.25f);
                __instance.m_damage.Modify(mult);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "UpdateEquipmentVisuals")]
        [HarmonyPostfix]
        private static void VisEquipment_UpdateEquipmentVisuals_Postfix(VisEquipment __instance)
        {
            if (__instance == null)
            {
                return;
            }
            AshlandsDvergrEnhancer enhancer = __instance.GetComponent<AshlandsDvergrEnhancer>();
            if (enhancer != null)
            {
                enhancer.CheckEquipmentVisualsChanged();
            }
        }

        private static IEnumerator RenderDvergrValidationCoroutine()
        {
            yield return new WaitForSeconds(1.0f);
            Log.LogInfo("=== Starting DvergerAshlands Visual Validation Render ===");
            try
            {
                if (!AssetID.TryParse("2ece1cf15f2885a4da6da84af9d5c542", out AssetID dvergrAssetId))
                {
                    Log.LogError("Failed to parse DvergerAshlands AssetID");
                    yield break;
                }
                SoftReference<GameObject> dvergrRef = new SoftReference<GameObject>(dvergrAssetId);
                LoadResult res = dvergrRef.Load();
                Log.LogInfo($"Loaded DvergerAshlands SoftReference: {res}");
                GameObject prefab = dvergrRef.Asset;
                if (prefab == null)
                {
                    Log.LogError("DvergerAshlands prefab is null");
                    yield break;
                }

                // Log all renderers, materials, shaders, and properties on the prefab
                foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(includeInactive: true))
                {
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        int propCount = m.shader != null ? m.shader.GetPropertyCount() : 0;
                        List<string> props = new List<string>();
                        for (int p = 0; p < propCount; p++)
                        {
                            props.Add($"{m.shader.GetPropertyName(p)}({m.shader.GetPropertyType(p)})");
                        }
                        Log.LogInfo($"[Prefab Renderer] {r.name} (active={r.gameObject.activeInHierarchy}, enabled={r.enabled}, {r.GetType().Name}) | Mat={m.name} | Shader={m.shader?.name} | Props=[{string.Join(", ", props)}]");
                    }
                }

                List<GameObject> equipItems = new List<GameObject>();
                Humanoid hum = prefab.GetComponent<Humanoid>();
                if (hum != null)
                {
                    if (hum.m_defaultItems != null)
                    {
                        foreach (var go in hum.m_defaultItems)
                        {
                            if (go != null)
                            {
                                Log.LogInfo($"[Humanoid defaultItem] {go.name}");
                                equipItems.Add(go);
                            }
                        }
                    }
                    if (hum.m_randomArmor != null && hum.m_randomArmor.Length > 0)
                    {
                        foreach (var go in hum.m_randomArmor)
                            if (go != null) Log.LogInfo($"[Humanoid randomArmor] {go.name}");
                        if (hum.m_randomArmor[0] != null) equipItems.Add(hum.m_randomArmor[0]);
                    }
                    if (hum.m_randomSets != null && hum.m_randomSets.Length > 0)
                    {
                        foreach (var set in hum.m_randomSets)
                        {
                            if (set?.m_items == null) continue;
                            foreach (var go in set.m_items)
                                if (go != null) Log.LogInfo($"[Humanoid randomSet {set.m_name}] {go.name}");
                        }
                        if (hum.m_randomSets[0]?.m_items != null)
                        {
                            foreach (var go in hum.m_randomSets[0].m_items)
                                if (go != null) equipItems.Add(go);
                        }
                    }
                }

                LevelEffects lePrefab = prefab.GetComponentInChildren<LevelEffects>(includeInactive: true);
                if (lePrefab != null && lePrefab.m_levelSetups != null)
                {
                    for (int s = 0; s < lePrefab.m_levelSetups.Count; s++)
                    {
                        var ls = lePrefab.m_levelSetups[s];
                        Log.LogInfo($"[Prefab LevelSetup {s}] scale={ls.m_scale}, hue={ls.m_hue}, sat={ls.m_saturation}, val={ls.m_value}, setEmissive={ls.m_setEmissiveColor}, emissive={ls.m_emissiveColor}, enableObj={ls.m_enableObject?.name}");
                    }
                }

                VisEquipment prefabVisEq = prefab.GetComponent<VisEquipment>();
                if (prefabVisEq != null)
                {
                    Log.LogInfo($"[Prefab VisEquipment] rightHand={prefabVisEq.m_rightHand?.name}, leftHand={prefabVisEq.m_leftHand?.name}, helmet={prefabVisEq.m_helmet?.name}, backBow={prefabVisEq.m_backBow?.name}");
                }
                foreach (GameObject itemPrefab in equipItems)
                {
                    List<string> childNames = new List<string>();
                    foreach (Transform t in itemPrefab.GetComponentsInChildren<Transform>(includeInactive: true))
                        childNames.Add(t.name);
                    Log.LogInfo($"[EquipItem {itemPrefab.name}] children=[{string.Join(", ", childNames)}]");
                }

                Transform visualTemplate = prefab.transform.Find("Visual");
                if (visualTemplate == null)
                {
                    visualTemplate = prefab.transform;
                }

                Vector3 stageOrigin = new Vector3(0f, 500f, 0f);
                GameObject stageRoot = new GameObject("DvergrValidationStage");
                stageRoot.transform.position = stageOrigin;

                float[] xOffsets = { -2.7f, -0.9f, 0.9f, 2.7f };
                int[] levels = { 3, 3, 3, 3 };
                string[] prefabNames = { "DvergerAshlands", "DvergerMageFire", "DvergerMageIce", "DvergerMageSupport" };

                for (int i = 0; i < 4; i++)
                {
                    int level = levels[i];
                    string pName = prefabNames[i];
                    GameObject curPrefab = ResolvePrefabByNameOrAssetId(pName) ?? prefab;
                    Transform curVisualTemplate = curPrefab.transform.Find("Visual") ?? curPrefab.transform;

                    GameObject model = UnityEngine.Object.Instantiate(curVisualTemplate.gameObject, stageRoot.transform);
                    model.name = pName;
                    model.transform.localPosition = new Vector3(xOffsets[i], 0f, 0f);
                    model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    float scale = level == 1 ? 1.0f : (level == 2 ? VisualScale1Star.Value : VisualScale2Star.Value);
                    model.transform.localScale = Vector3.one * scale;

                    VisEquipment curVisEq = curPrefab.GetComponent<VisEquipment>();
                    string rightHandBoneName = curVisEq?.m_rightHand != null ? curVisEq.m_rightHand.name : "RightHand_Attach";

                    List<GameObject> curEquipItems = new List<GameObject>();
                    Humanoid curHum = curPrefab.GetComponent<Humanoid>();
                    if (curHum != null)
                    {
                        if (curHum.m_defaultItems != null)
                        {
                            foreach (var go in curHum.m_defaultItems)
                                if (go != null) curEquipItems.Add(go);
                        }
                        if (curHum.m_randomArmor != null && curHum.m_randomArmor.Length > 0 && curHum.m_randomArmor[0] != null)
                        {
                            curEquipItems.Add(curHum.m_randomArmor[0]);
                        }
                        if (curHum.m_randomSets != null && curHum.m_randomSets.Length > 0 && curHum.m_randomSets[0]?.m_items != null)
                        {
                            foreach (var go in curHum.m_randomSets[0].m_items)
                                if (go != null) curEquipItems.Add(go);
                        }
                    }

                    SkinnedMeshRenderer bodySmr = model.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true);
                    if (bodySmr != null)
                    {
                        Transform rightHand = null;
                        foreach (Transform t in model.GetComponentsInChildren<Transform>(includeInactive: true))
                        {
                            if (string.Equals(t.name, rightHandBoneName, StringComparison.OrdinalIgnoreCase) ||
                                (rightHand == null && t.name.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                rightHand = t;
                                if (string.Equals(t.name, rightHandBoneName, StringComparison.OrdinalIgnoreCase))
                                    break;
                            }
                        }

                        foreach (GameObject itemPrefab in curEquipItems)
                        {
                            Transform attachSkin = itemPrefab.transform.Find("attach_skin");
                            if (attachSkin != null)
                            {
                                GameObject skinInst = UnityEngine.Object.Instantiate(attachSkin.gameObject, bodySmr.transform.parent);
                                skinInst.SetActive(true);
                                foreach (SkinnedMeshRenderer smr in skinInst.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
                                {
                                    smr.rootBone = bodySmr.rootBone;
                                    smr.bones = bodySmr.bones;
                                }
                            }

                            Transform attachWeapon = itemPrefab.transform.Find("attach") ?? itemPrefab.transform.Find("attach_r.hand");
                            if (attachWeapon != null && rightHand != null)
                            {
                                GameObject weaponInst = UnityEngine.Object.Instantiate(attachWeapon.gameObject);
                                weaponInst.name = "attach_" + itemPrefab.name;
                                weaponInst.transform.SetParent(rightHand, worldPositionStays: true);
                                weaponInst.transform.localPosition = Vector3.zero;
                                weaponInst.transform.localRotation = Quaternion.identity;
                                weaponInst.SetActive(true);
                                Transform debugCube = weaponInst.transform.Find("Cube");
                                if (debugCube != null)
                                {
                                    debugCube.gameObject.SetActive(false);
                                }
                            }
                        }
                    }

                    Light aura = null;
                    AshlandsDvergrEnhancer.ApplyStyleToHierarchy(model.transform, level, ref aura, out _);
                }

                // Step root ParticleSystems forward so 2-star boss particle effects are visible in the still capture
                foreach (ParticleSystem ps in stageRoot.GetComponentsInChildren<ParticleSystem>(includeInactive: false))
                {
                    if (ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>() == null)
                    {
                        ps.Simulate(0.85f, withChildren: true, restart: true);
                    }
                }

                // Studio lighting for validation render
                GameObject keyLightObj = new GameObject("KeyLight");
                keyLightObj.transform.SetParent(stageRoot.transform, false);
                keyLightObj.transform.localPosition = new Vector3(1.5f, 3.2f, -3.5f);
                keyLightObj.transform.LookAt(stageOrigin + new Vector3(0f, 1.1f, 0f));
                Light keyLight = keyLightObj.AddComponent<Light>();
                keyLight.type = LightType.Directional;
                keyLight.color = new Color(1.0f, 0.96f, 0.90f);
                keyLight.intensity = 1.1f;

                GameObject camObj = new GameObject("ValidationCamera");
                camObj.transform.SetParent(stageRoot.transform, false);
                camObj.transform.localPosition = new Vector3(0f, 1.15f, -4.8f);
                camObj.transform.LookAt(stageOrigin + new Vector3(0f, 1.05f, 0f));

                Camera cam = camObj.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.11f, 0.10f, 0.12f, 1f);
                cam.fieldOfView = 44f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 50f;

                int width = 1920;
                int height = 800;
                RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;

                byte[] png = ImageConversion.EncodeToPNG(tex);
                string outPath = "/tmp/dvergr_render_validation.png";
                File.WriteAllBytes(outPath, png);
                Log.LogInfo($"=== Saved validation render ({png.Length} bytes) to {outPath} ===");
            }
            catch (Exception ex)
            {
                Log.LogError($"Validation render failed: {ex}");
            }
            System.Diagnostics.Process.GetCurrentProcess().Kill();
            yield break;
        }
    }

    /// <summary>
    /// Component attached to Ashlands Dvergr instances to drive slow passive regeneration
    /// and star-tiered visual enhancements that preserve original texture details.
    /// </summary>
    internal class AshlandsDvergrEnhancer : MonoBehaviour
    {
        private static readonly Dictionary<int, Color> OriginalMaterialColors = new Dictionary<int, Color>();
        private static readonly Dictionary<int, Color> OriginalMaterialEmissions = new Dictionary<int, Color>();

        private Character _character;
        private ZNetView _nview;
        private Light _auraLight;
        private float _healTimer;
        private float _noiseSeed;
        private float _baseLightIntensity = 1.0f;
        private int _lastRendererCount;
        private int _lastAppliedLevel;

        private void Awake()
        {
            _character = GetComponent<Character>();
            _nview = GetComponent<ZNetView>();
            _noiseSeed = UnityEngine.Random.Range(0f, 100f);
        }

        private void Start()
        {
            ApplyCoolVisuals();
        }

        internal void CheckEquipmentVisualsChanged()
        {
            if (_character == null)
            {
                return;
            }
            Renderer[] renderers = _character.GetComponentsInChildren<Renderer>(includeInactive: true);
            int level = Mathf.Max(1, _character.GetLevel());
            if (renderers.Length != _lastRendererCount || level != _lastAppliedLevel)
            {
                ApplyCoolVisuals();
            }
        }

        private void Update()
        {
            if (_character == null || _character.IsDead())
            {
                if (_auraLight != null)
                {
                    _auraLight.enabled = false;
                }
                return;
            }

            // Flicker molten aura light like a living forge fire
            if (_auraLight != null && _auraLight.enabled)
            {
                float flicker = 0.85f + 0.30f * Mathf.PerlinNoise(Time.time * 3.5f, _noiseSeed);
                _auraLight.intensity = _baseLightIntensity * flicker;
            }

            // Slow passive regeneration (owner authoritative)
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }

            float healPct = AshlandsDvergrPlugin.PassiveHealPercentPerTick.Value;
            if (healPct <= 0f)
            {
                return;
            }

            float interval = Mathf.Max(1f, AshlandsDvergrPlugin.PassiveHealIntervalSeconds.Value);
            _healTimer += Time.deltaTime;
            if (_healTimer < interval)
            {
                return;
            }
            _healTimer = 0f;

            float currentHp = _character.GetHealth();
            float maxHp = _character.GetMaxHealth();
            if (currentHp > 0f && currentHp < maxHp)
            {
                float healAmount = maxHp * (healPct / 100f);
                _character.Heal(healAmount, showText: false);
            }
        }

        internal void ApplyCoolVisuals()
        {
            if (_character == null)
            {
                return;
            }

            AshlandsDvergrPlugin.EnsureVisualStarSetups(_character);
            int level = Mathf.Max(1, _character.GetLevel());
            _lastAppliedLevel = level;

            LevelEffects le = _character.GetComponentInChildren<LevelEffects>();
            if (le != null)
            {
                le.SetupLevelVisualization(level);
            }

            _lastRendererCount = ApplyStyleToHierarchy(_character.transform, level, ref _auraLight, out _baseLightIntensity);
        }

        private static bool IsWeaponRenderer(Renderer r, Material m, VisEquipment visEq)
        {
            if (r == null)
            {
                return false;
            }
            if (visEq != null)
            {
                Transform rt = r.transform;
                if ((visEq.m_rightHand != null && rt.IsChildOf(visEq.m_rightHand)) ||
                    (visEq.m_leftHand != null && rt.IsChildOf(visEq.m_leftHand)) ||
                    (visEq.m_backMelee != null && rt.IsChildOf(visEq.m_backMelee)) ||
                    (visEq.m_backBow != null && rt.IsChildOf(visEq.m_backBow)) ||
                    (visEq.m_backTool != null && rt.IsChildOf(visEq.m_backTool)) ||
                    (visEq.m_backAtgeir != null && rt.IsChildOf(visEq.m_backAtgeir)))
                {
                    return true;
                }
            }
            string rName = r.name ?? string.Empty;
            string mName = m != null ? (m.name ?? string.Empty) : string.Empty;
            if (rName.IndexOf("Arbalest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rName.IndexOf("Staff", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rName.IndexOf("Crossbow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mName.IndexOf("Arbalest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mName.IndexOf("Staff", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mName.IndexOf("Crossbow", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            for (Transform t = r.transform; t != null; t = t.parent)
            {
                string tName = t.name ?? string.Empty;
                if (tName.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tName.IndexOf("BackBow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tName.IndexOf("BackMelee", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tName.IndexOf("BackTool", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tName.IndexOf("attach_Dverger", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tName.IndexOf("AshlandsDvergr_BloodWeapon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static Transform FindBone(Transform root, params string[] names)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                foreach (string n in names)
                {
                    if (string.Equals(t.name, n, StringComparison.OrdinalIgnoreCase))
                    {
                        return t;
                    }
                }
            }
            return null;
        }

        private static void EnsureBloodWeaponVisual(Transform root, VisEquipment visEq, int level)
        {
            bool useBloodWep = AshlandsDvergrPlugin.UseAshlandsBloodWeapons.Value && level >= 3;
            Transform rightHand = visEq != null && visEq.m_rightHand != null
                ? visEq.m_rightHand
                : FindBone(root, "r.hand", "RightHand_Attach", "RightHand");
            if (rightHand == null)
            {
                return;
            }

            Transform existingBloodWep = rightHand.Find("AshlandsDvergr_BloodWeapon");
            if (!useBloodWep)
            {
                if (existingBloodWep != null)
                {
                    existingBloodWep.gameObject.SetActive(false);
                }
                for (int i = 0; i < rightHand.childCount; i++)
                {
                    Transform child = rightHand.GetChild(i);
                    if (child != existingBloodWep && child.name.IndexOf("AshlandsDvergr_", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        foreach (Renderer r in child.GetComponentsInChildren<Renderer>(includeInactive: true))
                        {
                            if (r.name != "Cube")
                            {
                                r.enabled = true;
                            }
                        }
                    }
                }
                return;
            }

            // Hide vanilla held weapon meshes (e.g., DvergerArbalest) while keeping any other attachments intact
            for (int i = 0; i < rightHand.childCount; i++)
            {
                Transform child = rightHand.GetChild(i);
                if (child == existingBloodWep || child.name.IndexOf("AshlandsDvergr_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                foreach (Renderer r in child.GetComponentsInChildren<Renderer>(includeInactive: true))
                {
                    r.enabled = false;
                }
            }

            if (existingBloodWep != null)
            {
                existingBloodWep.gameObject.SetActive(true);
                return;
            }

            Character ch = root.GetComponentInParent<Character>() ?? root.GetComponentInChildren<Character>();
            string charName = ch != null ? ch.gameObject.name : root.name;
            string wepPrefabName = AshlandsDvergrPlugin.GetEquivalentBloodWeaponPrefabName(charName);
            GameObject wepPrefab = AshlandsDvergrPlugin.ResolvePrefabByNameOrAssetId(wepPrefabName);
            if (wepPrefab == null)
            {
                AshlandsDvergrPlugin.Log.LogError($"[EnsureBloodWeaponVisual] Failed to resolve weaponPrefab '{wepPrefabName}' for character '{charName}'");
                return;
            }

            Transform attach = wepPrefab.transform.Find("attach") ?? wepPrefab.transform.Find("attach_r.hand");
            if (attach == null)
            {
                foreach (Transform t in wepPrefab.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (t.name.IndexOf("attach", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        attach = t;
                        break;
                    }
                }
            }
            if (attach == null)
            {
                List<string> childNames = new List<string>();
                foreach (Transform t in wepPrefab.GetComponentsInChildren<Transform>(includeInactive: true))
                    childNames.Add(t.name);
                AshlandsDvergrPlugin.Log.LogError($"[EnsureBloodWeaponVisual] No attach transform on '{wepPrefab.name}', children=[{string.Join(", ", childNames)}]");
                return;
            }

            AshlandsDvergrPlugin.Log.LogInfo($"[EnsureBloodWeaponVisual] Equipping '{wepPrefab.name}' via attach '{attach.name}' to '{rightHand.name}' on '{charName}'");

            GameObject wepInst = UnityEngine.Object.Instantiate(attach.gameObject);
            wepInst.name = "AshlandsDvergr_BloodWeapon";
            wepInst.transform.SetParent(rightHand, worldPositionStays: true);
            wepInst.transform.localPosition = Vector3.zero;
            wepInst.transform.localRotation = Quaternion.identity;
            wepInst.SetActive(true);
            Transform debugCube = wepInst.transform.Find("Cube");
            if (debugCube != null)
            {
                debugCube.gameObject.SetActive(false);
            }
        }

        private static void Ensure2StarBossEffects(Transform root, int level, bool coolEnabled, float lightMult)
        {
            bool shouldShowBossFx = coolEnabled && level >= 3 && AshlandsDvergrPlugin.Enable2StarBossEffects.Value;

            Transform existingBossRoot = root.Find("AshlandsDvergr_BossFX");
            Transform leftHand = FindBone(root, "l.hand", "LeftHand_Attach", "LeftHand");
            Transform rightHand = FindBone(root, "r.hand", "RightHand_Attach", "RightHand");
            Transform chestBone = FindBone(root, "spine2", "spine1", "chest", "Spine");

            Transform existingLeftHandFx = leftHand != null ? leftHand.Find("AshlandsDvergr_BossHandFX") : null;
            Transform existingRightHandFx = rightHand != null ? rightHand.Find("AshlandsDvergr_BossHandFX") : null;
            Transform existingChestFx = chestBone != null ? chestBone.Find("AshlandsDvergr_BossChestFX") : null;

            if (!shouldShowBossFx)
            {
                if (existingBossRoot != null) existingBossRoot.gameObject.SetActive(false);
                if (existingLeftHandFx != null) existingLeftHandFx.gameObject.SetActive(false);
                if (existingRightHandFx != null) existingRightHandFx.gameObject.SetActive(false);
                if (existingChestFx != null) existingChestFx.gameObject.SetActive(false);
                return;
            }

            if (existingBossRoot != null)
            {
                existingBossRoot.gameObject.SetActive(true);
                if (existingLeftHandFx != null) existingLeftHandFx.gameObject.SetActive(true);
                if (existingRightHandFx != null) existingRightHandFx.gameObject.SetActive(true);
                if (existingChestFx != null) existingChestFx.gameObject.SetActive(true);
                return;
            }

            GameObject bossRoot = new GameObject("AshlandsDvergr_BossFX");
            bossRoot.transform.SetParent(root, worldPositionStays: false);
            bossRoot.transform.localPosition = Vector3.zero;
            bossRoot.transform.localRotation = Quaternion.identity;

            // 1. Dvergr Arcane Boss Ring & Sparks (fx_DvergerMage_Nova_ring)
            GameObject novaRingPrefab = AshlandsDvergrPlugin.ResolvePrefabByNameOrAssetId("fx_DvergerMage_Nova_ring");
            if (novaRingPrefab != null)
            {
                GameObject ringInst = UnityEngine.Object.Instantiate(novaRingPrefab, bossRoot.transform);
                ringInst.name = "BossArcaneRing";
                ringInst.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                ringInst.transform.localRotation = Quaternion.identity;
                ringInst.transform.localScale = Vector3.one * 0.25f;
                ConfigureBossParticlePrefab(ringInst, new Color(0.85f, 0.22f, 1.0f, 0.55f), sizeMult: 0.35f, disableBrightFlareChildren: true);
            }

            // 2. Boss Chest Blood-Arcane Fire (vfx_crossbow_blood_fire)
            GameObject bloodFirePrefab = AshlandsDvergrPlugin.ResolvePrefabByNameOrAssetId("vfx_crossbow_blood_fire");
            if (bloodFirePrefab != null && chestBone != null)
            {
                GameObject chestInst = UnityEngine.Object.Instantiate(bloodFirePrefab);
                chestInst.name = "AshlandsDvergr_BossChestFX";
                chestInst.transform.SetParent(chestBone, worldPositionStays: true);
                chestInst.transform.localPosition = Vector3.zero;
                chestInst.transform.localRotation = Quaternion.identity;
                chestInst.transform.localScale = Vector3.one * 0.0052f;
                ConfigureBossParticlePrefab(chestInst, new Color(0.95f, 0.18f, 0.72f, 0.85f), sizeMult: 0.55f, disableBrightFlareChildren: true);
            }

            // 3. Boss Hand Blood-Arcane Fire on Left & Right Hands (vfx_crossbow_blood_fire)
            if (bloodFirePrefab != null)
            {
                if (leftHand != null)
                {
                    GameObject lhInst = UnityEngine.Object.Instantiate(bloodFirePrefab);
                    lhInst.name = "AshlandsDvergr_BossHandFX";
                    lhInst.transform.SetParent(leftHand, worldPositionStays: true);
                    lhInst.transform.localPosition = Vector3.zero;
                    lhInst.transform.localRotation = Quaternion.identity;
                    lhInst.transform.localScale = Vector3.one * 0.0042f;
                    ConfigureBossParticlePrefab(lhInst, new Color(0.85f, 0.22f, 1.0f, 0.88f), sizeMult: 0.45f, disableBrightFlareChildren: true);
                }
                if (rightHand != null)
                {
                    GameObject rhInst = UnityEngine.Object.Instantiate(bloodFirePrefab);
                    rhInst.name = "AshlandsDvergr_BossHandFX";
                    rhInst.transform.SetParent(rightHand, worldPositionStays: true);
                    rhInst.transform.localPosition = Vector3.zero;
                    rhInst.transform.localRotation = Quaternion.identity;
                    rhInst.transform.localScale = Vector3.one * 0.0042f;
                    ConfigureBossParticlePrefab(rhInst, new Color(1.0f, 0.18f, 0.58f, 0.88f), sizeMult: 0.45f, disableBrightFlareChildren: true);
                }
            }
        }

        private static void ConfigureBossParticlePrefab(GameObject fxInstance, Color tintColor, float sizeMult = 0.25f, bool disableBrightFlareChildren = true)
        {
            if (fxInstance == null)
            {
                return;
            }
            fxInstance.SetActive(true);
            foreach (TimedDestruction td in fxInstance.GetComponentsInChildren<TimedDestruction>(includeInactive: true))
            {
                td.enabled = false;
            }
            foreach (ZNetView znv in fxInstance.GetComponentsInChildren<ZNetView>(includeInactive: true))
            {
                znv.enabled = false;
            }
            foreach (AudioSource audio in fxInstance.GetComponentsInChildren<AudioSource>(includeInactive: true))
            {
                audio.enabled = false;
            }
            foreach (ZSFX zsfx in fxInstance.GetComponentsInChildren<ZSFX>(includeInactive: true))
            {
                zsfx.enabled = false;
            }
            foreach (Light l in fxInstance.GetComponentsInChildren<Light>(includeInactive: true))
            {
                l.enabled = false;
            }
            foreach (Renderer r in fxInstance.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (!(r is ParticleSystemRenderer))
                {
                    r.enabled = false;
                }
            }
            foreach (ParticleSystem ps in fxInstance.GetComponentsInChildren<ParticleSystem>(includeInactive: true))
            {
                string psName = ps.gameObject.name ?? string.Empty;
                ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
                string matName = psr != null && psr.sharedMaterial != null ? psr.sharedMaterial.name : string.Empty;
                var main = ps.main;
                var shape = ps.shape;
                // Disable only the stationary point/flare center orb while keeping moving flames, sparks, and wisps
                bool isStationaryCenterOrb = !shape.enabled && main.startSpeed.constantMax <= 0.05f && main.startSize.constantMax >= 0.4f;
                if (disableBrightFlareChildren &&
                    (psName.IndexOf("flare", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     psName.IndexOf("flash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     psName.IndexOf("glow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     psName.IndexOf("light", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     psName.IndexOf("shockwave", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     psName.IndexOf("refract", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     psName.IndexOf("sphere", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     psName.IndexOf("core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     psName.IndexOf("orb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     matName.IndexOf("flare", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (psr != null && psr.renderMode == ParticleSystemRenderMode.Mesh) ||
                     isStationaryCenterOrb))
                {
                    ps.gameObject.SetActive(false);
                    continue;
                }

                main.loop = true;
                main.playOnAwake = true;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.startSizeMultiplier *= sizeMult;
                main.startSpeedMultiplier *= sizeMult;
                main.maxParticles = Mathf.Min(main.maxParticles, 28);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    tintColor,
                    new Color(1.0f, 0.22f, 0.42f, tintColor.a * 0.85f));

                var emission = ps.emission;
                emission.rateOverTimeMultiplier *= 0.70f;
                for (int b = 0; b < emission.burstCount; b++)
                {
                    ParticleSystem.Burst burst = emission.GetBurst(b);
                    burst.count = new ParticleSystem.MinMaxCurve(Mathf.Min(6f, Mathf.Max(2f, burst.count.constant * 0.35f)));
                    emission.SetBurst(b, burst);
                }

                ps.Play(withChildren: false);
            }
        }

        internal static int ApplyStyleToHierarchy(Transform root, int level, ref Light auraLight, out float baseLightIntensity)
        {
            baseLightIntensity = 0f;
            bool coolEnabled = AshlandsDvergrPlugin.EnableCoolVisuals.Value;
            float lightMult = Mathf.Max(0f, AshlandsDvergrPlugin.AuraLightIntensity.Value);

            VisEquipment visEq = root.GetComponentInParent<VisEquipment>() ?? root.GetComponentInChildren<VisEquipment>();
            EnsureBloodWeaponVisual(root, visEq, level);
            Ensure2StarBossEffects(root, level, coolEnabled, lightMult);

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);

            // Armor/cloak/hat diffuse tint (pure RGB multiplication with _Hue/_Sat/_Val = 0 avoids Custom/Creature HSV wraparound artifacts
            // and preserves 100% of original texture shading, stitches, and dark folds)
            Color armorColorMult = !coolEnabled ? Color.white : (level switch
            {
                3 => new Color(1.18f, 0.46f, 0.42f, 1f), // 2-Star Ashlord: deep volcanic crimson & charred blood-iron
                2 => new Color(1.28f, 0.88f, 0.55f, 1f), // 1-Star Flameforged: warm burnished bronze & golden-copper
                _ => Color.white                         // 0-Star: 100% original Dvergr tunic/armor
            });

            // Body/skin tint (preserves iconic Dvergr blue-slate skin)
            Color bodyColorMult = !coolEnabled ? Color.white : (level switch
            {
                3 => new Color(0.70f, 0.74f, 0.86f, 1f), // 2-Star: dark obsidian-slate skin
                2 => new Color(0.88f, 0.92f, 1.00f, 1f), // 1-Star: steely slate-blue skin
                _ => Color.white                         // 0-Star: original blue-teal skin
            });

            // Weapon tint (subtle arcane-forged sheen on 2-star weapons while keeping wood/metal detail)
            Color weaponColorMult = (!coolEnabled || level < 3)
                ? Color.white
                : new Color(1.08f, 0.92f, 1.18f, 1f);

            foreach (Renderer r in renderers)
            {
                if (r == null || (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) || r.sharedMaterials == null)
                {
                    continue;
                }
                Material[] mats = r.materials;
                bool isWeaponRenderer = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null)
                    {
                        continue;
                    }
                    int matId = m.GetInstanceID();
                    bool hasEmissionMap = m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") != null;
                    bool isBodyMat = m.name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isWeaponMat = !isBodyMat && IsWeaponRenderer(r, m, visEq);
                    if (isWeaponMat)
                    {
                        isWeaponRenderer = true;
                    }

                    if (m.HasProperty("_Color"))
                    {
                        if (!OriginalMaterialColors.TryGetValue(matId, out Color origColor))
                        {
                            origColor = m.GetColor("_Color");
                            OriginalMaterialColors[matId] = origColor;
                        }
                        Color mult = isBodyMat ? bodyColorMult : (isWeaponMat ? weaponColorMult : armorColorMult);
                        m.SetColor("_Color", new Color(
                            origColor.r * mult.r,
                            origColor.g * mult.g,
                            origColor.b * mult.b,
                            origColor.a));
                    }

                    // Keep _Hue/_Saturation/_Value at 0 so Custom/Creature never triggers HSV channel wraparound banding
                    if (m.HasProperty("_Hue"))
                    {
                        m.SetFloat("_Hue", 0f);
                    }
                    if (m.HasProperty("_Saturation"))
                    {
                        m.SetFloat("_Saturation", 0f);
                    }
                    if (m.HasProperty("_Value"))
                    {
                        m.SetFloat("_Value", 0f);
                    }

                    if (m.HasProperty("_EmissionColor"))
                    {
                        if (!OriginalMaterialEmissions.TryGetValue(matId, out Color origEmission))
                        {
                            origEmission = m.GetColor("_EmissionColor");
                            OriginalMaterialEmissions[matId] = origEmission;
                        }

                        if (isBodyMat && hasEmissionMap)
                        {
                            // Masked emission on DvergerBody eyes:
                            // Negative G on 1-star/2-star cancels the cyan green baked into _MainTex exclusively on the eye mask!
                            Color eyeEmission = !coolEnabled ? origEmission : (level switch
                            {
                                3 => new Color(0.80f, -0.65f, 1.45f, 1f), // 2-Star: vivid royal purple eyes
                                2 => new Color(1.20f, -0.15f, -0.85f, 1f), // 1-Star: fiery golden-amber eyes
                                _ => origEmission                          // 0-Star: original cyan eyes
                            });
                            m.SetColor("_EmissionColor", eyeEmission);
                        }
                        else if (isWeaponMat)
                        {
                            if (coolEnabled && level >= 3)
                            {
                                // Subtle purple-arcane weapon glow on 2-star Dvergrs
                                m.EnableKeyword("_EMISSION");
                                Color wepGlow = hasEmissionMap
                                    ? new Color(1.60f, 0.35f, 2.40f, 1f)
                                    : new Color(0.22f, 0.06f, 0.34f, 1f);
                                m.SetColor("_EmissionColor", wepGlow);
                            }
                            else
                            {
                                m.SetColor("_EmissionColor", origEmission);
                            }
                        }
                        else
                        {
                            // Unmasked cloth/armor material: keep emission black so textures never wash out
                            m.SetColor("_EmissionColor", Color.black);
                        }
                    }

                    // Enable Custom/Creature boss ember/rune vein glow (_NoiseGlowEnabled) on 2-star armor when Enable2StarBossEffects is active
                    if (m.HasProperty("_NoiseGlowEnabled"))
                    {
                        bool enableBossNoiseGlow = coolEnabled && level >= 3 && AshlandsDvergrPlugin.Enable2StarBossEffects.Value && !isBodyMat && !isWeaponMat;
                        m.SetFloat("_NoiseGlowEnabled", enableBossNoiseGlow ? 1f : 0f);
                        if (enableBossNoiseGlow)
                        {
                            if (m.HasProperty("_NoiseGlowColor"))
                            {
                                m.SetColor("_NoiseGlowColor", new Color(1.40f, 0.22f, 1.85f, 1f));
                            }
                            if (m.HasProperty("_NoiseGlowSize"))
                            {
                                m.SetFloat("_NoiseGlowSize", 4.5f);
                            }
                            if (m.HasProperty("_NoiseGlowEdge"))
                            {
                                m.SetFloat("_NoiseGlowEdge", 0.35f);
                            }
                        }
                    }
                }

                if (isWeaponRenderer)
                {
                    Transform existingWepLight = r.transform.Find("AshlandsDvergr_WeaponGlow");
                    if (coolEnabled && level >= 3 && lightMult > 0f)
                    {
                        Light wepLight;
                        if (existingWepLight == null)
                        {
                            GameObject wlObj = new GameObject("AshlandsDvergr_WeaponGlow");
                            wlObj.transform.SetParent(r.transform, worldPositionStays: false);
                            wlObj.transform.localPosition = Vector3.zero;
                            wepLight = wlObj.AddComponent<Light>();
                            wepLight.type = LightType.Point;
                            wepLight.shadows = LightShadows.None;
                        }
                        else
                        {
                            wepLight = existingWepLight.GetComponent<Light>();
                        }
                        if (wepLight != null)
                        {
                            wepLight.enabled = true;
                            wepLight.color = new Color(0.72f, 0.30f, 1.0f);
                            wepLight.range = 1.15f;
                            wepLight.intensity = 0.28f * lightMult;
                        }
                    }
                    else if (existingWepLight != null)
                    {
                        Light wepLight = existingWepLight.GetComponent<Light>();
                        if (wepLight != null)
                        {
                            wepLight.enabled = false;
                        }
                    }
                }
            }

            if (!coolEnabled || lightMult <= 0f)
            {
                if (auraLight != null)
                {
                    auraLight.enabled = false;
                }
                return renderers.Length;
            }

            if (auraLight == null)
            {
                GameObject lightObj = new GameObject("AshlandsDvergr_ForgeAura");
                lightObj.transform.SetParent(root, worldPositionStays: false);
                lightObj.transform.localPosition = new Vector3(0f, 1.35f, 0.20f);
                auraLight = lightObj.AddComponent<Light>();
                auraLight.type = LightType.Point;
                auraLight.shadows = LightShadows.None;
            }

            auraLight.enabled = true;
            auraLight.color = level switch
            {
                3 => new Color(0.82f, 0.28f, 0.95f), // 2-Star: subtle arcane-purple & ember aura
                2 => new Color(1.0f, 0.68f, 0.22f),  // 1-Star: subtle golden-amber aura
                _ => new Color(0.85f, 0.92f, 1.0f)   // 0-Star: faint cool mystic glow
            };
            auraLight.range = level switch
            {
                3 => 3.5f,
                2 => 2.8f,
                _ => 2.0f
            };
            baseLightIntensity = (level switch
            {
                3 => 0.45f,
                2 => 0.30f,
                _ => 0.12f
            }) * lightMult;
            auraLight.intensity = baseLightIntensity;
            return renderers.Length;
        }
    }

    internal class ConfigurationManagerAttributes
    {
        public int? Order;
        public bool? Browsable;
    }
}
