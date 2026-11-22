using UnityEngine;

namespace ShiftFour
{
    // The prototype HUD already uses IMGUI. This view needs no second canvas, camera or event system.
    internal sealed partial class OutdoorMenuView
    {
        private static readonly Color Cyan = new Color(.38f, .89f, .89f);
        private static readonly Color Amber = new Color(1f, .74f, .32f);
        private static readonly Color Red = new Color(.89f, .38f, .32f);
        private static readonly Color Paper = new Color(.94f, .96f, .94f);
        private static readonly string[] Title = { "START GAME", "SETTINGS", "QUIT GAME" };
        private static readonly string[] Pause = { "RESUME", "RESTART MISSION", "SETTINGS", "MAIN MENU" };
        private static readonly string[] Victory = { "PLAY AGAIN", "MAIN MENU" };
        private static readonly string[] Defeat = { "RETRY MISSION", "MAIN MENU" };
        private static readonly string[] Buildings = { "Checkpoint", "Server", "Control", "Storage" };
        private readonly float[] highlights = new float[4];
        private Texture2D shade;
        private GUIStyle lettering;
        private int selected, lastHover = -1;
        private Vector2 previousMouse;
        private float alpha;

        internal void ResetSelection()
        {
            selected = 0;
            lastHover = -1;
            System.Array.Clear(highlights, 0, highlights.Length);
        }

        internal void Draw(OutdoorMissionMenu menu, float opacity, bool busy)
        {
            if (shade == null)
            {
                shade = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                for (int i = 0; i < shade.width; i++)
                {
                    float t = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.22f, .84f, i / 63f));
                    shade.SetPixel(i, 0, new Color(.025f, .056f, .068f, Mathf.Lerp(.98f, .15f, t)));
                }
                shade.Apply(false, true);
                lettering = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(), clipping = TextClipping.Clip };
            }
            var matrix = GUI.matrix;
            var color = GUI.color;
            int depth = GUI.depth;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            if (scale <= 0f) return;
            float w = Screen.width / scale, h = Screen.height / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUI.depth = -100;
            alpha = opacity;
            GUI.color = new Color(1, 1, 1, alpha);
            if (menu.Screen == OutdoorMissionMenu.ScreenState.Playing || menu.ViewingBriefing)
            {
                if (menu.ViewingBriefing) DrawBriefing(menu, w, h, busy);
                else DrawObjective(menu.Feedback);
                GUI.matrix = matrix;
                GUI.color = color;
                GUI.depth = depth;
                return;
            }
            GUI.DrawTexture(new Rect(0, 0, w, h), shade);
            if (menu.Screen == OutdoorMissionMenu.ScreenState.Settings || menu.Screen == OutdoorMissionMenu.ScreenState.Loading)
            {
                if (menu.Screen == OutdoorMissionMenu.ScreenState.Settings) DrawSettings(menu, w, h);
                else Text(new Rect(w * .067f, h * .5f, 480, 52), "LOADING FACILITY", 27, Paper, true);
                GUI.matrix = matrix;
                GUI.color = color;
                GUI.depth = depth;
                return;
            }
            bool paused = menu.Screen == OutdoorMissionMenu.ScreenState.Paused;
            bool result = menu.ShowingResult;
            bool victory = menu.Screen == OutdoorMissionMenu.ScreenState.Victory;
            string[] actions = result ? (victory ? Victory : Defeat) : paused ? Pause : Title;
            float x = w * .067f, top = h * .5f - 212f;
            float y;
            if (result)
            {
                Color accent = victory ? Cyan : Red;
                Fill(new Rect(0, 0, w, h), new Color(.025f, .038f, .046f, .74f));
                Text(new Rect(x, top + 8, 380, 36), "SHIFT FOUR", 24, Paper, true);
                Fill(new Rect(x, top + 60, 58, 4), accent);
                Text(new Rect(x - 3, top + 78, 650, 88), victory ? "MISSION COMPLETE" : "MISSION FAILED", 58, Paper, true);
                Text(new Rect(x, top + 167, 580, 36), victory ? "FACILITY ESCAPED" : "Your signal was lost inside the facility.",
                    victory ? 24 : 21, victory ? Cyan : Paper, victory);
                DrawReport(menu, w - x - 416, top + 8, accent);
                y = top + 270;
                busy |= opacity < .7f;
            }
            else
            {
                // Four cut-steel strokes form the facility insignia.
                for (int i = 0; i < 4; i++)
                {
                    float xx = x + i * 10f;
                    var logoMatrix = GUI.matrix;
                    GUIUtility.RotateAroundPivot(14f, new Vector2(xx, top + 12f));
                    Fill(new Rect(xx, top, 6, 25), i == 3 ? Amber : Cyan);
                    GUI.matrix = logoMatrix;
                }
                Text(new Rect(x + 60, top, 460, 25), "RESEARCH FACILITY OPERATIONS", 16, Cyan, true);
                Text(new Rect(x - 4, top + 44, 610, 94), "SHIFT FOUR", 76, Paper, true);
                y = top + 172;
                if (paused)
                {
                    Text(new Rect(x, top + 126, 440, 32), "PAUSED", 21, Amber, true);
                }
            }
            Fill(new Rect(x, y - 16, 350, 1), new Color(.56f, .72f, .74f, .45f));
            Fill(new Rect(x, y - 17, 45, 3), result && !victory ? Red : paused ? Amber : Cyan);

            var ev = Event.current;
            int activate = -1;
            if (!busy && ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.DownArrow || ev.keyCode == KeyCode.UpArrow)
                {
                    selected = (selected + (ev.keyCode == KeyCode.DownArrow ? 1 : actions.Length - 1)) % actions.Length;
                    ev.Use();
                }
                else if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.keyCode == KeyCode.Space)
                {
                    activate = selected;
                    ev.Use();
                }
            }
            int hovered = -1;
            for (int i = 0; i < actions.Length; i++)
            {
                var row = new Rect(x, y + i * 58f, 350, 48);
                if (row.Contains(ev.mousePosition)) hovered = i;
            }
            if (!busy && hovered >= 0 && (lastHover != hovered || previousMouse != ev.mousePosition))
                selected = hovered;
            lastHover = hovered;
            previousMouse = ev.mousePosition;
            for (int i = 0; i < actions.Length; i++)
            {
                var row = new Rect(x, y + i * 58f, 350, 48);
                if (ev.type == EventType.Repaint)
                    highlights[i] = Mathf.MoveTowards(highlights[i], selected == i ? 1f : 0f, Time.unscaledDeltaTime * 9f);
                float glow = highlights[i];
                Color accent = actions[i] == "QUIT GAME" || (result && !victory) ? Red : paused ? Amber : Cyan;
                bool pressed = hovered == i && Input.GetMouseButton(0);
                Fill(row, Color.Lerp(new Color(.05f, .09f, .11f, .85f), new Color(.14f, .24f, .26f), pressed ? .55f : glow));
                Fill(new Rect(row.x, row.y, 2f + 2f * glow, row.height), Color.Lerp(new Color(.26f, .37f, .39f), accent, glow));
                Text(new Rect(row.x + 18 + 5 * glow + (pressed ? 2f : 0f), row.y, 270, row.height), actions[i], 19, Paper, true);
                if (selected == i) Text(new Rect(row.xMax - 31, row.y, 25, row.height), "›", 30, accent, false);
                if (!busy && GUI.Button(row, GUIContent.none, GUIStyle.none)) activate = i;
            }
            GUI.matrix = matrix;
            GUI.color = color;
            GUI.depth = depth;
            if (activate < 0 || busy) return;
            if (result)
            {
                if (activate == 0) menu.RestartMission();
                else menu.MainMenu();
            }
            else if (activate == 0) menu.Play();
            else if (paused && activate == 1) menu.RestartMission();
            else if (activate == (paused ? 2 : 1)) menu.Settings();
            else if (paused) menu.MainMenu();
            else menu.Quit();
        }

        private void DrawReport(OutdoorMissionMenu menu, float x, float y, Color accent)
        {
            const float width = 416;
            Fill(new Rect(x, y, width, 404), new Color(.048f, .073f, .085f, .98f));
            Fill(new Rect(x, y, width, 2), accent);
            x += 26;
            Text(new Rect(x, y + 17, 360, 32), "MISSION RESULTS", 22, Paper, true);
            ResultRow(x, y + 66, "Building missions", menu.BuildingCount, Paper);
            Fill(new Rect(x, y + 101, width - 52, 1), Surface);
            for (int i = 0; i < Buildings.Length; i++)
                ResultRow(x, y + 110 + 37 * i, Buildings[i], menu.BuildingResults[i],
                    menu.BuildingResults[i] == "Complete" ? Cyan : Muted);
            Fill(new Rect(x, y + 263, width - 52, 1), Surface);
            ResultRow(x, y + 274, "Extraction targets defeated", menu.TargetCount, Paper);
            Text(new Rect(x, y + 310, width - 52, 22), "Scout  /  Guard  /  Flanker", 15, Muted, false);
            Fill(new Rect(x, y + 342, width - 52, 1), Surface);
            ResultRow(x, y + 354, "Active mission time", menu.ActiveTime, Paper);
        }

        private void ResultRow(float x, float y, string label, string value, Color color)
        {
            Text(new Rect(x, y, 243, 31), label, 18, Paper, false);
            lettering.alignment = TextAnchor.MiddleRight;
            Text(new Rect(x + 243, y, 121, 31), value, 17, color, true);
            lettering.alignment = TextAnchor.MiddleLeft;
        }

        private void Fill(Rect rect, Color tint)
        {
            tint.a *= alpha;
            GUI.color = tint;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        private void Text(Rect rect, string value, int size, Color tint, bool bold)
        {
            tint.a *= alpha;
            GUI.color = Color.white;
            lettering.normal.textColor = tint;
            lettering.fontSize = size;
            lettering.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            GUI.Label(rect, value, lettering);
        }

        internal void Dispose()
        {
            if (shade != null) Object.Destroy(shade);
        }
    }
}
