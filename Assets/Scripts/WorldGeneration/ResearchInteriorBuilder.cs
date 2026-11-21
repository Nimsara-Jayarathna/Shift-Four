using UnityEngine;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    // Props sit outside the central walkable aisle. Only the scanner spans it, above head height.
    internal static class ResearchInteriorBuilder
    {
        internal static void Build(Transform root, Vector3 size, PrototypeMaterials m, Font font, int type)
        {
            Transform fixtures = Group("Lighting", root);
            Lighting(fixtures, size, type == 1 ? new Color(.7f, .86f, 1f) : new Color(1f, .95f, .86f), m);
            for (int side = -1; side <= 1; side += 2)
                Box("Aisle guide", root, new Vector3(side * 1.7f, .006f, -1),
                    new Vector3(.045f, .012f, size.z - 5), m.Marking, false);

            Transform props = Group("Props", root);
            switch (type)
            {
                case 0: Checkpoint(props, m, font); break;
                case 1: Server(props, m, font); break;
                case 2: Control(props, m, font); break;
                case 3: Storage(props, m, font); break;
            }
        }

        private static void Lighting(Transform fixtures, Vector3 size, Color color, PrototypeMaterials m)
        {
            const float fixtureHeight = 4.6f;
            float suspension = size.y - ResearchBuildingBuilder.FloorHeight - fixtureHeight;
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Transform fixture = Group("Pendant light", fixtures,
                    new Vector3(x * size.x * .23f, fixtureHeight, z * size.z * .22f));
                Box("Suspension", fixture, Vector3.up * (suspension * .5f), new Vector3(.07f, suspension, .07f), m.Metal, false);
                Box("Diffuser", fixture, Vector3.zero, new Vector3(2.5f, .13f, .45f), m.Marking, false);
                var light = Group("Indoor fill", fixture, Vector3.down * .17f).gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.range = 15f;
                light.intensity = 4.5f;
                light.shadows = LightShadows.None;
            }
        }

        private static void Checkpoint(Transform props, PrototypeMaterials m, Font font)
        {
            Workstation("Security desk", props, new Vector3(-6, 0, -3), m.Checkpoint, m);
            Transform scanner = Group("Security scanner", props, new Vector3(0, 0, -4.5f));
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Scanner pillar", scanner, new Vector3(side * 2.5f, 1.6f, 0), new Vector3(.35f, 3.2f, .75f), m.Metal);
                Box("Scanner accent", scanner, new Vector3(side * 2.5f, 1.62f, -.4f), new Vector3(.14f, 2.7f, .04f), m.Checkpoint, false);
            }
            Box("Scanner bridge", scanner, new Vector3(0, 3.1f, 0), new Vector3(5.35f, .35f, .75f), m.Metal);
            Label("SECURITY SCAN", scanner, new Vector3(0, 3.1f, -.39f), .1f, font, 4, .24f, m.Lettering);

            Transform barrier = Group("Security barrier", props, new Vector3(5.8f, 0, -4.5f));
            for (int side = -1; side <= 1; side += 2)
                Cylinder("Barrier post", barrier, new Vector3(side * 1.6f, .55f, 0), .3f, 1.1f, m.Metal);
            Box("Barrier rail", barrier, new Vector3(0, .85f, 0), new Vector3(3.2f, .38f, .22f), m.Checkpoint);
            AddCheckpointAccess(props, m, font);
        }

        internal static void AddCheckpointAccess(Transform props, PrototypeMaterials m, Font font)
        {
            Material signal = m.CheckpointSignal != null ? m.CheckpointSignal : m.Marking;
            Transform desk = props.Find("Security desk");
            Transform pickup = Group("Security Access Card", desk, new Vector3(1.02f, 1.12f, -.34f));
            pickup.localRotation = Quaternion.Euler(18, 0, 0);
            Box("Card body", pickup, Vector3.zero, new Vector3(.42f, .24f, .045f), signal);
            Box("Card chip", pickup, new Vector3(.08f, .02f, -.024f),
                new Vector3(.12f, .07f, .02f), m.Metal, false);
            var card = pickup.gameObject.AddComponent<SecurityAccessCard>();
            var glow = Group("Card glow", pickup, new Vector3(0, .15f, -.12f)).gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(.16f, .85f, 1);
            glow.range = .85f;
            glow.intensity = 1.25f;

            BuildCheckpointReader(props, card, m, font);
        }

        internal static void BuildCheckpointReader(Transform props, SecurityAccessCard card, PrototypeMaterials m, Font font)
        {
            Material signal = m.CheckpointSignal != null ? m.CheckpointSignal : m.Marking;
            // The partition sits to the right of the entrance aisle, with its face angled toward arriving players.
            float front = -ResearchBuildingBuilder.FrontOffset(0);
            Transform partition = Group("Scanner partition", props, new Vector3(4.2f, 0, front + 5.95f));
            partition.localRotation = Quaternion.Euler(0, 28, 0);
            Box("Scanner wall", partition, Vector3.up * 1.25f, new Vector3(2.8f, 2.5f, .32f), m.Wall);
            Box("Amber edge", partition, new Vector3(-1.25f, 1.25f, -.171f),
                new Vector3(.12f, 2.48f, .03f), m.Checkpoint, false);
            Transform reader = Group("Security Scanner Reader", partition, new Vector3(0, 1.38f, -.30f));
            Box("Scanner housing", reader, Vector3.zero, new Vector3(2.12f, .96f, .28f), m.Metal);
            Box("Screen", reader, new Vector3(-.12f, .025f, -.151f), new Vector3(1.61f, .60f, .035f), m.Glass, false);
            var indicator = Box("Scanner indicator", reader, new Vector3(.83f, .03f, -.174f),
                new Vector3(.13f, .17f, .07f), signal, false).GetComponent<Renderer>();
            var indicatorLight = Group("Scanner glow", reader, new Vector3(.83f, .03f, -.28f)).gameObject.AddComponent<Light>();
            indicatorLight.type = LightType.Point;
            indicatorLight.intensity = .75f;
            indicatorLight.range = .6f;
            Box("Scanner sign", reader, new Vector3(0, .67f, -.13f), new Vector3(2.15f, .29f, .06f), m.Metal, false);
            Label("SECURITY SCANNER", reader, new Vector3(0, .67f, -.173f), .13f, font, 1.98f, .23f, m.Lettering);
            TextMesh status = Label("ACCESS GRANTED\n" + CheckpointSecurityScanner.CompletionMessage, reader,
                new Vector3(-.12f, .025f, -.175f), .12f, font, 1.5f, .43f, m.Lettering);
            reader.gameObject.AddComponent<CheckpointSecurityScanner>().Configure(card, indicator, indicatorLight, status);
        }

        private static void Server(Transform props, PrototypeMaterials m, Font font)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    Transform rack = Group("Server rack", props, new Vector3(side * (4f + 2.4f * i), 1.1f, 2));
                    Vector3 rackSize = new Vector3(2, 2.2f, 1.6f);
                    if (m.ServerRack != null)
                    {
                        Object.Instantiate(m.ServerRack, rack, false).name = "Server rack mesh";
                        rack.gameObject.AddComponent<BoxCollider>().size = rackSize;
                    }
                    else
                    {
                        Box("Rack enclosure", rack, Vector3.zero, rackSize, m.Metal);
                        Box("Rack status strip", rack, new Vector3(.7f, 0, -.82f), new Vector3(.1f, 1.8f, .04f), m.Server, false);
                    }
                }

                Transform cables = Group("Server cables", props, new Vector3(side * 6.4f, 2.85f, 2));
                Box("Cable tray", cables, Vector3.zero, new Vector3(7, .12f, .6f), m.Metal);
                for (int i = -1; i <= 1; i++)
                    Box("Data cable", cables, new Vector3(0, .085f, i * .14f), new Vector3(6.8f, .045f, .045f), m.Server, false);
                Box("Cable drop", cables, new Vector3(side * 2.4f, -.33f, 0), new Vector3(.12f, .66f, .16f), m.Metal);
            }

            Transform terminal = Workstation("Server terminal", props, new Vector3(5.8f, 0, -2.8f), m.Server, m);
            Label("DATA TERMINAL", terminal, new Vector3(0, .76f, -.57f), .11f, font, 2.8f, .25f, m.Lettering);
        }

        private static void Control(Transform props, PrototypeMaterials m, Font font)
        {
            Workstation("Control console", props, new Vector3(-5.2f, 0, 2), m.Control, m);
            Workstation("Control console", props, new Vector3(5.2f, 0, 2), m.Control, m);
            for (int i = -1; i <= 1; i++)
            {
                Transform monitor = Group("Operations monitor", props, new Vector3(i * 3.9f, 2.5f, 12.4f));
                Box("Display housing", monitor, Vector3.zero, new Vector3(3.55f, 1.65f, .22f), m.Metal);
                Box("Display surface", monitor, new Vector3(0, 0, -.122f), new Vector3(3.24f, 1.34f, .025f), m.Glass, false);
                Box("Status band", monitor, new Vector3(0, -.42f, -.14f), new Vector3(2.8f, .07f, .022f), m.Control, false);
            }

            Transform alarm = Group("Alarm panel", props, new Vector3(-7.9f, 1.55f, 12.35f));
            Box("Alarm housing", alarm, Vector3.zero, new Vector3(1.9f, .95f, .28f), m.Metal);
            Box("Alarm indicator", alarm, new Vector3(.61f, 0, -.16f), new Vector3(.28f, .28f, .08f), m.Storage, false);
            Label("ALARM", alarm, new Vector3(-.25f, 0, -.155f), .11f, font, 1.05f, .38f, m.Lettering);
        }

        private static void Storage(Transform props, PrototypeMaterials m, Font font)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Transform shelf = Group("Storage shelving", props, new Vector3(side * 7.5f, 0, 3.5f));
                for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Box("Shelf upright", shelf, new Vector3(x * 2.05f, 1.65f, z * .9f), new Vector3(.14f, 3.3f, .14f), m.Metal);
                for (int tier = 0; tier < 3; tier++)
                {
                    float y = .28f + tier * 1.25f;
                    Box("Shelf deck", shelf, Vector3.up * y, new Vector3(4.2f, .14f, 1.95f), m.Metal);
                    Box("Cargo crate", shelf, new Vector3(side * (tier - 1), y + .47f, 0), new Vector3(1.25f, .8f, 1.45f), m.Crate);
                }
                Box("Cargo crate", props, new Vector3(side * 7, .85f, -3.8f), new Vector3(1.7f, 1.7f, 1.7f), m.Crate);
            }

            Transform generator = Group("Power generator", props, new Vector3(4.7f, 0, 8.6f));
            Box("Generator housing", generator, Vector3.up * .9f, new Vector3(4, 1.8f, 2.2f), m.Metal);
            Box("Service panel", generator, new Vector3(0, .95f, -1.12f), new Vector3(2.6f, .6f, .08f), m.Storage, false);
            Label("POWER GENERATOR", generator, new Vector3(0, .95f, -1.17f), .13f, font, 2.35f, .42f, m.Lettering);
            Cylinder("Vent stack", generator, new Vector3(1.25f, 2.2f, .5f), .34f, .82f, m.Metal);
        }

        private static Transform Workstation(string name, Transform props, Vector3 position, Material accent, PrototypeMaterials m)
        {
            Transform station = Group(name, props, position);
            Box("Desk body", station, Vector3.up * .5f, new Vector3(3.1f, 1, 1.1f), m.Metal);
            Box("Accent band", station, new Vector3(0, .92f, -.565f), new Vector3(3, .12f, .04f), accent, false);
            Box("Monitor", station, new Vector3(0, 1.45f, .16f), new Vector3(1.6f, .9f, .2f), m.Metal);
            Box("Monitor screen", station, new Vector3(0, 1.45f, .05f), new Vector3(1.45f, .74f, .025f), m.Glass, false);
            return station;
        }
    }
}
