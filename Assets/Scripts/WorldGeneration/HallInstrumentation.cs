using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    // All instrument faces use the same quad and material. Only mission transitions need CPU writes.
    [DisallowMultipleComponent]
    internal sealed class HallInstrumentation : MonoBehaviour
    {
        private enum Pattern { Curtain, Strip, Rack, Network, Fan, Radar, Map, Core, Hazard, Hull, Lining, Chamber }
        [Serializable]
        private struct Face
        {
            public Renderer renderer;
            public Color ink;
            public Pattern pattern;
            public bool followsMission;
            public float offset;
        }

        private static readonly Color Cyan = new(.16f, .89f, .96f), Blue = new(.20f, .61f, 1f);
        private static readonly Color Amber = new(1f, .58f, .16f), Teal = new(.24f, .98f, .73f);
        private static readonly Color Alarm = new(1f, .24f, .18f);
        private static readonly int Ink = Shader.PropertyToID("_Ink"), Mode = Shader.PropertyToID("_Pattern"),
            Level = Shader.PropertyToID("_Level"), Offset = Shader.PropertyToID("_Offset");
        [SerializeField] private List<Face> faces = new();
        [SerializeField] private CheckpointSecurityScanner scanner;
        [SerializeField] private ServerTerminal[] terminals;
        [SerializeField] private ServerNetworkPuzzle network;
        [SerializeField] private ControlLockdownMission lockdown;
        [SerializeField] private StoragePowerMission generator;
        [SerializeField] private TextMesh authentication;
        private MaterialPropertyBlock block;
        private Mesh quad, cube;
        private Material instrument;
        private float lastState = float.NaN, nextSample;

        internal static void Build(Transform room, Transform props, PrototypeMaterials m, Font font, int type)
        {
            Material material = Resources.Load<Material>("Hall Instrumentation");
            if (material == null)
            {
                Debug.LogWarning("Interior instrumentation material missing.", room);
                return;
            }
            var fx = Group("Hall instrumentation", room).gameObject.AddComponent<HallInstrumentation>();
            fx.quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            fx.cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            fx.instrument = material;
            switch (type)
            {
                case 0: fx.Checkpoint(props, m, font); break;
                case 1: fx.Server(props); break;
                case 2: fx.Control(props, m); break;
                case 3: fx.Storage(props, m, font); break;
            }
            fx.Refresh(true);
        }

        // Attach to a fixture's local frame; the owner and every visual retire with the same room.
        private Renderer Surface(string name, Transform parent, Vector3 position, Vector3 size,
            Mesh mesh, Material material, Quaternion rotation)
        {
            Transform t = Group(name, parent, position);
            t.localScale = size;
            t.localRotation = rotation;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = t.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private void Faceplate(string name, Transform parent, Vector3 at, float width, float height,
            Pattern pattern, Color ink, bool linked = false, float pitch = 0, float roll = 0)
        {
            var renderer = Surface(name, parent, at, new Vector3(width, height, 1), quad, instrument, Quaternion.Euler(pitch, 0, roll));
            faces.Add(new Face { renderer = renderer, ink = ink, pattern = pattern, followsMission = linked, offset = faces.Count * .47f });
        }

        private void Casing(string name, Transform parent, Vector3 at, Vector3 size, Material material)
            => Surface(name, parent, at, size, cube, material, Quaternion.identity);

        private void Checkpoint(Transform props, PrototypeMaterials m, Font font)
        {
            var arch = props.Find("Security scanner");
            Faceplate("Security wall lining", props, new Vector3(0, 2.82f, 11.53f), 19.5f, 3.9f, Pattern.Lining, Amber);
            Faceplate("Optical scan field", arch, new Vector3(0, 1.51f, -.40f), 4.62f, 2.78f, Pattern.Curtain, Cyan);
            for (int s = -1; s <= 1; s += 2)
            {
                Faceplate("Arch light rail", arch, new Vector3(s * 2.5f, 1.62f, -.43f), 2.72f, .13f, Pattern.Strip, Amber, true, 0, 90);
            }
            foreach (Transform pendant in props.parent.Find("Lighting"))
            {
                if (pendant.localPosition.z >= 0) continue;
                var cam = Group("Surveillance camera", pendant, new Vector3(0, -.16f, -.20f));
                cam.localRotation = Quaternion.Euler(-16, Mathf.Sign(pendant.localPosition.x) * 26, 0);
                Casing("Gimbal", cam, Vector3.zero, new Vector3(.16f, .27f, .15f), m.Metal);
                Casing("Camera hood", cam, new Vector3(0, -.19f, -.16f), new Vector3(.38f, .26f, .53f), m.Glass);
                Faceplate("Optic", cam, new Vector3(0, -.19f, -.433f), .19f, .19f, Pattern.Radar, Cyan);
            }
            scanner = props.GetComponentInChildren<CheckpointSecurityScanner>(true);
            var reader = scanner.transform;
            authentication = Label("ACCESS GRANTED", reader, new Vector3(0, -.365f, -.149f), .09f, font, 1.27f, .115f, m.Lettering);
            authentication.name = "Authentication status";
            Faceplate("Authentication ticks", reader, new Vector3(0, -.26f, -.177f), 1.35f, .043f, Pattern.Strip, Amber, true);
            Faceplate("Desk security scope", props.Find("Security desk"), new Vector3(0, 1.45f, .026f), 1.28f, .63f, Pattern.Radar, Cyan);
            for (int s = -1; s <= 1; s += 2)
                Faceplate("Wall marker", props, new Vector3(s * 7.4f, 2.55f, 11.51f), 4.7f, .10f, Pattern.Strip, Amber, true);
        }

        private void Server(Transform props)
        {
            network = props.GetComponentInChildren<ServerNetworkPuzzle>(true);
            terminals = props.GetComponentsInChildren<ServerTerminal>(true);
            foreach (Transform rack in props)
            {
                if (rack.name != "Server rack") continue;
                Faceplate("Blade telemetry", rack, new Vector3(0, -.04f, -.84f), 1.65f, 1.64f, Pattern.Rack, Blue);
                Faceplate("Intake impeller", rack, new Vector3(-.46f, .46f, -.852f), .52f, .52f, Pattern.Fan, Cyan);
            }
            var display = network.transform;
            Faceplate("Routing trace", display, new Vector3(0, 1.635f, -.218f), 2.8f, .055f, Pattern.Network, Cyan, true);
            for (int s = -1; s <= 1; s += 2)
            {
                Faceplate("Network display rail", display, new Vector3(s * 1.67f, 1.81f, -.218f), 1.07f, .068f, Pattern.Strip, Blue, true, 0, 90);
                var service = Group("Cooling service panel", props, new Vector3(s * 11.54f, 2.04f, 1.9f));
                service.localRotation = Quaternion.Euler(0, s * 90, 0);
                Faceplate("Plenum lining", service, Vector3.zero, 13.2f, 3.9f, Pattern.Lining, Blue);
                Faceplate("Coolant telemetry", service, new Vector3(0, 0, -.025f), 2.55f, .96f, Pattern.Rack, Blue);
                var edge = Group("Rack aisle lighting", props, new Vector3(s * 3.0f, .02f, 1.6f));
                edge.localRotation = Quaternion.Euler(90, 90, 0);
                Faceplate("Floor-edge guide", edge, Vector3.zero, 8.0f, .075f, Pattern.Strip, Blue);
            }
        }

        private void Control(Transform props, PrototypeMaterials m)
        {
            lockdown = props.GetComponentInChildren<ControlLockdownMission>(true);
            var panel = lockdown.transform;
            Faceplate("Operations wall lining", props, new Vector3(0, 3.14f, 12.53f), 16.3f, 4.5f, Pattern.Lining, Teal);
            Casing("Projection rail", panel, new Vector3(0, 2.49f, 0), new Vector3(4.15f, .08f, .39f), m.Metal);
            var map = Group("Facility projection", panel, new Vector3(0, 3.06f, .12f));
            map.localRotation = Quaternion.Euler(53, 0, 0);
            Faceplate("Survey grid", map, Vector3.zero, 4.10f, 1.70f, Pattern.Map, Cyan);
            // Raised wireframe volumes sit on the four registered footprints of the schematic.
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            {
                float rise = x == 1 && y == 1 ? .62f : .34f;
                var hull = Surface("Projected building", map, new Vector3(x * 1.045f, y * .365f, -rise * .5f),
                    new Vector3(.735f, .22f, rise), cube, instrument, Quaternion.identity);
                faces.Add(new Face { renderer = hull, ink = Teal, pattern = Pattern.Hull });
            }
            Faceplate("Projection bed", panel, new Vector3(0, 2.535f, 0), 3.95f, .28f, Pattern.Strip, Teal, false, 90);
            foreach (Transform fixture in props)
            {
                if (fixture.name == "Operations monitor")
                {
                    Faceplate("Tactical scope", fixture, new Vector3(-.66f, .12f, -.146f), 1.07f, 1.07f, Pattern.Radar, Teal);
                    Faceplate("Sector telemetry", fixture, new Vector3(.66f, .12f, -.146f), 1.25f, .90f, Pattern.Map, Cyan);
                }
                if (fixture.name == "Emergency warning beacon")
                    Faceplate("Lockdown annunciator", fixture, new Vector3(0, 0, -.276f), .56f, .27f, Pattern.Strip, Alarm, true);
            }
        }

        private void Storage(Transform props, PrototypeMaterials m, Font font)
        {
            generator = props.GetComponentInChildren<StoragePowerMission>(true);
            var housing = generator.transform;
            Faceplate("Power distribution lining", props, new Vector3(3.2f, 2.42f, 11.53f), 14.7f, 3.8f, Pattern.Lining, Amber);
            var service = Group("Turbine inspection panel", housing, new Vector3(-2.36f, 1.12f, 0));
            service.localRotation = Quaternion.Euler(0, 90, 0);
            Faceplate("Turbine rotor", service, Vector3.zero, 1.85f, 1.85f, Pattern.Fan, Cyan, true);
            Casing("Core emitter bed", housing, new Vector3(-.51f, 2.20f, 0), new Vector3(2.52f, .04f, 1.42f), m.Glass);
            Faceplate("Field coil", housing, new Vector3(-.51f, 2.225f, 0), 1.38f, 1.38f, Pattern.Core, Cyan, true, 90);
            var field = Surface("Field containment", housing, new Vector3(-.51f, 2.86f, 0), new Vector3(1.55f, .63f, 1.1f),
                Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"), instrument, Quaternion.identity);
            faces.Add(new Face { renderer = field, ink = Cyan, pattern = Pattern.Chamber, followsMission = true });
            Faceplate("Energy core projection", housing, new Vector3(-.51f, 2.90f, 0), 1.52f, 1.38f, Pattern.Core, Cyan, true);

            for (int s = -1; s <= 1; s += 2)
                Faceplate("Generator bus", housing, new Vector3(s * 2.23f, 1.16f, -1.18f), 1.72f, .105f, Pattern.Strip, Cyan, true, 0, 90);
            foreach (Transform pendant in props.parent.Find("Lighting"))
                Faceplate("Industrial lamp grille", pendant, new Vector3(0, -.074f, 0), 2.42f, .39f, Pattern.Hazard, Amber, false, 90);
            foreach (Transform shelf in props)
            {
                if (shelf.name != "Storage shelving") continue;
                Label(shelf.localPosition.x < 0 ? "BAY 01" : "BAY 02", shelf, new Vector3(-1.45f, 2.78f, -.99f), .12f, font, .94f, .14f, m.Lettering);
                Faceplate("Shelf supply indicator", shelf, new Vector3(1.25f, 2.78f, -.987f), 1.05f, .08f, Pattern.Strip, Amber, true);
                var lane = Group("Service boundary", shelf, new Vector3(0, .013f, -1.30f));
                Faceplate("Hazard markers", lane, Vector3.zero, 4.2f, .22f, Pattern.Hazard, Amber, false, 90);
            }
        }

        private void OnEnable() => Refresh(true);
        private void Update()
        {
            if (Time.time < nextSample) return;
            nextSample = Time.time + .15f;
            Refresh(false);
        }
        private void Refresh(bool all)
        {
            float state = 0;
            if (scanner != null) state = scanner.IsComplete ? 1 : -1;
            if (lockdown != null) state = lockdown.IsComplete ? 2 : -1;
            if (generator != null) state = generator.IsComplete ? 1 : 0;
            if (network != null && terminals.Length > 0)
            {
                foreach (var terminal in terminals)
                    if (terminal.IsOnline) state++;
                state = network.HasWarning ? -1 : state / terminals.Length;
            }
            if (!all && state == lastState) return;
            lastState = state;
            if (authentication != null) authentication.text = state > 0 ? "ACCESS GRANTED" : "LOCKED";
            block ??= new MaterialPropertyBlock();
            foreach (var face in faces)
            {
                if (!all && !face.followsMission) continue;
                Color ink = face.ink;
                if (face.followsMission && state >= 1 && (scanner != null || lockdown != null)) ink = Teal;
                if (face.followsMission && network != null && state < 0) ink = Alarm;
                block.SetColor(Ink, ink);
                block.SetFloat(Mode, (float)face.pattern);
                block.SetFloat(Level, face.followsMission ? state : 1);
                block.SetFloat(Offset, face.offset);
                face.renderer.SetPropertyBlock(block);
            }
        }
    }
}
