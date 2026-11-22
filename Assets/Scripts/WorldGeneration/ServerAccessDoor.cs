using System.Collections;
using UnityEngine;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    internal sealed class ServerAccessDoor : MonoBehaviour, IInteractable
    {
        internal const string KeyRequired = "SERVER ACCESS KEY REQUIRED";
        private const string Granted = "ACCESS GRANTED";

        private OutdoorPlayerEquipment equipment;
        private Transform shutter;
        private BoxCollider barrier;
        private Renderer[] indicators;
        private bool opening;
        private float noticeUntil;

        internal bool IsOpen { get; private set; }
        internal string Notice
        {
            get
            {
                if (Time.unscaledTime >= noticeUntil) return string.Empty;
                if (opening || IsOpen) return Granted;
                return equipment != null && equipment.HasServerKey ? string.Empty : KeyRequired;
            }
        }

        public string Prompt => opening || IsOpen ? Granted : "E: UNLOCK SERVER DOOR";

        internal static void Build(Transform server, OutdoorPlayerEquipment owner, PrototypeMaterials materials, Font font)
        {
            float front = -ResearchBuildingBuilder.FrontOffset(1);
            Transform root = Group("Server Access Door", server,
                new Vector3(0f, ResearchBuildingBuilder.FloorHeight, front - .18f));

            var door = root.gameObject.AddComponent<ServerAccessDoor>();
            door.equipment = owner;
            // Keep the closed doorway sealed during the animation. The moving visual has no colliders.
            door.barrier = root.GetComponent<BoxCollider>();
            door.barrier.center = Vector3.up * 1.9f;
            door.barrier.size = new Vector3(4.06f, 3.8f, .31f);

            door.shutter = Group("Server security shutter", root);
            Box("Armored panel", door.shutter, Vector3.up * 1.9f,
                new Vector3(4.02f, 3.79f, .29f), materials.Metal, false);
            Box("Inset panel", door.shutter, new Vector3(0f, 1.91f, -.16f),
                new Vector3(3.59f, 3.21f, .082f), materials.Concrete, false);
            for (int i = 0; i < 4; i++)
                Box("Horizontal reinforcement", door.shutter, new Vector3(0f, .51f + i * .86f, -.224f),
                    new Vector3(3.66f, .095f, .062f), materials.Metal, false);
            Box("Door plaque", door.shutter, new Vector3(0f, 2.64f, -.235f),
                new Vector3(2.94f, .54f, .052f), materials.Metal, false);
            Label("SERVER ACCESS", door.shutter, new Vector3(0f, 2.64f, -.27f),
                .22f, font, 2.76f, .29f, materials.Lettering);

            Material signal = materials.CheckpointSignal != null ? materials.CheckpointSignal : materials.Marking;
            door.indicators = new Renderer[2];
            for (int i = 0; i < door.indicators.Length; i++)
            {
                float x = i == 0 ? -2.69f : 2.69f;
                // Stand proud of the existing decorative jamb so the lenses remain visible from the approach.
                Box("Server lock housing", root, new Vector3(x, 2.42f, -1.20f),
                    new Vector3(.49f, .54f, .20f), materials.Metal, false);
                door.indicators[i] = Box("Server lock indicator", root, new Vector3(x, 2.42f, -1.33f),
                    new Vector3(.30f, .32f, .074f), signal, false).GetComponent<Renderer>();
            }
            door.SetIndicators(new Color(1f, .095f, .075f));
        }

        public void Interact()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || opening || IsOpen) return;
            noticeUntil = Time.unscaledTime + 3.5f;
            if (equipment == null || !equipment.HasServerKey) return;

            opening = true;
            SetIndicators(new Color(.16f, 1f, .36f));
            StartCoroutine(Open());
        }

        private void SetIndicators(Color color)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            foreach (Renderer indicator in indicators) indicator.SetPropertyBlock(properties);
        }

        private IEnumerator Open()
        {
            const float duration = .66f;
            const float rise = 3.9f;
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < duration)
            {
                float progress = Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - start) / duration);
                shutter.localPosition = Vector3.up * (progress * rise);
                yield return null;
            }

            shutter.localPosition = Vector3.up * rise;
            barrier.enabled = false;
            IsOpen = true;
            opening = false;
        }
    }
}
