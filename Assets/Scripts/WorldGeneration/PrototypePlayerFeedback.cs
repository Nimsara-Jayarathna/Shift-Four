using UnityEngine;

namespace ShiftFour
{
    // Outdoor shooting feedback uses the shared audio and weapon state without a mission session.
    [RequireComponent(typeof(PlayerController), typeof(GameplayAudio))]
    public sealed class PrototypePlayerFeedback : MonoBehaviour
    {
        private enum Mission { None, Checkpoint, Server, Control, Storage }

        private static readonly Color CompleteAccent = new Color(.43f, 1f, .59f);
        private static readonly Color PanelColor = new Color(.045f, .075f, .09f, .96f);
        private PlayerController player;
        private Health health;
        private CheckpointSecurityScanner checkpoint;
        private ServerNetworkPuzzle server;
        private ControlLockdownMission control;
        private StoragePowerMission storage;
        private BoxCollider checkpointFloor, serverFloor, controlFloor, storageFloor;
        private GUIStyle titleStyle, objectiveStyle, detailStyle;

        internal void SetMissions(Transform buildings, CheckpointSecurityScanner scanner, ServerNetworkPuzzle network,
            ControlLockdownMission lockdown, StoragePowerMission power)
        {
            checkpoint = scanner;
            server = network;
            control = lockdown;
            storage = power;
            checkpointFloor = FindFloor(buildings, "Checkpoint");
            serverFloor = FindFloor(buildings, "Server");
            controlFloor = FindFloor(buildings, "Control");
            storageFloor = FindFloor(buildings, "Storage");
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

            DrawMissions();

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

        private BoxCollider FindFloor(Transform buildings, string room)
        {
            var floor = buildings != null ? buildings.Find($"{room}/Foundation")?.GetComponent<BoxCollider>() : null;
            if (floor == null) Debug.LogWarning($"Open World missions: {room} foundation is missing; HUD room focus is unavailable.", this);
            return floor;
        }

        private Mission FocusedMission()
        {
            Vector3 feet = transform.position;
            if (checkpoint != null && checkpoint.isActiveAndEnabled && Inside(checkpointFloor, feet)) return Mission.Checkpoint;
            if (server != null && server.isActiveAndEnabled && Inside(serverFloor, feet)) return Mission.Server;
            if (control != null && control.isActiveAndEnabled && Inside(controlFloor, feet)) return Mission.Control;
            if (storage != null && storage.isActiveAndEnabled && Inside(storageFloor, feet)) return Mission.Storage;
            return Mission.None;
        }

        private static bool Inside(BoxCollider floor, Vector3 feet)
        {
            if (floor == null || !floor.enabled || !floor.gameObject.activeInHierarchy) return false;
            Bounds bounds = floor.bounds;
            if (feet.y < bounds.min.y - .25f || feet.y > bounds.max.y + 4f) return false;
            feet.y = bounds.center.y;
            return bounds.Contains(feet);
        }

        private void DrawMissions()
        {
            float scale = Mathf.Clamp(Screen.height / 900f, .75f, 2f);
            float margin = 16 * scale;
            float width = Mathf.Min(490 * scale, Screen.width * .47f - margin);
            float top = margin;
            var focus = FocusedMission();

            switch (focus)
            {
                case Mission.Checkpoint:
                    top = DrawObjective(top, width, scale, "OPTIONAL CHECKPOINT TASK", checkpoint.Objective,
                        checkpoint.Hint, checkpoint.Notice, checkpoint.IsComplete, new Color(1f, .82f, .44f), true);
                    break;
                case Mission.Server:
                    top = DrawObjective(top, width, scale, "OPTIONAL SERVER TASK",
                        server.IsComplete ? ServerNetworkPuzzle.CompletionMessage : server.Objective,
                        server.IsComplete ? string.Empty : "Read the sequence display inside Server",
                        server.Notice, server.IsComplete, new Color(.51f, .88f, 1f), server.HasWarning);
                    break;
                case Mission.Control:
                    top = DrawObjective(top, width, scale, "OPTIONAL CONTROL ROOM TASK", control.Objective,
                        control.Hint, control.Notice, control.IsComplete, new Color(1f, .57f, .49f), true);
                    break;
                case Mission.Storage:
                    top = DrawObjective(top, width, scale, "OPTIONAL STORAGE TASK", storage.Objective,
                        storage.Hint, storage.Notice, storage.IsComplete, new Color(.91f, .81f, 1f), true);
                    break;
            }

            if (focus != Mission.Checkpoint && checkpoint != null && checkpoint.isActiveAndEnabled)
                top = DrawSummary(top, width, scale, "Checkpoint", checkpoint.Objective, checkpoint.IsComplete);
            if (focus != Mission.Server && server != null && server.isActiveAndEnabled)
                top = DrawSummary(top, width, scale, "Server", server.Objective, server.IsComplete);
            if (focus != Mission.Control && control != null && control.isActiveAndEnabled)
                top = DrawSummary(top, width, scale, "Control", control.Objective, control.IsComplete);
            if (focus != Mission.Storage && storage != null && storage.isActiveAndEnabled)
                DrawSummary(top, width, scale, "Storage", storage.Objective, storage.IsComplete);
        }

        private float DrawObjective(float top, float width, float scale, string title, string objective, string detail,
            string notice, bool complete, Color accent, bool warning)
        {
            titleStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            objectiveStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
            detailStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            titleStyle.fontSize = Mathf.RoundToInt(15 * scale);
            objectiveStyle.fontSize = Mathf.RoundToInt(23 * scale);
            detailStyle.fontSize = Mathf.RoundToInt(16 * scale);
            if (complete) accent = CompleteAccent;
            titleStyle.normal.textColor = accent;
            objectiveStyle.normal.textColor = complete ? accent : Color.white;
            detailStyle.normal.textColor = new Color(.83f, .9f, .93f);

            float margin = 16 * scale;
            float textWidth = width - 30 * scale;
            float objectiveHeight = objectiveStyle.CalcHeight(new GUIContent(objective), textWidth);
            float detailHeight = string.IsNullOrEmpty(detail) ? 0 : detailStyle.CalcHeight(new GUIContent(detail), textWidth);
            float noticeHeight = string.IsNullOrEmpty(notice) ? 0 : detailStyle.CalcHeight(new GUIContent(notice), textWidth);
            float height = 43 * scale + objectiveHeight + detailHeight + noticeHeight;
            DrawBackground(new Rect(margin, top, width, height), accent);

            float textX = margin + 16 * scale;
            float y = top + 9 * scale;
            GUI.Label(new Rect(textX, y, textWidth, 22 * scale), title, titleStyle);
            y += 24 * scale;
            GUI.Label(new Rect(textX, y, textWidth, objectiveHeight), objective, objectiveStyle);
            y += objectiveHeight;
            if (detailHeight > 0)
            {
                GUI.Label(new Rect(textX, y, textWidth, detailHeight), detail, detailStyle);
                y += detailHeight;
            }
            if (noticeHeight > 0)
            {
                detailStyle.normal.textColor = warning ? new Color(1f, .43f, .38f) : accent;
                GUI.Label(new Rect(textX, y, textWidth, noticeHeight), notice, detailStyle);
            }
            return top + height + 10 * scale;
        }

        private float DrawSummary(float top, float width, float scale, string title, string objective, bool complete)
        {
            detailStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            detailStyle.fontSize = Mathf.RoundToInt(14 * scale);
            detailStyle.normal.textColor = complete ? CompleteAccent : Color.white;
            string text = $"{title}  ·  {(complete ? "Complete" : objective)}";
            float margin = 16 * scale;
            float textWidth = width - 22 * scale;
            float textHeight = detailStyle.CalcHeight(new GUIContent(text), textWidth);
            float height = textHeight + 6 * scale;
            DrawBackground(new Rect(margin, top, width, height), complete ? CompleteAccent : new Color(.45f, .57f, .63f));
            GUI.Label(new Rect(margin + 11 * scale, top + 3 * scale, textWidth, textHeight), text, detailStyle);
            return top + height + 4 * scale;
        }

        private static void DrawBackground(Rect rect, Color accent)
        {
            Color previousColor = GUI.color;
            GUI.color = PanelColor;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = accent;
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3, rect.height), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }
    }
}
