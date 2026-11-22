using UnityEngine;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    public static class ResearchBuildingBuilder
    {
        public const float FloorHeight = .2f;
        private const float WallThickness = .45f;
        private const float EntranceHeight = 3.8f;

        private static readonly Vector3[] Sizes =
        {
            new Vector3(24, 10, 24), new Vector3(24, 16, 26),
            new Vector3(26, 12, 26), new Vector3(30, 10, 24)
        };

        public static float FrontOffset(int index) => Sizes[index].z * .5f;

        public static void Build(Transform parent, PrototypeMaterials materials, Font font, Vector3[] positions = null)
        {
            bool fixedLayout = positions == null;
            positions ??= new[]
            {
                new Vector3(-42, 0, -18), new Vector3(-46, 0, 46),
                new Vector3(46, 0, 46), new Vector3(42, 0, -18)
            };
            Building(parent, "Checkpoint", "01 / SECURITY INTAKE", positions[0], materials.Checkpoint, materials, font, 0, fixedLayout);
            Building(parent, "Server", "02 / DATA & RESEARCH", positions[1], materials.Server, materials, font, 1, fixedLayout);
            Building(parent, "Control", "03 / FACILITY OPERATIONS", positions[2], materials.Control, materials, font, 2, fixedLayout);
            Building(parent, "Storage", "04 / LOGISTICS HANGAR", positions[3], materials.Storage, materials, font, 3, fixedLayout);
        }

        private static void Building(Transform parent, string name, string subtitle,
            Vector3 position, Material accent, PrototypeMaterials m, Font font, int type, bool fixedLayout)
        {
            Vector3 size = Sizes[type];
            Transform root = Group(name, parent, position);
            float front = -size.z * .5f;
            Box("Foundation", root, Vector3.up * (FloorHeight * .5f), new Vector3(size.x + 2, FloorHeight, size.z + 2), m.Concrete);
            Box("Roof cap", root, Vector3.up * (size.y + .25f), new Vector3(size.x + 1, .5f, size.z + 1), m.Metal);
            Box("Identity band", root, new Vector3(0, size.y - 1.5f, front - .12f), new Vector3(size.x + .1f, 2, .18f), m.Metal, false);
            Box("Accent lintel", root, new Vector3(0, size.y - .35f, front - .2f), new Vector3(size.x, .16f, .22f), accent, false);
            if (type != 0)
                Label(name.ToUpperInvariant(), root, new Vector3(0, size.y - 1.45f, front - .24f), .48f, font, size.x - 3, 1.8f, m.Lettering);
            Box("Entrance sign", root, new Vector3(0, 4.5f, front - .18f), new Vector3(15, .9f, .18f), m.Metal, false);
            Label(subtitle, root, new Vector3(0, 4.5f, front - .3f), .19f, font, 14, .6f, m.Lettering);
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Entry jamb", root, new Vector3(side * (type == 3 ? 5.2f : 2.2f), 2, front - .25f), new Vector3(.22f, 4, .28f), accent, false);
                Box("Corner pier", root, new Vector3(side * (size.x * .5f - .65f), size.y * .5f, front - .15f), new Vector3(1.3f, size.y, .4f), m.Concrete, false);
            }
            Box("Entry apron", root, new Vector3(0, .055f, front - 2.5f), new Vector3(12, .11f, 5), m.Concrete);
            if (fixedLayout)
                Box("Road connection", root, new Vector3(0, .035f, front - 5), new Vector3(4, .07f, 10), m.Concrete);

            if (type == 0) Checkpoint(root, size, front, accent, m, font);
            else if (type == 1) Server(root, size, front, accent, m);
            else if (type == 2) Control(root, size, front, accent, m);
            else Storage(root, size, front, accent, m);

            AddWalkableInterior(root, m, font, type);
        }

        public static void AddWalkableInterior(Transform root, PrototypeMaterials m, Font font, int type)
        {
            Vector3 size = Sizes[type];
            float front = -FrontOffset(type);
            float entranceWidth = type == 3 ? 10f : 4f;
            Material facade = type == 1 ? m.Metal : m.Wall;
            Transform walls = Group("Walls", root);
            float sideX = (size.x - WallThickness) * .5f;
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Side wall", walls, new Vector3(side * sideX, size.y * .5f, 0),
                    new Vector3(WallThickness, size.y, size.z), facade);
                float panelWidth = (size.x - entranceWidth) * .5f;
                Box("Entrance wall", walls, new Vector3(side * (entranceWidth + panelWidth) * .5f,
                    size.y * .5f, front + WallThickness * .5f), new Vector3(panelWidth, size.y, WallThickness), facade);
            }
            Box("Rear wall", walls, new Vector3(0, size.y * .5f, -front - WallThickness * .5f),
                new Vector3(size.x, size.y, WallThickness), facade);
            float openingTop = FloorHeight + EntranceHeight;
            Box("Door header", walls, new Vector3(0, (size.y + openingTop) * .5f, front + WallThickness * .5f),
                new Vector3(entranceWidth, size.y - openingTop, WallThickness), facade);

            ResearchInteriorBuilder.Build(Group("Interior", root, Vector3.up * FloorHeight), size, m, font, type);
        }

        private static void Checkpoint(Transform root, Vector3 size, float front, Material accent, PrototypeMaterials m, Font font)
        {
            Box("Security canopy", root, new Vector3(0, 5.5f, front - 3), new Vector3(16, .45f, 6), m.Metal);
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Canopy column", root, new Vector3(side * 7, 2.6f, front - 5), new Vector3(.35f, 5.2f, .35f), m.Concrete);
                Box("Observation window", root, new Vector3(side * 7, 2.8f, front - .25f), new Vector3(5, 2, .14f), m.Glass, false);
                Box("Security bollard", root, new Vector3(side * 3, .7f, front - 7), new Vector3(.4f, 1.4f, .4f), accent);
            }
            Box("Roof equipment", root, new Vector3(-6, size.y + 1, 3), new Vector3(5, 1.5f, 5), m.Concrete);

            // Resolve the imported model itself by path. A scene reference to an imported object
            // can deserialize as missing even when the FBX is present.
            var model = Resources.Load<GameObject>("SF_CheckpointExterior");
            Transform exterior = Group("ExteriorDetails", root);
            if (model != null)
                Object.Instantiate(model, exterior, false).name = "Armored shell";
            else
            {
                Debug.LogWarning("Open World Checkpoint exterior: SF_CheckpointExterior is missing. Using built-in exterior geometry.", exterior);
                BuildCheckpointExteriorFallback(exterior, size, front, m);
            }

            Label("CHECKPOINT 01", exterior, new Vector3(0, 6.27f, front - 6.54f), .85f, font, 13.82f, 1.42f, m.Lettering);
            var sideSign = Label("CHECKPOINT 01", exterior, new Vector3(size.x * .5f + 1.31f, 7.64f, 0),
                .85f, font, 12.65f, 1.85f, m.Lettering);
            sideSign.transform.localRotation = Quaternion.Euler(0, -90, 0);
            Label("SECURITY INTAKE", exterior, new Vector3(0, 4.83f, front - 1.33f),
                .22f, font, 4.3f, .27f, m.Lettering);

            Material signal = m.CheckpointSignal != null ? m.CheckpointSignal : m.Marking;
            var markerTint = new MaterialPropertyBlock();
            markerTint.SetColor("_BaseColor", new Color(1f, .83f, .05f));
            Transform marker = Group("Temporary exterior test marker", exterior, new Vector3(0, 8.18f, front - 6.52f));
            var back = Box("Yellow marker board", marker, Vector3.zero, new Vector3(13.4f, 1.32f, .2f), signal, false);
            back.GetComponent<Renderer>().SetPropertyBlock(markerTint);
            var text = Label("TEST EXTERIOR UPGRADE", marker, new Vector3(0, 0, -.12f),
                .64f, font, 12.8f, .96f, m.Lettering);
            var markerText = text.GetComponent<Renderer>();
            markerText.GetPropertyBlock(markerTint);
            markerTint.SetColor("_BaseColor", new Color(.07f, .06f, .03f));
            markerText.SetPropertyBlock(markerTint);
            markerTint.Clear();

            for (int side = -1; side <= 1; side += 2)
            {
                bool red = side < 0;
                Color color = red ? new Color(1, .13f, .09f) : new Color(.83f, .94f, 1);
                var lens = Box(red ? "Red warning lens" : "White inspection lens", exterior,
                    new Vector3(side * 3.72f, red ? 3.78f : 3.29f, front - .996f),
                    new Vector3(.38f, red ? .38f : .22f, .12f), signal, false);
                markerTint.SetColor("_BaseColor", color);
                lens.GetComponent<Renderer>().SetPropertyBlock(markerTint);

                var lamp = Group(red ? "Red warning light" : "White inspection light", exterior,
                    lens.transform.localPosition + Vector3.back * .12f).gameObject.AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.color = color;
                lamp.range = 3.4f;
                lamp.intensity = 3.2f;
                lamp.shadows = LightShadows.None;
            }
        }

        private static void BuildCheckpointExteriorFallback(Transform exterior, Vector3 size, float front, PrototypeMaterials m)
        {
            Material signal = m.CheckpointSignal != null ? m.CheckpointSignal : m.Marking;
            Box("Entrance fascia", exterior, new Vector3(0, 6.27f, front - 6.21f),
                new Vector3(16f, 1.78f, .6f), m.Metal, false);
            Box("Fascia light strip", exterior, new Vector3(0, 6.98f, front - 6.53f),
                new Vector3(15f, .10f, .09f), signal, false);
            Box("Avenue sign panel", exterior, new Vector3(size.x * .5f + 1.1f, 7.64f, 0),
                new Vector3(.35f, 2.5f, 14f), m.Metal, false);

            for (int side = -1; side <= 1; side += 2)
            {
                Box("Armored jamb", exterior, new Vector3(side * 2.73f, 2.22f, front - .66f),
                    new Vector3(1.02f, 4.4f, 1f), m.Metal, false);
                Box("Entrance light strip", exterior, new Vector3(side * 2.73f, 2.22f, front - 1.18f),
                    new Vector3(.14f, 3.2f, .08f), signal, false);
                Box("Reinforced barrier", exterior, new Vector3(side * 9.15f, .53f, front - 1.05f),
                    new Vector3(4.5f, 1f, .66f), m.Metal, false);
                Box("Barrier amber trim", exterior, new Vector3(side * 9.15f, .53f, front - 1.4f),
                    new Vector3(1.6f, .19f, .06f), m.Checkpoint, false);
                Box("Rooftop vent housing", exterior, new Vector3(side * 3.2f, size.y + 2.15f, 2.6f),
                    new Vector3(4.4f, .65f, 3.8f), m.Metal, false);
                Cylinder("Vent grille", exterior, new Vector3(side * 3.2f, size.y + 2.55f, 2.6f),
                    2.55f, .18f, m.Concrete, false);
            }

            Cylinder("Radio mast", exterior, new Vector3(8, size.y + 3.5f, -5.4f), .38f, 6f, m.Metal, false);
        }

        private static void Server(Transform root, Vector3 size, float front, Material accent, PrototypeMaterials m)
        {
            for (int i = -1; i <= 1; i++)
            {
                Box("Cooling tower", root, new Vector3(i * 6, size.y + 1.8f, 3), new Vector3(4, 3, 9), m.Concrete);
                Cylinder("Cooling fan", root, new Vector3(i * 6, size.y + 3.4f, 3), 3.2f, .25f, m.Metal, false);
                Box("Data facade fin", root, new Vector3(i * 6, 8.5f, front - .3f), new Vector3(.45f, 6.5f, .5f), accent, false);
            }
            for (int side = -1; side <= 1; side += 2)
                Box("Server window bank", root, new Vector3(side * 6, 6.5f, front - .2f), new Vector3(7, 1.4f, .16f), m.Glass, false);
        }

        private static void Control(Transform root, Vector3 size, float front, Material accent, PrototypeMaterials m)
        {
            Box("Setback operations floor", root, new Vector3(0, size.y + 2, 2), new Vector3(17, 4, 15), m.Glass);
            Box("Operations roof", root, new Vector3(0, size.y + 4.15f, 2), new Vector3(18, .3f, 16), m.Metal);
            Cylinder("Communications mast", root, new Vector3(5, size.y + 7, 3), .3f, 6, m.Metal);
            Sphere("Communications radome", root, new Vector3(5, size.y + 10, 3), Vector3.one * 2.7f, m.Concrete, false);
            Box("Operations window", root, new Vector3(0, 6.8f, front - .22f), new Vector3(18, 1.3f, .18f), m.Glass, false);
            Box("Mast beacon", root, new Vector3(5, size.y + 11.5f, 3), Vector3.one * .35f, accent, false);
        }

        private static void Storage(Transform root, Vector3 size, float front, Material accent, PrototypeMaterials m)
        {
            for (int i = -2; i <= 2; i++)
            {
                Box("Roof skylight", root, new Vector3(i * 5, size.y + .6f, 0), new Vector3(2, .4f, 17), m.Glass, false);
            }
            Box("Loading canopy", root, new Vector3(0, 5.6f, front - 2), new Vector3(19, .35f, 4), accent);
        }

    }
}
