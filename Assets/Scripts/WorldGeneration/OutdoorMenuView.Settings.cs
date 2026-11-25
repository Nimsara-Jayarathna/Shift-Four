using UnityEngine;

namespace ShiftFour
{
    internal sealed partial class OutdoorMenuView
    {
        private enum Field { Quality, Resolution, Mode, Master, Music, Effects, Sensitivity, Reset, Back, Apply }
        private static readonly Color Surface = new(.075f, .12f, .14f);
        private static readonly Color Muted = new(.60f, .69f, .70f);
        private static readonly string[] Presets = { "Low", "Balanced", "High" };
        private static readonly string[] Modes = { "Windowed", "Fullscreen" };
        private const float Column = 372f;

        private void DrawSettings(OutdoorMissionMenu menu, float w, float h)
        {
            var settings = menu.Preferences;
            HandleSettingsKeys(menu, settings);
            if (menu.Screen != OutdoorMissionMenu.ScreenState.Settings) return;
            ref var v = ref settings.Draft;
            float x = (w - 900f) / 2, y = (h - 516f) / 2;
            Fill(new Rect(x, y, 900, 516), new Color(.033f, .058f, .071f, .97f));
            Fill(new Rect(x, y, 900, 1), Muted);
            Fill(new Rect(x, y - 1, 76, 3), Cyan);
            x += 42;
            Text(new Rect(x, y + 24, 300, 19), "SHIFT FOUR", 14, Cyan, true);
            Text(new Rect(x - 2, y + 44, 500, 61), "SETTINGS", 48, Paper, true);
            float right = x + 444;
            Fill(new Rect(x + 406, y + 120, 1, 276), Surface);
            Heading(x, y + 119, "GRAPHICS");
            Text(new Rect(x, y + 150, Column, 23), "Quality", 17, Paper, false);
            v.Quality = (OutdoorSettings.Preset)Segments(x, y + 178, Presets, (int)v.Quality, Field.Quality, true);
            Text(new Rect(x, y + 228, Column, 23), "Resolution", 17, Paper, false);
            string size = settings.DisplayAvailable ? $"{v.Size.x} × {v.Size.y}" : $"{Screen.width} × {Screen.height}";
            var band = new Rect(x, y + 256, Column, 34);
            Fill(band, Surface);
            Center(band, size, 16, settings.DisplayAvailable ? Paper : Muted);
            if (FieldButton(new Rect(x, band.y, 42, 34), "‹", Field.Resolution, settings.DisplayAvailable)) StepResolution(settings, -1);
            if (FieldButton(new Rect(band.xMax - 42, band.y, 42, 34), "›", Field.Resolution, settings.DisplayAvailable)) StepResolution(settings, 1);
            Text(new Rect(x, y + 307, Column, 23), "Display mode", 17, Paper, false);
            v.Fullscreen = Segments(x, y + 335, Modes, v.Fullscreen ? 1 : 0, Field.Mode, settings.DisplayAvailable) == 1;
            if (!settings.DisplayAvailable)
                Text(new Rect(x, y + 373, Column, 23), Application.isEditor ? "Display is managed by the Editor Game view." : "Display modes are managed by this device.", 14, Muted, false);

            Heading(right, y + 119, "AUDIO");
            v.Master = Mathf.RoundToInt(Slider(right, y + 150, Field.Master, "Master volume", v.Master, 0, 100, v.Master + "%"));
            v.Music = Mathf.RoundToInt(Slider(right, y + 201, Field.Music, "Music", v.Music, 0, 100, v.Music + "%"));
            v.Effects = Mathf.RoundToInt(Slider(right, y + 252, Field.Effects, "Sound effects", v.Effects, 0, 100, v.Effects + "%"));
            Heading(right, y + 324, "CONTROLS");
            v.Sensitivity = Mathf.Round(Slider(right, y + 355, Field.Sensitivity, "Mouse sensitivity", v.Sensitivity, .2f, 5f,
                v.Sensitivity.ToString("0.0")) * 10f) / 10f;

            Fill(new Rect(x, y + 421, 816, 1), Surface);
            if (FieldButton(new Rect(x, y + 445, 174, 43), "RESET TO DEFAULT", Field.Reset, true))
                settings.Draft = settings.Defaults();
            if (settings.Dirty) Text(new Rect(x + 188, y + 445, 290, 43), "Back discards unapplied changes", 14, Amber, false);
            if (FieldButton(new Rect(right + 90, y + 445, 128, 43), "BACK", Field.Back, true)) menu.Back();
            if (FieldButton(new Rect(right + 230, y + 445, 142, 43), "APPLY", Field.Apply, settings.Dirty)) settings.Apply();
        }

        private void Heading(float x, float y, string title)
        {
            Fill(new Rect(x, y + 3, 3, 16), Cyan);
            Text(new Rect(x + 12, y, Column - 12, 23), title, 16, Cyan, true);
        }

        private void Center(Rect rect, string value, int size, Color color)
        {
            lettering.alignment = TextAnchor.MiddleCenter;
            Text(rect, value, size, color, true);
            lettering.alignment = TextAnchor.MiddleLeft;
        }

        private int Segments(float x, float y, string[] labels, int index, Field field, bool enabled)
        {
            float width = Column / labels.Length;
            for (int i = 0; i < labels.Length; i++)
                if (FieldButton(new Rect(x + i * width, y, width - 2, 34), labels[i], field, enabled, i == index)) index = i;
            return index;
        }

        private bool FieldButton(Rect rect, string label, Field field, bool enabled, bool chosen = false)
        {
            bool hover = enabled && rect.Contains(Event.current.mousePosition);
            Fill(rect, hover || chosen && enabled ? new Color(.13f, .24f, .26f) : Surface);
            Center(rect, label, 15, enabled ? chosen ? Cyan : Paper : Muted);
            if (chosen || hover || enabled && selected == (int)field)
                Fill(new Rect(rect.x, rect.yMax - 2, rect.width, 2), chosen && enabled ? Cyan : enabled ? Amber : Muted);
            if (!enabled || !GUI.Button(rect, GUIContent.none, GUIStyle.none)) return false;
            selected = (int)field;
            return true;
        }

        private float Slider(float x, float y, Field field, string label, float value, float min, float max, string display)
        {
            var hit = new Rect(x, y + 20, Column, 25);
            var ev = Event.current;
            int id = GUIUtility.GetControlID((int)field + 4100, FocusType.Passive, hit);
            bool hover = hit.Contains(ev.mousePosition);
            bool down = ev.type == EventType.MouseDown && ev.button == 0 && hover;
            if (down || GUIUtility.hotControl == id && ev.type == EventType.MouseDrag)
            {
                selected = (int)field;
                GUIUtility.hotControl = id;
                value = Mathf.Lerp(min, max, Mathf.InverseLerp(x + 5, x + Column - 5, ev.mousePosition.x));
                ev.Use();
            }
            else if (GUIUtility.hotControl == id && ev.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                ev.Use();
            }
            Text(new Rect(x, y, Column - 76, 22), label, 17, Paper, false);
            lettering.alignment = TextAnchor.MiddleRight;
            Text(new Rect(x + Column - 76, y, 76, 22), display, 17, Cyan, true);
            lettering.alignment = TextAnchor.MiddleLeft;
            float thumb = Mathf.Lerp(5, Column - 5, Mathf.InverseLerp(min, max, value));
            Fill(new Rect(x, y + 32, Column, 4), Surface);
            Fill(new Rect(x, y + 32, thumb, 4), Cyan);
            Fill(new Rect(x + thumb - 4, y + 26, 8, 16), hover || selected == (int)field ? Amber : Paper);
            return value;
        }

        private static void StepResolution(OutdoorSettings settings, int dir)
        {
            int count = settings.Resolutions.Count;
            if (count == 0) return;
            int index = settings.Resolutions.IndexOf(settings.Draft.Size);
            if (index < 0) index = dir > 0 ? -1 : 0;
            settings.Draft.Size = settings.Resolutions[(index + dir + count) % count];
        }

        private void HandleSettingsKeys(OutdoorMissionMenu menu, OutdoorSettings settings)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            int dir = e.keyCode == KeyCode.LeftArrow ? -1 : e.keyCode == KeyCode.RightArrow ? 1 : 0;
            var field = (Field)selected;
            if (e.keyCode is KeyCode.UpArrow or KeyCode.DownArrow or KeyCode.Tab)
            {
                int step = e.keyCode == KeyCode.UpArrow || e.shift ? -1 : 1;
                int count = (int)Field.Apply + 1;
                do { selected = (selected + step + count) % count; field = (Field)selected; }
                while ((!settings.DisplayAvailable && field is Field.Resolution or Field.Mode) || (!settings.Dirty && field == Field.Apply));
            }
            else if (dir != 0)
            {
                ref var v = ref settings.Draft;
                switch (field)
                {
                    case Field.Quality: v.Quality = (OutdoorSettings.Preset)(((int)v.Quality + dir + 3) % 3); break;
                    case Field.Resolution: StepResolution(settings, dir); break;
                    case Field.Mode: v.Fullscreen = !v.Fullscreen; break;
                    case Field.Master: v.Master = Mathf.Clamp(v.Master + dir, 0, 100); break;
                    case Field.Music: v.Music = Mathf.Clamp(v.Music + dir, 0, 100); break;
                    case Field.Effects: v.Effects = Mathf.Clamp(v.Effects + dir, 0, 100); break;
                    case Field.Sensitivity: v.Sensitivity = Mathf.Clamp(Mathf.Round(v.Sensitivity * 10 + dir) / 10, .2f, 5f); break;
                }
            }
            else if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter or KeyCode.Space)
            {
                if (field == Field.Reset) settings.Draft = settings.Defaults();
                if (field == Field.Back) menu.Back();
                if (field == Field.Apply && settings.Dirty) settings.Apply();
            }
            else return;
            e.Use();
        }
    }
}
