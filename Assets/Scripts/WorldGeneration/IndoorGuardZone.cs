using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    // Runtime configuration shared by the original Guard brains assigned to one building.
    internal sealed class IndoorGuardZone : MonoBehaviour
    {
        private readonly struct Site
        {
            internal readonly string Building;
            internal readonly float Detection, Attack, Cooldown;
            internal readonly Rect[] Routes;

            internal Site(string building, float detection, float attack, float cooldown, params Rect[] routes)
            {
                Building = building;
                Detection = detection;
                Attack = attack;
                Cooldown = cooldown;
                Routes = routes;
            }
        }

        // These loops sit beside mission fixtures, leaving the interaction faces and entrance aisles clear.
        private static readonly Site[] Sites =
        {
            new Site("Checkpoint", 13f, 9f, 2.4f,
                Rect.MinMaxRect(-7.6f, .3f, -3.2f, 3.1f)),
            new Site("Server", 17f, 11f, 2.1f,
                Rect.MinMaxRect(-7.5f, 5f, -2.5f, 8.2f),
                Rect.MinMaxRect(4f, -9.6f, 9f, -7.2f)),
            new Site("Control", 20f, 12f, 1.95f,
                Rect.MinMaxRect(-3.4f, 6.65f, 3.4f, 8.45f),
                Rect.MinMaxRect(-10.5f, -9.5f, -8.4f, -2.8f),
                Rect.MinMaxRect(8.6f, -1.4f, 10.7f, 5.5f)),
            new Site("Storage", 23f, 13f, 1.75f,
                Rect.MinMaxRect(10.6f, 6.6f, 12.6f, 9.5f),
                Rect.MinMaxRect(4.7f, -1f, 8.8f, .85f),
                Rect.MinMaxRect(-9.7f, -9.6f, -5.3f, -7.1f),
                Rect.MinMaxRect(-10f, 7f, -4.7f, 9.7f))
        };

        private static readonly Vector2[] PatrolAdjustments =
        {
            Vector2.zero, new Vector2(0, -.55f), new Vector2(0, .55f),
            new Vector2(-.45f, 0), new Vector2(.45f, 0)
        };
        private static readonly float[] PatrolScales = { 1f, .75f, .58f };
        private static readonly string[][] Posts =
        {
            new[] { "Security Scanner and Desk" },
            new[] { "Server Racks", "Hacking Terminals" },
            new[] { "Lockdown Override", "West Security Switch", "East Control Room" },
            new[] { "Emergency Generator", "Cargo Shelves", "Power Cell Storage", "Rear Storage" }
        };

        private Transform building;
        private Site site;
        private int index;
        private float detectionScale;
        private Bounds interior;
        private NavMeshData navigation;
        private NavMeshDataInstance registration;
        private NavMeshQueryFilter filter;
        private Vector3[][] patrols;
        private Vector3[] securityPosts;
        private Vector3[] approachGoals;
        private float[] approachUntil;
        internal WaypointGraph Graph { get; private set; }

        private Vector3 lastSighting;
        private float alertUntil = float.NegativeInfinity;
        private OutdoorScoutEncounter scoutPatrols;
        private OutdoorScout alertSource;
        private Vector3 scoutAlertPost;
        private float scoutAlertUntil = float.NegativeInfinity;
        private float nextZoneShot;
        private int lastFireWindow = -1;

        internal float Detection => site.Detection * detectionScale;
        internal float Attack => site.Attack;
        internal float Cooldown => site.Cooldown;
        internal float FieldOfView { get; private set; }
        internal Vector3[] Patrol(int slot) => patrols[slot];
        internal Vector3 SecurityPost(int slot) => securityPosts[slot];

        internal static GuardBrain Deploy(Transform layout, GameObject prefab, PlayerController player,
            float detectionScale, float fieldOfView, int seed)
        {
            var agent = prefab != null ? prefab.GetComponent<NavMeshAgent>() : null;
            var body = prefab != null ? prefab.GetComponent<DroneBody>() : null;
            if (agent == null || body == null)
            {
                Debug.LogWarning($"Open World: assign the original Guard prefab with NavMeshAgent and DroneBody. No indoor Guards deployed on seed {seed}.", layout);
                return null;
            }

            GuardBrain extractionGuard = null;
            Site[] sites = Sites;
            for (int i = 0; i < sites.Length; i++)
            {
                Site site = sites[i];
                Transform area = layout.Find("Buildings/" + site.Building);
                if (area == null || area.Find("Interior") == null)
                {
                    Debug.LogWarning($"Open World: cannot deploy indoor Guards without the {site.Building} interior on seed {seed}.", layout);
                    continue;
                }
                var zone = Group("IndoorGuards", area).gameObject.AddComponent<IndoorGuardZone>();
                zone.FieldOfView = fieldOfView;
                try
                {
                    zone.Initialize(area, site, i, detectionScale, agent, body);
                }
                catch (InvalidOperationException error)
                {
                    Debug.LogWarning($"Open World: {site.Building} indoor Guard setup failed on seed {seed}. {error.Message}", zone);
                    zone.gameObject.SetActive(false);
                    Destroy(zone.gameObject);
                    continue;
                }

                int spawned = 0;
                for (int slot = 0; slot < site.Routes.Length; slot++)
                {
                    bool tracksExtraction = i == 1 && slot == 0;
                    string role = tracksExtraction ? "Guard" : $"Guard {i + 1:00}-{slot + 1:00}";
                    GuardBrain guard = OutdoorDroneEncounter.Spawn<GuardBrain>(layout, prefab, player, site.Building, i,
                        role, seed, zone.transform, Posts[i][slot], zone, slot);
                    if (guard == null) continue;
                    spawned++;
                    if (tracksExtraction) extractionGuard = guard;
                }

                if (spawned == site.Routes.Length)
                    Debug.Log($"Open World: deployed {spawned} indoor Guards in {site.Building} on seed {seed}.", zone);
                else
                    Debug.LogWarning($"Open World: {site.Building} deployed {spawned}/{site.Routes.Length} indoor Guards on seed {seed}.", zone);
            }
            return extractionGuard;
        }

        private void Initialize(Transform area, Site config, int siteIndex, float scale, NavMeshAgent agent, DroneBody body)
        {
            building = area;
            site = config;
            index = siteIndex;
            detectionScale = scale;
            interior = ResearchBuildingBuilder.InteriorBounds(index);
            approachGoals = new Vector3[site.Routes.Length];
            approachUntil = new float[site.Routes.Length];
            filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };

            BakeNavigation(agent, body);
            patrols = new Vector3[site.Routes.Length][];
            securityPosts = new Vector3[patrols.Length];
            for (int slot = 0; slot < patrols.Length; slot++)
            {
                patrols[slot] = FindPatrol(slot, body);
                if (patrols[slot] == null)
                    throw new InvalidOperationException($"No clear indoor loop near {Posts[index][slot]}.");
                Vector3 center = Vector3.zero;
                foreach (Vector3 point in patrols[slot]) center += point;
                center /= patrols[slot].Length;
                securityPosts[slot] = NavMesh.SamplePosition(center, out NavMeshHit hit, .4f, filter) &&
                    AllowsFlight(hit.position) && CanConnect(patrols[slot][0], hit.position)
                    ? hit.position : patrols[slot][0];
                Group(Posts[index][slot] + " Security Post", transform).position = securityPosts[slot];
            }
            BuildGraph(body);
        }

        private void BakeNavigation(NavMeshAgent agent, DroneBody body)
        {
            var settings = NavMesh.GetSettingsByID(agent.agentTypeID);
            settings.agentRadius = body.HorizontalRadius * Mathf.Sqrt(2f) + .06f;
            settings.agentHeight = Mathf.Max(agent.height, body.EnvelopeCenter.y + body.EnvelopeExtents.y);
            settings.agentClimb = .05f;
            settings.ledgeDropHeight = 0f;
            settings.maxJumpAcrossDistance = 0f;

            // An inset floor with no links through the doorway keeps this island separate from outdoor navigation.
            Vector3 floorSize = new Vector3(interior.size.x, .2f, interior.size.z);
            float floorY = ResearchBuildingBuilder.FloorHeight;
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(building.TransformPoint(Vector3.up * (floorY - floorSize.y * .5f)),
                        building.rotation, Vector3.one),
                    size = floorSize
                }
            };

            foreach (Collider solid in building.Find("Interior").GetComponentsInChildren<Collider>())
            {
                if (!solid.enabled || solid.isTrigger) continue;
                if (solid is BoxCollider box)
                {
                    Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
                    BlockFixture(sources, box.transform.TransformPoint(box.center), box.transform.rotation, size, settings.agentHeight);
                }
                else
                    BlockFixture(sources, solid.bounds.center, Quaternion.identity, solid.bounds.size, settings.agentHeight);
            }

            var bounds = new Bounds(Vector3.up * (floorY + 1f), new Vector3(floorSize.x + 2f, 4f, floorSize.z + 2f));
            navigation = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, building.position, building.rotation);
            if (navigation == null) throw new InvalidOperationException("Indoor NavMesh could not be baked.");
            registration = NavMesh.AddNavMeshData(navigation);
            if (!registration.valid) throw new InvalidOperationException("Indoor NavMesh could not be registered.");
        }

        private void BlockFixture(List<NavMeshBuildSource> sources, Vector3 center, Quaternion rotation, Vector3 size, float flightHeight)
        {
            // Hang overhead lights and the scanner bridge over the flight envelope without closing their aisles.
            Vector3 local = building.InverseTransformPoint(center);
            float floor = ResearchBuildingBuilder.FloorHeight;
            if (local.y - size.y * .5f > floor + flightHeight) return;
            local.y = floor + flightHeight * .5f;
            size.y = flightHeight + .4f;
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.ModifierBox,
                transform = Matrix4x4.TRS(building.TransformPoint(local), rotation, Vector3.one),
                size = size,
                area = 1 // Not Walkable
            });
        }

        private void BuildGraph(DroneBody body)
        {
            Transform root = Group("Indoor Waypoints", transform);
            var nodes = new List<Transform>();
            foreach (Vector3[] patrol in patrols)
                foreach (Vector3 point in patrol)
                    AddWaypoint(root, nodes, point);

            // A few shared connections across the open floor let the patrols approach around fixtures.
            for (int z = -8; z <= 8; z += 4)
            for (int x = -8; x <= 8 && nodes.Count < 30; x += 4)
            {
                if (!TryPost(x, z, body, out Vector3 point) || nodes.Exists(node => (node.position - point).sqrMagnitude < 4f))
                    continue;
                AddWaypoint(root, nodes, point);
            }

            var edges = new List<GraphEdge>();
            for (int from = 0; from < nodes.Count; from++)
            for (int to = from + 1; to < nodes.Count; to++)
                if (CanConnect(nodes[from].position, nodes[to].position))
                    edges.Add(new GraphEdge(from, to, -1));

            Graph = root.gameObject.AddComponent<WaypointGraph>();
            Graph.Configure(nodes.ToArray(), edges.ToArray());
            Graph.SetConnectionValidator(CanConnect);
        }

        private static void AddWaypoint(Transform root, List<Transform> nodes, Vector3 point)
        {
            Transform post = Group($"Post {nodes.Count + 1}", root);
            post.position = point;
            nodes.Add(post);
        }

        private bool TryPost(float x, float z, DroneBody body, out Vector3 point)
        {
            var local = new Vector3(x, ResearchBuildingBuilder.FloorHeight, z);
            return OutdoorDroneEncounter.TryPost(building, local, body, filter, out point) && AllowsFlight(point);
        }

        private Vector3[] FindPatrol(int slot, DroneBody body)
        {
            Rect lane = site.Routes[slot];
            var posts = new Vector3[index == 0 ? 3 : 4];
            foreach (float scale in PatrolScales)
            foreach (Vector2 shift in PatrolAdjustments)
            {
                bool valid = true;
                for (int i = 0; i < posts.Length && valid; i++)
                {
                    var corner = new Vector2(i < 2 ? 1f : -1f, i == 0 || i == 3 ? -1f : 1f);
                    Vector2 position = lane.center + Vector2.Scale(corner, lane.size) * (.5f * scale) + shift;
                    valid = TryPost(position.x, position.y, body, out posts[i]);
                }
                for (int i = 0; i < posts.Length && valid; i++)
                {
                    Vector3 from = posts[i];
                    Vector3 to = posts[(i + 1) % posts.Length];
                    Vector3 delta = to - from;
                    valid = delta.sqrMagnitude >= 1.3f * 1.3f && CanConnect(from, to) &&
                        !Physics.BoxCast(from + body.EnvelopeCenter, body.EnvelopeExtents, delta.normalized,
                            Quaternion.identity, delta.magnitude, WeaponController.GameplayRaycastMask, QueryTriggerInteraction.Ignore);
                }
                if (valid) return posts;
            }
            return null;
        }

        internal bool CanTrack(Vector3 point)
        {
            Vector3 local = building.InverseTransformPoint(point);
            return local.y >= interior.min.y - .25f && local.y <= interior.max.y &&
                Mathf.Abs(local.x) <= interior.extents.x && Mathf.Abs(local.z) <= interior.extents.z;
        }

        private bool AllowsFlight(Vector3 point)
            => CanTrack(point) && Mathf.Abs(building.InverseTransformPoint(point).y - interior.min.y) <= .3f;

        private bool CanConnect(Vector3 start, Vector3 end)
            => AllowsFlight(start) && AllowsFlight(end) &&
                NavMesh.SamplePosition(start, out NavMeshHit from, .25f, filter) &&
                NavMesh.SamplePosition(end, out NavMeshHit to, .25f, filter) &&
                !NavMesh.Raycast(from.position, to.position, out _, filter);

        private void OnDisable()
        {
            if (scoutPatrols != null) scoutPatrols.AlertRaised -= OnScoutAlert;
            if (registration.valid) registration.Remove();
        }

        internal void ListenForScoutAlerts(OutdoorScoutEncounter patrols)
        {
            if (scoutPatrols != null) scoutPatrols.AlertRaised -= OnScoutAlert;
            scoutPatrols = patrols;
            scoutPatrols.AlertRaised += OnScoutAlert;
        }

        private void OnScoutAlert(OutdoorScout scout, Vector3 sighting)
        {
            if (Time.time < scoutAlertUntil) return;
            float front = -ResearchBuildingBuilder.FrontOffset(index);
            Vector3 entry = building.TransformPoint(new Vector3(0f, 0f, front));
            if (PlanarDistanceSquared(entry, sighting) > 16f * 16f ||
                PlanarDistanceSquared(entry, scout.transform.position) > 28f * 28f) return;

            // Relay only a room-local entrance post. The Guard must confirm sight for itself.
            Vector3 candidate = building.TransformPoint(new Vector3(3.8f, ResearchBuildingBuilder.FloorHeight, front + 4f));
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, filter) || !AllowsFlight(hit.position)) return;
            scoutAlertPost = hit.position;
            alertSource = scout;
            scoutAlertUntil = Time.time + 3f;
        }

        internal bool TryGetScoutAlert(out Vector3 post)
        {
            post = scoutAlertPost;
            return alertSource != null && alertSource.State != OutdoorScout.Activity.Destroyed && Time.time < scoutAlertUntil;
        }

        private void OnDestroy()
        {
            if (navigation != null) Destroy(navigation);
        }

        private static float PlanarDistanceSquared(Vector3 first, Vector3 second)
        {
            Vector3 delta = first - second;
            return delta.x * delta.x + delta.z * delta.z;
        }

        internal void ShareSighting(Vector3 point)
        {
            lastSighting = point;
            alertUntil = Time.time + 3.5f;
        }

        internal bool TryGetSighting(out Vector3 point)
        {
            point = lastSighting;
            return Time.time < alertUntil;
        }

        internal bool TryClaimApproach(int slot, Vector3 point)
        {
            for (int i = 0; i < approachGoals.Length; i++)
                if (i != slot && approachUntil[i] > Time.time &&
                    PlanarDistanceSquared(point, approachGoals[i]) < 3.5f * 3.5f)
                    return false;
            approachGoals[slot] = point;
            approachUntil[slot] = Time.time + 1.25f;
            return true;
        }

        internal void ReleaseApproach(int slot) => approachUntil[slot] = 0f;

        internal bool TryClaimShot(int slot)
        {
            if (Time.time < nextZoneShot) return false;
            if (index == 3)
            {
                // Four distinct windows let a player break line of sight between attacks.
                int window = Mathf.FloorToInt(Time.time / .65f);
                if (window == lastFireWindow || window % site.Routes.Length != slot) return false;
                lastFireWindow = window;
            }
            nextZoneShot = Time.time + .57f;
            return true;
        }
    }
}
