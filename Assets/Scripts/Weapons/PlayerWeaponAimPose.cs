using OskarMike.Network.Player;
using UnityEngine;

namespace OskarMike.Weapons
{
    /// <summary>
    /// 3인칭 상체 조준 자세 (모든 피어에서 로컬 실행, 네트워크 상태 없음 — 각도는 PlayerAimPitch 가 동기화).
    /// 무기 레이어는 팔·머리만 덮어서(WeaponArmsMask) 원본 애니메이션의 몸통 비틀기가 빠지므로,
    /// 그대로 두면 3인칭 총이 몸 정면보다 왼쪽(서기·앉기 약 31~33°)을 향하고 상하 조준도 반영되지 않는다.
    /// 여기서 매 프레임 '현재 총 방향'(오른손 손 프레임 × HandGripRotation, PlayerWeaponView 와 같은 계산)을
    /// '조준 방향'(몸 정면 yaw + 동기화된 pitch)으로 돌리는 회전을 구해 Spine/Chest/UpperChest 에 나눠 준다.
    /// 총은 가슴에 강체로 붙어 있는 것과 같으므로 결과적으로 총구가 조준 방향을 정확히 향한다.
    /// 실행 순서: Animator → PlayerAimPitch(-50) → 이 컴포넌트(-40) → PlayerWeaponView 총 정렬(0) → 보조손 IK(100).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)]
    public sealed class PlayerWeaponAimPose : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerWeaponLoadout loadout;
        [SerializeField] private PlayerAimPitch aimPitch;

        [Tooltip("서기/앉기에서 반영하는 최대 상하 각도 (도)")]
        [Range(0f, 90f)] [SerializeField] private float maxPitch = 70f;
        [Tooltip("엎드리기에서 반영하는 최대 상하 각도 (도)")]
        [Range(0f, 90f)] [SerializeField] private float maxPronePitch = 25f;
        [Tooltip("총 방향 보정 최대 각도 (도). 이상한 포즈에서 몸이 과하게 꺾이는 것 방지")]
        [Range(0f, 120f)] [SerializeField] private float maxCorrection = 90f;
        [Tooltip("맨손일 때 상체가 따라 숙이는 비율")]
        [Range(0f, 1f)] [SerializeField] private float unarmedPitchRatio = 0.5f;
        [Tooltip("척추 본별 분배 비율: Spine, Chest, UpperChest (없는 본의 몫은 나머지에 나눠짐)")]
        [SerializeField] private Vector3 boneWeights = new Vector3(0.35f, 0.4f, 0.25f);

        [Header("기울이기 (Q/E)")]
        [Tooltip("머리가 옆으로 이동할 거리 (m). PlayerNetworkController.leanDistance 와 맞추면 1인칭 카메라 이동량과 같아진다.")]
        [Min(0f)] [SerializeField] private float leanDistance = 0.22f;
        [Tooltip("허리 기준 최대 기울기 각도 (도)")]
        [Range(0f, 45f)] [SerializeField] private float maxLeanAngle = 30f;
        [Tooltip("기울기 분배: Spine(허리), Chest, UpperChest — 허리 쪽이 클수록 허리를 축으로 꺾인다")]
        [SerializeField] private Vector3 leanWeights = new Vector3(0.6f, 0.3f, 0.1f);
        [Tooltip("기울기 변화 속도 (1/초, 카메라와 같은 5)")]
        [Min(0.1f)] [SerializeField] private float leanSpeed = 5f;

        private PlayerNetworkController controller;
        private PlayerHealth health;
        private readonly Transform[] spine = new Transform[3];
        private readonly float[] shares = new float[3];
        private readonly float[] leanShares = new float[3];
        private Transform hand, middle, index, little, head;
        private float lean;
        private int upperBodyLayer = -1;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (loadout == null) loadout = GetComponent<PlayerWeaponLoadout>();
            if (aimPitch == null) aimPitch = GetComponent<PlayerAimPitch>();
            controller = GetComponent<PlayerNetworkController>();
            health = GetComponent<PlayerHealth>();
            if (animator == null || !animator.isHuman) return;

            spine[0] = animator.GetBoneTransform(HumanBodyBones.Spine);
            spine[1] = animator.GetBoneTransform(HumanBodyBones.Chest);
            spine[2] = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            float total = 0f;
            for (int i = 0; i < 3; i++) if (spine[i] != null) total += Mathf.Max(0f, boneWeights[i]);
            for (int i = 0; i < 3; i++) shares[i] = spine[i] != null && total > 0f ? Mathf.Max(0f, boneWeights[i]) / total : 0f;
            float leanTotal = 0f;
            for (int i = 0; i < 3; i++) if (spine[i] != null) leanTotal += Mathf.Max(0f, leanWeights[i]);
            for (int i = 0; i < 3; i++) leanShares[i] = spine[i] != null && leanTotal > 0f ? Mathf.Max(0f, leanWeights[i]) / leanTotal : 0f;
            head = animator.GetBoneTransform(HumanBodyBones.Head);

            hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            index = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            little = animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            upperBodyLayer = animator.GetLayerIndex("UpperBody");
        }

        private void LateUpdate()
        {
            if (animator == null || (health != null && health.Dead)) { lean = 0f; return; }
            ApplyLean();
            if (aimPitch == null || !aimPitch.IsSpawned) return;

            float limit = controller != null && controller.Posture == PlayerPosture.Prone ? maxPronePitch : maxPitch;
            float pitch = Mathf.Clamp(aimPitch.Pitch, -limit, limit);
            Vector3 right = transform.right;

            // 맨손: 상체를 시선 쪽으로 조금 숙이거나 젖힌다.
            Quaternion correction = Quaternion.AngleAxis(-pitch * unarmedPitchRatio, right);

            float armed = upperBodyLayer >= 0 ? animator.GetLayerWeight(upperBodyLayer) : 0f;
            var def = loadout != null && loadout.IsSpawned ? loadout.ActiveDefinition : null;
            if (armed > 0.001f && def != null && def.UseHandGrip && TryGetGunForward(def, out Vector3 gunForward))
            {
                Vector3 aimDirection = Quaternion.AngleAxis(-pitch, right) * transform.forward;
                Quaternion toAim = Quaternion.FromToRotation(gunForward, aimDirection);
                toAim.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                toAim = Quaternion.AngleAxis(Mathf.Clamp(angle, -maxCorrection, maxCorrection), axis);
                correction = Quaternion.Slerp(correction, toAim, armed);
            }

            correction.ToAngleAxis(out float total, out Vector3 totalAxis);
            if (total > 180f) total -= 360f;
            if (Mathf.Abs(total) < 0.01f || float.IsNaN(totalAxis.x)) return;

            // 같은 축으로 나눠 돌리면 가슴(=총)의 방향 변화는 합계 회전과 같다.
            for (int i = 0; i < 3; i++)
            {
                if (spine[i] == null || shares[i] <= 0f) continue;
                spine[i].rotation = Quaternion.AngleAxis(total * shares[i], totalAxis) * spine[i].rotation;
            }
        }

        /// <summary>
        /// Q/E 기울이기: 상체를 허리(Spine) 기준으로 몸 정면 축을 따라 옆으로 꺾는다 (동기화된 netLean 사용).
        /// 각도는 머리가 leanDistance 만큼 옆으로 가도록 허리→머리 높이로 계산한다.
        /// 이후 조준 보정이 총구 방향을 다시 맞추므로 총은 기울어진 채 조준 방향을 유지한다.
        /// </summary>
        private void ApplyLean()
        {
            float target = controller != null ? Mathf.Clamp(controller.Lean, -1f, 1f) : 0f;
            lean = Mathf.MoveTowards(lean, target, leanSpeed * Time.deltaTime);
            if (Mathf.Abs(lean) < 0.001f || spine[0] == null || head == null) return;

            float waistToHead = Vector3.Distance(head.position, spine[0].position);
            if (waistToHead < 0.05f) return;
            float angle = Mathf.Min(maxLeanAngle, Mathf.Asin(Mathf.Clamp01(leanDistance / waistToHead)) * Mathf.Rad2Deg);

            // 오른쪽(+) 기울기 = 몸 정면 축 기준 음의 회전.
            Vector3 axis = transform.forward;
            for (int i = 0; i < 3; i++)
            {
                if (spine[i] == null || leanShares[i] <= 0f) continue;
                spine[i].rotation = Quaternion.AngleAxis(-angle * lean * leanShares[i], axis) * spine[i].rotation;
            }
        }

        /// <summary>PlayerWeaponView 가 이번 프레임에 총을 놓을 방향과 같은 계산 (오른손 손 프레임 × HandGripRotation).</summary>
        private bool TryGetGunForward(WeaponDefinition def, out Vector3 forward)
        {
            forward = Vector3.forward;
            if (hand == null || middle == null || index == null || little == null) return false;
            Vector3 along = middle.position - hand.position;
            Vector3 side = index.position - little.position;
            if (along.sqrMagnitude < 1e-8f || side.sqrMagnitude < 1e-8f) return false;
            Quaternion frame = Quaternion.LookRotation(along, Vector3.Cross(along.normalized, side.normalized));
            forward = frame * def.HandGripRotation * Vector3.forward;
            return true;
        }
    }
}
