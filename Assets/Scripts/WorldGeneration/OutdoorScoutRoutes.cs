using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Random = System.Random;

namespace ShiftFour
{
    // Places patrol loops against the generated roads, terrain and scout flight envelope.
    internal sealed class OutdoorScoutRoutes
    {
        internal static readonly string[] Names =
            { "Main Avenue", "Control Road", "Woodland", "North Boundary", "Checkpoint Approach",
                "Server Road", "East Woodland" };

        private readonly Transform world;
        private readonly float boundary;
        private readonly Vector3 envelope, extents;
        private readonly NavMeshQueryFilter filter;
        private readonly Random random;
        private readonly NavMeshPath path = new NavMeshPath();
        private readonly List<Vector3> launches = new List<Vector3>();

        internal OutdoorScoutRoutes(Transform world, float boundary, int seed, Vector3 envelope, Vector3 extents,
            NavMeshQueryFilter filter)
        {
            this.world = world;
            this.boundary = boundary;
            this.envelope = envelope;
            this.extents = extents;
            this.filter = filter;
            random = new Random(seed ^ 0x2ad2b6);
            foreach (DroneBody body in world.GetComponentsInChildren<DroneBody>())
                launches.Add(body.transform.position);
        }

        internal Vector3[] Find(int index)
        {
            Rect zone = PreferredLane(index);
            float jitter = (float)random.NextDouble() * 2f - 1f;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                Rect lane = MoveLane(zone, index, attempt, jitter);
                Vector3[] result = TryLoop(lane, index == 0);
                if (result != null) return result;
            }
            return index == 6 ? FindEastPerimeterLoop(zone.center.y) : null;
        }

        private Vector3[] FindEastPerimeterLoop(float grove)
        {
            // Use shorter circuits in the same east-side region when a densely wooded
            // seed cannot fit a full circuit around its closest grove.
            for (int band = 0; band < 4; band++)
            for (int row = 0; row < 15; row++)
            {
                int step = (row + 1) / 2 * (row % 2 == 0 ? 1 : -1);
                float depth = grove + step * 8f;
                if (Mathf.Abs(depth) > boundary - 12f) continue;
                float east = boundary - 5f - band * 5.3f;
                var lane = Rect.MinMaxRect(east - 5f, depth - 5.5f, east, depth + 5.5f);
                Vector3[] result = TryLoop(lane, false);
                if (result != null) return result;
            }
            return null;
        }

        private Rect PreferredLane(int index)
        {
            Transform roads = world.Find("Roads");
            if (index == 0)
            {
                Transform avenue = roads.Find("Main avenue");
                float start = Mathf.Max(-boundary + 26f, RoadZ("Checkpoint branch") + 4f);
                float end = Mathf.Max(start + 16f, RoadZ("Control branch") + 8f);
                end = Mathf.Min(avenue.localScale.z * .5f - 6f, end);
                return Rect.MinMaxRect(-2.1f, start, 2.1f, end);
            }
            if (index == 1)
            {
                Transform branch = roads.Find("Control branch");
                float end = branch.localPosition.x + branch.localScale.x * .5f - 7.5f;
                float z = branch.localPosition.z;
                return Rect.MinMaxRect(9f, z - 1.65f, end, z + 1.65f);
            }
            if (index == 2)
            {
                float west = -boundary + 5.8f;
                float grove = FindWoodlandCenter(west);
                return Rect.MinMaxRect(west, grove - 12f, west + 6.3f, grove + 12f);
            }
            if (index == 3)
                return Rect.MinMaxRect(7f, boundary - 10f, boundary * .7f, boundary - 4.9f);
            if (index == 5)
            {
                Transform branch = roads.Find("Server branch");
                float west = branch.localPosition.x - branch.localScale.x * .5f + 7.5f;
                float z = branch.localPosition.z;
                return Rect.MinMaxRect(west, z - 1.65f, -9f, z + 1.65f);
            }
            if (index == 6)
            {
                float east = boundary - 5.8f;
                float grove = FindWoodlandCenter(east);
                return Rect.MinMaxRect(east - 6.3f, grove - 12f, east, grove + 12f);
            }

            Vector3 checkpoint = world.Find("Buildings/Checkpoint").localPosition;
            float front = checkpoint.z - ResearchBuildingBuilder.FrontOffset(0);
            return Rect.MinMaxRect(checkpoint.x + 5.1f, front - 7.2f, checkpoint.x + 11.3f, front - 3f);
        }

        private float RoadZ(string name) => world.Find("Roads/" + name).localPosition.z;

        private float FindWoodlandCenter(float edge)
        {
            // Select an actual stand near the perimeter when individual trees are present.
            float preferredDepth = -boundary * .28f;
            float grove = preferredDepth;
            float nearest = float.PositiveInfinity;
            foreach (Transform tree in world.Find("Environment/Trees"))
            {
                Vector3 local = world.InverseTransformPoint(tree.position);
                float score = Mathf.Abs(local.x - edge) + Mathf.Abs(local.z - preferredDepth) * .18f;
                if (score >= nearest) continue;
                nearest = score;
                grove = Mathf.Clamp(local.z, -boundary + 26f, boundary - 25f);
            }
            return grove;
        }

        private Rect MoveLane(Rect lane, int role, int attempt, float jitter)
        {
            // The default road and entrance lanes already have reserved clearances.
            // Perimeter searches also try adjacent bands when trees or buildings occupy the initial loop.
            int step = (attempt + 1) / 2 * (attempt % 2 == 0 ? 1 : -1);
            Vector2 shift = role is 2 or 6 ? new Vector2((role == 2 ? 1 : -1) * (attempt % 3) * 1.7f, step * 2.5f) :
                role == 3 ? new Vector2(step * 2.6f, -attempt % 3 * 1.85f) :
                new Vector2(attempt % 3 == 2 ? .85f : 0f, step * .8f);
            Vector2 center = lane.center + shift + new Vector2(0f, jitter * .52f);
            Vector2 size = lane.size;
            if (attempt > 11) size *= .78f;
            float edge = boundary - 3.2f;
            center.x = Mathf.Clamp(center.x, -edge + size.x * .5f, edge - size.x * .5f);
            center.y = Mathf.Clamp(center.y, -edge + size.y * .5f, edge - size.y * .5f);
            return new Rect(center - size * .5f, size);
        }

        private Vector3[] TryLoop(Rect lane, bool longRoute)
        {
            Vector2[] points = longRoute
                ? new[] { new Vector2(lane.xMin, lane.yMin), new Vector2(lane.xMin, lane.center.y),
                    new Vector2(lane.xMin, lane.yMax), new Vector2(lane.xMax, lane.yMax),
                    new Vector2(lane.xMax, lane.center.y), new Vector2(lane.xMax, lane.yMin) }
                : new[] { new Vector2(lane.xMin, lane.yMin), new Vector2(lane.xMax, lane.yMin),
                    new Vector2(lane.xMax, lane.yMax), new Vector2(lane.xMin, lane.yMax) };

            var posts = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 target = world.TransformPoint(new Vector3(points[i].x, 0f, points[i].y));
                if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 2.2f, filter) ||
                    Mathf.Abs(world.InverseTransformPoint(hit.position).y) > .3f ||
                    Physics.CheckBox(hit.position + envelope, extents, Quaternion.identity,
                        WeaponController.GameplayRaycastMask, QueryTriggerInteraction.Ignore)) return null;
                posts[i] = hit.position;
            }

            for (int i = 0; i < posts.Length; i++)
            {
                Vector3 from = posts[i];
                Vector3 to = posts[(i + 1) % posts.Length];
                if ((to - from).sqrMagnitude < 2.5f * 2.5f || !NavMesh.CalculatePath(from, to, filter, path) ||
                    path.status != NavMeshPathStatus.PathComplete) return null;
                foreach (Vector3 corner in path.corners)
                {
                    Vector3 local = world.InverseTransformPoint(corner);
                    if (Mathf.Abs(local.x) >= boundary - 1.5f || Mathf.Abs(local.z) >= boundary - 1.5f)
                        return null;
                }
            }

            // Rotate the loop to a free first post without changing its patrol region.
            int first = System.Array.FindIndex(posts, point =>
                !launches.Exists(other => OutdoorScoutEncounter.FlatDistanceSquared(other, point) < 4f * 4f));
            if (first < 0) return null;
            var patrol = new Vector3[posts.Length];
            for (int i = 0; i < posts.Length; i++) patrol[i] = posts[(i + first) % posts.Length];
            launches.Add(patrol[0]);
            return patrol;
        }
    }
}
