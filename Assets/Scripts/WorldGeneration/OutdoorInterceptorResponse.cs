using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.AI;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    // Owns the outdoor dispatch lifecycle. Prediction, flight and fire remain on the original Interceptor.
    internal sealed class OutdoorInterceptorResponse
    {
        internal enum Phase { Patrol, Alerted, Intercept, Attack, Reposition, Search, Return, Destroyed }

        internal const float AttackDistance = 10f;
        private static readonly ProfilerMarker PlanningMarker = new("ShiftFour.Interceptor.TacticalPlan");
        private static readonly float[] ApproachAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
        private static readonly float[] ApproachRadii = { 4f, 6.5f };
        private readonly OutdoorScoutEncounter outdoors;
        private readonly NavMeshQueryFilter filter;
        private readonly OutdoorInterceptorDispatch dispatch;
        private readonly NavMeshPath connection = new NavMeshPath();
        private readonly Vector3 station;
        private readonly List<Vector3> roads = new List<Vector3>();
        private readonly float pursuitTime, pursuitDistance;
        private Vector3 lastKnown, goal;
        private float alertEnds, responseEnds, reportExpires, planAt, searchEnds, burstReadyAt;
        private float nextResponseAt;
        private int fired, patrolPost = 1;
        private bool seen, sightLastTick;
        private Vector3 lastBurstPost;
        private int burstCount;
        private bool relocationPlanned;
        private InterceptorDecisionSettings settings;
        private InterceptorBeliefState belief;
        private InterceptorUtilityDecision utility;
        private InterceptorEvidence? pendingEvidence;
        private float lastStarterSound = float.NegativeInfinity;
        private bool soundEngagement;
        private int lastGraphVersion = -1;

        private readonly List<(int Index, float Rank)> searchOrder = new(24);
        private readonly RaycastHit[] sightHits = new RaycastHit[16];
        private readonly Collider[] sightOverlaps = new Collider[16];
        private static readonly Vector3[] InspectionOffsets =
            { Vector3.zero, Vector3.forward, Vector3.right, Vector3.back, Vector3.left };

        private readonly List<InterceptorTask> tasks = new(6);
        private float shareAt;
        private int received, learnedThrough;
        private Vector3? observedBlock;
        private int activeRoute;
        private Vector3? routedTo;
        internal InterceptorLearning Learning { get; private set; }
        internal int UnitId { get; set; }
        internal float ResponseRemaining(float now) => Mathf.Max(0f, responseEnds - now);

        internal void ShareSight(Vector3 position)
        {
            if (Time.time < shareAt) return;
            shareAt = Time.time + .6f;
            dispatch.Share(new InterceptorEvidence(InterceptorObservationSource.DirectVisual, position,
                Time.time, Time.time + 5f, 1.25f, .95f, UnitId));
        }

        internal void CopyTasks(List<InterceptorTask> destination, float now)
        {
            foreach (var task in tasks)
                if (task.Valid(now) && task.Graph == Graph.Version) destination.Add(task);
        }

        private void OfferTask(InterceptorAction action, Vector3 point, float score, float eta, float uncertainty, string reason)
        {
            float now = Time.time;
            float normalized = utility.Score(score, TravelCost(eta), uncertainty);
            var task = new InterceptorTask(UnitId, point, normalized, eta, Mathf.Min(responseEnds, now + .85f), Graph.Version, action);
            if (!task.Valid(now)) return;
            if (tasks.Count == 6) return;
            tasks.Add(task);

            if (!dispatch.isActiveAndEnabled)
            {
                utility.Offer(action, point, score, TravelCost(eta), uncertainty, reason);
                return;
            }
            var coordinator = dispatch.Coordinator;
            if (coordinator.Conflicts(UnitId, point, now)) return;
            var reservation = coordinator.Reservations[UnitId - 1];
            if (reservation.Active(now) && reservation.Task.Matches(task)) score += .18f;
            utility.Offer(action, point, score, TravelCost(eta), uncertainty, reason);
        }

        private void ReceiveShared(float now, Func<Vector3, Vector3, Vector3[]> route)
        {
            if (!dispatch.isActiveAndEnabled) return;
            var records = dispatch.Knowledge.Records;
            var latest = default(InterceptorSharedKnowledge.Record);
            foreach (var record in records)
            {
                if (record.Id <= received) continue;
                var evidence = record.Evidence;
                if (!evidence.Fresh(now) || evidence.Sender == UnitId &&
                    evidence.Source != InterceptorObservationSource.ScoutReport || !Allows(evidence.Position)) continue;
                if (evidence.Source == InterceptorObservationSource.BlockedRoute) continue;
                if (evidence.Source == InterceptorObservationSource.InspectedRegion)
                {
                    if (evidence.Graph < 0 || evidence.Graph == Graph.Version) belief.ApplyInspection(evidence);
                }
                else if (latest.Id == 0 || evidence.Strength(now) > latest.Evidence.Strength(now)) latest = record;
            }
            if (records.Count > 0) received = Mathf.Max(received, records[records.Count - 1].Id);
            if (latest.Id != 0) belief.Fuse(latest.Evidence, now, Project, route);
        }

        private void ReceiveLearning(float now)
        {
            if (!dispatch.isActiveAndEnabled) return;
            foreach (var record in dispatch.Knowledge.Records)
            {
                if (record.Id <= learnedThrough) continue;
                learnedThrough = record.Id;
                var evidence = record.Evidence;
                if (evidence.Sender == UnitId || !evidence.Fresh(now) || !Allows(evidence.Position)) continue;
                if (evidence.Source == InterceptorObservationSource.DirectVisual)
                    Learning.OfferEvidence(now, evidence);
                if (evidence.Source == InterceptorObservationSource.BlockedRoute && evidence.Graph == Graph.Version)
                    Learning.Memory.Add(InterceptorOutcome.PhysicalObstruction, evidence.Position, evidence.Time,
                        evidence.Graph, evidence.Route, evidence.Sender, lifetime: evidence.Expires - evidence.Time);
            }
        }

        internal void ConfigureLearning(InterceptorLearningSettings tuning) =>
            Learning = new InterceptorLearning(tuning) { Unit = UnitId };

        internal InterceptorAction Action => utility.Current.Action;
        internal InterceptorUtilityDecision Utility => utility;
        internal InterceptorBeliefState Belief => belief;
        internal float SearchRemaining(float now) => Mathf.Max(0f, searchEnds - now);
        internal int TacticalRouteQueries { get; private set; }

        internal void Configure(InterceptorDecisionSettings tuning)
        {
            settings = tuning;
            belief = new InterceptorBeliefState(tuning.beliefRegions);
            utility = new InterceptorUtilityDecision(tuning);
        }

        internal Phase State { get; private set; }
        internal Vector3 LastReport => lastKnown;
        internal bool IsResponding => State is Phase.Alerted or Phase.Intercept or Phase.Attack or Phase.Reposition or Phase.Search;
        internal float LeadHorizon { get; }
        internal float VisionDistance { get; }
        internal WaypointGraph Graph { get; private set; }
        internal Vector3[] Posts { get; }
        internal IReadOnlyList<Vector3> RoadJunctions => roads;

        internal OutdoorInterceptorResponse(OutdoorScoutEncounter outdoors, OutdoorInterceptorDispatch dispatch,
            NavMeshQueryFilter filter, Vector3[] posts, List<Vector3> roadPosts, float pursuitTime,
            float pursuitDistance, float leadHorizon, float visionDistance)
        {
            this.outdoors = outdoors;
            this.filter = filter;
            this.dispatch = dispatch;
            station = posts[0];
            this.pursuitTime = pursuitTime;
            this.pursuitDistance = pursuitDistance;
            LeadHorizon = leadHorizon;
            VisionDistance = visionDistance;
            Posts = posts;
            foreach (Vector3 point in roadPosts)
                if (Allows(point)) roads.Add(point);
        }

        internal void BuildGraph(Transform parent)
        {
            Transform root = Group("Response Navigation", parent);
            Graph = root.gameObject.AddComponent<WaypointGraph>();
            var nodes = new Transform[Posts.Length];
            var edges = new GraphEdge[Posts.Length];
            for (int i = 0; i < Posts.Length; i++)
            {
                nodes[i] = Group($"Patrol post {i + 1}", root);
                nodes[i].position = Posts[i];
                edges[i] = new GraphEdge(i, (i + 1) % Posts.Length, -1);
            }
            Graph.Configure(nodes, edges);
            Graph.SetConnectionValidator(CanConnect);
        }

        private static float DistanceSquared(Vector3 a, Vector3 b) => OutdoorScoutEncounter.FlatDistanceSquared(a, b);
        private static Vector3 Flat(Vector3 point) => Vector3.ProjectOnPlane(point, Vector3.up);

        private bool Allows(Vector3 point)
            => outdoors.AllowsTarget(point) && DistanceSquared(station, point) <= pursuitDistance * pursuitDistance;

        private bool CanStart => State == Phase.Patrol && Time.time >= nextResponseAt;
        internal bool CanSee(Vector3 playerPosition) => (CanStart || IsResponding) && Allows(playerPosition);

        internal bool TryRespondToSight(InterceptorBrain unit) => CanStart && dispatch.TryReserve(unit);

        private bool CanConnect(Vector3 start, Vector3 end)
        {
            if (!Allows(start) || !Allows(end) ||
                !NavMesh.CalculatePath(start, end, filter, connection) || connection.status != NavMeshPathStatus.PathComplete)
                return false;
            Vector3 from = start;
            foreach (Vector3 corner in connection.corners)
            {
                if (!Allows(corner)) return false;
                int steps = Mathf.CeilToInt(Vector3.Distance(from, corner) / 2f);
                for (int i = 1; i < steps; i++)
                    if (!Allows(Vector3.Lerp(from, corner, (float)i / steps))) return false;
                from = corner;
            }
            return true;
        }

        internal bool TryPlanDispatch(Vector3 origin, Vector3 report, ControlTacticalRouteValidator routes, float speed,
            out Vector3 destination, out float distance, bool avoidReservations = false)
        {
            destination = default;
            distance = float.PositiveInfinity;
            if (!CanStart || !Allows(report)) return false;
            if (!FindStandoff(origin, report, routes, out destination, out distance, avoidReservations)) return false;
            return distance / Mathf.Max(1f, speed) + 2f < pursuitTime;
        }

        internal void Dispatch(Vector3 report, Vector3 destination, float distance, float speed, float reaction,
            InterceptorEvidence? evidence)
        {
            float now = Time.time;
            State = Phase.Alerted;
            belief.Clear();
            tasks.Clear();
            pendingEvidence = evidence;
            soundEngagement = evidence?.Source == InterceptorObservationSource.ApproximateSound;
            goal = destination;
            lastKnown = report;
            alertEnds = now + reaction;
            responseEnds = now + pursuitTime;
            reportExpires = now + Mathf.Min(pursuitTime, Mathf.Max(7f, distance / Mathf.Max(1f, speed) + 4f));
            if (evidence.HasValue) reportExpires = Mathf.Min(reportExpires, evidence.Value.Expires);
            burstReadyAt = now + Mathf.Max(.6f, reaction);
            planAt = searchEnds = 0f;
            fired = 0;
            burstCount = 0;
            seen = relocationPlanned = sightLastTick = false;
            observedBlock = routedTo = null;
            activeRoute = 0;
        }

        internal bool TryRespondToSound(InterceptorBrain brain, DroneHearingSensor hearing,
            ControlTacticalRouteValidator routes, float investigationSpeed)
        {
            if (!CanStart || !TrySound(hearing, Time.time, out var evidence) ||
                evidence.Time <= lastStarterSound || evidence.Time <= belief.LatestVisual) return false;
            // Do not repeatedly try the same observation on every decision tick.
            lastStarterSound = evidence.Time;
            dispatch.Share(new InterceptorEvidence(evidence.Source, evidence.Position, evidence.Time,
                evidence.Expires, evidence.Radius, evidence.Reliability, UnitId));
            Vector3 origin = brain.transform.position;
            Vector3? post = Project(evidence.Position);
            if (!post.HasValue || dispatch.isActiveAndEnabled &&
                dispatch.Coordinator.Conflicts(UnitId, post.Value, Time.time)) return false;
            var route = routes.Route(origin, post.Value);
            if (!InterceptorPredictionModel.Complete(route, origin, post.Value)) return false;
            float length = InterceptorPolicy.Length(route);
            float seconds = length / Mathf.Max(1f, investigationSpeed);
            float soundValue = .58f + .26f * evidence.Strength(Time.time) -
                settings.travelPenalty * TravelCost(seconds) -
                settings.uncertaintyPenalty * Mathf.Clamp01(evidence.Radius / 10f);
            if (soundValue <= .47f || !dispatch.TryReserve(brain)) return false;
            Dispatch(evidence.Position, post.Value, length, investigationSpeed, .25f, evidence);
            return true;
        }

        internal DecisionOption Decide(InterceptorBrain brain, InterceptorPolicy prediction, ControlTacticalRouteValidator routes,
            bool visible, Vector3 playerPosition, float speed, Vector3 ownVelocity, float acceleration, bool shotReady)
        {
            Vector3 position = brain.transform.position;
            float now = Time.time;
            if (State == Phase.Destroyed) return Option("Destroyed", position);
            if (State == Phase.Return && DistanceSquared(position, station) < 1.45f * 1.45f)
            {
                State = Phase.Patrol;
                patrolPost = 1;
            }
            if (State == Phase.Patrol)
            {
                if (now >= planAt)
                {
                    Learning.Tick(now);
                    planAt = now + 1f;
                }
                if (DistanceSquared(position, Posts[patrolPost]) <= 1.45f * 1.45f)
                    patrolPost = (patrolPost + 1) % Posts.Length;
                var post = Posts[patrolPost];
                utility.Begin();
                utility.Offer(InterceptorAction.Patrol, post, .55f, 0f, 0f, "Patrol has no response evidence");
                utility.Select(now);
                return Option($"Patrol post {patrolPost + 1}", post);
            }

            if (State == Phase.Return || now >= responseEnds || (!seen && !visible && now >= reportExpires))
            {
                if (State != Phase.Return && !visible)
                    Learning.Memory.Add(InterceptorOutcome.StaleObservation, lastKnown, now, Graph.Version, sourceUnit: UnitId);
                return Return(now, position, routes);
            }

            bool sightChanged = visible != sightLastTick;
            sightLastTick = visible;
            if (visible)
            {
                if (seen && sightChanged)
                    Learning.Memory.Add(InterceptorOutcome.SuccessfulReacquisition, playerPosition, now, Graph.Version, sourceUnit: UnitId);
                lastKnown = playerPosition;
                seen = true;
                searchEnds = 0f;
            }
            else if (searchEnds > 0f && now >= searchEnds)
                return Return(now, position, routes);

            if (now < alertEnds)
            {
                utility.Begin();
                utility.Offer(soundEngagement ? InterceptorAction.InvestigateSound : InterceptorAction.PursueLastKnown,
                    goal, .65f, 0f, .25f, visible ? "Reaction delay after confirming sight" : "Accepted report approach");
                utility.Select(now);
                return Option("Alerted and moving to reported area", goal);
            }

            bool burstRecovery = State == Phase.Reposition && now < burstReadyAt;
            bool refresh = sightChanged || lastGraphVersion != Graph.Version || now >= planAt ||
                State == Phase.Reposition && !relocationPlanned;
            if (refresh)
            {
                ObserveNavigation(brain.DiagnosticMotor, now);
                lastGraphVersion = Graph.Version;
                planAt = now + Mathf.Clamp(settings.decisionInterval, .25f, .8f);
                RefreshActions(brain, prediction, routes, visible, playerPosition, speed, ownVelocity, acceleration,
                    burstRecovery, position, now);
            }

            // Combat eligibility refreshes with the shared sensor, independently of expensive path planning.
            utility.Reject(InterceptorAction.Attack, "Requires confirmed sight, range, and a ready burst");
            float distance = DistanceSquared(position, playerPosition);
            float attackDistance = State == Phase.Attack ? AttackDistance : AttackDistance - .55f;
            bool hasAhead = prediction.SelectedCandidate != null;
            if (visible && shotReady && now >= burstReadyAt && distance <= attackDistance * attackDistance &&
                (!hasAhead || distance <= 5.5f * 5.5f || fired > 0))
                utility.Offer(InterceptorAction.Attack, position, 1f, 0f, 0f, "Confirmed sight and shared combat gates");

            var choice = utility.Select(now, visible && sightChanged);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int selected = -1;
                for (int i = 0; i < tasks.Count; i++)
                    if (tasks[i].Action == choice.Action && InterceptorTask.Conflicts(tasks[i].Destination, choice.Destination, .1f))
                    {
                        selected = i;
                        break;
                    }
                if (selected < 0 || dispatch.Claim(tasks[selected])) break;
                Learning.Memory.Add(InterceptorOutcome.AssignmentConflict, choice.Destination, now, Graph.Version, sourceUnit: UnitId);
                utility.Reject(choice.Action, "Destination reserved by the squad");
                choice = utility.Select(now, visible && sightChanged);
            }
            if (refresh)
            {
                routedTo = choice.Destination;
                activeRoute = 0;
                if (choice.Action == InterceptorAction.Intercept)
                {
                    foreach (var candidate in prediction.Candidates)
                    {
                        if (!InterceptorTask.Conflicts(candidate.Position, choice.Destination, .1f)) continue;
                        activeRoute = InterceptorTacticalMemory.RouteSignature(candidate.DroneRoute);
                        if (visible) Learning.Capture(candidate, Graph.Version, now, prediction.LastObservedAt,
                            prediction.Settings.arrivalSlack);
                        break;
                    }
                }
            }
            if (choice.Action == InterceptorAction.Attack)
            {
                tasks.Clear();
                dispatch.Release(UnitId);
            }
            if (choice.Action == InterceptorAction.ReturnToPatrol) return Return(now, position, routes);
            if (!visible && fired > 0)
            {
                fired = 0;
                burstReadyAt = Mathf.Max(burstReadyAt, now + 2.2f);
            }

            State = choice.Action switch
            {
                InterceptorAction.Attack => Phase.Attack,
                InterceptorAction.Reposition => Phase.Reposition,
                InterceptorAction.InvestigateSound or InterceptorAction.SearchBeliefRegion => Phase.Search,
                _ => Phase.Intercept
            };
            string label = choice.Action switch
            {
                InterceptorAction.Intercept => visible ? "Intercept ahead on road" : "Continue confirmed interception",
                InterceptorAction.ObserveTarget or InterceptorAction.PursueLastKnown => visible ?
                    "Pursue last known area" : "Pursue last sighting",
                InterceptorAction.InvestigateSound => "Investigate approximate player sound",
                InterceptorAction.SearchBeliefRegion => "Search probable region",
                InterceptorAction.Reposition => "Reposition between response bursts",
                _ => "Face player and fire response burst"
            };
            return Option(label, choice.Destination);
        }

        private void ObserveNavigation(DroneMotor motor, float now)
        {
            if (motor.MovementStatus != "Blocked" || motor.FinalDestination is not Vector3 destination)
            {
                observedBlock = null;
                return;
            }
            if (observedBlock is Vector3 previous && InterceptorTask.Conflicts(previous, destination, 1f)) return;
            observedBlock = destination;
            var outcome = InterceptorTacticalMemory.Classify(motor.BlockedReason);
            int signature = routedTo is Vector3 point && InterceptorTask.Conflicts(point, destination, 1f) ? activeRoute : 0;
            int version = motor.PlannedGraphVersion;
            if (version != Graph.Version) return;
            Learning.Memory.Add(outcome, destination, now, version, signature, UnitId);
            if (!Learning.Enabled) return;
            dispatch.Release(UnitId);
            // Only broadcast a verified, identifiable physical obstruction. Other units still validate their own paths.
            if (outcome == InterceptorOutcome.PhysicalObstruction && signature != 0 &&
                motor.RouteBlocker is Collider obstacle && obstacle != null &&
                obstacle is not CharacterController && obstacle.GetComponentInParent<DroneBody>() == null)
                dispatch.Share(new InterceptorEvidence(InterceptorObservationSource.BlockedRoute, destination,
                    now, now + 3f, 1f, 1f, UnitId, version, signature));
        }

        private bool RouteCooling(Vector3 point, IReadOnlyList<Vector3> path, float now, float observedAt) =>
            Learning.Enabled && Learning.Memory.Cooling(point, Graph.Version,
                InterceptorTacticalMemory.RouteSignature(path), now, 3f, observedAt);

        private void RefreshActions(InterceptorBrain brain, InterceptorPolicy prediction, ControlTacticalRouteValidator routes,
            bool visible, Vector3 playerPosition, float speed, Vector3 ownVelocity, float acceleration,
            bool burstRecovery, Vector3 position, float now)
        {
            using var sample = PlanningMarker.Auto();
            utility.Begin();
            tasks.Clear();
            routes.BeginDecision();
            try
            {
                ReceiveLearning(now);
                Func<Vector3, Vector3, Vector3[]> playerRoute = routes.PlayerRoute;
                DecisionOption proposed = prediction.StepWithMotion(now, visible, playerPosition, position, station,
                    speed, ownVelocity, acceleration, Graph.Version, roads, routes.Route, playerRoute);
                if (visible && Project(playerPosition) is Vector3 sight)
                {
                    var evidence = new InterceptorEvidence(InterceptorObservationSource.DirectVisual, sight,
                        prediction.LastObservedAt, prediction.LastObservedAt + prediction.Settings.observationExpiry, .65f, 1f);
                    belief.Observe(evidence, prediction.Hypotheses);
                    pendingEvidence = null;
                }
                if (!visible)
                {
                    belief.Propagate(now, prediction.Settings.maxPlayerSpeed, prediction.Settings.uncertaintyGrowth,
                        prediction.ForecastHypothesis, Project, playerRoute);
                    if (pendingEvidence.HasValue)
                    {
                        belief.Fuse(pendingEvidence.Value, now, Project, playerRoute);
                        pendingEvidence = null;
                    }
                    if (TrySound(brain.Hearing, now, out var sound) && belief.Fuse(sound, now, Project, playerRoute))
                        dispatch.Share(new InterceptorEvidence(sound.Source, sound.Position, sound.Time,
                            sound.Expires, sound.Radius, sound.Reliability, UnitId));
                    ReceiveShared(now, playerRoute);
                    InspectArrivedRegion(brain, position);
                }

                if (prediction.State == InterceptorState.InterceptAhead)
                {
                    var preferred = prediction.SelectedCandidate;
                    void OfferInterception(InterceptorCandidate interception, float preference)
                    {
                        float margin = Mathf.Clamp01(.5f + (interception.PlayerArrival - interception.DroneArrival) / 4f);
                        float value = .74f + .12f * interception.Confidence + .10f * margin + preference +
                            Learning.UtilityBias(interception.Hypothesis, now);
                        OfferTask(InterceptorAction.Intercept, interception.Position, value,
                            interception.DroneArrival, prediction.MotionUncertainty(now) / 10f, "Validated path and feasible ETA");
                    }

                    if (preferred != null) OfferInterception(preferred, .045f);
                    if (dispatch.isActiveAndEnabled)
                        foreach (var interception in prediction.Candidates)
                            if (interception != preferred) OfferInterception(interception, 0f);
                }

                if (visible)
                {
                    utility.Offer(InterceptorAction.ObserveTarget, position, .47f + .12f * (1f - prediction.Confidence),
                        0f, 0f, "Maintain confirmed sight to improve prediction");
                    if (prediction.SelectedCandidate == null &&
                        FindStandoff(position, playerPosition, routes, out var approach, out float length) &&
                        !RouteCooling(approach, routes.Route(position, approach), now, prediction.LastObservedAt))
                        utility.Offer(InterceptorAction.PursueLastKnown, approach, .75f,
                            TravelCost(length / Mathf.Max(1f, speed)), 0f, "Clear approach from a direct sighting");
                    if (burstRecovery)
                    {
                        if (!relocationPlanned)
                        {
                            relocationPlanned = true;
                            goal = FindBurstPosition(position, playerPosition, routes);
                        }
                        if (DistanceSquared(position, goal) > 1f &&
                            InterceptorPredictionModel.Complete(routes.Route(position, goal), position, goal))
                            utility.Offer(InterceptorAction.Reposition, goal, .97f,
                                TravelCost(Vector3.Distance(position, goal) / Mathf.Max(1f, speed)), 0f, "Clear flank during burst recovery");
                    }
                    return;
                }

                if (seen && prediction.State == InterceptorState.PursueLastKnown &&
                    InterceptorPredictionModel.Complete(routes.Route(position, proposed.Destination), position, proposed.Destination) &&
                    !RouteCooling(proposed.Destination, routes.Route(position, proposed.Destination), now, prediction.LastObservedAt))
                    utility.Offer(InterceptorAction.PursueLastKnown, proposed.Destination,
                        .72f - .06f * Mathf.Min(4f, prediction.ObservationAge(now)),
                        TravelCost(Vector3.Distance(position, proposed.Destination) / Mathf.Max(1f, speed)),
                        prediction.MotionUncertainty(now) / 10f, "Recent genuine sighting");

                if (!seen && !soundEngagement && DistanceSquared(position, goal) > 2.25f &&
                    InterceptorPredictionModel.Complete(routes.Route(position, goal), position, goal) &&
                    !RouteCooling(goal, routes.Route(position, goal), now, prediction.LastObservedAt))
                    utility.Offer(InterceptorAction.PursueLastKnown, goal, .81f,
                        TravelCost(Vector3.Distance(position, goal) / Mathf.Max(1f, speed)), .2f, "Reach the original Scout report");

                OfferSearches(position, now, speed, routes);
                if (Learning.Enabled && seen && prediction.ObservationAge(now) < Mathf.Min(1.2f, prediction.Settings.pursueSeconds))
                    utility.Offer(InterceptorAction.ObserveTarget, position, .31f, 0f, 0f,
                        "Hold briefly after a recent sighting if no search path is clear");
                utility.Offer(InterceptorAction.ReturnToPatrol, station, .23f, 0f, 0f, "No useful evidence or search route");
            }
            finally
            {
                var lease = dispatch.Coordinator.Reservations[UnitId - 1];
                if (lease.Id != 0 && !lease.Task.Activates)
                {
                    bool stillValid = false;
                    foreach (var task in tasks)
                        if (task.Matches(lease.Task)) { stillValid = true; break; }
                    if (!stillValid) dispatch.Release(UnitId);
                }
                TacticalRouteQueries = routes.RouteQueries;
                routes.EndDecision();
            }
        }

        private void OfferSearches(Vector3 position, float now, float speed, ControlTacticalRouteValidator routes)
        {
            searchOrder.Clear();
            for (int i = 0; i < belief.Regions.Count; i++)
            {
                var region = belief.Regions[i];
                if (region.Inspection is InterceptorInspection.Inspected or InterceptorInspection.Occluded ||
                    region.Probability < .025f || !Allows(region.Position)) continue;
                float evidence = region.Evidence.Strength(now);
                float binaryEntropy = -region.Probability * Mathf.Log(region.Probability);
                float rangeProxy = 1f / (1f + region.Radius * .18f);
                float value = .34f + .28f * region.Probability + .27f * evidence +
                    settings.informationWeight * binaryEntropy * rangeProxy;
                if (Learning.Enabled) value -= Learning.Memory.SearchPenalty(region.Position, Graph.Version, now, Learning.Decay);
                searchOrder.Add((i, value));
            }
            searchOrder.Sort((a, b) =>
            {
                int rank = b.Rank.CompareTo(a.Rank);
                return rank != 0 ? rank : a.Index.CompareTo(b.Index);
            });

            for (int i = 0; i < Mathf.Min(6, searchOrder.Count); i++)
            {
                var item = searchOrder[i];
                var region = belief.Regions[item.Index];
                var path = routes.Route(position, region.Position);
                bool reachable = InterceptorPredictionModel.Complete(path, position, region.Position);
                belief.SetReachable(item.Index, reachable);
                if (!reachable || RouteCooling(region.Position, path, now, belief.LatestVisual)) continue;
                var action = region.Evidence.Source == InterceptorObservationSource.ApproximateSound
                    ? InterceptorAction.InvestigateSound : InterceptorAction.SearchBeliefRegion;
                OfferTask(action, region.Position, item.Rank, InterceptorPolicy.Length(path) / Mathf.Max(1f, speed),
                    region.Radius / 10f, action == InterceptorAction.InvestigateSound ? "Reachable approximate sound" : "Reachable probable region");
            }
            if (searchEnds <= 0f && (seen || soundEngagement || DistanceSquared(position, goal) <= 2.25f))
                searchEnds = now + Mathf.Clamp(settings.searchSeconds, 2f, 7f);
        }

        private void InspectArrivedRegion(InterceptorBrain brain, Vector3 position)
        {
            if (Action is not (InterceptorAction.InvestigateSound or InterceptorAction.SearchBeliefRegion) ||
                DistanceSquared(position, utility.Current.Destination) > 1.45f * 1.45f) return;
            int count = belief.Regions.Count;
            for (int i = 0; i < count; i++)
            {
                var region = belief.Regions[i];
                if (region.Inspection != InterceptorInspection.Unexamined ||
                    DistanceSquared(region.Position, utility.Current.Destination) > .6f * .6f) continue;
                bool covered = HasCoverage(brain, region);
                belief.Inspect(i, covered);
                Learning.Memory.Add(covered ? InterceptorOutcome.FailedSearch : InterceptorOutcome.InsufficientInspection,
                    region.Position, Time.time, Graph.Version, sourceUnit: UnitId);
                dispatch.Release(UnitId);
                if (covered)
                    dispatch.Share(new InterceptorEvidence(InterceptorObservationSource.InspectedRegion, region.Position,
                        Time.time, Time.time + 4f, region.Radius, .8f, UnitId, Graph.Version, 0));
                break;
            }
        }

        private bool HasCoverage(InterceptorBrain brain, InterceptorBeliefState.Region region)
        {
            if (region.Radius > 6f) return false;
            Vector3 eye = brain.InspectionOrigin;
            int nearby = Physics.OverlapSphereNonAlloc(eye, .025f, sightOverlaps, ~0, QueryTriggerInteraction.Ignore);
            if (nearby == sightOverlaps.Length) return false;
            for (int i = 0; i < nearby; i++)
                if (!sightOverlaps[i].transform.IsChildOf(brain.transform)) return false;

            foreach (Vector3 offset in InspectionOffsets)
            {
                Vector3 probe = region.Position + offset * (region.Radius * .75f);
                if (!Allows(probe)) return false;
                Vector3 delta = probe + Vector3.up * 1.6f - eye;
                if (delta.magnitude > VisionDistance || !brain.InspectionArc(delta)) return false;
                if (delta.sqrMagnitude < .01f) continue;
                int hits = Physics.RaycastNonAlloc(eye, delta.normalized, sightHits, delta.magnitude,
                    ~0, QueryTriggerInteraction.Ignore);
                if (hits == sightHits.Length) return false;
                for (int i = 0; i < hits; i++)
                    if (!sightHits[i].collider.transform.IsChildOf(brain.transform)) return false;
            }
            return true;
        }

        private Vector3? Project(Vector3 point) =>
            Allows(point) && NavMesh.SamplePosition(point, out var hit, .65f, filter) && Allows(hit.position) ? hit.position : null;

        private static bool TrySound(DroneHearingSensor hearing, float now, out InterceptorEvidence evidence)
        {
            evidence = default;
            if (hearing == null || !hearing.TryGetLatestPlayerSound(out var sound)) return false;
            evidence = new InterceptorEvidence(InterceptorObservationSource.ApproximateSound, sound.Position, sound.EmittedAt,
                sound.ExpiresAt, Mathf.Max(1.8f, sound.UncertaintyRadius), .28f + .48f * sound.Strength);
            return evidence.Fresh(now);
        }

        private static float TravelCost(float seconds) => Mathf.Clamp01(seconds / 8f);

        private Vector3 FindBurstPosition(Vector3 origin, Vector3 target, ControlTacticalRouteValidator routes)
        {
            Vector3 away = Flat(lastBurstPost - target).normalized;
            if (away.sqrMagnitude < .01f) return origin;
            float side = burstCount % 2 == 0 ? 1f : -1f;
            foreach (float angle in new[] { 58f * side, -58f * side, 33f * side, -33f * side })
            {
                Vector3 probe = target + Quaternion.Euler(0f, angle, 0f) * away * 6.4f;
                probe.y = station.y;
                if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, 1f, filter) || !Allows(hit.position) ||
                    DistanceSquared(lastBurstPost, hit.position) < 2.5f * 2.5f) continue;
                if (InterceptorPolicy.ValidRoute(routes.Route(origin, hit.position))) return hit.position;
            }
            return origin;
        }

        private bool FindStandoff(Vector3 origin, Vector3 location, ControlTacticalRouteValidator validator,
            out Vector3 destination, out float distance, bool avoidReservations = false)
        {
            destination = default;
            distance = float.PositiveInfinity;
            Vector3 toward = Flat(origin - location).normalized;
            if (toward.sqrMagnitude < .01f) toward = Vector3.forward;
            foreach (float radius in ApproachRadii)
            foreach (float angle in ApproachAngles)
            {
                Vector3 candidate = location + Quaternion.Euler(0f, angle, 0f) * toward * radius;
                candidate.y = station.y;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1f, filter) || !Allows(hit.position)) continue;
                if (avoidReservations && dispatch.Coordinator.Conflicts(UnitId, hit.position, Time.time)) continue;
                Vector3[] route = validator.Route(origin, hit.position);
                if (!InterceptorPolicy.ValidRoute(route)) continue;
                float length = InterceptorPolicy.Length(route);
                if (length >= distance) continue;
                distance = length;
                destination = hit.position;
            }
            return float.IsFinite(distance);
        }

        internal bool AuthorizeShot(Vector3 position, Vector3 forward, Vector3 playerPosition)
        {
            if (State != Phase.Attack || Time.time < burstReadyAt ||
                Vector3.Dot(forward, Flat(playerPosition - position).normalized) < .96f) return false;
            if (++fired < 2) return true;
            fired = 0;
            burstReadyAt = Time.time + 2.2f;
            lastBurstPost = position;
            burstCount++;
            relocationPlanned = false;
            State = Phase.Reposition;
            return true;
        }

        internal void Defeat()
        {
            State = Phase.Destroyed;
            Learning.Tick(Time.time, close: true);
            tasks.Clear();
            dispatch.Release(UnitId);
        }

        private DecisionOption Return(float now, Vector3 position, ControlTacticalRouteValidator routes)
        {
            // Recheck the route at the next decision interval if geometry blocks the path home.
            if (State != Phase.Return || now >= planAt)
            {
                if (State != Phase.Return)
                {
                    nextResponseAt = now + dispatch.Cooldown;
                    Learning.Tick(now, close: true);
                }
                else Learning.Tick(now);
                State = Phase.Return;
                tasks.Clear();
                dispatch.Release(UnitId);
                planAt = now + Mathf.Clamp(settings.decisionInterval, .25f, .8f);
                belief.Clear();
                searchEnds = 0f;
                utility.Begin();
                bool valid = routes != null && InterceptorPredictionModel.Complete(routes.Route(position, station), position, station);
                utility.Offer(InterceptorAction.ReturnToPatrol, valid ? station : position, 1f, 0f, 0f,
                    valid ? "Response expired or no useful search" : "Return path blocked, holding and retrying");
                utility.Select(now);
            }
            return Option(utility.Current.Reason, utility.Current.Destination);
        }

        private static DecisionOption Option(string label, Vector3 point) => new DecisionOption(label, point, 50f);
    }
}
