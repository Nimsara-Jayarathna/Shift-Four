using UnityEngine;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    internal static class ServerExteriorBuilder
    {
        internal static void Build(Transform building, Vector3 size, float front, PrototypeMaterials materials, Font font)
        {
            Transform exterior = Group("ExteriorDetails", building);
            Material signal = materials.CheckpointSignal != null ? materials.CheckpointSignal : materials.Marking;
            BuildFacade(Group("Cladding and services", exterior), size, front, materials, signal);
            BuildEntrance(Group("Entrance", exterior, Vector3.forward * front), materials, signal, font);
            BuildRoof(Group("Rooftop equipment", exterior, Vector3.up * (size.y + .5f)), front, materials, signal);
            BuildAvenueSign(exterior, size, materials, signal, font);
        }

        private static void BuildFacade(Transform cladding, Vector3 size, float front, PrototypeMaterials m, Material signal)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Transform wall = Group(side < 0 ? "West wall" : "East wall", cladding, new Vector3(side * size.x * .5f, 0, 0));
                wall.localRotation = Quaternion.Euler(0, -side * 90f, 0);
                for (int bay = -2; bay <= 2; bay++)
                {
                    float x = bay * 5f;
                    Panel("Armor panel", wall, new Vector3(x, 11.5f, -.16f), new Vector3(4.82f, 7.8f, .4f), m.Wall);
                    Panel("Panel seam", wall, new Vector3(x, 12.86f, -.39f), new Vector3(4.82f, .12f, .1f), m.Metal);
                    Panel("Glazing strip", wall, new Vector3(x, 5.8f, -.21f), new Vector3(4.83f, 2.7f, .43f), m.Glass);
                    Panel("Vertical support", wall, new Vector3(x + 2.44f, 7.35f, -.43f), new Vector3(.47f, 14.7f, .81f), m.Concrete, solid: true);
                }

                Panel("Side sill", wall, new Vector3(0, 3.94f, -.39f), new Vector3(25.6f, .53f, .6f), m.Server);
                Panel("Cable tray", wall, new Vector3(0, 2.73f, -.29f), new Vector3(24.5f, .32f, .49f), m.Metal);
                for (int line = 0; line < 2; line++)
                {
                    float y = 2.07f - .4f * line;
                    Pipe("Coolant line", wall, new Vector3(-11.85f, y, -.59f), new Vector3(10.75f, y, -.59f), .23f, m.Concrete);
                    Pipe("Vertical return", wall, new Vector3(10.75f, y, -.59f), new Vector3(10.75f, y + 1.45f, -.59f), .23f, m.Concrete);
                }

                Panel("Service cabinet", wall, new Vector3(-7.7f, 1.2f, -.53f), new Vector3(2.3f, 2.2f, .8f), m.Metal, solid: true);
                Panel("Cabinet face", wall, new Vector3(-7.7f, 1.24f, -.95f), new Vector3(1.97f, 1.88f, .10f), m.Server);
                Panel("Cabinet status", wall, new Vector3(-7.32f, 1.9f, -1.01f), new Vector3(.57f, .09f, .06f), signal);

                float facadeX = side * 6.7f;
                Panel("Front armor panel", cladding, new Vector3(facadeX, 12.35f, front - .19f),
                    new Vector3(10.38f, 6.45f, .48f), m.Wall);
                Panel("Vertical corner pier", cladding, new Vector3(side * 11.75f, 7.9f, front - .47f),
                    new Vector3(.87f, 15.8f, .93f), m.Concrete, solid: true);
                Panel("Inset louver surround", cladding, new Vector3(facadeX, 12.33f, front - .47f),
                    new Vector3(8.45f, 3.76f, .42f), m.Metal);
                for (int slat = 0; slat < 5; slat++)
                {
                    var grille = Panel("Angled louver blade", cladding, new Vector3(facadeX, 11.03f + slat * .61f, front - .72f),
                        new Vector3(7.96f, .18f, .62f), m.Concrete);
                    grille.localRotation = Quaternion.Euler(22f, 0, 0);
                }
            }

            Panel("Front roof girder", cladding, new Vector3(0, 15.6f, front - .62f),
                new Vector3(24.85f, .95f, .98f), m.Metal);
            Panel("Girder accent", cladding, new Vector3(0, 15.80f, front - 1.13f),
                new Vector3(23.65f, .16f, .06f), m.Server);
        }

        private static void BuildEntrance(Transform entrance, PrototypeMaterials m, Material signal, Font font)
        {
            Panel("Sign housing", entrance, new Vector3(0, 8.08f, -.79f), new Vector3(17.8f, 2.85f, 1.08f), m.Metal);
            Panel("Sign face", entrance, new Vector3(0, 8.08f, -1.36f), new Vector3(16.8f, 2.33f, .12f), m.Glass);
            Label("SERVER FACILITY 02", entrance, new Vector3(0, 8.10f, -1.44f),
                .93f, font, 15.9f, 1.24f, m.Lettering);
            Panel("Upper fascia light", entrance, new Vector3(0, 9.17f, -1.45f), new Vector3(16.25f, .12f, .1f), signal);
            Panel("Lower fascia light", entrance, new Vector3(0, 6.98f, -1.45f), new Vector3(16.25f, .10f, .1f), signal);
            Panel("Reinforced header", entrance, new Vector3(0, 4.56f, -.59f), new Vector3(6.55f, 1.1f, 1.23f), m.Concrete, solid: true);
            Panel("Header light", entrance, new Vector3(0, 4.31f, -1.23f), new Vector3(3.8f, .11f, .08f), signal);
            Label("DATA & RESEARCH", entrance, new Vector3(0, 4.71f, -1.22f),
                .32f, font, 4.45f, .35f, m.Lettering);

            for (int side = -1; side <= 1; side += 2)
            {
                // The center 4 m opening stays clear. Markings sit on the existing landing and approach.
                Panel("Reinforced jamb", entrance, new Vector3(side * 2.69f, 2.2f, -.53f), new Vector3(1.07f, 4.4f, 1.12f), m.Concrete, solid: true);
                Panel("Jamb recess", entrance, new Vector3(side * 2.69f, 2.2f, -1.13f), new Vector3(.69f, 3.82f, .14f), m.Metal);
                Panel("Cyan entrance strip", entrance, new Vector3(side * 2.69f, 2.35f, -1.22f), new Vector3(.14f, 2.9f, .07f), signal);
                Panel("Safety panel", entrance, new Vector3(side * 2.69f, .65f, -1.23f), new Vector3(.62f, .7f, .10f), m.Metal);
                for (int stripe = 0; stripe < 3; stripe++)
                {
                    var safety = Panel("Jamb hazard stripe", entrance, new Vector3(side * 2.69f, .45f + stripe * .19f, -1.29f),
                        new Vector3(.45f, .106f, .024f), m.Checkpoint);
                    safety.localRotation = Quaternion.Euler(0, 0, 20f);
                }
                for (int stripe = 0; stripe < 5; stripe++)
                {
                    var marking = Panel("Apron hazard stripe", entrance, new Vector3(side * 3.92f, .116f, -1.63f - .57f * stripe),
                        new Vector3(1.45f, .01f, .17f), m.Checkpoint);
                    marking.localRotation = Quaternion.Euler(0, 24f * side, 0);
                }

                Panel("Pavement guide strip", entrance, new Vector3(side * 2.61f, .116f, -3.22f),
                    new Vector3(.09f, .01f, 3.27f), m.Marking);
                // Sits within the width shared by the procedural approach and the fixed-layout road spur.
                Panel("Approach lane inlay", entrance, new Vector3(side * 1.7f, .083f, -6.5f),
                    new Vector3(.09f, .008f, 2.9f), m.Marking);
                var lamp = Group("Cyan inspection light", entrance, new Vector3(side * 3.04f, 3.86f, -1.38f)).gameObject.AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.color = new Color(.20f, .76f, 1f);
                lamp.range = 4.0f;
                lamp.intensity = 3.4f;
                lamp.shadows = LightShadows.None;
            }
        }

        private static void BuildRoof(Transform roof, float front, PrototypeMaterials m, Material signal)
        {
            Transform cooling = Group("Cooling bank", roof, new Vector3(0, 0, front + .95f));
            Panel("Cooling bank deck", cooling, new Vector3(0, .31f, 0), new Vector3(23.38f, .72f, 5.17f), m.Concrete);
            for (int bay = 0; bay < 4; bay++)
                BuildFan(Group($"Axial fan {bay + 1}", cooling, new Vector3((bay - 1.5f) * 5.64f, 2.97f, 0)), m, signal);
            Panel("Bank header rail", cooling, new Vector3(0, 5.58f, -.24f), new Vector3(23.3f, .30f, .55f), m.Concrete);
            Pipe("Cooling supply manifold", cooling, new Vector3(-10.7f, .93f, 2.58f), new Vector3(10.7f, .93f, 2.58f), .41f, m.Server);
            Pipe("Cooling return manifold", cooling, new Vector3(-10.7f, 1.57f, 2.58f), new Vector3(10.7f, 1.57f, 2.58f), .34f, m.Concrete);

            Panel("Network cabinet bank", roof, new Vector3(-8.65f, .96f, -4.8f), new Vector3(4.72f, 1.95f, 3.4f), m.Metal);
            for (int cabinet = 0; cabinet < 4; cabinet++)
            {
                float x = -10.35f + 1.13f * cabinet;
                Panel("Network switch cabinet", roof, new Vector3(x, 1.03f, -6.57f), new Vector3(.98f, 1.71f, .33f), m.Server);
                Panel("Network cabinet indicator", roof, new Vector3(x, 1.5f, -6.76f), new Vector3(.52f, .09f, .09f), signal);
            }
            Panel("Auxiliary switchgear", roof, new Vector3(4.8f, .65f, -4f), new Vector3(3.35f, 1.36f, 2.35f), m.Concrete);
            // The plinth straddles the middle housings, keeping the antenna above the cooling bank from the approach.
            Transform mast = Group("Communications mast", roof, new Vector3(0, 5.35f, front + .3f));
            mast.localScale = new Vector3(1, .83f, 1);
            BuildMast(mast, m, signal);
        }

        private static void BuildFan(Transform fan, PrototypeMaterials m, Material signal)
        {
            Panel("Fan enclosure", fan, Vector3.zero, new Vector3(5.15f, 4.94f, 3.95f), m.Metal);
            Panel("Enclosure service rail", fan, new Vector3(0, 2.22f, -2.01f), new Vector3(4.95f, .28f, .23f), m.Server);
            FanDisc("Intake flange", fan, -2.05f, 4.42f, .37f, m.Concrete);
            FanDisc("Cyan intake ring", fan, -2.27f, 4.14f, .15f, signal);
            FanDisc("Dark intake well", fan, -2.36f, 3.81f, .10f, m.Glass);
            for (int blade = 0; blade < 7; blade++)
            {
                var rotation = Quaternion.Euler(0, 0, blade * (360f / 7));
                Vector3 position = rotation * (Vector3.up * 1.03f) + Vector3.back * 2.44f;
                var vane = Panel("Fan blade", fan, position, new Vector3(.37f, 1.48f, .09f), m.Concrete);
                vane.localRotation = rotation * Quaternion.Euler(0, 0, 24f);
            }
            FanDisc("Impeller hub", fan, -2.50f, .96f, .22f, m.Metal);
            FanDisc("Hub cap", fan, -2.64f, .42f, .09f, m.Server);
        }

        private static void BuildMast(Transform mast, PrototypeMaterials m, Material signal)
        {
            Panel("Mast plinth", mast, new Vector3(0, .45f, 0), new Vector3(2.8f, .9f, 2.8f), m.Concrete);
            Pipe("Central antenna pole", mast, new Vector3(0, .8f, 0), new Vector3(0, 12.5f, 0), .33f, m.Metal);
            for (int side = -1; side <= 1; side += 2)
            {
                Pipe("Mast brace", mast, new Vector3(side * 1.1f, .8f, 0), new Vector3(side * .55f, 9.1f, 0), .19f, m.Concrete);
                Pipe("Antenna crossarm", mast, new Vector3(0, 9.1f, 0), new Vector3(side * 1.35f, 9.1f, 0), .17f, m.Metal);
                Panel("Sector antenna", mast, new Vector3(side * 1.27f, 9.46f, -.11f), new Vector3(.38f, 2.57f, .33f), m.Wall);
            }

            Pipe("Rear mast stay", mast, new Vector3(0, .8f, 1.1f), new Vector3(0, 9.1f, .48f), .19f, m.Concrete);
            Pipe("Lower cross-brace", mast, new Vector3(-.77f, 3.1f, 0), new Vector3(.57f, 8.25f, 0), .16f, m.Server);
            Pipe("Upper cross-brace", mast, new Vector3(.77f, 3.1f, 0), new Vector3(-.57f, 8.25f, 0), .16f, m.Server);
            Panel("Dish mount", mast, new Vector3(0, 7.4f, -.27f), new Vector3(.56f, .74f, .54f), m.Metal);
            FanDisc("Microwave dish", mast, -.62f, 1.84f, .36f, m.Concrete, 7.4f);
            FanDisc("Dish face", mast, -.82f, 1.63f, .07f, m.Glass, 7.4f);
            Pipe("Dish feed horn", mast, new Vector3(0, 7.4f, -.85f), new Vector3(0, 7.4f, -1.23f), .22f, m.Concrete);
            Panel("Antenna beacon", mast, new Vector3(0, 12.62f, 0), new Vector3(.39f, .4f, .39f), signal);
        }

        private static void BuildAvenueSign(Transform exterior, Vector3 size, PrototypeMaterials m, Material signal, Font font)
        {
            Transform plate = Group("Avenue identifier", exterior, new Vector3(size.x * .5f + 1.03f, 10.5f, 0));
            plate.localRotation = Quaternion.Euler(0, -90f, 0);
            Panel("Avenue sign surround", plate, Vector3.zero, new Vector3(16.35f, 2.61f, .48f), m.Metal);
            Panel("Avenue sign face", plate, Vector3.back * .27f, new Vector3(15.86f, 2.26f, .12f), m.Glass);
            Panel("Avenue sign lighting", plate, new Vector3(0, .93f, -.35f), new Vector3(14.8f, .12f, .08f), signal);
            Label("SERVER FACILITY 02", plate, Vector3.back * .35f, .88f, font, 14.8f, 1.25f, m.Lettering);
        }

        private static Transform Panel(string name, Transform parent, Vector3 center, Vector3 dimensions, Material material, bool solid = false)
            => Box(name, parent, center, dimensions, material, solid).transform;

        private static void Pipe(string name, Transform parent, Vector3 start, Vector3 end, float diameter, Material material)
        {
            Vector3 direction = end - start;
            var pipe = Cylinder(name, parent, (start + end) * .5f, diameter, direction.magnitude, material, false);
            pipe.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
        }

        private static void FanDisc(string name, Transform parent, float depth, float diameter, float thickness, Material material,
            float height = 0)
        {
            var disc = Cylinder(name, parent, new Vector3(0, height, depth), diameter, thickness, material, false);
            disc.transform.localRotation = Quaternion.Euler(90f, 0, 0);
        }
    }
}
