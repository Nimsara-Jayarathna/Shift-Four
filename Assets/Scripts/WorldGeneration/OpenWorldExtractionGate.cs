using UnityEngine;
using static ShiftFour.PrototypeGeometry;

namespace ShiftFour
{
    [DisallowMultipleComponent]
    public sealed class OpenWorldExtractionGate : MonoBehaviour, IInteractable
    {
        internal const float HalfWidth = 5.5f;
        public const string Locked = "EXTRACTION LOCKED";
        public const string Available = "EXTRACTION AVAILABLE";
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color LockedColor = new Color(1f, .12f, .09f);
        private static readonly Color ReadyColor = new Color(.15f, 1f, .38f);

        [SerializeField] private OpenWorldMissionManager mission;
        [SerializeField] private Transform shutter;
        [SerializeField] private Renderer[] lamps;
        [SerializeField] private Renderer display;
        [SerializeField] private TextMesh status;
        [SerializeField] private Light[] glows;
        private MaterialPropertyBlock tint;
        public string Status { get; private set; }
        public string Prompt => mission != null && mission.Escaped ? "FACILITY ESCAPED" :
            mission != null && mission.IsReady ? "E: Extract from the facility" : "E: Check extraction requirements";

        internal static void Build(Transform world, float south, PrototypeMaterials materials, Font font)
        {
            Transform root = Group("Final Extraction Gate", world, new Vector3(0f, 0f, south));
            root.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var lamps = new Renderer[2];
            var glows = new Light[2];
            Material signal = materials.CheckpointSignal != null ? materials.CheckpointSignal : materials.Marking;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 5f;
                int index = (side + 1) / 2;
                Box("Gate pillar", root, new Vector3(x, 2.45f, 0f), new Vector3(1.1f, 4.9f, 1.5f), materials.Concrete);
                Box("Pillar stripe", root, new Vector3(x, 2.45f, -.77f),
                    new Vector3(.32f, 4.75f, .05f), materials.Storage, false);
                lamps[index] = Box("Extraction warning indicator", root, new Vector3(x, 3.5f, -.9f),
                    new Vector3(.62f, .56f, .18f), signal, false).GetComponent<Renderer>();
                var glow = Group("Extraction warning light", root, new Vector3(x, 3.5f, -1.08f)).gameObject.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.range = 2.4f;
                glow.intensity = 3f;
                glow.shadows = LightShadows.None;
                glows[index] = glow;
            }

            Box("Gate header", root, new Vector3(0f, 4.65f, 0f), new Vector3(11.1f, .92f, 1.35f), materials.Metal);
            Label("FINAL EXTRACTION", root, new Vector3(0f, 4.65f, -.69f), .47f, font, 10.2f, .65f, materials.Lettering);
            Transform shutter = Group("Extraction shutter", root);
            Box("Shutter body", shutter, new Vector3(0f, 2f, 0f), new Vector3(9f, 4f, .48f), materials.Metal);
            for (int y = 1; y <= 3; y++)
                Box("Shutter rib", shutter, new Vector3(0f, y * .74f, -.27f), new Vector3(8.8f, .13f, .10f), materials.Concrete, false);
            Box("Status panel", shutter, new Vector3(0f, 2.9f, -.28f), new Vector3(6.8f, .85f, .12f), materials.Glass, false);
            TextMesh status = Label(Available, shutter, new Vector3(0f, 2.9f, -.35f),
                .31f, font, 6.2f, .65f, materials.Lettering);

            var manager = root.gameObject.AddComponent<OpenWorldMissionManager>();
            var gate = root.gameObject.AddComponent<OpenWorldExtractionGate>();
            gate.mission = manager;
            gate.shutter = shutter;
            gate.lamps = lamps;
            gate.status = status;
            gate.display = status.GetComponent<Renderer>();
            gate.glows = glows;
            manager.BindGate(gate);
            gate.Refresh();
        }

        public void Interact()
        {
            if (isActiveAndEnabled && mission != null) mission.TryEscape();
        }

        private void OnEnable() => Refresh();
        private void LateUpdate() => Refresh();

        internal void Refresh()
        {
            if (mission == null) return;
            string next = mission.Escaped ? "FACILITY ESCAPED" : mission.IsReady ? Available : Locked;
            if (Status == next) return;
            Status = next;
            status.text = next;
            Color color = mission.IsReady ? ReadyColor : LockedColor;
            foreach (Renderer lamp in lamps) SetTint(lamp, color);
            SetTint(display, color);
            foreach (Light glow in glows) glow.color = color;
            if (mission.Escaped)
                shutter.localPosition = Vector3.up * 4f;
        }

        private void SetTint(Renderer target, Color color)
        {
            tint ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(tint);
            tint.SetColor(BaseColor, color);
            target.SetPropertyBlock(tint);
        }
    }
}
