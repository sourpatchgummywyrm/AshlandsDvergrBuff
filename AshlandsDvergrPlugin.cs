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
        public const string PluginVersion = "2.0.4";

        internal static ManualLogSource Log;

        // Configuration entries
        internal static ConfigEntry<float> SpawnRateMultiplier;
        internal static ConfigEntry<float> CampRespawnIntervalMinutes;
        internal static ConfigEntry<float> VarietyDvergrSpawnChance;
        internal static ConfigEntry<bool> EnableFireMageSpawns;
        internal static ConfigEntry<bool> EnableIceMageSpawns;
        internal static ConfigEntry<bool> EnableSupportMageSpawns;

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
        private static readonly System.Random VarietyRng = new System.Random();

        private static readonly List<string> VarietyDeck = new List<string>(10);
        private static int VarietyDeckIndex;
        private static float LastDeckVarietyChance = -1f;

        internal static bool InsideSpawnerSpawn;
        internal static bool InsideConsoleSpawn;
        internal static bool InsideConsoleVarietySplit;
        internal static int ConsoleExplicitLevel;
        internal static int PendingVarietySpawnLevel;
        internal static bool VarietyReplacedInCurrentSpawn;

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

            VarietyDvergrSpawnChance = Config.Bind("1. Spawning", "VarietyDvergrSpawnChance", 50f,
                new ConfigDescription(
                    "Percentage chance (0-100) for Ashlands Dvergr spawns to be a variety type (Fire Mage, Ice Mage, or Support Mage) instead of standard DvergerAshlands.\n" +
                    "Default: 50% (giving a 50% Crossbow / 50% Mages split: ~16.7% Fire / 16.7% Ice / 16.7% Support).\n" +
                    "Range: 0.0 to 100.0",
                    new AcceptableValueRange<float>(0.0f, 100.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            EnableFireMageSpawns = Config.Bind("1. Spawning", "EnableFireMageSpawns", true,
                new ConfigDescription(
                    "Allow Fire Mages (DvergerMageFire with Staff of Fracturing) to appear in Ashlands spawns.\n" +
                    "Default: true",
                    null,
                    new ConfigurationManagerAttributes { Order = --order }));

            EnableIceMageSpawns = Config.Bind("1. Spawning", "EnableIceMageSpawns", true,
                new ConfigDescription(
                    "Allow Ice Mages (DvergerMageIce with Dundr) to appear in Ashlands spawns.\n" +
                    "Default: true",
                    null,
                    new ConfigurationManagerAttributes { Order = --order }));

            EnableSupportMageSpawns = Config.Bind("1. Spawning", "EnableSupportMageSpawns", false,
                new ConfigDescription(
                    "Allow Support Mages (DvergerMageSupport with default Dvergr Staff of Healing, Heal, & Buff) to appear in Ashlands spawns.\n" +
                    "Default: false",
                    null,
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
                        bool isMage = IsDvergrMage(c);
                        float ashlandsStandardHp = isMage ? Mathf.Max(baseHp, 650f) : baseHp;
                        float hpMult = GetHealthMultiplierFor(c);
                        c.m_health = ashlandsStandardHp * hpMult;
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
            if (ch.gameObject.name.IndexOf("Ashlands", StringComparison.OrdinalIgnoreCase) >= 0 ||
                InsideSpawnerSpawn ||
                InsideConsoleSpawn ||
                PendingVarietySpawnLevel > 0 ||
                Console.IsVisible())
            {
                return true;
            }
            ZNetView nview = ch.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                ZDO zdo = nview.GetZDO();
                if (zdo != null && zdo.GetBool("AshlandsDvergrMod_IsAshlands"))
                {
                    return true;
                }
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

        internal static bool IsDvergrSupportMageName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            return name.IndexOf("MageSupport", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Support", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Heal", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static float GetHealthMultiplierFor(Character ch)
        {
            return IsDvergrMage(ch) ? Mathf.Max(0.1f, MageHealthMultiplier.Value) : Mathf.Max(0.1f, HealthMultiplier.Value);
        }

        internal static float GetDamageMultiplierFor(Character ch)
        {
            return IsDvergrMage(ch) ? Mathf.Max(0.1f, MageDamageMultiplier.Value) : Mathf.Max(0.1f, DamageMultiplier.Value);
        }

        internal static float NextVarietyRollPercent()
        {
            return (float)(VarietyRng.NextDouble() * 100.0);
        }

        internal static List<string> GetEnabledVarietyMagePrefabNames()
        {
            List<string> list = new List<string>(3);
            if (EnableFireMageSpawns == null || EnableFireMageSpawns.Value)
            {
                list.Add("DvergerMageFire");
            }
            if (EnableIceMageSpawns == null || EnableIceMageSpawns.Value)
            {
                list.Add("DvergerMageIce");
            }
            if (EnableSupportMageSpawns == null || EnableSupportMageSpawns.Value)
            {
                list.Add("DvergerMageSupport");
            }
            return list;
        }

        internal static string PickRandomVarietyMagePrefabName()
        {
            List<string> enabled = GetEnabledVarietyMagePrefabNames();
            if (enabled.Count == 0)
            {
                return "DvergerAshlands";
            }
            return enabled[VarietyRng.Next(enabled.Count)];
        }

        private static void RefillAndShuffleVarietyDeck(float varietyChance)
        {
            VarietyDeck.Clear();
            VarietyDeckIndex = 0;
            LastDeckVarietyChance = varietyChance;

            List<string> enabledMages = GetEnabledVarietyMagePrefabNames();
            int mageSlots = enabledMages.Count == 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(varietyChance / 10f), 0, 10);
            int rangerSlots = 10 - mageSlots;

            if (enabledMages.Count > 0)
            {
                int startMageIdx = VarietyRng.Next(enabledMages.Count);
                for (int i = 0; i < mageSlots; i++)
                {
                    VarietyDeck.Add(enabledMages[(startMageIdx + i) % enabledMages.Count]);
                }
            }
            for (int i = 0; i < rangerSlots; i++)
            {
                VarietyDeck.Add("DvergerAshlands");
            }

            // Fisher-Yates shuffle
            for (int i = VarietyDeck.Count - 1; i > 0; i--)
            {
                int j = VarietyRng.Next(i + 1);
                string tmp = VarietyDeck[i];
                VarietyDeck[i] = VarietyDeck[j];
                VarietyDeck[j] = tmp;
            }

            // Ensure at least 1 mage appears within the first 3 draws so testing a few spawns in console immediately shows mages
            if (mageSlots > 0 && VarietyDeck.Count >= 3)
            {
                bool hasEarlyMage = false;
                for (int i = 0; i < 3; i++)
                {
                    if (!string.Equals(VarietyDeck[i], "DvergerAshlands", StringComparison.OrdinalIgnoreCase))
                    {
                        hasEarlyMage = true;
                        break;
                    }
                }
                if (!hasEarlyMage)
                {
                    for (int i = 3; i < VarietyDeck.Count; i++)
                    {
                        if (!string.Equals(VarietyDeck[i], "DvergerAshlands", StringComparison.OrdinalIgnoreCase))
                        {
                            int earlySlot = VarietyRng.Next(3);
                            string tmp = VarietyDeck[earlySlot];
                            VarietyDeck[earlySlot] = VarietyDeck[i];
                            VarietyDeck[i] = tmp;
                            break;
                        }
                    }
                }
            }
        }

        internal static string RollVarietyPrefabForSpawn()
        {
            float varietyChance = Mathf.Clamp(VarietyDvergrSpawnChance.Value, 0f, 100f);
            if (varietyChance <= 0f)
            {
                return "DvergerAshlands";
            }
            if (VarietyDeck.Count == 0 || VarietyDeckIndex >= VarietyDeck.Count || Mathf.Abs(varietyChance - LastDeckVarietyChance) > 0.5f)
            {
                RefillAndShuffleVarietyDeck(varietyChance);
            }
            return VarietyDeck[VarietyDeckIndex++];
        }

        internal static int RollConfiguredLevel()
        {
            int min = Mathf.Max(1, MinLevel.Value);
            int max = Mathf.Max(min, MaxLevel.Value);
            float chance = Mathf.Clamp(LevelUpChance.Value, 0f, 100f);
            int level = min;
            while (level < max && NextVarietyRollPercent() <= chance)
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
            if (EnableCoolVisuals.Value)
            {
                // Prevent LevelEffects.Start() from overwriting DvergerBody's sharedMaterials with an un-tinted material
                le.m_mainRender = null;
            }
            else if (le.m_mainRender == null)
            {
                le.m_mainRender = ch.GetComponentInChildren<SkinnedMeshRenderer>();
            }

            float s1 = Mathf.Max(1f, VisualScale1Star.Value);
            float s2 = Mathf.Max(s1, VisualScale2Star.Value);

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
            if (IsDvergrSupportMageName(characterName))
            {
                return "DvergerStaffHeal";
            }
            if (characterName.IndexOf("Mage", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "StaffClusterbomb";
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
            if (ZNetScene.instance != null)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
                if (prefab != null)
                {
                    return prefab;
                }
            }
            if (ObjectDB.instance != null)
            {
                GameObject item = ObjectDB.instance.GetItemPrefab(prefabName);
                if (item != null)
                {
                    return item;
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
                    "DvergerMage" => "8c3b1095f7874dc469e0e324a6fb82d7",
                    "DvergerMageFire" => "1ed38d388ceda5f4e89f5be4d1768fab",
                    "DvergerMageIce" => "8ad104a536dd94d4bb764f8a9f3d8ee7",
                    "DvergerMageSupport" => "c0e85e3d180619047bdf97cd98271a4d",
                    "Dverger" => "b780f1078c33d2344836ccefd3bf9352",
                    "CrossbowRipperBlood" => "483a950c53776e74ea40aef310fefbb2",
                    "StaffClusterbomb" => "d4812897e4412e540b7fa649d25b99d5",
                    "StaffLightning" => "90f15fa074aa08a478750566117d84d0",
                    "StaffRedTroll" => "87eebe8749379744eacfa37ea818aeab",
                    "StaffIceShards" => "d268d47613aec2963b1878ed2a224e1e",
                    "StaffShield" => "2ac6892f975d1667f96a24586cd6b976",
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
                        bool isMage = IsDvergrMage(ch);
                        float ashlandsStandardHp = isMage ? Mathf.Max(baseHp, 650f) : baseHp;
                        float hpMult = GetHealthMultiplierFor(ch);
                        ch.m_health = ashlandsStandardHp * hpMult;
                        EnsureVisualStarSetups(ch);
                        Log.LogInfo($"Configured {pName} prefab base health: {baseHp} -> {ch.m_health} (mult={hpMult})");
                    }
                }
            }
        }

        internal static void ApplyAshlandsDvergrBaseHealth(Character ch)
        {
            if (ch == null)
            {
                return;
            }
            EnsureVisualStarSetups(ch);
            bool isMage = IsDvergrMage(ch);
            float baseStandard = isMage ? 650f : 1000f;
            float hpMult = GetHealthMultiplierFor(ch);
            float targetHp = baseStandard * hpMult;
            if (ch.m_health < targetHp)
            {
                ch.m_health = targetHp;
            }
        }

        internal static void EnsureAshlandsDvergrInitialized(Character ch, ZNetView nview)
        {
            if (ch == null || !IsAshlandsDvergr(ch))
            {
                return;
            }

            ApplyAshlandsDvergrBaseHealth(ch);

            AshlandsDvergrEnhancer enhancer = ch.GetComponent<AshlandsDvergrEnhancer>();
            if (enhancer == null)
            {
                enhancer = ch.gameObject.AddComponent<AshlandsDvergrEnhancer>();
                enhancer.WasSpawnedBySpawner = VarietyReplacedInCurrentSpawn || PendingVarietySpawnLevel > 0;
            }
            else if (VarietyReplacedInCurrentSpawn || PendingVarietySpawnLevel > 0)
            {
                enhancer.WasSpawnedBySpawner = true;
            }

            if (nview == null)
            {
                nview = ch.GetComponent<ZNetView>();
            }
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                return;
            }
            ZDO zdo = nview.GetZDO();
            if (zdo == null)
            {
                return;
            }
            zdo.Set("AshlandsDvergrMod_IsAshlands", true);
            if (VarietyReplacedInCurrentSpawn || PendingVarietySpawnLevel > 0)
            {
                zdo.Set("AshlandsDvergrMod_VarietyChecked", true);
            }
            const string RollKey = "AshlandsDvergrMod_Initialized";
            if (!zdo.GetBool(RollKey))
            {
                zdo.Set(RollKey, true);
                int rolledLevel = PendingVarietySpawnLevel > 0
                    ? PendingVarietySpawnLevel
                    : ((InsideConsoleSpawn && ConsoleExplicitLevel > 0) ? ConsoleExplicitLevel : RollConfiguredLevel());
                if (rolledLevel > ch.GetLevel())
                {
                    ch.SetLevel(rolledLevel);
                }
                float maxHp = CalculateScaledMaxHealth(ch);
                ch.SetMaxHealth(maxHp);
                ch.SetHealth(maxHp);
            }
        }

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.TryRunCommand))]
        [HarmonyPrefix]
        private static bool Terminal_TryRunCommand_Prefix(Terminal __instance, ref string text, bool silentFail, bool skipAllowedCheck)
        {
            if (string.IsNullOrEmpty(text))
            {
                return true;
            }

            string trimmed = text.Trim();
            if (!trimmed.StartsWith("spawn ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return true;
            }

            string spawnName = parts[1];
            bool isAnyDvergrSpawn = spawnName.IndexOf("Dverger", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    spawnName.IndexOf("Dvergr", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isAnyDvergrSpawn)
            {
                return true;
            }

            InsideConsoleSpawn = true;
            if (parts.Length >= 4 && int.TryParse(parts[3], out int explicitLvl) && explicitLvl > 0)
            {
                ConsoleExplicitLevel = explicitLvl;
            }
            else
            {
                ConsoleExplicitLevel = 0;
            }

            bool isExplicitMageRandom = string.Equals(spawnName, "DvergerMage", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(spawnName, "DvergrMage", StringComparison.OrdinalIgnoreCase);
            bool isVarietyTarget = isExplicitMageRandom ||
                                   string.Equals(spawnName, "DvergerAshlands", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(spawnName, "Dverger", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(spawnName, "Dvergr", StringComparison.OrdinalIgnoreCase);

            if (!isVarietyTarget || InsideConsoleVarietySplit)
            {
                return true;
            }

            int count = 1;
            if (parts.Length >= 3 && int.TryParse(parts[2], out int parsedCount) && parsedCount > 1)
            {
                count = parsedCount;
            }

            string tailArgs = parts.Length > 3 ? " " + string.Join(" ", parts, 3, parts.Length - 3) : string.Empty;

            if (count <= 1)
            {
                string rolledPrefab = isExplicitMageRandom ? PickRandomVarietyMagePrefabName() : RollVarietyPrefabForSpawn();
                string countArg = parts.Length >= 3 ? " " + parts[2] : string.Empty;
                text = $"spawn {rolledPrefab}{countArg}{tailArgs}";
                VarietyReplacedInCurrentSpawn = true;
                Log.LogInfo($"[Console Variety Spawn] Rolled '{rolledPrefab}' for '{trimmed}'");
                return true;
            }

            Dictionary<string, int> rolledCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                string rolled = isExplicitMageRandom ? PickRandomVarietyMagePrefabName() : RollVarietyPrefabForSpawn();
                rolledCounts.TryGetValue(rolled, out int cur);
                rolledCounts[rolled] = cur + 1;
            }

            InsideConsoleVarietySplit = true;
            VarietyReplacedInCurrentSpawn = true;
            try
            {
                foreach (KeyValuePair<string, int> kv in rolledCounts)
                {
                    string subCmd = $"spawn {kv.Key} {kv.Value}{tailArgs}";
                    Log.LogInfo($"[Console Variety Spawn] Executing batch split: '{subCmd}'");
                    __instance.TryRunCommand(subCmd, silentFail, skipAllowedCheck);
                }
            }
            finally
            {
                InsideConsoleVarietySplit = false;
                InsideConsoleSpawn = false;
                VarietyReplacedInCurrentSpawn = false;
                ConsoleExplicitLevel = 0;
            }
            return false;
        }

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.TryRunCommand))]
        [HarmonyPostfix]
        private static void Terminal_TryRunCommand_Postfix()
        {
            if (!InsideConsoleVarietySplit)
            {
                InsideConsoleSpawn = false;
                VarietyReplacedInCurrentSpawn = false;
                ConsoleExplicitLevel = 0;
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
            ApplyAshlandsDvergrBaseHealth(__instance);
        }

        [HarmonyPatch(typeof(Character), "Awake")]
        [HarmonyPostfix]
        private static void Character_Awake_Postfix(Character __instance, ZNetView ___m_nview)
        {
            EnsureAshlandsDvergrInitialized(__instance, ___m_nview);
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
        private static void Character_SetLevel_Postfix(Character __instance, ZNetView ___m_nview)
        {
            if (!IsAshlandsDvergr(__instance))
            {
                return;
            }
            EnsureAshlandsDvergrInitialized(__instance, ___m_nview);
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

        [HarmonyPatch(typeof(LevelEffects), "SetupLevelVisualization")]
        [HarmonyPostfix]
        private static void LevelEffects_SetupLevelVisualization_Postfix(LevelEffects __instance)
        {
            if (__instance == null)
            {
                return;
            }
            Character ch = __instance.GetComponentInParent<Character>();
            if (ch != null)
            {
                AshlandsDvergrEnhancer enhancer = ch.GetComponent<AshlandsDvergrEnhancer>();
                if (enhancer != null)
                {
                    enhancer.ApplyCoolVisuals(fromLevelEffects: true);
                }
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
                InsideSpawnerSpawn = true;
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

                string pickName = RollVarietyPrefabForSpawn();
                if (!string.Equals(pickName, __instance.m_creaturePrefab.name, StringComparison.OrdinalIgnoreCase))
                {
                    GameObject altPrefab = ResolvePrefabByNameOrAssetId(pickName);
                    if (altPrefab != null)
                    {
                        __state = __instance.m_creaturePrefab;
                        __instance.m_creaturePrefab = altPrefab;
                        VarietyReplacedInCurrentSpawn = true;
                    }
                }
                else
                {
                    VarietyReplacedInCurrentSpawn = true;
                }
            }
        }

        [HarmonyPatch(typeof(CreatureSpawner), "Spawn")]
        [HarmonyPostfix]
        private static void CreatureSpawner_Spawn_Postfix(CreatureSpawner __instance, GameObject __state)
        {
            InsideSpawnerSpawn = false;
            VarietyReplacedInCurrentSpawn = false;
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
                List<string> enabledMages = GetEnabledVarietyMagePrefabNames();
                if (varietyChance > 0f && enabledMages.Count > 0 && ZNetScene.instance != null)
                {
                    float perVariantWeight = Mathf.Max(0.05f, (baseWeight * (varietyChance / 100f)) / enabledMages.Count);
                    foreach (string varName in enabledMages)
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
                    List<string> enabledMages = GetEnabledVarietyMagePrefabNames();
                    if (varietyChance > 0f && enabledMages.Count > 0)
                    {
                        float varChance = Mathf.Clamp(AmbientSpawnChance.Value * (varietyChance / 100f), 1f, 100f);
                        int varMaxSpawned = Mathf.Max(1, AmbientMaxSpawned.Value / 2);
                        foreach (string varName in enabledMages)
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

        [HarmonyPatch(typeof(Humanoid), "GiveDefaultItems")]
        [HarmonyPostfix]
        private static void Humanoid_GiveDefaultItems_Postfix(Humanoid __instance)
        {
            if (__instance == null || !IsAshlandsDvergr(__instance))
            {
                return;
            }
            AshlandsDvergrEnhancer.ConfigureAshlandsCombatInventory(__instance);
        }

        private static IEnumerator RenderDvergrValidationCoroutine()
        {
            yield return new WaitForSeconds(1.0f);
            Log.LogInfo("=== Starting DvergerAshlands Visual Validation Render ===");
            Vector3 stageOrigin = new Vector3(0f, 500f, 0f);
            GameObject stageRoot = null;
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

                // Comprehensive runtime tests for Console Variety Spawning, Mistlands Mage Scaling, and Ashlands Weapon Attacks
                List<string> first5 = new List<string>();
                for (int r = 0; r < 5; r++)
                {
                    string cmd = "spawn DvergerAshlands";
                    Terminal_TryRunCommand_Prefix(null, ref cmd, true, true);
                    Terminal_TryRunCommand_Postfix();
                    first5.Add(cmd);
                }

                string mageCmd = "spawn DvergerMage 1 3";
                Terminal_TryRunCommand_Prefix(null, ref mageCmd, true, true);
                Terminal_TryRunCommand_Postfix();

                Dictionary<string, int> dist100 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int r = 0; r < 100; r++)
                {
                    string rolled = RollVarietyPrefabForSpawn();
                    dist100.TryGetValue(rolled, out int c);
                    dist100[rolled] = c + 1;
                }
                List<string> distSummary = new List<string>();
                foreach (var kv in dist100) distSummary.Add($"{kv.Key}={kv.Value}%");
                Log.LogInfo($"[RuntimeTest] First 5 console spawns: [{string.Join(" | ", first5)}], 'spawn DvergerMage 1 3' -> '{mageCmd}', 100-roll split: [{string.Join(", ", distSummary)}]");

                string[] testCreatures = { "DvergerAshlands", "DvergerMageFire", "DvergerMageIce", "DvergerMageSupport" };
                foreach (string tcName in testCreatures)
                {
                    GameObject tcPrefab = ResolvePrefabByNameOrAssetId(tcName);
                    if (tcPrefab == null) continue;
                    bool prevDisable = ZNetView.m_forceDisableInit;
                    ZNetView.m_forceDisableInit = true;
                    GameObject tcInst = null;
                    try
                    {
                        InsideConsoleSpawn = true;
                        ConsoleExplicitLevel = 3;
                        tcInst = UnityEngine.Object.Instantiate(tcPrefab, stageOrigin + new Vector3(0f, 20f, 0f), Quaternion.identity);
                        Character tcCh = tcInst.GetComponent<Character>();
                        Humanoid tcHum = tcInst.GetComponent<Humanoid>();
                        if (tcCh != null)
                        {
                            ApplyAshlandsDvergrBaseHealth(tcCh);
                            var levelField = typeof(Character).GetField("m_level", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            levelField?.SetValue(tcCh, 3);
                        }
                        if (tcHum != null && tcHum.m_defaultItems != null)
                        {
                            Inventory inv = tcHum.GetInventory();
                            foreach (GameObject di in tcHum.m_defaultItems)
                            {
                                if (di != null) inv.AddItem(di, 1);
                            }
                            AshlandsDvergrEnhancer.ConfigureAshlandsCombatInventory(tcHum);
                            float scaled0StarHp = tcCh.m_health;
                            float scaled2StarHp = CalculateScaledMaxHealth(tcCh);
                            foreach (ItemDrop.ItemData item in inv.GetAllItems())
                            {
                                if (item?.m_shared?.m_attack == null) continue;
                                var sd = item.m_shared;
                                var atk = sd.m_attack;
                                if (sd.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon || sd.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon || sd.m_itemType == ItemDrop.ItemData.ItemType.Bow)
                                {
                                    HitData testHit = new HitData { m_damage = sd.m_damages.Clone() };
                                    // Simulate vanilla 2-star (2.0x) followed by SEMan_ModifyAttack_Postfix
                                    testHit.m_damage.Modify(2.0f);
                                    SEMan_ModifyAttack_Postfix(tcCh, ref testHit);
                                    Log.LogInfo($"[RuntimeVerifyAttack {tcName}] hp0Star={scaled0StarHp}, hp2Star={scaled2StarHp} | item={item.m_dropPrefab?.name ?? sd.m_name} | atkAnim={atk.m_attackAnimation} | proj={atk.m_attackProjectile?.name} | bursts={atk.m_projectiles}/{atk.m_burstInterval}s | eitr={atk.m_attackEitr} | aiRange={sd.m_aiAttackRange}m | aiInterval={sd.m_aiAttackInterval}s | final2StarDmg=(pierce={testHit.m_damage.m_pierce:F1}, fire={testHit.m_damage.m_fire:F1}, frost={testHit.m_damage.m_frost:F1}, lightning={testHit.m_damage.m_lightning:F1}, blunt={testHit.m_damage.m_blunt:F1})");
                                }
                            }
                        }
                    }
                    finally
                    {
                        if (tcInst != null) UnityEngine.Object.DestroyImmediate(tcInst);
                        InsideConsoleSpawn = false;
                        ConsoleExplicitLevel = 0;
                        ZNetView.m_forceDisableInit = prevDisable;
                    }
                }

                stageRoot = new GameObject("DvergrValidationStage");
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

                // Advance root ParticleSystems by 2.50s in 50 frame steps (with restart: false)
                // Since particle lifetime is 0.35..0.75s, all t=0 burst particles die before t=0.75s,
                // proving continuous emission and repeating bursts at t=2.50s!
                foreach (ParticleSystem ps in stageRoot.GetComponentsInChildren<ParticleSystem>(includeInactive: false))
                {
                    if (ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>() == null)
                    {
                        ps.Simulate(0.05f, withChildren: true, restart: true);
                        for (int step = 0; step < 50; step++)
                        {
                            ps.Simulate(0.05f, withChildren: true, restart: false);
                        }
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
                camObj.transform.localPosition = new Vector3(0f, 1.15f, -3.7f);
                camObj.transform.LookAt(stageOrigin + new Vector3(0f, 1.05f, 0f));

                Camera cam = camObj.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.11f, 0.10f, 0.12f, 1f);
                cam.fieldOfView = 42f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 50f;

                int width = 1600;
                int height = 900;
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

        internal bool WasSpawnedBySpawner;

        private Character _character;
        private ZNetView _nview;
        private Light _auraLight;
        private float _healTimer;
        private float _visualVerifyTimer;
        private int _visualVerifyPasses;
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
            if (TryReplaceWithVarietyDvergr())
            {
                return;
            }
            ApplyCoolVisuals();
        }

        private bool TryReplaceWithVarietyDvergr()
        {
            if (_character == null || WasSpawnedBySpawner || _nview == null || !_nview.IsValid() || !_nview.IsOwner())
            {
                return false;
            }

            string objName = gameObject.name ?? string.Empty;
            bool isStandardAshlandsRanger = objName.StartsWith("DvergerAshlands", StringComparison.OrdinalIgnoreCase);
            bool isClassicRogue = objName.StartsWith("Dverger", StringComparison.OrdinalIgnoreCase) &&
                                  !isStandardAshlandsRanger &&
                                  objName.IndexOf("Mage", StringComparison.OrdinalIgnoreCase) < 0;

            if (!isStandardAshlandsRanger && !isClassicRogue)
            {
                return false;
            }

            ZDO zdo = _nview.GetZDO();
            if (zdo == null || zdo.GetBool("AshlandsDvergrMod_VarietyChecked"))
            {
                return false;
            }
            zdo.Set("AshlandsDvergrMod_VarietyChecked", true);

            string targetPrefabName = AshlandsDvergrPlugin.RollVarietyPrefabForSpawn();
            if (isStandardAshlandsRanger && string.Equals(targetPrefabName, "DvergerAshlands", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            GameObject altPrefab = AshlandsDvergrPlugin.ResolvePrefabByNameOrAssetId(targetPrefabName);
            if (altPrefab == null)
            {
                return false;
            }

            int targetLevel = Mathf.Max(1, _character.GetLevel());
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;

            AshlandsDvergrPlugin.PendingVarietySpawnLevel = targetLevel;
            GameObject newObj;
            try
            {
                newObj = UnityEngine.Object.Instantiate(altPrefab, pos, rot);
            }
            finally
            {
                AshlandsDvergrPlugin.PendingVarietySpawnLevel = 0;
            }

            if (newObj == null)
            {
                return false;
            }

            ZNetView newNview = newObj.GetComponent<ZNetView>();
            if (newNview != null && newNview.IsValid() && newNview.GetZDO() != null)
            {
                ZDO newZdo = newNview.GetZDO();
                newZdo.Set("AshlandsDvergrMod_IsAshlands", true);
                newZdo.Set("AshlandsDvergrMod_VarietyChecked", true);
            }
            Character newCh = newObj.GetComponent<Character>();
            if (newCh != null && newCh.GetLevel() != targetLevel)
            {
                newCh.SetLevel(targetLevel);
            }
            _nview.Destroy();
            return true;
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

            // Re-verify visuals across initial post-spawn frames so VisEquipment.CustomUpdate() and LevelEffects.Start() never overwrite styling
            if (_visualVerifyPasses < 4)
            {
                _visualVerifyTimer += Time.deltaTime;
                float nextThreshold = _visualVerifyPasses == 0 ? 0.10f : (_visualVerifyPasses == 1 ? 0.40f : (_visualVerifyPasses == 2 ? 1.0f : 2.5f));
                if (_visualVerifyTimer >= nextThreshold)
                {
                    _visualVerifyPasses++;
                    ApplyCoolVisuals();
                }
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

        internal static void ConfigureAshlandsCombatInventory(Humanoid hum)
        {
            if (hum == null)
            {
                return;
            }
            Inventory inv = hum.GetInventory();
            if (inv == null)
            {
                return;
            }
            List<ItemDrop.ItemData> items = inv.GetAllItems();
            if (items == null || items.Count == 0)
            {
                return;
            }

            string charName = hum.gameObject.name ?? string.Empty;
            bool isMage = AshlandsDvergrPlugin.IsDvergrMageName(charName);
            bool isSupportMage = AshlandsDvergrPlugin.IsDvergrSupportMageName(charName);
            int level = Mathf.Max(1, hum.GetLevel());
            bool useAshlandsWep = AshlandsDvergrPlugin.UseAshlandsBloodWeapons.Value && (level >= 3 || (isMage && !isSupportMage));
            if (!useAshlandsWep || isSupportMage)
            {
                return;
            }

            string wepPrefabName = AshlandsDvergrPlugin.GetEquivalentBloodWeaponPrefabName(charName);
            GameObject wepPrefab = AshlandsDvergrPlugin.ResolvePrefabByNameOrAssetId(wepPrefabName);
            ItemDrop wepDrop = wepPrefab != null ? wepPrefab.GetComponent<ItemDrop>() : null;
            if (wepDrop == null || wepDrop.m_itemData == null || wepDrop.m_itemData.m_shared == null)
            {
                return;
            }

            ItemDrop.ItemData.SharedData srcShared = wepDrop.m_itemData.m_shared;
            Attack srcAtk = srcShared.m_attack;
            if (srcAtk == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null || item.m_shared == null || item.m_shared.m_attack == null)
                {
                    continue;
                }

                string dropName = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
                if (string.IsNullOrEmpty(dropName))
                {
                    continue;
                }

                // 1. Fire Mage (DvergerMageFire) -> Staff of Fracturing (StaffClusterbomb)
                if (string.Equals(wepPrefabName, "StaffClusterbomb", StringComparison.OrdinalIgnoreCase) &&
                    (dropName.IndexOf("fireball", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     dropName.IndexOf("clusterbomb", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    ItemDrop.ItemData.SharedData clonedShared = (ItemDrop.ItemData.SharedData)ShallowCloneSharedData(item.m_shared);
                    Attack clonedAtk = clonedShared.m_attack.Clone();
                    if (srcAtk.m_attackProjectile != null)
                    {
                        clonedAtk.m_attackProjectile = srcAtk.m_attackProjectile;
                    }
                    clonedAtk.m_projectiles = Mathf.Max(1, srcAtk.m_projectiles);
                    clonedAtk.m_burstInterval = srcAtk.m_burstInterval;
                    clonedAtk.m_projectileAccuracy = srcAtk.m_projectileAccuracy;
                    clonedAtk.m_projectileAccuracyMin = srcAtk.m_projectileAccuracyMin;
                    clonedAtk.m_useCharacterFacing = srcAtk.m_useCharacterFacing;
                    clonedAtk.m_useCharacterFacingYAim = srcAtk.m_useCharacterFacingYAim;
                    clonedAtk.m_launchAngle = srcAtk.m_launchAngle;
                    clonedAtk.m_projectileVel = srcAtk.m_projectileVel;
                    clonedAtk.m_projectileVelMin = srcAtk.m_projectileVelMin;
                    clonedAtk.m_attackEitr = 0f;
                    clonedAtk.m_attackHealth = 0f;
                    clonedAtk.m_attackHealthPercentage = 0f;
                    clonedAtk.m_attackStamina = 0f;
                    // Keep valid Dvergr animation ("attack_cast" or "attack_fireshower") so ZSyncAnimation triggers OnAttackTrigger!
                    clonedShared.m_attack = clonedAtk;
                    clonedShared.m_damages = srcShared.m_damages;
                    if (clonedShared.m_damages.m_fire < 130f)
                    {
                        clonedShared.m_damages.m_fire = 130f;
                    }
                    if (srcShared.m_startEffect != null && srcShared.m_startEffect.m_effectPrefabs != null && srcShared.m_startEffect.m_effectPrefabs.Length > 0)
                    {
                        clonedShared.m_startEffect = srcShared.m_startEffect;
                    }
                    if (srcShared.m_triggerEffect != null && srcShared.m_triggerEffect.m_effectPrefabs != null && srcShared.m_triggerEffect.m_effectPrefabs.Length > 0)
                    {
                        clonedShared.m_triggerEffect = srcShared.m_triggerEffect;
                    }
                    clonedShared.m_aiAttackRange = Mathf.Max(16f, clonedShared.m_aiAttackRange);
                    clonedShared.m_aiAttackInterval = dropName.IndexOf("clusterbomb", StringComparison.OrdinalIgnoreCase) >= 0 ? 18f : 5f;
                    item.m_shared = clonedShared;
                }
                // 2. Ice Mage (DvergerMageIce) -> Dundr (StaffLightning)
                else if (string.Equals(wepPrefabName, "StaffLightning", StringComparison.OrdinalIgnoreCase) &&
                         dropName.IndexOf("icebolt", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ItemDrop.ItemData.SharedData clonedShared = (ItemDrop.ItemData.SharedData)ShallowCloneSharedData(item.m_shared);
                    Attack clonedAtk = clonedShared.m_attack.Clone();
                    if (srcAtk.m_attackProjectile != null)
                    {
                        clonedAtk.m_attackProjectile = srcAtk.m_attackProjectile;
                    }
                    clonedAtk.m_projectiles = Mathf.Max(1, srcAtk.m_projectiles);
                    clonedAtk.m_burstInterval = srcAtk.m_burstInterval;
                    clonedAtk.m_projectileAccuracy = srcAtk.m_projectileAccuracy;
                    clonedAtk.m_projectileAccuracyMin = srcAtk.m_projectileAccuracyMin;
                    clonedAtk.m_projectileVel = srcAtk.m_projectileVel;
                    clonedAtk.m_projectileVelMin = srcAtk.m_projectileVelMin;
                    clonedAtk.m_attackEitr = 0f;
                    clonedAtk.m_attackHealth = 0f;
                    clonedAtk.m_attackHealthPercentage = 0f;
                    clonedAtk.m_attackStamina = 0f;
                    // Keep valid Dvergr animation ("attack_icicle")
                    clonedShared.m_attack = clonedAtk;
                    clonedShared.m_damages = srcShared.m_damages;
                    if (srcShared.m_startEffect != null && srcShared.m_startEffect.m_effectPrefabs != null && srcShared.m_startEffect.m_effectPrefabs.Length > 0)
                    {
                        clonedShared.m_startEffect = srcShared.m_startEffect;
                    }
                    if (srcShared.m_triggerEffect != null && srcShared.m_triggerEffect.m_effectPrefabs != null && srcShared.m_triggerEffect.m_effectPrefabs.Length > 0)
                    {
                        clonedShared.m_triggerEffect = srcShared.m_triggerEffect;
                    }
                    clonedShared.m_aiAttackRange = 15f;
                    clonedShared.m_aiAttackInterval = 5f;
                    item.m_shared = clonedShared;
                }
                // 3. Support Mage (DvergerMageSupport) -> Staff of Frost (StaffIceShards) replacing DvergerMistile so Support Mage fires frost shard bursts while keeping Heal & Buff!
                else if (string.Equals(wepPrefabName, "StaffIceShards", StringComparison.OrdinalIgnoreCase) &&
                         dropName.IndexOf("Mistile", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ItemDrop.ItemData.SharedData clonedShared = (ItemDrop.ItemData.SharedData)ShallowCloneSharedData(item.m_shared);
                    Attack clonedAtk = clonedShared.m_attack.Clone();
                    if (srcAtk.m_attackProjectile != null)
                    {
                        clonedAtk.m_attackProjectile = srcAtk.m_attackProjectile;
                    }
                    clonedAtk.m_attackAnimation = "attack_cast";
                    clonedAtk.m_projectiles = 3;
                    clonedAtk.m_burstInterval = 0.18f;
                    clonedAtk.m_projectileAccuracy = srcAtk.m_projectileAccuracy;
                    clonedAtk.m_projectileAccuracyMin = srcAtk.m_projectileAccuracyMin;
                    clonedAtk.m_projectileVel = srcAtk.m_projectileVel;
                    clonedAtk.m_projectileVelMin = srcAtk.m_projectileVelMin;
                    clonedAtk.m_attackEitr = 0f;
                    clonedAtk.m_attackHealth = 0f;
                    clonedAtk.m_attackHealthPercentage = 0f;
                    clonedAtk.m_attackStamina = 0f;
                    clonedShared.m_attack = clonedAtk;
                    clonedShared.m_damages = srcShared.m_damages;
                    if (clonedShared.m_damages.m_frost < 65f)
                    {
                        clonedShared.m_damages.m_frost = 65f;
                    }
                    if (srcShared.m_startEffect != null && srcShared.m_startEffect.m_effectPrefabs != null && srcShared.m_startEffect.m_effectPrefabs.Length > 0)
                    {
                        clonedShared.m_startEffect = srcShared.m_startEffect;
                    }
                    if (srcShared.m_triggerEffect != null && srcShared.m_triggerEffect.m_effectPrefabs != null && srcShared.m_triggerEffect.m_effectPrefabs.Length > 0)
                    {
                        clonedShared.m_triggerEffect = srcShared.m_triggerEffect;
                    }
                    clonedShared.m_aiAttackRange = 16f;
                    clonedShared.m_aiAttackInterval = 4.5f;
                    clonedShared.m_aiAttackMaxAngle = 25f;
                    item.m_shared = clonedShared;
                }
                // 4. 2-Star Ashlands Ranger (DvergerAshlands) -> Wound Ripper (CrossbowRipperBlood)
                else if (string.Equals(wepPrefabName, "CrossbowRipperBlood", StringComparison.OrdinalIgnoreCase) &&
                         dropName.IndexOf("Arbalest_shoot", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ItemDrop.ItemData.SharedData clonedShared = (ItemDrop.ItemData.SharedData)ShallowCloneSharedData(item.m_shared);
                    Attack clonedAtk = clonedShared.m_attack.Clone();
                    clonedAtk.m_attackEitr = 0f;
                    clonedAtk.m_attackHealth = 0f;
                    clonedAtk.m_attackHealthPercentage = 0f;
                    clonedShared.m_attack = clonedAtk;
                    clonedShared.m_damages.m_pierce = Mathf.Max(clonedShared.m_damages.m_pierce, srcShared.m_damages.m_pierce);
                    if (srcShared.m_startEffect != null && srcShared.m_startEffect.m_effectPrefabs != null && srcShared.m_startEffect.m_effectPrefabs.Length > 0)
                    {
                        clonedShared.m_startEffect = srcShared.m_startEffect;
                    }
                    if (srcShared.m_triggerEffect != null && srcShared.m_triggerEffect.m_effectPrefabs != null && srcShared.m_triggerEffect.m_effectPrefabs.Length > 0)
                    {
                        clonedShared.m_triggerEffect = srcShared.m_triggerEffect;
                    }
                    item.m_shared = clonedShared;
                }
            }
        }

        private static object ShallowCloneSharedData(ItemDrop.ItemData.SharedData shared)
        {
            var m = typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return m != null ? m.Invoke(shared, null) : shared;
        }

        internal void ApplyCoolVisuals(bool fromLevelEffects = false)
        {
            if (_character == null)
            {
                return;
            }

            if (_character is Humanoid hum)
            {
                ConfigureAshlandsCombatInventory(hum);
            }

            AshlandsDvergrPlugin.EnsureVisualStarSetups(_character);
            int level = Mathf.Max(1, _character.GetLevel());
            _lastAppliedLevel = level;

            if (!fromLevelEffects)
            {
                LevelEffects le = _character.GetComponentInChildren<LevelEffects>();
                if (le != null)
                {
                    le.SetupLevelVisualization(level);
                }
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

        private static Transform FindBone(Transform root, VisEquipment visEq, params string[] names)
        {
            // Prefer animated skeleton bones on DvergerBody so we never match un-animated dummy Armature bones inside attach_skin(Clone)
            SkinnedMeshRenderer bodySmr = visEq != null && visEq.m_bodyModel != null ? visEq.m_bodyModel : null;
            if (bodySmr == null)
            {
                foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
                {
                    if (smr != null && smr.name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        bodySmr = smr;
                        break;
                    }
                }
            }
            if (bodySmr != null && bodySmr.bones != null)
            {
                foreach (Transform bone in bodySmr.bones)
                {
                    if (bone == null)
                    {
                        continue;
                    }
                    foreach (string n in names)
                    {
                        if (string.Equals(bone.name, n, StringComparison.OrdinalIgnoreCase))
                        {
                            return bone;
                        }
                    }
                }
            }
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
            Character ch = root.GetComponentInParent<Character>() ?? root.GetComponentInChildren<Character>();
            string charName = ch != null ? ch.gameObject.name : root.name;
            bool isMage = AshlandsDvergrPlugin.IsDvergrMageName(charName);
            bool isSupportMage = AshlandsDvergrPlugin.IsDvergrSupportMageName(charName);
            bool useBloodWep = AshlandsDvergrPlugin.UseAshlandsBloodWeapons.Value && (level >= 3 || (isMage && !isSupportMage));
            Transform rightHand = visEq != null && visEq.m_rightHand != null
                ? visEq.m_rightHand
                : FindBone(root, visEq, "r.hand", "RightHand_Attach", "RightHand");
            if (rightHand == null)
            {
                return;
            }

            Transform existingBloodWep = rightHand.Find("AshlandsDvergr_BloodWeapon");
            if (!useBloodWep || isSupportMage)
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
                        child.gameObject.SetActive(true);
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

            // Hide vanilla held weapon meshes (e.g., DvergerArbalest / DvergerStaff) while keeping our blood weapon & hand FX active
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
                child.gameObject.SetActive(false);
            }

            if (existingBloodWep != null)
            {
                existingBloodWep.gameObject.SetActive(true);
                return;
            }

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

            GameObject wepInst = UnityEngine.Object.Instantiate(attach.gameObject, rightHand.position, rightHand.rotation);
            wepInst.name = "AshlandsDvergr_BloodWeapon";
            wepInst.transform.SetParent(rightHand, worldPositionStays: true);
            float s = wepInst.transform.localScale.x;
            if (string.Equals(wepPrefabName, "CrossbowRipperBlood", StringComparison.OrdinalIgnoreCase))
            {
                // Preserve wepInst.transform.localScale (0.01) computed by SetParent(..., worldPositionStays: true) for the 100x Armature bone!
                // Quaternion.Euler(0f, 90f, 0f) maps:
                //   attach +Z (barrel, rear -0.98 -> front +0.88) -> +X_rhand (forward away from torso, matching DvergerArbalest [-0.38, +1.20]),
                //   attach +Y (vertical thickness) -> +Y_rhand (upward),
                //   attach +X (bow limbs, -0.59 -> +0.59) -> -Z_rhand (horizontal across the hands, matching DvergerArbalest [-0.54, +0.49])!
                // Offset (+0.46f * s, +0.06f * s, -0.02f * s) shifts the barrel forward along +X_rhand so the rear stock clears the torso.
                wepInst.transform.localPosition = new Vector3(0.46f * s, 0.06f * s, -0.02f * s);
                wepInst.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                Transform unloadedChild = wepInst.transform.Find("Unloaded");
                Transform loadedChild = wepInst.transform.Find("Loaded");
                if (loadedChild != null)
                {
                    loadedChild.gameObject.SetActive(true);
                    if (unloadedChild != null)
                    {
                        unloadedChild.gameObject.SetActive(false);
                    }
                }
            }
            else if (string.Equals(wepPrefabName, "StaffClusterbomb", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(wepPrefabName, "StaffIceShards", StringComparison.OrdinalIgnoreCase))
            {
                // Player staffs lie along +Z_att in attach space, whereas DvergerMage staffs lie along +X_rhand with Y=+0.12, Z=-0.04.
                // Rotating by Euler(0, 90, 0) maps +Z_att -> +X_rhand so the staff shaft sits inside the mage's right hand grip!
                wepInst.transform.localPosition = new Vector3(0.0f * s, 0.12f * s, -0.04f * s);
                wepInst.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            else if (string.Equals(wepPrefabName, "StaffLightning", StringComparison.OrdinalIgnoreCase))
            {
                // StaffLightning's grip in attach space is shifted toward the butt end (Z=[-0.30, +0.99]);
                // Euler(0, 90, 0) + localPosition (-0.20*s, 0.12*s, -0.04*s) centers the shaft in the mage's hand.
                wepInst.transform.localPosition = new Vector3(-0.20f * s, 0.12f * s, -0.04f * s);
                wepInst.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                Transform unloadedChild = wepInst.transform.Find("Unloaded");
                Transform loadedChild = wepInst.transform.Find("Loaded");
                if (loadedChild != null)
                {
                    loadedChild.gameObject.SetActive(true);
                    if (unloadedChild != null)
                    {
                        unloadedChild.gameObject.SetActive(false);
                    }
                }
            }
            else
            {
                wepInst.transform.localPosition = Vector3.zero;
                wepInst.transform.localRotation = Quaternion.identity;
            }
            wepInst.SetActive(true);
            foreach (Collider col in wepInst.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                col.enabled = false;
            }
            foreach (LODGroup lod in wepInst.GetComponentsInChildren<LODGroup>(includeInactive: true))
            {
                lod.enabled = false;
            }
            Transform equipedChild = wepInst.transform.Find("equiped");
            if (equipedChild != null)
            {
                equipedChild.gameObject.SetActive(true);
            }
            Transform debugCube = wepInst.transform.Find("Cube");
            if (debugCube != null)
            {
                debugCube.gameObject.SetActive(false);
            }
        }

        private static void Ensure2StarBossEffects(Transform root, VisEquipment visEq, int level, bool coolEnabled, float lightMult)
        {
            bool shouldShowBossFx = coolEnabled && level >= 3 && AshlandsDvergrPlugin.Enable2StarBossEffects.Value;

            Transform existingBossRoot = root.Find("AshlandsDvergr_BossFX");
            Transform leftHand = visEq != null && visEq.m_leftHand != null
                ? visEq.m_leftHand
                : FindBone(root, visEq, "l.hand", "LeftHand_Attach", "LeftHand");
            Transform rightHand = visEq != null && visEq.m_rightHand != null
                ? visEq.m_rightHand
                : FindBone(root, visEq, "r.hand", "RightHand_Attach", "RightHand");
            Transform chestBone = FindBone(root, visEq, "spine2", "spine1", "chest", "Spine");

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

            if (existingBossRoot == null)
            {
                GameObject bossRoot = new GameObject("AshlandsDvergr_BossFX");
                bossRoot.transform.SetParent(root, worldPositionStays: false);
                bossRoot.transform.localPosition = Vector3.zero;
                bossRoot.transform.localRotation = Quaternion.identity;
                existingBossRoot = bossRoot.transform;

                // 1. Dvergr Arcane Boss Ring & Sparks (fx_DvergerMage_Nova_ring)
                GameObject novaRingPrefab = AshlandsDvergrPlugin.ResolvePrefabByNameOrAssetId("fx_DvergerMage_Nova_ring");
                if (novaRingPrefab != null)
                {
                    bool prevDisableInit = ZNetView.m_forceDisableInit;
                    ZNetView.m_forceDisableInit = true;
                    GameObject ringInst;
                    try
                    {
                        ringInst = UnityEngine.Object.Instantiate(novaRingPrefab, existingBossRoot, false);
                    }
                    finally
                    {
                        ZNetView.m_forceDisableInit = prevDisableInit;
                    }
                    ringInst.name = "BossArcaneRing";
                    ringInst.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                    ringInst.transform.localRotation = Quaternion.identity;
                    ringInst.transform.localScale = Vector3.one * 0.25f;
                    ConfigureBossParticlePrefab(ringInst, new Color(0.85f, 0.22f, 1.0f, 0.55f), sizeMult: 0.35f, disableBrightFlareChildren: true);
                }
            }
            else
            {
                existingBossRoot.gameObject.SetActive(true);
            }

            GameObject bloodFirePrefab = AshlandsDvergrPlugin.ResolvePrefabByNameOrAssetId("vfx_crossbow_blood_fire");
            if (bloodFirePrefab != null)
            {
                // 2. Boss Chest Blood-Arcane Fire (vfx_crossbow_blood_fire)
                if (chestBone != null && existingChestFx == null)
                {
                    bool prevDisableInit = ZNetView.m_forceDisableInit;
                    ZNetView.m_forceDisableInit = true;
                    GameObject chestInst;
                    try
                    {
                        chestInst = UnityEngine.Object.Instantiate(bloodFirePrefab, chestBone.position, chestBone.rotation);
                    }
                    finally
                    {
                        ZNetView.m_forceDisableInit = prevDisableInit;
                    }
                    chestInst.name = "AshlandsDvergr_BossChestFX";
                    chestInst.transform.SetParent(chestBone, worldPositionStays: true);
                    chestInst.transform.localPosition = Vector3.zero;
                    chestInst.transform.localRotation = Quaternion.identity;
                    chestInst.transform.localScale = Vector3.one * 0.0052f;
                    ConfigureBossParticlePrefab(chestInst, new Color(0.95f, 0.18f, 0.72f, 0.85f), sizeMult: 0.55f, disableBrightFlareChildren: true);
                    existingChestFx = chestInst.transform;
                }
                else if (existingChestFx != null)
                {
                    existingChestFx.gameObject.SetActive(true);
                }

                // 3. Boss Hand Blood-Arcane Fire on Left & Right Hands (vfx_crossbow_blood_fire)
                if (leftHand != null && existingLeftHandFx == null)
                {
                    bool prevDisableInit = ZNetView.m_forceDisableInit;
                    ZNetView.m_forceDisableInit = true;
                    GameObject lhInst;
                    try
                    {
                        lhInst = UnityEngine.Object.Instantiate(bloodFirePrefab, leftHand.position, leftHand.rotation);
                    }
                    finally
                    {
                        ZNetView.m_forceDisableInit = prevDisableInit;
                    }
                    lhInst.name = "AshlandsDvergr_BossHandFX";
                    lhInst.transform.SetParent(leftHand, worldPositionStays: true);
                    lhInst.transform.localPosition = Vector3.zero;
                    lhInst.transform.localRotation = Quaternion.identity;
                    lhInst.transform.localScale = Vector3.one * 0.0042f;
                    ConfigureBossParticlePrefab(lhInst, new Color(0.85f, 0.22f, 1.0f, 0.88f), sizeMult: 0.45f, disableBrightFlareChildren: true);
                    existingLeftHandFx = lhInst.transform;
                }
                else if (existingLeftHandFx != null)
                {
                    existingLeftHandFx.gameObject.SetActive(true);
                }

                if (rightHand != null && existingRightHandFx == null)
                {
                    bool prevDisableInit = ZNetView.m_forceDisableInit;
                    ZNetView.m_forceDisableInit = true;
                    GameObject rhInst;
                    try
                    {
                        rhInst = UnityEngine.Object.Instantiate(bloodFirePrefab, rightHand.position, rightHand.rotation);
                    }
                    finally
                    {
                        ZNetView.m_forceDisableInit = prevDisableInit;
                    }
                    rhInst.name = "AshlandsDvergr_BossHandFX";
                    rhInst.transform.SetParent(rightHand, worldPositionStays: true);
                    rhInst.transform.localPosition = Vector3.zero;
                    rhInst.transform.localRotation = Quaternion.identity;
                    rhInst.transform.localScale = Vector3.one * 0.0042f;
                    ConfigureBossParticlePrefab(rhInst, new Color(1.0f, 0.18f, 0.58f, 0.88f), sizeMult: 0.45f, disableBrightFlareChildren: true);
                    existingRightHandFx = rhInst.transform;
                }
                else if (existingRightHandFx != null)
                {
                    existingRightHandFx.gameObject.SetActive(true);
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
                UnityEngine.Object.DestroyImmediate(znv);
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

                // Stop and clear the premature burst that fired on Awake before SetParent/localScale/sizeMult were applied
                ps.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmittingAndClear);

                main.loop = true;
                main.playOnAwake = true;
                main.duration = 0.85f;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.startLifetimeMultiplier = Mathf.Min(main.startLifetimeMultiplier, 0.85f);
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
                    burst.time = 0f;
                    burst.count = new ParticleSystem.MinMaxCurve(Mathf.Min(6f, Mathf.Max(2f, burst.count.constant * 0.35f)));
                    burst.cycleCount = 0; // 0 = infinite burst repeats in Unity
                    burst.repeatInterval = 0.85f;
                    burst.probability = 1f;
                    emission.SetBurst(b, burst);
                }

                ps.Clear(withChildren: false);
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
            Ensure2StarBossEffects(root, visEq, level, coolEnabled, lightMult);

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

            // Weapon tint: preserve authentic bloodstone, iron, wood, and ice diffuse colors without pink/purple tinting
            Color weaponColorMult = Color.white;

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
                    string shaderName = m.shader != null ? (m.shader.name ?? string.Empty) : string.Empty;
                    bool isUnlitOrParticleShader = shaderName.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   shaderName.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool hasEmissionMap = m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") != null;
                    bool isBodyMat = m.name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isWeaponMat = !isBodyMat && IsWeaponRenderer(r, m, visEq);
                    if (isWeaponMat)
                    {
                        isWeaponRenderer = true;
                    }
                    if (isUnlitOrParticleShader)
                    {
                        continue;
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
                            // Preserve native weapon emission (Custom/Creature weapons have no _EmissionMap, so forcing _EmissionColor washes them out bright pink in-game)
                            m.SetColor("_EmissionColor", origEmission);
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
                r.SetPropertyBlock(null);
                r.materials = mats;

                if (isWeaponRenderer)
                {
                    Transform existingWepLight = r.transform.Find("AshlandsDvergr_WeaponGlow");
                    if (existingWepLight != null)
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
