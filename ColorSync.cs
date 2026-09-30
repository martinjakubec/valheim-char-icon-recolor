using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MinimapPlayerColor
{
    /// <summary>
    /// Broadcasts the local player's colour and tracks the colours announced by other players.
    /// Colours are keyed by the sender's network session ID, which matches ZDOID.UserID of their character.
    /// </summary>
    internal struct PlayerColors
    {
        /// <summary>Colour of the person in the pin icon and of the name label; null keeps the game's colour.</summary>
        public Color? Person;

        /// <summary>Colour of the sword and shield in the pin icon; null keeps the game's colour.</summary>
        public Color? Gear;

        public bool IsVanilla => !Person.HasValue && !Gear.HasValue;

        public bool Equals(PlayerColors other) => Nullable.Equals(Person, other.Person) && Nullable.Equals(Gear, other.Gear);
    }

    internal static class ColorSync
    {
        private const string RpcColor = "MinimapPlayerColor_Color";
        private const string RpcRequest = "MinimapPlayerColor_Request";

        private static readonly int RpcColorHash = RpcColor.GetStableHashCode();
        private static readonly int RpcRequestHash = RpcRequest.GetStableHashCode();

        // Upper bound on remembered colours, so a misbehaving client cannot grow the table without limit
        private const int MaxColors = 256;

        // Minimum seconds between our own broadcasts, however many requests or config changes arrive
        private const float BroadcastInterval = 2f;

        private static readonly Dictionary<long, PlayerColors> Colors = new Dictionary<long, PlayerColors>();

        private static bool _broadcastPending;
        private static float _nextBroadcast;

        public static bool TryGetColors(long userId, out PlayerColors colors) => Colors.TryGetValue(userId, out colors);

        public static bool TryParse(string hex, out Color color)
        {
            hex = (hex ?? "").Trim();
            if (!hex.StartsWith("#"))
            {
                hex = "#" + hex;
            }
            if (hex.Length == 7 && ColorUtility.TryParseHtmlString(hex, out color))
            {
                color.a = 1f;
                return true;
            }
            color = Color.white;
            return false;
        }

        /// <summary>Like <see cref="TryParse"/>, but an empty value is valid and means "not set".</summary>
        public static bool TryParseOptional(string hex, out Color? color)
        {
            color = null;
            if (string.IsNullOrWhiteSpace(hex))
            {
                return true;
            }
            if (hex.Length > 16 || !TryParse(hex, out Color parsed))
            {
                return false;
            }
            color = parsed;
            return true;
        }

        private static string ToHex(Color? color) => color.HasValue ? "#" + ColorUtility.ToHtmlStringRGB(color.Value) : "";

        /// <summary>Queue a broadcast of our colour; sent from <see cref="Tick"/> at a limited rate.</summary>
        public static void BroadcastOwnColor()
        {
            _broadcastPending = true;
        }

        /// <summary>Called every frame from the plugin.</summary>
        public static void Tick()
        {
            if (!_broadcastPending || Time.unscaledTime < _nextBroadcast)
            {
                return;
            }
            _broadcastPending = false;
            if (ZRoutedRpc.instance == null || ZNet.instance == null)
            {
                return;
            }
            _nextBroadcast = Time.unscaledTime + BroadcastInterval;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcColor, ToHex(Plugin.OwnColors.Person), ToHex(Plugin.OwnColors.Gear));
        }

        private static void RPC_Color(long sender, string personHex, string gearHex)
        {
            PlayerColors colors;
            if (!TryParseOptional(personHex, out colors.Person) || !TryParseOptional(gearHex, out colors.Gear))
            {
                return;
            }
            bool changed;
            // Nothing set means the sender went back to the game's default colours
            if (colors.IsVanilla)
            {
                changed = Colors.Remove(sender);
            }
            else
            {
                bool known = Colors.TryGetValue(sender, out PlayerColors old);
                if (!known && Colors.Count >= MaxColors && !PruneAbsentPlayers())
                {
                    return;
                }
                changed = !known || !old.Equals(colors);
                Colors[sender] = colors;
            }
            // Pins are only re-laid out (and re-coloured) on demand
            if (changed && Minimap.instance != null)
            {
                Minimap.instance.m_pinUpdateRequired = true;
            }
        }

        /// <summary>Drops colours of players no longer in the world. Returns true if there is room afterwards.</summary>
        private static bool PruneAbsentPlayers()
        {
            if (ZNet.instance == null)
            {
                return false;
            }
            HashSet<long> present = new HashSet<long>(ZNet.instance.m_players.Select(p => p.m_characterID.UserID));
            foreach (long id in Colors.Keys.Where(id => !present.Contains(id)).ToList())
            {
                Colors.Remove(id);
            }
            return Colors.Count < MaxColors;
        }

        private static void RPC_Request(long sender)
        {
            if (sender != ZNet.GetUID())
            {
                BroadcastOwnColor();
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        private static class ZNet_Awake_Patch
        {
            private static void Postfix()
            {
                Colors.Clear();
                _broadcastPending = false;
                _nextBroadcast = 0f;
                ZRoutedRpc.instance.Register<string, string>(RpcColor, RPC_Color);
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

        // The game trusts the sender ID written by the client. When we are the host, drop our
        // messages if that ID does not belong to the connection they arrived on, so a player
        // cannot set someone else's colour.
        [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.RPC_RoutedRPC))]
        private static class ZRoutedRpc_RPC_RoutedRPC_Patch
        {
            private static bool Prefix(ZRoutedRpc __instance, ZRpc rpc, ZPackage pkg)
            {
                if (!__instance.m_server)
                {
                    return true;
                }
                long sender;
                int methodHash;
                try
                {
                    // Header layout of ZRoutedRpc.RoutedRPCData
                    pkg.ReadLong();
                    sender = pkg.ReadLong();
                    pkg.ReadLong();
                    pkg.ReadZDOID();
                    methodHash = pkg.ReadInt();
                }
                catch (System.Exception)
                {
                    // Malformed packet: let the game deal with it as it normally would
                    pkg.SetPos(0);
                    return true;
                }
                pkg.SetPos(0);
                if (methodHash != RpcColorHash && methodHash != RpcRequestHash)
                {
                    return true;
                }
                foreach (ZNetPeer peer in __instance.m_peers)
                {
                    if (peer.m_rpc == rpc)
                    {
                        return peer.m_uid == sender;
                    }
                }
                return false;
            }
        }
    }
}
