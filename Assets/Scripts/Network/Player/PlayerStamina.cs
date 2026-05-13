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
    ///   - 파쿠르             : costParkour (1회, 일시적)
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
        private bool  wasSprintingLastFrame = false;

        // ── 공개 읽기 전용 ──────────────────────────────────
        public float Stamina      => stamina.Value;
        public float MaxStamina   => maxStamina;
        public float Ratio        => stamina.Value / maxStamina;
        public bool  IsExhausted  => Ratio <= lowStaminaThreshold;

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
                postSprintTimer = 0f;

            // 질주 후 타이머 진행
            if (!isSprinting)
                postSprintTimer += dt;

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
            else
            {
                delta += recoverRate * dt;
            }

            stamina.Value = Mathf.Clamp(stamina.Value + delta, 0f, maxStamina);
        }

        /// <summary>점프 스태미나 소모 (서버 호출).</summary>
        public bool ServerConsumeJump()
        {
            if (!IsServer) return false;
            if (stamina.Value < costJump) return false;
            stamina.Value = Mathf.Max(0f, stamina.Value - costJump);
            return true;
        }

        /// <summary>파쿠르 스태미나 소모 (서버 호출).</summary>
        public bool ServerConsumeParkour()
        {
            if (!IsServer) return false;
            if (stamina.Value < costParkour) return false;
            stamina.Value = Mathf.Max(0f, stamina.Value - costParkour);
            return true;
        }
    }
}
