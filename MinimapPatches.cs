using System.Collections.Generic;
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
                if (PinSprites.IsOverCapacity)
                {
                    PinSprites.Clear();
                }
                // m_playerPins and m_tempPlayerInfo are index-aligned (see Minimap.UpdatePlayerPins)
                int count = Mathf.Min(__instance.m_playerPins.Count, __instance.m_tempPlayerInfo.Count);
                for (int i = 0; i < count; i++)
                {
                    Minimap.PinData pin = __instance.m_playerPins[i];
                    if (pin.m_iconElement == null)
                    {
                        continue;
                    }
                    long userId = __instance.m_tempPlayerInfo[i].m_characterID.UserID;
                    Sprite icon = pin.m_icon;
                    if (ColorSync.TryGetColors(userId, out PlayerColors colors))
                    {
                        icon = PinSprites.Get(pin.m_icon, colors) ?? pin.m_icon;
                        if (colors.Person.HasValue && pin.m_NamePinData != null && pin.m_NamePinData.PinNameText != null)
                        {
                            pin.m_NamePinData.PinNameText.color = colors.Person.Value;
                        }
                    }
                    // Also restores the game's icon once a player goes back to default colours
                    if (pin.m_iconElement.sprite != icon)
                    {
                        pin.m_iconElement.sprite = icon;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Start))]
        private static class Minimap_Start_Patch
        {
            private static void Postfix(Minimap __instance)
            {
                // Remember the game's own arrow colours so an empty config can restore them
                PinSprites.Clear();
                VanillaMarkerColors.Clear();
                Capture(__instance.m_smallMarker);
                Capture(__instance.m_largeMarker);
                ApplyOwnMarkerColor();
            }
        }

        private static readonly Dictionary<Image, Color> VanillaMarkerColors = new Dictionary<Image, Color>();

        private static void Capture(RectTransform marker)
        {
            if (marker == null)
            {
                return;
            }
            foreach (Image image in marker.GetComponentsInChildren<Image>(includeInactive: true))
            {
                VanillaMarkerColors[image] = image.color;
                Plugin.Log.LogInfo($"Vanilla colour of {image.name}: #{ColorUtility.ToHtmlStringRGBA(image.color)}");
            }
        }

        public static void ApplyOwnMarkerColor()
        {
            foreach (KeyValuePair<Image, Color> entry in VanillaMarkerColors)
            {
                if (entry.Key != null)
                {
                    entry.Key.color = Plugin.OwnColors.Person ?? entry.Value;
                }
            }
        }
    }
}
