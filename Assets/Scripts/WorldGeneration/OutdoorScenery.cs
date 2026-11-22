using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace ShiftFour
{
    // Render batches use the world's local coordinates. Major boulders get individual physics compounds.
    public sealed class OutdoorScenery : MonoBehaviour
    {
        public enum Detail { Low, Standard }
        internal static readonly Vector3 BoulderSize = new Vector3(1.6f, 1.9f, 1.4f);

        private readonly List<Mesh> ownedMeshes = new List<Mesh>();

        internal static void ApplyLighting(Light sun)
        {
            if (sun == null) return;
            sun.transform.rotation = Quaternion.Euler(33f, -38f, 0);
            sun.color = new Color(1, .91f, .78f);
            sun.intensity = 1.18f;
            sun.shadowStrength = .78f;
            RenderSettings.ambientSkyColor = new Color(.54f, .65f, .75f);
            RenderSettings.ambientEquatorColor = new Color(.43f, .47f, .44f);
            RenderSettings.ambientGroundColor = new Color(.28f, .30f, .24f);
            RenderSettings.fogColor = new Color(.62f, .71f, .74f);
            RenderSettings.fogDensity = .0046f;
        }

        internal void Build(int seed, float size, float treeDensity, float grassDensity, Detail detail,
            List<Rect> occupied, PrototypeMaterials materials)
        {
            Material material = materials.Foliage;
            var skyline = new Batch();
            var timber = new Batch();
            var stones = new Batch();
            var meadow = new[] { new Batch(), new Batch(), new Batch(), new Batch() };
            var random = new Random(seed ^ 0x36f415);
            float half = size * .5f;
            float phase = Range(random, 10, 500);
            bool detailed = detail == Detail.Standard;

            // Place shoulders under all pavement. Overlapping roads cover them naturally at junctions.
            var shoulders = new Batch();
            foreach (Transform road in transform.parent.Find("Roads"))
            {
                if (!road.TryGetComponent<BoxCollider>(out var slab) || !slab.enabled) continue;
                Vector3 halfSlab = road.localScale * .5f;
                Vector3 center = road.localPosition;
                center.y = .018f;
                var across = Vector3.right * (halfSlab.x + .52f);
                var along = Vector3.forward * (halfSlab.z + .52f);
                var gravel = new Color(.43f, .39f, .30f);
                shoulders.Face(center - across - along, center - across + along, center + across - along, gravel, .8f);
                shoulders.Face(center + across - along, center - across + along, center + across + along, gravel, .8f);
            }
            Publish(shoulders, material, "Gravel shoulders", false);

            foreach (Transform tree in transform.Find("Trees"))
            {
                var trunk = tree.GetChild(0);
                Pine(timber, random, tree.localPosition, trunk.localScale.y * 2 + 2.6f, detailed, false);
            }
            Publish(timber, material, "Facility conifers");
            foreach (Transform rock in transform.Find("Rocks"))
            {
                int firstVertex = stones.Points.Count;
                Boulder(stones, random, rock.localPosition - Vector3.up * .75f, BoulderSize);
                GeneratedBoulderCollision.Build(rock, stones.Points, firstVertex);
            }
            BuildFenceInfill(materials.Metal);

            // Start the landform beyond the fence. A low saddle frames the extraction road to the south.
            for (int side = 0; side < 4; side++)
            {
                int segments = Mathf.CeilToInt(size / (detailed ? 4f : 8f));
                int bands = detailed ? 14 : 9;
                float bandWidth = 72f / bands;
                for (int band = 0; band < bands; band++)
                    for (int i = 0; i < segments; i++)
                    {
                        float u = i * 2f / segments - 1, v = (i + 1) * 2f / segments - 1;
                        float inner = half - .8f + band * bandWidth, outer = inner + bandWidth;
                        Vector3 a = RidgePoint(side, u, inner, half, phase);
                        Vector3 b = RidgePoint(side, v, inner, half, phase);
                        Vector3 c = RidgePoint(side, u, outer, half, phase);
                        Vector3 d = RidgePoint(side, v, outer, half, phase);
                        RidgeFace(skyline, a, c, b, phase);
                        RidgeFace(skyline, b, c, d, phase);
                    }

                // A jittered stand follows each flank of the ridge; its footprint is outside the playable site.
                int columns = Mathf.CeilToInt(size / 4f * treeDensity);
                for (int row = 0; row < 3; row++)
                    for (int i = 0; i < columns; i++)
                    {
                        float u = -1 + 2 * (i + Range(random, .12f, .88f)) / columns;
                        float edge = half + 4 + row * 10 + Range(random, 0, 7);
                        // Interpolate the same triangles used by the ridge mesh, including in Low detail.
                        float column = (u + 1) * .5f * segments;
                        float band = (edge - (half - .8f)) / bandWidth;
                        float left = Mathf.Floor(column) * 2f / segments - 1;
                        float right = left + 2f / segments;
                        float near = half - .8f + Mathf.Floor(band) * bandWidth;
                        float x = column - Mathf.Floor(column), z = band - Mathf.Floor(band);
                        Vector3 b = RidgePoint(side, right, near, half, phase);
                        Vector3 c = RidgePoint(side, left, near + bandWidth, half, phase);
                        Vector3 foot;
                        if (x + z <= 1)
                        {
                            Vector3 a = RidgePoint(side, left, near, half, phase);
                            foot = a + (b - a) * x + (c - a) * z;
                        }
                        else
                        {
                            Vector3 d = RidgePoint(side, right, near + bandWidth, half, phase);
                            foot = d + (c - d) * (1 - x) + (b - d) * (1 - z);
                        }
                        if (side == 2 && Mathf.Abs(foot.x) < 10) continue;
                        float grove = Mathf.PerlinNoise(phase + foot.x * .032f, foot.z * .032f + 20);
                        if (grove < .36f) continue;
                        Pine(timber, random, foot - Vector3.up * .08f, Range(random, 5.5f, 13f) * (.75f + grove * .5f), detailed && row == 0, true);
                    }
                // Give each flank its own renderer so the camera can cull the three behind it.
                Publish(timber, material, "Conifers " + (side + 1));
                Publish(skyline, material, "Rocky ridge " + (side + 1));
            }

            var grassRandom = new Random(seed ^ 0x3c9820);
            // A bounded number of patches, each with one conservative footprint check for all its blades.
            int attempts = Mathf.RoundToInt(Mathf.Min(size * size * .047f, 3800) * grassDensity);
            for (int i = 0; i < attempts; i++)
            {
                var center = new Vector3(Range(grassRandom, 3 - half, half - 3), 0, Range(grassRandom, 3 - half, half - 3));
                const float radius = 1.6f;
                var footprint = new Rect(center.x - radius, center.z - radius, radius * 2, radius * 2);
                bool blocked = false;
                foreach (Rect area in occupied)
                    if (area.Overlaps(footprint)) { blocked = true; break; }
                if (blocked) continue;
                Batch patch = meadow[(center.x < 0 ? 0 : 1) + (center.z < 0 ? 0 : 2)];
                float kind = Range(grassRandom, 0, 1);
                if (kind < .06f)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        float scale = Range(grassRandom, .08f, .36f);
                        Boulder(stones, grassRandom, Offset(center, grassRandom, .65f), new Vector3(scale, .7f * scale, .85f * scale));
                    }
                }
                else if (kind < .15f)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        var p = Offset(center, grassRandom, .64f);
                        float r = Range(grassRandom, .36f, .72f);
                        Crown(patch, grassRandom, p, r, r * 1.35f, 8, new Color(.28f, .39f, .19f), .2f);
                    }
                }
                else
                {
                    for (int j = 0; j < (detailed ? 7 : 3); j++)
                        Tuft(patch, grassRandom, Offset(center, grassRandom, 1.1f));
                }
            }
            for (int i = 0; i < meadow.Length; i++)
                Publish(meadow[i], material, "Grasses and shrubs " + (i + 1), false);
            Publish(stones, material, "Boulders and scree");
        }

        private void BuildFenceInfill(Material metal)
        {
            // Make the solid perimeter visible between the existing rails in one render mesh.
            var infill = new Batch();
            foreach (Transform fence in transform.Find("Fences"))
            {
                var barrier = fence.GetComponent<BoxCollider>();
                int bars = Mathf.CeilToInt(barrier.size.x / .42f);
                for (int i = 0; i < bars; i++)
                {
                    float x = barrier.size.x * ((i + .5f) / bars - .5f);
                    Vector3 left = fence.localPosition + fence.localRotation * new Vector3(x - .016f, .16f, 0);
                    Vector3 right = left + fence.localRotation * (Vector3.right * .032f);
                    Vector3 height = Vector3.up * 2.18f;
                    infill.Face(left, left + height, right, Color.gray);
                    infill.Face(right, left + height, right + height, Color.gray);
                    infill.Face(right, left + height, left, Color.gray);
                    infill.Face(right + height, left + height, right, Color.gray);
                }
            }
            Publish(infill, metal, "Perimeter fence infill", false);
        }

        private static float Range(Random r, float min, float max) => Mathf.Lerp(min, max, (float)r.NextDouble());
        private static Vector3 Offset(Vector3 center, Random r, float spread) =>
            center + new Vector3(Range(r, -spread, spread), 0, Range(r, -spread, spread));

        private static Vector3 RidgePoint(int side, float u, float edge, float half, float phase)
        {
            Vector3 p;
            switch (side)
            {
                case 0: p = new Vector3(u * edge, 0, edge); break;
                case 1: p = new Vector3(edge, 0, -u * edge); break;
                case 2: p = new Vector3(-u * edge, 0, -edge); break;
                default: p = new Vector3(-edge, 0, u * edge); break;
            }
            float depth = edge - half;
            float n = Mathf.PerlinNoise(phase + p.x * .024f, phase + p.z * .024f);
            float crag = 1 - Mathf.Abs(2 * Mathf.PerlinNoise(phase + p.x * .061f, p.z * .061f + 53) - 1);
            float saddle = p.z < -half ? Mathf.Lerp(.15f, 1, Mathf.SmoothStep(0, 1, Mathf.Abs(p.x) / 28)) : 1;
            p.y = -.12f + Mathf.SmoothStep(0, 1, (depth + 1) / 33) * (5 + 34 * n * n + 9 * crag) * saddle;
            return p;
        }

        private static void RidgeFace(Batch batch, Vector3 a, Vector3 b, Vector3 c, float phase)
        {
            float slope = 1 - Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y);
            float mix = Mathf.Clamp01(slope * 3 + (a.y - 7) * .046f);
            Color color = Color.Lerp(new Color(.32f, .38f, .22f), new Color(.49f, .48f, .43f), mix);
            batch.Face(a, b, c, color * (.90f + .18f * Mathf.PerlinNoise(a.x * .08f + phase, a.z * .08f)), .5f);
        }

        private static void Pine(Batch batch, Random r, Vector3 p, float height, bool detailed, bool trunk)
        {
            if (trunk)
                Crown(batch, r, p, height * .028f, height * .94f, 6, new Color(.31f, .245f, .17f), 0);
            float width = Range(r, .18f, .26f) * height;
            Color needles = Color.Lerp(new Color(.095f, .205f, .155f), new Color(.24f, .37f, .23f), Range(r, 0, 1));
            int tiers = detailed ? 7 : 4;
            for (int i = 0; i < tiers; i++)
            {
                float t = (float)i / (tiers - 1);
                Vector3 basePoint = p + Vector3.up * (height * Mathf.Lerp(.22f, .84f, t));
                Crown(batch, r, basePoint, width * Mathf.Lerp(1, .21f, t), height * Mathf.Lerp(.34f, .19f, t),
                    detailed ? 16 : 10, needles * Mathf.Lerp(.82f, 1.23f, t), .24f);
            }
        }

        // Serrated skirts form individual boughs. A lifted inner ring gives the crown volume from below.
        private static void Crown(Batch batch, Random r, Vector3 p, float width, float height,
            int sides, Color color, float droop)
        {
            float angle = Range(r, 0, 6.28f);
            Vector3 tip = Offset(p, r, width * .12f) + Vector3.up * height;
            Vector3 underside = p + Vector3.up * height * .24f;
            Vector3 first = default, previous = default;
            for (int i = 0; i <= sides; i++)
            {
                float a = angle + i * (2 * Mathf.PI / sides);
                float radius = width * Range(r, .88f, 1.08f) * (i % 2 == 0 ? 1 : 1 - droop * 1.65f);
                Vector3 rim = i == sides ? first : p + new Vector3(Mathf.Cos(a) * radius,
                    height * Range(r, 0, .08f) - (i % 2 == 0 ? height * droop : 0), Mathf.Sin(a) * radius);
                if (i == 0) first = rim;
                else
                {
                    batch.Face(previous, tip, rim, color * Range(r, .9f, 1.1f));
                    batch.Face(previous, rim, underside, color * .70f);
                }
                previous = rim;
            }
        }

        private static void Boulder(Batch batch, Random r, Vector3 p, Vector3 size)
        {
            const int sides = 7;
            Color rock = Color.Lerp(new Color(.33f, .35f, .34f), new Color(.55f, .52f, .43f), Range(r, 0, 1));
            Vector3 top = Offset(p, r, .1f * size.x) + Vector3.up * size.y;
            float angle = Range(r, 0, 6.28f);
            Vector3 firstBase = default, firstRim = default, lastBase = default, lastRim = default;
            for (int i = 0; i <= sides; i++)
            {
                float a = angle + i * 2 * Mathf.PI / sides;
                var axis = new Vector3(Mathf.Cos(a) * size.x, 0, Mathf.Sin(a) * size.z);
                Vector3 foot = i == sides ? firstBase : p + axis * Range(r, .88f, 1.05f) - Vector3.up * .08f;
                Vector3 rim = i == sides ? firstRim : p + axis * Range(r, .67f, .95f) + Vector3.up * size.y * Range(r, .39f, .78f);
                if (i == 0) { firstBase = foot; firstRim = rim; }
                else
                {
                    batch.Face(lastBase, lastRim, rim, rock * .86f, .5f);
                    batch.Face(lastBase, rim, foot, rock * .83f, .5f);
                    batch.Face(lastRim, top, rim, rock * Range(r, .93f, 1.09f), .5f);
                }
                lastBase = foot;
                lastRim = rim;
            }
        }

        private static void Tuft(Batch batch, Random r, Vector3 p)
        {
            Color blade = Color.Lerp(new Color(.35f, .43f, .19f), new Color(.62f, .58f, .30f), Range(r, 0, 1));
            for (int i = 0; i < 6; i++)
            {
                float a = Range(r, 0, 2 * Mathf.PI);
                var lean = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var width = new Vector3(lean.z, 0, -lean.x) * Range(r, .025f, .055f);
                Vector3 foot = p + lean * Range(r, 0, .1f);
                Vector3 tip = foot + Vector3.up * Range(r, .15f, .40f) + lean * Range(r, .07f, .24f);
                batch.Face(foot - width, tip, foot + width, blade);
                batch.Face(foot + width, tip, foot - width, blade);
            }
        }

        private sealed class Batch
        {
            internal readonly List<Vector3> Points = new List<Vector3>();
            internal readonly List<Color32> Colors = new List<Color32>();
            internal readonly List<int> Triangles = new List<int>();

            internal void Face(Vector3 a, Vector3 b, Vector3 c, Color color, float surfaceDetail = 0)
            {
                color.a = surfaceDetail;
                int start = Points.Count;
                Points.Add(a); Points.Add(b); Points.Add(c);
                Colors.Add(color); Colors.Add(color); Colors.Add(color);
                Triangles.Add(start); Triangles.Add(start + 1); Triangles.Add(start + 2);
            }
        }

        private void Publish(Batch batch, Material material, string label, bool shadows = true)
        {
            if (batch.Points.Count == 0) return;
            var mesh = new Mesh { name = label, indexFormat = batch.Points.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            ownedMeshes.Add(mesh);
            mesh.SetVertices(batch.Points);
            mesh.SetColors(batch.Colors);
            mesh.SetTriangles(batch.Triangles, 0);
            mesh.RecalculateNormals();
            var item = PrototypeGeometry.Group(label, transform);
            item.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            batch.Points.Clear(); batch.Colors.Clear(); batch.Triangles.Clear();
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in ownedMeshes)
            {
                if (mesh == null) continue;
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
        }
    }
}
