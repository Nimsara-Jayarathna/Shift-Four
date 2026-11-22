using UnityEngine;

namespace ShiftFour
{
    [DisallowMultipleComponent]
    internal sealed class OutdoorPlayerEquipment : MonoBehaviour
    {
        private PlayerController player;
        private CheckpointSecurityScanner checkpointMission;
        private ServerNetworkPuzzle serverMission;
        private ControlLockdownMission controlMission;
        private WeaponController weapons;
        private OutdoorKnifeViewmodel knife;
        private FirstPersonWeaponMount mount;
        private readonly FirstPersonEquipTransition knifeDraw = new FirstPersonEquipTransition();
        private bool wantsKnife = true, hitDrone;
        private float range, damage, cooldown, nextSlash, swingStarted;
        private float serverKeyNoticeUntil, controlKeyNoticeUntil, storageKeyNoticeUntil;
        internal bool HasSupplies { get; private set; }
        internal bool HasCheckpointKey => HasSupplies;
        internal bool HasServerKey { get; private set; }
        internal string ServerKeyNotice => HasServerKey && Time.unscaledTime < serverKeyNoticeUntil
            ? "SERVER ACCESS KEY ACQUIRED" : string.Empty;
        internal bool HasControlKey { get; private set; }
        internal string ControlKeyNotice => HasControlKey && Time.unscaledTime < controlKeyNoticeUntil
            ? "CONTROL ACCESS KEY ACQUIRED" : string.Empty;
        internal bool HasStorageKey { get; private set; }
        internal string StorageKeyNotice => HasStorageKey && Time.unscaledTime < storageKeyNoticeUntil
            ? "STORAGE ACCESS KEY ACQUIRED" : string.Empty;
        internal WeaponDefinition Pistol { get; private set; }
        internal bool KnifeSelected => weapons != null && weapons.ActiveWeapon == null;

        internal void Configure(WeaponDefinition pistol, float reach, float hitDamage, float attackCooldown)
        {
            Pistol = pistol;
            range = Mathf.Clamp(reach, .5f, 3f);
            damage = Mathf.Clamp(hitDamage, 1f, 100f);
            cooldown = Mathf.Clamp(attackCooldown, .3f, 2f);
        }

        internal void Bind(PlayerController actor, WeaponController controller)
        {
            player = actor;
            weapons = controller;
            mount = actor.GetComponent<FirstPersonWeaponMount>();
        }

        internal void BuildKnife(Camera view, PrototypeMaterials materials)
        {
            knife = OutdoorKnifeViewmodel.Build(view, materials);
        }

        internal void BindCheckpointMission(CheckpointSecurityScanner mission)
        {
            if (ReferenceEquals(checkpointMission, mission)) return;
            if (checkpointMission != null) checkpointMission.Completed -= AwardServerKey;
            checkpointMission = mission;
            if (checkpointMission != null) checkpointMission.Completed += AwardServerKey;
        }

        private void AwardServerKey(CheckpointSecurityScanner scanner)
        {
            if (scanner != checkpointMission || !scanner.IsComplete || HasServerKey) return;
            HasServerKey = true;
            serverKeyNoticeUntil = Time.unscaledTime + 4f;
        }

        internal void BindServerMission(ServerNetworkPuzzle mission)
        {
            if (ReferenceEquals(serverMission, mission)) return;
            if (serverMission != null) serverMission.Completed -= AwardControlKey;
            serverMission = mission;
            if (serverMission != null) serverMission.Completed += AwardControlKey;
        }

        private void AwardControlKey(ServerNetworkPuzzle puzzle)
        {
            if (puzzle != serverMission || !puzzle.IsComplete || HasControlKey) return;
            HasControlKey = true;
            controlKeyNoticeUntil = Time.unscaledTime + 4f;
        }

        internal void BindControlMission(ControlLockdownMission mission)
        {
            if (ReferenceEquals(controlMission, mission)) return;
            if (controlMission != null) controlMission.Completed -= AwardStorageKey;
            controlMission = mission;
            if (controlMission != null) controlMission.Completed += AwardStorageKey;
        }

        private void AwardStorageKey(ControlLockdownMission lockdown)
        {
            if (lockdown != controlMission || !lockdown.IsComplete || HasStorageKey) return;
            HasStorageKey = true;
            storageKeyNoticeUntil = Time.unscaledTime + 4f;
        }

        private void OnDestroy()
        {
            BindCheckpointMission(null);
            BindServerMission(null);
            BindControlMission(null);
        }

        internal void ResetForWorld()
        {
            HasSupplies = false;
            HasServerKey = false;
            HasControlKey = false;
            HasStorageKey = false;
            serverKeyNoticeUntil = controlKeyNoticeUntil = storageKeyNoticeUntil = 0f;
            BindCheckpointMission(null);
            BindServerMission(null);
            BindControlMission(null);
            wantsKnife = true;
            hitDrone = false;
            knifeDraw.ResetReady(Time.timeAsDouble);
            nextSlash = 0f;
            swingStarted = float.NegativeInfinity;
            if (weapons != null) weapons.ClearLoadout();
            PresentKnife();
        }

        internal bool CollectSupplies()
        {
            if (HasSupplies || Pistol == null || player == null || !player.CanAct || !mount.EquipLowered(Pistol))
                return false;
            HasSupplies = true;
            wantsKnife = false;
            return true;
        }

        internal void ProcessInput()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) SelectKnife();
            else if (Input.GetKeyDown(KeyCode.Alpha2)) SelectPistol();

            if (!wantsKnife && !KnifeSelected) weapons.ProcessInput();
            else if (Input.GetMouseButtonDown(0)) TrySlash();
        }

        internal void SelectKnife()
        {
            if (!player.CanAct || wantsKnife) return;
            wantsKnife = true;
            mount.RequestUnequip();
        }

        internal bool SelectPistol()
        {
            if (!HasSupplies || !player.CanAct || !wantsKnife) return false;
            if (KnifeSelected && !mount.EquipLowered(Pistol)) return false;
            wantsKnife = false;
            return true;
        }

        internal bool TrySlash()
        {
            if (!wantsKnife || !KnifeSelected || knifeDraw.State != WeaponPresentationState.Ready ||
                !player.CanAct || Time.time < nextSlash) return false;
            nextSlash = Time.time + cooldown;
            swingStarted = Time.time;
            hitDrone = ResolveStrike();
            return true;
        }

        private bool ResolveStrike()
        {
            Camera camera = player.ViewCamera;
            if (!Physics.Raycast(player.EyePosition, camera.transform.forward, out RaycastHit hit, range,
                    WeaponController.GameplayRaycastMask, QueryTriggerInteraction.Ignore)) return false;
            var target = hit.collider.GetComponentInParent<Health>();
            if (target == null || !target.IsAlive || !target.TryGetComponent<DroneBrain>(out _)) return false;
            target.ApplyDamage(damage, player, "Knife melee");
            return true;
        }

        private void LateUpdate()
        {
            if (player == null || !player.CanAct)
            {
                swingStarted = float.NegativeInfinity;
                PresentKnife();
                return;
            }

            double now = Time.timeAsDouble;
            knifeDraw.Sample(now);
            if (wantsKnife)
            {
                if (!KnifeSelected && mount.TransitionState == WeaponPresentationState.Hidden) weapons.Holster();
                if (KnifeSelected) knifeDraw.RequestEquip(now, .19f);
            }
            else
            {
                knifeDraw.RequestUnequip(now, .14f);
                if (knifeDraw.State == WeaponPresentationState.Hidden) mount.RequestEquip();
            }

            PresentKnife();
        }

        private void PresentKnife()
        {
            if (knife != null) knife.Present(player != null && player.CanPresentWeapon &&
                knifeDraw.State != WeaponPresentationState.Hidden, knifeDraw.Visibility, swingStarted, hitDrone);
        }
    }
}
