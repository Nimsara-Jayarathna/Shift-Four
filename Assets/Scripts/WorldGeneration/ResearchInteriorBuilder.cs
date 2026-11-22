using UnityEngine;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    // Props sit outside the central walkable aisle. Only the scanner spans it, above head height.
    internal static class ResearchInteriorBuilder
    {
        // Slightly tinted task lights retain neutral highlights on keycards, terminals and power cells.
        private static readonly Color[] RoomLights =
        {
            new Color(1f, .81f, .59f), new Color(.68f, .85f, 1f),
            new Color(.66f, .94f, .86f), new Color(1f, .75f, .51f)
        };

        internal static void Build(Transform root, Vector3 size, PrototypeMaterials m, Font font, int type)
        {
            Transform fixtures = Group("Lighting", root);
            Lighting(fixtures, size, RoomLights[type], m);
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
            HallInstrumentation.Build(root, props, m, font, type);
        }

        private static void Lighting(Transform fixtures, Vector3 size, Color color, PrototypeMaterials m)
        {
            var diffuserColor = new MaterialPropertyBlock();
            diffuserColor.SetColor("_BaseColor", Color.Lerp(color, Color.white, .30f));
            const float fixtureHeight = 4.6f;
            float suspension = size.y - ResearchBuildingBuilder.FloorHeight - fixtureHeight;
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Transform fixture = Group("Pendant light", fixtures,
                    new Vector3(x * size.x * .23f, fixtureHeight, z * size.z * .22f));
                Box("Suspension", fixture, Vector3.up * (suspension * .5f), new Vector3(.07f, suspension, .07f), m.Metal, false);
                var diffuser = Box("Diffuser", fixture, Vector3.zero, new Vector3(2.5f, .13f, .45f), m.CheckpointSignal != null ? m.CheckpointSignal : m.Marking, false);
                diffuser.GetComponent<Renderer>().SetPropertyBlock(diffuserColor);
                var light = Group("Indoor fill", fixture, Vector3.down * .17f).gameObject.AddComponent<Light>();
                light.type = LightType.Spot;
                light.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                light.spotAngle = 138f;
                light.innerSpotAngle = 86f;
                light.color = color;
                light.range = 11.5f;
                light.intensity = 5.8f;
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
                        Transform mesh = Object.Instantiate(m.ServerRack, rack, false).transform;
                        mesh.name = "Server rack mesh";
                        var chassis = mesh.Find("Server_Steel");
                        if (chassis != null && chassis.TryGetComponent<Renderer>(out var steel))
                            steel.sharedMaterial = m.Metal;
                        var leds = mesh.Find("Server_Cyan");
                        if (m.CheckpointSignal != null && leds != null && leds.TryGetComponent<Renderer>(out var lamps))
                            lamps.sharedMaterial = m.CheckpointSignal;
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

            BuildServerPuzzle(props, m, font);
        }

        internal static void BuildServerPuzzle(Transform props, PrototypeMaterials m, Font font)
        {
            Transform display = Group("Network sequence display", props, new Vector3(-4.9f, 0, -5.85f));
            display.localRotation = Quaternion.Euler(0, -26, 0);
            Box("Display stand", display, Vector3.up * .57f, new Vector3(2.7f, 1.14f, .65f), m.Metal);
            Box("Display housing", display, Vector3.up * 1.81f, new Vector3(3.8f, 1.42f, .35f), m.Metal);
            Box("Display screen", display, new Vector3(0, 1.81f, -.185f),
                new Vector3(3.6f, 1.24f, .04f), m.Glass, false);
            Label("ACTIVATE IN ORDER", display, new Vector3(0, 2.19f, -.21f), .14f, font, 3.15f, .22f, m.Lettering);
            Label(ServerNetworkPuzzle.SequenceText, display, new Vector3(0, 1.85f, -.21f), .24f, font, 3.15f, .38f, m.Lettering);
            TextMesh boardStatus = Label(ServerNetworkPuzzle.CompletionTitle + "\n" + ServerNetworkPuzzle.CompletionDetail,
                display, new Vector3(0, 1.43f, -.21f), .14f, font, 3.15f, .35f, m.Lettering);
            var puzzle = display.gameObject.AddComponent<ServerNetworkPuzzle>();

            var terminals = new ServerTerminal[3];
            Material signal = m.CheckpointSignal != null ? m.CheckpointSignal : m.Marking;
            for (int index = 0; index < terminals.Length; index++)
            {
                int number = index + 1;
                string name = $"SERVER {number}";
                Transform terminal = Group(name, props, new Vector3(3.7f + 2.8f * index, 0, -3.9f));
                Box("Console base", terminal, Vector3.up * .55f, new Vector3(2.22f, 1.1f, 1.05f), m.Metal);
                Box("Console housing", terminal, new Vector3(0, 1.51f, -.13f), new Vector3(2.36f, .86f, .54f), m.Metal);
                Box("Console face", terminal, new Vector3(0, 1.51f, -.411f),
                    new Vector3(2.16f, .68f, .035f), m.Glass, false);
                Box("Server trim", terminal, new Vector3(0, .91f, -.534f), new Vector3(2.08f, .08f, .025f), m.Server, false);
                var lamp = Box("Status lamp", terminal, new Vector3(.79f, 1.37f, -.46f),
                    new Vector3(.21f, .21f, .075f), signal, false).GetComponent<Renderer>();
                Label(name, terminal, new Vector3(0, 1.66f, -.436f), .17f, font, 1.9f, .26f, m.Lettering);
                TextMesh status = Label("OFFLINE", terminal, new Vector3(-.24f, 1.37f, -.436f), .12f, font, 1.2f, .23f, m.Lettering);
                terminals[index] = terminal.gameObject.AddComponent<ServerTerminal>();
                terminals[index].Configure(puzzle, number, lamp, status);
            }
            puzzle.Configure(terminals, boardStatus);
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

            BuildControlLockdown(props, m, font);
        }

        internal static void BuildControlLockdown(Transform props, PrototypeMaterials m, Font font)
        {
            Transform panel = Group("LOCKDOWN OVERRIDE", props, new Vector3(0, 0, 10.45f));
            Box("Override stand", panel, Vector3.up * .57f, new Vector3(3.4f, 1.14f, .68f), m.Metal);
            Box("Override housing", panel, Vector3.up * 1.72f, new Vector3(4.9f, 1.46f, .46f), m.Metal);
            Box("Override screen", panel, new Vector3(0, 1.72f, -.24f),
                new Vector3(4.65f, 1.25f, .04f), m.Glass, false);
            Material signal = m.CheckpointSignal != null ? m.CheckpointSignal : m.Marking;
            var indicator = Box("Override indicator", panel, new Vector3(1.96f, 1.51f, -.29f),
                new Vector3(.28f, .28f, .08f), signal, false).GetComponent<Renderer>();
            Label("LOCKDOWN OVERRIDE", panel, new Vector3(0, 2.10f, -.268f), .21f, font, 4.15f, .34f, m.Lettering);
            TextMesh status = Label(ControlLockdownMission.LockdownDisabled + "\n" + ControlLockdownMission.CompletionMessage,
                panel, new Vector3(-.26f, 1.52f, -.268f), .16f, font, 3.65f, .56f, m.Lettering);
            var mission = panel.gameObject.AddComponent<ControlLockdownMission>();

            var switches = new[]
            {
                BuildSecuritySwitch(props, mission, 1, new Vector3(-5.3f, 0, -5.9f), -26, m, signal, font),
                BuildSecuritySwitch(props, mission, 2, new Vector3(5.3f, 0, -5.9f), 26, m, signal, font),
                BuildSecuritySwitch(props, mission, 3, new Vector3(7.15f, 0, 10.45f), 26, m, signal, font)
            };

            var warningIndicators = new Renderer[2];
            var warningLights = new Light[warningIndicators.Length];
            for (int i = 0; i < warningIndicators.Length; i++)
            {
                float x = i == 0 ? -3.9f : 3.9f;
                Transform beacon = Group("Emergency warning beacon", props, new Vector3(x, 3.8f, 12.24f));
                Box("Beacon housing", beacon, Vector3.zero, new Vector3(.76f, .48f, .39f), m.Metal, false);
                warningIndicators[i] = Box("Beacon lens", beacon, Vector3.back * .22f,
                    new Vector3(.58f, .33f, .1f), signal, false).GetComponent<Renderer>();
                Light light = Group("Warning light", beacon, new Vector3(0, -.12f, -.65f)).gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 5.5f;
                light.intensity = 4f;
                light.shadows = LightShadows.None;
                warningLights[i] = light;
            }

            mission.Configure(switches, indicator, status, warningIndicators, warningLights);
        }

        private static LockdownSecuritySwitch BuildSecuritySwitch(Transform props, ControlLockdownMission mission,
            int number, Vector3 position, float yaw, PrototypeMaterials m, Material signal, Font font)
        {
            string name = $"SWITCH {number}";
            Transform roomSwitch = Group(name, props, position);
            roomSwitch.localRotation = Quaternion.Euler(0, yaw, 0);
            Box("Switch stand", roomSwitch, Vector3.up * .57f, new Vector3(1.4f, 1.14f, .65f), m.Metal);
            Box("Switch housing", roomSwitch, Vector3.up * 1.58f, new Vector3(1.93f, .94f, .38f), m.Metal);
            Box("Switch screen", roomSwitch, new Vector3(0, 1.58f, -.203f),
                new Vector3(1.76f, .77f, .032f), m.Glass, false);
            Box("Switch trim", roomSwitch, new Vector3(0, 1.11f, -.20f),
                new Vector3(1.6f, .055f, .04f), m.Control, false);
            var lamp = Box("Switch indicator", roomSwitch, new Vector3(.59f, 1.47f, -.255f),
                new Vector3(.22f, .22f, .075f), signal, false).GetComponent<Renderer>();
            Label(name, roomSwitch, new Vector3(0, 1.76f, -.223f), .15f, font, 1.57f, .24f, m.Lettering);
            TextMesh status = Label("DISABLED", roomSwitch, new Vector3(-.21f, 1.47f, -.223f), .11f, font, 1.08f, .18f, m.Lettering);
            var component = roomSwitch.gameObject.AddComponent<LockdownSecuritySwitch>();
            component.Configure(mission, number, lamp, status);
            return component;
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
                    Box("Cargo crate", shelf, new Vector3(side * (tier - 1), y + .47f, 0), new Vector3(1.25f, .8f, 1.45f), tier == 1 ? m.Storage : m.Crate);
                }
                Box("Cargo crate", props, new Vector3(side * 7, .85f, -3.8f), new Vector3(1.7f, 1.7f, 1.7f), m.Crate);
            }

            BuildStoragePower(props, m, font);
        }

        internal static void BuildStoragePower(Transform props, PrototypeMaterials m, Font font)
        {
            Transform generator = Group("Emergency generator", props, new Vector3(4.7f, 0, 8.6f));
            generator.localRotation = Quaternion.Euler(0, 22, 0);
            Box("Generator housing", generator, Vector3.up * 1.09f, new Vector3(4.7f, 2.18f, 2.34f), m.Metal);
            Box("Service panel", generator, new Vector3(0, 1.48f, -1.192f),
                new Vector3(4.34f, 1.08f, .08f), m.Glass, false);
            Label("EMERGENCY GENERATOR", generator, new Vector3(0, 1.83f, -1.24f),
                .17f, font, 3.9f, .26f, m.Lettering);
            TextMesh status = Label(StoragePowerMission.PowerRestored + "\n" + StoragePowerMission.CompletionMessage,
                generator, new Vector3(-.21f, 1.33f, -1.24f), .145f, font, 3.52f, .44f, m.Lettering);
            Material signal = m.CheckpointSignal != null ? m.CheckpointSignal : m.Marking;
            var indicator = Box("Generator indicator", generator, new Vector3(1.85f, 1.34f, -1.26f),
                new Vector3(.26f, .26f, .10f), signal, false).GetComponent<Renderer>();
            Cylinder("Vent stack", generator, new Vector3(1.25f, 2.51f, .5f), .43f, .75f, m.Metal);

            Box("Cell bay", generator, new Vector3(0, .61f, -1.2f), new Vector3(3.15f, .49f, .1f), m.Storage, false);
            Label("POWER CELLS", generator, new Vector3(-.72f, .61f, -1.263f), .12f, font, 1.37f, .22f, m.Lettering);
            var installedCells = new Renderer[2];
            for (int i = 0; i < installedCells.Length; i++)
            {
                float x = .35f + .56f * i;
                Box("Cell socket", generator, new Vector3(x, .61f, -1.27f), new Vector3(.42f, .30f, .09f), m.Glass, false);
                installedCells[i] = Box("Installed power cell", generator, new Vector3(x, .61f, -1.33f),
                    new Vector3(.29f, .2f, .055f), signal, false).GetComponent<Renderer>();
            }

            Light powerLight = Group("Restored power light", generator, new Vector3(0, 2.50f, -1.44f)).gameObject.AddComponent<Light>();
            powerLight.type = LightType.Point;
            powerLight.range = 5f;
            powerLight.intensity = 6f;
            powerLight.shadows = LightShadows.None;
            var mission = generator.gameObject.AddComponent<StoragePowerMission>();

            // The feet of the cells sit on the crate top and the middle shelf deck, outside the aisle.
            var cells = new[]
            {
                BuildPowerCell(props, "Power Cell 1", new Vector3(-7, 1.7f, -4.15f), mission, m, signal),
                BuildPowerCell(props, "Power Cell 2", new Vector3(6.18f, 1.60f, 2.84f), mission, m, signal)
            };
            mission.Configure(cells, indicator, installedCells, status, powerLight);
        }

        private static PowerCellPickup BuildPowerCell(Transform props, string name, Vector3 position,
            StoragePowerMission mission, PrototypeMaterials m, Material signal)
        {
            Transform cell = Group(name, props, position);
            Cylinder("Power cell core", cell, Vector3.up * .39f, .48f, .53f, signal, false);
            Cylinder("Cell base", cell, Vector3.up * .075f, .58f, .15f, m.Metal, false);
            Cylinder("Cell cap", cell, Vector3.up * .70f, .54f, .16f, m.Metal, false);
            Cylinder("Top contact", cell, Vector3.up * .815f, .20f, .09f, m.Storage, false);
            var collider = cell.gameObject.AddComponent<BoxCollider>();
            collider.center = Vector3.up * .43f;
            collider.size = new Vector3(.57f, .86f, .57f);

            Light light = Group("Power cell glow", cell, new Vector3(0, .44f, -.32f)).gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(.18f, .87f, 1f);
            light.range = 2.25f;
            light.intensity = 3.3f;
            light.shadows = LightShadows.None;
            var pickup = cell.gameObject.AddComponent<PowerCellPickup>();
            pickup.Configure(mission);
            return pickup;
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
