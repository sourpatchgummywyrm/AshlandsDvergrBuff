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
        public const string PluginVersion = "2.0.8";

        internal static AshlandsDvergrPlugin Instance;
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
        internal static ConfigEntry<bool> PreventUnprovokedLightningFriendlyFire;
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

        internal static ConfigEntry<bool> EnableDeepNorthBuff;
        internal static ConfigEntry<float> DeepNorthHealthMultiplier;
        internal static ConfigEntry<float> DeepNorthDamageMultiplier;
        internal static ConfigEntry<float> DeepNorthMageSpawnChance;

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
        private static AnimationClip _cachedPlayerFrostShieldClip;
        private static readonly System.Random VarietyRng = new System.Random();

        private static readonly List<string> VarietyDeck = new List<string>(10);
        private static int VarietyDeckIndex;
        private static float LastDeckVarietyChance = -1f;

        private static readonly List<string> DeepNorthDeck = new List<string>(10);
        private static int DeepNorthDeckIndex;
        private static float LastDeepNorthMageChance = -1f;

        internal static bool InsideSpawnerSpawn;
        internal static bool InsideDeepNorthSpawn;
        internal static bool InsideConsoleSpawn;
        internal static bool InsideConsoleVarietySplit;
        internal static int ConsoleExplicitLevel;
        internal static int PendingVarietySpawnLevel;
        internal static bool VarietyReplacedInCurrentSpawn;

        private static FileSystemWatcher _configFileWatcher;
        private static DateTime _lastConfigReloadTime = DateTime.MinValue;
        private static bool _isApplyingRemoteSync;
        private static bool _wasRemoteClient;
        private static readonly Dictionary<string, string> LastSyncedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            int order = 100;

            // 1. Spawning - Multipliers & Ruin Spawners
            SpawnRateMultiplier = Config.Bind("1. Spawning", "SpawnRateMultiplier", 2.0f,
                new ConfigDescription(
                    "Global multiplier for Ashlands Dvergr spawn rate, spawn chance, group sizes, and spawner weights.\n" +
                    "Default: 2.0 (2x faster spawns, 2x capacity).\n" +
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
            HealthMultiplier = Config.Bind("3. Combat Stats", "HealthMultiplier", 1.30f,
                new ConfigDescription(
                    "Base health multiplier for Dvergrs in the Ashlands (default: 1.30 = +30% base HP: 1000 -> 1300).\n" +
                    "Star levels add +25% HP per star (0-star: 1300 HP, 1-star: 1625 HP, 2-star: 1950 HP).\n" +
                    "Range: 0.5 to 3.0",
                    new AcceptableValueRange<float>(0.5f, 3.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            DamageMultiplier = Config.Bind("3. Combat Stats", "DamageMultiplier", 1.20f,
                new ConfigDescription(
                    "Outgoing damage multiplier for Ashlands Dvergrs when UseAshlandsBloodWeapons is false (melee strikes, arbalest bolts, and magic AOEs).\n" +
                    "Default: 1.20 (+20% base damage, +25% at 1-star; 2-star skips default star damage scaling due to weapon buff).\n" +
                    "Range: 0.5 to 3.0",
                    new AcceptableValueRange<float>(0.5f, 3.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            MageHealthMultiplier = Config.Bind("3. Combat Stats", "MageHealthMultiplier", 1.15f,
                new ConfigDescription(
                    "Separate base health multiplier for Dvergr Mages in the Ashlands (Fire, Ice, Support).\n" +
                    "Set lower than warriors/crossbowmen by default to balance glass-cannon magic power (default: 1.15 = +15% base HP: 650 -> 747.5).\n" +
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

            PreventUnprovokedLightningFriendlyFire = Config.Bind("3. Combat Stats", "PreventUnprovokedLightningFriendlyFire", true,
                new ConfigDescription(
                    "Prevent Dvergrs from taking player lightning damage (e.g. Storm weapon chain lightning) unless they have already been attacked directly by a player (aggravated).\n" +
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

            // 5. Deep North
            EnableDeepNorthBuff = Config.Bind("5. Deep North", "EnableDeepNorthBuff", true,
                new ConfigDescription(
                    "Slightly buff Deep North Dvergrs and split spawns 50/50 between Nord Crossbowmen and Protection + Northern Vengeance Mages (no healing).\n" +
                    "Default: true",
                    null,
                    new ConfigurationManagerAttributes { Order = --order }));

            DeepNorthHealthMultiplier = Config.Bind("5. Deep North", "DeepNorthHealthMultiplier", 1.15f,
                new ConfigDescription(
                    "Base health multiplier for Deep North Dvergrs (default: 1.15 = +15% base HP: 1500 -> 1725 for Nord Crossbowman, 1100 -> 1265 for Deep North Mage).\n" +
                    "Range: 0.5 to 3.0",
                    new AcceptableValueRange<float>(0.5f, 3.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            DeepNorthDamageMultiplier = Config.Bind("5. Deep North", "DeepNorthDamageMultiplier", 1.10f,
                new ConfigDescription(
                    "Outgoing damage multiplier for Deep North Dvergrs.\n" +
                    "Default: 1.10 (+10% damage).\n" +
                    "Range: 0.5 to 3.0",
                    new AcceptableValueRange<float>(0.5f, 3.0f),
                    new ConfigurationManagerAttributes { Order = --order }));

            DeepNorthMageSpawnChance = Config.Bind("5. Deep North", "DeepNorthMageSpawnChance", 50f,
                new ConfigDescription(
                    "Percentage chance (0-100) for Deep North Dvergr spawns to be a Staff of Protection + Northern Vengeance Mage (no healing) instead of a Nord Crossbowman.\n" +
                    "Default: 50% (50/50 split).\n" +
                    "Range: 0.0 to 100.0",
                    new AcceptableValueRange<float>(0.0f, 100.0f),
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
            else if (Environment.GetEnvironmentVariable("LIVE_WORLD_TEST") == "1")
            {
                StartCoroutine(LiveDeepNorthWorldTestCoroutine());
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
            if (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                return;
            }
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
            if (!_isApplyingRemoteSync && ZNet.instance != null && ZNet.instance.IsServer())
            {
                BroadcastConfigToPeers();
            }
        }

        private static void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            if (_isApplyingRemoteSync)
            {
                RefreshPrefabStats();
                return;
            }

            if (ZNet.instance != null)
            {
                if (ZNet.instance.IsServer())
                {
                    RefreshPrefabStats();
                    BroadcastConfigToPeers();
                    return;
                }

                if (e?.ChangedSetting != null)
                {
                    string key = e.ChangedSetting.Definition.Key;
                    if (IsLocalAdminOrHost())
                    {
                        string val = e.ChangedSetting.GetSerializedValue();
                        ZRoutedRpc.instance?.InvokeRoutedRPC("AshlandsDvergrMod_SetConfig", key, val);
                    }
                    else if (LastSyncedValues.TryGetValue(key, out string syncedVal))
                    {
                        _isApplyingRemoteSync = true;
                        try
                        {
                            e.ChangedSetting.SetSerializedValue(syncedVal);
                        }
                        finally
                        {
                            _isApplyingRemoteSync = false;
                        }
                    }
                }
            }
            RefreshPrefabStats();
        }

        internal static bool IsLocalAdminOrHost()
        {
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                return true;
            }
            try
            {
                return ZNet.instance.LocalPlayerIsAdminOrHost();
            }
            catch
            {
                return false;
            }
        }

        private static void UpdateConfigReadOnlyState()
        {
            if (Instance == null)
            {
                return;
            }
            bool readOnly = !IsLocalAdminOrHost();
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in Instance.Config)
            {
                if (kv.Value?.Description?.Tags == null)
                {
                    continue;
                }
                foreach (object tag in kv.Value.Description.Tags)
                {
                    if (tag is ConfigurationManagerAttributes attrs)
                    {
                        attrs.ReadOnly = readOnly;
                    }
                }
            }
        }

        private static ZPackage BuildConfigPackage()
        {
            ZPackage pkg = new ZPackage();
            if (Instance == null)
            {
                pkg.Write(0);
                return pkg;
            }
            pkg.Write(Instance.Config.Count);
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in Instance.Config)
            {
                pkg.Write(kv.Key.Key);
                pkg.Write(kv.Value.GetSerializedValue());
            }
            return pkg;
        }

        private static void BroadcastConfigToPeers(long targetPeerId = 0L)
        {
            if (ZRoutedRpc.instance == null || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }
            ZPackage pkg = BuildConfigPackage();
            if (targetPeerId == 0L)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "AshlandsDvergrMod_SyncConfig", pkg);
            }
            else
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(targetPeerId, "AshlandsDvergrMod_SyncConfig", pkg);
            }
        }

        private static void RPC_AshlandsDvergrSyncConfig(long sender, ZPackage pkg)
        {
            if (Instance == null || pkg == null || (ZNet.instance != null && ZNet.instance.IsServer()))
            {
                return;
            }
            int count = pkg.ReadInt();
            bool prevSave = Instance.Config.SaveOnConfigSet;
            Instance.Config.SaveOnConfigSet = false;
            _isApplyingRemoteSync = true;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    string key = pkg.ReadString();
                    string val = pkg.ReadString();
                    LastSyncedValues[key] = val;
                    ConfigEntryBase entry = FindConfigEntryByKey(key);
                    entry?.SetSerializedValue(val);
                }
            }
            finally
            {
                _isApplyingRemoteSync = false;
                Instance.Config.SaveOnConfigSet = prevSave;
            }
            UpdateConfigReadOnlyState();
            RefreshPrefabStats();
            Log.LogInfo($"Synced {count} Ashlands Dvergr settings from world host/server.");
        }

        private static void RPC_AshlandsDvergrRequestSync(long sender)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                BroadcastConfigToPeers(sender);
            }
        }

        private static void RPC_AshlandsDvergrSetConfig(long sender, string key, string serializedValue)
        {
            if (Instance == null || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }
            bool isHost = sender == ZNet.GetUID();
            bool isAdmin = false;
            if (!isHost)
            {
                ZNetPeer peer = ZNet.instance.GetPeer(sender);
                string hostName = peer?.m_socket?.GetHostName();
                isAdmin = !string.IsNullOrEmpty(hostName) && ZNet.instance.IsAdmin(hostName);
            }
            if (!isHost && !isAdmin)
            {
                Log.LogWarning($"Rejected AshlandsDvergrMod_SetConfig('{key}') from non-admin peer {sender}.");
                return;
            }

            ConfigEntryBase entry = FindConfigEntryByKey(key);
            if (entry == null)
            {
                return;
            }
            entry.SetSerializedValue(serializedValue);
            Instance.Config.Save();
            RefreshPrefabStats();
            BroadcastConfigToPeers();
            Log.LogInfo($"Admin peer {sender} updated '{entry.Definition.Key}' = '{entry.GetSerializedValue()}'.");
        }

        internal static ConfigEntryBase FindConfigEntryByKey(string key)
        {
            if (Instance == null || string.IsNullOrEmpty(key))
            {
                return null;
            }
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in Instance.Config)
            {
                if (string.Equals(kv.Key.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Value;
                }
            }
            return null;
        }

        internal static bool TryParseAndApplyConfigValue(ConfigEntryBase entry, string rawValue, out string error)
        {
            error = null;
            if (entry == null)
            {
                error = "Unknown config key.";
                return false;
            }
            try
            {
                object parsed;
                Type t = entry.SettingType;
                if (t == typeof(bool))
                {
                    if (string.Equals(rawValue, "1") || string.Equals(rawValue, "on", StringComparison.OrdinalIgnoreCase))
                        parsed = true;
                    else if (string.Equals(rawValue, "0") || string.Equals(rawValue, "off", StringComparison.OrdinalIgnoreCase))
                        parsed = false;
                    else
                        parsed = bool.Parse(rawValue);
                }
                else if (t == typeof(int))
                {
                    parsed = int.Parse(rawValue, System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (t == typeof(float))
                {
                    parsed = float.Parse(rawValue, System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    parsed = TomlTypeConverter.ConvertToValue(rawValue, t);
                }

                if (entry.Description?.AcceptableValues != null)
                {
                    parsed = entry.Description.AcceptableValues.Clamp(parsed);
                }

                if (ZNet.instance == null || ZNet.instance.IsServer())
                {
                    entry.BoxedValue = parsed;
                    Instance.Config.Save();
                }
                else
                {
                    _isApplyingRemoteSync = true;
                    try
                    {
                        entry.BoxedValue = parsed;
                    }
                    finally
                    {
                        _isApplyingRemoteSync = false;
                    }
                    ZRoutedRpc.instance?.InvokeRoutedRPC("AshlandsDvergrMod_SetConfig", entry.Definition.Key, entry.GetSerializedValue());
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"Invalid value '{rawValue}' for {entry.Definition.Key} ({entry.SettingType.Name}): {ex.Message}";
                return false;
            }
        }

        internal static string FormatWorldStatsSummary()
        {
            string role = ZNet.instance == null ? "Local" : (ZNet.instance.IsServer() ? "Host/Server" : (IsLocalAdminOrHost() ? "Admin" : "Player (Read-Only)"));
            float rangerBase = 1000f * Mathf.Max(0.1f, HealthMultiplier.Value);
            float mageBase = 650f * Mathf.Max(0.1f, MageHealthMultiplier.Value);
            float dnRangerBase = 1500f * Mathf.Max(0.1f, DeepNorthHealthMultiplier.Value);
            float dnMageBase = 1100f * Mathf.Max(0.1f, DeepNorthHealthMultiplier.Value);
            float dnDmg0 = Mathf.Max(0.1f, DeepNorthDamageMultiplier.Value);
            float dnDmg1 = dnDmg0 * 1.25f;
            string dn2StarSuffix = UseAshlandsBloodWeapons.Value ? " (0-1★=NordCrossbow, 2★=ThunderbloodCrossbow+Blood)" : "";
            List<string> enabledMages = GetEnabledVarietyMagePrefabNames();
            string magesLabel = enabledMages.Count == 0 ? "None" : string.Join(", ", enabledMages);

            return
                $"=== Ashlands Dvergr Boost ({PluginVersion}) [{role}] ===\n" +
                $"Ranger (DvergerAshlands): HP={rangerBase:F0} / {rangerBase * 1.25f:F0} / {rangerBase * 1.50f:F0} (0/1/2★), Dmg={DamageMultiplier.Value:F2}x / {DamageMultiplier.Value * 1.25f:F2}x / Q2-Base (0/1/2★), BloodWeapons={UseAshlandsBloodWeapons.Value}\n" +
                $"Mage (650 base): HP={mageBase:F0} / {mageBase * 1.25f:F0} / {mageBase * 1.50f:F0} (0/1/2★), Dmg={MageDamageMultiplier.Value:F2}x / {MageDamageMultiplier.Value * 1.25f:F2}x / Q2-Base (0/1/2★), Enabled=[{magesLabel}]\n" +
                $"Deep North Ranger (Enabled={EnableDeepNorthBuff.Value}): HP={dnRangerBase:F0} / {dnRangerBase * 1.25f:F0} / {dnRangerBase * 1.50f:F0} (0/1/2★), Dmg={dnDmg0:F2}x / {dnDmg1:F2}x / Q2-Base{dn2StarSuffix}\n" +
                $"Deep North Mage (no heal): HP={dnMageBase:F0} / {dnMageBase * 1.25f:F0} / {dnMageBase * 1.50f:F0} (0/1/2★), Dmg={dnDmg0:F2}x / {dnDmg1:F2}x / Q2-Base, 0★=Prot, 1★=Prot+Vengeance, 2★=ThunderbloodOnly, Split={DeepNorthMageSpawnChance.Value:F0}%\n" +
                $"Spawning: Rate={SpawnRateMultiplier.Value:F2}x, Respawn={CampRespawnIntervalMinutes.Value:F1}m, MageChance={VarietyDvergrSpawnChance.Value:F0}%, Ambient={EnableAmbientAshlandsSpawns.Value} ({AmbientGroupMin.Value}-{AmbientGroupMax.Value}, max {AmbientMaxSpawned.Value})\n" +
                $"Combat/Stars: Levels={MinLevel.Value}-{MaxLevel.Value} ({LevelUpChance.Value:F0}% up), Heal={PassiveHealPercentPerTick.Value:F1}%/{PassiveHealIntervalSeconds.Value:F1}s, NoUnprovokedLightningFF={PreventUnprovokedLightningFriendlyFire.Value}\n" +
                $"Visuals: Scale1★={VisualScale1Star.Value:F2}x, Scale2★={VisualScale2Star.Value:F2}x, CoolVisuals={EnableCoolVisuals.Value}, 2★BossFX={Enable2StarBossEffects.Value}, Aura={AuraLightIntensity.Value:F2}\n" +
                $"Tip: Use 'dvergrconfig' (or '/dvergrconfig' in chat) to list all keys; admins/hosts can run 'dvergrconfig <key> <value>'.";
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        [HarmonyPostfix]
        private static void ZNet_Start_Postfix(ZNet __instance)
        {
            if (__instance == null)
            {
                return;
            }
            _wasRemoteClient = !__instance.IsServer();
            LastSyncedValues.Clear();
            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.Register<ZPackage>("AshlandsDvergrMod_SyncConfig", RPC_AshlandsDvergrSyncConfig);
                ZRoutedRpc.instance.Register("AshlandsDvergrMod_RequestSync", RPC_AshlandsDvergrRequestSync);
                ZRoutedRpc.instance.Register<string, string>("AshlandsDvergrMod_SetConfig", RPC_AshlandsDvergrSetConfig);
                if (!__instance.IsServer())
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC("AshlandsDvergrMod_RequestSync");
                }
            }
            UpdateConfigReadOnlyState();
        }

        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        [HarmonyPostfix]
        private static void ZNet_RPC_PeerInfo_Postfix(ZNet __instance, ZRpc rpc)
        {
            if (__instance == null || !__instance.IsServer() || rpc == null)
            {
                return;
            }
            var getPeer = AccessTools.Method(typeof(ZNet), "GetPeer", new[] { typeof(ZRpc) });
            if (getPeer?.Invoke(__instance, new object[] { rpc }) is ZNetPeer peer && peer.m_uid != 0L)
            {
                BroadcastConfigToPeers(peer.m_uid);
            }
        }

        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        [HarmonyPostfix]
        private static void ZNet_OnDestroy_Postfix()
        {
            if (_wasRemoteClient && Instance != null)
            {
                _wasRemoteClient = false;
                LastSyncedValues.Clear();
                _isApplyingRemoteSync = true;
                try
                {
                    Instance.Config.Reload();
                }
                finally
                {
                    _isApplyingRemoteSync = false;
                }
            }
            UpdateConfigReadOnlyState();
        }

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        [HarmonyPostfix]
        private static void Terminal_InitTerminal_Postfix()
        {
            new Terminal.ConsoleCommand(
                "dvergrstats",
                "Show active Ashlands Dvergr Boost stats and settings for the current world.",
                args =>
                {
                    foreach (string line in FormatWorldStatsSummary().Split('\n'))
                    {
                        args.Context.AddString(line);
                    }
                },
                isCheat: false,
                isNetwork: false,
                onlyServer: false);

            new Terminal.ConsoleCommand(
                "dvergrconfig",
                "View Ashlands Dvergr Boost config (all players) or set values (admins/hosts): dvergrconfig [list|<key> [<value>]|reset]",
                args =>
                {
                    if (Instance == null)
                    {
                        return;
                    }
                    if (args.Length <= 1 || string.Equals(args[1], "list", StringComparison.OrdinalIgnoreCase))
                    {
                        string role = IsLocalAdminOrHost() ? "Admin/Host - editable" : "Read-Only";
                        args.Context.AddString($"=== Ashlands Dvergr Boost Config ({role}) ===");
                        foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in Instance.Config)
                        {
                            string rangeStr = kv.Value.Description?.AcceptableValues != null ? $" ({kv.Value.Description.AcceptableValues.ToDescriptionString()})" : string.Empty;
                            args.Context.AddString($"  {kv.Key.Key} = {kv.Value.GetSerializedValue()}{rangeStr}");
                        }
                        return;
                    }

                    if (string.Equals(args[1], "reset", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!IsLocalAdminOrHost())
                        {
                            args.Context.AddString("Only server hosts or admins can reset Ashlands Dvergr settings in-game.");
                            return;
                        }
                        foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in Instance.Config)
                        {
                            TryParseAndApplyConfigValue(kv.Value, TomlTypeConverter.ConvertToString(kv.Value.DefaultValue, kv.Value.SettingType), out _);
                        }
                        args.Context.AddString("Reset all Ashlands Dvergr settings to defaults and synced to world.");
                        return;
                    }

                    ConfigEntryBase target = FindConfigEntryByKey(args[1]);
                    if (target == null)
                    {
                        args.Context.AddString($"Unknown config key '{args[1]}'. Run 'dvergrconfig list' to see all keys.");
                        return;
                    }

                    if (args.Length == 2)
                    {
                        string rangeStr = target.Description?.AcceptableValues != null ? $" [{target.Description.AcceptableValues.ToDescriptionString()}]" : string.Empty;
                        args.Context.AddString($"{target.Definition.Key} = {target.GetSerializedValue()} (default: {target.DefaultValue}){rangeStr}");
                        if (!string.IsNullOrEmpty(target.Description?.Description))
                        {
                            args.Context.AddString(target.Description.Description.Replace("\n", " "));
                        }
                        return;
                    }

                    if (!IsLocalAdminOrHost())
                    {
                        args.Context.AddString("Only server hosts or admins can modify Ashlands Dvergr settings in-game. Use 'dvergrstats' or 'dvergrconfig' to view world settings.");
                        return;
                    }

                    if (TryParseAndApplyConfigValue(target, args[2], out string err))
                    {
                        args.Context.AddString($"Updated {target.Definition.Key} = {target.GetSerializedValue()} (synced to world).");
                    }
                    else
                    {
                        args.Context.AddString(err);
                    }
                },
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                optionsFetcher: () =>
                {
                    List<string> opts = new List<string> { "list", "reset" };
                    if (Instance != null)
                    {
                        foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in Instance.Config)
                        {
                            opts.Add(kv.Key.Key);
                        }
                    }
                    return opts;
                });
        }

        private static void RefreshPrefabStats()
        {
            if (ZNetScene.instance == null)
            {
                return;
            }

            string[] dvergrPrefabsToRefresh = { "DvergerAshlands", "DvergerDeepNorth", "DvergerMageFire", "DvergerMageIce", "DvergerMageSupport", "Dverger" };
            foreach (string pName in dvergrPrefabsToRefresh)
            {
                GameObject go = ZNetScene.instance.GetPrefab(pName);
                if (go != null)
                {
                    Character c = go.GetComponent<Character>();
                    if (c != null && OriginalPrefabHealth.TryGetValue(go.GetInstanceID(), out float baseHp))
                    {
                        bool isDeepNorth = string.Equals(pName, "DvergerDeepNorth", StringComparison.OrdinalIgnoreCase);
                        bool isMage = IsDvergrMage(c);
                        float standardHp = isDeepNorth ? baseHp : (isMage ? Mathf.Max(baseHp, 650f) : baseHp);
                        float hpMult = isDeepNorth
                            ? (EnableDeepNorthBuff.Value ? Mathf.Max(0.1f, DeepNorthHealthMultiplier.Value) : 1f)
                            : GetHealthMultiplierFor(c);
                        c.m_health = standardHp * hpMult;
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

        internal static bool IsInDeepNorth(Vector3 pos)
        {
            if (WorldGenerator.instance != null && (WorldGenerator.instance.GetBiome(pos) & Heightmap.Biome.DeepNorth) != 0)
            {
                return true;
            }
            return false;
        }

        internal static bool IsDeepNorthDvergr(Character ch)
        {
            if (ch == null || ch.IsPlayer() || EnableDeepNorthBuff == null || !EnableDeepNorthBuff.Value)
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
            if (ch.gameObject.name.IndexOf("DeepNorth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                InsideDeepNorthSpawn)
            {
                return true;
            }
            AshlandsDvergrEnhancer enhancer = ch.GetComponent<AshlandsDvergrEnhancer>();
            if (enhancer != null && enhancer.IsDeepNorth)
            {
                return true;
            }
            ZNetView nview = ch.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                ZDO zdo = nview.GetZDO();
                if (zdo != null && zdo.GetBool("AshlandsDvergrMod_IsDeepNorth"))
                {
                    return true;
                }
            }
            return IsInDeepNorth(ch.transform.position);
        }

        internal static bool IsAshlandsDvergr(Character ch)
        {
            if (ch == null || ch.IsPlayer() || IsDeepNorthDvergr(ch))
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

        internal static bool IsModdedDvergr(Character ch)
        {
            return IsAshlandsDvergr(ch) || IsDeepNorthDvergr(ch);
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
            if (IsDeepNorthDvergr(ch))
            {
                return Mathf.Max(0.1f, DeepNorthHealthMultiplier.Value);
            }
            return IsDvergrMage(ch) ? Mathf.Max(0.1f, MageHealthMultiplier.Value) : Mathf.Max(0.1f, HealthMultiplier.Value);
        }

        internal static float GetDamageMultiplierFor(Character ch)
        {
            if (IsDeepNorthDvergr(ch))
            {
                return Mathf.Max(0.1f, DeepNorthDamageMultiplier.Value);
            }
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

        internal static string RollDeepNorthPrefabForSpawn()
        {
            if (EnableDeepNorthBuff == null || !EnableDeepNorthBuff.Value)
            {
                return "DvergerDeepNorth";
            }
            float mageChance = Mathf.Clamp(DeepNorthMageSpawnChance.Value, 0f, 100f);
            if (mageChance <= 0f)
            {
                return "DvergerDeepNorth";
            }
            if (mageChance >= 100f)
            {
                return "DvergerMageSupport";
            }
            if (DeepNorthDeck.Count == 0 || DeepNorthDeckIndex >= DeepNorthDeck.Count || Mathf.Abs(mageChance - LastDeepNorthMageChance) > 0.5f)
            {
                DeepNorthDeck.Clear();
                DeepNorthDeckIndex = 0;
                LastDeepNorthMageChance = mageChance;
                int mageSlots = Mathf.Clamp(Mathf.RoundToInt(mageChance / 10f), 0, 10);
                for (int i = 0; i < mageSlots; i++)
                {
                    DeepNorthDeck.Add("DvergerMageSupport");
                }
                for (int i = mageSlots; i < 10; i++)
                {
                    DeepNorthDeck.Add("DvergerDeepNorth");
                }
                for (int i = DeepNorthDeck.Count - 1; i > 0; i--)
                {
                    int j = VarietyRng.Next(i + 1);
                    string tmp = DeepNorthDeck[i];
                    DeepNorthDeck[i] = DeepNorthDeck[j];
                    DeepNorthDeck[j] = tmp;
                }
            }
            return DeepNorthDeck[DeepNorthDeckIndex++];
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
                    "Player" => "3cc4f440c0f1043fc9f35cc14d2699cd",
                    "DvergerAshlands" => "2ece1cf15f2885a4da6da84af9d5c542",
                    "DvergerDeepNorth" => "8b80dafa62497e64bbe7ffe09a7ad6f2",
                    "DvergerMage" => "8c3b1095f7874dc469e0e324a6fb82d7",
                    "DvergerMageFire" => "1ed38d388ceda5f4e89f5be4d1768fab",
                    "DvergerMageIce" => "8ad104a536dd94d4bb764f8a9f3d8ee7",
                    "DvergerMageSupport" => "c0e85e3d180619047bdf97cd98271a4d",
                    "Dverger" => "b780f1078c33d2344836ccefd3bf9352",
                    "CrossbowRipperBlood" => "483a950c53776e74ea40aef310fefbb2",
                    "CrossbowGold" => "3c95d939c9bce9f418ef8fcf7d549e05",
                    "CrossbowGold_BloodLightning" => "627967be231e19649bb184c9f3f93b45",
                    "StaffClusterbomb" => "d4812897e4412e540b7fa649d25b99d5",
                    "StaffLightning" => "90f15fa074aa08a478750566117d84d0",
                    "StaffRedTroll" => "87eebe8749379744eacfa37ea818aeab",
                    "StaffIceShards" => "d268d47613aec2963b1878ed2a224e1e",
                    "StaffShield" => "2ac6892f975d1667f96a24586cd6b976",
                    "StaffFrostOrbs" => "e40884be0d0e7a444b6766378a8186b1",
                    "StaffThunderBlood" => "42ab7d31d40bf9548908b5f2aa9f887a",
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

            string[] dvergrPrefabs = { "DvergerAshlands", "DvergerDeepNorth", "DvergerMageFire", "DvergerMageIce", "DvergerMageSupport", "Dverger" };
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
                        bool isDeepNorth = string.Equals(pName, "DvergerDeepNorth", StringComparison.OrdinalIgnoreCase);
                        bool isMage = IsDvergrMage(ch);
                        float standardHp = isDeepNorth ? baseHp : (isMage ? Mathf.Max(baseHp, 650f) : baseHp);
                        float hpMult = isDeepNorth
                            ? (EnableDeepNorthBuff.Value ? Mathf.Max(0.1f, DeepNorthHealthMultiplier.Value) : 1f)
                            : GetHealthMultiplierFor(ch);
                        ch.m_health = standardHp * hpMult;
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
            bool isDeepNorth = IsDeepNorthDvergr(ch);
            bool isMage = IsDvergrMage(ch);
            float baseStandard = isDeepNorth ? (isMage ? 1100f : 1500f) : (isMage ? 650f : 1000f);
            float hpMult = GetHealthMultiplierFor(ch);
            float targetHp = baseStandard * hpMult;
            if (isDeepNorth || ch.m_health < targetHp)
            {
                ch.m_health = targetHp;
            }
        }

        internal static AnimationClip GetPlayerFrostShieldClip()
        {
            if (_cachedPlayerFrostShieldClip != null)
            {
                return _cachedPlayerFrostShieldClip;
            }
            GameObject playerPrefab = ResolvePrefabByNameOrAssetId("Player");
            Animator playerAnim = playerPrefab != null ? playerPrefab.GetComponentInChildren<Animator>(includeInactive: true) : null;
            if (playerAnim?.runtimeAnimatorController != null)
            {
                foreach (AnimationClip clip in playerAnim.runtimeAnimatorController.animationClips)
                {
                    if (clip != null && string.Equals(clip.name, "FrostShield", StringComparison.OrdinalIgnoreCase))
                    {
                        _cachedPlayerFrostShieldClip = clip;
                        break;
                    }
                }
            }
            return _cachedPlayerFrostShieldClip;
        }

        internal static void EnsureDeepNorthMageAnimatorOverride(Character ch)
        {
            if (ch == null || !IsDeepNorthDvergr(ch) || !IsDvergrMage(ch))
            {
                return;
            }
            AnimationClip frostShieldClip = GetPlayerFrostShieldClip();
            if (frostShieldClip == null)
            {
                return;
            }
            Animator anim = ch.GetComponentInChildren<Animator>(includeInactive: true);
            if (anim == null || anim.runtimeAnimatorController == null)
            {
                return;
            }
            if (anim.runtimeAnimatorController is AnimatorOverrideController existingAoc &&
                existingAoc["Staff Magic Heal"] == frostShieldClip)
            {
                return;
            }
            AnimatorOverrideController aoc = new AnimatorOverrideController(anim.runtimeAnimatorController);
            aoc["Staff Magic Heal"] = frostShieldClip;
            anim.runtimeAnimatorController = aoc;
        }

        internal static void EnsureAshlandsDvergrInitialized(Character ch, ZNetView nview)
        {
            if (ch == null || !IsModdedDvergr(ch))
            {
                return;
            }

            bool isDeepNorth = IsDeepNorthDvergr(ch);
            ApplyAshlandsDvergrBaseHealth(ch);
            if (isDeepNorth && IsDvergrMage(ch))
            {
                EnsureDeepNorthMageAnimatorOverride(ch);
            }

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
            if (isDeepNorth)
            {
                enhancer.IsDeepNorth = true;
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
            if (isDeepNorth)
            {
                zdo.Set("AshlandsDvergrMod_IsDeepNorth", true);
            }
            else
            {
                zdo.Set("AshlandsDvergrMod_IsAshlands", true);
            }
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
                    : ((InsideConsoleSpawn && ConsoleExplicitLevel > 0)
                        ? ConsoleExplicitLevel
                        : RollConfiguredLevel());
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

            bool isDeepNorthTarget = EnableDeepNorthBuff.Value &&
                                     string.Equals(spawnName, "DvergerDeepNorth", StringComparison.OrdinalIgnoreCase);
            bool isExplicitMageRandom = string.Equals(spawnName, "DvergerMage", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(spawnName, "DvergrMage", StringComparison.OrdinalIgnoreCase);
            bool isVarietyTarget = isDeepNorthTarget ||
                                   isExplicitMageRandom ||
                                   string.Equals(spawnName, "DvergerAshlands", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(spawnName, "Dverger", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(spawnName, "Dvergr", StringComparison.OrdinalIgnoreCase);

            if (!isVarietyTarget || InsideConsoleVarietySplit)
            {
                return true;
            }

            if (isDeepNorthTarget)
            {
                InsideDeepNorthSpawn = true;
            }

            int count = 1;
            if (parts.Length >= 3 && int.TryParse(parts[2], out int parsedCount) && parsedCount > 1)
            {
                count = parsedCount;
            }

            string tailArgs = parts.Length > 3 ? " " + string.Join(" ", parts, 3, parts.Length - 3) : string.Empty;

            if (count <= 1)
            {
                string rolledPrefab = isDeepNorthTarget
                    ? RollDeepNorthPrefabForSpawn()
                    : (isExplicitMageRandom ? PickRandomVarietyMagePrefabName() : RollVarietyPrefabForSpawn());
                string countArg = parts.Length >= 3 ? " " + parts[2] : string.Empty;
                text = $"spawn {rolledPrefab}{countArg}{tailArgs}";
                VarietyReplacedInCurrentSpawn = true;
                Log.LogInfo($"[Console Variety Spawn] Rolled '{rolledPrefab}' for '{trimmed}'");
                return true;
            }

            Dictionary<string, int> rolledCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                string rolled = isDeepNorthTarget
                    ? RollDeepNorthPrefabForSpawn()
                    : (isExplicitMageRandom ? PickRandomVarietyMagePrefabName() : RollVarietyPrefabForSpawn());
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
                InsideDeepNorthSpawn = false;
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
                InsideDeepNorthSpawn = false;
                VarietyReplacedInCurrentSpawn = false;
                ConsoleExplicitLevel = 0;
            }
        }

        [HarmonyPatch(typeof(Character), "Awake")]
        [HarmonyPrefix]
        private static void Character_Awake_Prefix(Character __instance)
        {
            if (!IsModdedDvergr(__instance))
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
            if (!IsModdedDvergr(__instance))
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
            bool isDeepNorthDvergrSpawner = EnableDeepNorthBuff.Value &&
                (__instance.gameObject.name.IndexOf("DvergerDeepNorth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (IsDvergrPrefab(__instance.m_creaturePrefab) &&
                  (__instance.m_creaturePrefab.name.IndexOf("DeepNorth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   IsInDeepNorth(__instance.transform.position))));

            if (isDeepNorthDvergrSpawner)
            {
                PatchedCreatureSpawners.Add(id);
                __instance.m_minLevel = Mathf.Max(__instance.m_minLevel, MinLevel.Value);
                __instance.m_maxLevel = Mathf.Max(__instance.m_maxLevel, MaxLevel.Value);
                __instance.m_levelupChance = Mathf.Max(__instance.m_levelupChance, LevelUpChance.Value);
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
            if (EnableDeepNorthBuff.Value && IsDvergrPrefab(__instance.m_creaturePrefab) &&
                (__instance.m_creaturePrefab.name.IndexOf("DeepNorth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 IsInDeepNorth(__instance.transform.position)))
            {
                InsideDeepNorthSpawn = true;
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
                string pickName = RollDeepNorthPrefabForSpawn();
                if (!string.Equals(pickName, __instance.m_creaturePrefab.name, StringComparison.OrdinalIgnoreCase))
                {
                    GameObject altPrefab = ResolvePrefabByNameOrAssetId(pickName);
                    if (altPrefab != null)
                    {
                        __state = __instance.m_creaturePrefab;
                        __instance.m_creaturePrefab = altPrefab;
                    }
                }
                VarietyReplacedInCurrentSpawn = true;
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
            InsideDeepNorthSpawn = false;
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
            // 2-star Q2 upgraded weapon deals exact Q2 base damage at full HP + Bloodstone missing-HP bonus (up to +50% at low HP)
            float missingHpRatio = ch != null ? Mathf.Clamp01(1f - ch.GetHealthPercentage()) : 0f;
            return 1f + missingHpRatio * 0.50f;
        }

        internal static bool ShouldBlockUnprovokedPlayerLightning(Character target, HitData hit)
        {
            if (PreventUnprovokedLightningFriendlyFire == null || !PreventUnprovokedLightningFriendlyFire.Value ||
                target == null || hit == null || target.IsPlayer())
            {
                return false;
            }
            if (target.m_faction != Character.Faction.Dverger && !IsDvergrPrefab(target.gameObject))
            {
                return false;
            }
            if (hit.m_damage.m_lightning <= 0f)
            {
                return false;
            }
            Character attacker = hit.GetAttacker();
            if (attacker == null || (!attacker.IsPlayer() && !(attacker is Player)))
            {
                return false;
            }

            BaseAI baseAI = target.GetBaseAI();
            if (baseAI != null && baseAI.IsAggravated())
            {
                return false;
            }
            ZNetView nview = target.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                ZDO zdo = nview.GetZDO();
                if (zdo != null && zdo.GetBool("AshlandsDvergrMod_DirectPlayerHit"))
                {
                    return false;
                }
            }

            float nonLightningDamage =
                hit.m_damage.m_damage +
                hit.m_damage.m_blunt +
                hit.m_damage.m_slash +
                hit.m_damage.m_pierce +
                hit.m_damage.m_chop +
                hit.m_damage.m_pickaxe +
                hit.m_damage.m_fire +
                hit.m_damage.m_frost +
                hit.m_damage.m_poison +
                hit.m_damage.m_spirit;

            if (nonLightningDamage > 0f)
            {
                if (nview != null && nview.IsValid() && nview.IsOwner())
                {
                    nview.GetZDO()?.Set("AshlandsDvergrMod_DirectPlayerHit", true);
                }
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        [HarmonyPrefix]
        private static bool Character_Damage_Prefix(Character __instance, HitData hit)
        {
            if (ShouldBlockUnprovokedPlayerLightning(__instance, hit))
            {
                hit.m_damage.m_lightning = 0f;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        [HarmonyPrefix]
        private static bool Character_RPC_Damage_Prefix(Character __instance, HitData hit)
        {
            if (ShouldBlockUnprovokedPlayerLightning(__instance, hit))
            {
                hit.m_damage.m_lightning = 0f;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(SEMan), "ModifyAttack")]
        [HarmonyPostfix]
        private static void SEMan_ModifyAttack_Postfix(Character ___m_character, ref HitData hitData)
        {
            if (hitData != null && IsModdedDvergr(___m_character))
            {
                int stars = Mathf.Max(0, ___m_character.GetLevel() - 1);
                // Vanilla Attack already multiplied damage by (1 + stars * 0.50).
                // 0★: 1.00x * configMult; 1★: 1.25x * configMult; 2★ (with BloodWeapons): exact Q2 weapon base (1.00x) + Bloodstone missing-HP bonus.
                float vanillaStarMult = 1f + stars * 0.50f;
                float targetMult = (UseAshlandsBloodWeapons.Value && stars >= 2)
                    ? CalculateBloodWeaponDamageMultiplier(___m_character)
                    : GetDamageMultiplierFor(___m_character) * (1f + Mathf.Min(stars, 1) * 0.25f);
                hitData.m_damage.Modify(targetMult / vanillaStarMult);
            }
        }

        [HarmonyPatch(typeof(Aoe), "Setup")]
        [HarmonyPostfix]
        private static void Aoe_Setup_Postfix(Aoe __instance, Character owner, HitData hitData)
        {
            if (__instance == null || !IsModdedDvergr(owner))
            {
                return;
            }
            if (_liveWorldTestActive)
            {
                _liveAoeEvents.Add($"{__instance.name} by {owner.name} (status={__instance.m_statusEffect})");
            }
            if (hitData == null || !__instance.m_useAttackSettings)
            {
                int stars = Mathf.Max(0, owner.GetLevel() - 1);
                float targetMult = (UseAshlandsBloodWeapons.Value && stars >= 2)
                    ? CalculateBloodWeaponDamageMultiplier(owner)
                    : GetDamageMultiplierFor(owner) * (1f + Mathf.Min(stars, 1) * 0.25f);
                __instance.m_damage.Modify(targetMult);
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        [HarmonyPostfix]
        private static void Humanoid_StartAttack_Postfix(Humanoid __instance, bool __result)
        {
            if (!__result || __instance == null || !IsDeepNorthDvergr(__instance) || !IsDvergrMage(__instance))
            {
                return;
            }
            ItemDrop.ItemData curWep = __instance.GetCurrentWeapon();
            string curProj = curWep?.m_shared?.m_attack?.m_attackProjectile?.name;
            bool castShield = string.Equals(curProj, "staff_shield_aoe", StringComparison.OrdinalIgnoreCase);
            bool castVengeance = string.Equals(curProj, "staff_FrostOrbs_aoe", StringComparison.OrdinalIgnoreCase);
            if (!castShield && !castVengeance)
            {
                return;
            }
            // Enforce strict Protection <-> Northern Vengeance alternation with >= 8s delay between casts
            Inventory inv = __instance.GetInventory();
            if (inv == null) return;
            const float minDelayBetweenCasts = 8f;
            foreach (ItemDrop.ItemData it in inv.GetAllItems())
            {
                if (it?.m_shared?.m_attack == null) continue;
                string pName = it.m_shared.m_attack.m_attackProjectile?.name;
                bool isShield = string.Equals(pName, "staff_shield_aoe", StringComparison.OrdinalIgnoreCase);
                bool isVengeance = string.Equals(pName, "staff_FrostOrbs_aoe", StringComparison.OrdinalIgnoreCase);
                if (!isShield && !isVengeance) continue;

                if ((castShield && isVengeance) || (castVengeance && isShield))
                {
                    it.m_shared.m_aiPrioritized = true;
                    float interval = Mathf.Max(minDelayBetweenCasts, it.m_shared.m_aiAttackInterval);
                    float minLastTime = Time.time - (interval - minDelayBetweenCasts);
                    if (it.m_lastAttackTime < minLastTime)
                    {
                        it.m_lastAttackTime = minLastTime;
                    }
                }
                else
                {
                    it.m_shared.m_aiPrioritized = false;
                }
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
            if (__instance == null || !IsModdedDvergr(__instance))
            {
                return;
            }
            AshlandsDvergrEnhancer.ConfigureAshlandsCombatInventory(__instance);
        }

        private static bool _liveWorldTestActive;
        private static readonly List<string> _liveAoeEvents = new List<string>();
        private static readonly List<string> _liveProjEvents = new List<string>();

        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
        [HarmonyPostfix]
        private static void Projectile_Setup_Postfix(Projectile __instance, Character owner)
        {
            if (!_liveWorldTestActive || __instance == null || owner == null)
            {
                return;
            }
            if (IsModdedDvergr(owner) || __instance.name.IndexOf("FrostOrbs", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _liveProjEvents.Add($"{__instance.name} by {owner.name}");
            }
        }

        private static IEnumerator LiveDeepNorthWorldTestCoroutine()
        {
            _liveWorldTestActive = true;
            _liveAoeEvents.Clear();
            _liveProjEvents.Clear();

            float startupWait = 0f;
            FejdStartup fejd = null;
            while ((fejd = UnityEngine.Object.FindFirstObjectByType<FejdStartup>()) == null && startupWait < 30f)
            {
                startupWait += 0.5f;
                yield return new WaitForSeconds(0.5f);
            }
            yield return new WaitForSeconds(2.0f);
            Log.LogInfo($"=== [LiveWorldTest] FejdStartup ready (waited {startupWait:F1}s), booting temporary local world '__DNLiveWorld' ===");

            try
            {
                PlayerProfile prof = new PlayerProfile("__DNLiveChar", FileHelpers.FileSource.Local);
                prof.SetName("DNLiveTest");
                prof.Save();
                Game.SetProfile("__DNLiveChar", FileHelpers.FileSource.Local);

                World world = World.GetCreateWorld("__DNLiveWorld", FileHelpers.FileSource.Local);

                ZNet.m_onlineBackend = OnlineBackendType.Steamworks;
                ZNet.SetServer(true, false, false, world.m_name, "", world);
                ZNet.ResetServerHost();

                if (fejd != null)
                {
                    AccessTools.Method(typeof(FejdStartup), "LoadMainScene")?.Invoke(fejd, null);
                }
                else
                {
                    UnityEngine.SceneManagement.SceneManager.LoadScene("main");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[LiveWorldTest] Failed to start world: {ex}");
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                yield break;
            }

            float waitTimer = 0f;
            while ((Player.m_localPlayer == null || ZNetScene.instance == null || ZoneSystem.instance == null || Console.instance == null) && waitTimer < 60f)
            {
                if (Valkyrie.m_instance != null)
                {
                    AccessTools.Method(typeof(Valkyrie), "DropPlayer")?.Invoke(Valkyrie.m_instance, new object[] { true });
                }
                waitTimer += 0.5f;
                yield return new WaitForSeconds(0.5f);
            }

            if (Player.m_localPlayer == null)
            {
                Log.LogError("[LiveWorldTest] Timed out waiting for Player.m_localPlayer!");
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                yield break;
            }

            if (Valkyrie.m_instance != null)
            {
                AccessTools.Method(typeof(Valkyrie), "DropPlayer")?.Invoke(Valkyrie.m_instance, new object[] { true });
            }
            yield return new WaitForSeconds(2.0f);
            Player lp = Player.m_localPlayer;
            lp.SetGodMode(true);
            lp.SetGhostMode(true);
            Log.LogInfo($"[LiveWorldTest] In world at pos={lp.transform.position}, biome={WorldGenerator.instance?.GetBiome(lp.transform.position)}");

            Vector3 forward = lp.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 arenaCenter = lp.transform.position + forward * 8f;
            if (ZoneSystem.instance.GetGroundHeight(arenaCenter, out float gh))
            {
                arenaCenter.y = gh + 0.5f;
            }

            // Spawn 2x 2-star Deep North Rangers + 2x Deep North Mages to verify 2-star Thunderblood Crossbow, star scaling, and Mage cast alternation!
            int dnCrossbowCount = 0;
            int dnMageCount = 0;
            List<Character> spawnedDvergrs = new List<Character>();
            string[] liveRoster = { "DvergerDeepNorth", "DvergerMageSupport", "DvergerDeepNorth", "DvergerMageSupport" };
            int[] liveLevels = { 1, 1, 3, 3 };
            for (int i = 0; i < liveRoster.Length; i++)
            {
                string pick = liveRoster[i];
                GameObject p = ZNetScene.instance.GetPrefab(pick) ?? ResolvePrefabByNameOrAssetId(pick);
                if (p == null) continue;
                Vector3 pos = arenaCenter + new Vector3((i - 1.5f) * 1.8f, 0f, 0f);
                if (ZoneSystem.instance.GetGroundHeight(pos, out float h)) pos.y = h + 0.2f;
                InsideDeepNorthSpawn = true;
                VarietyReplacedInCurrentSpawn = true;
                PendingVarietySpawnLevel = liveLevels[i];
                GameObject inst = UnityEngine.Object.Instantiate(p, pos, Quaternion.LookRotation(-forward));
                InsideDeepNorthSpawn = false;
                VarietyReplacedInCurrentSpawn = false;
                PendingVarietySpawnLevel = 0;
                Character ch = inst.GetComponent<Character>();
                if (ch != null)
                {
                    spawnedDvergrs.Add(ch);
                    if (IsDvergrMage(ch)) dnMageCount++; else dnCrossbowCount++;
                }
            }

            yield return new WaitForSeconds(1.5f);

            bool allHaveNoHeal = true;
            foreach (Character dv in spawnedDvergrs)
            {
                if (dv == null) continue;
                Humanoid h = dv as Humanoid;
                List<string> invNames = new List<string>();
                if (h?.GetInventory() != null)
                {
                    foreach (var it in h.GetInventory().GetAllItems())
                    {
                        string n = it.m_dropPrefab?.name ?? it.m_shared?.m_name ?? "?";
                        string proj = it.m_shared?.m_attack?.m_attackProjectile?.name ?? "";
                        invNames.Add(string.IsNullOrEmpty(proj) ? n : $"{n}->{proj}");
                        if (n.IndexOf("_heal", StringComparison.OrdinalIgnoreCase) >= 0) allHaveNoHeal = false;
                    }
                }
                bool hasR = Array.Exists(dv.GetComponentsInChildren<Transform>(true), t => t.name == "AshlandsDvergr_BloodWeapon" && t.gameObject.activeInHierarchy);
                bool hasL = Array.Exists(dv.GetComponentsInChildren<Transform>(true), t => t.name == "AshlandsDvergr_LeftWeapon" && t.gameObject.activeInHierarchy);
                Log.LogInfo($"[LiveWorldTest Dvergr] name={dv.name}, isDN={IsDeepNorthDvergr(dv)}, hp={dv.GetHealth():F0}/{dv.GetMaxHealth():F0}, rWep={hasR}, lWep={hasL}, inv=[{string.Join(", ", invNames)}]");
            }

            // Spawn 1 high-HP StoneGolem / Lox wave (refilled if needed) so combat lasts ~34s to observe Protection <-> Northern Vengeance alternation & delay!
            GameObject enemyPrefab = ZNetScene.instance.GetPrefab("Lox") ?? ZNetScene.instance.GetPrefab("Troll") ?? ZNetScene.instance.GetPrefab("Charred_Melee");
            List<Character> enemies = new List<Character>();
            if (enemyPrefab != null)
            {
                for (int e = 0; e < 3; e++)
                {
                    Vector3 ePos = arenaCenter + forward * 5.0f + new Vector3((e - 1) * 2.0f, 0f, 0f);
                    if (ZoneSystem.instance.GetGroundHeight(ePos, out float eh)) ePos.y = eh + 0.2f;
                    GameObject eInst = UnityEngine.Object.Instantiate(enemyPrefab, ePos, Quaternion.LookRotation(-forward));
                    Character eCh = eInst.GetComponent<Character>();
                    if (eCh != null) { eCh.SetLevel(3); enemies.Add(eCh); }
                }
                Log.LogInfo($"[LiveWorldTest] Spawned {enemies.Count}x 2-star {enemyPrefab.name} for extended live combat observation.");
            }

            bool sawShieldBuff = false;
            bool sawFrostOrbsBuff = false;
            bool sawHealCast = false;
            bool sawShieldAoe = false;
            bool sawFrostOrbsAoe = false;
            bool sawFrostOrbProj = false;
            bool screenshotTaken = false;
            int lastLoggedAoeCount = 0;
            List<string> castTimeline = new List<string>();
            float combatStart = Time.time;

            for (int sec = 1; sec <= 34; sec++)
            {
                yield return new WaitForSeconds(1.0f);
                while (lastLoggedAoeCount < _liveAoeEvents.Count)
                {
                    string ev = _liveAoeEvents[lastLoggedAoeCount++];
                    castTimeline.Add($"t={Time.time - combatStart:F1}s:{ev}");
                    Log.LogInfo($"[LiveWorldCast] t={Time.time - combatStart:F1}s | {ev}");
                }
                List<string> states = new List<string>();
                foreach (Character dv in spawnedDvergrs)
                {
                    if (dv == null) continue;
                    List<string> ses = new List<string>();
                    foreach (StatusEffect se in dv.GetSEMan().GetStatusEffects())
                    {
                        ses.Add(se.name);
                        if (se.name.IndexOf("Staff_shield", StringComparison.OrdinalIgnoreCase) >= 0) sawShieldBuff = true;
                        if (se.name.IndexOf("Staff_FrostOrbs", StringComparison.OrdinalIgnoreCase) >= 0) sawFrostOrbsBuff = true;
                    }
                    states.Add($"{dv.name.Replace("(Clone)", "")}[L{dv.GetLevel()}](hp={dv.GetHealth():F0}/{dv.GetMaxHealth():F0},se=[{string.Join(",", ses)}])");
                }
                foreach (string ev in _liveAoeEvents)
                {
                    if (ev.IndexOf("staff_shield_aoe", StringComparison.OrdinalIgnoreCase) >= 0) sawShieldAoe = true;
                    if (ev.IndexOf("staff_FrostOrbs_aoe", StringComparison.OrdinalIgnoreCase) >= 0) sawFrostOrbsAoe = true;
                    if (ev.IndexOf("DvergerStaffHeal_aoe", StringComparison.OrdinalIgnoreCase) >= 0) sawHealCast = true;
                }
                foreach (string pev in _liveProjEvents)
                {
                    if (pev.IndexOf("FrostOrbs", StringComparison.OrdinalIgnoreCase) >= 0) sawFrostOrbProj = true;
                }
                if (sec % 4 == 0 || sec <= 3)
                {
                    Log.LogInfo($"[LiveWorldTest t={sec}s] {string.Join(" | ", states)} | aoes={_liveAoeEvents.Count}, projs={_liveProjEvents.Count}");
                }

                if (!screenshotTaken && (sec == 10 || (sawShieldBuff && sawFrostOrbsBuff)))
                {
                    screenshotTaken = true;
                    try
                    {
                        Camera cam = Camera.main;
                        if (cam != null)
                        {
                            int sw = 1280, sh = 720;
                            RenderTexture prevRt = cam.targetTexture;
                            RenderTexture rt = new RenderTexture(sw, sh, 24, RenderTextureFormat.ARGB32);
                            cam.targetTexture = rt;
                            cam.Render();
                            RenderTexture.active = rt;
                            Texture2D tex = new Texture2D(sw, sh, TextureFormat.RGB24, false);
                            tex.ReadPixels(new Rect(0, 0, sw, sh), 0, 0);
                            tex.Apply();
                            RenderTexture.active = null;
                            cam.targetTexture = prevRt;
                            File.WriteAllBytes("/tmp/dvergr_deepnorth_live.png", ImageConversion.EncodeToPNG(tex));
                            Log.LogInfo("[LiveWorldTest] Captured live combat screenshot to /tmp/dvergr_deepnorth_live.png");
                        }
                    }
                    catch (Exception sex)
                    {
                        Log.LogWarning($"[LiveWorldTest] Screenshot failed: {sex.Message}");
                    }
                }
            }

            // Test dvergrstats command live in world
            Log.LogInfo("[LiveWorldTest dvergrstats]\n" + FormatWorldStatsSummary());
            Log.LogInfo($"[LiveWorldTest CastTimeline] [{string.Join(" | ", castTimeline)}]");
            Log.LogInfo($"[LiveWorldTest Events] AOEs={_liveAoeEvents.Count} | Projs={_liveProjEvents.Count}");
            bool livePass = dnCrossbowCount > 0 && dnMageCount > 0 && allHaveNoHeal && !sawHealCast && (sawShieldAoe || sawShieldBuff) && (sawFrostOrbsAoe || sawFrostOrbsBuff);
            Log.LogInfo($"[LiveWorldVerifyDeepNorth] PASS={livePass} | split=({dnCrossbowCount} NordCrossbow, {dnMageCount} Mage) | noHealInv={allHaveNoHeal} | noHealCast={!sawHealCast} | shieldAoe={sawShieldAoe} | shieldBuff={sawShieldBuff} | frostOrbsAoe={sawFrostOrbsAoe} | frostOrbsBuff={sawFrostOrbsBuff} | frostOrbProjFired={sawFrostOrbProj}");

            yield return new WaitForSeconds(0.5f);
            System.Diagnostics.Process.GetCurrentProcess().Kill();
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

                // Runtime verification for Deep North Dvergrs: 50/50 split, slight buff, Nord Crossbow, Staff of Protection + Northern Vengeance (no heal)
                {
                    Dictionary<string, int> dnDist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int r = 0; r < 100; r++)
                    {
                        string rolled = RollDeepNorthPrefabForSpawn();
                        dnDist.TryGetValue(rolled, out int c);
                        dnDist[rolled] = c + 1;
                    }
                    dnDist.TryGetValue("DvergerDeepNorth", out int dnCrossbowPct);
                    dnDist.TryGetValue("DvergerMageSupport", out int dnMagePct);

                    bool dnCrossbowOk = false;
                    bool dnMageOk = false;
                    bool dnMageNoHeal = false;
                    float dnCrossbowHp = 0f;
                    float dnCrossbowPierce = 0f;
                    float dnMageHp = 0f;
                    bool dnMageHasShield = false;
                    bool dnMageHasVengeance = false;

                    bool prevDisable = ZNetView.m_forceDisableInit;
                    ZNetView.m_forceDisableInit = true;
                    try
                    {
                        GameObject dnRangerPrefab = ResolvePrefabByNameOrAssetId("DvergerDeepNorth");
                        if (dnRangerPrefab != null)
                        {
                            InsideDeepNorthSpawn = true;
                            GameObject inst = UnityEngine.Object.Instantiate(dnRangerPrefab, stageOrigin + new Vector3(0f, 30f, 0f), Quaternion.identity);
                            InsideDeepNorthSpawn = false;
                            Character ch = inst.GetComponent<Character>();
                            Humanoid dnHum = inst.GetComponent<Humanoid>();
                            if (ch != null && dnHum != null)
                            {
                                ApplyAshlandsDvergrBaseHealth(ch);
                                EnsureAshlandsDvergrInitialized(ch, null);
                                Inventory dnInv = dnHum.GetInventory();
                                if (dnHum.m_defaultItems != null)
                                    foreach (GameObject di in dnHum.m_defaultItems) if (di != null) dnInv.AddItem(di, 1);
                                AshlandsDvergrEnhancer.ConfigureAshlandsCombatInventory(dnHum);
                                inst.GetComponent<AshlandsDvergrEnhancer>()?.ApplyCoolVisuals();
                                dnCrossbowHp = ch.m_health;
                                foreach (var it in dnInv.GetAllItems())
                                {
                                    string dn = it.m_dropPrefab != null ? it.m_dropPrefab.name : (it.m_shared?.m_name ?? "");
                                    if (dn.IndexOf("Arbalest_shoot", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        dnCrossbowPierce = it.m_shared.m_damages.m_pierce;
                                    }
                                }
                                bool hasNordVisual = inst.GetComponentInChildren<Transform>() != null &&
                                                     Array.Exists(inst.GetComponentsInChildren<Transform>(true), t => t.name == "AshlandsDvergr_BloodWeapon");
                                // Verify 0★ -> 1★ +25% damage scaling
                                var lvlField = typeof(Character).GetField("m_level", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                                lvlField?.SetValue(ch, 1);
                                HitData h0 = new HitData(); h0.m_damage.m_pierce = 264f;
                                SEMan_ModifyAttack_Postfix(ch, ref h0);
                                lvlField?.SetValue(ch, 2);
                                HitData h1 = new HitData(); h1.m_damage.m_pierce = 264f * 1.5f; // vanilla 1★ multiplies by 1.5x
                                SEMan_ModifyAttack_Postfix(ch, ref h1);
                                lvlField?.SetValue(ch, 1);
                                bool star1Scaled25 = Mathf.Abs(h1.m_damage.m_pierce - h0.m_damage.m_pierce * 1.25f) < 0.1f;
                                Log.LogInfo($"[RuntimeVerifyStarDamageScaling] 0★={h0.m_damage.m_pierce:F1}, 1★={h1.m_damage.m_pierce:F1}, scaled25={star1Scaled25}");
                                dnCrossbowOk = Mathf.Abs(dnCrossbowHp - 1725f) < 1f && dnCrossbowPierce >= 264f && hasNordVisual && star1Scaled25;
                            }
                            UnityEngine.Object.DestroyImmediate(inst);
                        }

                        GameObject dnMagePrefab = ResolvePrefabByNameOrAssetId("DvergerMageSupport");
                        if (dnMagePrefab != null)
                        {
                            var lvlField = typeof(Character).GetField("m_level", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            bool m0Ok = false, m1Ok = false, m2Ok = false;
                            foreach (int testLvl in new[] { 1, 2, 3 })
                            {
                                InsideDeepNorthSpawn = true;
                                GameObject inst = UnityEngine.Object.Instantiate(dnMagePrefab, stageOrigin + new Vector3(0f, 35f + testLvl, 0f), Quaternion.identity);
                                InsideDeepNorthSpawn = false;
                                Character ch = inst.GetComponent<Character>();
                                Humanoid dnHum = inst.GetComponent<Humanoid>();
                                if (ch != null && dnHum != null)
                                {
                                    lvlField?.SetValue(ch, testLvl);
                                    EnsureAshlandsDvergrInitialized(ch, null);
                                    Inventory dnInv = dnHum.GetInventory();
                                    if (dnHum.m_defaultItems != null)
                                        foreach (GameObject di in dnHum.m_defaultItems) if (di != null) dnInv.AddItem(di, 1);
                                    AshlandsDvergrEnhancer.ConfigureAshlandsCombatInventory(dnHum);
                                    inst.GetComponent<AshlandsDvergrEnhancer>()?.ApplyCoolVisuals();
                                    bool noHeal = true, hasShield = false, hasVeng = false, hasTb = false;
                                    foreach (var it in dnInv.GetAllItems())
                                    {
                                        string dn = it.m_dropPrefab != null ? it.m_dropPrefab.name : (it.m_shared?.m_name ?? "");
                                        string projName = it.m_shared?.m_attack?.m_attackProjectile?.name ?? "";
                                        if (dn.IndexOf("_heal", StringComparison.OrdinalIgnoreCase) >= 0) noHeal = false;
                                        if (projName == "staff_shield_aoe") hasShield = true;
                                        if (projName == "staff_FrostOrbs_aoe") hasVeng = true;
                                        if (projName == "staff_thunderblood_projectile") hasTb = true;
                                    }
                                    bool hasRight = Array.Exists(inst.GetComponentsInChildren<Transform>(true), t => t.name == "AshlandsDvergr_BloodWeapon" && t.gameObject.activeInHierarchy);
                                    bool hasOrb = Array.Exists(inst.GetComponentsInChildren<Transform>(true), t => t.name == "AshlandsDvergr_LeftWeapon" && t.gameObject.activeInHierarchy);
                                    if (testLvl == 1)
                                    {
                                        dnMageHp = ch.m_health;
                                        m0Ok = Mathf.Abs(dnMageHp - 1265f) < 1f && noHeal && hasShield && !hasVeng && !hasTb && hasRight && !hasOrb;
                                    }
                                    else if (testLvl == 2)
                                    {
                                        dnMageNoHeal = noHeal; dnMageHasShield = hasShield; dnMageHasVengeance = hasVeng;
                                        m1Ok = noHeal && hasShield && hasVeng && !hasTb && hasRight && hasOrb;
                                    }
                                    else if (testLvl == 3)
                                    {
                                        m2Ok = noHeal && !hasShield && !hasVeng && hasTb && hasRight && !hasOrb;
                                    }
                                    Log.LogInfo($"[RuntimeVerifyDNMage L{testLvl}] noHeal={noHeal}, shield={hasShield}, veng={hasVeng}, thunderblood={hasTb}, rWep={hasRight}, lOrb={hasOrb}");
                                }
                                UnityEngine.Object.DestroyImmediate(inst);
                            }
                            dnMageOk = m0Ok && m1Ok && m2Ok;
                        }
                    }
                    finally
                    {
                        InsideDeepNorthSpawn = false;
                        ZNetView.m_forceDisableInit = prevDisable;
                    }

                    bool dnPass = dnCrossbowPct == 50 && dnMagePct == 50 && dnCrossbowOk && dnMageOk;
                    Log.LogInfo($"[RuntimeVerifyDeepNorth] split=({dnCrossbowPct}% NordCrossbow / {dnMagePct}% ProtVengeanceMage), NordCrossbow=(hp={dnCrossbowHp:F0}, pierce={dnCrossbowPierce:F0}, ok={dnCrossbowOk}), Mage=(hp={dnMageHp:F0}, shield={dnMageHasShield}, vengeance={dnMageHasVengeance}, noHeal={dnMageNoHeal}, ok={dnMageOk}), PASS={dnPass}");

                    // Verify Player 'FrostShield' AnimationClip override on Deep North Mage Animator
                    {
                        AnimationClip fsClip = GetPlayerFrostShieldClip();
                        Log.LogInfo($"[RuntimeVerifyFrostShieldAnim] playerClip={fsClip?.name ?? "NULL"}, len={fsClip?.length ?? 0f:F2}s, human={fsClip?.humanMotion ?? false}, events={fsClip?.events?.Length ?? 0}");
                    }
                    foreach (string q2Wep in new[] { "CrossbowRipperBlood", "StaffClusterbomb", "StaffLightning", "CrossbowGold_BloodLightning", "StaffThunderBlood" })
                    {
                        var sd = ResolvePrefabByNameOrAssetId(q2Wep)?.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
                        if (sd == null) continue;
                        var q1 = sd.m_damages;
                        var q2 = q1;
                        q2.Add(sd.m_damagesPerLevel);
                        Log.LogInfo($"[RuntimeVerifyQ2Weapon {q2Wep}] Q1=(p={q1.m_pierce},b={q1.m_blunt},f={q1.m_fire},fr={q1.m_frost},l={q1.m_lightning}) -> Q2=(p={q2.m_pierce},b={q2.m_blunt},f={q2.m_fire},fr={q2.m_frost},l={q2.m_lightning})");
                    }

                    // Deep observation of Deep North spell projectiles, Aoe flags, StatusEffects, and live NPC SE_Shield / SE_React behavior
                    foreach (string spellWepName in new[] { "StaffShield", "StaffFrostOrbs", "CrossbowGold" })
                    {
                        GameObject wp = ResolvePrefabByNameOrAssetId(spellWepName);
                        if (wp == null) continue;
                        ItemDrop id = wp.GetComponent<ItemDrop>();
                        var sd = id?.m_itemData?.m_shared;
                        var atk = sd?.m_attack;
                        StatusEffect se = sd?.m_attackStatusEffect;
                        if (se != null)
                        {
                            List<string> seFields = new List<string>();
                            foreach (var fi in se.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                            {
                                if (fi.DeclaringType == se.GetType())
                                    seFields.Add($"{fi.Name}={fi.GetValue(se)}");
                            }
                            Log.LogInfo($"[DNStatusEffect {spellWepName}] type={se.GetType().FullName}, name={se.name}, ttl={se.m_ttl}, fields=[{string.Join(", ", seFields)}]");
                        }
                        if (atk != null)
                        {
                            Log.LogInfo($"[DNAtk {spellWepName}] atkType={atk.m_attackType}, anim={atk.m_attackAnimation}, proj={atk.m_attackProjectile?.name}, spawnOnTrig={atk.m_spawnOnTrigger?.name}, aiTarget={sd.m_aiTargetType}, aiRange={sd.m_aiAttackRange}");
                        }
                    }

                    // Observe Deep North Mage configured AI attack items & test SE_Shield + SE_React on NPC SEMan
                    {
                        bool prevDis = ZNetView.m_forceDisableInit;
                        ZNetView.m_forceDisableInit = true;
                        GameObject dnMageTest = null;
                        try
                        {
                            GameObject dnMagePrefab2 = ResolvePrefabByNameOrAssetId("DvergerMageSupport");
                            if (dnMagePrefab2 != null)
                            {
                                InsideDeepNorthSpawn = true;
                                dnMageTest = UnityEngine.Object.Instantiate(dnMagePrefab2, stageOrigin + new Vector3(0f, 40f, 0f), Quaternion.identity);
                                InsideDeepNorthSpawn = false;
                                Character mCh = dnMageTest.GetComponent<Character>();
                                Humanoid mHum = dnMageTest.GetComponent<Humanoid>();
                                EnsureAshlandsDvergrInitialized(mCh, null);
                                Inventory mInv = mHum.GetInventory();
                                if (mHum.m_defaultItems != null)
                                    foreach (GameObject di in mHum.m_defaultItems) if (di != null) mInv.AddItem(di, 1);
                                AshlandsDvergrEnhancer.ConfigureAshlandsCombatInventory(mHum);
                                foreach (var it in mInv.GetAllItems())
                                {
                                    if (it?.m_shared?.m_attack == null) continue;
                                    Log.LogInfo($"[DNMageInv] item={it.m_dropPrefab?.name ?? it.m_shared.m_name}, aiTarget={it.m_shared.m_aiTargetType}, prio={it.m_shared.m_aiPrioritized}, atkType={it.m_shared.m_attack.m_attackType}, anim={it.m_shared.m_attack.m_attackAnimation}, proj={it.m_shared.m_attack.m_attackProjectile?.name}, range={it.m_shared.m_aiAttackRange}, interval={it.m_shared.m_aiAttackInterval}");
                                }

                                // Test SE_Shield & SE_React directly on NPC SEMan
                                StatusEffect shieldSe = ResolvePrefabByNameOrAssetId("StaffShield")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_attackStatusEffect;
                                StatusEffect orbsSe = ResolvePrefabByNameOrAssetId("StaffFrostOrbs")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_attackStatusEffect;
                                SEMan seman = mCh.GetSEMan();
                                StatusEffect appliedShield = shieldSe != null ? seman.AddStatusEffect(shieldSe) : null;
                                StatusEffect appliedOrbs = orbsSe != null ? seman.AddStatusEffect(orbsSe) : null;
                                HitData simHit = new HitData();
                                simHit.m_damage.m_blunt = 100f;
                                float shieldBefore = simHit.m_damage.m_blunt;
                                appliedShield?.OnDamaged(simHit, null);
                                float shieldAfter = simHit.m_damage.m_blunt;
                                bool reactNoThrow = true;
                                try
                                {
                                    HitData reactHit = new HitData();
                                    reactHit.m_damage.m_blunt = 20f;
                                    appliedOrbs?.OnDamaged(reactHit, mCh);
                                }
                                catch (Exception rex)
                                {
                                    reactNoThrow = false;
                                    Log.LogWarning($"[DNMageSE] SE_React.OnDamaged threw: {rex.Message}");
                                }
                                Log.LogInfo($"[RuntimeObserveDeepNorthSpells] shieldApplied={appliedShield != null}, shieldAbsorbed={shieldBefore}->{shieldAfter}, frostOrbsApplied={appliedOrbs != null}, frostOrbsReactNoThrow={reactNoThrow}");
                            }
                        }
                        finally
                        {
                            InsideDeepNorthSpawn = false;
                            if (dnMageTest != null) UnityEngine.Object.DestroyImmediate(dnMageTest);
                            ZNetView.m_forceDisableInit = prevDis;
                        }
                    }
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

                // Live test for PreventUnprovokedLightningFriendlyFire with ZDOMan + ZNetScene + Player + DvergerAshlands
                GameObject netHost = new GameObject("LiveTestNetHost");
                netHost.SetActive(false);
                bool prevToggle = PreventUnprovokedLightningFriendlyFire.Value;
                bool prevZNetDisable = ZNetView.m_forceDisableInit;
                GameObject testPlayerGo = null;
                GameObject testDvergrGo = null;
                try
                {
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    ZNet fakeZNet = netHost.AddComponent<ZNet>();
                    typeof(ZNet).GetField("m_isServer", flags)?.SetValue(fakeZNet, true);
                    typeof(ZNet).GetField("m_instance", flags)?.SetValue(null, fakeZNet);

                    ZDOExtraData.Init();
                    ZDOMan fakeZdoMan = (ZDOMan)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ZDOMan));
                    var zdoDict = new Dictionary<ZDOID, ZDO>();
                    typeof(ZDOMan).GetField("m_objectsByID", flags)?.SetValue(fakeZdoMan, zdoDict);
                    typeof(ZDOMan).GetField("m_width", flags)?.SetValue(fakeZdoMan, 16);
                    typeof(ZDOMan).GetField("m_halfWidth", flags)?.SetValue(fakeZdoMan, 8);
                    typeof(ZDOMan).GetField("m_objectsBySector", flags)?.SetValue(fakeZdoMan, new List<ZDO>[16 * 16]);
                    typeof(ZDOMan).GetField("s_instance", flags)?.SetValue(null, fakeZdoMan);

                    ZNetScene fakeZNetScene = netHost.AddComponent<ZNetScene>();
                    var znsInstDict = new Dictionary<ZDO, ZNetView>();
                    typeof(ZNetScene).GetField("m_instances", flags)?.SetValue(fakeZNetScene, znsInstDict);
                    typeof(ZNetScene).GetField("s_instance", flags)?.SetValue(null, fakeZNetScene);

                    ZNetView.m_forceDisableInit = true;
                    testPlayerGo = new GameObject("TestPlayer");
                    testPlayerGo.SetActive(false);
                    Player testPlayer = testPlayerGo.AddComponent<Player>();
                    testPlayer.m_faction = Character.Faction.Players;
                    ZNetView playerNv = testPlayerGo.AddComponent<ZNetView>();
                    ZDO playerZdo = new ZDO();
                    ZDOID playerZdoid = new ZDOID(1L, 1001u);
                    playerZdo.m_uid = playerZdoid;
                    typeof(ZDOMan).GetField("m_sessionID", flags)?.SetValue(fakeZdoMan, 1L);
                    playerZdo.SetOwnerInternal(1L);
                    playerZdo.SetPrefab("Player".GetStableHashCode());
                    zdoDict[playerZdoid] = playerZdo;
                    znsInstDict[playerZdo] = playerNv;
                    typeof(ZNetView).GetField("m_zdo", flags)?.SetValue(playerNv, playerZdo);
                    typeof(Character).GetField("m_nview", flags)?.SetValue(testPlayer, playerNv);

                    GameObject dvPrefab = ResolvePrefabByNameOrAssetId("DvergerAshlands");
                    testDvergrGo = UnityEngine.Object.Instantiate(dvPrefab, stageOrigin + new Vector3(0f, 30f, 0f), Quaternion.identity);
                    Character testDvergr = testDvergrGo.GetComponent<Character>();
                    ZNetView dvergrNv = testDvergrGo.GetComponent<ZNetView>();
                    BaseAI dvergrAi = testDvergr.GetBaseAI();
                    ZDO dvergrZdo = new ZDO();
                    ZDOID dvergrZdoid = new ZDOID(1L, 1002u);
                    dvergrZdo.m_uid = dvergrZdoid;
                    dvergrZdo.SetOwnerInternal(1L);
                    dvergrZdo.SetPrefab("DvergerAshlands".GetStableHashCode());
                    zdoDict[dvergrZdoid] = dvergrZdo;
                    znsInstDict[dvergrZdo] = dvergrNv;
                    typeof(ZNetView).GetField("m_zdo", flags)?.SetValue(dvergrNv, dvergrZdo);
                    typeof(Character).GetField("m_nview", flags)?.SetValue(testDvergr, dvergrNv);
                    typeof(BaseAI).GetField("m_nview", flags)?.SetValue(dvergrAi, dvergrNv);

                    PreventUnprovokedLightningFriendlyFire.Value = true;

                    // Case 1: Unprovoked pure lightning hit -> must be blocked
                    HitData hit1 = new HitData { m_attacker = playerZdoid };
                    hit1.m_damage.m_lightning = 50f;
                    bool case1Allowed = Character_Damage_Prefix(testDvergr, hit1);

                    // Case 2: Direct hybrid hit (slash + lightning) -> must be allowed & mark DirectPlayerHit
                    HitData hit2 = new HitData { m_attacker = playerZdoid };
                    hit2.m_damage.m_slash = 80f;
                    hit2.m_damage.m_lightning = 30f;
                    bool case2Allowed = Character_Damage_Prefix(testDvergr, hit2);

                    // Case 3: Pure lightning hit AFTER direct hit -> must be allowed
                    HitData hit3 = new HitData { m_attacker = playerZdoid };
                    hit3.m_damage.m_lightning = 50f;
                    bool case3Allowed = Character_Damage_Prefix(testDvergr, hit3);

                    // Case 4: Reset DirectPlayerHit, set BaseAI aggravated -> pure lightning must be allowed
                    dvergrZdo.Set("AshlandsDvergrMod_DirectPlayerHit", false);
                    dvergrZdo.Set(ZDOVars.s_aggravated, true);
                    typeof(BaseAI).GetField("m_aggravated", flags)?.SetValue(dvergrAi, true);
                    typeof(BaseAI).GetField("m_lastAggravatedCheck", flags)?.SetValue(dvergrAi, Time.time);
                    HitData hit4 = new HitData { m_attacker = playerZdoid };
                    hit4.m_damage.m_lightning = 50f;
                    bool case4Allowed = Character_Damage_Prefix(testDvergr, hit4);

                    // Case 5: Reset aggravated, disable toggle -> pure lightning must be allowed
                    dvergrZdo.Set(ZDOVars.s_aggravated, false);
                    typeof(BaseAI).GetField("m_aggravated", flags)?.SetValue(dvergrAi, false);
                    PreventUnprovokedLightningFriendlyFire.Value = false;
                    HitData hit5 = new HitData { m_attacker = playerZdoid };
                    hit5.m_damage.m_lightning = 50f;
                    bool case5Allowed = Character_Damage_Prefix(testDvergr, hit5) && Character_RPC_Damage_Prefix(testDvergr, hit5);

                    // Case 6: Re-enable toggle -> pure lightning must be blocked again on both Damage & RPC_Damage
                    PreventUnprovokedLightningFriendlyFire.Value = true;
                    HitData hit6 = new HitData { m_attacker = playerZdoid };
                    hit6.m_damage.m_lightning = 50f;
                    bool case6AllowedDamage = Character_Damage_Prefix(testDvergr, hit6);
                    HitData hit6Rpc = new HitData { m_attacker = playerZdoid };
                    hit6Rpc.m_damage.m_lightning = 50f;
                    bool case6AllowedRpc = Character_RPC_Damage_Prefix(testDvergr, hit6Rpc);

                    bool allPass = !case1Allowed && hit1.m_damage.m_lightning == 0f &&
                                   case2Allowed && hit2.m_damage.m_lightning == 30f &&
                                   case3Allowed && hit3.m_damage.m_lightning == 50f &&
                                   case4Allowed && hit4.m_damage.m_lightning == 50f &&
                                   case5Allowed && hit5.m_damage.m_lightning == 50f &&
                                   !case6AllowedDamage && hit6.m_damage.m_lightning == 0f &&
                                   !case6AllowedRpc && hit6Rpc.m_damage.m_lightning == 0f;
                    Log.LogInfo($"[RuntimeVerifyLightningFriendlyFire] PASS={allPass} (attackerIsPlayer={hit1.GetAttacker()?.IsPlayer()}, aiAggr={dvergrAi?.IsAggravated()}, directHit={dvergrZdo.GetBool("AshlandsDvergrMod_DirectPlayerHit")}) | unprovokedBlocked={!case1Allowed}(dmg={hit1.m_damage.m_lightning}) | directAllowed={case2Allowed}(dmg={hit2.m_damage.m_lightning}) | postDirectAllowed={case3Allowed}(dmg={hit3.m_damage.m_lightning}) | aggravatedAllowed={case4Allowed}(dmg={hit4.m_damage.m_lightning}) | toggleOffAllowed={case5Allowed}(dmg={hit5.m_damage.m_lightning}) | toggleOnAgainBlocked={!case6AllowedDamage && !case6AllowedRpc}(dmg={hit6.m_damage.m_lightning}/rpc={hit6Rpc.m_damage.m_lightning})");

                    // Verify config sync, admin RPC, and world stats summary
                    float prevHpMult = HealthMultiplier.Value;
                    try
                    {
                        ZPackage pkg = BuildConfigPackage();
                        typeof(ZNet).GetField("m_isServer", flags)?.SetValue(fakeZNet, false);
                        HealthMultiplier.Value = 2.0f; // simulated local change before sync
                        pkg.SetPos(0);
                        RPC_AshlandsDvergrSyncConfig(1L, pkg);
                        bool syncRestored = Mathf.Approximately(HealthMultiplier.Value, prevHpMult);
                        string clientSummary = FormatWorldStatsSummary();

                        typeof(ZNet).GetField("m_isServer", flags)?.SetValue(fakeZNet, true);
                        RPC_AshlandsDvergrSetConfig(99999L, "HealthMultiplier", "2.5"); // non-existent/non-admin peer -> rejected
                        bool nonAdminRejected = Mathf.Approximately(HealthMultiplier.Value, prevHpMult);
                        RPC_AshlandsDvergrSetConfig(ZNet.GetUID(), "HealthMultiplier", "1.45"); // host -> accepted
                        bool hostAccepted = Mathf.Approximately(HealthMultiplier.Value, 1.45f);
                        Log.LogInfo($"[RuntimeVerifyConfigSync] PASS={syncRestored && nonAdminRejected && hostAccepted && clientSummary.Contains("Ashlands Dvergr Boost")} | syncRestored={syncRestored} | nonAdminRejected={nonAdminRejected} | hostAccepted={hostAccepted} | statsLines={clientSummary.Split('\n').Length}");
                    }
                    finally
                    {
                        HealthMultiplier.Value = prevHpMult;
                        Instance?.Config.Save();
                    }
                }
                finally
                {
                    PreventUnprovokedLightningFriendlyFire.Value = prevToggle;
                    ZNetView.m_forceDisableInit = prevZNetDisable;
                    var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    typeof(ZDOMan).GetField("s_instance", flags)?.SetValue(null, null);
                    typeof(ZNetScene).GetField("s_instance", flags)?.SetValue(null, null);
                    typeof(ZNet).GetField("m_instance", flags)?.SetValue(null, null);
                    if (testDvergrGo != null) UnityEngine.Object.DestroyImmediate(testDvergrGo);
                    if (testPlayerGo != null) UnityEngine.Object.DestroyImmediate(testPlayerGo);
                    UnityEngine.Object.DestroyImmediate(netHost);
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

                // Deep North visual validation render: 0★ Mage (Prot only), 1★ Mage (Prot + Vengeance orb), 2★ Mage (Thunderblood only)
                GameObject dnStage = new GameObject("DeepNorthValidationStage");
                dnStage.transform.position = stageOrigin + new Vector3(30f, 0f, 0f);
                string[] dnNames = { "DvergerMageSupport", "DvergerMageSupport", "DvergerMageSupport" };
                int[] dnLvls = { 1, 2, 3 };
                float[] dnX = { -1.85f, 0.0f, 1.85f };
                for (int i = 0; i < 3; i++)
                {
                    GameObject curPrefab = ResolvePrefabByNameOrAssetId(dnNames[i]) ?? prefab;
                    Transform curVisualTemplate = curPrefab.transform.Find("Visual") ?? curPrefab.transform;
                    GameObject model = UnityEngine.Object.Instantiate(curVisualTemplate.gameObject, dnStage.transform);
                    model.name = dnNames[i];
                    model.transform.localPosition = new Vector3(dnX[i], 0f, 0f);
                    model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    float sc = dnLvls[i] == 1 ? 1.0f : (dnLvls[i] == 2 ? VisualScale1Star.Value : VisualScale2Star.Value);
                    model.transform.localScale = Vector3.one * sc;

                    VisEquipment curVisEq = curPrefab.GetComponent<VisEquipment>();
                    string rightHandBoneName = curVisEq?.m_rightHand != null ? curVisEq.m_rightHand.name : "RightHand_Attach";
                    Humanoid curHum = curPrefab.GetComponent<Humanoid>();
                    SkinnedMeshRenderer bodySmr = model.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true);
                    if (bodySmr != null && curHum?.m_defaultItems != null)
                    {
                        Transform rightHand = null;
                        foreach (Transform t in model.GetComponentsInChildren<Transform>(includeInactive: true))
                        {
                            if (string.Equals(t.name, rightHandBoneName, StringComparison.OrdinalIgnoreCase))
                            {
                                rightHand = t;
                                break;
                            }
                        }
                        foreach (GameObject itemPrefab in curHum.m_defaultItems)
                        {
                            if (itemPrefab == null) continue;
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
                            }
                        }
                    }
                    Light dnAura = null;
                    InsideDeepNorthSpawn = true;
                    try
                    {
                        AshlandsDvergrEnhancer.ApplyStyleToHierarchy(model.transform, dnLvls[i], ref dnAura, out _);
                    }
                    finally
                    {
                        InsideDeepNorthSpawn = false;
                    }
                }
                GameObject dnLightObj = new GameObject("DNKeyLight");
                dnLightObj.transform.SetParent(dnStage.transform, false);
                dnLightObj.transform.localPosition = new Vector3(1.5f, 3.2f, -3.5f);
                dnLightObj.transform.LookAt(dnStage.transform.position + new Vector3(0f, 1.1f, 0f));
                Light dnKeyLight = dnLightObj.AddComponent<Light>();
                dnKeyLight.type = LightType.Directional;
                dnKeyLight.color = new Color(0.92f, 0.96f, 1.0f);
                dnKeyLight.intensity = 1.2f;

                GameObject dnCamObj = new GameObject("DNValidationCamera");
                dnCamObj.transform.SetParent(dnStage.transform, false);
                dnCamObj.transform.localPosition = new Vector3(0f, 1.15f, -3.9f);
                dnCamObj.transform.LookAt(dnStage.transform.position + new Vector3(0f, 1.05f, 0f));
                Camera dnCam = dnCamObj.AddComponent<Camera>();
                dnCam.clearFlags = CameraClearFlags.SolidColor;
                dnCam.backgroundColor = new Color(0.09f, 0.12f, 0.16f, 1f);
                dnCam.fieldOfView = 42f;
                dnCam.nearClipPlane = 0.1f;
                dnCam.farClipPlane = 50f;
                dnCam.targetTexture = rt;
                dnCam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                byte[] dnPng = ImageConversion.EncodeToPNG(tex);
                string dnOutPath = "/tmp/dvergr_deepnorth_validation.png";
                File.WriteAllBytes(dnOutPath, dnPng);
                Log.LogInfo($"=== Saved Deep North validation render ({dnPng.Length} bytes) to {dnOutPath} ===");

                // Close-up of Deep North Mage left palm holding Northern Vengeance sphere
                dnCamObj.transform.localPosition = new Vector3(1.85f, 0.72f, -1.35f);
                dnCamObj.transform.LookAt(dnStage.transform.position + new Vector3(2.15f, 0.55f, 0f));
                dnCam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes("/tmp/dvergr_deepnorth_palm.png", ImageConversion.EncodeToPNG(tex));
                Log.LogInfo("=== Saved Deep North Mage palm close-up to /tmp/dvergr_deepnorth_palm.png ===");
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
        internal bool IsDeepNorth;

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
            bool isDeepNorthRanger = objName.StartsWith("DvergerDeepNorth", StringComparison.OrdinalIgnoreCase);
            bool isStandardAshlandsRanger = objName.StartsWith("DvergerAshlands", StringComparison.OrdinalIgnoreCase);
            bool isClassicRogue = objName.StartsWith("Dverger", StringComparison.OrdinalIgnoreCase) &&
                                  !isStandardAshlandsRanger &&
                                  !isDeepNorthRanger &&
                                  objName.IndexOf("Mage", StringComparison.OrdinalIgnoreCase) < 0;

            if (!isDeepNorthRanger && !isStandardAshlandsRanger && !isClassicRogue)
            {
                return false;
            }

            ZDO zdo = _nview.GetZDO();
            if (zdo == null || zdo.GetBool("AshlandsDvergrMod_VarietyChecked"))
            {
                return false;
            }
            zdo.Set("AshlandsDvergrMod_VarietyChecked", true);

            string targetPrefabName = isDeepNorthRanger
                ? AshlandsDvergrPlugin.RollDeepNorthPrefabForSpawn()
                : AshlandsDvergrPlugin.RollVarietyPrefabForSpawn();
            if ((isDeepNorthRanger && string.Equals(targetPrefabName, "DvergerDeepNorth", StringComparison.OrdinalIgnoreCase)) ||
                (isStandardAshlandsRanger && string.Equals(targetPrefabName, "DvergerAshlands", StringComparison.OrdinalIgnoreCase)))
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
            if (isDeepNorthRanger)
            {
                AshlandsDvergrPlugin.InsideDeepNorthSpawn = true;
            }
            GameObject newObj;
            try
            {
                newObj = UnityEngine.Object.Instantiate(altPrefab, pos, rot);
            }
            finally
            {
                AshlandsDvergrPlugin.PendingVarietySpawnLevel = 0;
                if (isDeepNorthRanger)
                {
                    AshlandsDvergrPlugin.InsideDeepNorthSpawn = false;
                }
            }

            if (newObj == null)
            {
                return false;
            }

            ZNetView newNview = newObj.GetComponent<ZNetView>();
            if (newNview != null && newNview.IsValid() && newNview.GetZDO() != null)
            {
                ZDO newZdo = newNview.GetZDO();
                if (isDeepNorthRanger)
                {
                    newZdo.Set("AshlandsDvergrMod_IsDeepNorth", true);
                }
                else
                {
                    newZdo.Set("AshlandsDvergrMod_IsAshlands", true);
                }
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
            bool isDeepNorth = AshlandsDvergrPlugin.IsDeepNorthDvergr(hum);
            bool isMage = AshlandsDvergrPlugin.IsDvergrMageName(charName);
            bool isSupportMage = AshlandsDvergrPlugin.IsDvergrSupportMageName(charName);

            if (isDeepNorth)
            {
                int dnLevel = Mathf.Max(1, hum.GetLevel());
                if (isMage)
                {
                    // Deep North Mage tiers (NEVER casts healing):
                    //   0★ (level 1): Staff of Protection (StaffShield)
                    //   1★ (level 2): Staff of Protection + Northern Vengeance sphere (StaffFrostOrbs)
                    //   2★ (level 3+): ONLY Thunderblood (StaffThunderBlood / Lightning Strike)
                    List<ItemDrop.ItemData> toRemove = new List<ItemDrop.ItemData>();
                    for (int i = 0; i < items.Count; i++)
                    {
                        ItemDrop.ItemData item = items[i];
                        string dn = item?.m_dropPrefab != null ? item.m_dropPrefab.name : (item?.m_shared?.m_name ?? string.Empty);
                        if (dn.IndexOf("_heal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (dnLevel >= 3 && (dn.IndexOf("Support_buff", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              dn.IndexOf("_buff", StringComparison.OrdinalIgnoreCase) >= 0)))
                        {
                            toRemove.Add(item);
                        }
                    }
                    foreach (ItemDrop.ItemData remItem in toRemove)
                    {
                        inv.RemoveItem(remItem);
                    }

                    ItemDrop shieldDrop = dnLevel < 3 ? AshlandsDvergrPlugin.ResolvePrefabByNameOrAssetId("StaffShield")?.GetComponent<ItemDrop>() : null;
                    ItemDrop orbsDrop = dnLevel == 2 ? AshlandsDvergrPlugin.ResolvePrefabByNameOrAssetId("StaffFrostOrbs")?.GetComponent<ItemDrop>() : null;
                    ItemDrop tbDrop = dnLevel >= 3 ? AshlandsDvergrPlugin.ResolvePrefabByNameOrAssetId("StaffThunderBlood")?.GetComponent<ItemDrop>() : null;
                    ItemDrop.ItemData.SharedData shieldShared = shieldDrop?.m_itemData?.m_shared;
                    ItemDrop.ItemData.SharedData orbsShared = orbsDrop?.m_itemData?.m_shared;
                    ItemDrop.ItemData.SharedData tbShared = tbDrop?.m_itemData?.m_shared;

                    items = inv.GetAllItems();
                    for (int i = 0; i < items.Count; i++)
                    {
                        ItemDrop.ItemData item = items[i];
                        if (item?.m_shared?.m_attack == null) continue;
                        string dn = item.m_dropPrefab != null ? item.m_dropPrefab.name : (item.m_shared.m_name ?? string.Empty);

                        if (dnLevel < 3 && shieldShared?.m_attack != null &&
                            (dn.IndexOf("Support_buff", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             dn.IndexOf("_buff", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            ItemDrop.ItemData.SharedData clonedShared = (ItemDrop.ItemData.SharedData)ShallowCloneSharedData(item.m_shared);
                            Attack clonedAtk = clonedShared.m_attack.Clone();
                            if (shieldShared.m_attack.m_attackProjectile != null)
                            {
                                clonedAtk.m_attackProjectile = shieldShared.m_attack.m_attackProjectile;
                            }
                            clonedAtk.m_attackAnimation = "attack_protect";
                            clonedAtk.m_attackEitr = 0f;
                            clonedAtk.m_attackHealth = 0f;
                            clonedAtk.m_attackHealthPercentage = 0f;
                            clonedAtk.m_attackStamina = 0f;
                            clonedShared.m_attack = clonedAtk;
                            if (shieldShared.m_attackStatusEffect != null)
                            {
                                clonedShared.m_attackStatusEffect = shieldShared.m_attackStatusEffect;
                            }
                            if (shieldShared.m_startEffect?.m_effectPrefabs != null && shieldShared.m_startEffect.m_effectPrefabs.Length > 0)
                            {
                                clonedShared.m_startEffect = shieldShared.m_startEffect;
                            }
                            if (shieldShared.m_triggerEffect?.m_effectPrefabs != null && shieldShared.m_triggerEffect.m_effectPrefabs.Length > 0)
                            {
                                clonedShared.m_triggerEffect = shieldShared.m_triggerEffect;
                            }
                            clonedShared.m_aiAttackRange = 15f;
                            clonedShared.m_aiAttackInterval = 20f;
                            clonedShared.m_aiPrioritized = true;
                            item.m_shared = clonedShared;
                        }
                        else if (dnLevel == 2 && orbsShared?.m_attack != null &&
                                 dn.IndexOf("Mistile", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            ItemDrop.ItemData.SharedData clonedShared = (ItemDrop.ItemData.SharedData)ShallowCloneSharedData(item.m_shared);
                            Attack clonedAtk = clonedShared.m_attack.Clone();
                            if (orbsShared.m_attack.m_attackProjectile != null)
                            {
                                clonedAtk.m_attackProjectile = orbsShared.m_attack.m_attackProjectile;
                            }
                            // Uses 'attack_heal' trigger whose 'Staff Magic Heal' clip is overridden with Player's 'FrostShield' animation
                            clonedAtk.m_attackAnimation = "attack_heal";
                            clonedAtk.m_projectiles = Mathf.Max(1, orbsShared.m_attack.m_projectiles);
                            clonedAtk.m_burstInterval = orbsShared.m_attack.m_burstInterval;
                            clonedAtk.m_projectileAccuracy = orbsShared.m_attack.m_projectileAccuracy;
                            clonedAtk.m_projectileAccuracyMin = orbsShared.m_attack.m_projectileAccuracyMin;
                            clonedAtk.m_projectileVel = orbsShared.m_attack.m_projectileVel;
                            clonedAtk.m_projectileVelMin = orbsShared.m_attack.m_projectileVelMin;
                            clonedAtk.m_attackEitr = 0f;
                            clonedAtk.m_attackHealth = 0f;
                            clonedAtk.m_attackHealthPercentage = 0f;
                            clonedAtk.m_attackStamina = 0f;
                            clonedShared.m_attack = clonedAtk;
                            clonedShared.m_damages = orbsShared.m_damages;
                            if (orbsShared.m_attackStatusEffect != null)
                            {
                                clonedShared.m_attackStatusEffect = orbsShared.m_attackStatusEffect;
                            }
                            if (orbsShared.m_startEffect?.m_effectPrefabs != null && orbsShared.m_startEffect.m_effectPrefabs.Length > 0)
                            {
                                clonedShared.m_startEffect = orbsShared.m_startEffect;
                            }
                            if (orbsShared.m_triggerEffect?.m_effectPrefabs != null && orbsShared.m_triggerEffect.m_effectPrefabs.Length > 0)
                            {
                                clonedShared.m_triggerEffect = orbsShared.m_triggerEffect;
                            }
                            clonedShared.m_aiAttackRange = 16f;
                            clonedShared.m_aiAttackInterval = 20f;
                            clonedShared.m_aiPrioritized = false;
                            clonedShared.m_aiAttackMaxAngle = 35f;
                            item.m_shared = clonedShared;
                        }
                        else if (dnLevel >= 3 && tbShared?.m_attack != null &&
                                 dn.IndexOf("Mistile", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            ItemDrop.ItemData.SharedData clonedShared = (ItemDrop.ItemData.SharedData)ShallowCloneSharedData(item.m_shared);
                            Attack clonedAtk = clonedShared.m_attack.Clone();
                            if (tbShared.m_attack.m_attackProjectile != null)
                            {
                                clonedAtk.m_attackProjectile = tbShared.m_attack.m_attackProjectile;
                            }
                            clonedAtk.m_attackAnimation = "attack_protect";
                            clonedAtk.m_projectiles = Mathf.Max(1, tbShared.m_attack.m_projectiles);
                            clonedAtk.m_burstInterval = tbShared.m_attack.m_burstInterval;
                            clonedAtk.m_projectileAccuracy = tbShared.m_attack.m_projectileAccuracy;
                            clonedAtk.m_projectileAccuracyMin = tbShared.m_attack.m_projectileAccuracyMin;
                            clonedAtk.m_projectileVel = tbShared.m_attack.m_projectileVel;
                            clonedAtk.m_projectileVelMin = tbShared.m_attack.m_projectileVelMin;
                            clonedAtk.m_attackEitr = 0f;
                            clonedAtk.m_attackHealth = 0f;
                            clonedAtk.m_attackHealthPercentage = 0f;
                            clonedAtk.m_attackStamina = 0f;
                            clonedShared.m_attack = clonedAtk;
                            clonedShared.m_damages = GetWeaponDamageAtQuality(tbShared, 2);
                            if (tbShared.m_startEffect?.m_effectPrefabs != null && tbShared.m_startEffect.m_effectPrefabs.Length > 0)
                            {
                                clonedShared.m_startEffect = tbShared.m_startEffect;
                            }
                            if (tbShared.m_triggerEffect?.m_effectPrefabs != null && tbShared.m_triggerEffect.m_effectPrefabs.Length > 0)
                            {
                                clonedShared.m_triggerEffect = tbShared.m_triggerEffect;
                            }
                            clonedShared.m_aiAttackRange = 20f;
                            clonedShared.m_aiAttackInterval = 8f;
                            clonedShared.m_aiPrioritized = true;
                            clonedShared.m_aiAttackMaxAngle = 35f;
                            item.m_shared = clonedShared;
                        }
                    }
                }
                else
                {
                    // Deep North Ranger (DvergerDeepNorth): 0/1★ -> Nord Crossbow (CrossbowGold), 2★ -> Thunderblood Crossbow (CrossbowGold_BloodLightning)
                    string dnBowName = (dnLevel >= 3 && AshlandsDvergrPlugin.UseAshlandsBloodWeapons.Value)
                        ? "CrossbowGold_BloodLightning"
                        : "CrossbowGold";
                    ItemDrop nordDrop = AshlandsDvergrPlugin.ResolvePrefabByNameOrAssetId(dnBowName)?.GetComponent<ItemDrop>();
                    ItemDrop.ItemData.SharedData nordShared = nordDrop?.m_itemData?.m_shared;
                    if (nordShared != null)
                    {
                        for (int i = 0; i < items.Count; i++)
                        {
                            ItemDrop.ItemData item = items[i];
                            if (item?.m_shared?.m_attack == null) continue;
                            string dn = item.m_dropPrefab != null ? item.m_dropPrefab.name : (item.m_shared.m_name ?? string.Empty);
                            if (dn.IndexOf("Arbalest_shoot", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                ItemDrop.ItemData.SharedData clonedShared = (ItemDrop.ItemData.SharedData)ShallowCloneSharedData(item.m_shared);
                                Attack clonedAtk = clonedShared.m_attack.Clone();
                                clonedAtk.m_attackEitr = 0f;
                                clonedAtk.m_attackHealth = 0f;
                                clonedAtk.m_attackHealthPercentage = 0f;
                                clonedShared.m_attack = clonedAtk;
                                HitData.DamageTypes nordDmg = GetWeaponDamageAtQuality(nordShared, dnLevel >= 3 ? 2 : 1);
                                clonedShared.m_damages.m_pierce = Mathf.Max(clonedShared.m_damages.m_pierce, nordDmg.m_pierce);
                                if (nordDmg.m_lightning > 0f)
                                {
                                    clonedShared.m_damages.m_lightning = Mathf.Max(clonedShared.m_damages.m_lightning, nordDmg.m_lightning);
                                }
                                if (nordShared.m_startEffect?.m_effectPrefabs != null && nordShared.m_startEffect.m_effectPrefabs.Length > 0)
                                {
                                    clonedShared.m_startEffect = nordShared.m_startEffect;
                                }
                                if (nordShared.m_triggerEffect?.m_effectPrefabs != null && nordShared.m_triggerEffect.m_effectPrefabs.Length > 0)
                                {
                                    clonedShared.m_triggerEffect = nordShared.m_triggerEffect;
                                }
                                item.m_shared = clonedShared;
                            }
                        }
                    }
                }
                return;
            }

            int level = Mathf.Max(1, hum.GetLevel());
            bool useAshlandsWep = AshlandsDvergrPlugin.UseAshlandsBloodWeapons.Value && (level >= 3 || (isMage && !isSupportMage));
            if (!useAshlandsWep || isSupportMage)
            {
                return;
            }

            string wepPrefabName = isDeepNorth
                ? (isMage ? "StaffShield" : "CrossbowGold")
                : AshlandsDvergrPlugin.GetEquivalentBloodWeaponPrefabName(charName);
            GameObject wepPrefab = AshlandsDvergrPlugin.ResolvePrefabByNameOrAssetId(wepPrefabName);
            ItemDrop wepDrop = wepPrefab != null ? wepPrefab.GetComponent<ItemDrop>() : null;
            if (wepDrop == null || wepDrop.m_itemData == null || wepDrop.m_itemData.m_shared == null)
            {
                return;
            }

            ItemDrop.ItemData.SharedData srcShared = wepDrop.m_itemData.m_shared;
            HitData.DamageTypes srcDmg = GetWeaponDamageAtQuality(srcShared, level >= 3 ? 2 : 1);
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
                    clonedShared.m_damages = srcDmg;
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
                    clonedShared.m_damages = srcDmg;
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
                    clonedShared.m_damages = srcDmg;
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
                    clonedShared.m_damages.m_pierce = Mathf.Max(clonedShared.m_damages.m_pierce, srcDmg.m_pierce);
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

        private static HitData.DamageTypes GetWeaponDamageAtQuality(ItemDrop.ItemData.SharedData shared, int quality)
        {
            HitData.DamageTypes dmg = shared.m_damages;
            for (int q = 1; q < quality; q++)
            {
                dmg.Add(shared.m_damagesPerLevel);
            }
            return dmg;
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
            bool isDeepNorth = ch != null
                ? AshlandsDvergrPlugin.IsDeepNorthDvergr(ch)
                : (AshlandsDvergrPlugin.InsideDeepNorthSpawn || charName.IndexOf("DeepNorth", StringComparison.OrdinalIgnoreCase) >= 0);
            bool isMage = AshlandsDvergrPlugin.IsDvergrMageName(charName);
            bool isSupportMage = AshlandsDvergrPlugin.IsDvergrSupportMageName(charName);
            bool useBloodWep = isDeepNorth || (AshlandsDvergrPlugin.UseAshlandsBloodWeapons.Value && (level >= 3 || (isMage && !isSupportMage)));
            Transform rightHand = visEq != null && visEq.m_rightHand != null
                ? visEq.m_rightHand
                : FindBone(root, visEq, "r.hand", "RightHand_Attach", "RightHand");
            if (rightHand == null)
            {
                return;
            }

            Transform existingBloodWep = rightHand.Find("AshlandsDvergr_BloodWeapon");
            if (!useBloodWep || (!isDeepNorth && isSupportMage))
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

            string wepPrefabName = isDeepNorth
                ? (isMage ? (level >= 3 ? "StaffThunderBlood" : "StaffShield") : ((level >= 3 && AshlandsDvergrPlugin.UseAshlandsBloodWeapons.Value) ? "CrossbowGold_BloodLightning" : "CrossbowGold"))
                : AshlandsDvergrPlugin.GetEquivalentBloodWeaponPrefabName(charName);

            // Ensure left-hand Northern Vengeance sphere ONLY appears on 1-star (level == 2) Deep North Mages
            if (isDeepNorth && isMage)
            {
                Transform lhCheck = visEq != null && visEq.m_leftHand != null
                    ? visEq.m_leftHand
                    : FindBone(root, visEq, "l.hand", "LeftHand_Attach", "LeftHand");
                Transform existingLeftWep = lhCheck != null ? lhCheck.Find("AshlandsDvergr_LeftWeapon") : null;
                if (level != 2 && existingLeftWep != null)
                {
                    existingLeftWep.gameObject.SetActive(false);
                }
                else if (level == 2 && existingLeftWep != null)
                {
                    existingLeftWep.gameObject.SetActive(true);
                }
            }

            if (existingBloodWep != null)
            {
                if (existingBloodWep.Find("__tag_" + wepPrefabName) != null)
                {
                    existingBloodWep.gameObject.SetActive(true);
                    return;
                }
                UnityEngine.Object.DestroyImmediate(existingBloodWep.gameObject);
            }

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
            new GameObject("__tag_" + wepPrefabName).transform.SetParent(wepInst.transform, false);
            wepInst.transform.SetParent(rightHand, worldPositionStays: true);
            float s = wepInst.transform.localScale.x;
            if (string.Equals(wepPrefabName, "CrossbowRipperBlood", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(wepPrefabName, "CrossbowGold", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(wepPrefabName, "CrossbowGold_BloodLightning", StringComparison.OrdinalIgnoreCase))
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
                     string.Equals(wepPrefabName, "StaffIceShards", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(wepPrefabName, "StaffShield", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(wepPrefabName, "StaffFrostOrbs", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(wepPrefabName, "StaffThunderBlood", StringComparison.OrdinalIgnoreCase))
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

            // ONLY 1-star (level == 2) Deep North Mage dual-wields Northern Vengeance frost orb in l.hand
            if (isDeepNorth && isMage && level == 2)
            {
                Transform leftHand = visEq != null && visEq.m_leftHand != null
                    ? visEq.m_leftHand
                    : FindBone(root, visEq, "l.hand", "LeftHand_Attach", "LeftHand");
                if (leftHand != null && leftHand.Find("AshlandsDvergr_LeftWeapon") == null)
                {
                    GameObject orbPrefab = AshlandsDvergrPlugin.ResolvePrefabByNameOrAssetId("StaffFrostOrbs");
                    Transform orbAttach = orbPrefab != null ? (orbPrefab.transform.Find("attach") ?? orbPrefab.transform.Find("attach_l.hand")) : null;
                    if (orbAttach != null)
                    {
                        GameObject orbInst = UnityEngine.Object.Instantiate(orbAttach.gameObject, leftHand.position, leftHand.rotation);
                        orbInst.name = "AshlandsDvergr_LeftWeapon";
                        orbInst.transform.SetParent(leftHand, worldPositionStays: true);
                        float ls = orbInst.transform.localScale.x;
                        // Center StaffFrostOrbs mesh dead-center in l.hand palm with clean clearance so nothing pokes through back of hand
                        orbInst.transform.localScale = Vector3.one * (ls * 0.65f);
                        orbInst.transform.localPosition = new Vector3(0.005f * ls, 0.015f * ls, -0.11f * ls);
                        orbInst.transform.localRotation = Quaternion.identity;
                        orbInst.SetActive(true);
                        foreach (Collider col in orbInst.GetComponentsInChildren<Collider>(includeInactive: true)) col.enabled = false;
                        foreach (LODGroup lod in orbInst.GetComponentsInChildren<LODGroup>(includeInactive: true)) lod.enabled = false;
                    }
                }
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
        public bool? ReadOnly;
    }
}
