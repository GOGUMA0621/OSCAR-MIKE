using UnityEngine;

namespace OskarMike.Items
{
    /// <summary>
    /// 아이템을 올려둘 테이블 표시. 윗면(BoxCollider 기준) 위에 <see cref="TableLootSpawner"/> 가 아이템을 놓는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class LootTableSurface : MonoBehaviour
    {
        [Tooltip("테이블 등급. 높을수록 고가치 아이템이 잘 나온다 (확률표는 TableLootSpawner).")]
        [SerializeField] private LootTableGrade grade = LootTableGrade.Common;
        [Tooltip("스폰할 카테고리. 아무것도 선택하지 않으면(Nothing) 전체 허용.")]
        [SerializeField] private LootCategoryMask allowedCategories = 0;
        [Tooltip("이 테이블에 놓을 아이템 개수 (최소~최대, 포함)")]
        [SerializeField] private Vector2Int itemCount = new Vector2Int(2, 4);
        [Tooltip("테이블 가장자리에서 띄울 거리 (m)")]
        [Min(0f)] [SerializeField] private float edgeInset = 0.25f;

        private BoxCollider box;

        public LootTableGrade Grade => grade;
        public LootCategoryMask AllowedCategories => allowedCategories;
        public bool AllowsCategory(LootCategory category) => allowedCategories.Allows(category);

        public int RollItemCount(System.Random random)
        {
            int min = Mathf.Max(0, Mathf.Min(itemCount.x, itemCount.y));
            int max = Mathf.Max(min, Mathf.Max(itemCount.x, itemCount.y));
            return random.Next(min, max + 1);
        }

        /// <summary>윗면의 월드 중심, 테이블 회전, 사용 가능한 반 크기(로컬 X/Z, 인셋 적용).</summary>
        public bool TryGetTop(out Vector3 center, out Quaternion rotation, out Vector2 halfSize)
        {
            if (box == null) box = GetComponent<BoxCollider>();
            center = default;
            rotation = transform.rotation;
            halfSize = default;
            if (box == null) return false;

            Vector3 scale = transform.lossyScale;
            Vector3 size = Vector3.Scale(box.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            center = transform.TransformPoint(box.center + new Vector3(0f, box.size.y * 0.5f, 0f));
            halfSize = new Vector2(
                Mathf.Max(0f, size.x * 0.5f - edgeInset),
                Mathf.Max(0f, size.z * 0.5f - edgeInset));
            // 회전값만 쓰고 스케일은 이미 반영했다.
            rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            return true;
        }

        private void OnValidate()
        {
            itemCount.x = Mathf.Max(0, itemCount.x);
            itemCount.y = Mathf.Max(itemCount.x, itemCount.y);
            edgeInset = Mathf.Max(0f, edgeInset);
        }

        private void OnDrawGizmosSelected()
        {
            if (!TryGetTop(out Vector3 center, out Quaternion rotation, out Vector2 half)) return;
            Gizmos.color = GradeColor(grade);
            Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0f, 0.05f, 0f), new Vector3(half.x * 2f, 0.1f, half.y * 2f));
        }

        private void OnDrawGizmos()
        {
            // 선택하지 않아도 등급을 구분할 수 있도록 윗면에 얇은 색 판을 그린다.
            if (!TryGetTop(out Vector3 center, out Quaternion rotation, out Vector2 half)) return;
            Color color = GradeColor(grade);
            color.a = 0.35f;
            Gizmos.color = color;
            Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
            Gizmos.DrawCube(new Vector3(0f, 0.01f, 0f), new Vector3(half.x * 2f, 0.02f, half.y * 2f));
        }

        public static Color GradeColor(LootTableGrade value)
        {
            return value switch
            {
                LootTableGrade.Uncommon => new Color(0.3f, 0.85f, 0.3f, 0.9f),
                LootTableGrade.Rare => new Color(0.25f, 0.55f, 1f, 0.9f),
                LootTableGrade.Epic => new Color(0.7f, 0.3f, 0.95f, 0.9f),
                LootTableGrade.Legendary => new Color(1f, 0.6f, 0.1f, 0.9f),
                _ => new Color(0.75f, 0.75f, 0.75f, 0.9f)
            };
        }
    }
}
