using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace MinimapPlayerColor
{
    internal static class MinimapPatches
    {
        // The game resets pin colours every time it lays out pins, so tint right after it.
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePins))]
        private static class Minimap_UpdatePins_Patch
        {
            private static void Postfix(Minimap __instance)
            {
                // m_playerPins and m_tempPlayerInfo are index-aligned (see Minimap.UpdatePlayerPins)
                int count = Mathf.Min(__instance.m_playerPins.Count, __instance.m_tempPlayerInfo.Count);
                for (int i = 0; i < count; i++)
                {
                    Minimap.PinData pin = __instance.m_playerPins[i];
                    long userId = __instance.m_tempPlayerInfo[i].m_characterID.UserID;
                    if (pin.m_iconElement == null || !ColorSync.TryGetColor(userId, out Color color))
                    {
                        continue;
                    }
                    pin.m_iconElement.color = color;
                    if (pin.m_NamePinData != null && pin.m_NamePinData.PinNameText != null)
                    {
                        pin.m_NamePinData.PinNameText.color = color;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Start))]
        private static class Minimap_Start_Patch
        {
            private static void Postfix()
            {
                ApplyOwnMarkerColor();
            }
        }

        public static void ApplyOwnMarkerColor()
        {
            Minimap minimap = Minimap.instance;
            if (minimap == null)
            {
                return;
            }
            Tint(minimap.m_smallMarker);
            Tint(minimap.m_largeMarker);
        }

        private static void Tint(RectTransform marker)
        {
            if (marker == null)
            {
                return;
            }
            foreach (Image image in marker.GetComponentsInChildren<Image>(includeInactive: true))
            {
                image.color = Plugin.OwnColor;
            }
        }
    }
}
