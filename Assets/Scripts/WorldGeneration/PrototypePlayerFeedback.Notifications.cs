using System.Collections.Generic;
using UnityEngine;

namespace ShiftFour
{
    public sealed partial class PrototypePlayerFeedback
    {
        internal enum NoticeKind { Progress, Warning, Completion }

        internal readonly struct MissionNotice
        {
            internal readonly Site Site;
            internal readonly string Title, Detail, Next;
            internal readonly NoticeKind Kind;

            internal MissionNotice(Site site, string title, string detail, NoticeKind kind, string next)
            {
                Site = site;
                Title = title;
                Detail = detail;
                Kind = kind;
                Next = next;
            }
        }

        private readonly List<MissionNotice> notices = new(6);
        private const float NoticeDuration = 2.8f;
        private float noticeAge;
        private bool noticesBound;
        internal MissionNotice ActiveNotice => notices.Count > 0 ? notices[0] : default;
        internal float NoticeOpacity => Mathf.SmoothStep(0f, 1f, noticeAge / .16f) *
            Mathf.SmoothStep(0f, 1f, (NoticeDuration - noticeAge) / .35f);

        private void AdvanceNotice(float seconds)
        {
            if (notices.Count == 0) return;
            noticeAge += seconds;
            if (noticeAge < NoticeDuration) return;
            notices.RemoveAt(0);
            noticeAge = 0f;
        }

        private void Notify(Site site, string title, string detail, NoticeKind kind = NoticeKind.Progress, string next = null)
        {
            // Replace superseded progress for this site. Completions precede pending progress,
            // and can never be displaced by a later pickup or terminal interaction.
            for (int i = notices.Count - 1; i >= 0; i--)
            {
                if (notices[i].Site != site || notices[i].Kind == NoticeKind.Completion) continue;
                if (i == 0) noticeAge = 0f;
                notices.RemoveAt(i);
            }
            int at = notices.Count;
            if (kind == NoticeKind.Completion)
            {
                at = 0;
                while (at < notices.Count && notices[at].Kind == NoticeKind.Completion) at++;
            }
            if (at == 0) noticeAge = 0f;
            notices.Insert(at, new MissionNotice(site, title, detail, kind, next));
        }

        private void Complete(Site site, string title, string detail)
        {
            string next = CurrentSite switch
            {
                Site.Supply => "Find the supply station",
                Site.Checkpoint => "Reach Checkpoint",
                Site.Server => "Proceed to the Server facility.",
                Site.Control => "Reach the Control building",
                Site.Storage => "Reach the Storage facility",
                _ => ReadObjective(Site.Extraction).Instruction
            };
            Notify(site, title, detail, NoticeKind.Completion, next);
        }

        private void SuppliesTaken() => Complete(Site.Supply, "SUPPLIES COLLECTED", "Preparation complete");
        private void CardTaken(SecurityAccessCard _) => Notify(Site.Checkpoint, "CREDENTIAL ACQUIRED", "Locate and activate the security scanner.");
        private void Authenticated(CheckpointSecurityScanner _) => Complete(Site.Checkpoint, "CHECKPOINT SECURED", "Security authentication complete.");
        private void TerminalOnline(ServerTerminal terminal)
        {
            if (!server.IsComplete)
                Notify(Site.Server, $"SERVER {terminal.Number} ONLINE", $"Terminals activated   {server.OnlineCount} / {server.TerminalCount}");
        }

        private void SequenceRejected(ServerTerminal _) => Notify(Site.Server, "INCORRECT SEQUENCE",
            $"Progress reset   {server.OnlineCount} / {server.TerminalCount}   ·   Check the sequence display.", NoticeKind.Warning);
        private void NetworkRestored(ServerNetworkPuzzle puzzle) => Complete(Site.Server, "SERVER NETWORK RESTORED",
            $"Terminals activated   {puzzle.OnlineCount} / {puzzle.TerminalCount}");
        private void SwitchActivated(LockdownSecuritySwitch _) => Notify(Site.Control,
            control.ActivatedCount == control.TotalSwitches ? "OVERRIDE AUTHORIZED" : "SECURITY SUBSYSTEM DISABLED",
            $"Progress: {control.ActivatedCount} / {control.TotalSwitches}" +
            (control.ActivatedCount == control.TotalSwitches ? "   Activate the main lockdown terminal." : string.Empty));
        private void LockdownCleared(ControlLockdownMission _) => Notify(Site.Control, "CONTROL SYSTEMS RESTORED",
            "Emergency lockdown disabled.", NoticeKind.Completion, "Proceed to Storage.");
        private void CellCollected(PowerCellPickup _) => Notify(Site.Storage,
            storage.CollectedCount == storage.TotalCells ? "GENERATOR READY" : "POWER CELL COLLECTED",
            $"Power cells collected   {storage.CollectedCount} / {storage.TotalCells}");
        private void PowerRestored() => Complete(Site.Storage, "GENERATOR ONLINE", "Emergency power restored");
        private void TargetDefeated()
        {
            if (extraction.IsReady)
                Notify(Site.Extraction, "EXTRACTION AVAILABLE", "Reach the final extraction gate.", NoticeKind.Completion);
        }

        private void OnEnable() => SubscribeToMissions();

        private void SubscribeToMissions()
        {
            if (noticesBound || !isActiveAndEnabled) return;
            noticesBound = true;
            if (supplyCache != null) supplyCache.Discovered += SuppliesTaken;
            if (checkpoint != null)
            {
                checkpoint.CardCollected += CardTaken;
                checkpoint.Completed += Authenticated;
            }
            if (server != null)
            {
                server.TerminalActivated += TerminalOnline;
                server.SequenceRejected += SequenceRejected;
                server.Completed += NetworkRestored;
            }
            if (control != null)
            {
                control.SwitchActivated += SwitchActivated;
                control.Completed += LockdownCleared;
            }
            if (storage != null)
            {
                storage.CellCollected += CellCollected;
                storage.Restored += PowerRestored;
            }
            if (extraction != null) extraction.DesignatedTargetDefeated += TargetDefeated;
        }

        private void UnsubscribeFromMissions()
        {
            if (!noticesBound) return;
            noticesBound = false;
            if (supplyCache != null) supplyCache.Discovered -= SuppliesTaken;
            if (checkpoint != null)
            {
                checkpoint.CardCollected -= CardTaken;
                checkpoint.Completed -= Authenticated;
            }
            if (server != null)
            {
                server.TerminalActivated -= TerminalOnline;
                server.SequenceRejected -= SequenceRejected;
                server.Completed -= NetworkRestored;
            }
            if (control != null)
            {
                control.SwitchActivated -= SwitchActivated;
                control.Completed -= LockdownCleared;
            }
            if (storage != null)
            {
                storage.CellCollected -= CellCollected;
                storage.Restored -= PowerRestored;
            }
            if (extraction != null) extraction.DesignatedTargetDefeated -= TargetDefeated;
        }

        private void OnDisable()
        {
            UnsubscribeFromMissions();
            notices.Clear();
            noticeAge = 0f;
        }
    }
}
