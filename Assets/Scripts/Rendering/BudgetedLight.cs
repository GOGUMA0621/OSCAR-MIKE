using System.Collections.Generic;
using UnityEngine;

namespace OskarMike.Rendering
{
    /// <summary>
    /// <see cref="LightShadowBudget"/>가 관리하는 실시간 라이트 표시.
    /// 등기구 프리팹(CeilingLightFixture)이나 절차적 방 프리팹의 라이트에 붙인다.
    /// 로컬 렌더링 전용 — 네트워크 상태 없음.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class BudgetedLight : MonoBehaviour
    {
        private static readonly List<BudgetedLight> active = new List<BudgetedLight>();
        public static IReadOnlyList<BudgetedLight> Active => active;

        [Tooltip("켜져 있을 때 쓸 그림자 종류. None이면 예산에 들어와도 그림자를 켜지 않는다.")]
        [SerializeField] private LightShadows shadowType = LightShadows.Soft;

        public Light Light { get; private set; }
        public LightShadows ShadowType => shadowType;

        private void Awake()
        {
            Light = GetComponent<Light>();
        }

        private void OnEnable()
        {
            if (Light == null) Light = GetComponent<Light>();
            active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
            // 관리가 끝나면 원래 상태(켜짐 + 그림자)로 둔다.
            if (Light != null)
            {
                Light.enabled = true;
                Light.shadows = shadowType;
            }
        }
    }
}
