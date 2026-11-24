using UnityEngine;
using Site = ShiftFour.PrototypePlayerFeedback.Site;
using Status = ShiftFour.PrototypePlayerFeedback.Status;
using Objective = ShiftFour.PrototypePlayerFeedback.Objective;

namespace ShiftFour
{
    internal sealed partial class OutdoorMenuView
    {
        private static readonly Color Ink = new(.023f, .051f, .063f, .93f);
        private static readonly Color Green = new(.45f, .88f, .58f);
        private static readonly string[] Names = { "Supply station", "Checkpoint", "Server", "Control", "Storage", "Extraction" };
        private static readonly string[] Titles = { "Preparation", "Security access", "Network access", "Emergency lockdown", "Power restoration", "Final extraction" };
        private static readonly string[] States = { "LOCKED", "AVAILABLE", "IN PROGRESS", "COMPLETE", "UNAVAILABLE" };
        private static readonly Color[] SiteColors = { new(.73f, .83f, .85f), new(1f, .74f, .32f), new(.45f, .68f, 1f), new(.25f, .84f, .72f), new(1f, .56f, .29f), Green };
        private readonly GUIContent noticeContent = new();
        private Site selectedSite;
        internal void FocusSite(Site site) => selectedSite = site;

        private static string Progress(Objective row) => row.Total == 0 ? string.Empty :
            row.Site == Site.Extraction ? $"Targets   {row.Count} / {row.Total}" : $"{row.Count} / {row.Total}";

        private void DrawObjective(PrototypePlayerFeedback hud)
        {
            Objective row = hud.ReadObjective(hud.CurrentSite);
            int i = (int)row.Site;
            const float x = 24, y = 22, width = 346;
            Fill(new Rect(x, y, width, 100), Ink);
            Fill(new Rect(x, y, 3, 100), SiteColors[i]);
            Text(new Rect(x + 14, y + 6, 160, 18), hud.ObjectiveUpdated ? "OBJECTIVE UPDATED" : "CURRENT OBJECTIVE", 12, Cyan, true);
            Text(new Rect(x + 228, y + 6, 110, 18), "TAB  List   M  Map", 12, Muted, false);
            Text(new Rect(x + 14, y + 27, width - 25, 26), Names[i] + "  /  " + Titles[i], 19, Paper, true);
            lettering.wordWrap = true;
            Text(new Rect(x + 14, y + 53, width - 22, 43), row.Instruction, 15, Paper, false);
            lettering.wordWrap = false;
            if (row.Total > 0)
            {
                Fill(new Rect(x, y + 95, width, 5), Surface);
                Fill(new Rect(x, y + 95, width * row.Count / row.Total, 5), SiteColors[i]);
                Text(new Rect(x + 175, y + 6, 50, 18), $"{row.Count} / {row.Total}", 13, SiteColors[i], true);
            }
            string notice = hud.InteractionNotice;
            if (string.IsNullOrEmpty(notice) || notice == hud.FocusedPrompt) return;
            lettering.wordWrap = true;
            lettering.fontSize = 14;
            noticeContent.text = notice;
            float height = lettering.CalcHeight(noticeContent, width - 28) + 14;
            Fill(new Rect(x, y + 105, width, height), Ink);
            Text(new Rect(x + 14, y + 105, width - 28, height), notice, 14, Amber, false);
            lettering.wordWrap = false;
        }

        private void DrawMissionNotice(PrototypePlayerFeedback hud, float screenWidth)
        {
            var message = hud.ActiveNotice;
            if (message.Title == null) return;
            float previousAlpha = alpha;
            alpha *= hud.NoticeOpacity;
            float x = (screenWidth - 408) * .5f, y = 22 - 5 * (1 - hud.NoticeOpacity);
            float height = message.Next == null ? 57 : 81;
            Color accent = message.Kind switch
            {
                PrototypePlayerFeedback.NoticeKind.Completion => Green,
                PrototypePlayerFeedback.NoticeKind.Warning => Amber,
                _ => Cyan
            };
            Fill(new Rect(x, y, 408, height), Ink);
            Fill(new Rect(x, y, 3, height), accent);
            Text(new Rect(x + 16, y + 5, 384, 25), message.Title, 18, accent, true);
            Text(new Rect(x + 16, y + 29, 384, 21), message.Detail, 15, Paper, false);
            if (message.Next != null)
            {
                bool secured = message.Site == Site.Checkpoint;
                Text(new Rect(x + 16, y + 52, 120, 23), secured ? "NEW OBJECTIVE" : "NEXT", 12, Cyan, true);
                float inset = secured ? 136 : 68;
                Text(new Rect(x + inset, y + 52, 395 - inset, 23), message.Next, 14, Paper, false);
            }
            alpha = previousAlpha;
        }

        private void DrawBriefing(OutdoorMissionMenu menu, float w, float h, bool busy)
        {
            var hud = menu.Feedback;
            bool map = menu.Screen == OutdoorMissionMenu.ScreenState.Map;
            Fill(new Rect(0, 0, w, h), new Color(.014f, .028f, .038f, .98f));
            float x = (w - 1080) * .5f, top = (h - 648) * .5f;
            Text(new Rect(x, top, 700, 50), map ? "Facility map" : "Mission overview", 36, Paper, true);
            var close = new Rect(x + 894, top + 6, 186, 36);
            Fill(close, Surface);
            Text(new Rect(close.x + 14, close.y, 174, 36), map ? "M / ESC    Close map" : "TAB / ESC    Close", 15, Cyan, true);
            if (!busy && GUI.Button(close, GUIContent.none, GUIStyle.none)) menu.Play();
            if (map) DrawMap(hud, x, top + 62, busy);
            else
            {
                for (int i = (int)Site.Supply; i <= (int)Site.Extraction; i++)
                {
                    var row = hud.ReadObjective((Site)i);
                    float y = top + 64 + i * 87;
                    Color accent = row.Status == Status.Complete ? Green : SiteColors[i];
                    Fill(new Rect(x, y, 1080, 78), Surface);
                    Fill(new Rect(x, y, 4, 78), accent);
                    if (row.Site == hud.CurrentSite)
                    {
                        Outline(new Rect(x, y, 1080, 78), Amber);
                        Text(new Rect(x + 560, y + 9, 240, 27), "CURRENT OBJECTIVE", 14, Amber, true);
                    }
                    Text(new Rect(x + 21, y + 9, 345, 32), Names[i] + "  /  " + Titles[i], 20, Paper, true);
                    Text(new Rect(x + 21, y + 43, 790, 28), row.Instruction, 16, Muted, false);
                    Text(new Rect(x + 830, y + 10, 234, 25), States[(int)row.Status], 15, accent, true);
                    string progress = row.Site == Site.Extraction && hud.ExtractionManager != null
                        ? $"Buildings   {hud.ExtractionManager.BuildingMissionsCompleted} / {OpenWorldMissionManager.RequiredMissions}     {Progress(row)}"
                        : Progress(row);
                    Text(new Rect(x + 830, y + 39, 234, 28), progress, 15, Paper, false);
                }
                Text(new Rect(x, top + 590, 1080, 32), "Extraction targets   /   Designated Scout, Guard and Flanker", 16, Muted, false);
            }
        }

        private void DrawMap(PrototypePlayerFeedback hud, float x, float y, bool busy)
        {
            FacilityMap map = hud.Map;
            var frame = new Rect(x + 14, y + 10, 552, 552);
            Fill(new Rect(x, y, 580, 580), Surface);
            Fill(frame, Ink);
            for (int i = 1; i < 8; i++)
            {
                Fill(new Rect(frame.x + i * 69, frame.y, 1, frame.height), Surface);
                Fill(new Rect(frame.x, frame.y + i * 69, frame.width, 1), Surface);
            }
            if (map.Ground.size.x > 0 && map.Ground.size.z > 0)
            {
                Outline(map.ProjectBounds(map.Ground, frame), Muted);
                foreach (Bounds road in map.Roads) Fill(map.ProjectBounds(road, frame), new Color(.22f, .31f, .34f));
                for (int i = 0; i < map.Foundations.Length; i++)
                {
                    if (map.Foundations[i].size.x <= 0) continue;
                    Rect footprint = map.ProjectBounds(map.Foundations[i], frame);
                    Fill(footprint, Color.Lerp(Surface, SiteColors[i + 1], .25f));
                    Outline(footprint, SiteColors[i + 1]);
                }
                if (map.Spawn != null)
                {
                    Vector2 spawn = map.Project(map.Spawn.position, frame);
                    Outline(new Rect(spawn.x - 9, spawn.y - 9, 18, 18), Paper);
                }
                for (int i = 0; i < Names.Length; i++)
                {
                    Site site = (Site)i;
                    var marker = map.Marker(site);
                    if (marker == null) continue;
                    Vector2 p = map.Project(marker.position, frame);
                    var hit = new Rect(p.x - 12, p.y - 12, 24, 24);
                    if (site == hud.CurrentSite) Outline(hit, Amber);
                    Status state = hud.ReadObjective(site).Status;
                    DrawSymbol(site, p, state);
                    // Equipment and gate are close to spawn. Use their distinct symbols and the keyed legend.
                    if (site >= Site.Checkpoint && site <= Site.Storage)
                    {
                        float lx = Mathf.Clamp(p.x - 48, frame.x + 2, frame.xMax - 96);
                        lettering.alignment = TextAnchor.MiddleCenter;
                        Text(new Rect(lx, p.y + 10, 96, 24), Names[i], 14, Paper, true);
                        lettering.alignment = TextAnchor.MiddleLeft;
                    }
                    if (!busy && GUI.Button(hit, GUIContent.none, GUIStyle.none)) FocusSite(site);
                }
                Vector2 pos = map.Project(hud.transform.position, frame);
                var matrix = GUI.matrix;
                GUI.matrix = matrix * Matrix4x4.TRS(pos, Quaternion.Euler(0, 0, hud.transform.eulerAngles.y), Vector3.one);
                for (int line = 0; line < 8; line++)
                    Fill(new Rect(-line, -9 + line * 2, 1 + line * 2, 2), Cyan);
                GUI.matrix = matrix;
            }
            else Text(new Rect(frame.x + 24, frame.center.y, 360, 32), "Map geometry unavailable", 20, Paper, true);
            Text(new Rect(frame.x + 9, frame.y + 4, 120, 20), "N  ↑", 14, Paper, true);
            float left = x + 612;
            Text(new Rect(left, y, 450, 33), "Locations", 24, Paper, true);
            for (int i = 0; i < Names.Length; i++)
            {
                Site site = (Site)i;
                var row = hud.ReadObjective(site);
                var entry = new Rect(left, y + 46 + i * 47, 468, 40);
                Fill(entry, selectedSite == site ? new Color(.12f, .21f, .23f) : Surface);
                DrawSymbol(site, new Vector2(left + 20, entry.center.y), row.Status);
                Text(new Rect(left + 42, entry.y, 185, 40), Names[i], 18, Paper, true);
                Text(new Rect(left + 265, entry.y, 194, 40), States[(int)row.Status], 14, SiteColors[i], true);
                if (!busy && GUI.Button(entry, GUIContent.none, GUIStyle.none)) FocusSite(site);
            }
            var detail = hud.ReadObjective(selectedSite);
            Text(new Rect(left, y + 351, 468, 24), Titles[(int)selectedSite], 19, SiteColors[(int)selectedSite], true);
            lettering.wordWrap = true;
            Text(new Rect(left, y + 381, 460, 54), detail.Instruction, 17, Paper, false);
            lettering.wordWrap = false;
            Text(new Rect(left, y + 439, 460, 25), Progress(detail), 16, Muted, false);
            Text(new Rect(left, y + 508, 468, 27), "Cyan arrow   Player      White square   Spawn", 15, Muted, false);
            Text(new Rect(left, y + 537, 468, 27), "Amber outline   Current objective", 15, Amber, false);
        }

        private void DrawSymbol(Site site, Vector2 p, Status state)
        {
            Color tint = SiteColors[(int)site];
            var box = new Rect(p.x - 6, p.y - 6, 12, 12);
            Fill(box, Ink);
            if (site == Site.Extraction)
            {
                Fill(new Rect(p.x - 7, p.y - 6, 14, 3), tint);
                Fill(new Rect(p.x - 7, p.y - 3, 3, 10), tint);
                Fill(new Rect(p.x + 4, p.y - 3, 3, 10), tint);
            }
            else if (site == Site.Supply)
            {
                Fill(new Rect(p.x - 7, p.y - 4, 14, 10), tint);
                Outline(new Rect(p.x - 4, p.y - 8, 8, 5), tint);
            }
            else Outline(box, tint);
            if (state == Status.Complete) Text(new Rect(p.x - 6, p.y - 12, 21, 24), "✓", 15, Green, true);
            else if (state == Status.InProgress || state == Status.Available)
                Fill(new Rect(p.x - 2, p.y - 2, 4, 4), tint);
        }

        private void Outline(Rect r, Color color)
        {
            Fill(new Rect(r.x, r.y, r.width, 1), color);
            Fill(new Rect(r.x, r.yMax - 1, r.width, 1), color);
            Fill(new Rect(r.x, r.y, 1, r.height), color);
            Fill(new Rect(r.xMax - 1, r.y, 1, r.height), color);
        }
    }
}
