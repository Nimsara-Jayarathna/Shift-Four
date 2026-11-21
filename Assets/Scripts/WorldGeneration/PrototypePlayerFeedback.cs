using UnityEngine;

namespace ShiftFour
{
    // Outdoor shooting feedback uses the shared audio and weapon state without a mission session.
    [RequireComponent(typeof(PlayerController), typeof(GameplayAudio))]
    public sealed class PrototypePlayerFeedback : MonoBehaviour
    {
        private PlayerController player;
        private Health health;
        private CheckpointSecurityScanner checkpoint;
        private ServerNetworkPuzzle server;
        private ControlLockdownMission control;
        private StoragePowerMission storage;
        private GUIStyle titleStyle, objectiveStyle, detailStyle;

        internal void SetMissions(CheckpointSecurityScanner scanner, ServerNetworkPuzzle network,
            ControlLockdownMission lockdown, StoragePowerMission power)
        {
            checkpoint = scanner;
            server = network;
            control = lockdown;
            storage = power;
        }

        private void Start()
        {
            player = GetComponent<PlayerController>();
            health = GetComponent<Health>();
            GetComponent<GameplayAudio>().Initialize(player, null);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || player == null) return;

            float scale = Mathf.Clamp(Screen.height / 900f, .75f, 2f);
            float top = 16 * scale;
            var completeAccent = new Color(.43f, 1f, .59f);
            if (checkpoint != null && checkpoint.isActiveAndEnabled)
                top = DrawObjective(top, scale, "OPTIONAL CHECKPOINT TASK", checkpoint.Objective,
                    checkpoint.Hint, checkpoint.Notice, checkpoint.IsComplete,
                    checkpoint.IsComplete ? completeAccent : new Color(1f, .82f, .44f), true);
            if (server != null && server.isActiveAndEnabled)
                top = DrawObjective(top, scale, "OPTIONAL SERVER TASK",
                    server.IsComplete ? ServerNetworkPuzzle.CompletionMessage : server.Objective,
                    server.IsComplete ? string.Empty : "Read the sequence display inside Server",
                    server.Notice, server.IsComplete, server.IsComplete ? completeAccent : new Color(.51f, .88f, 1f),
                    server.HasWarning);
            if (control != null && control.isActiveAndEnabled)
                top = DrawObjective(top, scale, "OPTIONAL CONTROL ROOM TASK", control.Objective, control.Hint,
                    control.Notice, control.IsComplete,
                    control.IsComplete ? completeAccent : new Color(1f, .57f, .49f), true);
            if (storage != null && storage.isActiveAndEnabled)
                DrawObjective(top, scale, "OPTIONAL STORAGE TASK", storage.Objective, storage.Hint,
                    storage.Notice, storage.IsComplete,
                    storage.IsComplete ? completeAccent : new Color(.91f, .81f, 1f), true);

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                float x = Screen.width * .5f;
                float y = Screen.height * .5f;
                GUI.DrawTexture(new Rect(x - 4, y, 9, 1), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x, y - 4, 1, 9), Texture2D.whiteTexture);
                if (!string.IsNullOrEmpty(player.InteractionPrompt))
                {
                    Vector2 size = GUI.skin.box.CalcSize(new GUIContent(player.InteractionPrompt));
                    GUI.Box(new Rect(x - size.x * .5f, y + 16, size.x, size.y), player.InteractionPrompt);
                }
            }

            var weapon = player.Weapons?.ActiveWeapon;
            if (health != null)
                GUI.Box(new Rect(16, Screen.height - 44, 132, 28), $"HP {Mathf.CeilToInt(health.Current)} / {Mathf.CeilToInt(health.Maximum)}");
            if (weapon == null) return;
            string ammo = $"{weapon.RoundsInMagazine} / {weapon.ReserveRounds}";
            if (weapon.ReloadState == WeaponReloadState.Reloading) ammo += "  Reloading";
            GUI.Box(new Rect(Screen.width - 166, Screen.height - 44, 150, 28), ammo);
        }

        private float DrawObjective(float top, float scale, string title, string objective, string detail,
            string notice, bool complete, Color accent, bool warning)
        {
            float margin = 16 * scale;
            float width = Mathf.Min(490 * scale, Screen.width - margin * 2);
            float height = (string.IsNullOrEmpty(notice) ? 96 : 123) * scale;
            Color previousColor = GUI.color;
            GUI.color = new Color(.045f, .075f, .09f, .96f);
            GUI.DrawTexture(new Rect(margin, top, width, height), Texture2D.whiteTexture);
            GUI.color = accent;
            GUI.DrawTexture(new Rect(margin, top, 4 * scale, height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            titleStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            objectiveStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            detailStyle ??= new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = Mathf.RoundToInt(15 * scale);
            objectiveStyle.fontSize = Mathf.RoundToInt(23 * scale);
            detailStyle.fontSize = Mathf.RoundToInt(16 * scale);
            titleStyle.normal.textColor = accent;
            objectiveStyle.normal.textColor = complete ? accent : Color.white;
            detailStyle.normal.textColor = new Color(.83f, .9f, .93f);
            bool hasDetail = !string.IsNullOrEmpty(detail);
            objectiveStyle.wordWrap = !hasDetail;
            float textX = margin + 16 * scale;
            float textWidth = width - 30 * scale;
            GUI.Label(new Rect(textX, top + 9 * scale, textWidth, 22 * scale), title, titleStyle);
            GUI.Label(new Rect(textX, top + 33 * scale, textWidth, (hasDetail ? 30 : 58) * scale), objective, objectiveStyle);
            if (hasDetail) GUI.Label(new Rect(textX, top + 66 * scale, textWidth, 23 * scale), detail, detailStyle);
            if (!string.IsNullOrEmpty(notice))
            {
                detailStyle.normal.textColor = warning ? new Color(1f, .43f, .38f) : accent;
                GUI.Label(new Rect(textX, top + 92 * scale, textWidth, 26 * scale), notice, detailStyle);
            }
            GUI.color = previousColor;
            return top + height + 10 * scale;
        }
    }
}
