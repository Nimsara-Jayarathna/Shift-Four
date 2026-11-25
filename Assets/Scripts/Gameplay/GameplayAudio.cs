using System.Collections.Generic;
using UnityEngine;

namespace ShiftFour
{
    /// <summary>Scene-owned, bounded audible effects. Playback never emits gameplay noises.</summary>
    [DisallowMultipleComponent]
    public sealed class GameplayAudio : MonoBehaviour
    {
        public const int VoiceCapacity = 16;
        [SerializeField, Range(0f, 1f)] private float playerShotVolume = .65f;
        [SerializeField, Range(0f, 1f)] private float droneShotVolume = .5f;
        [SerializeField, Range(0f, 1f)] private float doorVolume = .85f;
        [SerializeField, Range(0f, 1f)] private float droneFlightVolume = .45f;
        [SerializeField, Range(0f, 1f)] private float backgroundMusicVolume = .12f;
        [SerializeField, Range(0f, 1f)] private float reloadVolume = .65f;
        private float musicGain = 1f, effectsGain = 1f;
        internal float EffectsGain => effectsGain;
        private readonly float[] voiceLevels = new float[VoiceCapacity];
        private AudioSource backgroundMusic;
        private AudioSource reloadSource;
        private AudioClip reloadClip;
        private WeaponController weapons;
        private readonly Dictionary<LabDoor, AudioSource> doorSources = new Dictionary<LabDoor, AudioSource>();
        private AudioClip playerShot, droneShot, doorOpen;
        private AudioClip droneFlight, advisory, relay;
        [System.Serializable]
        private struct FlightLoop
        {
            public AudioSource Source;
            public Health Health;
            public DroneRotorVisuals InterceptorVisuals;
        }

        private FlightLoop[] flightLoops = System.Array.Empty<FlightLoop>();
        private AudioSource[] voices;
        private LabDoor[] doors;
        private PlayerController player;
        private int nextVoice;
        public int PlayerShotsPlayed { get; private set; }
        public int DroneShotsPlayed { get; private set; }
        public int DoorsPlayed { get; private set; }
        public int ReloadsPlayed { get; private set; }

        private void Awake()
        {
            playerShot = Resources.Load<AudioClip>("Audio/PlayerPistol");
            droneShot = Resources.Load<AudioClip>("Audio/DroneShot");
            doorOpen = Resources.Load<AudioClip>("Audio/LabDoorOpen");
            droneFlight = Resources.Load<AudioClip>("Audio/DroneFlight");
            reloadClip = Resources.Load<AudioClip>("Audio/PlayerReload");
            voices = new AudioSource[VoiceCapacity];
            for (int i = 0; i < voices.Length; i++)
            {
                var voice = new GameObject("Gameplay audio voice " + i);
                voice.transform.SetParent(transform, false);
                var source = voices[i] = voice.AddComponent<AudioSource>();
                source.playOnAwake = false; source.loop = false;
                source.dopplerLevel = 0f; source.minDistance = 2f; source.maxDistance = 45f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
            }
            var musicClip = Resources.Load<AudioClip>("Audio/BackgroundMusic");
            if (musicClip != null)
            {
                var music = new GameObject("Background music");
                music.transform.SetParent(transform, false);
                backgroundMusic = music.AddComponent<AudioSource>();
                backgroundMusic.clip = musicClip;
                backgroundMusic.loop = true; backgroundMusic.playOnAwake = false;
                backgroundMusic.spatialBlend = 0f; backgroundMusic.dopplerLevel = 0f;
                backgroundMusic.volume = backgroundMusicVolume * musicGain;
                backgroundMusic.priority = 192;
            }
        }

        public void Initialize(PlayerController owner, LabDoor[] gates)
        {
            BindDoors(false);
            BindWeapon(false);
            ClearDedicatedSources();
            player = owner; doors = gates;
            weapons = player != null ? player.GetComponent<WeaponController>() : null;
            var camera = player != null ? player.GetComponentInChildren<Camera>() : null;
            if (camera != null && FindAnyObjectByType<AudioListener>() == null)
                camera.gameObject.AddComponent<AudioListener>();
            InitializeDedicatedSources();
            if (isActiveAndEnabled) { BindDoors(true); BindWeapon(true); }
            BindFlightLoops(FindObjectsByType<DroneBrain>());
        }

        private void InitializeDedicatedSources()
        {
            if (doors != null)
                foreach (var door in doors)
                {
                    if (door == null) continue;
                    var child = new GameObject("Door opening audio");
                    child.transform.SetParent(door.transform, false);
                    child.transform.localPosition = Vector3.up * 1.2f;
                    var source = child.AddComponent<AudioSource>();
                    source.clip = doorOpen; source.playOnAwake = false;
                    source.spatialBlend = 1f; source.volume = doorVolume * effectsGain; source.dopplerLevel = 0f;
                    source.minDistance = 8f; source.maxDistance = 50f;
                    source.rolloffMode = AudioRolloffMode.Linear; source.priority = 32;
                    doorSources.Add(door, source);
                }
            if (player != null)
            {
                var child = new GameObject("Player reload audio");
                child.transform.SetParent(player.transform, false);
                reloadSource = child.AddComponent<AudioSource>();
                reloadSource.clip = reloadClip; reloadSource.playOnAwake = false;
                reloadSource.spatialBlend = 0f; reloadSource.volume = reloadVolume * effectsGain;
                reloadSource.dopplerLevel = 0f; reloadSource.priority = 64;
            }
        }

        private void ClearDedicatedSources()
        {
            foreach (var source in doorSources.Values)
                if (source != null) { source.Stop(); Destroy(source.gameObject); }
            doorSources.Clear();
            if (reloadSource != null) { reloadSource.Stop(); Destroy(reloadSource.gameObject); }
            reloadSource = null;
        }

        private void BindWeapon(bool bind)
        {
            if (weapons == null) return;
            weapons.ReloadStateChanged -= OnReloadStateChanged;
            if (bind) weapons.ReloadStateChanged += OnReloadStateChanged;
        }

        private void OnReloadStateChanged(WeaponReloadState state)
        {
            if (reloadSource == null) return;
            if (state != WeaponReloadState.Reloading) { reloadSource.Stop(); return; }
            if (reloadClip == null) return;
            reloadSource.Play();
            ReloadsPlayed++;
        }

        internal void BindFlightLoops(DroneBrain[] drones)
        {
            ClearFlightLoops();
            if (droneFlight == null) return;
            var loops = new FlightLoop[drones.Length];
            for (int i = 0; i < drones.Length; i++)
            {
                var drone = drones[i];
                if (drone == null) continue;
                var loop = new GameObject("Drone flight loop");
                loop.transform.SetParent(drone.transform, false);
                var source = loop.AddComponent<AudioSource>();
                DroneRotorVisuals visual = null;
                if (drone is InterceptorBrain)
                {
                    advisory ??= Resources.Load<AudioClip>("Audio/Halls/Advisory");
                    relay ??= Resources.Load<AudioClip>("Audio/Halls/Relay");
                    visual = drone.GetComponentInChildren<DroneRotorVisuals>();
                    if (visual != null) visual.AudioBus = this;
                }
                source.clip = droneFlight; source.loop = true; source.playOnAwake = false;
                source.spatialBlend = 1f; source.volume = droneFlightVolume * effectsGain; source.dopplerLevel = 0f;
                source.minDistance = 4f; source.maxDistance = 28f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                loops[i] = new FlightLoop { Source = source, Health = drone.GetComponent<Health>(), InterceptorVisuals = visual };
            }
            flightLoops = loops;
        }

        private void Update()
        {
            if (AudioListener.pause) return;
            bool ended = GameSession.Instance != null && GameSession.Instance.Ended;
            foreach (var loop in flightLoops)
            {
                var source = loop.Source;
                if (source == null) continue;
                bool flying = !ended && source.gameObject.activeInHierarchy &&
                    loop.Health != null && loop.Health.IsAlive;
                if (flying && loop.InterceptorVisuals != null)
                    source.pitch = Mathf.Lerp(source.pitch, .91f + .22f * Mathf.Clamp01(loop.InterceptorVisuals.MovementSpeed / 7f),
                        1f - Mathf.Exp(-4f * Time.deltaTime));
                if (flying && !source.isPlaying) source.Play();
                else if (!flying && source.isPlaying) source.Stop();
            }
        }

        private void ClearFlightLoops()
        {
            foreach (var loop in flightLoops)
            {
                if (loop.Source != null) { loop.Source.Stop(); Destroy(loop.Source.gameObject); }
                if (loop.InterceptorVisuals != null) loop.InterceptorVisuals.AudioBus = null;
            }
            flightLoops = System.Array.Empty<FlightLoop>();
        }

        private void OnDestroy()
        {
            BindWeapon(false);
            ClearDedicatedSources();
            ClearFlightLoops();
        }

        private void OnEnable()
        {
            flightLoops ??= System.Array.Empty<FlightLoop>();
            SoundEvents.NoiseEmitted += OnNoise; BindDoors(true); BindWeapon(true);
            if (backgroundMusic != null && !backgroundMusic.isPlaying) backgroundMusic.Play();
        }
        private void OnDisable()
        {
            SoundEvents.NoiseEmitted -= OnNoise; BindDoors(false); BindWeapon(false);
            if (voices != null) foreach (var voice in voices) if (voice != null) voice.Stop();
            foreach (var loop in flightLoops)
                if (loop.Source != null) loop.Source.Stop();
            if (backgroundMusic != null) backgroundMusic.Stop();
            if (reloadSource != null) reloadSource.Stop();
            foreach (var source in doorSources.Values) if (source != null) source.Stop();
        }
        private void BindDoors(bool bind)
        {
            if (doors == null) return;
            foreach (var door in doors)
            {
                if (door == null) continue;
                door.OpeningStarted -= OnDoor;
                if (bind) door.OpeningStarted += OnDoor;
            }
        }
        private void OnNoise(NoiseEvent noise)
        {
            if (noise.Category != NoiseCategory.Gunshot) return;
            bool drone = noise.Source is DroneBrain;
            bool local = !drone && player != null && (noise.Position - player.transform.position).sqrMagnitude < .04f;
            if (!Play(drone ? droneShot : playerShot, noise.Position, drone ? droneShotVolume : playerShotVolume, !local)) return;
            if (drone) DroneShotsPlayed++; else PlayerShotsPlayed++;
        }
        internal void PreviewSettingsMusic(bool enabled)
        {
            if (backgroundMusic != null) backgroundMusic.ignoreListenerPause = enabled;
        }

        internal void SetCategoryVolumes(float music, float effects)
        {
            musicGain = Mathf.Clamp01(music);
            effectsGain = Mathf.Clamp01(effects);
            if (backgroundMusic != null) backgroundMusic.volume = backgroundMusicVolume * musicGain;
            if (reloadSource != null) reloadSource.volume = reloadVolume * effectsGain;
            foreach (var source in doorSources.Values)
                if (source != null) source.volume = doorVolume * effectsGain;
            foreach (var loop in flightLoops)
                if (loop.Source != null) loop.Source.volume = droneFlightVolume * effectsGain;
            if (voices != null)
                for (int i = 0; i < voices.Length; i++) voices[i].volume = voiceLevels[i] * effectsGain;
        }

        internal void InterceptorAlert(Vector3 point) => Play(advisory, point, .21f, true, 1.18f);
        internal void InterceptorFailure(Vector3 point) => Play(relay, point, .22f, true, .78f);
        internal void InterceptorImpact(Vector3 point) => Play(relay, point, .36f, true, .62f);

        internal void PlayOutdoorDoor(Vector3 position)
        {
            if (Play(doorOpen, position, doorVolume, true)) DoorsPlayed++;
        }

        private void OnDoor(LabDoor door)
        {
            if (!doorSources.TryGetValue(door, out var source) || source == null || source.clip == null) return;
            // A dedicated voice keeps the supplied recording audible through busy firefights.
            source.Play();
            DoorsPlayed++;
        }
        private bool Play(AudioClip clip, Vector3 position, float volume, bool spatial, float pitch = 1f)
        {
            if (clip == null || voices == null) return false;
            // Prefer an idle voice; replace the oldest round-robin voice only when all are busy.
            int slot = nextVoice;
            for (int i = 0; i < voices.Length; i++)
            {
                int candidate = (nextVoice + i) % voices.Length;
                if (!voices[candidate].isPlaying) { slot = candidate; break; }
            }
            var voice = voices[slot]; nextVoice = (slot + 1) % voices.Length;
            voice.Stop(); voice.transform.position = position;
            voiceLevels[slot] = volume;
            voice.clip = clip; voice.volume = volume * effectsGain; voice.pitch = pitch;
            voice.spatialBlend = spatial ? 1f : 0f; voice.Play();
            return true;
        }
    }
}
