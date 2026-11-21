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
        private GUIStyle checkpointTitle, checkpointObjective, checkpointDetail;

        internal void SetCheckpoint(CheckpointSecurityScanner scanner) => checkpoint = scanner;

        private void Start()
        {
            player = GetComponent<PlayerController>();
            health = GetComponent<Health>();
            GetComponent<GameplayAudio>().Initialize(player, null);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || player == null) return;

            if (checkpoint != null && checkpoint.isActiveAndEnabled) DrawCheckpointObjective();

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

        private void DrawCheckpointObjective()
        {
            float scale = Mathf.Clamp(Screen.height / 900f, .75f, 2f);
            float margin = 16 * scale;
            float width = Mathf.Min(490 * scale, Screen.width - margin * 2);
            string notice = checkpoint.Notice;
            float height = (string.IsNullOrEmpty(notice) ? 96 : 123) * scale;
            var accent = checkpoint.IsComplete ? new Color(.43f, 1f, .59f) : new Color(1f, .82f, .44f);
            Color previousColor = GUI.color;
            GUI.color = new Color(.045f, .075f, .09f, .96f);
            GUI.DrawTexture(new Rect(margin, margin, width, height), Texture2D.whiteTexture);
            GUI.color = accent;
            GUI.DrawTexture(new Rect(margin, margin, 4 * scale, height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            checkpointTitle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            checkpointObjective ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            checkpointDetail ??= new GUIStyle(GUI.skin.label);
            checkpointTitle.fontSize = Mathf.RoundToInt(15 * scale);
            checkpointObjective.fontSize = Mathf.RoundToInt(23 * scale);
            checkpointDetail.fontSize = Mathf.RoundToInt(16 * scale);
            checkpointTitle.normal.textColor = accent;
            checkpointObjective.normal.textColor = checkpoint.IsComplete ? accent : Color.white;
            checkpointDetail.normal.textColor = new Color(.83f, .9f, .93f);
            float textX = margin + 16 * scale;
            float textWidth = width - 30 * scale;
            GUI.Label(new Rect(textX, margin + 9 * scale, textWidth, 22 * scale), "OPTIONAL CHECKPOINT TASK", checkpointTitle);
            GUI.Label(new Rect(textX, margin + 33 * scale, textWidth, 30 * scale), checkpoint.Objective, checkpointObjective);
            GUI.Label(new Rect(textX, margin + 66 * scale, textWidth, 23 * scale), checkpoint.Hint, checkpointDetail);
            if (!string.IsNullOrEmpty(notice))
            {
                checkpointDetail.normal.textColor = new Color(1f, .43f, .38f);
                GUI.Label(new Rect(textX, margin + 92 * scale, textWidth, 26 * scale), notice, checkpointDetail);
            }
            GUI.color = previousColor;
        }
    }
}
