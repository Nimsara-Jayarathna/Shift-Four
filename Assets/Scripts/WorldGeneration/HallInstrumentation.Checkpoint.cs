using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    internal sealed partial class HallInstrumentation
    {
        [SerializeField] private TextMesh laneDisplay;
        [SerializeField] private Transform pickupEcho;
        [SerializeField] private Vector3 echoOrigin;
        [SerializeField] private List<Mesh> inspectionMeshes = new();

        // Draw the repeated solid trim in one mesh per existing palette material.
        // Coordinates are in the room frame. The fixtures below shoulder height are skins
        // on the existing walls, desk and arch. Only the overhead steel spans the aisle.
        private sealed class InspectionSteel
        {
            private readonly Mesh cube;
            private readonly Mesh housing = MakeHousing();

            // Bevel the rim and clip the corners of cast housings. Hard normals keep the
            // different faces readable in the existing hall lights at every quality preset.
            private static Mesh MakeHousing()
            {
                Vector2[] rim = { new(-.42f,-.5f), new(-.5f,-.32f), new(-.5f,.32f), new(-.42f,.5f),
                    new(.42f,.5f), new(.5f,.32f), new(.5f,-.32f), new(.42f,-.5f) };
                var vertices = new List<Vector3>();
                var uv = new List<Vector2>();
                var indices = new List<int>();
                void Triangle(Vector3 a, Vector3 b, Vector3 c)
                {
                    foreach (var v in new[] { a, b, c })
                    {
                        indices.Add(vertices.Count);
                        vertices.Add(v);
                        uv.Add(new Vector2(v.x + .5f, v.y + .5f));
                    }
                }
                for (int i = 0; i < rim.Length; i++)
                {
                    Vector3 a = rim[i], b = rim[(i + 1) % rim.Length];
                    Vector3 fa = a * .94f + Vector3.back * .5f, fb = b * .94f + Vector3.back * .5f;
                    Vector3 ma = a + Vector3.back * .27f, mb = b + Vector3.back * .27f;
                    Vector3 ba = a + Vector3.forward * .5f, bb = b + Vector3.forward * .5f;
                    Triangle(Vector3.back * .5f, fa, fb);
                    Triangle(fa, ma, mb); Triangle(fa, mb, fb);
                    Triangle(ma, ba, bb); Triangle(ma, bb, mb);
                    Triangle(Vector3.forward * .5f, bb, ba);
                }
                var mesh = new Mesh { name = "Inspection housing die" };
                mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(indices, 0);
                mesh.RecalculateNormals();
                return mesh;
            }
            private readonly Dictionary<Material, List<CombineInstance>> parts = new();

            internal InspectionSteel(Mesh cubeMesh) => cube = cubeMesh;

            internal void Plate(Material material, Vector3 at, Vector3 size, float roll = 0f, bool cast = false)
            {
                if (!parts.TryGetValue(material, out var group)) parts.Add(material, group = new());
                group.Add(new CombineInstance
                {
                    mesh = cast ? housing : cube,
                    transform = Matrix4x4.TRS(at, Quaternion.Euler(0, 0, roll), size)
                });
            }

            internal void Beam(Material material, Vector3 from, Vector3 to, float width, float depth)
            {
                Vector3 d = to - from;
                Plate(material, (from + to) * .5f, new Vector3(width, d.magnitude, depth),
                    -Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg);
            }

            internal void Publish(Transform root, List<Mesh> owned)
            {
                foreach (var pair in parts)
                {
                    var mesh = new Mesh { name = "Inspection chamber / " + pair.Key.name };
                    mesh.CombineMeshes(pair.Value.ToArray());
                    owned.Add(mesh);
                    var batch = Group(pair.Key.name, root);
                    batch.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    batch.gameObject.AddComponent<MeshRenderer>().sharedMaterial = pair.Key;
                }
                if (Application.isPlaying) Destroy(housing);
                else DestroyImmediate(housing);
            }
        }

        private void BuildInspectionChamber(Transform props, Transform arch, PrototypeMaterials m, Font font)
        {
            var room = Group("Inspection chamber", transform);
            var steel = new InspectionSteel(cube);
            float z = arch.localPosition.z;

            // Paired Warren trusses and suspension hangers reveal the height of the hall.
            for (int side = -1; side <= 1; side += 2)
            {
                float rail = z + side * .77f;
                steel.Plate(m.Metal, new(0, 5.72f, rail), new(15.8f, .22f, .25f));
                steel.Plate(m.Concrete, new(0, 4.86f, rail), new(15.8f, .22f, .29f));
                for (int n = 0; n < 12; n++)
                {
                    float x = (n - 6) * 1.29f;
                    bool rise = n % 2 == 0;
                    steel.Beam(m.Metal, new(x, rise ? 4.9f : 5.68f, rail),
                        new(x + 1.29f, rise ? 5.68f : 4.9f, rail), .12f, .14f);
                }
                for (int end = -1; end <= 1; end += 2)
                {
                    steel.Plate(m.Metal, new(end * 7.45f, 7.7f, rail), new(.14f, 4.05f, .18f));
                    steel.Plate(m.Checkpoint, new(end * 7.45f, 5.25f, rail), new(.49f, 1.14f, .45f));
                }

                // Arch shoulders converge on the existing bridge. Leave its lower face clear.
                float edge = side * 2.5f;
                steel.Plate(m.Concrete, new(edge, 2.0f, z - .39f), new(.34f, 2.3f, .085f), cast: true);
                steel.Beam(m.Wall, new(edge, 3.16f, z), new(side * 3.42f, 4.16f, z), .54f, .95f);
                steel.Plate(m.Metal, new(side * 3.46f, 4.44f, z), new(.80f, .8f, 1.12f), cast: true);
                for (int k = 0; k < 4; k++)
                    steel.Plate(m.Checkpoint, new(side * (2.57f + k * .19f), 3.35f + k * .20f, z - .49f),
                        new(.28f, .12f, .075f), -side * 42f);

                // Flush cassette panelling, cable trays and suspended rectangular HVAC trunks.
                for (int bay = -2; bay <= 2; bay++)
                {
                    float along = bay * 4.4f;
                    steel.Plate(m.Metal, new(side * 11.51f, 3.62f, along), new(.10f, 7.12f, 4.34f));
                    steel.Plate(m.Concrete, new(side * 11.46f, 1.64f, along), new(.07f, 2.94f, 4.09f));
                    steel.Plate(m.Wall, new(side * 11.40f, 5.18f, along), new(.16f, 3.47f, 4.07f));
                    steel.Plate(m.Metal, new(side * 11.27f, 4.65f, along + 2.12f), new(.36f, 3.1f, .18f));
                    for (int slot = 0; slot < 5; slot++)
                        steel.Plate(m.Metal, new(side * 11.29f, 4.43f + slot * .125f, along), new(.07f, .068f, 1.45f));
                }
                steel.Plate(m.Concrete, new(side * 8.65f, 7.1f, 0), new(1.32f, .87f, 23f));
                steel.Plate(m.Metal, new(side * 6.88f, 6.62f, 0), new(.74f, .23f, 23f));
                for (int tie = -5; tie <= 5; tie++)
                {
                    steel.Plate(m.Metal, new(side * 8.65f, 7.1f, tie * 2.14f), new(1.41f, .95f, .11f));
                    steel.Plate(m.Concrete, new(side * 6.88f, 6.79f, tie * 2.14f), new(.9f, .18f, .12f));
                }
                Faceplate("Inspection lane arrows", room, new(side * 1.4f, .018f, -6.7f),
                    .28f, 8.1f, Pattern.Lane, Amber, true, 90);
                // Two inset emergency lamps face the aisle and share its security colour.
                var lamp = Group("Emergency light housing", room, new(side * 11.30f, 3.79f, -7.7f));
                lamp.localRotation = Quaternion.Euler(0, side * 90f, 0);
                steel.Plate(m.Metal, lamp.localPosition, new(.17f, .40f, 1.22f));
                Faceplate("Emergency lens", lamp, new(0, 0, -.09f), .99f, .18f, Pattern.Strip, Amber, true);
            }

            steel.Plate(m.Metal, new(0, 3.96f, z), new(6.44f, 1.42f, 1.01f), cast: true);
            for (int edge = -1; edge <= 1; edge += 2)
            {
                for (int fin = 0; fin < 6; fin++)
                    steel.Plate(m.Metal, new(edge * 2.5f, .65f + fin * .19f, z - .433f),
                        new(.31f, .052f, .08f), edge * 12f);
                steel.Plate(m.Marking, new(edge * 2.5f, 2.62f, z - .439f), new(.19f, .31f, .04f), cast: true);
                steel.Plate(m.Metal, new(edge * 3.27f, 3.83f, z - .44f), new(.50f, .42f, .33f), cast: true);
            }
            steel.Plate(m.Glass, new(0, 3.99f, z - .53f), new(5.99f, 1.19f, .08f), cast: true);
            Label("01   /   SECURITY INTAKE", room, new(0, 4.25f, z - .59f), .21f, font, 5.58f, .4f, m.Lettering);
            laneDisplay = Label("AUTHENTICATION ACTIVE", room, new(0, 3.74f, z - .59f), .18f, font, 5.42f, .31f, m.Lettering);
            laneDisplay.name = "Lane clearance";
            Faceplate("Verification progress", room, new(0, 3.44f, z - .59f), 5.62f, .075f, Pattern.CredentialProgress, Amber, true);

            // The back wall is a surveillance gallery, readable on approach through the arch.
            var gallery = Group("Surveillance gallery", room);
            GallerySolid(gallery, new(0, 4.39f, 11.34f), new(19.9f, 4.95f, .34f));
            steel.Plate(m.Metal, new(0, 4.39f, 11.34f), new(19.9f, 4.95f, .34f));
            steel.Plate(m.Checkpoint, new(0, 6.65f, 11.07f), new(19.92f, .15f, .29f));
            for (int i = -1; i <= 1; i++)
            {
                float x = i * 5.9f;
                GallerySolid(gallery, new(x, 4.25f, 11.03f), new(5.60f, 3.08f, .28f));
                steel.Plate(m.Concrete, new(x, 4.25f, 11.08f), new(5.68f, 3.14f, .22f), cast: true);
                steel.Plate(m.Glass, new(x, 4.27f, 10.94f), new(5.43f, 2.9f, .10f), cast: true);
                Faceplate("Surveillance feed", room, new(x, 4.42f, 10.878f), i == 0 ? 4.6f : 2.1f, 1.93f,
                    i == 0 ? Pattern.Map : i < 0 ? Pattern.Radar : Pattern.Network, i == 0 ? Cyan : Amber);
                // Two operator consoles below the surveillance feeds. The rear bank is outside
                // the Guard circuit. Fit the plinth and overhanging worktop separately.
                if (i != 0)
                {
                    steel.Plate(m.Metal, new(x, .54f, 10.89f), new(4.2f, 1.08f, 1.22f));
                    GallerySolid(gallery, new(x, .54f, 10.89f), new(4.2f, 1.08f, 1.22f));
                    steel.Plate(m.Concrete, new(x, 1.17f, 10.68f), new(4.85f, .22f, 1.65f), cast: true);
                    GallerySolid(gallery, new(x, 1.17f, 10.68f), new(4.77f, .22f, 1.59f));
                    steel.Plate(m.Checkpoint, new(x, .88f, 10.26f), new(4.0f, .09f, .05f));
                    steel.Plate(m.Glass, new(x, 1.58f, 11.21f), new(3.8f, .63f, .14f), cast: true);
                    Faceplate("Console readout", room, new(x, 1.58f, 11.13f), 3.45f, .46f, Pattern.Rack, Cyan);
                    for (int j = 0; j < 7; j++)
                        steel.Plate(m.Metal, new(x - 1.26f + j * .27f, 1.294f, 10.36f), new(.21f, .027f, .15f));
                }
                Label(i == 0 ? "FACILITY OVERWATCH" : i < 0 ? "PERIMETER   /   LIDAR" : "UPLINK   /   TELEMETRY",
                    room, new(x, 3.13f, 10.875f), .135f, font, 4.9f, .25f, m.Lettering);
            }
            Label("CHECKPOINT  01     /     SECURITY SYSTEM", room, new(0, 6.11f, 11.13f),
                .26f, font, 16.8f, .60f, m.Lettering);

            // Augment the real desk within its existing footprint. Keep its right end free for the card.
            var desk = props.Find("Security desk");
            var d = desk.localPosition;
            steel.Plate(m.Concrete, d + new Vector3(0, .48f, -.553f), new(2.94f, .68f, .04f), cast: true);
            steel.Plate(m.Glass, d + new Vector3(-.47f, 1.014f, -.30f), new(1.12f, .026f, .35f), cast: true);
            for (int row = 0; row < 3; row++)
                for (int key = 0; key < 11; key++)
                    steel.Plate(row == 2 && key > 8 ? m.Checkpoint : m.Concrete,
                        d + new Vector3(-.94f + key * .094f, 1.043f, -.40f + row * .09f), new(.079f, .028f, .071f));
            steel.Plate(m.Glass, d + new Vector3(1.02f, 1.018f, -.34f), new(.78f, .033f, .37f));
            Label("CREDENTIAL ISSUE", desk, new(.65f, .71f, -.582f), .09f, font, 1.51f, .17f, m.Lettering);
            Faceplate("Credential tray locator", desk, new(1.02f, 1.039f, -.34f), .74f, .34f, Pattern.Credential, Cyan, false, 90);
            pickupEcho = Surface("Credential imprint", desk, new(1.02f, 1.13f, -.34f), new(.51f, .3f, 1),
                quad, instrument, Quaternion.identity).transform;
            echoOrigin = pickupEcho.localPosition;
            faces.Add(new Face { renderer = pickupEcho.GetComponent<Renderer>(), ink = Cyan, pattern = Pattern.Credential });
            pickupEcho.gameObject.SetActive(false);

            // A desk-mounted directory is visible over the arch approach, above the monitor.
            steel.Plate(m.Metal, d + new Vector3(-1.36f, 1.82f, .37f), new(.10f, 1.66f, .12f));
            steel.Plate(m.Metal, d + new Vector3(0, 2.62f, .37f), new(3.04f, .54f, .12f));
            Label("01   /   CARD ISSUE", desk, new(0, 2.62f, .294f), .14f, font, 2.79f, .28f, m.Lettering);
            var reader = scanner.transform;
            Casing("Reader identification blade", reader, new(0, 1.23f, -.04f), new(2.65f, .55f, .14f), m.Metal);
            Label("02   /   AUTHENTICATION", reader, new(0, 1.23f, -.12f), .13f, font, 2.42f, .26f, m.Lettering);
            Faceplate("Reader locator", reader, new(.92f, -.16f, -.182f), .18f, .22f, Pattern.Credential, Amber, true);
            Label("01   CREDENTIAL DESK     /     02   READER", arch, new(0, 3.13f, .39f),
                .12f, font, 4.9f, .22f, m.Lettering).transform.localRotation = Quaternion.Euler(0, 180, 0);
            steel.Publish(Group("Architectural metalwork", room), inspectionMeshes);
            BindCardImprint();
        }

        private static void GallerySolid(Transform gallery, Vector3 center, Vector3 size)
        {
            var shell = gallery.gameObject.AddComponent<BoxCollider>();
            shell.center = center;
            shell.size = size;
        }

        private void BindCardImprint()
        {
            if (scanner == null || !isActiveAndEnabled) return;
            scanner.CardCollected -= ShowImprint;
            scanner.CardCollected += ShowImprint;
        }

        private void ShowImprint(SecurityAccessCard card)
        {
            if (pickupEcho != null) StartCoroutine(LiftImprint());
        }

        private IEnumerator LiftImprint()
        {
            pickupEcho.gameObject.SetActive(true);
            float start = Time.time;
            while (Time.time - start < .55f)
            {
                float t = (Time.time - start) / .55f;
                pickupEcho.localPosition = echoOrigin + Vector3.up * (.34f * Mathf.SmoothStep(0, 1, t));
                pickupEcho.localScale = new Vector3(.51f, .3f, 1) * (1 - t * t);
                yield return null;
            }
            pickupEcho.gameObject.SetActive(false);
        }

        private void UpdateInspection(float state)
        {
            string label = state < 0 ? "CREDENTIAL REQUIRED" : state == 0 ? "PRESENT CARD AT READER" :
                state < 1 ? "AUTHENTICATION ACTIVE" : "ACCESS GRANTED";
            Color tint = state < 0 ? Amber : state < 1 ? Cyan : Teal;
            if (laneDisplay != null)
            {
                if (laneDisplay.text != label) laneDisplay.text = label;
                var displayRenderer = laneDisplay.GetComponent<Renderer>();
                block ??= new MaterialPropertyBlock();
                displayRenderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", tint);
                displayRenderer.SetPropertyBlock(block);
            }
            if (authentication != null) authentication.text = state < 0 ? "LOCKED" : state == 0 ? "READER READY" :
                state < 1 ? "VERIFYING" : "ACCESS GRANTED";
        }

        private void OnDisable()
        {
            if (scanner != null) scanner.CardCollected -= ShowImprint;
            StopAllCoroutines();
            if (pickupEcho != null) pickupEcho.gameObject.SetActive(false);
        }

        private void OnDestroy() => ReleaseInspectionMeshes();

        internal void ReleaseInspectionMeshes()
        {
            foreach (Mesh mesh in inspectionMeshes)
            {
                if (mesh == null) continue;
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
            inspectionMeshes.Clear();
        }
    }
}
