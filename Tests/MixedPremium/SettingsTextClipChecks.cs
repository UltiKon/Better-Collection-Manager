using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckSettingsTextSnapshot(ModsSettingsMenu menu)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var viewport = (Rect)typeof(ModsSettingsMenu).GetField("m_settingsViewport", flags).GetValue(menu);
        var rows = (System.Collections.IList)typeof(ModsSettingsMenu).GetField("m_scrollRows", flags).GetValue(menu);
        var update = typeof(ModsSettingsMenu).GetMethod("UpdateSettingsClipping", flags);
        var stop = typeof(ModsSettingsMenu).GetMethod("StopSettingsClipping", flags);
        var owner = new GameObject("SettingsFontAtFirstCut"); owner.transform.SetParent(menu.transform, false);
        owner.layer = (int)GameLayer.HighPriorityUI;
        owner.transform.position = CameraUtils.Camera.ScreenToWorldPoint(new Vector3(viewport.center.x, viewport.yMin - 2, 1));
        owner.transform.localScale = new Vector3(20,20,20);
        var label = owner.AddComponent<UberText>(); label.Width = .01f; label.FontSize = 35; label.Text = "Remember filter modes";
        label.RenderToTexture = false; label.UpdateNow(true);
        CounterTextLayout.TryScreenInkRect(label, CameraUtils.Camera, out Rect initial);
        float inkHeight = Math.Min(viewport.height * .25f, viewport.width * .5f * initial.height / initial.width);
        owner.transform.localScale *= inkHeight / initial.height;
        CounterTextLayout.TryScreenInkRect(label, CameraUtils.Camera, out initial);
        owner.transform.position += CameraUtils.Camera.ScreenToWorldPoint(new Vector3(viewport.center.x, viewport.yMin + inkHeight * .1f, 1))
            - CameraUtils.Camera.ScreenToWorldPoint(new Vector3(initial.center.x, initial.center.y, 1));
        var glyph = (MeshRenderer)CounterTextLayout.GlyphRenderer(label);
        glyph.WorldBounds = label.GetTextWorldSpaceBounds(); glyph.renderingLayerMask = 8;
        var regular = new Material { name = "Native outline" }; var bold = new Material { name = "Native bold" };
        glyph.sharedMaterials = new[] { regular, bold };
        var rowType = typeof(ModsSettingsMenu).GetNestedType("ScrollRow", flags);
        object row = Activator.CreateInstance(rowType, true);
        rowType.GetField("Object").SetValue(row, owner); rowType.GetField("Active").SetValue(row, true);
        rows.Add(row);
        try
        {
            int layoutCalls = label.UpdateCalls; Vector3 pose = label.transform.position;
            CounterTextLayout.TryScreenInkRect(label, CameraUtils.Camera, out Rect ink);
            update.Invoke(menu,null);
            var surface = owner.GetComponentsInChildren<MeshRenderer>(false).Single(renderer => renderer.gameObject.name == "BCM_SettingsClippedText");
            Mesh clipped = surface.GetComponent<MeshFilter>().sharedMesh;
            Check(!label.RenderToTexture && label.Width == .01f && label.UpdateCalls == layoutCalls && label.transform.position.Equals(pose)
                && label.FontSize == 35 && glyph.forceRenderingOff && surface.enabled && surface.renderingLayerMask == 8,
                "The first cut preserves the native text mode, tiny inherited fit width, layout, pose and font; only its captured output is clipped");
            Check(UberTextRenderToTexture.LastCaptureSource == glyph && UberTextRenderToTexture.LastCaptureSize.x >= ink.width * 2
                && UberTextRenderToTexture.LastCaptureSize.y >= ink.height * 2 && UnityEngine.Rendering.CommandBuffer.Drawn.Contains(bold),
                "The edge captures the actual screen ink at double pixel resolution, including native bold, instead of stretching the label's fit region");
            Check(clipped.vertexCount > 0 && !regular.Destroyed && !bold.Destroyed, "The first cut produces visible geometry without replacing shared font materials");
            int captures = UberTextRenderToTexture.CaptureCalls;
            Vector3[] clippedVertices = clipped.vertices;
            for (int i = 0; i < 5; i++) { Time.frameCount++; update.Invoke(menu, null); }
            Check(UberTextRenderToTexture.CaptureCalls == captures && ReferenceEquals(clippedVertices, clipped.vertices)
                && glyph.forceRenderingOff && surface.gameObject.activeInHierarchy,
                "Unchanged edge glyphs retain their existing capture and clipped mesh across idle frames");
            label.Text = "Remember changed modes"; label.UpdateNow(true); glyph.WorldBounds = label.GetTextWorldSpaceBounds();
            update.Invoke(menu, null);
            Check(UberTextRenderToTexture.CaptureCalls > captures && glyph.forceRenderingOff,
                "A late native text or glyph change refreshes the edge capture instead of retaining stale pixels");
            layoutCalls = label.UpdateCalls;
            foreach (Vector3 vertex in clipped.vertices)
            {
                Vector3 point = CameraUtils.Camera.WorldToScreenPoint(surface.transform.TransformPoint(vertex));
                Check(point.y >= viewport.yMin - .001f && point.y <= viewport.yMax + .001f, "Captured glyph triangles terminate at the viewport edge");
            }
            owner.transform.position += CameraUtils.Camera.ScreenToWorldPoint(new Vector3(0,viewport.height * .5f,1)) - CameraUtils.Camera.ScreenToWorldPoint(new Vector3(0,0,1));
            glyph.WorldBounds = label.GetTextWorldSpaceBounds(); update.Invoke(menu,null);
            Check(!glyph.forceRenderingOff && !surface.gameObject.activeInHierarchy && !label.RenderToTexture && label.UpdateCalls == layoutCalls,
                "Returning inside restores the same direct glyph without updating UberText or revealing a stale native texture plane");
            owner.SetActive(false); update.Invoke(menu,null);
            Check(!glyph.forceRenderingOff && !surface.gameObject.activeInHierarchy, "A hidden row cannot leave an independent text capture visible");
            stop.Invoke(menu,null);
            Check(clipped.Destroyed && !regular.Destroyed && !bold.Destroyed, "Closing releases the private capture mesh while retaining native fonts");
        }
        finally { rows.Remove(row); stop.Invoke(menu,null); UnityEngine.Object.Destroy(owner); update.Invoke(menu,null); }
    }
}
