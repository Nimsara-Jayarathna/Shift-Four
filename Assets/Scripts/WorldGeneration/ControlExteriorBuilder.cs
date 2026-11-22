using System.Collections.Generic;
using UnityEngine;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    internal static class ControlExteriorBuilder
    {
        private static readonly Color WarningRed = new Color(1f, .11f, .072f);
        private static readonly Color SignRed = new Color(.88f, .23f, .19f);

        internal static void Build(Transform building, float width, float front, Transform operationsRoof,
            PrototypeMaterials materials, Font font)
        {
            Transform exterior = Group("ExteriorDetails", building);
            Material signal = materials.CheckpointSignal != null ? materials.CheckpointSignal : materials.Marking;
            var red = new MaterialPropertyBlock();
            red.SetColor("_BaseColor", SignRed);
            var beacons = new List<Renderer>(4);

            BuildEntrance(Group("Main entrance", exterior, Vector3.forward * front), materials, signal, red, font, beacons);
            BuildCladding(Group("Cladding and services", exterior), width, front, materials, signal, red, font);

            // The new structure bears directly on the original solid operations roof.
            Vector3 roofTop = operationsRoof.localPosition + Vector3.up * (operationsRoof.localScale.y * .5f);
            Transform tower = Group("Command tower", exterior, roofTop + Vector3.back * 2.4f);
            BuildTower(tower, materials, signal, red, beacons);

            var mission = building.GetComponentInChildren<ControlLockdownMission>(true);
            exterior.gameObject.AddComponent<ControlExteriorBeacons>().Configure(mission, beacons.ToArray());
        }

        private static void BuildEntrance(Transform entrance, PrototypeMaterials m, Material signal, MaterialPropertyBlock red,
            Font font, List<Renderer> beacons)
        {
            Panel("Sign surround", entrance, new Vector3(0, 8f, -.69f), new Vector3(18.3f, 2.72f, 1.16f), m.Metal);
            Panel("Inset sign face", entrance, new Vector3(0, 8f, -1.3f), new Vector3(16.95f, 2.12f, .12f), m.Glass);
            Accent("Upper sign light", entrance, new Vector3(0, 9f, -1.39f), new Vector3(16.5f, .13f, .075f), signal, red);
            Accent("Lower sign light", entrance, new Vector3(0, 7f, -1.39f), new Vector3(16.5f, .10f, .075f), signal, red);
            Label("CONTROL CENTER 03", entrance, new Vector3(0, 8.01f, -1.4f), .94f, font, 15.8f, 1.29f, m.Lettering);

            Panel("Portal lintel", entrance, new Vector3(0, 4.65f, -.78f), new Vector3(6.7f, 1.18f, 1.38f), m.Concrete);
            Panel("Header insert", entrance, new Vector3(0, 4.7f, -1.49f), new Vector3(5.26f, .78f, .08f), m.Metal);
            Label("OPERATIONS ACCESS", entrance, new Vector3(0, 4.79f, -1.55f), .25f, font, 4.9f, .31f, m.Lettering);
            Accent("Lintel strip", entrance, new Vector3(0, 4.45f, -1.55f), new Vector3(4.85f, .10f, .07f), signal, red);

            for (int side = -1; side <= 1; side += 2)
            {
                // The inner sides of the new jambs stay outside the existing four-metre doorway.
                float jamb = side * 2.74f;
                Panel("Reinforced portal column", entrance, new Vector3(jamb, 2.29f, -.72f), new Vector3(1.16f, 4.58f, 1.3f), m.Concrete);
                Panel("Portal inset", entrance, new Vector3(jamb, 2.29f, -1.40f), new Vector3(.82f, 4.25f, .14f), m.Metal);
                Accent("Red jamb light", entrance, new Vector3(jamb, 2.54f, -1.50f), new Vector3(.135f, 2.65f, .09f), signal, red);
                for (int stripe = 0; stripe < 3; stripe++)
                {
                    var plate = Panel("Portal safety stripe", entrance, new Vector3(jamb, .38f + stripe * .2f, -1.51f),
                        new Vector3(.6f, .12f, .035f), m.Checkpoint);
                    plate.localRotation = Quaternion.Euler(0, 0, 24f);
                }
                for (int stripe = 0; stripe < 5; stripe++)
                {
                    var paint = Panel("Apron safety marking", entrance, new Vector3(side * 4.18f, .115f, -1.82f - stripe * .52f),
                        new Vector3(1.24f, .007f, .15f), m.Checkpoint);
                    paint.localRotation = Quaternion.Euler(0, side * 27f, 0);
                }

                var camera = Group(side < 0 ? "West security camera" : "East security camera", entrance,
                    new Vector3(side * 4.78f, 4.58f, -1.07f));
                camera.localRotation = Quaternion.Euler(-14f, -side * 18f, 0);
                Panel("Wall mount", camera, Vector3.back * .02f, new Vector3(.38f, .55f, .28f), m.Metal);
                Panel("Camera arm", camera, Vector3.back * .37f, new Vector3(.21f, .17f, .64f), m.Concrete);
                Panel("Camera housing", camera, new Vector3(0, -.12f, -.74f), new Vector3(.56f, .47f, .94f), m.Wall);
                Panel("Camera hood", camera, new Vector3(0, .12f, -.83f), new Vector3(.67f, .12f, 1.06f), m.Metal);
                Rod("Camera lens", camera, new Vector3(0, -.12f, -1.18f), new Vector3(0, -.12f, -1.27f), .30f, m.Glass);
                Accent("Camera indicator", camera, new Vector3(.19f, -.21f, -1.23f), Vector3.one * .07f, signal, red);

                Transform barrier = Group("Facade security barrier", entrance, new Vector3(side * 8.35f, 0, -1.28f));
                Panel("Armored barrier foot", barrier, new Vector3(0, .28f, 0), new Vector3(4.4f, .56f, 1.04f), m.Concrete);
                Panel("Armored barrier rail", barrier, new Vector3(0, .79f, .02f), new Vector3(3.96f, .71f, .78f), m.Metal);
                Panel("Barrier warning strip", barrier, new Vector3(0, .93f, -.39f), new Vector3(3.35f, .125f, .06f), m.Checkpoint);

                AddBeacon(entrance, new Vector3(side * 3.6f, 4.26f, -1.18f), m, signal, beacons);
                var light = Group("Portal accent light", entrance, new Vector3(side * 3.08f, 3.91f, -1.77f)).gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = WarningRed;
                light.range = 3.25f;
                light.intensity = 2.25f;
                light.shadows = LightShadows.None;
            }
        }

        private static void BuildCladding(Transform cladding, float width, float front, PrototypeMaterials m,
            Material signal, MaterialPropertyBlock red, Font font)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 9.25f;
                Panel("Front armor plate", cladding, new Vector3(x, 6.1f, front - .2f),
                    new Vector3(7.05f, 11.7f, .45f), m.Wall);
                Panel("Corner column", cladding, new Vector3(side * 12.55f, 6.04f, front - .53f),
                    new Vector3(1.12f, 12.15f, 1.04f), m.Metal);
                Panel("Corner reinforcement", cladding, new Vector3(side * 12.55f, 6.18f, front - 1.07f),
                    new Vector3(.51f, 10.65f, .24f), m.Concrete);
                Panel("Front panel joint", cladding, new Vector3(x, 9.64f, front - .45f), new Vector3(6.3f, .17f, .11f), m.Metal);
                Accent("Front red marker", cladding, new Vector3(side * 10.16f, 10.09f, front - .5f),
                    new Vector3(2.3f, .15f, .10f), signal, red);
                Transform sideWall = Group(side < 0 ? "West wall" : "East wall", cladding, Vector3.right * (side * width * .5f));
                sideWall.localRotation = Quaternion.Euler(0, -side * 90f, 0);
                for (int bay = -1; bay <= 1; bay++)
                {
                    float bayX = bay * 8.46f;
                    Panel("Side armor plate", sideWall, new Vector3(bayX, 7.9f, -.17f), new Vector3(8.16f, 7.87f, .43f), m.Wall);
                    Panel("Side panel reveal", sideWall, new Vector3(bayX, 6.47f, -.41f), new Vector3(8.13f, .19f, .13f), m.Metal);
                    Panel("Support brace", sideWall, new Vector3(bayX - 4.18f, 5.99f, -.45f), new Vector3(.53f, 11.98f, .83f), m.Metal);
                }
                for (int cable = 0; cable < 2; cable++)
                {
                    float y = 2.16f + .37f * cable;
                    Rod("Armored cable conduit", sideWall, new Vector3(-11.1f, y, -.59f),
                        new Vector3(10.7f, y, -.59f), .19f, m.Concrete);
                    Rod("Conduit riser", sideWall, new Vector3(10.7f, y, -.59f),
                        new Vector3(10.7f, y + 1.02f, -.59f), .19f, m.Concrete);
                }
                Panel("Electrical service cabinet", sideWall, new Vector3(-8.4f, 1.02f, -.56f),
                    new Vector3(2.2f, 2.04f, .85f), m.Metal);
                Panel("Cabinet cover", sideWall, new Vector3(-8.4f, 1.03f, -1.02f), new Vector3(1.84f, 1.72f, .11f), m.Concrete);
                Accent("Cabinet indicator", sideWall, new Vector3(-8.87f, 1.68f, -1.09f),
                    new Vector3(.39f, .12f, .05f), signal, red);
                Panel("Exhaust vent housing", sideWall, new Vector3(8.4f, 4.95f, -.57f), new Vector3(4.36f, 2.16f, .68f), m.Metal);
                for (int vane = 0; vane < 5; vane++)
                {
                    var slat = Panel("Exhaust louver", sideWall, new Vector3(8.4f, 4.14f + vane * .36f, -.94f),
                        new Vector3(3.97f, .14f, .17f), m.Concrete);
                    slat.localRotation = Quaternion.Euler(27f, 0, 0);
                }
                if (side < 0)
                {
                    Panel("Avenue sign housing", sideWall, new Vector3(0, 8f, -.66f), new Vector3(14.9f, 2.52f, .67f), m.Metal);
                    Accent("Avenue sign lighting", sideWall, new Vector3(0, 8.96f, -1.03f), new Vector3(13.8f, .12f, .09f), signal, red);
                    Label("CONTROL CENTER 03", sideWall, new Vector3(0, 8f, -1.02f), .92f, font, 13.9f, 1.07f, m.Lettering);
                }
            }
        }

        private static void BuildTower(Transform tower, PrototypeMaterials m, Material signal, MaterialPropertyBlock red, List<Renderer> beacons)
        {
            Panel("Tower mounting deck", tower, new Vector3(0, .36f, 0), new Vector3(10.9f, .74f, 8.25f), m.Metal);
            Panel("Tower core", tower, new Vector3(0, 3.81f, 0), new Vector3(6.4f, 7.1f, 5.2f), m.Concrete);
            for (int side = -1; side <= 1; side += 2)
            {
                Beam("Tower buttress", tower, new Vector3(side * 4.55f, .58f, -2.35f),
                    new Vector3(side * 3.05f, 7.45f, -2.35f), .61f, m.Metal);
                Beam("Rear buttress", tower, new Vector3(side * 4.55f, .58f, 2.35f),
                    new Vector3(side * 3.05f, 7.45f, 2.35f), .61f, m.Metal);
                Panel("Service spine", tower, new Vector3(side * 2.17f, 3.94f, -2.69f), new Vector3(1.29f, 6.25f, .31f), m.Wall);
                Accent("Tower running light", tower, new Vector3(side * 2.17f, 4.88f, -2.88f),
                    new Vector3(.13f, 3.39f, .09f), signal, red);
            }

            Transform observation = Group("Observation deck", tower, Vector3.up * 7.65f);
            Panel("Deck underframe", observation, Vector3.zero, new Vector3(11.28f, .81f, 8.2f), m.Metal);
            Panel("Cabin core", observation, new Vector3(0, 1.72f, 0), new Vector3(9.95f, 3.1f, 7.24f), m.Metal);
            Panel("Cabin roof", observation, new Vector3(0, 3.42f, 0), new Vector3(11.66f, .58f, 8.54f), m.Metal);

            for (int face = 0; face < 4; face++)
            {
                Transform window = Group("Glazed observation wall", observation);
                window.localRotation = Quaternion.Euler(0, 90f * face, 0);
                float halfWidth = face % 2 == 0 ? 5.36f : 3.87f;
                float depth = face % 2 == 0 ? 3.83f : 5.23f;
                Panel("Window surround", window, new Vector3(0, 1.8f, -depth),
                    new Vector3(halfWidth * 2, 2.97f, .38f), m.Metal);
                float span = halfWidth * 2 / 4f;
                for (int pane = 0; pane < 4; pane++)
                {
                    float x = (pane - 1.5f) * span;
                    Panel("Armored glass pane", window, new Vector3(x, 1.99f, -depth - .22f),
                        new Vector3(span - .22f, 2.11f, .10f), m.Glass);
                    Panel("Glazing bottom rail", window, new Vector3(x, 1.03f, -depth - .29f),
                        new Vector3(span - .31f, .095f, .09f), m.Server);
                }
                Accent("Deck position strip", window, new Vector3(0, .63f, -depth - .27f),
                    new Vector3(halfWidth * 1.78f, .115f, .11f), signal, red);
            }

            Transform comms = Group("Communications", tower, Vector3.up * 11.36f);
            Panel("Communications equipment box", comms, new Vector3(-3.8f, .53f, 2.1f), new Vector3(2.26f, 1.06f, 1.75f), m.Concrete);
            BuildDish(Group("Satellite dish", comms, new Vector3(-.76f, 0, -.58f)), m);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 4.26f;
                Panel("Antenna foot", comms, new Vector3(x, .14f, -2.12f), new Vector3(.64f, .28f, .62f), m.Metal);
                Rod("Whip aerial", comms, new Vector3(x, .24f, -2.12f), new Vector3(x, 4.36f, -2.12f), .16f, m.Wall);
                Panel("Sector antenna", comms, new Vector3(x + .14f, 2.61f, -2.28f), new Vector3(.35f, 1.69f, .34f), m.Concrete);
                AddBeacon(comms, new Vector3(side * 4.91f, .22f, -3.22f), m, signal, beacons);
            }
        }

        private static void BuildDish(Transform mount, PrototypeMaterials m)
        {
            Rod("Azimuth pedestal", mount, Vector3.zero, new Vector3(0, 2.72f, 0), .78f, m.Metal);
            Transform dish = Group("Dish reflector", mount, new Vector3(0, 3.05f, -.32f));
            dish.localRotation = Quaternion.Euler(22f, 19f, 0);
            Rod("Outer reflector", dish, new Vector3(0, 0, .12f), Vector3.back * .13f, 4.62f, m.Concrete);
            Rod("Reflector insert", dish, Vector3.back * .14f, Vector3.back * .22f, 4.18f, m.Metal);
            for (int spoke = 0; spoke < 8; spoke++)
            {
                Vector3 rim = Quaternion.Euler(0, 0, spoke * 45f) * Vector3.up * 2.19f + Vector3.back * .39f;
                Rod("Reflector rib", dish, Vector3.back * .25f, rim, .075f, m.Wall);
            }
            Rod("Receiver arm", dish, Vector3.back * .22f, Vector3.back * 1.58f, .14f, m.Wall);
            Rod("Feed horn", dish, Vector3.back * 1.52f, Vector3.back * 1.77f, .34f, m.Concrete);
        }

        private static void AddBeacon(Transform parent, Vector3 position, PrototypeMaterials m, Material signal, List<Renderer> beacons)
        {
            Transform beacon = Group("Emergency beacon", parent, position);
            Rod("Beacon socket", beacon, Vector3.down * .24f, Vector3.up * .04f, .61f, m.Metal);
            Rod("Dark beacon housing", beacon, Vector3.zero, Vector3.up * .44f, .41f, m.Metal);
            Renderer lens = Rod("Red beacon lens", beacon, Vector3.up * .05f, Vector3.up * .39f, .47f, signal).GetComponent<Renderer>();
            lens.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beacons.Add(lens);
        }

        private static void Accent(string name, Transform parent, Vector3 center, Vector3 dimensions, Material m, MaterialPropertyBlock tint)
        {
            var renderer = Panel(name, parent, center, dimensions, m).GetComponent<Renderer>();
            renderer.SetPropertyBlock(tint);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Transform Panel(string name, Transform parent, Vector3 center, Vector3 dimensions, Material material)
            => Box(name, parent, center, dimensions, material, false).transform;

        private static void Beam(string name, Transform parent, Vector3 start, Vector3 end, float width, Material material)
        {
            Vector3 direction = end - start;
            Panel(name, parent, (start + end) * .5f, new Vector3(width, direction.magnitude, width), material)
                .localRotation = Quaternion.FromToRotation(Vector3.up, direction);
        }

        private static Transform Rod(string name, Transform parent, Vector3 start, Vector3 end, float diameter, Material material)
        {
            Vector3 direction = end - start;
            var piece = Cylinder(name, parent, (start + end) * .5f, diameter, direction.magnitude, material, false).transform;
            piece.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
            return piece;
        }
    }
}
