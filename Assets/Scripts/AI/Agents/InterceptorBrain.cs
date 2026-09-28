using UnityEngine;

namespace ShiftFour
{
    // Member 4: uses observed velocity to score junctions ahead of the player.
    public sealed class InterceptorBrain : DroneBrain
    {
        protected override DecisionOption Decide(bool visible, Vector3 playerPosition, float distance)
        {
            bool recent = Time.time - LastSeenAt < 4f;
            Vector3 source = visible ? playerPosition : LastKnown;
            Vector3 direction = ObservedVelocity;
            direction.y = 0f;
            DecisionOption best = Option("Watch control room", Home, 10f);
            if (recent)
            {
                best = Best(best, Option("Pursue last known position", source, 34f));
                WaypointGraph graph = GameSession.Instance?.Graph;
                if (graph != null && direction.sqrMagnitude > 0.3f)
                {
                    direction.Normalize();
                    for (int i = 0; i < graph.NodeCount; i++)
                    {
                        Vector3 junction = graph.NodePosition(i);
                        Vector3 fromPlayer = junction - source;
                        fromPlayer.y = 0f;
                        float ahead = Vector3.Dot(fromPlayer, direction);
                        if (ahead < 1f || !Reachable(junction)) continue;
                        float score = 42f + Mathf.Min(ahead, 15f) * 1.5f
                            - Vector3.Distance(transform.position, junction) * 0.3f;
                        best = Best(best, Option("Intercept ahead", junction, score));
                    }
                }
            }
            return Best(best,
                Option("Engage visible player", playerPosition, visible && distance < 3.5f ? 76f : -1f));
        }
    }
}
