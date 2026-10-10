using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShiftFour
{
    [Serializable]
    public struct GraphEdge
    {
        public int from;
        public int to;
        public bool doorShortcut;
        public int gateId;
        public GraphEdge(int a, int b, bool door) { from = a; to = b; doorShortcut = door; gateId = 0; }
        public GraphEdge(int a, int b, int gate) { from = a; to = b; doorShortcut = true; gateId = gate; }
    }

    public sealed class WaypointGraph : MonoBehaviour
    {
        [SerializeField] private Transform[] nodes;
        [SerializeField] private GraphEdge[] edges;
        [SerializeField] private bool doorOpen;
        [SerializeField] private bool[] gatesOpen = new bool[4];
        public int Version { get; private set; }

        public void Configure(Transform[] waypoints, GraphEdge[] connections)
        {
            nodes = waypoints;
            edges = connections;
        }

        public void SetGateOpen(int id, bool open)
        {
            if (id < 0 || id >= gatesOpen.Length || gatesOpen[id] == open) return;
            gatesOpen[id] = open;
            Version++;
        }

        public void SetDoorOpen(bool open)
        {
            if (doorOpen == open) return;
            doorOpen = open;
            Version++;
        }

        public Vector3 NodePosition(int index) => nodes[index].position;
        public int NodeCount => nodes == null ? 0 : nodes.Length;

        public List<Vector3> FindRoute(Vector3 origin, Vector3 destination)
        {
            List<Vector3> route = new List<Vector3>();
            if (nodes == null || nodes.Length == 0 || edges == null) return route;
            int start = Nearest(origin);
            int goal = Nearest(destination);
            int size = nodes.Length;
            float[] cost = new float[size];
            int[] parent = new int[size];
            bool[] closed = new bool[size];
            bool[] open = new bool[size];
            for (int i = 0; i < size; i++) { cost[i] = float.PositiveInfinity; parent[i] = -1; }
            cost[start] = 0f;
            open[start] = true;

            while (true)
            {
                int current = -1;
                float bestF = float.PositiveInfinity;
                for (int i = 0; i < size; i++)
                {
                    if (!open[i]) continue;
                    float f = cost[i] + Vector3.Distance(nodes[i].position, nodes[goal].position);
                    if (f < bestF) { bestF = f; current = i; }
                }
                if (current < 0) return route; // No traversable route.
                if (current == goal) break;
                open[current] = false;
                closed[current] = true;

                foreach (GraphEdge edge in edges)
                {
                    if (edge.doorShortcut && !(edge.gateId >= 0 && edge.gateId < gatesOpen.Length ? gatesOpen[edge.gateId] : doorOpen)) continue;
                    int next = edge.from == current ? edge.to : edge.to == current ? edge.from : -1;
                    if (next < 0 || next >= size || closed[next]) continue;
                    float candidate = cost[current] + Vector3.Distance(nodes[current].position, nodes[next].position);
                    if (candidate >= cost[next]) continue;
                    cost[next] = candidate;
                    parent[next] = current;
                    open[next] = true;
                }
            }

            List<int> order = new List<int>();
            for (int at = goal; at >= 0; at = parent[at]) order.Add(at);
            order.Reverse();
            foreach (int index in order)
                if (Vector3.Distance(origin, nodes[index].position) > 1.2f)
                    route.Add(nodes[index].position);
            if (route.Count == 0 || Vector3.Distance(route[route.Count - 1], destination) > 0.5f)
                route.Add(destination);
            return route;
        }

        private int Nearest(Vector3 point)
        {
            int result = 0;
            float best = float.PositiveInfinity;
            for (int i = 0; i < nodes.Length; i++)
            {
                float distance = (point - nodes[i].position).sqrMagnitude;
                if (distance < best) { best = distance; result = i; }
            }
            return result;
        }
    }
}
