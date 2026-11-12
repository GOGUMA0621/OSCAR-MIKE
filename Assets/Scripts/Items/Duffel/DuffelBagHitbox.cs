using UnityEngine;

namespace OskarMike.Items
{
    /// <summary>
    /// 더플백 피격 판정용 콜라이더 마커. 무기 레이캐스트가 이 컴포넌트를 찾으면
    /// 서버에서 <see cref="DuffelBag.ServerApplyHit"/> 를 호출한다.
    /// "Hitbox" 레이어(물리 충돌 없음, 레이캐스트만)에 배치한다.
    /// </summary>
    public sealed class DuffelBagHitbox : MonoBehaviour
    {
        public DuffelBag bag;

        private void Awake()
        {
            if (bag == null) bag = GetComponentInParent<DuffelBag>();
        }
    }
}
