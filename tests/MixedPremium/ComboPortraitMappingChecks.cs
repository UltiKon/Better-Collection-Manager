using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckComboPortraitMapping()
    {
        foreach (float targetAspect in new[] { .4f, .75f, 1f, 1.6f, 3f })
        foreach (float sourceAspect in new[] { .6f, 1f, 2f })
        foreach (bool ellipse in new[] { false, true })
        {
            var target = new Mesh { name = "Selected frame", subMeshCount = 2, vertices = new[] {
                new Vector3(-targetAspect/2,0,-.5f), new Vector3(targetAspect/2,0,-.5f),
                new Vector3(targetAspect/2,0,.5f), new Vector3(-targetAspect/2,0,.5f) } };
            target.SetTriangles(new List<int> { 0,1,2,0,2,3 }, 0, false);
            target.SetTriangles(new List<int> { 0,1,2,0,2,3 }, 1, false);
            var frameUv = new List<Vector4> { new Vector4(2,2,0,0), new Vector4(3,2,0,0), new Vector4(3,3,0,0), new Vector4(2,3,0,0) };
            target.SetUVs(0, frameUv);
            var source = new Mesh { name = "Native premium portrait", subMeshCount = 1, vertices = new[] {
                new Vector3(-sourceAspect/2,0,-.5f), new Vector3(sourceAspect/2,0,-.5f),
                new Vector3(sourceAspect/2,0,.5f), new Vector3(-sourceAspect/2,0,.5f), Vector3.zero } };
            source.SetTriangles(new List<int> { 0,1,4,1,2,4,2,3,4,3,0,4 }, 0, false);
            source.SetUVs(0, new List<Vector4> { new Vector4(.2f,.1f,0,0), new Vector4(.8f,.1f,0,0),
                new Vector4(.8f,.9f,0,0), new Vector4(.2f,.9f,0,0), new Vector4(.5f,.5f,0,0) });
            source.SetUVs(1, new List<Vector4> { new Vector4(0,0,0,0), new Vector4(1,0,0,0),
                new Vector4(1,1,0,0), new Vector4(0,1,0,0), new Vector4(.73f,.21f,0,0) });
            Mesh mapped = ComboPortraitMapping.Map(target, 1, Matrix4x4.identity, source, 0, Matrix4x4.identity, ellipse);
            try
            {
                var uv = new List<Vector4>(); var uv1 = new List<Vector4>(); mapped.GetUVs(0, uv); mapped.GetUVs(1, uv1);
                Vector4[] art = uv.Skip(4).ToArray();
                Check(mapped.GetTriangles(0).SequenceEqual(target.GetTriangles(0)) && uv.Take(4).SequenceEqual(frameUv)
                    && mapped.vertices.Take(4).SequenceEqual(target.vertices) && mapped.GetTriangles(1).All(index => index >= 4),
                    "Cross-type mapping changes only private portrait geometry and leaves the selected frame's original vertices, triangles and atlas coordinates intact");
                Check(art.All(value => value.x > .2f && value.x < .8f && value.y > .1f && value.y < .9f),
                    "Mapped artwork samples stay strictly inside the native portrait instead of using black or transparent atlas borders");
                float width = (art.Max(value => value.x) - art.Min(value => value.x)) / .6f * sourceAspect;
                float height = (art.Max(value => value.y) - art.Min(value => value.y)) / .8f;
                Check(Math.Abs(width / height - sourceAspect) < .0001f,
                    "The complete native portrait remains visible without an additional centered crop across selected frames");
                if (ellipse) Check(art.All(value => {
                    float x = (value.x - .5f) / .3f, y = (value.y - .5f) / .4f; return x*x + y*y < 1f; }),
                    "A rectangular template receives a covered region inside an oval native portrait, including at all four corners");
                int center = Enumerable.Range(4, mapped.vertices.Length - 4).First(index => mapped.vertices[index].sqrMagnitude < .0000001f);
                Check(Near(uv1[center].x, .73f) && Near(uv1[center].y,.21f),
                    "Native non-affine animation coordinates are sampled from the containing source triangle, including channels absent on the destination");
                Check(target.vertices.Length == 4 && source.vertices.Length == 5,
                    "Sampling does not mutate imported source or destination assets");
            }
            finally { UnityEngine.Object.Destroy(mapped); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(source); }
        }
    }
}
