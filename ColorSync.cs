using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MinimapPlayerColor
{
    /// <summary>
    /// Broadcasts the local player's colour and tracks the colours announced by other players.
    /// Colours are keyed by the sender's network session ID, which matches ZDOID.UserID of their character.
    /// </summary>
    internal static class ColorSync
    {
        private const string RpcColor = "MinimapPlayerColor_Color";
        private const string RpcRequest = "MinimapPlayerColor_Request";

        private static readonly Dictionary<long, Color> Colors = new Dictionary<long, Color>();

        public static bool TryGetColor(long userId, out Color color) => Colors.TryGetValue(userId, out color);

        public static bool TryParse(string hex, out Color color)
        {
            hex = (hex ?? "").Trim();
            if (!hex.StartsWith("#"))
            {
                hex = "#" + hex;
            }
            if (hex.Length == 7 && ColorUtility.TryParseHtmlString(hex, out color))
            {
                return true;
            }
            color = Color.white;
            return false;
        }

        public static void BroadcastOwnColor()
        {
            Send(ZRoutedRpc.Everybody);
        }

        private static void Send(long target)
        {
            if (ZRoutedRpc.instance == null || ZNet.instance == null)
            {
                return;
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(target, RpcColor, "#" + ColorUtility.ToHtmlStringRGB(Plugin.OwnColor));
        }

        private static void RPC_Color(long sender, string hex)
        {
            if (TryParse(hex, out Color color))
            {
                Colors[sender] = color;
                // Pins are only re-laid out (and re-tinted) on demand
                if (Minimap.instance != null)
                {
                    Minimap.instance.m_pinUpdateRequired = true;
                }
            }
        }

        private static void RPC_Request(long sender)
        {
            if (sender != ZNet.GetUID())
            {
                Send(sender);
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        private static class ZNet_Awake_Patch
        {
            private static void Postfix()
            {
                Colors.Clear();
                ZRoutedRpc.instance.Register<string>(RpcColor, RPC_Color);
                ZRoutedRpc.instance.Register(RpcRequest, RPC_Request);
            }
        }

        // Announce our colour and ask everyone already in the world for theirs.
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class Player_OnSpawned_Patch
        {
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || ZRoutedRpc.instance == null)
                {
                    return;
                }
                BroadcastOwnColor();
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcRequest);
            }
        }
    }
}
