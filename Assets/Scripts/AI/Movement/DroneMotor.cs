using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ShiftFour
{
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class DroneMotor : MonoBehaviour
    {
        [SerializeField] private Transform visual;
        [SerializeField] private float turnSpeed = 7f;
        private NavMeshAgent agent;
        private WaypointGraph graph;
        private List<Vector3> route = new List<Vector3>();
        private int step;
        private int graphVersion = -1;
        private Vector3 goal;
        private float bobClock;

        public float DistanceToGoal => Vector3.Distance(transform.position, goal);
        public bool HasRoute => step < route.Count;

        public void Configure(Transform model) => visual = model;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.updateRotation = false;
        }

        private void Start()
        {
            graph = GameSession.Instance != null ? GameSession.Instance.Graph : null;
            if (graph != null) graphVersion = graph.Version;
        }

        public void GoTo(Vector3 destination)
        {
            if (graph == null || !agent.isOnNavMesh) return;
            if (route.Count > 0 && graphVersion == graph.Version && Vector3.Distance(goal, destination) < 1.8f)
                return;
            goal = destination;
            graphVersion = graph.Version;
            route = graph.FindRoute(transform.position, destination);
            step = 0;
            Advance();
        }

        private void Update()
        {
            if (graph == null || !agent.isOnNavMesh) return;
            if (graphVersion != graph.Version) GoTo(goal);
            if (step < route.Count && !agent.pathPending &&
                Vector3.Distance(transform.position, route[step]) <= 1.35f)
            {
                step++;
                Advance();
            }
            Vector3 velocity = agent.desiredVelocity;
            velocity.y = 0f;
            if (velocity.sqrMagnitude > 0.08f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(velocity), turnSpeed * Time.deltaTime);
            if (visual != null)
            {
                bobClock += Time.deltaTime * 3f;
                Vector3 position = visual.localPosition;
                position.y = 1.05f + Mathf.Sin(bobClock) * 0.08f;
                visual.localPosition = position;
            }
        }

        private void Advance()
        {
            if (!agent.isOnNavMesh) return;
            if (step < route.Count) agent.SetDestination(route[step]);
            else agent.ResetPath();
        }

        public void Stop()
        {
            route.Clear();
            step = 0;
            if (agent != null && agent.isOnNavMesh) agent.ResetPath();
            enabled = false;
        }
    }
}
