using OskarMike.Network.Player;
using UnityEngine;

namespace OskarMike.Weapons
{
    /// <summary>
    /// 3인칭 상체 총기 애니메이션 파라미터를 Animator 에 전달한다 (모든 피어에서 로컬 실행, 네트워크 상태 없음).
    /// - "WeaponSlot" (Int): 0 = Primary(M4A1 세트), 1 = Secondary(Glock 세트)
    /// - "UpperBody" 레이어: 무기를 든 상체 자세 (자세/속도는 PlayerAnimatorParameters 의 Posture/Speed 를 그대로 사용)
    /// - "WeaponAim" 레이어: 조준(Aim_Idle) 자세. 가중치를 무기의 aimTime 에 맞춰 보간한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerWeaponAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerWeaponLoadout loadout;
        [Tooltip("무기 상체 레이어 가중치 변화 속도 (사망/부활 시)")]
        [Min(0.1f)] [SerializeField] private float upperBodyBlendSpeed = 6f;

        private static readonly int WeaponSlotParam = Animator.StringToHash("WeaponSlot");

        private PlayerNetworkController controller;
        private PlayerHealth health;
        private int upperBodyLayer = -1;
        private int aimLayer = -1;
        private float upperBodyWeight;
        private float aimWeight;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (loadout == null) loadout = GetComponent<PlayerWeaponLoadout>();
            controller = GetComponent<PlayerNetworkController>();
            health = GetComponent<PlayerHealth>();
            if (animator != null)
            {
                upperBodyLayer = animator.GetLayerIndex("UpperBody");
                aimLayer = animator.GetLayerIndex("WeaponAim");
            }
        }

        private void Update()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;

            bool dead = health != null && health.Dead;
            bool armed = loadout != null && loadout.IsSpawned && !dead;
            float dt = Time.deltaTime;

            upperBodyWeight = Mathf.MoveTowards(upperBodyWeight, armed ? 1f : 0f, upperBodyBlendSpeed * dt);

            bool prone = controller != null && controller.Posture == PlayerPosture.Prone;
            bool aiming = armed && loadout.IsAiming && !loadout.IsReloading && !prone;
            var definition = armed ? loadout.ActiveDefinition : null;
            float aimRate = 1f / Mathf.Max(0.01f, definition != null ? definition.AimTime : 0.18f);
            aimWeight = Mathf.MoveTowards(aimWeight, aiming ? 1f : 0f, aimRate * dt);

            if (armed) animator.SetInteger(WeaponSlotParam, (int)loadout.ActiveSlot);
            if (upperBodyLayer >= 0) animator.SetLayerWeight(upperBodyLayer, upperBodyWeight);
            if (aimLayer >= 0) animator.SetLayerWeight(aimLayer, aimWeight * upperBodyWeight);
        }
    }
}
