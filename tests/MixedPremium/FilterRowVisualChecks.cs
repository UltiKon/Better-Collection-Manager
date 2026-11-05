using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static MeshFilter FilterButtonFace(SetFilterItem item)
        => item.GetComponentsInChildren<MeshFilter>(false).Single(mesh => mesh.gameObject.name == "Row face");

    private static void CheckFilterRowVisuals(KeywordSetTray.GridFixture fixture)
    {
        CheckFilterRowGradients();
        float skewX = CameraUtils.Camera.DepthSkewX, skewZ = CameraUtils.Camera.DepthSkewZ;
        foreach (int columns in new[] { 2, 3, 4 })
        {
            CollectionQualitySettings.FilterMenuRows = columns == 4 ? 16 : 7;
            CollectionQualitySettings.FilterMenuColumns = columns;
            CameraUtils.Camera.DepthSkewX = 0.6f;
            CameraUtils.Camera.DepthSkewZ = -0.4f;
            fixture.RebuildCount(31);
            foreach (SetFilterItem item in fixture.Items) item.m_uberText.GlyphOffset = 0.21f;
            fixture.Items[1].m_uberText.Text = "Casts When\nDrawn";
            fixture.Relayout();
            fixture.RefreshVisible();
            UberText wrapped = fixture.Items[1].m_uberText;
            Check(Near(wrapped.GradientUpperColor.r, 0.7f) && Near(wrapped.GradientUpperColor.g, 0.6f)
                && Near(wrapped.GradientUpperColor.b, 0.5f) && Near(wrapped.GradientUpperColor.a, 0.9f)
                && wrapped.GradientLowerColor.Equals(wrapped.GradientUpperColor)
                && wrapped.TextColor.Equals(new Color(0.32f, 0.21f, 0.12f, 1f)) && wrapped.FontSize == 35,
                "Wrapped captions must retain a uniform native midpoint tint, foreground color and font size after layout/visibility refresh");
            Bounds viewport = fixture.Scroller.m_ScrollBounds.bounds;
            Bounds first = FilterButtonFace(fixture.Items[1]).GetComponent<Renderer>().bounds;
            Bounds last = FilterButtonFace(fixture.Items[columns]).GetComponent<Renderer>().bounds;
            Check(Near(first.min.x, viewport.center.x - viewport.size.x * 0.49f)
                && Near(last.max.x, viewport.center.x + viewport.size.x * 0.49f),
                "Column button backgrounds must cover the same inner width as the full-row heading");
            for (int i = 1; i < columns; i++)
            {
                Bounds left = FilterButtonFace(fixture.Items[i]).GetComponent<Renderer>().bounds;
                Bounds right = FilterButtonFace(fixture.Items[i + 1]).GetComponent<Renderer>().bounds;
                Check(Near(left.max.x, right.min.x), "Adjacent column textures must meet without a strip of exposed wood");
            }
            Bounds nextRow = FilterButtonFace(fixture.Items[columns + 1]).GetComponent<Renderer>().bounds;
            Check(Near(first.min.z, nextRow.max.z), "Consecutive button textures must fill their row pitch without a vertical gap");
            foreach (SetFilterItem item in fixture.Items.Take(columns + 1))
            {
                Bounds face = FilterButtonFace(item).GetComponent<Renderer>().bounds;
                Check(CounterTextLayout.TryScreenInkRect(item.m_uberText, CameraUtils.Camera, out Rect ink)
                    && Math.Abs(ink.center.x - CameraUtils.Camera.WorldToScreenPoint(face.center).x) < 0.01f,
                    "Headers and column labels must center their visible ink, including native glyph bearings and tilted projection");
                Check(item.m_uberText.FontSize == 35 && Near(item.m_uberText.transform.lossyScale.x, fixture.Fit),
                    "Filling column backgrounds must preserve native text size and proportions");
                if (!item.IsHeader)
                    Check(item.GetComponent<BoxCollider>().bounds.min.x >= face.min.x - 0.001f
                        && item.GetComponent<BoxCollider>().bounds.max.x <= face.max.x + 0.001f,
                        "Resized column hit areas must stay within their own button texture");
            }
            SetFilterItem back = fixture.MakeBack(true);
            Bounds backFace = FilterButtonFace(back).GetComponent<Renderer>().bounds;
            Check(CounterTextLayout.TryScreenInkRect(back.m_uberText, CameraUtils.Camera, out Rect backInk)
                && Math.Abs(backInk.center.x - CameraUtils.Camera.WorldToScreenPoint(backFace.center).x) < 0.01f,
                "Back must center the visible render-to-texture caption when its output is a sibling of the label");
            fixture.RemoveBack(back);
            SetFilterItem hidden = fixture.MakeBack(true, true);
            Renderer output = CounterTextLayout.TextureSurface(hidden.m_uberText);
            Bounds hiddenBounds = output.GetComponent<MeshFilter>().sharedMesh.bounds;
            Check(Near(hiddenBounds.size.x, .8f) && Near(hiddenBounds.size.z, .15f)
                && !output.enabled && output.transform.localPosition.Equals(Vector3.zero),
                "Reflow treats an offscreen RTT output as protected text, preserving its native mesh dimensions and local position");
            hidden.m_uberText.TextureRefreshDeferred = false;
            output.enabled = true;
            fixture.RefreshText();
            Check(Near(output.GetComponent<MeshFilter>().sharedMesh.bounds.size.x, .8f)
                && Near(output.GetComponent<MeshFilter>().sharedMesh.bounds.size.z, .15f),
                "Revealing a previously hidden caption cannot inherit a stretched button mesh and run into adjacent labels");
            fixture.RemoveBack(hidden);
            SetFilterItem direct = fixture.MakeBack(true);
            UberText retired = direct.m_uberText;
            var inheritedGlyph = new GameObject("Inherited font glyphs"); inheritedGlyph.transform.SetParent(retired.transform, false);
            inheritedGlyph.AddComponent<TextMesh>(); Renderer oldGlyphs = inheritedGlyph.AddComponent<MeshRenderer>();
            var inherited = new GameObject("UberText_ShadowPlane_ClonedCaption"); inherited.transform.SetParent(direct.transform, false);
            Renderer oldShadow = inherited.AddComponent<MeshRenderer>();
            Renderer oldSurface = CounterTextLayout.TextureSurface(direct.m_uberText);
            var siblingGlyph = new GameObject("Inherited sibling caption"); siblingGlyph.transform.SetParent(direct.transform, false);
            siblingGlyph.AddComponent<TextMesh>(); Renderer siblingRenderer = siblingGlyph.AddComponent<MeshRenderer>();
            var retiredMesh = inheritedGlyph.AddComponent<MeshFilter>();
            retiredMesh.sharedMesh = oldSurface.GetComponent<MeshFilter>().sharedMesh;
            using (var geometry = new FilterTrayGeometry(direct.gameObject, null))
            {
                oldGlyphs.enabled = false;
                geometry.Restore();
                Check(!oldGlyphs.enabled, "Restoring row artwork cannot restore a retired glyph renderer's enabled state");
            }
            CounterTextLayout.UseDirectCaption(direct);
            direct.m_uberText.Text = "Overload"; direct.m_uberText.UpdateNow(true);
            Check(!direct.m_uberText.RenderToTexture && direct.m_uberText.RenderOnObject == null
                && !inherited.activeInHierarchy && !oldSurface.enabled && !retired.enabled && !oldGlyphs.enabled
                && CounterTextLayout.VisibleRenderers(direct.m_uberText).Length == 1,
                "Moving custom filter captions to direct native text retires the RTT surface and inherited shadow, leaving exactly one caption");
            direct.gameObject.SetActive(false); direct.gameObject.SetActive(true);
            direct.m_uberText.Text = "Overload refreshed"; direct.m_uberText.UpdateNow(true);
            Check(!oldShadow.gameObject.activeInHierarchy && !oldSurface.enabled
                && CounterTextLayout.VisibleRenderers(direct.m_uberText).Length == 1,
                "Revealing and updating a previously hidden filter cannot resurrect its old RTT text or shadow");
            var lifetime = direct.GetComponent<FilterCaptionLifetime>();
            Renderer detached = CounterTextLayout.GlyphRenderer(direct.m_uberText);
            detached.transform.SetParent(fixture.Root.transform, true);
            CounterTextLayout.SetRowVisible(direct.gameObject, false);
            Check(detached.gameObject.activeInHierarchy && !detached.enabled && direct.m_uberText.isHidden(),
                "Culling a filter explicitly disables even a detached native glyph renderer, so its text cannot remain over another visible button");
            CounterTextLayout.SetRowVisible(direct.gameObject, true);
            oldSurface.enabled = true; siblingRenderer.enabled = true;
            typeof(FilterCaptionLifetime).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(lifetime, null);
            Check(detached.enabled && detached.transform.IsChildOf(direct.m_uberText.transform) && !oldSurface.enabled && !siblingRenderer.enabled,
                "Revealing the same row reclaims its own glyph renderer and keeps retired text surfaces disabled");
            fixture.RemoveBack(direct);
            UberText live = fixture.Items[1].m_uberText;
            var stale = new GameObject("Cloned native font mesh"); stale.transform.SetParent(live.transform, false);
            stale.AddComponent<TextMesh>(); Renderer duplicate = stale.AddComponent<MeshRenderer>();
            fixture.RefreshText();
            Check(!duplicate.enabled && CounterTextLayout.VisibleRenderers(live).Length > 0,
                "A cloned runtime glyph mesh is suppressed while the row's current native font remains visible");
            duplicate.enabled = true; fixture.Relayout(); fixture.RefreshText();
            Check(!duplicate.enabled, "Filter refresh and scrolling cannot revive an inherited caption on top of its current text");
            UnityEngine.Object.Destroy(stale);
            fixture.Items[0].m_uberText.TextGeometryReady = false;
            fixture.Items[0].m_uberText.GlyphOffset = 0.37f;
            fixture.Relayout();
            fixture.Items[0].m_uberText.TextGeometryReady = true;
            fixture.RefreshText();
            Bounds heading = FilterButtonFace(fixture.Items[0]).GetComponent<Renderer>().bounds;
            Check(CounterTextLayout.TryScreenInkRect(fixture.Items[0].m_uberText, CameraUtils.Camera, out Rect lateInk)
                && Math.Abs(lateInk.center.x - CameraUtils.Camera.WorldToScreenPoint(heading.center).x) < 0.01f,
                "A heading whose font geometry becomes ready after layout must center during the visible-text refresh");
            fixture.Relayout();
            Bounds repeat = FilterButtonFace(fixture.Items[1]).GetComponent<Renderer>().bounds;
            Check(Near(repeat.min.x, first.min.x) && Near(repeat.max.x, first.max.x),
                "Repeated column layout must not accumulate texture stretching or horizontal shifts");
        }
        CameraUtils.Camera.DepthSkewX = skewX; CameraUtils.Camera.DepthSkewZ = skewZ;
    }

    private static void CheckFilterRowGradients()
    {
        Bounds sourceBounds = new Bounds(Vector3.zero, new Vector3(1f, 0.01f, 0.5f));
        Bounds target = new Bounds(Vector3.zero, new Vector3(2f, 0.01f, 0.5f));
        foreach (bool tinted in new[] { false, true })
        foreach (bool rotated in new[] { false, true })
        {
            var source = new Mesh { name = "Native button", subMeshCount = 1, bounds = sourceBounds,
                vertices = new[] { new Vector3(-0.5f, 0f, -0.25f), new Vector3(0.5f, 0f, -0.25f),
                    new Vector3(0.5f, 0f, 0.25f), new Vector3(-0.5f, 0f, 0.25f) } };
            source.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0, false);
            var uv = rotated ? new List<Vector4> { new Vector4(0.2f, 0.3f, 0f, 0f), new Vector4(0.2f, 0.8f, 0f, 0f),
                new Vector4(0.7f, 0.8f, 0f, 0f), new Vector4(0.7f, 0.3f, 0f, 0f) }
                : new List<Vector4> { new Vector4(0.2f, 0.3f, 0f, 0f), new Vector4(0.7f, 0.3f, 0f, 0f),
                    new Vector4(0.7f, 0.8f, 0f, 0f), new Vector4(0.2f, 0.8f, 0f, 0f) };
            source.SetUVs(0, uv);
            if (tinted) source.colors = new[] { Color.white, new Color(0.4f, 0.5f, 0.6f, 1f),
                new Color(0.4f, 0.5f, 0.6f, 1f), Color.white };
            foreach (int columns in new[] { 2, 3, 4 })
            for (int column = 0; column < columns; column++)
            {
                var rowObject = new GameObject("Gradient row");
                SetFilterItem row = rowObject.AddComponent<SetFilterItem>();
                var layers = new List<MeshFilter>();
                foreach (string name in new[] { "Row face", "Selected Glow", "Mouseover Glow", "Pressed Shadow" })
                {
                    var part = new GameObject(name);
                    part.transform.SetParent(rowObject.transform, false);
                    part.AddComponent<MeshRenderer>();
                    MeshFilter filter = part.AddComponent<MeshFilter>();
                    filter.sharedMesh = source;
                    part.SetActive(layers.Count == 0);
                    layers.Add(filter);
                }
                row.m_selectedGlow = layers[1].gameObject;
                row.m_mouseOverGlow = layers[2].gameObject;
                row.m_pressedShadow = layers[3].gameObject;
                using (var geometry = new FilterTrayGeometry(rowObject, null))
                {
                    geometry.Resize(sourceBounds, target, 0.1f, null, column, columns);
                    foreach (MeshFilter layer in layers)
                    {
                        Mesh mesh = layer.sharedMesh;
                        Check(layer.GetComponent<Renderer>().enabled && layer.gameObject.activeSelf == (layer == layers[0]),
                            "Sharing row shading must preserve native feedback renderers and their inactive state until hover/press/selection");
                        var mapped = new List<Vector4>(); mesh.GetUVs(0, mapped);
                        if (tinted)
                            Check(Near(mesh.colors.Min(color => color.r), 1f - 0.6f * (column + 1) / columns)
                                && Near(mesh.colors.Max(color => color.r), 1f - 0.6f * column / columns),
                                "Vertex shading must continue across the row in every idle/hover/selected/pressed layer");
                        else
                        {
                            var samples = new List<float>();
                            int[] triangles = mesh.GetTriangles(0);
                            for (int i = 0; i < triangles.Length; i += 3)
                            {
                                Vector3 middle = (mesh.vertices[triangles[i]] + mesh.vertices[triangles[i + 1]]
                                    + mesh.vertices[triangles[i + 2]]) * (1f / 3f);
                                if (Math.Abs(middle.x) >= 0.9f - 0.0001f || Math.Abs(middle.z) >= 0.15f - 0.0001f) continue;
                                for (int j = 0; j < 3; j++) samples.Add(rotated ? mapped[triangles[i + j]].y : mapped[triangles[i + j]].x);
                            }
                            float low = rotated ? 0.35f : 0.25f;
                            Check(samples.Count > 0 && Near(samples.Min(), low + 0.4f * column / columns)
                                && Near(samples.Max(), low + 0.4f * (column + 1) / columns),
                                "Baked shading must continue across columns in every native state, including a rotated atlas entry");
                        }
                        for (int i = 0; i < mesh.vertices.Length; i++)
                            if (Near(mesh.vertices[i].x, -1f) && Near(mesh.vertices[i].z, -0.25f))
                                Check(Near(mapped[i].x, uv[0].x) && Near(mapped[i].y, uv[0].y),
                                    "Spreading a gradient must preserve the original border and corner atlas coordinates");
                    }
                }
                UnityEngine.Object.Destroy(rowObject);
            }
            Check(source.vertices.Length == 4 && source.GetTriangles(0).Length == 6,
                "Column shading must not modify the shared native button mesh");
            UnityEngine.Object.Destroy(source);
        }
    }
}
