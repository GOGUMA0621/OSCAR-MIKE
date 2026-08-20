using Unity.Netcode;
using UnityEngine;

namespace OskarMike.Network.Player
{
    /// <summary>
    /// Host-authoritative 스태미나 시스템.
    /// 서버에서만 값을 갱신하고 NetworkVariable로 클라이언트에 전파한다.
    ///
    /// 소모 조건:
    ///   - 달리기 (Sprint)    : drainSprint / 초
    ///   - 점프               : costJump (1회)
    ///   - 볼트/파쿠르        : costParkour (1회, 일시적)
    ///   - 슬라이딩           : costSlide (1회)
    ///   - 다이빙             : costDive (1회)
    ///   - 과적 상태 이동     : drainOverweightWalk / 초
    ///
    /// 회복 조건: 위 상황 외 모든 상황 → recoverRate / 초
    ///
    /// 조준 흔들림 조건:
    ///   - 스태미나 20% 이하
    ///   - 질주 종료 후 2초 이내
    /// </summary>
    public class PlayerStamina : NetworkBehaviour
    {
        // ── Inspector ──────────────────────────────────────
        [Header("Stamina")]
        [SerializeField] private float maxStamina          = 100f;
        [SerializeField] private float recoverRate         = 10f;   // /초
        [SerializeField] private float drainSprint         = 15f;   // /초
        [SerializeField] private float drainOverweightWalk = 5f;    // /초 (과적 이동)
        [SerializeField] private float costJump            = 10f;   // 1회
        [SerializeField] private float costParkour         = 12f;   // 1회
        [SerializeField] private float costSlide           = 14f;   // 1회
        [SerializeField] private float costDive            = 18f;   // 1회
        [SerializeField] private float recoveryDelayAfterAction = 0.75f;

        [Header("Sway Thresholds")]
        [SerializeField] private float lowStaminaThreshold = 0.20f; // 20%
        [SerializeField] private float postSprintSwayTime  = 2f;    // 질주 후 흔들림 지속 초

        // ── NetworkVariable ────────────────────────────────
        private readonly NetworkVariable<float> stamina = new NetworkVariable<float>(
            100f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // ── 서버 전용 상태 ──────────────────────────────────
        private float postSprintTimer = 0f;   // 질주 종료 후 경과 시간
        private float recoveryDelayTimer = 0f;
        private bool  wasSprintingLastFrame = false;

        // ── 공개 읽기 전용 ──────────────────────────────────
        public float Stamina      => stamina.Value;
        public float MaxStamina   => maxStamina;
        public float Ratio        => stamina.Value / maxStamina;
        public bool  IsExhausted  => Ratio <= lowStaminaThreshold;
        public bool  CanSprint    => !IsExhausted;
        public bool  CanJump      => CanSpend(costJump);
        public bool  CanVault     => CanSpend(costParkour);
        public bool  CanSlide     => CanSpend(costSlide);
        public bool  CanDive      => CanSpend(costDive);

        /// 조준 흔들림이 발생해야 하는 상태인지 (오너 클라이언트가 읽는다)
        public bool ShouldSway => IsExhausted || postSprintTimer < postSprintSwayTime;

        // ── 서버 업데이트 ───────────────────────────────────

        /// <summary>
        /// PlayerNetworkController의 서버 측 Update에서 매 프레임 호출.
        /// </summary>
        public void ServerTick(PlayerMoveState moveState, PlayerPosture posture,
                               bool isOverweight, float dt)
        {
            if (!IsServer) return;

            bool isSprinting = moveState == PlayerMoveState.Sprint;

            // 질주 종료 감지 → 타이머 리셋
            if (wasSprintingLastFrame && !isSprinting)
            {
                postSprintTimer = 0f;
                StartRecoveryDelay();
            }

            // 질주 후 타이머 진행
            if (!isSprinting)
                postSprintTimer += dt;

            if (recoveryDelayTimer > 0f)
                recoveryDelayTimer = Mathf.Max(0f, recoveryDelayTimer - dt);

            wasSprintingLastFrame = isSprinting;

            // 소모 / 회복 계산
            float delta = 0f;

            if (isSprinting)
            {
                delta -= drainSprint * dt;
            }
            else if (isOverweight && moveState == PlayerMoveState.Walk)
            {
                delta -= drainOverweightWalk * dt;
            }
            else if (recoveryDelayTimer <= 0f)
            {
                delta += recoverRate * dt;
            }

            stamina.Value = Mathf.Clamp(stamina.Value + delta, 0f, maxStamina);
        }

        /// <summary>점프 스태미나 소모 (서버 호출).</summary>
        public bool ServerConsumeJump()
        {
            if (!IsServer) return false;
            return ServerConsume(costJump);
        }

        /// <summary>볼트/파쿠르 스태미나 소모 (서버 호출).</summary>
        public bool ServerConsumeParkour()
        {
            if (!IsServer) return false;
            return ServerConsume(costParkour);
        }

        /// <summary>슬라이딩 스태미나 소모 (서버 호출).</summary>
        public bool ServerConsumeSlide()
        {
            if (!IsServer) return false;
            return ServerConsume(costSlide);
        }

        /// <summary>다이빙 스태미나 소모 (서버 호출).</summary>
        public bool ServerConsumeDive()
        {
            if (!IsServer) return false;
            return ServerConsume(costDive);
        }

        private bool ServerConsume(float amount)
        {
            if (!CanSpend(amount)) return false;
            stamina.Value = Mathf.Max(0f, stamina.Value - amount);
            StartRecoveryDelay();
            return true;
        }

        private bool CanSpend(float amount) => stamina.Value >= amount;

        private void StartRecoveryDelay()
        {
            recoveryDelayTimer = Mathf.Max(recoveryDelayTimer, recoveryDelayAfterAction);
        }
    }
}
