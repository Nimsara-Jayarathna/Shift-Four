using UnityEngine;
using static ShiftFour.PrototypeGeometry;
using Site = ShiftFour.PrototypePlayerFeedback.Site;

namespace ShiftFour
{
    internal sealed partial class HallInstrumentation
    {
        private static readonly string[] Subsystems = { "SURVEILLANCE SUBSYSTEM", "SECURITY NETWORK", "ACCESS CONTROL" };
        private static readonly string[] Sectors = { "CHECKPOINT", "SERVER", "CONTROL", "STORAGE", "EXTRACTION" };
        private Transform projection;
        private Font commandFont;
        private Material commandLettering;
        private OpenWorldMissionManager commandFeed;
        private readonly Renderer[] sectorSymbols = new Renderer[5], stationMeters = new Renderer[3];
        private readonly LockdownSecuritySwitch[] stationSwitches = new LockdownSecuritySwitch[3];
        private readonly TextMesh[] sectorLabels = new TextMesh[5];
        private readonly TextMesh[] subsystemReadouts = new TextMesh[3];
        private readonly float[] pulseUntil = new float[3];
        private TextMesh securitySummary, verificationCaption;
        private Renderer verificationMeter;
        private int lastSectorMask = -1, lastSwitchMask = -1;
        private float lastVerification = -1;
        private bool mapBuilt;

        private void Control(Transform props, PrototypeMaterials m, Font font)
        {
            commandFont = font;
            commandLettering = m.Lettering;
            lockdown = props.GetComponentInChildren<ControlLockdownMission>(true);
            var panel = lockdown.transform;
            var steel = new InspectionSteel(cube);
            // Flush perimeter service bays and overhead portal frames share four material batches.
            for (int bay = 0; bay < 5; bay++)
            {
                float z = -9f + bay * 4.5f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * 12.43f;
                    steel.Plate(m.Wall, new(x, 3.2f, z), new(.18f, 5.9f, 4.32f));
                    steel.Plate(m.Concrete, new(x - side * .13f, 3.4f, z - 2f), new(.22f, 6.6f, .24f));
                    steel.Beam(m.Metal, new(x, 6.4f, z), new(side * 9.9f, 8.25f, z), .32f, .52f);
                    for (int louvre = 0; louvre < 5; louvre++)
                        steel.Plate(m.Metal, new(x - side * .14f, 4.48f + louvre * .14f, z), new(.14f, .07f, 2.94f));
                    steel.Plate(m.Metal, new(x - side * .18f, 2.35f, z), new(.10f, 2.6f, 3.65f));
                    var service = Group("Service bay", transform, new(x - side * .245f, 2.35f, z));
                    service.localRotation = Quaternion.Euler(0, side * 90, 0);
                    Faceplate("Access covers", service, Vector3.zero, 3.52f, 2.48f, Pattern.Lining, Cyan);
                    steel.Plate(m.Metal, new(side * 9.9f, 8.27f, z), new(.64f, .33f, 4.5f));
                }
                steel.Plate(m.Metal, new(0, 8.47f, z), new(20f, .24f, .38f));
                steel.Plate(m.Metal, new(0, 7.64f, z), new(19.6f, .12f, .18f));
                for (int web = -5; web < 5; web++)
                {
                    float x = 1.94f * web;
                    bool rising = (web & 1) == 0;
                    steel.Beam(m.Metal, new(x, rising ? 7.64f : 8.45f, z),
                        new(x + 1.94f, rising ? 8.45f : 7.64f, z), .09f, .16f);
                }
                for (int channel = -1; channel <= 1; channel++)
                    steel.Plate(m.Concrete, new(channel * .21f, 8.68f, z), new(.10f, .12f, 4.5f));
            }
            steel.Plate(m.Metal, new(0, 5.69f, 12.25f), new(16.9f, 4.26f, .45f), cast: true);
            steel.Plate(m.Glass, new(0, 5.69f, 11.99f), new(16.25f, 3.89f, .12f), cast: true);
            steel.Plate(m.Concrete, new(0, 7.85f, 12.2f), new(17.12f, .18f, .58f));
            for (int side = -1; side <= 1; side += 2)
            {
                // Recessed avionics cabinets flank the display wall above the switch stations.
                float x = side * 10.15f;
                steel.Plate(m.Metal, new(x, 4.25f, 12.25f), new(2.9f, 6.1f, .48f), cast: true);
                Faceplate("Avionics access covers", props, new(x, 4.26f, 11.995f), 2.47f, 5.65f, Pattern.Lining, Blue);
                for (int shelf = 0; shelf < 3; shelf++)
                    Faceplate("Diagnostic blades", props, new(x, 2.6f + shelf * 1.32f, 11.98f), 1.88f, .82f, Pattern.Rack, Teal);
                steel.Plate(m.Concrete, new(side * 8.53f, 5.55f, 12.22f), new(.25f, 4.76f, .45f), cast: true);
                steel.Plate(m.Metal, new(side * 2.53f, 1.56f, 10.45f), new(.23f, 1.68f, .52f), cast: true);
                Faceplate("Aisle guidance", props, new(side * 1.76f, .017f, -.7f), 20f, .08f, Pattern.Strip, Teal, false, 90, 90);
            }
            steel.Publish(Group("Command deck metalwork", transform), inspectionMeshes);

            Label("SHIFT FOUR  /  FACILITY OPERATIONS", props, new(0, 7.12f, 11.89f), .23f, font, 13, .72f, m.Lettering);
            Label("03   /   CONTROL HALL", props, new(-5.6f, 6.34f, 11.88f), .15f, font, 4.1f, .33f, m.Lettering);
            Label("DIAGNOSTIC TELEMETRY", props, new(-5.6f, 4.25f, 11.88f), .1f, font, 4, .24f, m.Lettering);
            Faceplate("Telemetry trace", props, new(-5.6f, 5.24f, 11.89f), 4.3f, 1.48f, Pattern.Telemetry, Cyan);
            Label("SECURITY SUBSYSTEM STATUS", props, new(0, 6.34f, 11.88f), .15f, font, 5.1f, .35f, m.Lettering);
            securitySummary = Label("LOCKDOWN DISABLED\n3 / 3 DISABLED", props, new(0, 5.25f, 11.88f), .18f, font, 4.75f, .98f, m.Lettering);
            Label("FACILITY STATUS", props, new(5.47f, 6.34f, 11.88f), .13f, font, 4.3f, .32f, m.Lettering);
            for (int i = 0; i < Sectors.Length; i++)
                sectorLabels[i] = Label(Sectors[i] + "  /  UNAVAILABLE", props, new(5.48f, 5.92f - i * .39f, 11.88f), .12f, font, 4.5f, .29f, m.Lettering);

            Casing("Projection rail", panel, new(0, 2.49f, 0), new(4.5f, .1f, .43f), m.Metal);
            projection = Group("Facility projection", panel, new(0, 3.38f, 1.26f));
            projection.localRotation = Quaternion.Euler(65, 0, 0);
            Casing("Survey glass", projection, new(0, 0, .04f), new(4.06f, 4.06f, .068f), m.Glass);
            Faceplate("Survey grid", projection, Vector3.zero, 4f, 4f, Pattern.Survey, Cyan);
            Label("FACILITY MAP   /   03 CONTROL", projection, new(0, -1.89f, -.027f), .08f, font, 2.65f, .13f, m.Lettering);
            verificationMeter = Instrument("Authorization progress", panel, new(0, 1.105f, -.274f), new(4.2f, .11f, 1), quad);
            Casing("Authorization strip housing", panel, new(0, .78f, -.351f), new(3.15f, .32f, .046f), m.Glass);
            verificationCaption = Label("OVERRIDE VERIFICATION", panel, new(0, .78f, -.381f), .10f, font, 2.85f, .22f, m.Lettering);
            foreach (Transform fixture in props)
            {
                if (fixture.name == "Control console")
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Casing("Console cheek", fixture, new(side * 1.38f, 1.08f, -.04f), new(.26f, .21f, .86f), m.Concrete);
                        Faceplate("Desk diagnostics", fixture, new(side * 1.06f, .58f, -.562f), .74f, .43f, Pattern.Rack, Teal);
                    }
                    Faceplate("Operator identification", fixture, new(0, .58f, -.563f), 1.1f, .46f, Pattern.Telemetry, Blue);
                    Casing("Operator keyboard", fixture, new(0, 1.025f, -.26f), new(1.7f, .05f, .42f), m.Glass);
                    Faceplate("Input matrix", fixture, new(0, 1.056f, -.26f), 1.55f, .34f, Pattern.Rack, Cyan, false, 90);
                    Faceplate("Operator scope", fixture, new(0, 1.45f, .033f), 1.33f, .64f, Pattern.Radar, Teal);
                }
                else if (fixture.name == "Operations monitor")
                {
                    int index = fixture.localPosition.x < -1 ? 0 : fixture.localPosition.x > 1 ? 2 : 1;
                    subsystemReadouts[index] = Label(Subsystems[index] + "  /  DISABLED", fixture,
                        new(0, -.56f, -.157f), .10f, font, 3.05f, .18f, m.Lettering);
                    Faceplate("Tactical scope", fixture, new(-.72f, .09f, -.147f), 1.03f, 1.03f, Pattern.Radar, Teal);
                    Faceplate("Sector telemetry", fixture, new(.65f, .1f, -.147f), 1.28f, .82f, Pattern.Telemetry, Cyan);
                }
                else if (fixture.name == "Emergency warning beacon")
                    Faceplate("Lockdown annunciator", fixture, new(0, 0, -.276f), .56f, .27f, Pattern.Strip, Alarm, true);
                else if (fixture.TryGetComponent<LockdownSecuritySwitch>(out var station))
                {
                    int index = station.Number - 1;
                    stationSwitches[index] = station;
                    Casing("Subsystem fascia", fixture, new(0, 2.16f, 0), new(2.02f, .32f, .34f), m.Metal);
                    Label(Subsystems[index], fixture, new(0, 2.16f, -.177f), .1f, font, 1.84f, .21f, m.Lettering);
                    stationMeters[index] = Instrument("Subsystem signal", fixture, new(0, 1.27f, -.225f), new(1.55f, .085f, 1), quad);
                }
            }
            RefreshCommandDeck();
        }

        private Renderer Instrument(string name, Transform parent, Vector3 at, Vector3 size, Mesh mesh)
            => Surface(name, parent, at, size, mesh, instrument, Quaternion.identity);

        private void SignalInk(Renderer target, Pattern pattern, Color color, float level)
        {
            if (target == null) return;
            block ??= new MaterialPropertyBlock();
            block.Clear();
            block.SetFloat(Mode, (float)pattern);
            block.SetColor(Ink, color);
            block.SetFloat(Level, level);
            target.SetPropertyBlock(block);
        }

        internal void BindCommandMap(FacilityMap map, OpenWorldMissionManager feed)
        {
            commandFeed = feed;
            if (projection == null || map.Ground.size.x <= 0 || map.Ground.size.z <= 0) return;
            if (!mapBuilt)
            {
                mapBuilt = true;
                Rect frame = new(-1.94f, -1.94f, 3.88f, 3.88f);
                foreach (var road in map.Roads)
                {
                    Rect r = map.ProjectBounds(road, frame);
                    var roadFace = Instrument("Road", projection, new(r.center.x, -r.center.y, -.012f), new(r.width, r.height, 1), quad);
                    SignalInk(roadFace, Pattern.Hull, Blue, 1);
                }
                for (int i = 0; i < Sectors.Length; i++)
                {
                    var site = (Site)((int)Site.Checkpoint + i);
                    var marker = map.Marker(site);
                    if (marker == null) continue;
                    Rect r = i < 4 ? map.ProjectBounds(map.Foundations[i], frame) : new Rect();
                    Vector2 point = i < 4 ? r.center : map.Project(marker.position, frame);
                    float height = site == Site.Control ? .22f : .13f;
                    sectorSymbols[i] = Instrument(Sectors[i], projection, new(point.x, -point.y, -.02f - height * .5f),
                        i < 4 ? new Vector3(r.width, r.height, height) : new Vector3(.18f, .11f, height), cube);
                    Label(i == 4 ? "EX" : $"0{i + 1}", projection, new(point.x, -point.y, -.025f - height),
                        .07f, commandFont, .24f, .115f, commandLettering);
                }
            }
            lastSectorMask = -1;
            RefreshCommandDeck();
        }

        private void RefreshCommandDeck()
        {
            if (securitySummary == null) return;
            int switches = 0;
            for (int i = 0; i < stationSwitches.Length; i++)
                if (stationSwitches[i] != null && stationSwitches[i].IsActivated) switches |= 1 << i;
            int panelState = switches | (lockdown.IsComplete ? 8 : 0);
            bool panelChanged = panelState != lastSwitchMask;
            if (panelChanged)
            {
                for (int i = 0; i < stationMeters.Length; i++)
                {
                    bool disabled = (switches & (1 << i)) != 0;
                    subsystemReadouts[i].text = Subsystems[i] + (disabled ? "  /  DISABLED" : "  /  ARMED");
                    if (disabled && (lastSwitchMask & (1 << i)) == 0)
                        pulseUntil[i] = Time.time + .65f;
                    SignalInk(stationMeters[i], Pattern.Strip, disabled ? Teal : Alarm, disabled ? 1 : -1);
                }
                lastSwitchMask = panelState;
                string headline = lockdown.IsComplete ? "LOCKDOWN DISABLED" : switches == 7 ? "OVERRIDE AUTHORIZED" : "LOCKDOWN ACTIVE";
                securitySummary.text = $"{headline}\n{lockdown.ActivatedCount} / {lockdown.TotalSwitches} DISABLED";
            }
            for (int i = 0; i < pulseUntil.Length; i++)
            {
                if (pulseUntil[i] <= 0) continue;
                float pulse = Mathf.Clamp01((pulseUntil[i] - Time.time) / .65f);
                SignalInk(stationMeters[i], Pattern.Strip, Color.Lerp(Teal, Color.white, pulse * .65f), 1);
                if (pulse == 0) pulseUntil[i] = 0;
            }
            float verification = lockdown.IsComplete ? lockdown.Verification : -1;
            if (verification != lastVerification || panelChanged)
            {
                if (panelChanged || verification >= 1 && lastVerification < 1)
                    verificationCaption.text = lockdown.IsComplete
                        ? (verification < 1 ? "VERIFYING OVERRIDE" : "OPERATION RESTORED") : "AUTHORIZATION BUS";
                lastVerification = verification;
                SignalInk(verificationMeter, Pattern.CredentialProgress, lockdown.IsComplete ? Teal : switches == 7 ? Cyan : Alarm,
                    lockdown.IsComplete ? verification : (float)lockdown.ActivatedCount / lockdown.TotalSwitches);
            }
            if (commandFeed == null) return;
            int mask = (commandFeed.CheckpointComplete ? 1 : 0) | (commandFeed.ServerComplete ? 2 : 0) |
                (commandFeed.ControlComplete ? 4 : 0) | (commandFeed.StorageComplete ? 8 : 0) | (commandFeed.IsReady ? 16 : 0);
            if (mask == lastSectorMask) return;
            lastSectorMask = mask;
            for (int i = 0; i < Sectors.Length; i++)
            {
                bool active = (mask & (1 << i)) != 0;
                string status = i == 4 ? (active ? "READY" : "LOCKED") : (active ? "COMPLETE" : "PENDING");
                sectorLabels[i].text = Sectors[i] + "  /  " + status;
                SignalInk(sectorSymbols[i], Pattern.Hull, active ? Teal : i == 2 ? Amber : Cyan, 1);
            }
        }
    }
}
