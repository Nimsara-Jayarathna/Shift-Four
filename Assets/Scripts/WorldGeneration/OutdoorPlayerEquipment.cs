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
            nextSlash = 0f;
            swingStarted = float.NegativeInfinity;
            if (weapons != null) weapons.ClearLoadout();
            RefreshKnife();
        }

        internal bool CollectSupplies()
        {
            if (HasSupplies || Pistol == null || player == null || !player.CanAct || !weapons.Equip(Pistol))
                return false;
            HasSupplies = true;
            RefreshKnife();
            return true;
        }

        internal void ProcessInput()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) SelectKnife();
            else if (Input.GetKeyDown(KeyCode.Alpha2)) SelectPistol();

            if (!KnifeSelected) weapons.ProcessInput();
            else if (Input.GetMouseButtonDown(0)) TrySlash();
        }

        internal void SelectKnife()
        {
            if (!player.CanAct || KnifeSelected) return;
            swingStarted = float.NegativeInfinity;
            weapons.Holster();
            RefreshKnife();
        }

        internal bool SelectPistol()
        {
            if (!HasSupplies || !weapons.Equip(Pistol)) return false;
            RefreshKnife();
            return true;
        }

        internal bool TrySlash()
        {
            if (!KnifeSelected || !player.CanAct || Time.time < nextSlash) return false;
            nextSlash = Time.time + cooldown;
            swingStarted = Time.time;
            Camera camera = player.ViewCamera;
            if (Physics.Raycast(player.EyePosition, camera.transform.forward, out RaycastHit hit, range,
                    WeaponController.GameplayRaycastMask, QueryTriggerInteraction.Ignore))
            {
                var target = hit.collider.GetComponentInParent<Health>();
                if (target != null && target.GetComponent<DroneBrain>() != null)
                    target.ApplyDamage(damage, player, "Knife melee");
            }
            return true;
        }

        private void LateUpdate() => RefreshKnife();

        private void RefreshKnife()
        {
            if (knife != null) knife.Present(KnifeSelected && player.CanPresentWeapon, swingStarted);
        }
    }
}
