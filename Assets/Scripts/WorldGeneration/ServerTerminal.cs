using UnityEngine;

namespace ShiftFour
{
    [DisallowMultipleComponent]
    public sealed class ServerTerminal : MonoBehaviour, IInteractable
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color OfflineColor = new Color(1f, .15f, .12f);
        internal static readonly Color OnlineColor = new Color(.18f, 1f, .37f);

        [SerializeField] private ServerNetworkPuzzle puzzle;
        [SerializeField] private int number;
        [SerializeField] private Renderer indicator;
        [SerializeField] private TextMesh status;
        private MaterialPropertyBlock indicatorProperties;

        internal int Number => number;
        public bool IsOnline { get; private set; }
        public string Prompt => IsOnline ? $"SERVER {number} ONLINE" : $"E: Activate SERVER {number}";

        internal void Configure(ServerNetworkPuzzle owner, int terminalNumber, Renderer lamp, TextMesh display)
        {
            puzzle = owner;
            number = terminalNumber;
            indicator = lamp;
            status = display;
            SetOnline(false);
        }

        public void Interact()
        {
            if (isActiveAndEnabled && puzzle != null) puzzle.Activate(this);
        }

        internal void SetOnline(bool online)
        {
            IsOnline = online;
            var color = online ? OnlineColor : OfflineColor;
            if (indicator != null)
            {
                indicatorProperties ??= new MaterialPropertyBlock();
                indicator.GetPropertyBlock(indicatorProperties);
                indicatorProperties.SetColor(BaseColor, color);
                indicator.SetPropertyBlock(indicatorProperties);
            }
            if (status != null) status.text = online ? "ONLINE" : "OFFLINE";
        }
    }
}
