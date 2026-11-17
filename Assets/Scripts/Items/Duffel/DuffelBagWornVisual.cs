using OskarMike.Network.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace OskarMike.Items
{
    /// <summary>
    /// 더플백을 멘 모습 (모든 피어에서 로컬 실행, 네트워크 상태 없음).
    ///
    /// NetworkObject 는 뼈에 붙일 수 없어서 더플백 루트는 플레이어 루트에 붙어 있다(<see cref="DuffelBag.ServerAttach"/>).
    /// 이 컴포넌트가 매 프레임 운반자의 휴머노이드 뼈를 읽어
    /// - 보이는 모델(Visual)과 Hitbox 자식을 왼쪽 허리(크로스백 위치)로 옮기고,
    /// - 가방 앞끝 → 가슴 앞 → 오른쪽 어깨 위 → 등 → 가방 뒤끝으로 이어지는 어깨끈 띠 메시를 그린다.
    /// 호스트도 Animator 를 돌리므로 서버 피격 판정(Hitbox)과 보이는 위치가 같다.
    /// 왼손 IK(PlayerWeaponHandIK)가 잡을 끈 위치를 <see cref="TryGetStrapGrip"/> 로 제공한다.
    /// 운반자가 로컬 오너이고 1인칭 몸을 그리는 중이면 보이는 모델과 끈은 그 대리 뼈(<see cref="FirstPersonBodyView.GetViewBone"/>,
    /// 상체를 펴고 뒤로 민 자세)를 따른다. Hitbox 와 손잡이 위치는 실제 뼈 기준이라 피격 판정·다른 피어와 어긋나지 않는다.
    ///
    /// 실행 순서: PlayerWeaponAimPose(-40, 기울이기·상체 비틀기) 다음, PlayerWeaponHandIK(100) 이전.
    /// </summary>
    [DefaultExecutionOrder(20)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DuffelBag))]
    public sealed class DuffelBagWornVisual : MonoBehaviour
    {
        [Header("참조 (비우면 이름으로 찾음)")]
        [SerializeField] private Transform visual;
        [SerializeField] private Transform hitbox;

        [Header("허리 배치 (골반 프레임: x = 오른쪽, y = 위, z = 앞)")]
        [Tooltip("Hips 뼈 기준 가방 중심 위치 (음수 x = 왼쪽)")]
        [SerializeField] private Vector3 hipOffset = new Vector3(-0.27f, -0.02f, 0f);
        [Tooltip("플레이어 정면(수평) 기준 가방 회전. Y -90 = 가방 긴 축(X)이 몸 앞쪽")]
        [SerializeField] private Vector3 wornEuler = new Vector3(0f, -90f, 8f);
        [Range(0.3f, 1f)] [SerializeField] private float wornScale = 0.7f;

        [Header("어깨끈")]
        [Tooltip("가방 로컬(착용 축소 전) 기준 끈 고리 위치. 앞끝은 +x, 뒤끝은 x 를 뒤집어 사용")]
        [SerializeField] private Vector3 bagAnchor = new Vector3(0.3f, 0.16f, 0f);
        [Tooltip("가슴 프레임(Chest 뼈 기준, x = 오른쪽, y = 목 방향, z = 앞) 끈 경로: 앞 아래 → 어깨 → 등 아래")]
        [SerializeField] private Vector3[] chestPath =
        {
            // HumanDummy_F 바인드 포즈에서 몸 표면을 재서 정한 값 (가슴 앞 +0.15, 등 -0.10, 어깨 위 1.49 m)
            new Vector3(-0.05f, -0.057f, 0.128f),
            new Vector3(0.05f, 0.103f, 0.152f),
            new Vector3(0.11f, 0.213f, 0.106f),
            new Vector3(0.12f, 0.27f, -0.026f),
            new Vector3(0.10f, 0.17f, -0.133f),
            new Vector3(0.0f, 0.02f, -0.12f),
            new Vector3(-0.12f, -0.12f, -0.13f)
        };
        [Tooltip("왼손이 잡는 지점: chestPath 의 몇 번째 구간인지 + 그 안의 비율 (0.5 = 첫 두 점 사이 가운데)")]
        [Min(0f)] [SerializeField] private float gripPathPosition = 0.5f;
        [SerializeField] private Vector3 gripEuler = Vector3.zero;
        [Min(0.005f)] [SerializeField] private float strapWidth = 0.04f;
        [Min(0.001f)] [SerializeField] private float strapThickness = 0.01f;
        [Range(2, 12)] [SerializeField] private int samplesPerSegment = 6;
        [Tooltip("비우면 Visual 렌더러의 두 번째 머티리얼(Trim)")]
        [SerializeField] private Material strapMaterial;

        private DuffelBag bag;
        private Vector3 visualLocalPosition, hitboxLocalPosition;
        private Quaternion visualLocalRotation, hitboxLocalRotation;
        private Vector3 visualLocalScale, hitboxLocalScale;

        private Transform carrier;
        private FirstPersonBodyView carrierFirstPerson;
        private Transform hips, spine, chest, neck, leftUpperArm, rightUpperArm, leftUpperLeg, rightUpperLeg;
        private bool worn;

        private Transform strapTransform;
        private Mesh strapMesh;
        private MeshRenderer strapRenderer;
        private Vector3[] controlPoints;
        private Vector3[] samples;
        private Vector3[] vertices;
        private Vector3[] normals;
        private int[] triangles;
        private bool trianglesApplied;

        private bool hasGrip;
        private Vector3 gripPosition;
        private Quaternion gripRotation;

        /// <summary>왼손이 잡을 끈 위치·손 프레임 (+Z = 손가락 방향, +Y = 손바닥 법선 쪽). 착용 중이 아니면 false.</summary>
        public bool TryGetStrapGrip(out Vector3 position, out Quaternion rotation)
        {
            position = gripPosition;
            rotation = gripRotation;
            return worn && hasGrip;
        }

        private void Awake()
        {
            bag = GetComponent<DuffelBag>();
            if (visual == null) visual = transform.Find("Visual");
            if (hitbox == null) hitbox = transform.Find("Hitbox");
            if (visual != null)
            {
                visualLocalPosition = visual.localPosition;
                visualLocalRotation = visual.localRotation;
                visualLocalScale = visual.localScale;
                if (strapMaterial == null)
                {
                    var renderer = visual.GetComponentInChildren<Renderer>(true);
                    var materials = renderer != null ? renderer.sharedMaterials : null;
                    if (materials != null && materials.Length > 0) strapMaterial = materials[Mathf.Min(1, materials.Length - 1)];
                }
            }
            if (hitbox != null)
            {
                hitboxLocalPosition = hitbox.localPosition;
                hitboxLocalRotation = hitbox.localRotation;
                hitboxLocalScale = hitbox.localScale;
            }
        }

        private void LateUpdate()
        {
            Transform parent = bag.IsSpawned && bag.IsCarried ? transform.parent : null;
            if (parent != carrier) BindCarrier(parent);

            if (carrier == null || hips == null || chest == null)
            {
                if (worn) Restore();
                return;
            }

            worn = true;
            Quaternion bagRotation = Quaternion.LookRotation(carrier.forward, carrier.up) * Quaternion.Euler(wornEuler);

            // 실제 몸 기준: Hitbox(피격 판정)와 왼손이 잡을 끈 위치
            Vector3 realCenter = BagCenter(false);
            PlaceTransform(hitbox, realCenter, bagRotation, hitboxLocalPosition, hitboxLocalRotation, hitboxLocalScale);
            FillControlPoints(false, realCenter, bagRotation);
            UpdateGrip();

            // 보이는 모델·끈: 오너 1인칭 몸을 그리는 중이면 그 대리 뼈(펴고 민 상체) 기준
            bool view = carrierFirstPerson != null && carrierFirstPerson.IsShowing;
            Vector3 viewCenter = view ? BagCenter(true) : realCenter;
            if (view) FillControlPoints(true, viewCenter, bagRotation);
            PlaceTransform(visual, viewCenter, bagRotation, visualLocalPosition, visualLocalRotation, visualLocalScale);
            BuildStrapMesh(Bone(hips, view).position, Bone(neck != null ? neck : chest, view).position);
        }

        private Transform Bone(Transform real, bool view) =>
            view && carrierFirstPerson != null ? carrierFirstPerson.GetViewBone(real) : real;

        // ══════════════════════════════════════════════════
        // 배치
        // ══════════════════════════════════════════════════

        private void BindCarrier(Transform parent)
        {
            if (worn) Restore();
            carrier = parent;
            carrierFirstPerson = null;
            hips = spine = chest = neck = leftUpperArm = rightUpperArm = leftUpperLeg = rightUpperLeg = null;
            if (carrier == null) return;
            carrierFirstPerson = carrier.GetComponent<FirstPersonBodyView>();

            var animator = carrier.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman) return;
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null) chest = spine;
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (neck == null) neck = animator.GetBoneTransform(HumanBodyBones.Head);
            leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        }

        /// <summary>리그 축과 무관한 몸 프레임: up = a→b, right = 좌→우 뼈, forward = right × up.</summary>
        private Quaternion BodyFrame(Transform from, Transform to, Transform left, Transform right)
        {
            Vector3 up = to != null && from != null ? to.position - from.position : carrier.up;
            if (up.sqrMagnitude < 1e-8f) up = carrier.up;
            up.Normalize();
            Vector3 side = left != null && right != null ? right.position - left.position : carrier.right;
            side = Vector3.ProjectOnPlane(side, up);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.ProjectOnPlane(carrier.right, up);
            Vector3 forward = Vector3.Cross(side.normalized, up);
            return Quaternion.LookRotation(forward, up);
        }

        /// <summary>
        /// 가방 중심: 위치는 골반을 따라가고(앉기·엎드리기), 방향(bagRotation)은 중력에 매달린 것처럼
        /// 플레이어 정면 기준 수평을 유지한다 (골반 프레임으로 돌리면 엎드렸을 때 가방이 세로로 선다).
        /// </summary>
        private Vector3 BagCenter(bool view)
        {
            Transform h = Bone(hips, view);
            Quaternion pelvis = BodyFrame(h, Bone(spine, view), Bone(leftUpperLeg, view), Bone(rightUpperLeg, view));
            return h.position + pelvis * hipOffset;
        }

        private void PlaceTransform(Transform target, Vector3 center, Quaternion rotation,
            Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            if (target == null) return;
            target.SetPositionAndRotation(center + rotation * (localPosition * wornScale), rotation * localRotation);
            target.localScale = localScale * wornScale;
        }

        private void Restore()
        {
            worn = false;
            hasGrip = false;
            if (visual != null)
            {
                visual.localPosition = visualLocalPosition;
                visual.localRotation = visualLocalRotation;
                visual.localScale = visualLocalScale;
            }
            if (hitbox != null)
            {
                hitbox.localPosition = hitboxLocalPosition;
                hitbox.localRotation = hitboxLocalRotation;
                hitbox.localScale = hitboxLocalScale;
            }
            if (strapRenderer != null) strapRenderer.enabled = false;
        }

        // ══════════════════════════════════════════════════
        // 어깨끈
        // ══════════════════════════════════════════════════

        /// <summary>끈 제어점: 가방 앞끝 → chestPath(가슴 프레임) → 가방 뒤끝.</summary>
        private void FillControlPoints(bool view, Vector3 bagCenter, Quaternion bagRotation)
        {
            int pathCount = chestPath != null ? chestPath.Length : 0;
            int count = pathCount + 2;
            if (controlPoints == null || controlPoints.Length != count) controlPoints = new Vector3[count];

            Transform c = Bone(chest, view);
            Quaternion chestFrame = BodyFrame(c, Bone(neck, view), Bone(leftUpperArm, view), Bone(rightUpperArm, view));
            Vector3 front = new Vector3(bagAnchor.x, bagAnchor.y, bagAnchor.z) * wornScale;
            Vector3 rear = new Vector3(-bagAnchor.x, bagAnchor.y, bagAnchor.z) * wornScale;
            controlPoints[0] = bagCenter + bagRotation * front;
            for (int i = 0; i < pathCount; i++) controlPoints[i + 1] = c.position + chestFrame * chestPath[i];
            controlPoints[count - 1] = bagCenter + bagRotation * rear;
        }

        /// <summary>현재 controlPoints 로 끈 띠 메시를 만든다. axisA→axisB = 몸 중심선 (끈 면이 바깥을 향하도록).</summary>
        private void BuildStrapMesh(Vector3 axisA, Vector3 axisB)
        {
            EnsureStrap();
            int count = controlPoints.Length;
            int segments = count - 1;
            int sampleCount = segments * samplesPerSegment + 1;
            if (samples == null || samples.Length != sampleCount) AllocateStrap(sampleCount);

            for (int s = 0; s < sampleCount; s++)
            {
                float u = (float)s / samplesPerSegment;
                int seg = Mathf.Min(Mathf.FloorToInt(u), segments - 1);
                samples[s] = CatmullRom(controlPoints, seg, u - seg);
            }

            float halfWidth = strapWidth * 0.5f;
            float halfThick = strapThickness * 0.5f;
            Matrix4x4 toLocal = strapTransform.worldToLocalMatrix;
            for (int s = 0; s < sampleCount; s++)
            {
                Vector3 p = samples[s];
                Vector3 tangent = samples[Mathf.Min(s + 1, sampleCount - 1)] - samples[Mathf.Max(s - 1, 0)];
                if (tangent.sqrMagnitude < 1e-10f) tangent = Vector3.up;
                tangent.Normalize();
                Vector3 outward = Vector3.ProjectOnPlane(p - ClosestOnSegment(axisA, axisB, p), tangent);
                if (outward.sqrMagnitude < 1e-8f) outward = Vector3.ProjectOnPlane(carrier.forward, tangent);
                outward.Normalize();
                Vector3 side = Vector3.Cross(tangent, outward) * halfWidth;
                Vector3 lift = outward * halfThick;

                int v = s * 4;
                vertices[v] = toLocal.MultiplyPoint3x4(p + lift - side);       // 바깥면
                vertices[v + 1] = toLocal.MultiplyPoint3x4(p + lift + side);
                vertices[v + 2] = toLocal.MultiplyPoint3x4(p - lift - side);   // 안쪽면
                vertices[v + 3] = toLocal.MultiplyPoint3x4(p - lift + side);
                Vector3 n = toLocal.MultiplyVector(outward);
                normals[v] = normals[v + 1] = n;
                normals[v + 2] = normals[v + 3] = -n;
            }

            strapMesh.vertices = vertices;
            strapMesh.normals = normals;
            if (!trianglesApplied) { strapMesh.triangles = triangles; trianglesApplied = true; }
            strapMesh.RecalculateBounds();
            strapRenderer.enabled = true;
        }

        /// <summary>왼손 손잡이: 실제 몸 기준 controlPoints 로 계산해야 한다 (FillControlPoints(false, ...) 직후).</summary>
        private void UpdateGrip()
        {
            hasGrip = false;
            int pathCount = controlPoints != null ? controlPoints.Length - 2 : 0;
            if (pathCount < 2) return;
            // controlPoints[1..] = chestPath. gripPathPosition 0 = chestPath[0], 1 = chestPath[1] ...
            float u = Mathf.Clamp(gripPathPosition, 0f, pathCount - 1.001f);
            int seg = Mathf.FloorToInt(u) + 1;
            Vector3 p = CatmullRom(controlPoints, seg, u - Mathf.Floor(u));
            Vector3 tangent = CatmullRom(controlPoints, seg, Mathf.Min(1f, u - Mathf.Floor(u) + 0.05f)) - p;
            if (tangent.sqrMagnitude < 1e-10f) return;

            Vector3 axisA = hips.position;
            Vector3 axisB = neck != null ? neck.position : chest.position;
            Vector3 outward = Vector3.ProjectOnPlane(p - ClosestOnSegment(axisA, axisB, p), tangent.normalized);
            if (outward.sqrMagnitude < 1e-8f) return;

            // 손가락은 끈을 감싸듯 끈과 수직 + 몸 쪽으로, 손바닥은 몸 쪽(-outward)
            Vector3 fingers = Vector3.Cross(tangent.normalized, outward.normalized);
            gripPosition = p + outward.normalized * (strapThickness + 0.015f);
            gripRotation = Quaternion.LookRotation(fingers, -outward.normalized) * Quaternion.Euler(gripEuler);
            hasGrip = true;
        }

        private void EnsureStrap()
        {
            if (strapTransform != null) return;
            var go = new GameObject("어깨끈");
            strapTransform = go.transform;
            strapTransform.SetParent(transform, false);
            go.layer = visual != null ? visual.gameObject.layer : gameObject.layer;
            strapMesh = new Mesh { name = "DuffelBag Strap" };
            strapMesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = strapMesh;
            strapRenderer = go.AddComponent<MeshRenderer>();
            strapRenderer.sharedMaterial = strapMaterial;
            strapRenderer.shadowCastingMode = ShadowCastingMode.On;
        }

        private void AllocateStrap(int sampleCount)
        {
            samples = new Vector3[sampleCount];
            vertices = new Vector3[sampleCount * 4];
            normals = new Vector3[sampleCount * 4];
            // 각 구간: 바깥면 2 삼각형, 안쪽면 2 삼각형, 양 옆면 4 삼각형
            triangles = new int[(sampleCount - 1) * 24];
            int t = 0;
            for (int s = 0; s < sampleCount - 1; s++)
            {
                int a = s * 4, b = a + 4;
                // 바깥면 (0,1)
                t = Quad(t, a, b, b + 1, a + 1);
                // 안쪽면 (2,3) 반대 와인딩
                t = Quad(t, a + 2, a + 3, b + 3, b + 2);
                // 옆면 (0,2) / (1,3)
                t = Quad(t, a, a + 2, b + 2, b);
                t = Quad(t, a + 1, b + 1, b + 3, a + 3);
            }
            strapMesh.Clear();
            trianglesApplied = false;
        }

        private int Quad(int t, int a, int b, int c, int d)
        {
            triangles[t] = a; triangles[t + 1] = b; triangles[t + 2] = c;
            triangles[t + 3] = a; triangles[t + 4] = c; triangles[t + 5] = d;
            return t + 6;
        }

        private static Vector3 CatmullRom(Vector3[] points, int segment, float t)
        {
            Vector3 p1 = points[segment];
            Vector3 p2 = points[segment + 1];
            Vector3 p0 = segment > 0 ? points[segment - 1] : p1 + (p1 - p2);
            Vector3 p3 = segment + 2 < points.Length ? points[segment + 2] : p2 + (p2 - p1);
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float len = ab.sqrMagnitude;
            if (len < 1e-10f) return a;
            return a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / len);
        }

        private void OnDisable()
        {
            if (worn) Restore();
        }

        private void OnDestroy()
        {
            if (strapMesh != null) Destroy(strapMesh);
        }
    }
}
