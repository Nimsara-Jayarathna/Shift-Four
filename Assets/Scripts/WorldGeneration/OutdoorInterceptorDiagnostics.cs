using System.Collections.Generic;
using UnityEngine;

namespace ShiftFour
{
    // Read-only Game view overlay. Never requests a route or queries the player's position.
    [DisallowMultipleComponent]
    internal sealed class OutdoorInterceptorDiagnostics : MonoBehaviour
    {
        private static readonly Color JunctionColor = new(.6f, .72f, .82f);
        private static readonly Color RouteColor = new(.29f, 1f, .53f);
        private static readonly Color PredictionColor = new(.36f, .89f, 1f);
        private static readonly Color LastSeenColor = new(1f, .79f, .29f);
        private static readonly Color WaypointColor = new(.95f, 1f, .72f);
        private static readonly Color DestinationColor = new(.85f, .58f, 1f);
        private static readonly Color RejectedColor = new(1f, .4f, .39f);
        private IReadOnlyList<InterceptorBrain> stations;
        private PlayerController player;
        private InterceptorBrain unit;
        private DroneRotorVisuals visual;
        private bool shown;
        private int pinned = -1;
        private float nextRefresh;
        private string report;
        private GUIStyle title, body;

        internal void Bind(PlayerController actor, IReadOnlyList<InterceptorBrain> units)
        { player = actor; stations = units; }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6))
            {
                if (shown && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                    pinned = pinned + 1 >= stations.Count ? -1 : pinned + 1;
                else shown = !shown;
                nextRefresh = 0f;
            }

            if (!shown || player == null || player.MenuBlocksInput)
            {
                Follow(null);
                return;
            }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .15f;
            Follow(pinned >= 0 ? stations[pinned] : FindResponder());
            report = ReadReport();
        }

        private void Follow(InterceptorBrain next)
        {
            if (ReferenceEquals(unit, next)) return;
            if (unit != null) unit.DiagnosticMotor?.RequestSearchEvidence(false);
            unit = next;
            visual = next != null ? next.GetComponentInChildren<DroneRotorVisuals>() : null;
            if (unit != null) unit.DiagnosticMotor?.RequestSearchEvidence(true);
        }

        private void OnDisable() => Follow(null);

        private InterceptorBrain FindResponder()
        {
            InterceptorBrain first = null;
            foreach (var station in stations)
            {
                if (station == null || !station.DiagnosticAlive) continue;
                if (station.IsResponding) return station;
                if (first == null || station.ResponsePhase == "Return") first = station;
            }
            return first;
        }

        private string ReadReport()
        {
            if (unit == null) return "No live Interceptor";
            var policy = unit.Policy;
            var candidate = policy?.SelectedCandidate;
            bool observed = policy != null && float.IsFinite(policy.LastObservedAt);
            string observation = observed
                ? $"{policy.LastObserved:F1} · {policy.ObservationAge(Time.time):F1} s ago" : "No visual observation";
            string proposal = candidate != null
                ? $"J{candidate.Node + 1}  {candidate.Position:F1} · score {candidate.Score:F1}\n" +
                  $"Estimated arrival  Player {candidate.PlayerArrival:F2} s / Drone {candidate.DroneArrival:F2} s\n" +
                  $"Validated candidate route  {InterceptorPolicy.Length(candidate.DroneRoute):F1} m" : "No selected junction";
            var route = unit.ActiveAStarRoute;
            float routeLength = 0f;
            var previous = unit.transform.position;
            if (route != null)
                for (int i = unit.ActiveRouteStep; i < route.Count; i++)
                {
                    routeLength += Vector3.Distance(previous, route[i]);
                    previous = route[i];
                }

            var motor = unit.DiagnosticMotor;
            string movement = motor == null ? "Motor unavailable" :
                $"AGENT CONTROLLER\n{motor.MovementStatus}   |   {motor.MovementMode}   |   Path {motor.PathStatus}\n" +
                $"Speed {motor.ActualSpeed:F1} / {motor.TargetSpeed:F1} m/s   |   Graph version {motor.PlannedGraphVersion}\n" +
                $"Waypoint {(route != null ? (unit.ActiveRouteStep + 1).ToString() : "—")} / {route?.Count ?? 0}" +
                $"   |   A* remaining {(route != null ? routeLength.ToString("F1") + " m" : "—")}\n" +
                $"Route replans {motor.ReplanCount}   |   Block {motor.BlockedReason}\n" +
                (motor.SearchEvidence?.Result != null ? motor.SearchEvidence.Describe() : "Awaiting next route search");

            return $"{unit.name}  ·  {(pinned < 0 ? "AUTO" : "PINNED")}\n" +
                   $"Response  {unit.ResponsePhase}   |   Prediction  {policy?.State.ToString() ?? "Awaiting alert"}\n" +
                   $"{unit.CurrentDecision}\n" +
                   $"Last seen  {observation}\n" +
                   $"Estimated velocity  {(observed ? policy.EstimatedVelocity.ToString("F1") + " m/s" : "—")}\n" +
                   $"Confidence at last decision  {(observed ? policy.Confidence.ToString("F2") : "—")}\n" +
                   $"{proposal}\n" +
                   $"Prediction replans {policy?.Replans ?? 0}   |   Missed predictions {policy?.FailedAttempts ?? 0}\n\n" +
                   movement + (visual != null ? "\n" + visual.AnimationReport : "");
        }

        private void OnGUI()
        {
            if (!shown || player == null || player.MenuBlocksInput || Event.current.type != EventType.Repaint) return;
            var camera = player.ViewCamera;
            if (camera == null) return;
            int previousDepth = GUI.depth;
            Color previousColor = GUI.color;
            GUI.depth = -80;
            if (unit != null)
            {
                var graph = unit.DiagnosticGraph;
                if (graph != null)
                    for (int i = 0; i < graph.NodeCount; i++)
                        if (graph.TryNodePosition(i, out Vector3 node)) Mark(camera, node, JunctionColor, 6f);
                foreach (var point in unit.ResponseJunctions) Mark(camera, point, JunctionColor, 4f);

                Polyline(camera, unit.ActiveAStarRoute, unit.ActiveRouteStep, unit.transform.position, RouteColor);
                var motor = unit.DiagnosticMotor;
                if (motor?.FinalDestination is Vector3 destination) Mark(camera, destination, DestinationColor, 12f);
                if (motor?.CurrentWaypoint is Vector3 waypoint) Mark(camera, waypoint, WaypointColor, 8f);
                if (motor?.InvalidWaypoint is Vector3 invalid) Mark(camera, invalid, RejectedColor, 12f);
                if (motor != null && motor.ActualSpeed > .15f)
                    Line(camera, unit.transform.position, unit.transform.position + Vector3.ClampMagnitude(motor.ActualVelocity, 2f), WaypointColor);

                var policy = unit.Policy;
                if (policy != null)
                {
                    if (float.IsFinite(policy.LastObservedAt)) Mark(camera, policy.LastObserved, LastSeenColor, 10f);
                    foreach (var rejection in policy.Rejected)
                        if (rejection.Reason is InterceptorRejection.Blocked or InterceptorRejection.CoolingDown)
                            Mark(camera, rejection.Position, RejectedColor, 7f);
                    if (policy.SelectedCandidate is InterceptorCandidate candidate)
                    {
                        var path = policy.PredictedTrajectory;
                        if (path.Length > 1) Polyline(camera, path, 1, path[0], PredictionColor);
                        Mark(camera, candidate.Position, RouteColor, 13f);
                    }
                }
            }
            DrawPanel();
            GUI.depth = previousDepth;
            GUI.color = previousColor;
        }

        private void DrawPanel()
        {
            title ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Color.white }
            };
            body ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, wordWrap = true, normal = { textColor = new Color(.88f, .94f, .98f) }
            };
            float width = Mathf.Min(405f, Screen.width - 24f);
            var content = new GUIContent(report ?? string.Empty);
            const float margin = 12f;
            float textHeight = body.CalcHeight(content, width - 2f * margin);
            Rect panel = new(Screen.width - width - margin, 16f, width, textHeight + 116f);
            GUI.color = new Color(.025f, .055f, .073f, .96f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            float x = panel.x + margin;
            GUI.Label(new Rect(x, panel.y + 8, width - 24f, 25), "INTERCEPTOR DIAGNOSTICS", title);
            GUI.Label(new Rect(x, panel.y + 35, width - 24f, textHeight), content, body);
            float y = panel.y + 39 + textHeight;
            Legend(x, y, JunctionColor, "Nodes");
            Legend(x + 53, y, RouteColor, "A* / junction");
            Legend(x + 147, y, PredictionColor, "Prediction");
            Legend(x + 230, y, LastSeenColor, "Last seen");
            Legend(x + 305, y, RejectedColor, "Blocked");
            Legend(x, y + 20, WaypointColor, "Waypoint");
            Legend(x + 83, y + 20, DestinationColor, "Destination");
            GUI.Label(new Rect(x, y + 44, width - 24f, 22), "F6  Close     Shift + F6  Next unit / Auto", body);
        }

        private void Legend(float x, float y, Color color, string text)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y + 7, 4, 4), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 6, y, 85, 21), text, body);
        }

        private static Vector2 ScreenPoint(Vector3 p) => new(p.x, Screen.height - p.y);
        private static void Mark(Camera camera, Vector3 point, Color color, float size)
        {
            Vector3 projected = camera.WorldToScreenPoint(point + Vector3.up * .35f);
            if (projected.z <= camera.nearClipPlane || projected.x < 0 || projected.x > Screen.width ||
                projected.y < 0 || projected.y > Screen.height) return;
            Vector2 p = ScreenPoint(projected);
            GUI.color = color;
            GUI.DrawTexture(new Rect(p.x - size * .5f, p.y - size * .5f, size, size), Texture2D.whiteTexture);
        }

        private static void Polyline(Camera camera, IReadOnlyList<Vector3> route, int start, Vector3 previous, Color color)
        {
            if (route == null) return;
            for (int i = start; i < route.Count; i++)
            {
                Line(camera, previous, route[i], color);
                previous = route[i];
            }
        }

        private static void Line(Camera camera, Vector3 start, Vector3 end, Color color)
        {
            Vector3 a = camera.WorldToScreenPoint(start + Vector3.up * .35f);
            Vector3 b = camera.WorldToScreenPoint(end + Vector3.up * .35f);
            if (a.z <= camera.nearClipPlane || b.z <= camera.nearClipPlane) return;
            Vector2 from = ScreenPoint(a), delta = ScreenPoint(b) - from;
            Matrix4x4 matrix = GUI.matrix;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            GUI.DrawTexture(new Rect(from.x, from.y, delta.magnitude, 2f), Texture2D.whiteTexture);
            GUI.matrix = matrix;
        }
    }
}
