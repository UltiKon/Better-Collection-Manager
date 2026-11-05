using System;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckComboNativeSkinGeometry()
    {
        var flavorParent = new GameObject("Native flavor font fixture");
        try
        {
            UberText original = AssetLoader.Get().FlavorPrefab.GetComponent<CardInfoPane>().m_flavorText;
            using (var pane = new ComboFlavorPane(flavorParent.transform))
            {
                pane.Layout(CameraUtils.Camera, new Rect(300,200,600,540), 10, "AoE", "A native-size description.");
                Check(pane.Template.Text == "A native-size description." && !pane.Template.isHidden()
                    && pane.Template.FontSize == original.FontSize && pane.Template.CharacterSize == original.CharacterSize
                    && pane.Template.TextColor.r == 0 && original.Text != pane.Template.Text,
                    "Inspect renders descriptions with the native flavor component's font metrics and black text without mutating its source prefab");
            }
        }
        finally { UnityEngine.Object.Destroy(flavorParent); }
        using (ComboInputTemplate template = ComboInputTemplate.Load())
        {
            Check(template.Background?.sprite != null && template.Field?.m_selectedItem != null,
                "Native input backgrounds and font styles resolve before opening the shop or Options");
            var parentObject = new GameObject("Native field fixture");
            try
            {
                using (var field = new ComboNativeInput(parentObject.transform, template.Background, "BCM_ComboFieldFixture"))
                {
                    foreach (Rect target in new[] { new Rect(100,100,700,60), new Rect(100,100,300,180), new Rect(100,100,80,40) })
                    {
                        field.Layout(CameraUtils.Camera, target, 10, 4600);
                        Mesh mesh = field.Root.GetComponent<MeshFilter>().sharedMesh;
                        var uv = new System.Collections.Generic.List<Vector4>(); mesh.GetUVs(0, uv);
                        Vector3 first = CameraUtils.Camera.WorldToScreenPoint(field.Root.transform.TransformPoint(mesh.vertices[0]));
                        Vector3 next = CameraUtils.Camera.WorldToScreenPoint(field.Root.transform.TransformPoint(mesh.vertices[1]));
                        Check(mesh.vertices.Length == 16 && mesh.GetTriangles(0).Length == 54
                            && Near(next.x - first.x, 5f) && Near(uv[0].x,0) && Near(uv[1].x,.1f)
                            && field.Root.GetComponent<Renderer>().sharedMaterial.mainTexture == template.Background.sprite.texture
                            && field.Root.GetComponent<Renderer>().sharedMaterial.renderQueue > 4600
                            && template.Background.material.renderQueue == 4300,
                            "Native input sprites keep atlas borders at fixed pixel size across single-line and multiline fields without stretching or dropdown decorations");
                        Check(mesh.colors[5].r > .8f && mesh.colors[5].g > .8f && mesh.colors[0].r < .5f,
                            "Input interiors have a light parchment tint and a darker fixed border instead of an undifferentiated dark rectangle");
                    }
                }
            }
            finally { UnityEngine.Object.Destroy(parentObject); }
        }
        Camera camera = CameraUtils.Camera;
        Quaternion savedRotation = camera.transform.rotation;
        var parent = new GameObject("Rotated menu parent");
        var artwork = new GameObject("Native shop button"); artwork.transform.SetParent(parent.transform, false);
        var caption = new GameObject("Caption"); caption.transform.SetParent(artwork.transform, false);
        UberText label = caption.AddComponent<UberText>();
        var icon = new GameObject("Native confirmation icon"); icon.transform.SetParent(artwork.transform, false);
        try
        {
            const float half = .70710677f;
            parent.transform.localRotation = new Quaternion(0, half, 0, half);
            artwork.transform.localRotation = new Quaternion(0, 0, 1, 0);
            caption.transform.localRotation = new Quaternion(half, 0, 0, half);
            camera.transform.rotation = new Quaternion(-half, 0, 0, half);
            Quaternion iconRotation = icon.transform.localRotation, localCaption = caption.transform.localRotation;
            for (int i = 0; i < 20; i++)
            {
                ComboSceneGeometry.FaceCaption(artwork.transform, label, camera);
                Quaternion relative = Quaternion.Inverse(camera.transform.rotation) * caption.transform.rotation;
                Check(Math.Abs(relative.x) + Math.Abs(relative.y) + Math.Abs(relative.z) < .00001f
                    && Math.Abs(Math.Abs(relative.w) - 1f) < .00001f
                    && caption.transform.localRotation.Equals(localCaption) && icon.transform.localRotation.Equals(iconRotation),
                    "Shop buttons face the camera under a rotated menu parent without separately flipping captions or icons");
            }
        }
        finally { camera.transform.rotation = savedRotation; UnityEngine.Object.Destroy(parent); }

        var paper = new GameObject("Native opaque paper");
        var textObject = new GameObject("Late native editor text");
        try
        {
            var background = paper.AddComponent<MeshRenderer>();
            var paperMaterial = background.sharedMaterial = new Material { renderQueue = 4600 };
            UberText text = textObject.AddComponent<UberText>(); text.SimulateNativeQueue = true;
            text.RenderQueue = 1450; text.Text = "Saved combo description"; text.UpdateNow(true);
            Material originalFont = CounterTextLayout.VisibleRenderers(text).Single().sharedMaterial;
            Check(originalFont.renderQueue < paperMaterial.renderQueue,
                "The regression fixture reproduces a native message font drawing before the opaque paper");
            ComboSceneGeometry.TextAfterArtwork(text, paper);
            int stable = text.RenderQueue;
            for (int i = 0; i < 20; i++)
            {
                ComboSceneGeometry.TextAfterArtwork(text, paper);
                Check(CounterTextLayout.VisibleRenderers(text).All(renderer => renderer.sharedMaterials.All(material => material.renderQueue > paperMaterial.renderQueue))
                    && text.RenderQueue == stable && paperMaterial.renderQueue == 4600 && originalFont.renderQueue == 4450,
                    "Editor glyphs draw after the native paper without accumulating priority or mutating shared font or paper materials");
            }
            Renderer glyphs = CounterTextLayout.VisibleRenderers(text).Single();
            glyphs.sharedMaterial = null; // Material not available in an early frame.
            ComboSceneGeometry.TextAfterArtwork(text, paper);
            glyphs.sharedMaterial = new Material { renderQueue = 4400 };
            ComboSceneGeometry.TextAfterArtwork(text, paper);
            Check(glyphs.sharedMaterial.renderQueue > paperMaterial.renderQueue,
                "A late-loaded font material is moved above the native paper on the next ordering pass");
        }
        finally { UnityEngine.Object.Destroy(paper); UnityEngine.Object.Destroy(textObject); }
    }
}
