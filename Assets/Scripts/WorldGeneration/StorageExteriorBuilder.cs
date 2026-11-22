using UnityEngine;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    internal static class StorageExteriorBuilder
    {
        private static readonly Color Amber = new Color(1f, .49f, .08f);

        internal static void Build(Transform building, Vector3 size, float front, PrototypeMaterials m, Font font)
        {
            Transform exterior = Group("ExteriorDetails", building);
            Material signal = m.CheckpointSignal != null ? m.CheckpointSignal : m.Marking;
            var amber = new MaterialPropertyBlock();
            amber.SetColor("_BaseColor", Amber);

            BuildFront(Group("Warehouse entrance", exterior, Vector3.forward * front), m, signal, amber, font);
            BuildDock(Group("Loading dock", exterior, Vector3.forward * front), m, signal, amber, font);
            BuildCladding(Group("Warehouse cladding", exterior), size, m, signal, amber, font);
            BuildVentilation(Group("Roof ventilation", exterior, Vector3.up * (size.y + .5f)), m);
        }

        private static void BuildFront(Transform entrance, PrototypeMaterials m, Material signal, MaterialPropertyBlock amber, Font font)
        {
            Panel("Sign housing", entrance, new Vector3(0, 7.88f, -.72f), new Vector3(20.4f, 2.74f, 1.18f), m.Metal);
            Panel("Sign face", entrance, new Vector3(0, 7.88f, -1.34f), new Vector3(19.25f, 2.16f, .12f), m.Glass);
            Lamp("Sign top strip", entrance, new Vector3(0, 8.92f, -1.42f), new Vector3(18.9f, .12f, .08f), signal, amber);
            Lamp("Sign bottom strip", entrance, new Vector3(0, 6.82f, -1.42f), new Vector3(18.9f, .10f, .08f), signal, amber);
            Label("STORAGE FACILITY 04", entrance, new Vector3(0, 7.91f, -1.42f), .96f, font, 18.7f, 1.28f, m.Lettering);

            // Above and outside the original ten-metre doorway and its solid canopy.
            Panel("Portal crossbeam", entrance, new Vector3(0, 4.82f, -.72f), new Vector3(12.1f, .78f, 1.36f), m.Concrete);
            Panel("Lintel inset", entrance, new Vector3(0, 4.82f, -1.42f), new Vector3(10.84f, .55f, .12f), m.Metal);
            Label("CARGO HANDLING", entrance, new Vector3(0, 4.83f, -1.50f), .24f, font, 4.6f, .33f, m.Lettering);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 5.73f;
                Panel("Portal upright", entrance, new Vector3(x, 2.31f, -.5f), new Vector3(1.19f, 4.62f, 1.02f), m.Metal);
                Panel("Upright armor", entrance, new Vector3(x, 2.77f, -1.06f), new Vector3(.73f, 3.42f, .18f), m.Concrete);
                Lamp("Portal amber strip", entrance, new Vector3(x, 3f, -1.17f), new Vector3(.12f, 2.63f, .09f), signal, amber);
                Hazards("Portal hazard plate", entrance, new Vector3(x, .69f, -1.13f), 3, m);
                Panel("Apron edge line", entrance, new Vector3(side * 4.97f, .116f, -3.14f),
                    new Vector3(.12f, .008f, 3.41f), m.Marking);
            }

            Floodlight("Entrance floodlight", entrance, new Vector3(-4.18f, 4.31f, -1.58f), m, signal, amber);
        }

        private static void BuildDock(Transform dock, PrototypeMaterials m, Material signal, MaterialPropertyBlock amber, Font font)
        {
            // The dock occupies the east wing of the reserved front yard. The road and Flanker loop lie to the west.
            Panel("Loading platform", dock, new Vector3(10.88f, .069f, -4.26f), new Vector3(9.66f, .14f, 6.9f), m.Concrete);
            // This flush strip meets the old apron and continues its paving into the loading bay.
            Panel("Apron to dock paving", dock, new Vector3(5.86f, .112f, -3.38f), new Vector3(.31f, .012f, 3.05f), m.Concrete);
            Panel("Dock edge", dock, new Vector3(10.9f, .146f, -7.58f), new Vector3(9.46f, .014f, .15f), m.Metal);
            for (int stripe = 0; stripe < 12; stripe++)
            {
                var paint = Panel("Yellow dock hazard marking", dock, new Vector3(6.78f + stripe * .74f, .152f, -7.58f),
                    new Vector3(.40f, .01f, .10f), m.Checkpoint);
                paint.localRotation = Quaternion.Euler(0, 25, 0);
            }
            for (int i = 0; i < 3; i++)
                Panel("Dock expansion joint", dock, new Vector3(8.28f + i * 2.2f, .143f, -4.3f), new Vector3(.06f, .006f, 6.39f), m.Metal);

            Transform cargo = Group("Cargo containers", dock);
            Vector3 cargoBase = new Vector3(13.62f, .139f, -3.27f);
            Container(Group("Freight container", cargo, cargoBase), 4.24f, 2.3f, m.Storage, m, font);
            Container(Group("Upper freight container", cargo, cargoBase + Vector3.up * 2.32f), 4.24f, 2.3f, m.Metal, m, font);
            Container(Group("Transfer container", cargo, new Vector3(10.71f, .139f, -2.03f)), 2.39f, 1.79f, m.Concrete, m, font);
            var loader = Group("Parked forklift", dock, new Vector3(9.31f, .139f, -5.15f));
            loader.localRotation = Quaternion.Euler(0, -26f, 0);
            BuildForklift(loader, m, signal, amber);

            var crane = Group("Overhead cargo crane", dock, new Vector3(11.42f, .139f, -5.92f));
            BuildCrane(crane, m, signal, amber);
            Floodlight("Dock floodlight", crane, new Vector3(2.2f, 5.63f, -.37f), m, signal, amber);
            Label("KEEP CLEAR", crane, new Vector3(0, 6.24f, -.56f), .29f, font, 5.5f, .36f, m.Lettering);

            Transform barrier = Group("Outer dock barrier", dock, new Vector3(15.22f, .139f, -6.75f));
            Panel("Barrier plinth", barrier, new Vector3(0, .24f, 0), new Vector3(.54f, .5f, 1.43f), m.Concrete);
            Panel("Barrier wall", barrier, new Vector3(0, .74f, 0), new Vector3(.34f, .65f, 1.25f), m.Metal);
            Panel("Barrier yellow stripe", barrier, new Vector3(0, .82f, -.65f), new Vector3(.32f, .16f, .08f), m.Checkpoint);
            Panel("Dock warning board", dock, new Vector3(14.88f, 2.64f, -.52f), new Vector3(1.78f, .67f, .13f), m.Metal);
            Label("LOADING ZONE", dock, new Vector3(14.88f, 2.64f, -.60f), .16f, font, 1.62f, .28f, m.Lettering);
        }

        private static void Container(Transform freight, float length, float height, Material siding, PrototypeMaterials m, Font font)
        {
            Panel("Steel shell", freight, Vector3.up * (height * .5f), new Vector3(2.38f, height, length), siding);
            foreach (float y in new[] { .10f, height - .08f })
                Panel("Steel stacking rail", freight, Vector3.up * y, new Vector3(2.56f, .20f, length + .16f), m.Metal);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int rib = 1; rib <= 7; rib++)
                    Panel("Corrugation", freight, new Vector3(side * 1.19f, height * .5f, length * (rib / 8f - .5f)),
                        new Vector3(.115f, height - .42f, .13f), m.Metal);
                Panel("End frame", freight, new Vector3(side * 1.17f, height * .5f, -length * .5f),
                    new Vector3(.17f, height, .21f), m.Metal);
                Panel("Door", freight, new Vector3(side * .56f, height * .5f, -length * .5f - .067f),
                    new Vector3(1.08f, height - .44f, .14f), siding);
                Rod("Door locking bar", freight, new Vector3(side * .79f, .25f, -length * .5f - .17f),
                    new Vector3(side * .79f, height - .26f, -length * .5f - .17f), .075f, m.Wall);
            }
            Label("04", freight, new Vector3(.25f, height * .71f, -length * .5f - .15f), .25f, font, .7f, .35f, m.Lettering);
        }

        private static void BuildForklift(Transform loader, PrototypeMaterials m, Material signal, MaterialPropertyBlock amber)
        {
            Panel("Forklift chassis", loader, new Vector3(0, .61f, .21f), new Vector3(1.68f, .42f, 2.32f), m.Metal);
            Panel("Orange counterweight", loader, new Vector3(0, 1.01f, .89f), new Vector3(1.59f, .63f, .94f), m.Checkpoint);
            Panel("Operator seat", loader, new Vector3(0, 1.19f, .25f), new Vector3(.57f, .23f, .60f), m.Metal);
            Panel("Seat back", loader, new Vector3(0, 1.48f, .53f), new Vector3(.53f, .57f, .14f), m.Metal);
            Panel("Overhead guard", loader, new Vector3(0, 2.36f, .21f), new Vector3(1.55f, .19f, 1.5f), m.Checkpoint);
            Lamp("Forklift marker lamp", loader, new Vector3(0, 2.5f, .24f), new Vector3(.24f, .12f, .21f), signal, amber);
            for (int side = -1; side <= 1; side += 2)
            {
                foreach (float z in new[] { -.60f, .91f })
                {
                    Vector3 axle = new Vector3(side * .82f, .375f, z);
                    Rod("Rubber tire", loader, axle - Vector3.right * .14f, axle + Vector3.right * .14f, .75f, m.Metal);
                    Rod("Steel wheel hub", loader, axle + Vector3.right * (side * .14f), axle + Vector3.right * (side * .18f), .31f, m.Concrete);
                    Panel("Guard upright", loader, new Vector3(side * .7f, 1.47f, z * .74f), new Vector3(.13f, 1.75f, .14f), m.Metal);
                }
                Panel("Lift mast rail", loader, new Vector3(side * .51f, 1.28f, -1.04f), new Vector3(.19f, 2.47f, .22f), m.Metal);
                Panel("Steel fork", loader, new Vector3(side * .38f, .24f, -1.78f), new Vector3(.18f, .09f, 1.55f), m.Concrete);
            }
            Panel("Fork carriage", loader, new Vector3(0, .63f, -1.17f), new Vector3(1.28f, .57f, .18f), m.Concrete);
            Panel("Mast crossbar", loader, new Vector3(0, 2.38f, -1.04f), new Vector3(1.19f, .15f, .22f), m.Metal);
        }

        private static void BuildCrane(Transform crane, PrototypeMaterials m, Material signal, MaterialPropertyBlock amber)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 3.98f;
                Panel("Gantry foot", crane, new Vector3(x, .16f, 0), new Vector3(.68f, .31f, .81f), m.Concrete);
                Panel("Steel gantry column", crane, new Vector3(x, 3.08f, 0), new Vector3(.40f, 6.16f, .48f), m.Metal);
                Panel("Column inset", crane, new Vector3(x, 3.24f, -.246f), new Vector3(.17f, 4.12f, .08f), m.Storage);
                Hazards("Gantry hazard plate", crane, new Vector3(x, 1.08f, -.30f), 4, m);
                Rod("Gantry brace", crane, new Vector3(x, 4.8f, 0), new Vector3(x - side * 1.17f, 6.05f, 0), .24f, m.Concrete);
            }
            Panel("Gantry box girder", crane, new Vector3(0, 6.24f, 0), new Vector3(8.58f, .81f, 1.02f), m.Metal);
            Panel("Orange girder edge", crane, new Vector3(0, 6.61f, -.52f), new Vector3(8.31f, .14f, .09f), m.Storage);
            Panel("Hoist rail", crane, new Vector3(0, 5.77f, 0), new Vector3(7.78f, .20f, .51f), m.Concrete);
            Panel("Hoist trolley", crane, new Vector3(-.05f, 5.42f, 0), new Vector3(1.23f, .63f, .75f), m.Storage);
            Rod("Hoist cable", crane, new Vector3(-.05f, 5.11f, 0), new Vector3(-.05f, 3.88f, 0), .083f, m.Metal);
            Panel("Lift block", crane, new Vector3(-.05f, 3.76f, 0), new Vector3(.5f, .31f, .34f), m.Checkpoint);
            Rod("Hook shank", crane, new Vector3(-.07f, 3.69f, 0), new Vector3(-.07f, 3.33f, 0), .105f, m.Metal);
            Rod("Hook bend", crane, new Vector3(-.07f, 3.33f, 0), new Vector3(.18f, 3.25f, 0), .105f, m.Metal);
            Rod("Hook tip", crane, new Vector3(.18f, 3.25f, 0), new Vector3(.26f, 3.44f, 0), .105f, m.Metal);
            Lamp("Trolley indicator", crane, new Vector3(-.06f, 5.4f, -.41f), new Vector3(.21f, .11f, .09f), signal, amber);
        }

        private static void BuildCladding(Transform cladding, Vector3 size, PrototypeMaterials m, Material signal,
            MaterialPropertyBlock amber, Font font)
        {
            float front = -size.z * .5f;
            for (int side = -1; side <= 1; side += 2)
            {
                Panel("Front wall armor", cladding, new Vector3(side * 10.72f, 5.10f, front - .16f), new Vector3(8.51f, 9.45f, .4f), m.Concrete);
                Panel("Reinforced corner", cladding, new Vector3(side * 14.62f, 5.16f, front - .39f), new Vector3(1.16f, 10.24f, .88f), m.Metal);
                Panel("Corner wear plate", cladding, new Vector3(side * 14.62f, 4.18f, front - .86f), new Vector3(.57f, 6.3f, .15f), m.Storage);
                Transform wall = Group(side < 0 ? "Avenue wall" : "Service wall", cladding, Vector3.right * (side * size.x * .5f));
                wall.localRotation = Quaternion.Euler(0, -side * 90f, 0);
                for (int bay = -1; bay <= 1; bay++)
                {
                    float x = bay * 7.88f;
                    Panel("Warehouse wall panel", wall, new Vector3(x, 5.5f, -.18f), new Vector3(7.63f, 8.57f, .43f), m.Concrete);
                    Panel("Upper panel seam", wall, new Vector3(x, 6.41f, -.43f), new Vector3(7.44f, .18f, .14f), m.Metal);
                    Panel("Structural pilaster", wall, new Vector3(x - 3.82f, 5.14f, -.36f), new Vector3(.49f, 10.26f, .67f), m.Metal);
                }
                Panel("Dark base rail", wall, new Vector3(0, 1.37f, -.45f), new Vector3(23.45f, .52f, .39f), m.Metal);
                Rod("Surface conduit", wall, new Vector3(-9.7f, 2.22f, -.51f), new Vector3(9.95f, 2.22f, -.51f), .21f, m.Wall);
                Panel("Utility box", wall, new Vector3(9.48f, 1.52f, -.61f), new Vector3(1.81f, 1.45f, .69f), m.Metal);
                Lamp("Utility indicator", wall, new Vector3(9.7f, 1.74f, -.97f), new Vector3(.34f, .1f, .07f), signal, amber);
                if (side < 0)
                {
                    Panel("Avenue sign housing", wall, new Vector3(0, 7.52f, -.79f), new Vector3(16.4f, 2.53f, .74f), m.Metal);
                    Lamp("Avenue sign strip", wall, new Vector3(0, 8.42f, -1.2f), new Vector3(15.25f, .12f, .08f), signal, amber);
                    Label("STORAGE FACILITY 04", wall, new Vector3(0, 7.49f, -1.19f), .85f, font, 15.38f, 1.13f, m.Lettering);
                }
            }
            Transform generator = Group("Exterior generator housing", cladding, new Vector3(size.x * .5f, 0, 7.62f));
            generator.localRotation = Quaternion.Euler(0, -90, 0);
            Panel("Generator cabinet", generator, new Vector3(0, 1.08f, -.51f), new Vector3(3.31f, 2.15f, .87f), m.Metal);
            Panel("Generator vent panel", generator, new Vector3(0, 1.14f, -.97f), new Vector3(2.88f, 1.63f, .10f), m.Storage);
            for (int louver = 0; louver < 5; louver++)
                Panel("Generator louver", generator, new Vector3(.26f, .63f + louver * .22f, -1.04f), new Vector3(2.09f, .08f, .075f), m.Metal);
        }

        private static void BuildVentilation(Transform roof, PrototypeMaterials m)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Transform plant = Group("Rooftop exhaust plant", roof, new Vector3(side * 10.7f, 0, -7.84f));
                Panel("Ventilation machinery", plant, new Vector3(0, .84f, 0), new Vector3(4.6f, 1.68f, 3.54f), m.Metal);
                Panel("Ventilation cap", plant, new Vector3(0, 1.73f, 0), new Vector3(4.78f, .25f, 3.64f), m.Concrete);
                for (int vane = 0; vane < 5; vane++)
                    Panel("Ventilation grille", plant, new Vector3(0, .30f + vane * .24f, -1.79f), new Vector3(3.86f, .11f, .16f), m.Concrete);
                for (int stack = 0; stack < 2; stack++)
                {
                    Vector3 foot = new Vector3(-.93f + stack * 1.63f, 1.75f, .45f);
                    Vector3 tip = foot + Vector3.up * (2.36f + stack * .52f);
                    Rod("Exhaust stack", plant, foot, tip, .56f, m.Concrete);
                    Rod("Stack collar", plant, tip - Vector3.up * .33f, tip - Vector3.up * .19f, .73f, m.Storage);
                    Rod("Dark exhaust outlet", plant, tip, tip + Vector3.up * .11f, .61f, m.Metal);
                }
            }
        }

        private static void Floodlight(string name, Transform parent, Vector3 position, PrototypeMaterials m, Material signal,
            MaterialPropertyBlock amber)
        {
            Transform lamp = Group(name, parent, position);
            Panel("Floodlight housing", lamp, Vector3.zero, new Vector3(.78f, .34f, .43f), m.Metal);
            Lamp("Floodlight lens", lamp, new Vector3(0, -.09f, -.19f), new Vector3(.66f, .16f, .10f), signal, amber);
            var light = Group("Amber light", lamp, new Vector3(0, -.3f, -.37f)).gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 4.8f;
            light.intensity = 3.2f;
            light.color = Amber;
            light.shadows = LightShadows.None;
        }

        private static void Hazards(string name, Transform parent, Vector3 position, int stripes, PrototypeMaterials m)
        {
            Transform plate = Group(name, parent, position);
            Panel("Dark plate", plate, Vector3.zero, new Vector3(.49f, .23f * stripes + .21f, .08f), m.Metal);
            for (int stripe = 0; stripe < stripes; stripe++)
            {
                float y = (stripe - (stripes - 1) * .5f) * .21f;
                Panel("Yellow stripe", plate, new Vector3(0, y, -.05f), new Vector3(.39f, .095f, .022f), m.Checkpoint)
                    .localRotation = Quaternion.Euler(0, 0, 25f);
            }
        }

        private static void Lamp(string name, Transform parent, Vector3 position, Vector3 size, Material material,
            MaterialPropertyBlock tint)
        {
            var renderer = Panel(name, parent, position, size, material).GetComponent<Renderer>();
            renderer.SetPropertyBlock(tint);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Transform Panel(string name, Transform parent, Vector3 position, Vector3 size, Material material)
            => Box(name, parent, position, size, material, false).transform;

        private static void Rod(string name, Transform parent, Vector3 start, Vector3 end, float diameter, Material material)
        {
            Vector3 direction = end - start;
            var rod = Cylinder(name, parent, (start + end) * .5f, diameter, direction.magnitude, material, false);
            rod.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
        }
    }
}
