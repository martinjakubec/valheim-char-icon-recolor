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
        public const string PluginGuid = "com.martinkubo.minimapplayercolor";
        public const string PluginName = "MinimapPlayerColor";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        /// <summary>Last valid colour read from the config.</summary>
        internal static Color OwnColor = Color.white;

        private static ConfigEntry<string> _colorConfig;

        private readonly Harmony _harmony = new Harmony(PluginGuid);

        private void Awake()
        {
            Log = Logger;

            _colorConfig = Config.Bind("General", "Color", "#FFFFFF",
                "Colour of your player icon on the map, as hex RRGGBB (e.g. #FF8800). Seen by other players who have this mod.");
            _colorConfig.SettingChanged += (_, __) => OnColorChanged();
            ReadColor();

            _harmony.PatchAll();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        // BepInEx does not re-read the config file on its own; poll so edits made
        // outside the game (r2modman, text editor) apply without a restart.
        private void Update()
        {
            if (Time.unscaledTime < _nextConfigCheck)
            {
                return;
            }
            _nextConfigCheck = Time.unscaledTime + 1f;

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

        private float _nextConfigCheck;
        private System.DateTime _configWritten;

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }

        private static void OnColorChanged()
        {
            if (ReadColor())
            {
                MinimapPatches.ApplyOwnMarkerColor();
                ColorSync.BroadcastOwnColor();
            }
        }

        private static bool ReadColor()
        {
            if (ColorSync.TryParse(_colorConfig.Value, out Color color))
            {
                OwnColor = color;
                return true;
            }
            Log.LogWarning($"Invalid colour '{_colorConfig.Value}', expected hex like #FF8800. Keeping the previous colour.");
            return false;
        }
    }
}
