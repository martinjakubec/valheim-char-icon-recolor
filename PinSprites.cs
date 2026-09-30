using System;
using System.Collections.Generic;
using UnityEngine;

namespace MinimapPlayerColor
{
    /// <summary>
    /// Builds recoloured copies of the player pin icon. The vanilla icon is a red person with a
    /// white sword and shield, so tinting the whole image cannot colour the two parts separately.
    /// Each pixel is split into a "white" part (gear) and a "red" part (person) and rebuilt from
    /// the chosen colours; black outlines and transparency are left untouched.
    /// </summary>
    internal static class PinSprites
    {
        private const int MaxCached = 64;

        private struct Key : IEquatable<Key>
        {
            public int Source;
            public int Person;
            public int Gear;

            public bool Equals(Key other) => Source == other.Source && Person == other.Person && Gear == other.Gear;
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode() => (Source * 397 ^ Person) * 397 ^ Gear;
        }

        private class SourcePixels
        {
            public Color[] Pixels;
            public int Width;
            public int Height;
            public float MaxRed;
        }

        private static readonly Dictionary<Key, Sprite> Cache = new Dictionary<Key, Sprite>();
        private static readonly Dictionary<int, SourcePixels> Sources = new Dictionary<int, SourcePixels>();

        public static bool IsOverCapacity => Cache.Count > MaxCached;

        /// <summary>Returns a recoloured copy of <paramref name="source"/>, or null if it could not be built.</summary>
        public static Sprite Get(Sprite source, PlayerColors colors)
        {
            if (source == null)
            {
                return null;
            }
            Key key = new Key { Source = source.GetInstanceID(), Person = Pack(colors.Person), Gear = Pack(colors.Gear) };
            if (Cache.TryGetValue(key, out Sprite sprite))
            {
                return sprite;
            }
            try
            {
                sprite = Build(source, colors);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not recolour pin icon: {e.Message}");
                sprite = null;
            }
            // Failures are cached too, so a broken icon is not retried every frame
            Cache[key] = sprite;
            return sprite;
        }

        public static void Clear()
        {
            foreach (Sprite sprite in Cache.Values)
            {
                if (sprite != null)
                {
                    UnityEngine.Object.Destroy(sprite.texture);
                    UnityEngine.Object.Destroy(sprite);
                }
            }
            Cache.Clear();
            Sources.Clear();
        }

        // -1 marks "not set"; real colours have the top byte clear
        private static int Pack(Color? color)
        {
            if (!color.HasValue)
            {
                return -1;
            }
            Color32 c = color.Value;
            return (c.r << 16) | (c.g << 8) | c.b;
        }

        private static Sprite Build(Sprite source, PlayerColors colors)
        {
            SourcePixels src = GetSource(source);
            Color gear = colors.Gear ?? Color.white;
            Color[] pixels = new Color[src.Pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color p = src.Pixels[i];
                float white = Mathf.Min(p.r, Mathf.Min(p.g, p.b));
                float red = Mathf.Max(0f, p.r - Mathf.Max(p.g, p.b));
                // A chosen person colour is shown at full strength where the icon is reddest;
                // without one the original red is kept as is.
                Color person = colors.Person.HasValue ? colors.Person.Value * (red / src.MaxRed) : new Color(red, 0f, 0f);
                pixels[i] = new Color(
                    Mathf.Clamp01(white * gear.r + person.r),
                    Mathf.Clamp01(white * gear.g + person.g),
                    Mathf.Clamp01(white * gear.b + person.b),
                    p.a);
            }

            Texture2D texture = new Texture2D(src.Width, src.Height, TextureFormat.RGBA32, false);
            texture.filterMode = source.texture.filterMode;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, src.Width, src.Height), new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
        }

        private static SourcePixels GetSource(Sprite source)
        {
            int id = source.GetInstanceID();
            if (Sources.TryGetValue(id, out SourcePixels src))
            {
                return src;
            }

            // Game textures are not CPU-readable, so copy through a render texture first
            Texture2D texture = source.texture;
            RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Texture2D readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                readable.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
                readable.Apply();

                Rect rect = source.textureRect;
                src = new SourcePixels { Width = (int)rect.width, Height = (int)rect.height };
                src.Pixels = readable.GetPixels((int)rect.x, (int)rect.y, src.Width, src.Height);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(readable);
            }

            foreach (Color p in src.Pixels)
            {
                src.MaxRed = Mathf.Max(src.MaxRed, p.a > 0f ? p.r - Mathf.Max(p.g, p.b) : 0f);
            }
            // Avoid dividing by ~0 for an icon with no red in it
            src.MaxRed = Mathf.Max(src.MaxRed, 0.01f);

            Sources[id] = src;
            return src;
        }
    }
}
