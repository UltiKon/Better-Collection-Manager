using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckSettingsMeshGeometry()
    {
        Camera camera = CameraUtils.Camera;
        float up = camera.UpDirection, right = camera.RightDirection, scale = camera.PixelScale;
        var owner = new GameObject("ClipGeometryFixture");
        try
        {
            foreach (float vertical in new[] { -1f, 1f })
            foreach (float horizontal in new[] { -1f, 1f })
            foreach (float pixelScale in new[] { .5f, 2f })
            {
                camera.UpDirection = vertical; camera.RightDirection = horizontal; camera.PixelScale = pixelScale;
                Vector3 Point(float x, float y) => owner.transform.InverseTransformPoint(camera.ScreenToWorldPoint(new Vector3(x, y, 1)));
                var source = new Mesh { name = "ClipGeometry", subMeshCount = 2, vertices = new[] {
                    Point(20, 20), Point(180, 20), Point(180, 180), Point(20, 180), Point(999, 999) },
                    normals = Enumerable.Repeat(new Vector3(0, -1, 0), 5).ToArray(),
                    tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), 5).ToArray(),
                    colors = new[] { new Color(0,0,0,1), new Color(1,0,0,1), new Color(1,1,0,1), new Color(0,1,0,1), Color.white } };
                source.SetTriangles(new List<int> { 0, 1, 2 }, 0, false);
                source.SetTriangles(new List<int> { 0, 2, 3 }, 1, false);
                for (int channel = 0; channel < 4; channel++) source.SetUVs(channel, new List<Vector4> {
                    new Vector4(channel,0,channel,1), new Vector4(channel+1,0,channel,1), new Vector4(channel+1,1,channel,1),
                    new Vector4(channel,1,channel,1), Vector4.zero });
                var original = (Vector3[])source.vertices.Clone();
                var viewport = new Rect(60, 50, 80, 100);
                Mesh clipped = FilterTrayGeometry.ClipMesh(source, owner.transform, camera, viewport);
                try
                {
                    float area = 0;
                    for (int sub = 0; sub < clipped.subMeshCount; sub++)
                    {
                        int[] triangles = clipped.GetTriangles(sub);
                        Check(triangles.Length > 0, "Clipping retains both original material slots");
                        for (int i = 0; i < triangles.Length; i += 3)
                        {
                            Vector3 a = camera.WorldToScreenPoint(owner.transform.TransformPoint(clipped.vertices[triangles[i]]));
                            Vector3 b = camera.WorldToScreenPoint(owner.transform.TransformPoint(clipped.vertices[triangles[i+1]]));
                            Vector3 c = camera.WorldToScreenPoint(owner.transform.TransformPoint(clipped.vertices[triangles[i+2]]));
                            area += Math.Abs((b.x-a.x)*(c.y-a.y) - (b.y-a.y)*(c.x-a.x)) * .5f;
                        }
                    }
                    Check(Math.Abs(area - viewport.width * viewport.height) < .02f,
                        "Clipped triangle area exactly covers the remaining visible rectangle, without holes or overlap");
                    for (int i = 0; i < clipped.vertices.Length; i++)
                    {
                        Vector3 point = camera.WorldToScreenPoint(owner.transform.TransformPoint(clipped.vertices[i]));
                        float u = (point.x - 20) / 160, v = (point.y - 20) / 160;
                        Check(point.x >= viewport.xMin - .001f && point.x <= viewport.xMax + .001f
                            && point.y >= viewport.yMin - .001f && point.y <= viewport.yMax + .001f,
                            "Clipped pixels stay inside all four edges for either camera direction and scale");
                        for (int channel = 0; channel < 4; channel++)
                            Check(Math.Abs(clipped.Uvs[channel][i].x - channel - u) < .001f
                                && Math.Abs(clipped.Uvs[channel][i].y - v) < .001f,
                                "Native UV coordinates interpolate at the cut instead of stretching the texture");
                        Check(Math.Abs(clipped.colors[i].r - u) < .001f && Math.Abs(clipped.colors[i].g - v) < .001f,
                            "Native vertex gradients interpolate at the cut");
                    }
                    Check(source.vertices.SequenceEqual(original) && source.GetTriangles(0).SequenceEqual(new[] { 0,1,2 }),
                        "Clipping never changes the shared native mesh or its unused vertices");
                    Mesh reused = FilterTrayGeometry.ClipMesh(source, owner.transform, camera, new Rect(400,400,20,20), clipped);
                    Check(reused == clipped && clipped.vertices.Length == 0 && clipped.GetTriangles(0).Length == 0,
                        "A wholly outside part has no remaining triangles and reuses its private mesh");
                }
                finally { UnityEngine.Object.Destroy(clipped); UnityEngine.Object.Destroy(source); }
            }
        }
        finally { camera.UpDirection = up; camera.RightDirection = right; camera.PixelScale = scale; UnityEngine.Object.Destroy(owner); }
    }
}
