using UnityEngine;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    // Room-owned presentation. No triggers, physics queries or gameplay noise events.
    [DisallowMultipleComponent]
    internal sealed class HallAudio : MonoBehaviour
    {
        private static readonly string[] Rooms = { "Checkpoint", "Server", "Control", "Storage" };
        private static readonly string[] Beds = { "Security", "Cooling", "Monitoring", "Ventilation" };
        private static readonly string[] Fixtures = { "Security scanner", "Server rack", "Operations monitor", "Storage shelving" };

        private Transform listener, building;
        private GameplayAudio mix;
        private Bounds interior;
        private CheckpointSecurityScanner scanner;
        private ServerNetworkPuzzle network;
        private ControlLockdownMission lockdown;
        private StoragePowerMission generator;
        private AudioSource ambience, motor, signal;
        private AudioClip authenticate, relay, reject, advisory;
        private float presence, running, signalLevel, nextAlarm, nextReject;
        private float confirmAt = -1f;
        private bool bound, ambiencePlaying, motorPlaying;

        internal static void Bind(Transform buildings, PlayerController actor, CheckpointSecurityScanner scanner,
            ServerNetworkPuzzle network, ControlLockdownMission lockdown, StoragePowerMission generator)
        {
            if (buildings == null || actor == null || actor.ViewCamera == null || !actor.TryGetComponent<GameplayAudio>(out var mix)) return;
            for (int index = 0; index < Rooms.Length; index++)
            {
                Transform building = buildings.Find(Rooms[index]);
                Transform props = building != null ? building.Find("Interior/Props") : null;
                if (props == null) continue;
                HallAudio hall = building.GetComponentInChildren<HallAudio>(true);
                if (hall == null)
                {
                    hall = Group("Hall audio", props.parent).gameObject.AddComponent<HallAudio>();
                    hall.building = building;
                    hall.interior = ResearchBuildingBuilder.InteriorBounds(index);
                    hall.ambience = hall.Source("Equipment ambience", true);
                    Transform fixture = props.Find(Fixtures[index]);
                    hall.ambience.transform.position = fixture != null ? fixture.position + Vector3.up : hall.transform.position;
                    hall.ambience.clip = Clip(Beds[index]);
                    hall.signal = hall.Source("Equipment signal", false);
                    hall.authenticate = Clip("Authenticate");
                    hall.relay = Clip("Relay");
                    if (index <= 1) hall.reject = Clip("Reject");
                    if (index == 2) hall.advisory = Clip("Advisory");
                    if (index == 3 && generator != null)
                    {
                        hall.motor = hall.Source("Generator motor", true);
                        hall.motor.transform.position = generator.transform.position + Vector3.up;
                        hall.motor.clip = Clip("Generator");
                    }
                }
                hall.Unsubscribe();
                hall.listener = actor.ViewCamera.transform;
                hall.mix = mix;
                hall.scanner = index == 0 ? scanner : null;
                hall.network = index == 1 ? network : null;
                hall.lockdown = index == 2 ? lockdown : null;
                hall.generator = index == 3 ? generator : null;
                hall.Subscribe();
            }
        }

        private static AudioClip Clip(string name) => Resources.Load<AudioClip>("Audio/Halls/" + name);

        private AudioSource Source(string label, bool loop)
        {
            var source = Group(label, transform).gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = loop ? 4f : 2f;
            source.maxDistance = loop ? 33f : 24f;
            source.priority = loop ? 180 : 96;
            source.volume = 0f;
            return source;
        }

        private void OnEnable() => Subscribe();

        private void Subscribe()
        {
            if (bound || mix == null || !isActiveAndEnabled) return;
            bound = true;
            if (scanner != null)
            {
                scanner.Completed += VerificationStarted;
                scanner.VerificationFinished += Authenticated;
                scanner.Denied += Denied;
                scanner.CardCollected += CardCollected;
            }
            if (network != null)
            {
                network.TerminalActivated += TerminalActivated;
                network.SequenceRejected += SequenceRejected;
            }
            if (lockdown != null)
            {
                lockdown.Completed += LockdownCleared;
                lockdown.SwitchActivated += SwitchActivated;
            }
            if (generator != null)
            {
                generator.CellCollected += CellCollected;
                generator.Restored += GeneratorStarted;
            }
        }

        private void Unsubscribe()
        {
            if (!bound) return;
            if (scanner != null)
            {
                scanner.Completed -= VerificationStarted;
                scanner.VerificationFinished -= Authenticated;
                scanner.Denied -= Denied;
                scanner.CardCollected -= CardCollected;
            }
            if (network != null)
            {
                network.TerminalActivated -= TerminalActivated;
                network.SequenceRejected -= SequenceRejected;
            }
            if (lockdown != null)
            {
                lockdown.Completed -= LockdownCleared;
                lockdown.SwitchActivated -= SwitchActivated;
            }
            if (generator != null)
            {
                generator.CellCollected -= CellCollected;
                generator.Restored -= GeneratorStarted;
            }
            confirmAt = -1f;
            bound = false;
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (ambience != null) ambience.Stop();
            if (motor != null) motor.Stop();
            if (signal != null) signal.Stop();
            ambiencePlaying = motorPlaying = false;
            presence = running = signalLevel = nextAlarm = nextReject = 0f;
        }

        private float Proximity()
        {
            float distance = Mathf.Sqrt(interior.SqrDistance(building.InverseTransformPoint(listener.position)));
            return Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(distance / 3.5f));
        }

        private void Update()
        {
            if (!bound || listener == null || mix == null) return;
            float gain = mix.EffectsGain;
            if (!AudioListener.pause)
            {
                presence = Mathf.MoveTowards(presence, Proximity(), Time.deltaTime * 1.8f);
                running = Mathf.MoveTowards(running, generator != null && generator.IsComplete ? 1f : 0f, Time.deltaTime * .8f);
                if (confirmAt >= 0f && Time.time >= confirmAt)
                {
                    confirmAt = -1f;
                    if (lockdown != null && lockdown.IsComplete)
                        Signal(authenticate, lockdown.transform.position + Vector3.up * 1.7f, .30f, 1.18f);
                }
                if (lockdown != null && !lockdown.IsComplete)
                {
                    if (presence < .75f) nextAlarm = Time.time + 3f;
                    else if (Time.time >= nextAlarm)
                    {
                        Signal(advisory, lockdown.transform.position + Vector3.up * 2f, .11f);
                        nextAlarm = Time.time + 12f;
                    }
                }
            }
            Loop(ambience, ref ambiencePlaying, gain * presence * (1f - running) * .19f);
            if (motor != null) Loop(motor, ref motorPlaying, gain * presence * running * .24f);
            signal.volume = gain * presence * signalLevel;
        }

        private static void Loop(AudioSource source, ref bool playing, float level)
        {
            source.volume = level;
            bool audible = level > 0f;
            if (AudioListener.pause || playing == audible || source.clip == null) return;
            if (audible) source.Play();
            else source.Stop();
            playing = audible;
        }

        private void Signal(AudioClip clip, Vector3 position, float level, float pitch = 1f)
        {
            if (clip == null || listener == null || AudioListener.pause || Proximity() <= 0f) return;
            signal.transform.position = position;
            signal.clip = clip;
            signal.pitch = pitch;
            signalLevel = level;
            signal.volume = mix.EffectsGain * Proximity() * level;
            signal.Play();
        }

        private void CardCollected(SecurityAccessCard card) => Signal(relay, card.transform.position, .23f);
        private void SwitchActivated(LockdownSecuritySwitch item)
        {
            bool ready = lockdown.ActivatedCount == lockdown.TotalSwitches;
            Signal(ready ? authenticate : relay, item.transform.position + Vector3.up * 1.5f,
                ready ? .32f : .24f, .86f + .1f * lockdown.ActivatedCount);
            nextAlarm = Time.time + 12f;
        }
        private void SequenceRejected(ServerTerminal terminal) => RejectAt(terminal.transform.position);
        private void Denied() => RejectAt(scanner.transform.position);

        private void RejectAt(Vector3 position)
        {
            if (Time.time < nextReject) return;
            nextReject = Time.time + 1.4f;
            Signal(reject, position, .27f);
        }

        private void VerificationStarted(CheckpointSecurityScanner reader) => Signal(relay, reader.transform.position, .26f, 1.18f);
        private void Authenticated(CheckpointSecurityScanner reader) => Signal(authenticate, reader.transform.position, .36f);

        private void TerminalActivated(ServerTerminal terminal)
        {
            bool restored = network.IsComplete;
            Signal(restored ? authenticate : relay, terminal.transform.position + Vector3.up * 1.5f,
                restored ? .36f : .28f, restored ? 1.17f : .9f + .08f * terminal.Number);
        }

        private void LockdownCleared(ControlLockdownMission panel)
        {
            signal.Stop();
            Signal(relay, panel.transform.position + Vector3.up * 1.7f, .26f, .92f);
            confirmAt = Time.time + ControlLockdownMission.VerificationDuration;
        }

        private void CellCollected(PowerCellPickup cell) => Signal(relay, cell.transform.position, .34f, .72f);
        private void GeneratorStarted() => Signal(authenticate, generator.transform.position + Vector3.up, .32f, .68f);
    }
}
