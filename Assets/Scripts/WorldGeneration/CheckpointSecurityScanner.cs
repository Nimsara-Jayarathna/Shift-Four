using UnityEngine;

namespace ShiftFour
{
    // Optional task state belongs to this Checkpoint instance.
    public sealed class CheckpointSecurityScanner : MonoBehaviour, IInteractable
    {
        private enum Stage { FindCard, ActivateScanner, Complete }

        public const string CompletionMessage = "CHECKPOINT MISSION COMPLETE";
        private const string AccessGranted = "ACCESS GRANTED";
        private const string CardRequired = "SECURITY CARD REQUIRED";
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        [SerializeField] private SecurityAccessCard card;
        [SerializeField] private Renderer indicator;
        [SerializeField] private Light indicatorLight;
        [SerializeField] private TextMesh status;
        private Stage stage;
        private float deniedUntil;
        private MaterialPropertyBlock indicatorProperties;

        public string Prompt => stage == Stage.Complete ? AccessGranted : "E: Activate Security Scanner";
        public string Objective => stage switch
        {
            Stage.ActivateScanner => "Activate Security Scanner",
            Stage.Complete => CompletionMessage,
            _ => "Find the Security Access Card"
        };
        public string Hint => stage switch
        {
            Stage.ActivateScanner => "Reader on the right as you enter Checkpoint",
            Stage.Complete => AccessGranted,
            _ => "On the security desk inside Checkpoint"
        };
        public bool IsComplete => stage == Stage.Complete;
        public string Notice => Time.unscaledTime < deniedUntil ? CardRequired : string.Empty;

        internal void Configure(SecurityAccessCard accessCard, Renderer lamp, Light fill, TextMesh display)
        {
            card = accessCard;
            indicator = lamp;
            indicatorLight = fill;
            status = display;
            accessCard.Configure(this);
            UpdateDisplay();
        }

        private void Awake() => UpdateDisplay();

        internal bool TryCollect(SecurityAccessCard pickup)
        {
            if (stage != Stage.FindCard || pickup != card || !isActiveAndEnabled) return false;
            stage = Stage.ActivateScanner;
            deniedUntil = 0;
            return true;
        }

        public void Interact()
        {
            if (!isActiveAndEnabled || stage == Stage.Complete) return;
            if (stage == Stage.FindCard)
            {
                deniedUntil = Time.unscaledTime + 2.5f;
                return;
            }

            stage = Stage.Complete;
            UpdateDisplay();
            Debug.Log(CompletionMessage, this);
        }

        internal void ResetMission()
        {
            stage = Stage.FindCard;
            deniedUntil = 0;
            if (card != null) card.gameObject.SetActive(true);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            bool granted = stage == Stage.Complete;
            var color = granted ? new Color(.12f, 1f, .25f) : new Color(1f, .05f, .04f);
            if (indicator != null)
            {
                indicatorProperties ??= new MaterialPropertyBlock();
                indicator.GetPropertyBlock(indicatorProperties);
                indicatorProperties.SetColor(BaseColor, color);
                indicator.SetPropertyBlock(indicatorProperties);
            }
            if (indicatorLight != null) indicatorLight.color = color;
            if (status != null) status.text = granted ? AccessGranted + "\n" + CompletionMessage : "ACCESS DENIED";
        }
    }
}
