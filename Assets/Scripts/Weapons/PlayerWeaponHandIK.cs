using OskarMike.Items;
using UnityEngine;

namespace OskarMike.Weapons
{
    /// <summary>
    /// 3인칭 보조손(왼손) IK. 모든 피어에서 로컬로 동작하며 네트워크 상태는 없다.
    /// 총은 PlayerWeaponView 가 오른손 '손 프레임'에 붙이고(LateUpdate, 실행 순서 0),
    /// 이 컴포넌트가 그 다음(실행 순서 100)에 왼팔을 2본 IK 로 풀어 왼손을 총의 보조손 위치
    /// (WeaponDefinition.SupportHandOffset/Rotation)에 맞춘다.
    /// 애니메이션이 만들어진 몸(HumanDummy_F)에서는 목표가 원래 포즈와 같아 거의 변화가 없고,
    /// 체형이 다른 모델에서 왼손이 총에서 뜨거나 파고드는 것을 막는다. 팔꿈치는 애니메이션의 굽힘 평면을 유지한다.
    ///
    /// 더플백을 메고 권총(보조무장)을 든 동안에는 왼손이 총 대신 가슴 앞 가방끈을 잡는다
    /// (DuffelBagWornVisual.TryGetStrapGrip) → 한 손이 막혀 권총만 쓴다는 것을 보여 준다.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class PlayerWeaponHandIK : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerWeaponView view;
        [SerializeField] private PlayerWeaponLoadout loadout;
        [Tooltip("최대 IK 가중치. 실제 가중치 = 이 값 × UpperBody 레이어 가중치")]
        [Range(0f, 1f)] [SerializeField] private float maxWeight = 1f;
        [Tooltip("총 ↔ 가방끈 목표 전환 시간 (초)")]
        [Min(0.01f)] [SerializeField] private float strapBlendTime = 0.2f;

        private Transform upperArm, lowerArm, hand, middle, index, little;
        private int upperBodyLayer = -1;
        private PlayerLootCarrier lootCarrier;
        private float strapBlend;   // 0 = 총 보조손, 1 = 가방끈

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (view == null) view = GetComponent<PlayerWeaponView>();
            if (loadout == null) loadout = GetComponent<PlayerWeaponLoadout>();
            lootCarrier = GetComponent<PlayerLootCarrier>();
            if (animator == null || !animator.isHuman) return;
            upperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            lowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            middle = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            index = animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            little = animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            upperBodyLayer = animator.GetLayerIndex("UpperBody");
        }

        private void LateUpdate()
        {
            if (upperArm == null || lowerArm == null || hand == null || view == null || loadout == null) return;
            var gun = view.ActiveWorldModel;
            var def = loadout.ActiveDefinition;

            // 더플백 + 권총: 왼손은 가방끈
            bool hasStrap = TryGetStrapGrip(out Vector3 strapPosition, out Quaternion strapRotation);
            bool wantStrap = hasStrap && gun != null && loadout.ActiveSlot == WeaponSlot.Secondary && !loadout.Holstered;
            strapBlend = Mathf.MoveTowards(strapBlend, wantStrap ? 1f : 0f, Time.deltaTime / strapBlendTime);
            if (!hasStrap) strapBlend = 0f;

            bool hasGun = gun != null && def != null && def.UseSupportHandIK;
            if (!hasGun && strapBlend <= 0f) return;

            float weight = maxWeight * (upperBodyLayer >= 0 ? animator.GetLayerWeight(upperBodyLayer) : 1f);
            if (weight <= 0.001f) return;

            Vector3 targetPosition;
            Quaternion targetFrame;
            if (hasGun)
            {
                targetPosition = gun.position + gun.rotation * def.SupportHandOffset;
                targetFrame = gun.rotation * def.SupportHandRotation;
                if (strapBlend > 0f)
                {
                    targetPosition = Vector3.Lerp(targetPosition, strapPosition, strapBlend);
                    targetFrame = Quaternion.Slerp(targetFrame, strapRotation, strapBlend);
                }
            }
            else
            {
                targetPosition = strapPosition;
                targetFrame = strapRotation;
                weight *= strapBlend;
            }

            SolveTwoBone(upperArm, lowerArm, hand, Vector3.Lerp(hand.position, targetPosition, weight));

            if (TryGetHandFrame(out Quaternion frame))
            {
                Quaternion solved = targetFrame * Quaternion.Inverse(frame) * hand.rotation;
                hand.rotation = Quaternion.Slerp(hand.rotation, solved, weight);
            }
        }

        private bool TryGetStrapGrip(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (lootCarrier == null || !lootCarrier.IsSpawned || !lootCarrier.IsCarryingBag) return false;
            var bag = lootCarrier.CarriedBag;
            if (bag == null || !bag.TryGetComponent(out DuffelBagWornVisual worn)) return false;
            return worn.TryGetStrapGrip(out position, out rotation);
        }

        /// <summary>PlayerWeaponView.TryGetHandFrame 과 같은 정의의 왼손 프레임.</summary>
        private bool TryGetHandFrame(out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (middle == null || index == null || little == null) return false;
            Vector3 along = middle.position - hand.position;
            Vector3 side = index.position - little.position;
            if (along.sqrMagnitude < 1e-8f || side.sqrMagnitude < 1e-8f) return false;
            rotation = Quaternion.LookRotation(along, Vector3.Cross(along.normalized, side.normalized));
            return true;
        }

        /// <summary>
        /// 해석적 2본 IK. ① 팔꿈치 각도를 코사인 법칙으로 맞춰 어깨→손 거리를 목표 거리로 만들고
        /// (애니메이션의 굽힘 평면 안에서 회전), ② 어깨를 최소 회전으로 돌려 손을 목표 방향에 둔다.
        /// </summary>
        private static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 target)
        {
            Vector3 pa = a.position, pb = b.position, pc = c.position;
            float lab = (pb - pa).magnitude;
            float lcb = (pc - pb).magnitude;
            if (lab < 1e-4f || lcb < 1e-4f) return;
            float lat = Mathf.Clamp((target - pa).magnitude, Mathf.Abs(lab - lcb) + 1e-4f, lab + lcb - 1e-4f);

            // ① 팔꿈치: (c-b) 를 cross(c-b, a-b) 축으로 +θ 돌리면 (a-b) 쪽으로 접힌다(각도 감소).
            Vector3 elbowAxis = Vector3.Cross(pc - pb, pa - pb);
            if (elbowAxis.sqrMagnitude > 1e-10f)
            {
                float current = Angle(pa - pb, pc - pb);
                float desired = Mathf.Acos(Mathf.Clamp((lab * lab + lcb * lcb - lat * lat) / (2f * lab * lcb), -1f, 1f));
                b.rotation = Quaternion.AngleAxis((current - desired) * Mathf.Rad2Deg, elbowAxis.normalized) * b.rotation;
            }

            // ② 어깨: 손 방향을 목표 방향으로.
            Vector3 toHand = c.position - pa;
            Vector3 toTarget = target - pa;
            if (toHand.sqrMagnitude > 1e-10f && toTarget.sqrMagnitude > 1e-10f)
                a.rotation = Quaternion.FromToRotation(toHand, toTarget) * a.rotation;
        }

        private static float Angle(Vector3 from, Vector3 to)
        {
            if (from.sqrMagnitude < 1e-10f || to.sqrMagnitude < 1e-10f) return 0f;
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(from.normalized, to.normalized), -1f, 1f));
        }
    }
}
