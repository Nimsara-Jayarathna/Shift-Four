using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
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
        private enum Page { Overview, Prediction, Utility, Coordination, Controller, Adaptive, Performance }
        private static readonly string[] Pages = { "Overview", "Prediction", "Utility and Belief", "Coordination",
            "Agent Controller", "Adaptive Memory", "Performance" };
        private static readonly string[] Markers =
        {
            "ShiftFour.Decision", "ShiftFour.Interceptor.TacticalPlan", "ShiftFour.FindRoute", "ShiftFour.CanConnect",
            "ShiftFour.Interceptor.RouteValidation", "ShiftFour.InterceptorCoordination", "ShiftFour.InterceptorAssignment",
            "ShiftFour.PredictionEvaluation", "ShiftFour.InterceptorLearning", "ShiftFour.DroneMotor", "ShiftFour.DroneBody",
            "ShiftFour.DroneAnimation"
        };
        private readonly ProfilerRecorder[] timings = new ProfilerRecorder[Markers.Length];
        private readonly GUIContent report = new();
        private float scroll;
        private bool shown, tracing, sampling;
        private Page page;
        private OutdoorInterceptorDispatch squad;
        private readonly Vector3[] sharedPositions = new Vector3[24];
        private readonly Vector3[] reservedPositions = new Vector3[4];
        private readonly Color[] reservedColors = new Color[4];
        private int sharedCount, reservedCount;
        private int pinned = -1;
        private float nextRefresh;
        private GUIStyle title, body;
        private readonly InterceptorBeliefState.Region[] beliefSnapshot = new InterceptorBeliefState.Region[24];
        private readonly string[] beliefLabels = new string[24];
        private int beliefCount;
        private Vector3? searchDestination;

        internal void Bind(PlayerController actor, IReadOnlyList<InterceptorBrain> units)
        { player = actor; stations = units; squad = GetComponent<OutdoorInterceptorDispatch>(); }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6))
            {
                if (shown && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                {
                    pinned = pinned + 1 >= stations.Count ? -1 : pinned + 1;
                    scroll = 0f;
                }
                else if (shown && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                    Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)))
                {
                    page = (Page)(((int)page + 1) % Pages.Length);
                    scroll = 0f;
                }
                else shown = !shown;
                nextRefresh = 0f;
            }

            if (!shown || player == null || player.MenuBlocksInput)
            {
                Follow(null);
                StopTimers();
                return;
            }
            if (Input.GetKeyDown(KeyCode.PageDown)) scroll += 180f;
            if (Input.GetKeyDown(KeyCode.PageUp)) scroll = Mathf.Max(0f, scroll - 180f);
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .2f;
            Follow(pinned >= 0 ? stations[pinned] : FindResponder());
            if (page != Page.Performance) StopTimers();
            report.text = ReadPage();
        }

        private string ReadPage() => page switch
        {
            Page.Prediction => ReadPrediction(),
            Page.Utility => ReadUtility(),
            Page.Coordination => ReadSquad(),
            Page.Controller => ReadController(),
            Page.Adaptive => ReadAdaptive(),
            Page.Performance => ReadPerformance(),
            _ => ReadOverview()
        };

        private void Follow(InterceptorBrain next)
        {
            bool trace = next != null && page == Page.Controller;
            if (ReferenceEquals(unit, next) && trace == tracing) return;
            if (tracing && unit != null) unit.DiagnosticMotor?.RequestSearchEvidence(false);
            unit = next;
            tracing = trace;
            visual = next != null && page == Page.Controller ? next.GetComponentInChildren<DroneRotorVisuals>() : null;
            if (tracing) unit.DiagnosticMotor?.RequestSearchEvidence(true);
        }

        private void OnDisable()
        {
            Follow(null);
            StopTimers();
        }

        private void StopTimers()
        {
            if (!sampling) return;
            sampling = false;
            for (int i = 0; i < timings.Length; i++)
            {
                if (!timings[i].Valid) continue;
                timings[i].Dispose();
                timings[i] = default;
            }
        }

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

        private string ReadOverview()
        {
            if (squad == null) return "No Interceptor deployment";
            var text = new StringBuilder(450);
            text.AppendLine($"Deployed {squad.SpawnedCount}/{squad.ConfiguredCount}   Available {squad.AvailableCount}");
            text.AppendLine($"Active response slots {squad.ActiveCount}/{squad.ResponseLimit}");
            for (int i = 0; i < 4; i++)
            {
                var agent = squad.Unit(i);
                if (agent == null) { text.AppendLine($"{i + 1:00}   Unavailable"); continue; }
                text.AppendLine($"{i + 1:00}   {agent.ResponsePhase}  ·  {squad.Role(i + 1, Time.time)}");
                text.AppendLine($"       {agent.DiagnosticResponse.Action} · {agent.CurrentDecision}");
            }
            return text.ToString();
        }

        private string ReadPrediction()
        {
            var policy = unit != null ? unit.Policy : null;
            if (policy == null || !float.IsFinite(policy.LastObservedAt)) return "No confirmed visual observation";
            var text = new StringBuilder(550);
            text.AppendLine($"Last sighting {policy.LastObserved:F1} · {policy.ObservationAge(Time.time):F1} s ago");
            text.AppendLine($"Velocity {policy.EstimatedVelocity:F2} m/s");
            text.AppendLine($"Acceleration {policy.EstimatedAcceleration:F2} m/s²");
            text.AppendLine($"Confidence {policy.Confidence:F2}  ·  Uncertainty {policy.MotionUncertainty(Time.time):F2} m");
            foreach (var hypothesis in policy.Hypotheses)
                text.AppendLine($"{hypothesis.Kind}  ·  support {hypothesis.Support:P0}  ·  weight {hypothesis.Weight:P0}");
            if (policy.SelectedCandidate is InterceptorCandidate selected)
                text.AppendLine($"Junction {selected.Node + 1} {selected.Position:F1}  ·  {selected.Hypothesis}\n" +
                    $"Player ETA {selected.PlayerArrival:F2}s   Unit ETA {selected.DroneArrival:F2}s");
            else text.AppendLine("No selected intersection");
            text.AppendLine($"Prediction state {policy.State}  ·  Replans {policy.Replans}  ·  Expired attempts {policy.FailedAttempts}");
            return text.ToString();
        }

        private string ReadUtility()
        {
            beliefCount = 0;
            searchDestination = null;
            if (unit == null || unit.DiagnosticResponse is not OutdoorInterceptorResponse response)
                return "No live Interceptor";
            var utility = response.Utility;
            var text = new StringBuilder(800);
            text.AppendLine($"{utility.Current.Action}  ·  score {utility.Current.Utility:F2}  ·  commit {utility.Remaining(Time.time):F2}s");
            text.AppendLine($"{utility.Current.Reason}  ·  {utility.Transition}");
            text.AppendLine("Eligible actions show total, evidence, travel, uncertainty and stability");
            foreach (var choice in utility.Choices)
                text.AppendLine(choice.Eligible
                    ? $"{choice.Action}   {choice.Utility:F2}   {choice.Evidence:F2}   {choice.Travel:F2}   {choice.Uncertainty:F2}   {choice.Stability:F2}"
                    : $"{choice.Action}  ·  {choice.Reason}");
            var belief = response.Belief;
            beliefCount = Mathf.Min(belief.Regions.Count, beliefSnapshot.Length);
            text.AppendLine($"BELIEF   {beliefCount} regions  ·  entropy {belief.Entropy:F2}  ·  search {response.SearchRemaining(Time.time):F1}s");
            for (int i = 0; i < beliefCount; i++)
            {
                var region = belief.Regions[i];
                beliefSnapshot[i] = region;
                beliefLabels[i] = $"{region.Probability:P0}";
                text.AppendLine($"Region {region.Id}  {region.Probability:P0}  ·  {region.Evidence.Source}  ·  {region.Inspection}");
            }
            if (utility.Current.Action is InterceptorAction.InvestigateSound or InterceptorAction.SearchBeliefRegion)
                searchDestination = utility.Current.Destination;
            return text.ToString();
        }

        private string ReadController()
        {
            var motor = unit != null ? unit.DiagnosticMotor : null;
            if (motor == null) return "Motor unavailable";
            var text = new StringBuilder(650);
            text.AppendLine($"{motor.MovementStatus}  ·  {motor.MovementMode}  ·  Path {motor.PathStatus}");
            text.AppendLine($"Actual {motor.ActualSpeed:F2} m/s  ·  Target {motor.TargetSpeed:F2} m/s  ·  Graph v{motor.PlannedGraphVersion}");
            text.AppendLine($"Replans {motor.ReplanCount}  ·  Block {motor.BlockedReason}");
            var route = unit.ActiveAStarRoute;
            if (route != null)
                for (int i = 0; i < route.Count; i++)
                    text.AppendLine($"{(i == unit.ActiveRouteStep ? '>' : ' ')} Waypoint {i + 1}  {route[i]:F1}");
            text.AppendLine(motor.SearchEvidence?.Result != null
                ? motor.SearchEvidence.Describe() : "Awaiting next traced route query");
            if (visual != null) text.AppendLine(visual.AnimationReport);
            return text.ToString();
        }

        private string ReadPerformance()
        {
            sampling = true;
            var text = new StringBuilder(800);
            text.AppendLine($"Editor/game frame {Time.unscaledDeltaTime * 1000f:F1} ms");
            if (unit != null) text.AppendLine($"Selected unit's last tactical plan: {unit.DiagnosticResponse.TacticalRouteQueries} fresh route queries");
            text.AppendLine("Inclusive scene timings · mean / peak per sampled active frame");
            for (int i = 0; i < timings.Length; i++)
            {
                if (!timings[i].Valid)
                    timings[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, Markers[i], 120,
                        ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
                int frames = 0;
                long total = 0, peak = 0;
                for (int sample = 0; timings[i].Valid && sample < timings[i].Count; sample++)
                {
                    long nanoseconds = timings[i].GetSample(sample).Value;
                    if (nanoseconds <= 0) continue;
                    frames++;
                    total += nanoseconds;
                    if (nanoseconds > peak) peak = nanoseconds;
                }
                text.Append(Markers[i].Substring("ShiftFour.".Length)).Append("  ");
                text.AppendLine(frames == 0 ? "N/A" : $"{total / (1000d * frames):F2} / {peak / 1000d:F2} µs ({frames})");
            }
            return text.ToString();
        }

        private string ReadAdaptive()
        {
            var policy = unit != null ? unit.Policy : null;
            var learning = unit != null ? unit.DiagnosticResponse?.Learning : null;
            if (learning == null) return "ADAPTIVE MEMORY\nNo live Interceptor";
            float now = Time.time;
            var report = new StringBuilder(768);
            report.AppendLine($"ADAPTIVE MEMORY  ·  Interceptor {learning.Unit:00}");
            report.AppendLine(learning.Enabled ? $"Learning rate {learning.Rate:F2}" : "F3 baseline · adaptation off");
            report.AppendLine($"Evaluated {learning.Evaluated}  ·  Unevaluated {learning.Unevaluated}  ·  Pending {learning.Pending}");
            report.AppendLine($"Successful {learning.Successful}  ·  Failed {learning.Failed}");
            report.AppendLine(learning.Evaluated > 0 ? $"Recent mean error {learning.MeanError:F2} m" : "Recent mean error —");
            report.AppendLine($"Horizon {learning.Horizon:F2} s  ·  Motion uncertainty {policy?.MotionUncertainty(now) ?? 0f:F2} m");
            report.AppendLine($"Direction changes {learning.DirectionChanges:P0}  ·  Stops {learning.Stopping:P0}  ·  Reversals {learning.Reversals:P0}");
            report.AppendLine($"Observed speed {learning.MeanSpeed:F1} m/s  ·  Variability {learning.SpeedVariation:F1} m/s");
            report.AppendLine("HYPOTHESIS  |  SUPPORT → WEIGHT  |  RELIABILITY  |  LAST ERROR");
            for (int index = 0; index < 6; index++)
            {
                var kind = (InterceptorHypothesisKind)index;
                string weights = "—";
                if (policy != null)
                    foreach (var hypothesis in policy.Hypotheses)
                        if (hypothesis.Kind == kind) { weights = $"{hypothesis.Support:P0} → {hypothesis.Weight:P0}"; break; }
                float error = learning.LastError(kind);
                report.AppendLine($"{kind,-10}  {weights,-15}  {learning.Reliability(kind, now):F2}  " +
                    (float.IsFinite(error) ? $"{error:F2} m" : "—"));
            }
            if (learning.LastResult is InterceptorPredictionEvaluator.Result latest)
                report.AppendLine($"Last evaluation #{latest.Prediction.Id}  {latest.Prediction.Kind}  ·  {latest.Outcome}");
            report.AppendLine(learning.LastAdaptation);
            var memory = learning.Memory.Entries;
            int live = 0, failed = 0, listed = 0;
            for (int i = memory.Count - 1; i >= 0; i--)
            {
                var entry = memory[i];
                if (entry.Expires <= now) continue;
                live++;
                if (InterceptorTacticalMemory.IsNavigation(entry.Outcome) || entry.Outcome == InterceptorOutcome.MissedArrivalWindow)
                    failed++;
            }
            report.AppendLine($"Tactical memory {live}/64  ·  Recent failed destinations {failed}");
            for (int i = memory.Count - 1; i >= 0; i--)
            {
                var entry = memory[i];
                if (entry.Expires <= now || (!InterceptorTacticalMemory.IsNavigation(entry.Outcome) &&
                    entry.Outcome != InterceptorOutcome.MissedArrivalWindow)) continue;
                report.AppendLine($"Latest route failure  {entry.Destination:F1}  ·  {entry.Outcome}");
                break;
            }
            for (int i = memory.Count - 1; i >= 0 && listed < 4; i--)
            {
                var entry = memory[i];
                if (entry.Expires <= now) continue;
                report.AppendLine($"{entry.Outcome}  {entry.Destination:F1}  ·  {now - entry.Time:F1} s ago");
                listed++;
            }
            return report.ToString();
        }

        private string ReadSquad()
        {
            sharedCount = reservedCount = 0;
            if (squad == null) return "Squad unavailable";
            var text = new StringBuilder();
            float now = Time.time;
            var coordinator = squad.Coordinator;
            var records = squad.Knowledge.Records;
            text.AppendLine("SQUAD COORDINATION");
            text.AppendLine($"Configured {squad.ConfiguredCount}  Spawned {squad.SpawnedCount}  Available {squad.AvailableCount}");
            text.AppendLine($"Responses {squad.ActiveCount}/{squad.ResponseLimit}  Shared observations {records.Count}");
            float newest = float.NegativeInfinity;
            foreach (var record in records)
                if (record.Evidence.Fresh(now))
                {
                    newest = Mathf.Max(newest, record.Evidence.Time);
                    sharedPositions[sharedCount++] = record.Evidence.Position;
                }
            if (float.IsFinite(newest)) text.AppendLine($"Newest observation {now - newest:F1}s ago");
            text.AppendLine($"Group value {coordinator.GroupUtility:F2}  Combinations {coordinator.Evaluations}");
            text.AppendLine($"Updates {coordinator.Updates}  Reassignments {coordinator.Reassignments}");
            for (int i = 0; i < 4; i++)
            {
                var member = squad.Unit(i);
                if (member == null) { text.AppendLine($"{i + 1:00}  Unavailable"); continue; }
                var role = squad.Role(i + 1, now);
                var response = member.DiagnosticResponse;
                var lease = coordinator.Reservations[i];
                bool active = lease.Active(now);
                text.AppendLine($"{i + 1:00}  {response.State} / {role}  |  {response.Action} {response.Utility.Current.Utility:F2}");
                var goal = active ? (Vector3?)lease.Task.Destination : member.DiagnosticMotor?.FinalDestination;
                if (goal.HasValue) text.AppendLine($"     {goal.Value:F1}  |  {member.DiagnosticMotor.PathStatus}");
                if (active)
                {
                    reservedPositions[reservedCount] = lease.Task.Destination;
                    reservedColors[reservedCount++] = role switch
                    {
                        InterceptorRole.Primary => RejectedColor,
                        InterceptorRole.Secondary => new Color(1f, .55f, .26f),
                        InterceptorRole.Search => LastSeenColor,
                        _ => JunctionColor
                    };
                    text.AppendLine($"     #{lease.Id} expires in {lease.Until - now:F1}s  ETA {lease.Task.Eta:F1}s");
                }
                text.Append("     Proposals");
                foreach (var task in squad.Matrix[i])
                    text.Append($"  {task.Action} {task.Utility:F2}" +
                        (coordinator.Conflicts(i + 1, task.Destination, now) ? " [reserved]" : ""));
                if (squad.Matrix[i].Count == 0) text.Append(" —");
                text.AppendLine();
            }
            return text.ToString();
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

                if (page == Page.Utility && searchDestination.HasValue)
                    Mark(camera, searchDestination.Value, DestinationColor, 15f);
                for (int i = 0; page == Page.Utility && i < beliefCount; i++)
                {
                    var region = beliefSnapshot[i];
                    Color color = region.Inspection switch
                    {
                        InterceptorInspection.Inspected => RouteColor,
                        InterceptorInspection.Unreachable or InterceptorInspection.Occluded => RejectedColor,
                        _ => LastSeenColor
                    };
                    Mark(camera, region.Position, color, 5f + region.Probability * 14f);
                    Vector3 screen = camera.WorldToScreenPoint(region.Position);
                    if (screen.z > 0f)
                        GUI.Label(new Rect(screen.x + 8f, Screen.height - screen.y - 10f, 55f, 20f), beliefLabels[i]);
                }
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
            if (page == Page.Coordination)
            {
                for (int i = 0; i < sharedCount; i++) Mark(camera, sharedPositions[i], LastSeenColor, 6f);
                for (int i = 0; i < reservedCount; i++) Mark(camera, reservedPositions[i], reservedColors[i], 16f);
                foreach (var member in stations)
                {
                    if (member == unit || !member.CoordinationAvailable) continue;
                    Polyline(camera, member.ActiveAStarRoute, member.ActiveRouteStep, member.transform.position, RouteColor);
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
                fontStyle = FontStyle.Bold, normal = { textColor = Color.white }
            };
            body ??= new GUIStyle(GUI.skin.label)
            {
                wordWrap = true, normal = { textColor = new Color(.88f, .94f, .98f) }
            };
            float width = Mathf.Min(560f, Screen.width - 24f);
            title.fontSize = Screen.width < 850 ? 14 : 17;
            body.fontSize = Screen.width < 850 ? 11 : 13;
            const float margin = 12f;
            float contentWidth = width - 2f * margin;
            float fullHeight = body.CalcHeight(report, contentWidth);
            float height = Mathf.Min(fullHeight, Mathf.Max(24f, Screen.height - 127f));
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, fullHeight - height));
            Rect panel = new(Screen.width - width - margin, margin, width, height + 102f);
            GUI.color = new Color(.025f, .055f, .073f, .96f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            float x = panel.x + margin;
            GUI.Label(new Rect(x, panel.y + 5f, contentWidth, 26f),
                $"{(int)page + 1}/{Pages.Length}  {Pages[(int)page].ToUpperInvariant()}", title);
            GUI.Label(new Rect(x, panel.y + 30f, contentWidth, 22f),
                unit != null ? $"{unit.name}  ·  {(pinned < 0 ? "Auto" : "Pinned")}" : "No live unit", body);
            GUI.BeginGroup(new Rect(x, panel.y + 52f, contentWidth, height));
            GUI.Label(new Rect(0f, -scroll, contentWidth, fullHeight), report, body);
            GUI.EndGroup();
            GUI.Label(new Rect(x, panel.y + height + 55f, contentWidth, 40f),
                "F6 Close   Shift+F6 Unit   Ctrl/⌘+F6 Tab   PgUp/PgDn Scroll", body);
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
