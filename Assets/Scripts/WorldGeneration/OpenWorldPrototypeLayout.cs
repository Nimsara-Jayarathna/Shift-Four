using UnityEngine;
using UnityEngine.Rendering.Universal;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    public static class OpenWorldPrototypeLayout
    {
        public const float WorldSize = 200f;
        public static readonly Vector3 SpawnPosition = new Vector3(0, .12f, -82);

        public static void Build(Transform world, PrototypeMaterials m, Font font)
        {
            Box("Ground", world, new Vector3(0, -.4f, 0), new Vector3(WorldSize, .8f, WorldSize), m.Ground);
            BuildRoads(Group("Roads", world), m);
            ResearchBuildingBuilder.Build(Group("Buildings", world), m, font);
            BuildEnvironment(Group("Environment", world), m, font);
            BuildFixedExtraction(world, m, font);
            BuildSpawn(Group("Spawn", world, SpawnPosition));
        }

        internal static void BuildFixedExtraction(Transform layout, PrototypeMaterials m, Font font)
        {
            const float south = -96f;
            Box("Extraction approach", layout.Find("Roads"), new Vector3(0f, .045f, -92.5f),
                new Vector3(10f, .09f, 15f), m.Road);
            Transform fences = layout.Find("Environment/Fences");
            float gap = OpenWorldExtractionGate.HalfWidth;
            Fence(fences, new Vector3(-10f, 0f, south), new Vector3(-gap, 0f, south), m);
            Fence(fences, new Vector3(gap, 0f, south), new Vector3(10f, 0f, south), m);
            OpenWorldExtractionGate.Build(layout, south, m, font);
        }

        private static void BuildRoads(Transform roads, PrototypeMaterials m)
        {
            Box("Main avenue - 12 m", roads, new Vector3(0, .04f, 0), new Vector3(12, .08f, 180), m.Road);
            foreach (float z in new[] { -40f, 23f })
                Box("Cross avenue - 10 m", roads, new Vector3(0, .04f, z), new Vector3(150, .08f, 10), m.Road);
            foreach (float x in new[] { -75f, 75f })
                Box("Perimeter service lane - 8 m", roads, new Vector3(x, .04f, -8.5f), new Vector3(8, .08f, 63), m.Road);
            foreach (float x in new[] { -7.5f, 7.5f })
                Box("Avenue footpath", roads, new Vector3(x, .055f, 0), new Vector3(2.5f, .11f, 180), m.Concrete);
            for (int z = -85; z <= 85; z += 10)
                if (Mathf.Abs(z + 40) > 8 && Mathf.Abs(z - 23) > 8)
                    Box("Avenue lane dash", roads, new Vector3(0, .087f, z), new Vector3(.18f, .014f, 3), m.Marking, false);
            foreach (float z in new[] { -40f, 23f })
                for (int x = -65; x <= 65; x += 10)
                    if (Mathf.Abs(x) > 10)
                        Box("Cross avenue lane dash", roads, new Vector3(x, .087f, z), new Vector3(3, .014f, .18f), m.Marking, false);
            // Flat pedestrian crossings leave the entire road traversable.
            foreach (float z in new[] { -49f, 14f })
                for (int stripe = -4; stripe <= 4; stripe++)
                    Box("Pedestrian crossing", roads, new Vector3(stripe, .089f, z), new Vector3(.55f, .018f, 2.8f), m.Marking, false);
        }

        private static void BuildEnvironment(Transform environment, PrototypeMaterials m, Font font)
        {
            Transform trees = Group("Trees", environment);
            Vector3[] treePositions = {
                new Vector3(-87, 0, -73), new Vector3(-82, 0, -64), new Vector3(83, 0, -70),
                new Vector3(89, 0, -60), new Vector3(-89, 0, 13), new Vector3(88, 0, 8),
                new Vector3(-83, 0, 72), new Vector3(-73, 0, 82), new Vector3(79, 0, 77),
                new Vector3(88, 0, 65), new Vector3(-24, 0, 81), new Vector3(27, 0, 84) };
            for (int i = 0; i < treePositions.Length; i++)
            {
                Transform tree = Group("Tree " + (i + 1), trees, treePositions[i]);
                float height = 4.5f + i % 3;
                Cylinder("Trunk", tree, Vector3.up * (height * .5f), .65f, height, m.Wood);
                Sphere("Canopy", tree, Vector3.up * (height + 1), new Vector3(5, 5.5f, 5), m.Foliage, false);
                Sphere("Crown", tree, new Vector3(1.2f, height + 2.5f, .4f), new Vector3(3.3f, 3.8f, 3.3f), m.Foliage, false);
            }
            Transform rocks = Group("Rocks", environment);
            Vector3[] rockPositions = { new Vector3(-83, 0, -78), new Vector3(80, 0, -77),
                new Vector3(-85, 0, 34), new Vector3(86, 0, 36), new Vector3(-70, 0, 77),
                new Vector3(74, 0, 83), new Vector3(-22, 0, 75), new Vector3(24, 0, 79) };
            for (int i = 0; i < rockPositions.Length; i++)
            {
                var rock = Sphere("Rock " + (i + 1), rocks, rockPositions[i] + Vector3.up * .8f,
                    new Vector3(2.5f + i % 3, 2, 2.8f), m.Rock);
                rock.transform.localRotation = Quaternion.Euler(i * 9, i * 37, i * 11);
            }
            Transform crates = Group("Crates", environment);
            foreach (Vector3 position in new[] { new Vector3(63, 0, -23), new Vector3(63, 0, -20),
                new Vector3(63, 0, -17), new Vector3(63, 1.8f, -20), new Vector3(66, 0, -20),
                new Vector3(-59, 0, -23), new Vector3(-59, 0, -20) })
            {
                Transform crate = Group("Cargo crate", crates, position);
                Box("Crate body", crate, Vector3.up * .9f, Vector3.one * 1.8f, m.Crate);
                for (int side = -1; side <= 1; side += 2)
                    Box("Retention band", crate, new Vector3(side * .55f, .9f, 0), new Vector3(.12f, 1.85f, 1.85f), m.Metal, false);
            }
            Transform fences = Group("Fences", environment);
            Fence(fences, new Vector3(-96, 0, -96), new Vector3(-96, 0, 96), m);
            Fence(fences, new Vector3(96, 0, -96), new Vector3(96, 0, 96), m);
            Fence(fences, new Vector3(-96, 0, 96), new Vector3(96, 0, 96), m);
            Fence(fences, new Vector3(-96, 0, -96), new Vector3(-10, 0, -96), m);
            Fence(fences, new Vector3(10, 0, -96), new Vector3(96, 0, -96), m);

            Transform lighting = Group("Lighting", environment);
            foreach (float z in new[] { -65f, -9f, 55f, 82f })
                foreach (float x in new[] { -9.5f, 9.5f })
                {
                    Transform post = Group("Avenue light", lighting, new Vector3(x, 0, z));
                    Cylinder("Pole", post, Vector3.up * 3.5f, .18f, 7, m.Metal);
                    Box("Lamp head", post, new Vector3(0, 7, 0), new Vector3(1.5f, .22f, .7f), m.Metal, false);
                    Box("Diffuser", post, new Vector3(0, 6.86f, 0), new Vector3(1.3f, .06f, .5f), m.Server, false);
                }
            Transform sign = Group("Facility entry sign", environment, new Vector3(-13, 0, -89));
            Box("Sign plinth", sign, new Vector3(0, 1.8f, 0), new Vector3(7, 3.6f, .5f), m.Metal);
            Box("Sign accent", sign, new Vector3(-3.2f, 1.8f, -.3f), new Vector3(.15f, 3, .15f), m.Server, false);
            Label("SHIFT-FOUR", sign, new Vector3(0, 2.3f, -.3f), .28f, font, 6, .9f, m.Lettering);
            Label("RESEARCH FACILITY", sign, new Vector3(0, 1.3f, -.3f), .16f, font, 6, .45f, m.Lettering);
        }

        internal static void Fence(Transform parent, Vector3 start, Vector3 end, PrototypeMaterials m)
        {
            float length = Vector3.Distance(start, end);
            Transform fence = Group("Perimeter fence", parent, (start + end) * .5f);
            fence.localRotation = Quaternion.FromToRotation(Vector3.right, (end - start).normalized);
            var collision = fence.gameObject.AddComponent<BoxCollider>();
            collision.center = Vector3.up * 1.25f;
            collision.size = new Vector3(length, 2.5f, .25f);
            foreach (float y in new[] { .35f, 1.3f, 2.3f })
                Box("Fence rail", fence, Vector3.up * y, new Vector3(length, .08f, .08f), m.Metal, false);
            int posts = Mathf.CeilToInt(length / 6);
            for (int i = 0; i <= posts; i++)
                Box("Fence post", fence, new Vector3(-length * .5f + i * length / posts, 1.3f, 0), new Vector3(.14f, 2.6f, .14f), m.Metal, false);
        }

        private static void BuildSpawn(Transform spawn)
        {
            Transform player = Group("Player", spawn);
            var body = player.gameObject.AddComponent<CharacterController>();
            body.height = 1.8f;
            body.radius = .35f;
            body.center = Vector3.up * .9f;
            body.skinWidth = .05f;
            body.minMoveDistance = 0;
            body.stepOffset = .3f;
            var camera = Group("First Person Camera", player, Vector3.up * 1.3f).gameObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 64;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 350;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(.54f, .65f, .72f);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camera.gameObject.AddComponent<AudioListener>();
            ConfigurePlayer(player.gameObject, camera);
        }

        public static void ConfigurePlayer(GameObject player, Camera camera)
        {
            player.AddComponent<Health>().Configure(100f, true);
            player.AddComponent<PlayerController>().Configure(camera);
            player.AddComponent<PrototypePlayerFeedback>();
        }
    }
}
