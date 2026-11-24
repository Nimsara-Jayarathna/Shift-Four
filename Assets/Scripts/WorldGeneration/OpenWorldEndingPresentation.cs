using System.Collections.Generic;
using UnityEngine;

namespace ShiftFour
{
    // Owned by the outdoor menu for one extraction. All close shots use the gate's reserved approach.
    internal sealed class OpenWorldEndingPresentation
    {
        internal float Elapsed { get; private set; }
        private readonly Camera camera;
        private readonly OpenWorldExtractionGate gate;
        private readonly Vector3 localEye, firstPosition, pushPosition, facilityCenter, overviewOffset;
        private readonly Quaternion localRotation, firstRotation;
        private readonly float fov, farClip, side, fogDensity, aerialFog, effectsVolume;
        private bool aerialAtmosphere;
        private readonly int mask;
        private readonly AudioSource effects, score;
        private readonly AudioClip motor;

        internal OpenWorldEndingPresentation(Camera view, PlayerController player, OpenWorldExtractionGate gate,
            OutdoorSettings.Values settings)
        {
            camera = view;
            this.gate = gate;
            localEye = view.transform.localPosition;
            localRotation = view.transform.localRotation;
            firstPosition = view.transform.position;
            firstRotation = view.transform.rotation;
            mask = view.cullingMask;
            fov = view.fieldOfView;
            farClip = view.farClipPlane;
            fogDensity = RenderSettings.fogDensity;
            aerialFog = fogDensity * .38f;
            view.cullingMask &= ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
            side = gate.transform.InverseTransformPoint(firstPosition).x < 0f ? -1f : 1f;
            Vector3 target = At(side * 4.85f, 3.48f, -.85f);
            Vector3 direction = (target - firstPosition).normalized;
            float travel = Mathf.Min(.75f, Vector3.Distance(firstPosition, target) * .2f);
            foreach (var hit in Physics.SphereCastAll(firstPosition, .22f, direction, travel,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(player.transform))
                    travel = Mathf.Min(travel, Mathf.Max(0f, hit.distance - .04f));
            pushPosition = firstPosition + direction * travel;

            FrameFacility(gate.transform.parent, gate.transform, view, out facilityCenter, out overviewOffset);
            effectsVolume = settings.Effects * .005f;
            effects = Voice("Extraction mechanism audio", effectsVolume);
            score = Voice("Extraction score", settings.Music * .0042f);
            var authorization = Resources.Load<AudioClip>("Audio/Halls/Authenticate");
            motor = Resources.Load<AudioClip>("Audio/LabDoorOpen");
            if (authorization != null) effects.PlayOneShot(authorization, .72f);
            score.clip = Resources.Load<AudioClip>("Audio/ExtractionCoda");
            if (score.clip != null) score.Play();
        }

        private const float OverviewFov = 51f;
        private Vector3 At(float x, float y, float z) => gate.transform.TransformPoint(new Vector3(x, y, z));
        private static float Ease(float t) => Mathf.SmoothStep(0, 1, t);

        private static void FrameFacility(Transform layout, Transform gate, Camera camera, out Vector3 center,
            out Vector3 offsetFromCenter)
        {
            // Fit the four generated buildings once, including antennas above their roof caps.
            var ground = layout.Find("Ground")?.GetComponent<Collider>();
            var buildings = layout.Find("Buildings");
            var bounds = ground != null ? ground.bounds : new Bounds(layout.position, Vector3.zero);
            Vector3 back = (gate.forward + gate.right * .12f + Vector3.up * 1.55f).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, -back).normalized, up = Vector3.Cross(-back, right);
            float tanV = Mathf.Tan(OverviewFov * Mathf.Deg2Rad * .5f) * .84f; // inside the letterbox
            float tanH = Mathf.Tan(OverviewFov * Mathf.Deg2Rad * .5f) * Mathf.Max(.1f, camera.aspect) * .94f;
            Vector2 lower = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 upper = new(float.NegativeInfinity, float.NegativeInfinity);
            // Centre the projected silhouettes, including perspective. An AABB midpoint alone
            // leaves a large strip of empty horizon behind the northern roofs.
            var fixtures = new List<Renderer>();
            if (buildings != null)
                foreach (Transform site in buildings)
                {
                    var roof = site.Find("Roof cap")?.GetComponent<Renderer>();
                    Bounds hull = roof != null ? roof.bounds : new Bounds(site.position, Vector3.one);
                    hull.Encapsulate(site.position);
                    var exterior = site.Find("ExteriorDetails");
                    if (exterior != null)
                    {
                        exterior.GetComponentsInChildren(false, fixtures);
                        foreach (var fixture in fixtures)
                            if (fixture.enabled) hull.Encapsulate(fixture.bounds);
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 p = hull.center - bounds.center + Vector3.Scale(hull.extents, new Vector3(
                            (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector2 xy = new(Vector3.Dot(p, right), Vector3.Dot(p, up));
                        Vector2 perspective = Vector3.Dot(p, back) * new Vector2(tanH, tanV);
                        upper = Vector2.Max(upper, xy + perspective);
                        lower = Vector2.Min(lower, xy - perspective);
                    }
                }
            Vector2 offset = buildings != null && buildings.childCount > 0 ? (upper + lower) * .5f : Vector2.zero;
            Vector2 span = (upper - lower) * .5f;
            float distance = Mathf.Max(32f, Mathf.Max(span.x / tanH, span.y / tanV));
            center = bounds.center + right * offset.x + up * offset.y;
            offsetFromCenter = back * distance;
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, distance * 1.06f + bounds.size.magnitude);
        }

        private AudioSource Voice(string name, float volume)
        {
            var go = new GameObject(name);
            go.transform.SetParent(gate.transform, false);
            var voice = go.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.ignoreListenerPause = true;
            voice.volume = volume;
            return voice;
        }

        internal bool Advance(float delta)
        {
            if (camera == null || gate == null || !gate.isActiveAndEnabled) return true;
            float previous = Elapsed;
            Elapsed = Mathf.Min(12f, Elapsed + delta);
            float t = Elapsed;
            if (previous < 2f && t >= 2f && motor != null)
            {
                effects.pitch = .83f;
                effects.PlayOneShot(motor);
            }
            if (t > 4.55f && previous < 5.3f)
                effects.volume = effectsVolume * (1f - Ease((t - 4.55f) / .75f));
            gate.SetOpening(Ease((t - 2f) / 2.8f));
            Vector3 position, target;
            if (t < 2f)
            {
                position = Vector3.Lerp(firstPosition, pushPosition, Ease(t / 2f));
                target = At(side * 4.85f, 3.48f, -.85f);
                camera.transform.SetPositionAndRotation(position,
                    Quaternion.Slerp(firstRotation, Quaternion.LookRotation(target - position), Ease(t / 1.2f)));
                return false;
            }
            if (t < 5f)
            {
                float u = Ease((t - 2f) / 3f);
                position = Vector3.Lerp(At(side * 2.9f, 1.35f, -9.6f), At(side * 1.25f, 1.55f, -8.2f), u);
                target = At(0, Mathf.Lerp(2.65f, 2.3f, u), .4f);
            }
            else if (t < 8f)
            {
                float u = Ease((t - 5f) / 3f);
                position = Vector3.Lerp(At(side * 1.25f, 1.55f, -8.2f), At(-side * .65f, 1.85f, .8f), u);
                target = Vector3.Lerp(At(0, 2.3f, .4f), At(-side * 2f, 4.5f, 22), u);
            }
            else
            {
                float u = Ease((t - 8f) / 3.8f);
                // The ground-level haze otherwise obscures the far buildings at this viewing distance.
                if (!aerialAtmosphere)
                {
                    RenderSettings.fogDensity = aerialFog;
                    aerialAtmosphere = true;
                }
                position = facilityCenter + overviewOffset * Mathf.Lerp(1.055f, 1f, u);
                target = facilityCenter;
            }
            camera.fieldOfView = t >= 8f ? OverviewFov : Mathf.Lerp(63f, 57f, Ease((t - 2f) / 6f));
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            return t >= 12f;
        }

        internal void Dispose()
        {
            if (aerialAtmosphere && Mathf.Approximately(RenderSettings.fogDensity, aerialFog))
                RenderSettings.fogDensity = fogDensity;
            if (gate != null) gate.SetOpening(1f);
            if (camera != null)
            {
                camera.transform.SetLocalPositionAndRotation(localEye, localRotation);
                camera.cullingMask = mask;
                camera.fieldOfView = fov;
                camera.farClipPlane = farClip;
            }
            Stop(effects);
            Stop(score);
        }

        private static void Stop(AudioSource voice)
        {
            if (voice == null) return;
            voice.Stop();
            Object.Destroy(voice.gameObject);
        }
    }
}
