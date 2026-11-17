using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OskarMike.Network.Player
{
    /// <summary>
    /// 1인칭 몸 (오너 전용, 로컬). 원래 몸은 LocalPlayerVisibility 가 오너 카메라에서 숨기므로,
    /// 머리·목·양팔을 지운 메시(firstPersonMesh, FirstPersonBodyMeshBaker 로 굽기)를 두 번째 SkinnedMeshRenderer 로
    /// Default 레이어에 그린다 → 아래를 보면 가슴·배·다리가 보인다.
    ///
    /// 진짜 뼈는 건드리지 않는다(무기 그립·에임 포즈·IK·피격 판정·다른 피어 화면 그대로). 대신 뼈 자세를 매 프레임
    /// "대리 뼈"에 복사하면서 오너 화면용으로 세 가지를 보정한다.
    /// 1) 아래를 겨눌 때 PlayerWeaponAimPose 가 상체를 앞으로 숙이는 것(허리 기준 앞뒤 굽힘)을 편다 — 실제로는 고개만 숙이므로.
    ///    좌우 기울이기(Q/E)는 유지한다.
    /// 2) 점프·낙하 애니메이션으로 목이 카메라 근처까지 올라오면 목이 카메라보다 neckClearance 아래가 되도록 몸 전체를 내린다.
    /// 3) 그래도 가슴 윗부분이 화면 아래 가장자리 안에 들어오면, 밖으로 나갈 만큼만 몸 전체를 수평으로 뒤로 민다(<see cref="ViewOffset"/>).
    /// 오너 화면의 더플백·끈은 <see cref="GetViewBone"/> 으로 같은 대리 뼈를 따라간다.
    /// </summary>
    // PlayerWeaponAimPose(-40)가 상체를 만든 뒤, DuffelBagWornVisual(20)이 대리 뼈를 읽기 전.
    // 팔은 1인칭 몸에서 지웠으므로 왼팔 IK(100) 이전에 복사해도 된다.
    [DefaultExecutionOrder(10)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerNetworkController))]
    public sealed class FirstPersonBodyView : MonoBehaviour
    {
        [Tooltip("머리·팔을 지운 몸 메시 (Tools/OSCAR-MIKE/Bake First-Person Body Mesh)")]
        [SerializeField] private Mesh firstPersonMesh;
        [Tooltip("1인칭 몸을 둘 레이어 이름 (오너 카메라에 보이는 레이어)")]
        [SerializeField] private string visibleLayer = "Default";

        [Header("상체 펴기")]
        [Tooltip("허리 기준 앞뒤 굽힘을 펴는 비율 (1 = 완전히 곧게)")]
        [Range(0f, 1f)] [SerializeField] private float straighten = 1f;

        [Header("목이 카메라에 닿지 않게 내리기")]
        [Tooltip("목 뿌리가 카메라보다 최소 이만큼 아래에 있도록 몸 전체를 내린다 (m). 점프·낙하 애니메이션이 몸을 들어 올릴 때")]
        [Min(0f)] [SerializeField] private float neckClearance = 0.15f;

        [Header("가슴이 화면을 가리지 않게 뒤로 밀기")]
        [Tooltip("최대로 밀어내는 거리 (m)")]
        [Min(0f)] [SerializeField] private float maxPushBack = 0.6f;
        [Tooltip("목 뿌리에서 가슴 앞면까지 (m). 이 지점이 화면 아래 가장자리 밖에 있도록 민다")]
        [Min(0f)] [SerializeField] private float chestFrontDepth = 0.14f;
        [Tooltip("화면 가장자리에서 더 띄울 여유 (m)")]
        [Min(0f)] [SerializeField] private float edgeMargin = 0.03f;

        private PlayerNetworkController controller;
        private PlayerHealth health;
        private Camera ownerCamera;
        private Transform hips, spine, neck, leftUpperLeg, rightUpperLeg;
        private SkinnedMeshRenderer source;
        private SkinnedMeshRenderer body;
        private Transform[] sourceBones;
        private Transform[] proxyBones;
        private bool[] upperBody;          // Spine 과 그 아래(상체) 뼈
        private int spineIndex = -1, neckIndex = -1;
        private Transform proxyRoot;
        private readonly Dictionary<Transform, Transform> proxyOf = new Dictionary<Transform, Transform>();
        private Vector3 viewOffset;

        /// <summary>오너 1인칭 몸을 그리는 중인지.</summary>
        public bool IsShowing => body != null && body.enabled;

        /// <summary>이번 프레임에 1인칭 몸 전체에 더한 오프셋 = 내리기 + 수평 밀기 (보여 줄 때만, 아니면 0).</summary>
        public Vector3 ViewOffset => IsShowing ? viewOffset : Vector3.zero;

        /// <summary>
        /// 오너 화면에서 몸에 붙어 보이는 연출(더플백·끈)이 쓸 뼈: 1인칭 몸을 그리는 중이면 보정된 대리 뼈, 아니면 원래 뼈.
        /// 피격 판정·손 IK 처럼 실제 몸 기준이어야 하는 것에는 쓰지 않는다.
        /// </summary>
        public Transform GetViewBone(Transform realBone)
        {
            if (realBone == null || !IsShowing) return realBone;
            return proxyOf.TryGetValue(realBone, out var proxy) ? proxy : realBone;
        }

        private void Awake()
        {
            controller = GetComponent<PlayerNetworkController>();
            health = GetComponent<PlayerHealth>();
            ownerCamera = GetComponentInChildren<Camera>(true);
            var animator = GetComponentInChildren<Animator>(true);
            if (animator == null) return;
            source = animator.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (!animator.isHuman) return;
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        }

        private void LateUpdate()
        {
            bool show = firstPersonMesh != null && source != null && controller.IsSpawned && controller.IsOwner &&
                        (health == null || !health.Dead);
            if (!show)
            {
                if (body != null) body.enabled = false;
                viewOffset = Vector3.zero;
                return;
            }
            if (body == null) CreateBody();
            body.enabled = true;

            // 1) 그대로 복사
            for (int i = 0; i < sourceBones.Length; i++)
            {
                Transform bone = sourceBones[i];
                if (bone == null) continue;
                proxyBones[i].SetPositionAndRotation(bone.position, bone.rotation);
                proxyBones[i].localScale = bone.lossyScale;
            }

            // 2) 상체 앞뒤 굽힘 펴기 (허리 Spine 기준, 몸 오른쪽 축 회전만)
            Straighten();

            // 3) 목이 카메라 높이 근처까지 올라오면(점프·낙하 애니메이션) 몸 전체를 내린다.
            //    이게 없으면 아래 밀기 계산이 "가슴이 카메라보다 아래가 아님"으로 빠져 상체가 눈앞에 그대로 보인다.
            viewOffset = ComputeDrop();
            // 4) 가슴이 화면을 가리면 수평으로 뒤로 밀기
            viewOffset += ComputePush(viewOffset);
            if (viewOffset != Vector3.zero)
                for (int i = 0; i < proxyBones.Length; i++) proxyBones[i].position += viewOffset;
        }

        private void Straighten()
        {
            if (straighten <= 0f || spineIndex < 0 || neckIndex < 0 || hips == null) return;
            Transform proxySpine = proxyBones[spineIndex];
            Transform proxyNeck = proxyBones[neckIndex];

            Vector3 pelvisUp = spine.position - hips.position;
            if (pelvisUp.sqrMagnitude < 1e-8f) return;
            pelvisUp.Normalize();
            Vector3 right = leftUpperLeg != null && rightUpperLeg != null ? rightUpperLeg.position - leftUpperLeg.position : transform.right;
            right = Vector3.ProjectOnPlane(right, pelvisUp);
            if (right.sqrMagnitude < 1e-8f) return;
            right.Normalize();

            // 몸 오른쪽 축에 수직인 평면(앞뒤 평면)에서 허리→목 방향과 골반 위 방향 사이 각
            Vector3 torso = Vector3.ProjectOnPlane(proxyNeck.position - proxySpine.position, right);
            if (torso.sqrMagnitude < 1e-8f) return;
            float angle = Vector3.SignedAngle(torso, pelvisUp, right) * straighten;
            if (Mathf.Abs(angle) < 0.01f) return;

            Quaternion fix = Quaternion.AngleAxis(angle, right);
            Vector3 pivot = proxySpine.position;
            for (int i = 0; i < proxyBones.Length; i++)
            {
                if (!upperBody[i]) continue;
                Transform p = proxyBones[i];
                p.SetPositionAndRotation(pivot + fix * (p.position - pivot), fix * p.rotation);
            }
        }

        private Vector3 ComputeDrop()
        {
            if (ownerCamera == null || neckIndex < 0) return Vector3.zero;
            float rise = proxyBones[neckIndex].position.y + neckClearance - ownerCamera.transform.position.y;
            return rise > 0f ? Vector3.down * rise : Vector3.zero;
        }

        private Vector3 ComputePush(Vector3 alreadyApplied)
        {
            if (ownerCamera == null || neckIndex < 0 || maxPushBack <= 0f) return Vector3.zero;
            Transform cam = ownerCamera.transform;
            Vector3 forward = cam.forward;
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.ProjectOnPlane(cam.up, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f) flat = transform.forward;
            flat.Normalize();

            // 가슴 윗부분 앞면(목 뿌리 + 앞쪽)이 화면 아래 가장자리 밖에 있도록 필요한 만큼만 민다.
            Vector3 chestTop = proxyBones[neckIndex].position + alreadyApplied + flat * chestFrontDepth;
            Vector3 toChest = chestTop - cam.position;
            float below = -toChest.y;
            if (below <= 0.01f) return Vector3.zero;
            float ahead = Vector3.Dot(toChest, flat);
            float down = Mathf.Asin(Mathf.Clamp(-forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float edge = (down + ownerCamera.fieldOfView * 0.5f) * Mathf.Deg2Rad;   // 화면 아래 가장자리의 내려본 각
            if (edge <= 0.01f) return Vector3.zero;
            float allowedAhead = below / Mathf.Tan(Mathf.Min(edge, Mathf.PI - 0.01f)) - edgeMargin;
            float push = Mathf.Clamp(ahead - allowedAhead, 0f, maxPushBack);
            return -flat * push;
        }

        private void CreateBody()
        {
            // 대리 뼈: 플레이어 루트 아래 평평한 계층 (월드 자세를 직접 복사하므로 부모 관계 불필요)
            proxyRoot = new GameObject("1인칭 몸 뼈").transform;
            proxyRoot.SetParent(transform, false);
            sourceBones = source.bones;
            proxyBones = new Transform[sourceBones.Length];
            upperBody = new bool[sourceBones.Length];
            proxyOf.Clear();
            Transform proxyRootBone = null;
            for (int i = 0; i < sourceBones.Length; i++)
            {
                Transform real = sourceBones[i];
                var proxy = new GameObject(real != null ? real.name : "bone").transform;
                proxy.SetParent(proxyRoot, false);
                proxyBones[i] = proxy;
                if (real == null) continue;
                proxyOf[real] = proxy;
                if (real == source.rootBone) proxyRootBone = proxy;
                if (real == spine) spineIndex = i;
                if (real == neck) neckIndex = i;
                upperBody[i] = spine != null && (real == spine || real.IsChildOf(spine));
            }

            var go = new GameObject("1인칭 몸");
            go.transform.SetParent(transform, false);
            int layer = LayerMask.NameToLayer(visibleLayer);
            go.layer = layer >= 0 ? layer : 0;

            body = go.AddComponent<SkinnedMeshRenderer>();
            body.sharedMesh = firstPersonMesh;
            body.bones = proxyBones;
            body.rootBone = proxyRootBone != null ? proxyRootBone : proxyBones.Length > 0 ? proxyBones[0] : null;
            body.sharedMaterials = source.sharedMaterials;
            body.localBounds = source.localBounds;
            body.updateWhenOffscreen = true;
            body.quality = source.quality;
            body.shadowCastingMode = ShadowCastingMode.Off;
            body.receiveShadows = true;
        }

        private void OnDisable()
        {
            if (body != null) body.enabled = false;
        }

        private void OnDestroy()
        {
            if (body != null) Destroy(body.gameObject);
            if (proxyRoot != null) Destroy(proxyRoot.gameObject);
        }
    }
}
