using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace MinimapPlayerColor
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jakubecdev.minimapplayercolor";
        public const string PluginName = "MinimapPlayerColor";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        /// <summary>Last valid colours read from the config; unset parts use the game's own colours.</summary>
        internal static PlayerColors OwnColors;

        internal static ConfigEntry<bool> AllowAchievements;

        private static ConfigEntry<string> _colorConfig;
        private static ConfigEntry<string> _gearColorConfig;

        private readonly Harmony _harmony = new Harmony(PluginGuid);

        private void Awake()
        {
            Log = Logger;

            _colorConfig = Config.Bind("General", "Color", "",
                "Colour of the person in your map icon, your name label and your own arrow, as hex RRGGBB (e.g. #FF8800). Seen by other players who have this mod. Leave empty for the game's default.");
            _gearColorConfig = Config.Bind("General", "GearColor", "",
                "Colour of the sword and shield in your map icon, as hex RRGGBB. Seen by other players who have this mod. Leave empty for the game's default.");
            _colorConfig.SettingChanged += (_, __) => OnColorChanged();
            _gearColorConfig.SettingChanged += (_, __) => OnColorChanged();
            ReadColors();

            AllowAchievements = Config.Bind("General", "AllowAchievements", true,
                "The game disables achievements when any mod is loaded. Enable this to keep earning them; cheats still disable achievements as usual.");

            _harmony.PatchAll();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        // BepInEx does not re-read the config file on its own; poll so edits made
        // outside the game (r2modman, text editor) apply without a restart.
        private void Update()
        {
            ColorSync.Tick();

            if (Time.unscaledTime < _nextConfigCheck)
            {
                return;
            }
            _nextConfigCheck = Time.unscaledTime + 1f;

            try
            {
                System.DateTime written = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);
                if (written != _configWritten)
                {
                    if (_configWritten != default)
                    {
                        Config.Reload();
                    }
                    _configWritten = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);
                }
            }
            catch (System.Exception e)
            {
                // The file can be locked or half-written while another program saves it; retry on the next check
                Log.LogDebug($"Config reload skipped: {e.Message}");
            }
        }

        private float _nextConfigCheck;
        private System.DateTime _configWritten;

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }

        private static void OnColorChanged()
        {
            ReadColors();
            MinimapPatches.ApplyOwnMarkerColor();
            ColorSync.BroadcastOwnColor();
        }

        private static void ReadColors()
        {
            Read(_colorConfig, ref OwnColors.Person);
            Read(_gearColorConfig, ref OwnColors.Gear);
        }

        private static void Read(ConfigEntry<string> entry, ref Color? target)
        {
            if (ColorSync.TryParseOptional(entry.Value, out Color? color))
            {
                target = color;
                return;
            }
            Log.LogWarning($"Invalid {entry.Definition.Key} '{entry.Value}', expected hex like #FF8800. Keeping the previous colour.");
        }
    }
}
