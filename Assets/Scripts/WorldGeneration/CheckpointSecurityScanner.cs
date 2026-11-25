using System.Collections;
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
        private const float ScanDuration = 1.25f;
        private float scanStarted;
        private Coroutine verification;
        internal bool IsVerifying => verification != null;
        internal float ScanProgress => Mathf.Clamp01((Time.time - scanStarted) / ScanDuration);
        internal event System.Action<CheckpointSecurityScanner> VerificationFinished;
        private float deniedUntil;
        private MaterialPropertyBlock indicatorProperties;

        public string Prompt => IsVerifying ? "AUTHENTICATION ACTIVE" : stage == Stage.Complete ? AccessGranted : HasCard ? "E: Authenticate security card" : CardRequired;
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
        internal event System.Action<CheckpointSecurityScanner> Completed;
        internal event System.Action Denied;
        internal event System.Action<SecurityAccessCard> CardCollected;
        internal bool HasCard => stage != Stage.FindCard;
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
            UpdateDisplay();
            deniedUntil = 0;
            CardCollected?.Invoke(pickup);
            return true;
        }

        public void Interact()
        {
            if (!isActiveAndEnabled || stage == Stage.Complete) return;
            if (stage == Stage.FindCard)
            {
                deniedUntil = Time.unscaledTime + 2.5f;
                Denied?.Invoke();
                return;
            }

            stage = Stage.Complete;
            scanStarted = Time.time;
            verification = StartCoroutine(VerifyCredential());
            UpdateDisplay();
            Completed?.Invoke(this);
            Debug.Log(CompletionMessage, this);
        }

        private IEnumerator VerifyCredential()
        {
            yield return new WaitForSeconds(ScanDuration);
            verification = null;
            UpdateDisplay();
            VerificationFinished?.Invoke(this);
        }

        private void OnDisable()
        {
            if (verification == null) return;
            StopCoroutine(verification);
            verification = null;
            UpdateDisplay();
        }

        internal void ResetMission()
        {
            if (verification != null) StopCoroutine(verification);
            verification = null;
            stage = Stage.FindCard;
            deniedUntil = 0;
            if (card != null) card.gameObject.SetActive(true);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            bool granted = stage == Stage.Complete;
            var color = IsVerifying || (HasCard && !granted) ? new Color(.12f, .84f, .98f) : granted ? new Color(.12f, 1f, .25f) : new Color(1f, .05f, .04f);
            if (indicator != null)
            {
                indicatorProperties ??= new MaterialPropertyBlock();
                indicator.GetPropertyBlock(indicatorProperties);
                indicatorProperties.SetColor(BaseColor, color);
                indicator.SetPropertyBlock(indicatorProperties);
            }
            if (indicatorLight != null) indicatorLight.color = color;
            if (status != null) status.text = IsVerifying ? "AUTHENTICATING" : granted ? AccessGranted + "\n" + CompletionMessage : HasCard ? "CREDENTIAL READY" : "ACCESS DENIED";
        }
    }
}
