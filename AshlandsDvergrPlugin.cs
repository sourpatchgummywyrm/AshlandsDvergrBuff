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
        public const string PluginVersion = "1.0.5";

        internal static ManualLogSource Log;

        // Configuration entries
        internal static ConfigEntry<float> SpawnRateMultiplier;
        internal static ConfigEntry<float> CampRespawnIntervalMinutes;

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
        internal static ConfigEntry<float> PassiveHealPercentPerTick;
        internal static ConfigEntry<float> PassiveHealIntervalSeconds;

        internal static ConfigEntry<int> MinLevel;
        internal static ConfigEntry<int> MaxLevel;
        internal static ConfigEntry<float> LevelUpChance;
        internal static ConfigEntry<float> VisualScale1Star;
        internal static ConfigEntry<float> VisualScale2Star;
        internal static ConfigEntry<bool> EnableCoolVisuals;
        internal static ConfigEntry<float> AuraLightIntensity;

        private static readonly HashSet<int> PatchedSpawnLists = new HashSet<int>();
        private static readonly HashSet<int> PatchedCreatureSpawners = new HashSet<int>();
        private static readonly HashSet<int> PatchedSpawnAreas = new HashSet<int>();
        private static readonly Dictionary<int, float> OriginalPrefabHealth = new Dictionary<int, float>();

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
            HealthMultiplier = Config.Bind("3. Combat Stats", "HealthMultiplier", 2.0f,
                new ConfigDescription(
                    "Base health multiplier for Dvergrs in the Ashlands (default: 2.0 = 2x base HP: 600 -> 1200).\n" +
                    "Higher star levels scale up from this base.\n" +
                    "Range: 0.5 to 10.0",
                    new AcceptableValueRange<float>(0.5f, 10.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            DamageMultiplier = Config.Bind("3. Combat Stats", "DamageMultiplier", 2.0f,
                new ConfigDescription(
                    "Outgoing damage multiplier for Ashlands Dvergrs (melee strikes, arbalest bolts, and magic AOEs).\n" +
                    "Default: 2.0 (2x damage, plus vanilla +50% per star level).\n" +
                    "Range: 0.5 to 10.0",
                    new AcceptableValueRange<float>(0.5f, 10.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            PassiveHealPercentPerTick = Config.Bind("3. Combat Stats", "PassiveHealPercentPerTick", 2.0f,
                new ConfigDescription(
                    "Percentage of max health regenerated per heal tick by Ashlands Dvergrs (0 = disabled).\n" +
                    "Default: 2.0% every 5 seconds (~24 HP/tick at 0-star, ~72 HP/tick at 2-star).\n" +
                    "Range: 0.0 to 25.0",
                    new AcceptableValueRange<float>(0.0f, 25.0f),
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

            VisualScale1Star = Config.Bind("4. Stars & Levels", "VisualScale1Star", 1.1f,
                new ConfigDescription(
                    "Visual body scale multiplier for 1-star Ashlands Dvergrs.\n" +
                    "Default: 1.1 (10% larger).\n" +
                    "Range: 1.0 to 1.5",
                    new AcceptableValueRange<float>(1.0f, 1.5f),
                    new ConfigurationManagerAttributes { Order = --order }));

            VisualScale2Star = Config.Bind("4. Stars & Levels", "VisualScale2Star", 1.2f,
                new ConfigDescription(
                    "Visual body scale multiplier for 2-star Ashlands Dvergrs.\n" +
                    "Default: 1.2 (20% larger).\n" +
                    "Range: 1.0 to 2.0",
                    new AcceptableValueRange<float>(1.0f, 2.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            EnableCoolVisuals = Config.Bind("4. Stars & Levels", "EnableCoolVisuals", true,
                new ConfigDescription(
                    "Enhances starred Ashlands Dvergrs with subtle veteran armor shading and a soft warm rim light while preserving their original textures.\n" +
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
            GameObject dvergrAshlands = ZNetScene.instance.GetPrefab("DvergerAshlands");
            if (dvergrAshlands != null)
            {
                Character ch = dvergrAshlands.GetComponent<Character>();
                if (ch != null && OriginalPrefabHealth.TryGetValue(dvergrAshlands.GetInstanceID(), out float baseHp))
                {
                    ch.m_health = baseHp * Mathf.Max(0.1f, HealthMultiplier.Value);
                    EnsureVisualStarSetups(ch);
                    Log.LogInfo($"Refreshed DvergerAshlands prefab base health to {ch.m_health} (HealthMultiplier={HealthMultiplier.Value}).");
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

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        private static void ZNetScene_Awake_Postfix(ZNetScene __instance)
        {
            if (__instance == null)
            {
                return;
            }
            GameObject dvergrAshlands = __instance.GetPrefab("DvergerAshlands");
            if (dvergrAshlands != null)
            {
                Character ch = dvergrAshlands.GetComponent<Character>();
                if (ch != null)
                {
                    int id = dvergrAshlands.GetInstanceID();
                    if (!OriginalPrefabHealth.TryGetValue(id, out float baseHp))
                    {
                        baseHp = ch.m_health;
                        OriginalPrefabHealth[id] = baseHp;
                    }
                    ch.m_health = baseHp * Mathf.Max(0.1f, HealthMultiplier.Value);
                    EnsureVisualStarSetups(ch);
                    Log.LogInfo($"Configured DvergerAshlands prefab base health: {baseHp} -> {ch.m_health}");
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
                __instance.m_health *= Mathf.Max(0.1f, HealthMultiplier.Value);
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
                float maxHp = __instance.GetMaxHealthBase() * __instance.GetLevel();
                __instance.SetMaxHealth(maxHp);
                __instance.SetHealth(maxHp);
            }
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
                float maxHp = __instance.GetMaxHealth();
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
        private static void CreatureSpawner_Spawn_Prefix(CreatureSpawner __instance, Location ___m_location)
        {
            if (___m_location == null || __instance.m_creaturePrefab == null)
            {
                return;
            }
            if (IsDvergrPrefab(__instance.m_creaturePrefab) &&
                (__instance.m_creaturePrefab.name.IndexOf("Ashlands", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 IsInAshlands(__instance.transform.position)))
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
                }
            }
        }

        [HarmonyPatch(typeof(SpawnSystem), "GetLevelUpChance", new Type[] { typeof(Vector3), typeof(float) })]
        [HarmonyPostfix]
        private static void SpawnSystem_GetLevelUpChance_Postfix(Vector3 position, float levelUpChanceOverride, ref float __result)
        {
            if (levelUpChanceOverride >= LevelUpChance.Value && IsInAshlands(position))
            {
                __result = Mathf.Max(__result, Mathf.Clamp(levelUpChanceOverride, 0f, 100f));
            }
        }

        [HarmonyPatch(typeof(SEMan), "ModifyAttack")]
        [HarmonyPostfix]
        private static void SEMan_ModifyAttack_Postfix(Character ___m_character, ref HitData hitData)
        {
            if (hitData != null && IsAshlandsDvergr(___m_character))
            {
                hitData.m_damage.Modify(Mathf.Max(0.1f, DamageMultiplier.Value));
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
                float mult = Mathf.Max(0.1f, DamageMultiplier.Value) * (1f + Mathf.Max(0, owner.GetLevel() - 1) * 0.5f);
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

        [HarmonyPatch(typeof(FejdStartup), "Start")]
        [HarmonyPostfix]
        private static void FejdStartup_Start_Postfix(FejdStartup __instance)
        {
            if (Environment.GetEnvironmentVariable("RENDER_DVERGR_VALIDATION") == "1" && __instance != null)
            {
                __instance.StartCoroutine(RenderDvergrValidationCoroutine());
            }
        }

        private static IEnumerator RenderDvergrValidationCoroutine()
        {
            yield return new WaitForSeconds(1.5f);
            Log.LogInfo("=== Starting DvergerAshlands Visual Validation Render ===");
            try
            {
                Runtime.MakeAllAssetsLoadable();
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

                Transform visualTemplate = prefab.transform.Find("Visual");
                if (visualTemplate == null)
                {
                    visualTemplate = prefab.transform;
                }

                Vector3 stageOrigin = new Vector3(0f, 500f, 0f);
                GameObject stageRoot = new GameObject("DvergrValidationStage");
                stageRoot.transform.position = stageOrigin;

                float[] xOffsets = { -1.65f, 0f, 1.65f };
                float[] scales = { 1.0f, VisualScale1Star.Value, VisualScale2Star.Value };

                for (int i = 0; i < 3; i++)
                {
                    int level = i + 1;
                    GameObject model = UnityEngine.Object.Instantiate(visualTemplate.gameObject, stageRoot.transform);
                    model.transform.localPosition = new Vector3(xOffsets[i], 0f, 0f);
                    model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    model.transform.localScale = Vector3.one * scales[i];

                    SkinnedMeshRenderer bodySmr = model.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true);
                    if (bodySmr != null)
                    {
                        foreach (GameObject itemPrefab in equipItems)
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
                                    if (i == 0)
                                    {
                                        foreach (Material m in smr.sharedMaterials)
                                        {
                                            if (m == null) continue;
                                            int propCount = m.shader != null ? m.shader.GetPropertyCount() : 0;
                                            List<string> props = new List<string>();
                                            for (int p = 0; p < propCount; p++)
                                                props.Add($"{m.shader.GetPropertyName(p)}({m.shader.GetPropertyType(p)})");
                                            Log.LogInfo($"[Attached {itemPrefab.name} Renderer] {smr.name} | Mat={m.name} | Shader={m.shader?.name} | Props=[{string.Join(", ", props)}]");
                                        }
                                    }
                                }
                            }
                        }
                    }

                    Light aura = null;
                    AshlandsDvergrEnhancer.ApplyStyleToHierarchy(model.transform, level, ref aura, out _);
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
                camObj.transform.localPosition = new Vector3(0f, 1.15f, -3.6f);
                camObj.transform.LookAt(stageOrigin + new Vector3(0f, 1.05f, 0f));

                Camera cam = camObj.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.11f, 0.10f, 0.12f, 1f);
                cam.fieldOfView = 42f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 50f;

                int width = 1536;
                int height = 768;
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
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
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

        internal static int ApplyStyleToHierarchy(Transform root, int level, ref Light auraLight, out float baseLightIntensity)
        {
            baseLightIntensity = 0f;
            bool coolEnabled = AshlandsDvergrPlugin.EnableCoolVisuals.Value;
            float lightMult = Mathf.Max(0f, AshlandsDvergrPlugin.AuraLightIntensity.Value);

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

            foreach (Renderer r in renderers)
            {
                if (r == null || (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) || r.sharedMaterials == null)
                {
                    continue;
                }
                Material[] mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null)
                    {
                        continue;
                    }
                    int matId = m.GetInstanceID();
                    bool hasEmissionMap = m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") != null;
                    bool isBodyMat = hasEmissionMap || m.name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (m.HasProperty("_Color"))
                    {
                        if (!OriginalMaterialColors.TryGetValue(matId, out Color origColor))
                        {
                            origColor = m.GetColor("_Color");
                            OriginalMaterialColors[matId] = origColor;
                        }
                        Color mult = isBodyMat ? bodyColorMult : armorColorMult;
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

                        if (hasEmissionMap)
                        {
                            // Masked emission (e.g., DvergerBody eyes): 0-star cyan, 1-star fiery amber-gold, 2-star blazing crimson
                            Color eyeEmission = !coolEnabled ? origEmission : (level switch
                            {
                                3 => new Color(6.5f, 0.05f, 0.15f, 1f),
                                2 => new Color(4.5f, 1.10f, 0.00f, 1f),
                                _ => origEmission
                            });
                            m.SetColor("_EmissionColor", eyeEmission);
                        }
                        else
                        {
                            // Unmasked cloth/armor material: keep emission black so textures never wash out
                            m.SetColor("_EmissionColor", Color.black);
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
                3 => new Color(1.0f, 0.25f, 0.10f), // 2-Star: subtle crimson-ember aura
                2 => new Color(1.0f, 0.68f, 0.22f), // 1-Star: subtle golden-amber aura
                _ => new Color(0.85f, 0.92f, 1.0f)  // 0-Star: faint cool mystic glow
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
