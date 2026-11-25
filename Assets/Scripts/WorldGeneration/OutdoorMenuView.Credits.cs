using UnityEngine;

namespace ShiftFour
{
    internal sealed partial class OutdoorMenuView
    {
        private Texture2D creditsBackdrop;
        private static readonly Color CreditRule = new Color(.35f, .72f, .74f, .37f);

        private void DrawCredits(OutdoorMissionMenu menu, float w, float h)
        {
            float t = menu.CreditsTime;
            float fade = Mathf.SmoothStep(0f, 1f, t);
            alpha = 1f;
            Fill(new Rect(0, 0, w, h), new Color(0, 0, 0, Mathf.SmoothStep(0, 1, menu.EndingTime - 11f)));
            if (creditsBackdrop == null) creditsBackdrop = MakeCreditsBackdrop();
            GUI.color = new Color(1, 1, 1, fade);
            GUI.DrawTexture(new Rect(0, 0, w, h), creditsBackdrop);

            alpha = fade;
            float cx = w * .5f, cy = h * .5f;
            // Slow, deterministic dust and a soft scan line use the menu's existing white texture.
            for (int i = 1; i <= 22; i++)
            {
                float x = Mathf.Repeat(i * .618034f, 1f) * w;
                float y = Mathf.Repeat(i * .414214f * h - t * (2 + i % 4), h);
                Fill(new Rect(x, y, i % 3 == 0 ? 2 : 1, 2), new Color(.45f, .87f, .89f, .26f));
            }
            Fill(new Rect(0, Mathf.Repeat(t * 19f + .21f * h, h), w, 1), new Color(.28f, .74f, .77f, .045f));
            const float half = 440f;
            Fill(new Rect(cx - half, cy - 228, half * 2, 1), CreditRule);
            Fill(new Rect(cx - half, cy + 228, half * 2, 1), CreditRule);
            // Four machined strokes repeat the insignia on the title screen.
            for (int i = 0; i < 4; i++)
            {
                float x = cx - 18 + i * 10;
                var saved = GUI.matrix;
                GUI.matrix = saved * Matrix4x4.TRS(new Vector3(x, cy - 228), Quaternion.Euler(0, 0, 15), Vector3.one);
                Fill(new Rect(-2.5f, -8, 5, 16), Cyan);
                GUI.matrix = saved;
            }

            lettering.alignment = TextAnchor.MiddleCenter;
            if (t >= 1f && t < 4f)
            {
                alpha = CardOpacity(t - 1, 3);
                float y = cy + Mathf.Lerp(12, -8, (t - 1) / 3);
                Text(new Rect(cx - half, y - 87, 2 * half, 105), "SHIFT FOUR", 76, Paper, true);
                float length = Mathf.Lerp(24, 200, Mathf.SmoothStep(0, 1, t - 1));
                Fill(new Rect(cx - length / 2, y + 22, length, 2), Cyan);
                Text(new Rect(cx - half, y + 36, 2 * half, 48), menu.CreditsConfig.GroupLine, 23, Cyan, true);
            }
            else if (t >= 4f && t < 14f)
            {
                alpha = CardOpacity(t - 4, 10);
                float y = cy - 200 + Mathf.Lerp(10, -6, (t - 4) / 10);
                Text(new Rect(cx - half, y, 2 * half, 37), "DEVELOPMENT TEAM", 22, Cyan, true);
                var team = menu.CreditsConfig.Team;
                float row = 243f / Mathf.Max(1, Mathf.CeilToInt(team.Length / 2f));
                for (int i = 0; i < team.Length; i++)
                {
                    float x = cx - half + i % 2 * half;
                    float top = y + 49 + i / 2 * row;
                    Text(new Rect(x, top, half, 45), team[i].name, 35, Paper, true);
                    Text(new Rect(x, top + 45, half, 27), team[i].role, 21, Cyan, false);
                    Text(new Rect(x, top + 73, half, 25), team[i].assignment, 18, Paper, false);
                }
                Fill(new Rect(cx, y + 57, 1, 204), CreditRule);
                alpha *= Mathf.SmoothStep(0, 1, t - 6);
                Fill(new Rect(cx - 80, y + 301, 160, 1), CreditRule);
                Text(new Rect(cx - half, y + 316, 2 * half, 30), "SE3062 — Intelligent Systems", 19, Paper, false);
                Text(new Rect(cx - half, y + 346, 2 * half, 30), "SE3032 — Graphics and Visualization", 19, Paper, false);
            }
            else if (t >= 14f)
            {
                alpha = CardOpacity(t - 14, 3);
                float y = cy - 38 + Mathf.Lerp(10, -6, (t - 14) / 3);
                Text(new Rect(cx - half, y, 2 * half, 76), "THANK YOU FOR PLAYING", 49, Paper, true);
            }
            alpha = fade * Mathf.Clamp01(OutdoorMissionMenu.CreditsDuration - t);
            Text(new Rect(cx - half, h - 58, 2 * half, 32), "PRESS SPACE TO SKIP CREDITS", 16, Cyan, false);
            lettering.alignment = TextAnchor.MiddleLeft;
            alpha = Mathf.SmoothStep(0, 1, (t - 16.4f) / .6f);
            Fill(new Rect(0, 0, w, h), Color.black);
        }

        private static float CardOpacity(float age, float duration) =>
            Mathf.SmoothStep(0, 1, age / .42f) * Mathf.SmoothStep(0, 1, (duration - age) / .42f);

        private static Texture2D MakeCreditsBackdrop()
        {
            var background = new Texture2D(96, 48, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            for (int y = 0; y < background.height; y++)
            for (int x = 0; x < background.width; x++)
            {
                float u = x / 95f, v = y / 47f;
                float glow = Mathf.Clamp01(1 - 1.3f * new Vector2(u - .2f, v - 1.05f).magnitude);
                background.SetPixel(x, y, Color.Lerp(new Color(.029f, .038f, .048f),
                    new Color(.057f, .145f, .159f), glow * glow));
            }
            background.Apply(false, true);
            return background;
        }
    }
}
