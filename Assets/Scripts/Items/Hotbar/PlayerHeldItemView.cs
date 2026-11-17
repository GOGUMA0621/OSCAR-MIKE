using OskarMike.Network.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace OskarMike.Items
{
    /// <summary>
    /// 손에 든 아이템 표시 (모든 피어에서 로컬 실행, 네트워크 상태 없음 — <see cref="PlayerLootCarrier.HandItem"/> 를 따른다).
    /// - 3인칭: 휴머노이드 오른손 앞에 아이템 모델 사본 (오너 본인 사본은 LocalPlayerVisual 레이어)
    /// - 1인칭: 오너 카메라 아래 오른쪽 아래에 사본
    /// 사본은 아이템의 NetworkPrefab 을 복제한 뒤 네트워크·물리·스크립트를 제거한 순수 렌더 모델이며,
    /// 큰 아이템은 maxWorldSize / maxViewSize 에 맞게 축소된다.
    /// </summary>
    // PlayerWeaponAimPose(-40) 가 상체를 비튼 뒤의 손 위치를 쓴다.
    [DefaultExecutionOrder(10)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerLootCarrier))]
    public sealed class PlayerHeldItemView : MonoBehaviour
    {
        [Header("3인칭 (오른손 기준)")]
        [Tooltip("몸 기준 오프셋 (오른손 위치에서)")]
        [SerializeField] private Vector3 worldOffset = new Vector3(-0.05f, 0f, 0.12f);
        [Tooltip("가장 긴 변이 이보다 크면 축소 (m)")]
        [Min(0.05f)] [SerializeField] private float maxWorldSize = 0.45f;
        [Tooltip("손 본이 없을 때 몸 기준 위치")]
        [SerializeField] private Vector3 fallbackWorldOffset = new Vector3(0.25f, 1.1f, 0.35f);

        [Header("1인칭 (카메라 기준)")]
        [SerializeField] private Vector3 viewOffset = new Vector3(0.3f, -0.26f, 0.62f);
        [SerializeField] private Vector3 viewEuler = new Vector3(5f, -20f, 0f);
        [Min(0.05f)] [SerializeField] private float maxViewSize = 0.17f;
        [Tooltip("아이템을 바꿨을 때 1인칭 사본이 아래에서 올라오는 거리")]
        [SerializeField] private float drawDrop = 0.2f;
        [Min(0.1f)] [SerializeField] private float drawSpeed = 6f;

        private PlayerLootCarrier carrier;
        private PlayerNetworkController controller;
        private PlayerHealth health;
        private Transform hand;
        private Transform viewRoot;
        private int localLayer;

        private Transform worldPivot;
        private Transform viewPivot;
        private string shownItemId = string.Empty;
        private float draw;

        private void Awake()
        {
            carrier = GetComponent<PlayerLootCarrier>();
            controller = GetComponent<PlayerNetworkController>();
            health = GetComponent<PlayerHealth>();
            localLayer = LayerMask.NameToLayer("LocalPlayerVisual");
            var animator = GetComponentInChildren<Animator>(true);
            if (animator != null && animator.isHuman) hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var cam = GetComponentInChildren<Camera>(true);
            if (cam != null) viewRoot = cam.transform;
        }

        private void LateUpdate()
        {
            bool alive = carrier.IsSpawned && (health == null || !health.Dead);
            LootSlotEntry entry = alive && carrier.HasHandItem ? carrier.HandItem : default;
            string itemId = entry.IsEmpty ? string.Empty : entry.ItemId.ToString();
            if (itemId != shownItemId) Rebuild(itemId);
            if (worldPivot == null) return;

            // 3인칭: 오른손 앞, 몸 방향 기준
            Quaternion body = transform.rotation;
            Vector3 origin = hand != null ? hand.position + body * worldOffset : transform.position + body * fallbackWorldOffset;
            worldPivot.SetPositionAndRotation(origin, body);

            // 1인칭: 오너가 조작 중일 때만
            if (viewPivot != null)
            {
                bool showView = carrier.IsOwner && controller != null && controller.GameplayInputEnabled;
                viewPivot.gameObject.SetActive(showView);
                draw = Mathf.MoveTowards(draw, 0f, drawSpeed * drawDrop * Time.deltaTime);
                viewPivot.localPosition = viewOffset + Vector3.down * draw;
                viewPivot.localRotation = Quaternion.Euler(viewEuler);
            }
        }

        private void Rebuild(string itemId)
        {
            Clear();
            shownItemId = itemId;
            if (string.IsNullOrEmpty(itemId)) return;

            var definition = carrier.FindDefinition(itemId);
            if (definition == null || definition.NetworkPrefab == null) return;

            worldPivot = LootItemVisual.Create(definition.NetworkPrefab.gameObject, transform, maxWorldSize, ShadowCastingMode.On, "손에 든 아이템 (3인칭)");
            if (worldPivot != null && carrier.IsOwner && localLayer >= 0) LootItemVisual.SetLayer(worldPivot.gameObject, localLayer);

            if (carrier.IsOwner && viewRoot != null)
            {
                viewPivot = LootItemVisual.Create(definition.NetworkPrefab.gameObject, viewRoot, maxViewSize, ShadowCastingMode.Off, "손에 든 아이템 (1인칭)");
                draw = drawDrop;
            }
        }

        private void Clear()
        {
            if (worldPivot != null) Destroy(worldPivot.gameObject);
            if (viewPivot != null) Destroy(viewPivot.gameObject);
            worldPivot = viewPivot = null;
            shownItemId = string.Empty;
        }

        private void OnDisable() => Clear();
    }
}
