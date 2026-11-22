using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShiftFour
{
    // Installed by the outdoor generator after the scene's player and layout have been bound.
    [DefaultExecutionOrder(-200), DisallowMultipleComponent]
    public sealed class OutdoorMissionMenu : MonoBehaviour
    {
        internal OutdoorSettings Preferences { get; private set; }
        internal const string ScenePath = "Assets/Scenes/OpenWorldPrototype.unity";
        public enum ScreenState { Title, Playing, Paused, Settings, Dying, GameOver, Victory, Loading, Map, Missions }
        public ScreenState Screen { get; private set; } = ScreenState.Title;
        private ScreenState settingsParent;
        internal PrototypePlayerFeedback Feedback { get; private set; }
        internal bool ViewingBriefing => Screen == ScreenState.Map || Screen == ScreenState.Missions;
        private PlayerController player;
        private Health health;
        private OpenWorldMissionManager extraction;
        private PlayerDeathPresentation death;
        private double activeSince, elapsed;
        internal string BuildingCount { get; private set; }
        internal string TargetCount { get; private set; }
        internal string ActiveTime { get; private set; }
        internal readonly string[] BuildingResults = new string[4];
        internal bool ShowingResult => Screen == ScreenState.GameOver || Screen == ScreenState.Victory;
        private Camera view;
        private Transform overviewBuilding;
        private readonly OutdoorMenuView panel = new OutdoorMenuView();
        private float opacity, previousScale;
        private bool previousAudioPause, ownsClock, closing, overview;
        private int savedMask, actionFrame;
        private Quaternion savedRotation;
        private static bool startReloadedMission;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLaunch() => startReloadedMission = false;

        internal void Initialize(PlayerController actor)
        {
            player = actor;
            health = actor.GetComponent<Health>();
            health.Died += OnPlayerDied;
            Feedback = actor.GetComponent<PrototypePlayerFeedback>();
            BindExtraction(Feedback?.ExtractionManager);
            view = actor.ViewCamera;
            Preferences = new OutdoorSettings(actor, GetComponent<WorldGenerator>());
            if (startReloadedMission)
            {
                startReloadedMission = false;
                Change(ScreenState.Playing);
                SetCursor(true);
                return;
            }
            Freeze();
        }

        private void LateUpdate()
        {
            if (Screen == ScreenState.Dying)
            {
                // The menu blocks normal player LateUpdate. Use the same collision-aware camera pose.
                player.ApplyCameraPose();
                if (death == null || !death.isActiveAndEnabled || death.Progress >= 1f)
                {
                    Freeze();
                    Change(ScreenState.GameOver);
                }
                return;
            }
            bool title = Screen == ScreenState.Title || (Screen == ScreenState.Settings && settingsParent == ScreenState.Title);
            if (title && (overviewBuilding == null || !overviewBuilding.gameObject.activeInHierarchy)) ShowOverview();
        }

        private bool Ended => !health.IsAlive || (extraction != null && extraction.Escaped);

        internal void BindExtraction(OpenWorldMissionManager next)
        {
            if (extraction == next) return;
            if (extraction != null) extraction.Extracted -= OnExtracted;
            extraction = next;
            if (next != null) next.Extracted += OnExtracted;

            // The generator also supports deliberately replacing a finished layout in Play Mode.
            if (Screen == ScreenState.Victory && next != null && !next.Escaped && health.IsAlive)
            {
                RestoreClock();
                elapsed = 0;
                player.MenuBlocksInput = false;
                Change(ScreenState.Playing);
                SetCursor(true);
            }
        }

        private void OnPlayerDied(Health _)
        {
            death = player.GetComponent<PlayerDeathPresentation>();
            BeginResult(ScreenState.Dying);
        }
        private void OnExtracted() => BeginResult(ScreenState.Victory);

        private void BeginResult(ScreenState state)
        {
            if (ShowingResult || Screen == ScreenState.Dying || Screen == ScreenState.Loading) return;
            closing = false;
            if (Screen == ScreenState.Settings) Preferences.PreviewMusic(false);
            RestoreClock();
            RestoreView();
            Change(state);
            player.MenuBlocksInput = true;
            SetCursor(false);
            opacity = 0f;
            if (state == ScreenState.Victory) Freeze();

            BuildingCount = extraction == null ? "Unavailable" : $"{extraction.BuildingMissionsCompleted} / {OpenWorldMissionManager.RequiredMissions}";
            TargetCount = extraction == null ? "Unavailable" : $"{extraction.DronesDefeated} / {OpenWorldMissionManager.RequiredDrones}";
            BuildingResults[0] = Status(extraction?.CheckpointComplete);
            BuildingResults[1] = Status(extraction?.ServerComplete);
            BuildingResults[2] = Status(extraction?.ControlComplete);
            BuildingResults[3] = Status(extraction?.StorageComplete);
            var duration = System.TimeSpan.FromSeconds(elapsed);
            ActiveTime = $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
        }

        private static string Status(bool? complete) => complete.HasValue
            ? (complete.Value ? "Complete" : "Incomplete") : "Unavailable";

        private void Freeze()
        {
            previousScale = Time.timeScale;
            previousAudioPause = AudioListener.pause;
            ownsClock = true;
            player.MenuBlocksInput = true;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            SetCursor(false);
        }

        private void RestoreClock()
        {
            if (!ownsClock) return;
            Time.timeScale = previousScale;
            AudioListener.pause = previousAudioPause;
            ownsClock = false;
        }

        private void ShowOverview()
        {
            var building = transform.Find("GeneratedWorld/Buildings/Checkpoint") ?? transform.Find("Fixed Layout/Buildings/Checkpoint");
            if (view == null || building == null) return;
            overviewBuilding = building;
            if (!overview)
            {
                savedRotation = view.transform.localRotation;
                savedMask = view.cullingMask;
                overview = true;
            }
            view.cullingMask &= ~(1 << LayerMask.NameToLayer("Ignore Raycast")); // First-person meshes.
            Vector3 position = building.TransformPoint(new Vector3(-30f, 17f, -38f));
            var direction = building.TransformPoint(Vector3.up * 4f) - position;
            view.transform.SetPositionAndRotation(position, Quaternion.AngleAxis(-15f, Vector3.up) * Quaternion.LookRotation(direction));
        }

        private void RestoreView()
        {
            if (!overview || view == null) return;
            overview = false;
            view.cullingMask = savedMask;
            view.transform.localRotation = savedRotation;
            player.ApplyCameraPose();
        }

        private static void SetCursor(bool playing)
        {
            Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !playing;
        }

        private void Update()
        {
            if (player == null || Screen == ScreenState.Loading || Screen == ScreenState.Dying) return;
            if (Screen == ScreenState.Playing)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) Pause();
                else if (Input.GetKeyDown(KeyCode.M)) OpenMap();
                else if (Input.GetKeyDown(KeyCode.Tab)) OpenMissions();
                return;
            }
            opacity = Mathf.MoveTowards(opacity, closing ? 0f : 1f, Time.unscaledDeltaTime * 6f);
            if (closing)
            {
                // Held mouse/keyboard activation must end before the first gameplay input sample.
                if (opacity == 0f && Time.frameCount > actionFrame && !Input.GetMouseButton(0) &&
                    !Input.GetKey(KeyCode.Return) && !Input.GetKey(KeyCode.Space))
                {
                    RestoreView();
                    RestoreClock();
                    if (Screen == ScreenState.Title && Time.timeScale == 0f) Time.timeScale = 1f;
                    closing = false;
                    player.MenuBlocksInput = false;
                    Change(ScreenState.Playing);
                    SetCursor(true);
                }
                return;
            }
            if (ViewingBriefing)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(Screen == ScreenState.Map ? KeyCode.M : KeyCode.Tab))
                    Play();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (Screen == ScreenState.Settings) Back();
                else if (Screen == ScreenState.Paused) Play();
            }
        }

        public void OpenMap() => OpenBriefing(ScreenState.Map);
        public void OpenMissions() => OpenBriefing(ScreenState.Missions);

        private void OpenBriefing(ScreenState screen)
        {
            if (Screen != ScreenState.Playing || Ended || Feedback == null) return;
            Freeze();
            Change(screen);
            panel.FocusSite(Feedback.CurrentSite);
            opacity = 0f;
        }

        public void Pause()
        {
            if (Screen != ScreenState.Playing || Ended) return;
            Freeze();
            Change(ScreenState.Paused);
            opacity = 0f;
        }

        public void Play()
        {
            if (closing || (Screen != ScreenState.Title && Screen != ScreenState.Paused && !ViewingBriefing)) return;
            closing = true;
            actionFrame = Time.frameCount;
        }

        private void Change(ScreenState state)
        {
            if (Screen == ScreenState.Playing) elapsed += Time.unscaledTimeAsDouble - activeSince;
            if (state == ScreenState.Playing) activeSince = Time.unscaledTimeAsDouble;
            Screen = state;
            panel.ResetSelection();
        }

        public void Settings()
        {
            if (closing || (Screen != ScreenState.Title && Screen != ScreenState.Paused)) return;
            Preferences.Begin();
            Preferences.PreviewMusic(true);
            settingsParent = Screen;
            Change(ScreenState.Settings);
        }

        public void Back()
        {
            if (Screen != ScreenState.Settings) return;
            Preferences.PreviewMusic(false);
            Change(settingsParent);
        }

        public void RestartMission() => Reload(true);
        public void MainMenu() => Reload(false);

        private void Reload(bool startPlaying)
        {
            if ((Screen != ScreenState.Paused && !ShowingResult) || closing) return;
            Change(ScreenState.Loading);
            RestoreClock();
            Scene scene = gameObject.scene;
            startReloadedMission = startPlaying;
#if UNITY_EDITOR
            if (scene.buildIndex < 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scene.path,
                    new LoadSceneParameters(LoadSceneMode.Single));
                return;
            }
#endif
            SceneManager.LoadSceneAsync(scene.buildIndex, LoadSceneMode.Single);
        }

        public void Quit()
        {
            if (closing || Screen != ScreenState.Title) return;
            Change(ScreenState.Loading);
            RestoreClock();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnApplicationFocus(bool focused)
        {
            if (player == null || Screen == ScreenState.Loading || Ended) return;
            if (!focused)
            {
                if (closing) closing = false;
                Pause();
            }
            else SetCursor(Screen == ScreenState.Playing);
        }

        private void OnGUI()
        {
            if (player == null || Screen == ScreenState.Dying) return;
            if (Screen == ScreenState.Playing && (Feedback == null || player.MenuBlocksInput || Event.current.type != EventType.Repaint)) return;
            panel.Draw(this, Screen == ScreenState.Playing ? 1f : opacity, closing || Screen == ScreenState.Loading);
        }

        private void OnDisable()
        {
            RestoreClock();
            if (player != null)
            {
                RestoreView();
                player.MenuBlocksInput = false;
            }
        }

        private void OnDestroy()
        {
            if (health != null) health.Died -= OnPlayerDied;
            if (extraction != null) extraction.Extracted -= OnExtracted;
            Preferences?.Dispose();
            panel.Dispose();
        }
    }
}
