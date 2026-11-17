using OskarMike.Network.Player;
using UnityEngine;

namespace OskarMike.Weapons
{
    /// <summary>
    /// 선택적 피격 부위 마커. 적/플레이어의 머리 콜라이더 등에 붙이면 해당 부위로 판정한다.
    /// 마커가 없는 콜라이더는 몸통(Body)으로 판정한다.
    /// </summary>
    public sealed class WeaponHitbox : MonoBehaviour
    {
        public HitRegion region = HitRegion.Body;
    }
}
