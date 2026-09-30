using HarmonyLib;

namespace MinimapPlayerColor
{
    /// <summary>
    /// The game blocks achievements whenever Game.isModded is set (BepInEx sets it).
    /// Hide the flag only while the cheat check runs, so the real cheat checks
    /// (dev commands, cheated items, world modifiers) and the "modded" menu label still work.
    /// </summary>
    [HarmonyPatch(typeof(Achievements), nameof(Achievements.IsCheatedAtAll))]
    internal static class Achievements_IsCheatedAtAll_Patch
    {
        private static void Prefix(out bool __state)
        {
            __state = Game.isModded;
            if (Plugin.AllowAchievements.Value)
            {
                Game.isModded = false;
            }
        }

        private static void Finalizer(bool __state)
        {
            Game.isModded = __state;
        }
    }
}
