using OskarMike.Items;
using OskarMike.Network.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace OskarMike.Weapons
{
    /// <summary>
    /// 주무장(소총)이 손에 없을 때 등에 멘 모습으로 보여 준다 (모든 피어에서 로컬 실행, 네트워크 상태 없음).
    /// 보조무장 사용 중·아이템을 든 상태·더플백 착용 중(주무장 금지)에 표시 → "왜 권총만 쓰는지"를 눈으로 보여 준다.
    /// 위치는 리그 축과 무관한 가슴 프레임(Chest→Neck = 위, 왼팔→오른팔 = 오른쪽) 기준이라 기울이기·상체 비틀기를 따라간다.
    /// 오너 본인 사본은 LocalPlayerVisual 레이어(1인칭 카메라에서 숨김)에 둔다.
    /// </summary>
    [DefaultExecutionOrder(30)]
    [DisallowMultipleComponent]
    public sealed class PlayerSlungWeaponView : MonoBehaviour
    {
        [Header("등 배치 (가슴 프레임: x = 오른쪽, y = 목 방향, z = 앞)")]
        [Tooltip("Chest 뼈 기준 총 중심 위치 (음수 z = 등 뒤)")]
        [SerializeField] private Vector3 backOffset = new Vector3(0f, 0.12f, -0.17f);
        [Tooltip("총구 방향 (가슴 프레임). 기본: 왼쪽 위로 비스듬히")]
        [SerializeField] private Vector3 muzzleDirection = new Vector3(-0.55f, 0.83f, 0f);
        [Tooltip("총 모델 원점(손잡이)에서 총 중심까지 총구 방향 거리 (m)")]
        [SerializeField] private float gripToCenter = 0.18f;
        [Tooltip("총구 축 기준 추가 회전 (도). 0 = 총 윗면이 등 바깥을 향함")]
        [SerializeField] private float roll = 0f;

        private PlayerWeaponLoadout loadout;
        private PlayerHealth health;
        private Transform chest, neck, leftUpperArm, rightUpperArm;
        private int localLayer;
        private GameObject model;
        private WeaponDefinition modelDefinition;
        private bool ownerLayerApplied;

        private void Awake()
        {
            loadout = GetComponent<PlayerWeaponLoadout>();
            health = GetComponent<PlayerHealth>();
            localLayer = LayerMask.NameToLayer("LocalPlayerVisual");
            var animator = GetComponentInChildren<Animator>(true);
            if (animator != null && animator.isHuman)
            {
                chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            }
        }

        private void LateUpdate()
        {
            var definition = loadout != null ? loadout.GetDefinition(WeaponSlot.Primary) : null;
            bool show = loadout != null && loadout.IsSpawned && definition != null && chest != null &&
                        (health == null || !health.Dead) &&
                        (loadout.ActiveSlot != WeaponSlot.Primary || loadout.Holstered);

            if (!show)
            {
                if (model != null) model.SetActive(false);
                return;
            }

            if (model == null || modelDefinition != definition) CreateModel(definition);
            if (model == null) return;
            if (loadout.IsOwner && !ownerLayerApplied && localLayer >= 0)
            {
                LootItemVisual.SetLayer(model, localLayer);
                ownerLayerApplied = true;
            }

            model.SetActive(true);
            Quaternion frame = ChestFrame();
            Vector3 muzzle = frame * (muzzleDirection.sqrMagnitude > 1e-6f ? muzzleDirection.normalized : Vector3.up);
            Vector3 outward = frame * Vector3.back;   // 총 윗면 = 등 바깥
            Quaternion rotation = Quaternion.AngleAxis(roll, muzzle) * Quaternion.LookRotation(muzzle, outward);
            Vector3 center = chest.position + frame * backOffset;
            model.transform.SetPositionAndRotation(center - muzzle * gripToCenter, rotation);
        }

        private Quaternion ChestFrame()
        {
            Vector3 up = neck != null ? neck.position - chest.position : transform.up;
            if (up.sqrMagnitude < 1e-8f) up = transform.up;
            up.Normalize();
            Vector3 side = leftUpperArm != null && rightUpperArm != null ? rightUpperArm.position - leftUpperArm.position : transform.right;
            side = Vector3.ProjectOnPlane(side, up);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.ProjectOnPlane(transform.right, up);
            return Quaternion.LookRotation(Vector3.Cross(side.normalized, up), up);
        }

        private void CreateModel(WeaponDefinition definition)
        {
            if (model != null) Destroy(model);
            modelDefinition = definition;
            ownerLayerApplied = false;
            if (definition.WorldModelPrefab == null) return;

            // 뼈의 큰 스케일과 무관하도록 플레이어 루트 아래에 두고 월드 포즈만 맞춘다.
            model = Instantiate(definition.WorldModelPrefab, transform, false);
            model.name = definition.DisplayName + " (등)";
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.On;
        }

        private void OnDisable()
        {
            if (model != null) model.SetActive(false);
        }

        private void OnDestroy()
        {
            if (model != null) Destroy(model);
        }
    }
}
