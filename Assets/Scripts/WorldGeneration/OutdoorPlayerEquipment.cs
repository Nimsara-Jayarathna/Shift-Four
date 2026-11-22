using UnityEngine;

namespace ShiftFour
{
    [DisallowMultipleComponent]
    internal sealed class OutdoorPlayerEquipment : MonoBehaviour
    {
        private PlayerController player;
        private CheckpointSecurityScanner checkpointMission;
        private WeaponController weapons;
        private OutdoorKnifeViewmodel knife;
        private float range, damage, cooldown, nextSlash, swingStarted;
        internal bool HasSupplies { get; private set; }
        internal bool HasCheckpointKey => HasSupplies;
        // The active scanner is the source of truth, so there is no key pickup or extra completion flag.
        internal bool HasServerKey => checkpointMission != null && checkpointMission.isActiveAndEnabled && checkpointMission.IsComplete;
        internal string ServerKeyNotice => HasServerKey && Time.unscaledTime < checkpointMission.CompletedAt + 4f
            ? "SERVER ACCESS KEY ACQUIRED" : string.Empty;
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

        internal void BindCheckpointMission(CheckpointSecurityScanner mission) => checkpointMission = mission;

        internal void ResetForWorld()
        {
            HasSupplies = false;
            checkpointMission = null;
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
