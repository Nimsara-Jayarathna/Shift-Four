using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using Unity.Profiling;

namespace ShiftFour
{
    [Serializable]
    public struct GraphEdge
    {
        public int from;
        public int to;
        public bool doorShortcut; // Legacy serialization only.
        public int gateId; // -1: unrestricted; 0..3: independent security gates.
        public GraphEdge(int a, int b, int gate)
        {
            if (gate < -1 || gate > 3) throw new ArgumentOutOfRangeException(nameof(gate));
            from = a; to = b; gateId = gate; doorShortcut = false;
        }
        public GraphEdge(int a, int b, SecurityGate gate) : this(a, b, (int)gate) { }
        public GraphEdge(int a, int b, bool door) : this(a, b, door ? 0 : -1) { }
    }

    public sealed class WaypointGraph : MonoBehaviour
    {
        private static readonly ProfilerMarker RouteMarker = new ProfilerMarker("ShiftFour.FindRoute");
        private static readonly ProfilerMarker ConnectMarker = new ProfilerMarker("ShiftFour.CanConnect");
        [SerializeField] private Transform[] nodes;
        [SerializeField] private GraphEdge[] edges;
        private readonly bool[] gatesOpen = new bool[4];
        private Func<Vector3, Vector3, bool> connectionValidator;
        private NavMeshPath connectorPath;
        [SerializeField] private int contractVersion;
        public int ContractVersion => contractVersion;
        public bool IsKnownLegacyLayout
        {
            get
            {
                if (contractVersion != 0 || NodeCount != 4 || edges == null || edges.Length != 4) return false;
                return Array.Exists(edges, e => e.from == 0 && e.to == 1 && e.doorShortcut) &&
                    Array.Exists(edges, e => e.from == 0 && e.to == 2 && !e.doorShortcut) &&
                    Array.Exists(edges, e => e.from == 1 && e.to == 3 && !e.doorShortcut) &&
                    Array.Exists(edges, e => e.from == 2 && e.to == 3 && !e.doorShortcut);
            }
        }
        public int Version { get; private set; }

        public void Configure(Transform[] waypoints, GraphEdge[] connections)
        {
            contractVersion = 1;
            nodes = waypoints;
            edges = connections;
            Array.Clear(gatesOpen, 0, gatesOpen.Length);
            Version++;
        }

        internal void SetConnectionValidator(Func<Vector3, Vector3, bool> validator)
        {
            if (connectionValidator == validator) return;
            connectionValidator = validator;
            Version++;
        }

        public void SetDoorOpen(bool open) => SetGateOpen(0, open);

        public void SetGateOpen(int gateId, bool open)
        {
            if (!MissionProgress.Valid(gateId)) throw new ArgumentOutOfRangeException(nameof(gateId));
            if (gatesOpen[gateId] == open) return;
            gatesOpen[gateId] = open;
            Version++;
        }
        public bool IsGateOpen(int gateId) => MissionProgress.Valid(gateId) && gatesOpen[gateId];

        public Vector3 NodePosition(int index) => nodes[index].position;
        public int NodeCount => nodes == null ? 0 : nodes.Length;

        internal bool TryNodePosition(int index, out Vector3 position)
        {
            position = default;
            if (index < 0 || index >= NodeCount || nodes[index] == null) return false;
            position = nodes[index].position;
            return IsFinite(position);
        }

        internal struct SearchVisit
        {
            internal int Node, Parent;
            internal float G, H;
        }

        internal sealed class SearchTrace
        {
            internal readonly List<SearchVisit> Visits = new();
            internal string Result { get; private set; }

            internal void Finish(string result) => Result = result;

            internal string Describe()
            {
                var text = new StringBuilder(Result);
                int first = Mathf.Max(0, Visits.Count - 4);
                for (int i = first; i < Visits.Count; i++)
                {
                    SearchVisit visit = Visits[i];
                    string parent = visit.Parent < 0 ? "origin" : $"N{visit.Parent + 1}";
                    text.Append($"\nN{visit.Node + 1} ← {parent}   G {visit.G:F1}  H {visit.H:F1}  F {visit.G + visit.H:F1}");
                }
                if (first > 0) text.Append($"\n{Visits.Count} visits total");
                return text.ToString();
            }
        }

        public List<Vector3> FindRoute(Vector3 origin, Vector3 destination) => FindRouteWithTrace(origin, destination, null);

        internal List<Vector3> FindRouteWithTrace(Vector3 origin, Vector3 destination, SearchTrace trace)
        {
            using var sample = RouteMarker.Auto();
            trace?.Visits.Clear();
            var route = new List<Vector3>();
            trace?.Finish("No route");
            if (NodeCount == 0 || edges == null || !IsFinite(origin) || !IsFinite(destination)) return route;

            bool hasClosedEdge = false;
            foreach (GraphEdge edge in edges)
                if (edge.gateId >= 0 && !IsGateOpen(edge.gateId) && TryNodePosition(edge.from, out _) &&
                    TryNodePosition(edge.to, out _))
                { hasClosedEdge = true; break; }

            if (connectionValidator != null && !hasClosedEdge && CanConnect(origin, destination))
            {
                route.Add(destination);
                trace?.Finish("Direct validated connector");
                return route;
            }

            int start = Nearest(origin), goal = Nearest(destination);
            if (start < 0 || goal < 0) return route;
            if (start == goal && connectionValidator == null && CanConnect(origin, destination))
            {
                route.Add(destination);
                trace?.Finish("Direct within the same waypoint area");
                return route;
            }
            if (!CanConnect(origin, nodes[start].position))
                start = Nearest(origin, point => CanConnect(origin, point));
            if (!CanConnect(nodes[goal].position, destination))
                goal = Nearest(destination, point => CanConnect(point, destination));
            if (start < 0 || goal < 0) return route;

            if (Search(origin, destination, start, goal, null, null, route, trace))
            {
                trace?.Finish($"A*   N{start + 1} to N{goal + 1}");
                return route;
            }

            // Keep exact-node queries attached to their authored nodes. For an off-node
            // endpoint, try local alternatives only after the preferred route fails.
            if (origin == nodes[start].position && destination == nodes[goal].position) return route;
            var starts = Attachments(origin, start, hasClosedEdge, incoming: true);
            var goals = Attachments(destination, goal, hasClosedEdge, incoming: false);
            if (starts.Count + goals.Count <= 2) return route;
            trace?.Visits.Clear();
            if (Search(origin, destination, start, goal, starts, goals, route, trace))
                trace?.Finish("A* via alternate attachment");
            return route;
        }

        private const int AttachmentLimit = 4;
        private const float LocalAttachmentRadius = 4f;

        private List<int> Attachments(Vector3 endpoint, int preferred, bool hasClosedEdge, bool incoming)
        {
            var result = new List<int> { preferred };
            if (endpoint == nodes[preferred].position) return result;

            // Local connector edges must not jump between components separated by a
            // closed authored gate, even while NavMesh carving is catching up.
            bool[] openRegion = hasClosedEdge ? ReachableNodes(preferred, respectGates: true) : null;
            bool[] fullRegion = hasClosedEdge ? ReachableNodes(preferred, respectGates: false) : null;
            var candidates = new List<int>();
            for (int i = 0; i < NodeCount; i++)
                if (i != preferred && TryNodePosition(i, out Vector3 point) &&
                    (point - endpoint).sqrMagnitude <= LocalAttachmentRadius * LocalAttachmentRadius &&
                    (fullRegion == null || !fullRegion[i] || openRegion[i]))
                    candidates.Add(i);
            candidates.Sort((a, b) => (nodes[a].position - endpoint).sqrMagnitude
                .CompareTo((nodes[b].position - endpoint).sqrMagnitude));

            // Cap both physical probes and accepted attachments. A local alternative is
            // a direct NavMesh segment, so it cannot route through the mesh around a gate.
            for (int i = 0; i < Mathf.Min(candidates.Count, AttachmentLimit) && result.Count < AttachmentLimit; i++)
            {
                int node = candidates[i];
                Vector3 point = nodes[node].position;
                if (incoming ? CanConnectLocally(endpoint, point) : CanConnectLocally(point, endpoint))
                    result.Add(node);
            }
            return result;
        }

        private bool CanConnectLocally(Vector3 from, Vector3 to)
        {
            using var sample = ConnectMarker.Auto();
            if (connectionValidator != null && !connectionValidator(from, to)) return false;
            return NavMesh.SamplePosition(from, out var start, .25f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(to, out var end, .25f, NavMesh.AllAreas) &&
                !NavMesh.Raycast(start.position, end.position, out _, NavMesh.AllAreas);
        }

        private bool[] ReachableNodes(int start, bool respectGates)
        {
            var reached = new bool[NodeCount];
            var queue = new Queue<int>();
            reached[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (var edge in edges)
                {
                    int next = OtherEnd(edge, current, respectGates);
                    if (next < 0 || reached[next]) continue;
                    reached[next] = true;
                    queue.Enqueue(next);
                }
            }
            return reached;
        }

        private int OtherEnd(GraphEdge edge, int current, bool respectGates)
        {
            if (edge.gateId < -1 || edge.gateId >= gatesOpen.Length ||
                (respectGates && edge.gateId >= 0 && !IsGateOpen(edge.gateId))) return -1;
            int next = edge.from == current ? edge.to : edge.to == current ? edge.from : -1;
            return TryNodePosition(next, out _) ? next : -1;
        }

        private bool Search(Vector3 origin, Vector3 destination, int start, int goal,
            List<int> starts, List<int> goals, List<Vector3> route, SearchTrace trace)
        {
            int size = NodeCount;
            float[] cost = new float[size];
            int[] parent = new int[size];
            bool[] closed = new bool[size];
            bool[] open = new bool[size];
            Array.Fill(cost, float.PositiveInfinity);
            Array.Fill(parent, -1);
            if (starts == null)
            {
                open[start] = true;
                cost[start] = Vector3.Distance(origin, nodes[start].position);
            }
            else
                foreach (int node in starts)
                {
                    open[node] = true;
                    cost[node] = Vector3.Distance(origin, nodes[node].position);
                }

            // Use the world endpoint as the heuristic for multiple goal attachments.
            // Their Euclidean tail cost equals H, so the first popped goal is optimal.
            Vector3 heuristicTarget = goals == null ? nodes[goal].position : destination;
            while (true)
            {
                int current = -1;
                float bestF = float.PositiveInfinity;
                for (int i = 0; i < size; i++)
                {
                    if (!open[i]) continue;
                    float f = cost[i] + Vector3.Distance(nodes[i].position, heuristicTarget);
                    if (f < bestF) { bestF = f; current = i; }
                }
                if (current < 0) return false;
                Vector3 position = nodes[current].position;
                trace?.Visits.Add(new SearchVisit { Node = current, Parent = parent[current],
                    G = cost[current], H = Vector3.Distance(position, heuristicTarget) });
                if (goals == null ? current == goal : goals.Contains(current))
                {
                    for (int at = current; at >= 0; at = parent[at])
                        if (parent[at] >= 0 || origin != nodes[at].position) route.Add(nodes[at].position);
                    route.Reverse();
                    if (route.Count == 0 || route[route.Count - 1] != destination) route.Add(destination);
                    return true;
                }

                open[current] = false;
                closed[current] = true;
                foreach (var edge in edges)
                {
                    int next = OtherEnd(edge, current, respectGates: true);
                    if (next < 0 || closed[next]) continue;
                    float candidate = cost[current] + Vector3.Distance(position, nodes[next].position);
                    if (candidate >= cost[next]) continue;
                    cost[next] = candidate;
                    parent[next] = current;
                    open[next] = true;
                }
            }
        }

        private static bool IsFinite(Vector3 point)
            => !float.IsNaN(point.sqrMagnitude) && !float.IsInfinity(point.sqrMagnitude);

        private bool CanConnect(Vector3 from, Vector3 to)
        {
            using var sample = ConnectMarker.Auto();
            if (connectionValidator != null) return connectionValidator(from, to);
            if (from == to) return true; // Exact graph-node queries need no local connector.
            // SamplePosition alone does not establish connectivity across walls or gates.
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, 0.25f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(to, out NavMeshHit end, 0.25f, NavMesh.AllAreas)) return false;
            connectorPath ??= new NavMeshPath();
            return NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, connectorPath) &&
                connectorPath.status == NavMeshPathStatus.PathComplete;
        }

        private int Nearest(Vector3 point, Predicate<Vector3> canConnect = null)
        {
            int result = -1;
            float best = float.PositiveInfinity;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] == null || !IsFinite(nodes[i].position)) continue;
                float distance = (point - nodes[i].position).sqrMagnitude;
                if (distance < best && (canConnect == null || canConnect(nodes[i].position)))
                { best = distance; result = i; }
            }
            return result;
        }
    }
}
