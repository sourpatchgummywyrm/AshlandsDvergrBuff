using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AshlandsDvergrMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class AshlandsDvergrPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.valheim.ashlandsdvergrmod";
        public const string PluginName = "Ashlands Dvergr Boost";
        public const string PluginVersion = "1.0.2";

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

        internal static ConfigEntry<int> MinLevel;
        internal static ConfigEntry<int> MaxLevel;
        internal static ConfigEntry<float> LevelUpChance;
        internal static ConfigEntry<float> VisualScale1Star;
        internal static ConfigEntry<float> VisualScale2Star;

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

            // 3. Combat Stats
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

            // 4. Stars & Levels
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

            // Text-file support: Save immediately so the .cfg file exists on disk with full comments
            Config.Save();

            // Hook live changes from GUI or code
            Config.SettingChanged += OnSettingChanged;
            Config.ConfigReloaded += OnConfigReloaded;

            // Watch .cfg file on disk for manual edits in any text editor
            SetupConfigFileWatcher();

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
                // Reload values from text file into ConfigEntry objects
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
            float s1 = Mathf.Max(1f, VisualScale1Star.Value);
            float s2 = Mathf.Max(s1, VisualScale2Star.Value);

            if (le.m_levelSetups.Count == 0)
            {
                le.m_levelSetups.Add(new LevelEffects.LevelSetup
                {
                    m_scale = s1,
                    m_hue = -0.05f,
                    m_saturation = 0.2f,
                    m_value = 0.05f
                });
            }
            else
            {
                le.m_levelSetups[0].m_scale = s1;
            }

            if (le.m_levelSetups.Count == 1)
            {
                le.m_levelSetups.Add(new LevelEffects.LevelSetup
                {
                    m_scale = s2,
                    m_hue = -0.1f,
                    m_saturation = 0.35f,
                    m_value = 0.1f
                });
            }
            else if (le.m_levelSetups.Count >= 2)
            {
                le.m_levelSetups[1].m_scale = s2;
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
            if (!IsAshlandsDvergr(__instance) || ___m_nview == null || !___m_nview.IsValid() || !___m_nview.IsOwner())
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
            if (IsAshlandsDvergr(__instance) && __instance.IsOwner())
            {
                float maxHp = __instance.GetMaxHealth();
                __instance.SetHealth(maxHp);
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
    }

    internal class ConfigurationManagerAttributes
    {
        public int? Order;
        public bool? Browsable;
    }
}
