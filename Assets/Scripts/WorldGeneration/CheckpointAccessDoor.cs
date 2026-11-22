using System.Collections;
using UnityEngine;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    internal sealed class CheckpointAccessDoor : MonoBehaviour, IInteractable
    {
        private const string KeyRequired = "CHECKPOINT KEY REQUIRED";
        private const string Granted = "ACCESS GRANTED";
        private OutdoorPlayerEquipment equipment;
        private Transform left, right;
        private Renderer indicator;
        private BoxCollider barrier;
        private MaterialPropertyBlock tint;
        private bool unlocking;
        private float noticeUntil;

        internal bool IsOpen { get; private set; }
        internal string Notice
        {
            get
            {
                if (Time.unscaledTime >= noticeUntil) return string.Empty;
                if (unlocking || IsOpen) return Granted;
                return equipment != null && equipment.HasCheckpointKey ? string.Empty : KeyRequired;
            }
        }
        public string Prompt => unlocking || IsOpen ? Granted : "E: UNLOCK CHECKPOINT DOOR";

        internal static void Build(Transform checkpoint, OutdoorPlayerEquipment owner, PrototypeMaterials m, Font font)
        {
            float front = -ResearchBuildingBuilder.FrontOffset(0);
            // Match the opening in ResearchBuildingBuilder, from floor level to the original 4 m header.
            Transform root = Group("Checkpoint Access Door", checkpoint, new Vector3(0, ResearchBuildingBuilder.FloorHeight, front - .08f));
            var door = root.gameObject.AddComponent<CheckpointAccessDoor>();
            door.equipment = owner;
            door.barrier = root.GetComponent<BoxCollider>();
            door.barrier.center = Vector3.up * 1.9f;
            door.barrier.size = new Vector3(4.06f, 3.8f, .29f);
            door.left = BuildPanel(root, -.995f, m);
            door.right = BuildPanel(root, .995f, m);

            Material signal = m.CheckpointSignal != null ? m.CheckpointSignal : m.Marking;
            // Give the reader its own raycast target beside the moving shutter and its barrier.
            Box("Lock reader housing", root, new Vector3(2.77f, 1.59f, -1.30f), new Vector3(.49f, .65f, .18f), m.Metal);
            door.indicator = Box("Door lock indicator", root, new Vector3(2.77f, 1.72f, -1.41f),
                new Vector3(.25f, .28f, .08f), signal, false).GetComponent<Renderer>();
            Label("ACCESS", root, new Vector3(2.77f, 1.44f, -1.41f), .093f, font, .42f, .10f, m.Lettering);
            Label("CHECKPOINT KEY", door.left, new Vector3(0, 2.39f, -.213f), .14f, font, 1.64f, .22f, m.Lettering);
            door.SetIndicator(new Color(1f, .095f, .075f));
        }

        private static Transform BuildPanel(Transform root, float x, PrototypeMaterials m)
        {
            Transform panel = Group(x < 0 ? "Left door panel" : "Right door panel", root, Vector3.right * x);
            Box("Armored shutter", panel, Vector3.up * 1.9f, new Vector3(2f, 3.79f, .27f), m.Metal, false);
            Box("Inset steel plate", panel, new Vector3(0, 2.01f, -.151f), new Vector3(1.72f, 2.95f, .052f), m.Concrete, false);
            for (int rib = 0; rib < 4; rib++)
                Box("Shutter rib", panel, new Vector3(0, .51f + rib * .84f, -.184f),
                    new Vector3(1.77f, .105f, .062f), m.Metal, false);
            Box("Safety marking", panel, new Vector3(0, 1.05f, -.216f),
                new Vector3(1.58f, .15f, .045f), m.Checkpoint, false);
            return panel;
        }

        public void Interact()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || unlocking || IsOpen) return;
            noticeUntil = Time.unscaledTime + 3.5f;
            if (equipment == null || !equipment.HasCheckpointKey) return;
            unlocking = true;
            equipment.GetComponent<GameplayAudio>()?.PlayOutdoorDoor(transform.position);
            SetIndicator(new Color(.16f, 1f, .36f));
            StartCoroutine(Open());
        }

        private void SetIndicator(Color color)
        {
            tint ??= new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", color);
            indicator.SetPropertyBlock(tint);
        }

        private IEnumerator Open()
        {
            const float duration = .62f;
            float start = Time.unscaledTime;
            Vector3 leftStart = left.localPosition;
            Vector3 rightStart = right.localPosition;
            while (Time.unscaledTime - start < duration)
            {
                float slide = Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - start) / duration) * 2.08f;
                left.localPosition = leftStart + Vector3.left * slide;
                right.localPosition = rightStart + Vector3.right * slide;
                yield return null;
            }
            left.localPosition = leftStart + Vector3.left * 2.08f;
            right.localPosition = rightStart + Vector3.right * 2.08f;
            barrier.enabled = false;
            IsOpen = true;
            unlocking = false;
        }
    }
}
