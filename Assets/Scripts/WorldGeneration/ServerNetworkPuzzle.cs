using System.Collections;
using UnityEngine;

namespace ShiftFour
{
    // This state belongs to the local Server room and never touches the game's gate progression.
    [DisallowMultipleComponent]
    public sealed class ServerNetworkPuzzle : MonoBehaviour, IInteractable
    {
        private static readonly int[] ActivationOrder = { 2, 1, 3 };
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color WarningColor = new Color(1f, .29f, .18f);
        private static readonly Color DisplayColor = new Color(.9f, .95f, .94f);
        private const string ErrorMessage = "INCORRECT SEQUENCE";
        internal const string CompletionTitle = "NETWORK RESTORED";
        internal const string CompletionDetail = "SERVER MISSION COMPLETE";
        internal const string CompletionMessage = CompletionTitle + " - " + CompletionDetail;
        internal static readonly string SequenceText = string.Join(" -> ", ActivationOrder);

        [SerializeField] private ServerTerminal[] terminals;
        [SerializeField] private TextMesh display;
        private Renderer displayRenderer;
        private MaterialPropertyBlock displayProperties;
        private int completed;
        private Coroutine warning;
        private float instructionsUntil;

        internal int OnlineCount => completed;
        internal int TerminalCount => ActivationOrder.Length;
        public bool IsComplete => completed == ActivationOrder.Length;
        internal event System.Action<ServerNetworkPuzzle> Completed;
        internal event System.Action<ServerTerminal> TerminalActivated;
        internal event System.Action<ServerTerminal> SequenceRejected;
        public string Objective => $"Restore Server Network ({completed}/{ActivationOrder.Length})";
        public string Prompt => "E: Read Server Sequence";
        public bool HasWarning => warning != null;
        public string Notice => HasWarning ? ErrorMessage :
            Time.unscaledTime < instructionsUntil ? "ACTIVATE IN ORDER  " + SequenceText : string.Empty;

        internal void Configure(ServerTerminal[] stations, TextMesh status)
        {
            terminals = stations;
            display = status;
            ResetPuzzle();
        }

        private void Awake() => ResetPuzzle();

        private void OnDisable()
        {
            ClearNotice();
            UpdateDisplay();
        }

        public void Interact()
        {
            if (isActiveAndEnabled && !IsComplete && !HasWarning)
                instructionsUntil = Time.unscaledTime + 3f;
        }

        internal void Activate(ServerTerminal terminal)
        {
            if (!isActiveAndEnabled || IsComplete || terminal.IsOnline) return;

            if (terminal.Number != ActivationOrder[completed])
            {
                ResetPuzzle();
                warning = StartCoroutine(FlashWarning());
                SequenceRejected?.Invoke(terminal);
                return;
            }

            ClearNotice();
            completed++;
            terminal.SetOnline(true);
            UpdateDisplay();
            TerminalActivated?.Invoke(terminal);
            if (IsComplete)
            {
                Completed?.Invoke(this);
                Debug.Log(CompletionMessage, this);
            }
        }

        internal void ResetPuzzle()
        {
            ClearNotice();
            completed = 0;
            if (terminals != null)
                foreach (var terminal in terminals)
                    if (terminal != null) terminal.SetOnline(false);
            UpdateDisplay();
        }

        private void ClearNotice()
        {
            if (warning != null) StopCoroutine(warning);
            warning = null;
            instructionsUntil = 0f;
        }

        private IEnumerator FlashWarning()
        {
            if (display != null) display.text = ErrorMessage;
            var interval = new WaitForSecondsRealtime(.19f);
            for (int i = 0; i < 6; i++)
            {
                SetDisplayColor(i % 2 == 0 ? WarningColor : DisplayColor);
                yield return interval;
            }
            SetDisplayColor(WarningColor);
            yield return new WaitForSecondsRealtime(1f);
            warning = null;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (display == null) return;
            display.text = IsComplete ? CompletionTitle + "\n" + CompletionDetail :
                $"{completed} / {ActivationOrder.Length} ONLINE";
            SetDisplayColor(IsComplete ? ServerTerminal.OnlineColor : DisplayColor);
        }

        private void SetDisplayColor(Color color)
        {
            if (display == null) return;
            if (displayRenderer == null) displayRenderer = display.GetComponent<Renderer>();
            if (displayRenderer == null) return;
            displayProperties ??= new MaterialPropertyBlock();
            // Keep the dynamic font atlas already bound by PrototypeLabel.
            displayRenderer.GetPropertyBlock(displayProperties);
            displayProperties.SetColor(BaseColor, color);
            displayRenderer.SetPropertyBlock(displayProperties);
        }
    }
}
