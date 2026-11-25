using System.Collections.Generic;
using UnityEngine;

namespace ShiftFour
{
    internal static class GeneratedBoulderCollision
    {
        private const float SliceHeight = .28f;
        private const float StripWidth = .34f;

        internal static void Build(Transform rock, List<Vector3> vertices, int firstVertex)
        {
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            for (int i = firstVertex; i < vertices.Count; i++)
            {
                bottom = Mathf.Min(bottom, vertices[i].y);
                top = Mathf.Max(top, vertices[i].y);
            }

            int slices = Mathf.CeilToInt((top - bottom) / SliceHeight);
            float height = (top - bottom) / slices;
            var outline = new List<(Vector2 Start, Vector2 End)>(14);
            for (int slice = 0; slice < slices; slice++)
            {
                float centerY = bottom + (slice + .5f) * height;
                // Sample slightly below the center of a slab so the narrowing sides remain solid.
                float sampleY = centerY - height * .13f;
                outline.Clear();
                for (int i = firstVertex; i < vertices.Count; i += 3)
                {
                    Vector3 a = vertices[i], b = vertices[i + 1], c = vertices[i + 2];
                    bool ab = CutAtHeight(a, b, sampleY, out Vector2 first);
                    bool bc = CutAtHeight(b, c, sampleY, out Vector2 second);
                    bool ca = CutAtHeight(c, a, sampleY, out Vector2 third);
                    if (ab && bc) outline.Add((first, second));
                    else if (bc && ca) outline.Add((second, third));
                    else if (ca && ab) outline.Add((third, first));
                }

                AddSlice(rock, outline, centerY, height);
            }
        }

        private static bool CutAtHeight(Vector3 a, Vector3 b, float y, out Vector2 point)
        {
            point = default;
            if ((a.y < y) == (b.y < y)) return false;
            Vector3 hit = Vector3.LerpUnclamped(a, b, (y - a.y) / (b.y - a.y));
            point = new Vector2(hit.x, hit.z);
            return true;
        }

        private static void AddSlice(Transform rock, List<(Vector2 Start, Vector2 End)> outline, float y, float height)
        {
            if (outline.Count == 0) return;
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            foreach (var edge in outline)
            {
                left = Mathf.Min(left, Mathf.Min(edge.Start.x, edge.End.x));
                right = Mathf.Max(right, Mathf.Max(edge.Start.x, edge.End.x));
            }

            // Use narrow strips so the AABB of the rock cannot fill in its diagonal corners.
            int strips = Mathf.CeilToInt((right - left) / StripWidth);
            float width = (right - left) / strips;
            for (int strip = 0; strip < strips; strip++)
            {
                float x = left + (strip + .5f) * width;
                float near = float.PositiveInfinity, far = float.NegativeInfinity;
                foreach (var edge in outline)
                {
                    Vector2 a = edge.Start, b = edge.End;
                    if ((a.x < x) == (b.x < x)) continue;
                    float z = Mathf.LerpUnclamped(a.y, b.y, (x - a.x) / (b.x - a.x));
                    near = Mathf.Min(near, z);
                    far = Mathf.Max(far, z);
                }

                if (far <= near) continue;
                var box = rock.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(x, y, (near + far) * .5f) - rock.localPosition;
                box.size = new Vector3(width, height, far - near);
            }
        }
    }
}
