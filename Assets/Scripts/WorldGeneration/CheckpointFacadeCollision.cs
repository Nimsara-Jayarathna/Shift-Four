using UnityEngine;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    internal static class CheckpointFacadeCollision
    {
        internal static void Build(Transform exterior, float halfWidth, float front)
        {
            // These dimensions follow the individual solid pieces in SF_CheckpointExterior.
            // The imported renderer bounds also include disconnected pieces and open space.
            Transform shell = Group("Facade collision", exterior);
            Side(shell, "East armor", Vector3.right * halfWidth, 90f, true);
            Side(shell, "West armor", Vector3.left * halfWidth, -90f, true);
            Side(shell, "Rear armor", Vector3.back * front, 0f, false);

            Transform entrance = Group("Front armor and forecourt", shell, Vector3.forward * front);
            foreach (int side in new[] { -1, 1 })
            {
                Transform wing = Group(side < 0 ? "West" : "East", entrance);
                Solid(wing, "Entrance jamb", new Vector3(side * 2.73f, 2.21f, -.68f), new Vector3(1.02f, 4.38f, .98f));
                Solid(wing, "Jamb faceplate", new Vector3(side * 2.78f, 2.17f, -1.19f), new Vector3(.64f, 4.13f, .18f));
                Solid(wing, "Corner pier", new Vector3(side * 11.49f, 5.1f, -.37f), new Vector3(1.18f, 10.1f, .73f));
                Solid(wing, "Pier facing", new Vector3(side * 11.49f, 5.2f, -.78f), new Vector3(.65f, 8.74f, .23f));
                Solid(wing, "Lower front armor", new Vector3(side * 7f, 1.13f, -.32f), new Vector3(6.08f, 1.52f, .48f));
                foreach (float x in new[] { 4.25f, 9.2f })
                {
                    Solid(wing, "Upper front armor", new Vector3(side * x, 7.76f, -.34f), new Vector3(4.49f, 3.54f, .44f));
                    Solid(wing, "Upper front facing", new Vector3(side * x, 7.97f, -.61f), new Vector3(4.02f, 2.48f, .18f));
                }
                Solid(wing, "Forecourt barrier base", new Vector3(side * 9.15f, .43f, -.97f), new Vector3(4.48f, .70f, .62f));
                Solid(wing, "Forecourt barrier facing", new Vector3(side * 9.15f, .65f, -1.23f), new Vector3(4.18f, .49f, .35f));

                Transform rails = Group("Forecourt guard rails", wing, new Vector3(side * 9.15f, 0f, -.98f));
                Rail(rails, "Lower rail", 1.12f);
                Rail(rails, "Middle rail", 1.65f);
                Rail(rails, "Upper rail", 1.88f);
            }

            Solid(entrance, "Entrance lintel", new Vector3(0f, 4.58f, -.79f), new Vector3(6.42f, 1.07f, 1.02f));
        }

        private static void Side(Transform shell, string name, Vector3 position, float yaw, bool hasCabinet)
        {
            Transform face = Group(name, shell, position);
            face.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Solid(face, "Armor backing", new Vector3(0f, 5f, .07f), new Vector3(23.98f, 9.65f, .13f));
            for (int bay = 0; bay < 4; bay++)
            {
                float x = -9f + bay * 6f;
                Solid(face, "Lower armor panel", new Vector3(x, 1.15f, .20f), new Vector3(5.83f, 1.83f, .26f));
                Solid(face, "Middle armor panel", new Vector3(x, 3.43f, .26f), new Vector3(5.76f, 2.46f, .33f));
                Solid(face, "Glazing panel", new Vector3(x, 5.49f, .21f), new Vector3(5.72f, 1.14f, .22f));
                Solid(face, "Upper armor panel", new Vector3(x, 8.04f, .32f), new Vector3(5.69f, 3.12f, .4f));
                Solid(face, "Upper panel facing", new Vector3(x, 8.32f, .56f), new Vector3(5.18f, 2.08f, .15f));
            }

            foreach (float x in new[] { -11.65f, -6f, 0f, 6f, 11.65f })
            {
                Solid(face, "Structural rib", new Vector3(x, 5.2f, .59f), new Vector3(.59f, 9.96f, .59f));
                Solid(face, "Rib facing", new Vector3(x, 4.34f, .91f), new Vector3(.31f, 6.90f, .22f));
            }

            if (!hasCabinet) return;
            Solid(face, "Utility cabinet", new Vector3(5f, 2.15f, .59f), new Vector3(2.18f, 1.95f, .65f));
            Solid(face, "Cabinet facing", new Vector3(5f, 2.17f, .95f), new Vector3(1.92f, 1.69f, .12f));
            Transform manifold = Group("Utility manifold pipes", face);
            CoolantPipe(Group("Lower coolant pipe", manifold), 1.03f, .14f);
            CoolantPipe(Group("Upper coolant pipe", manifold), 1.46f, .105f);
        }

        private static void CoolantPipe(Transform parent, float height, float radius)
        {
            const float depth = .66f;
            var end = new Vector3(3.9f, height, depth);
            var elbow = new Vector3(4.2f, height + .30f, depth);
            RoundBar(parent, "Straight run", new Vector3(-10.9f, height, depth), end, radius);
            RoundBar(parent, "Elbow", end, elbow, radius);
            RoundBar(parent, "Riser", elbow, new Vector3(4.2f, height + 1.38f, depth), radius);
        }

        private static void Rail(Transform parent, string name, float height)
            => RoundBar(parent, name, new Vector3(-1.84f, height, 0f), new Vector3(1.84f, height, 0f), .055f);

        private static void RoundBar(Transform parent, string name, Vector3 start, Vector3 end, float radius)
        {
            Vector3 axis = end - start;
            Transform part = Group(name, parent, (start + end) * .5f);
            part.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
            var solid = part.gameObject.AddComponent<CapsuleCollider>();
            solid.radius = radius;
            // Include the rounded caps within the measured mesh length.
            solid.height = axis.magnitude;
        }

        private static void Solid(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var part = Group(name, parent, center).gameObject.AddComponent<BoxCollider>();
            part.size = size;
        }
    }
}
