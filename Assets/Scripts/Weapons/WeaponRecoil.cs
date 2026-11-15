using OskarMike.Network.Player;
using UnityEngine;

namespace OskarMike.Weapons
{
    /// <summary>
    /// 오너 로컬 리코일 (네트워크 상태 없음). 카메라 피치는 원래 로컬 전용이라 동기화가 필요 없다.
    ///
    /// <see cref="PlayerNetworkController"/> 가 매 Update 에 마우스 피치로 카메라 회전을 덮어쓰므로,
    /// 이 컴포넌트는 그 뒤(실행 순서 50)에 누적 반동 오프셋을 "얹는다".
    /// 플레이어가 마우스를 내리면 기본 피치가 내려가므로 반동 제어가 그대로 된다.
    /// 쏘지 않으면 누적분 중 recoilReturnRatio 만큼이 recoilRecovery 속도로 돌아온다.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerWeaponLoadout))]
    public sealed class WeaponRecoil : MonoBehaviour
    {
        [Tooltip("반동을 얹을 카메라 트랜스폼. 비우면 플레이어 카메라.")]
        [SerializeField] private Transform aimTransform;
        [Tooltip("마지막 반동 후 복귀를 시작하기까지의 지연(초). 연사 간격보다 길어야 연사 중 패턴이 깎이지 않는다.")]
        [Min(0f)] [SerializeField] private float recoveryDelay = 0.15f;
        [Tooltip("누적 반동 최대치(도) — 아주 긴 연사에서 카메라가 넘어가지 않게")]
        [Min(1f)] [SerializeField] private float maxOffset = 30f;

        private PlayerWeaponLoadout loadout;
        private PlayerNetworkController controller;
        private Vector2 offset;         // 현재 얹고 있는 반동 (x = 위 pitch, y = 오른쪽 yaw)
        private Vector2 recoverable;    // 그중 자동으로 돌아올 몫
        private float lastKickTime = -10f;
        private float recoverySpeed = 8f;
        private Quaternion lastBase = Quaternion.identity;
        private Quaternion lastApplied = Quaternion.identity;
        private bool applied;

        /// <summary>현재 누적 반동 (도).</summary>
        public Vector2 Offset => offset;

        private void Awake()
        {
            loadout = GetComponent<PlayerWeaponLoadout>();
            controller = GetComponent<PlayerNetworkController>();
            if (aimTransform == null)
            {
                var cam = GetComponentInChildren<Camera>(true);
                if (cam != null) aimTransform = cam.transform;
            }
        }

        /// <summary>오너: 한 발 쏜 직후 호출. 반동을 누적한다.</summary>
        public void Kick(Vector2 kick, float returnRatio, float recovery)
        {
            offset += kick;
            offset = Vector2.ClampMagnitude(offset, maxOffset);
            recoverable += kick * Mathf.Clamp01(returnRatio);
            recoverable = Vector2.ClampMagnitude(recoverable, offset.magnitude);
            recoverySpeed = Mathf.Max(0f, recovery);
            lastKickTime = Time.time;
        }

        /// <summary>반동 즉시 초기화 (사망, 입력 비활성 등).</summary>
        public void ResetRecoil()
        {
            offset = recoverable = Vector2.zero;
        }

        private void Update()
        {
            if (aimTransform == null || !loadout.IsSpawned || !loadout.IsOwner) return;
            bool active = controller == null || controller.GameplayInputEnabled;
            if (!active) ResetRecoil();

            // 복귀
            if (Time.time - lastKickTime >= recoveryDelay && recoverable.sqrMagnitude > 0f)
            {
                float step = Mathf.Min(recoverable.magnitude, recoverySpeed * Time.deltaTime);
                Vector2 delta = recoverable.normalized * step;
                recoverable -= delta;
                offset -= delta;
            }

            // 컨트롤러가 이번 프레임에 회전을 덮어쓰지 않았다면, 지난번에 얹은 오프셋을 먼저 걷어낸다.
            Quaternion current = aimTransform.localRotation;
            Quaternion baseRotation = applied && current == lastApplied ? lastBase : current;

            Quaternion result = offset.sqrMagnitude > 0f
                ? baseRotation * Quaternion.Euler(-offset.x, offset.y, 0f)
                : baseRotation;
            aimTransform.localRotation = result;
            lastBase = baseRotation;
            lastApplied = result;
            applied = true;
        }

        private void OnDisable()
        {
            if (applied && aimTransform != null && aimTransform.localRotation == lastApplied)
                aimTransform.localRotation = lastBase;
            applied = false;
            ResetRecoil();
        }
    }
}
