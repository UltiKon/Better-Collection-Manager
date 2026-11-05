using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>Replaces the native logo with a centered asterisk in a private texture.</summary>
    internal sealed class ExpansionIconMarker
    {
        private sealed class Entry
        {
            public Texture Source;
            public Rect Region;
            public Texture2D Marked;
        }
        private readonly List<Entry> m_entries = new List<Entry>();
        private readonly Type m_graphicsType;
        private MethodInfo m_blit;
        private object[] m_blitArguments;
        private Material m_copyMaterial;

        internal ExpansionIconMarker(Type graphicsType = null)
        {
            m_graphicsType = graphicsType ?? typeof(Graphics);
        }

        internal Texture2D Get(Texture source, Renderer logo)
        {
            if (source == null || source.width <= 0 || source.height <= 0) return null;
            Rect region = TextureRegion(logo);
            foreach (Entry entry in m_entries)
                if (entry.Source == source && entry.Region.Equals(region)) return entry.Marked;

            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = null;
            Texture2D copy = null;
            try
            {
                // Native icon textures can discard their CPU pixel data.
                // Read a GPU copy once per icon/atlas region, never per frame.
                temporary = RenderTexture.GetTemporary(source.width, source.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                // An explicit sRGB target supplies the conversion policy;
                // Hearthstone does not expose GL.sRGBWrite.
                Blit(source, temporary);
                RenderTexture.active = temporary;
                copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                copy.name = source.name + "_BCM_Star";
                copy.filterMode = source.filterMode;
                copy.wrapMode = source.wrapMode;
                copy.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
                Color[] pixels = copy.GetPixels();
                Stamp(pixels, copy.width, copy.height, region);
                copy.SetPixels(pixels);
                copy.Apply(false, false);
                m_entries.Add(new Entry { Source = source, Region = region, Marked = copy });
                DebugLog.Msg("[Expansion star] star-only-v4 copied; source=" + source.name
                    + "; uv=" + region + "; size=" + source.width + "x" + source.height);
                return copy;
            }
            catch
            {
                if (copy != null) UnityEngine.Object.Destroy(copy);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
            }
        }

        internal void Blit(Texture source, RenderTexture destination)
        {
            if (m_blit == null)
            {
                m_blit = ResolveBlit(m_graphicsType, out m_blitArguments);
                DebugLog.Msg("[Expansion star] native texture copy: " + m_blit);
            }
            object[] arguments = (object[])m_blitArguments.Clone();
            arguments[0] = source;
            arguments[1] = destination;
            if (arguments.Length >= 3 && m_blit.GetParameters()[2].ParameterType == typeof(Material))
            {
                // Material overloads require a real copy material. Passing null
                // can hit a native [NotNull] binding in stripped Unity builds.
                if (m_copyMaterial == null)
                {
                    MethodInfo find = typeof(Shader).GetMethod("Find", BindingFlags.Public | BindingFlags.Static,
                        null, new[] { typeof(string) }, null);
                    Shader shader = find?.Invoke(null, new object[] { "Hidden/BlitCopy" }) as Shader;
                    if (shader == null) throw new InvalidOperationException("Native texture copy shader Hidden/BlitCopy is unavailable");
                    m_copyMaterial = (Material)Activator.CreateInstance(typeof(Material), new object[] { shader });
                }
                arguments[2] = m_copyMaterial;
            }
            try { m_blit.Invoke(null, arguments); }
            catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        }

        internal static MethodInfo ResolveBlit(Type graphicsType, out object[] arguments)
        {
            Type texture = typeof(Texture), target = typeof(RenderTexture), vector = typeof(Vector2), integer = typeof(int);
            Type[][] signatures = {
                new[] { texture, target },
                new[] { texture, target, vector, vector },
                new[] { texture, target, integer, integer },
                new[] { texture, target, vector, vector, integer, integer }
            };
            object[][] defaults = {
                new object[] { null, null },
                new object[] { null, null, new Vector2(1f, 1f), Vector2.zero },
                new object[] { null, null, 0, 0 },
                new object[] { null, null, new Vector2(1f, 1f), Vector2.zero, 0, 0 }
            };
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            // Public convenience overloads may be stripped from the game's
            // assemblies while their underlying native bindings remain.
            foreach (string name in new[] { "Blit", "Blit2", "Blit4", "Blit3", "Blit5" })
                for (int i = 0; i < signatures.Length; i++)
                {
                    MethodInfo method = graphicsType.GetMethod(name, flags, null, signatures[i], null);
                    if (method == null) continue;
                    arguments = defaults[i];
                    return method;
                }
            foreach (Type[] signature in new[] {
                new[] { texture, target, typeof(Material), integer },
                new[] { texture, target, typeof(Material) },
                new[] { texture, target, typeof(Material), integer, integer }
            })
            {
                MethodInfo method = graphicsType.GetMethod("Blit", flags, null, signature, null);
                if (method == null) continue;
                arguments = signature.Length == 3 ? new object[] { null, null, null }
                    : signature.Length == 4 ? new object[] { null, null, null, -1 }
                    : new object[] { null, null, null, -1, 0 };
                return method;
            }
            var available = new List<string>();
            foreach (MethodInfo method in graphicsType.GetMethods(flags))
                if (method.Name.StartsWith("Blit", StringComparison.Ordinal)) available.Add(method.ToString());
            throw new MissingMethodException("No supported native texture copy binding. Available: " + string.Join("; ", available));
        }

        private static Rect TextureRegion(Renderer logo)
        {
            Mesh mesh = logo.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return new Rect(0f, 0f, 1f, 1f);
            if (!mesh.isReadable) mesh = MixedPremiumMesh.GetReadableCopy(mesh);
            var uv = new List<Vector4>();
            mesh.GetUVs(0, uv);
            if (uv.Count == 0) return new Rect(0f, 0f, 1f, 1f);
            Vector2 scale = logo.sharedMaterial != null ? logo.sharedMaterial.mainTextureScale : new Vector2(1f, 1f);
            Vector2 offset = logo.sharedMaterial != null ? logo.sharedMaterial.mainTextureOffset : Vector2.zero;
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            foreach (Vector4 point in uv)
            {
                float u = point.x * scale.x + offset.x, v = point.y * scale.y + offset.y;
                left = Mathf.Min(left, u); right = Mathf.Max(right, u);
                bottom = Mathf.Min(bottom, v); top = Mathf.Max(top, v);
            }
            return Rect.MinMaxRect(Mathf.Clamp01(left), Mathf.Clamp01(bottom), Mathf.Clamp01(right), Mathf.Clamp01(top));
        }

        internal static void Stamp(Color[] pixels, int width, int height, Rect region)
        {
            if (pixels == null || pixels.Length != width * height) throw new ArgumentException("Invalid icon pixels");
            float regionWidth = region.width * width, regionHeight = region.height * height;
            float size = Mathf.Min(regionWidth, regionHeight) * 0.65f;
            if (size < 2f) throw new InvalidOperationException("Native icon UV region is too small for an asterisk");
            Color ink = FindInk(pixels, width, height, region);
            // Preserve native ink and atlas UVs, but remove the format logo.
            int cellLeft = Math.Max(0, (int)Math.Floor(region.xMin * width));
            int cellRight = Math.Min(width, (int)Math.Ceiling(region.xMax * width));
            int cellBottom = Math.Max(0, (int)Math.Floor(region.yMin * height));
            int cellTop = Math.Min(height, (int)Math.Ceiling(region.yMax * height));
            for (int y = cellBottom; y < cellTop; y++)
                for (int x = cellLeft; x < cellRight; x++) pixels[y * width + x] = default(Color);
            float radius = size * 0.5f, halfStroke = size * 0.075f;
            float reach = radius - halfStroke - 0.5f;
            float centerX = region.center.x * width;
            float centerY = region.center.y * height;
            int left = Math.Max(0, (int)Math.Floor(centerX - radius));
            int right = Math.Min(width - 1, (int)Math.Ceiling(centerX + radius));
            int bottom = Math.Max(0, (int)Math.Floor(centerY - radius));
            int top = Math.Min(height - 1, (int)Math.Ceiling(centerY + radius));
            for (int y = bottom; y <= top; y++)
                for (int x = left; x <= right; x++)
                {
                    float px = x + 0.5f - centerX, py = y + 0.5f - centerY;
                    // Mathematical asterisk: three straight bars at 0, 60
                    // and 120 degrees, yielding six equal rays.
                    float distance = Math.Min(StrokeDistance(px, py, 1f, 0f, reach),
                        Math.Min(StrokeDistance(px, py, 0.5f, 0.8660254f, reach),
                            StrokeDistance(px, py, -0.5f, 0.8660254f, reach)));
                    float alpha = Mathf.Clamp01(halfStroke + 0.5f - distance) * ink.a;
                    if (alpha <= 0f) continue;
                    Color old = pixels[y * width + x];
                    float oldAlpha = old.a * (1f - alpha), combined = alpha + oldAlpha;
                    pixels[y * width + x] = new Color((ink.r * alpha + old.r * oldAlpha) / combined,
                        (ink.g * alpha + old.g * oldAlpha) / combined,
                        (ink.b * alpha + old.b * oldAlpha) / combined, combined);
                }
        }

        private static float StrokeDistance(float x, float y, float dx, float dy, float reach)
        {
            float t = Mathf.Clamp(x * dx + y * dy, -reach, reach);
            double px = x - t * dx, py = y - t * dy;
            return (float)Math.Sqrt(px * px + py * py);
        }

        private static Color FindInk(Color[] pixels, int width, int height, Rect region)
        {
            var counts = new Dictionary<int, int>();
            Color best = new Color(64f / 255f, 52f / 255f, 36f / 255f, 1f);
            Color fallback = best;
            float greatestAlpha = 0f;
            int bestCount = 0;
            int left = (int)((region.xMin + region.width * 0.25f) * width);
            int right = (int)((region.xMin + region.width * 0.7f) * width);
            int bottom = (int)((region.yMin + region.height * 0.25f) * height);
            int top = (int)((region.yMin + region.height * 0.75f) * height);
            for (int y = bottom; y < top; y++)
                for (int x = left; x < right; x++)
                {
                    Color color = pixels[y * width + x];
                    if (color.a > greatestAlpha) { greatestAlpha = color.a; fallback = color; }
                    if (color.a < 0.8f || color.r + color.g + color.b > 2f) continue;
                    int key = ((int)(color.r * 31f) << 10) | ((int)(color.g * 31f) << 5) | (int)(color.b * 31f);
                    counts.TryGetValue(key, out int count);
                    counts[key] = ++count;
                    if (count > bestCount) { bestCount = count; best = color; }
                }
            return bestCount > 0 ? best : fallback;
        }

        internal void Clear()
        {
            foreach (Entry entry in m_entries)
                if (entry.Marked != null) UnityEngine.Object.Destroy(entry.Marked);
            m_entries.Clear();
            if (m_copyMaterial != null) UnityEngine.Object.Destroy(m_copyMaterial);
            m_copyMaterial = null;
        }
    }
}
