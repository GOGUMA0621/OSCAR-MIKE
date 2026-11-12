using UnityEngine;

namespace OskarMike.Weapons
{
    /// <summary>무장 슬롯. 1번 = 주무장, 2번 = 보조무장.</summary>
    public enum WeaponSlot : byte
    {
        Primary = 0,
        Secondary = 1
    }

    /// <summary>
    /// 총기 한 종류의 데이터. 실제 사격 판정은 <see cref="PlayerWeaponLoadout"/> 가 서버에서 수행한다.
    /// </summary>
    [CreateAssetMenu(menuName = "OSCAR-MIKE/Weapons/Weapon Definition", fileName = "Weapon_")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("기본 정보")]
        [SerializeField] private string weaponId = "weapon";
        [SerializeField] private string displayName = "무기";
        [SerializeField] private WeaponSlot slot = WeaponSlot.Primary;

        [Header("사격")]
        [Tooltip("몸통 기준 피해. 머리는 DamageRules 에 따라 2배.")]
        [Min(0.1f)] [SerializeField] private float damage = 20f;
        [Tooltip("발사 간격(초)")]
        [Min(0.02f)] [SerializeField] private float fireInterval = 0.1f;
        [Tooltip("true = 누르고 있으면 연사, false = 클릭마다 1발")]
        [SerializeField] private bool automatic = true;
        [Min(1f)] [SerializeField] private float range = 300f;
        [Tooltip("탄 퍼짐 원뿔 반각(도)")]
        [Range(0f, 10f)] [SerializeField] private float spreadDegrees = 0.4f;

        [Header("탄약")]
        [Range(1, 200)] [SerializeField] private int magazineSize = 30;
        [Range(0, 999)] [SerializeField] private int startingReserve = 90;
        [Min(0.1f)] [SerializeField] private float reloadTime = 2.3f;
        [Tooltip("무기 전환 후 사격 가능까지 걸리는 시간(초)")]
        [Min(0f)] [SerializeField] private float drawTime = 0.45f;

        [Header("1인칭 모델 (카메라 하위)")]
        [SerializeField] private GameObject viewModelPrefab;
        [SerializeField] private Vector3 viewOffset = new Vector3(0.2f, -0.22f, 0.45f);
        [SerializeField] private Vector3 viewEuler = Vector3.zero;
        [Tooltip("1인칭 모델 기준 총구 위치 (머즐 플래시)")]
        [SerializeField] private Vector3 viewMuzzle = new Vector3(0f, 0.05f, 0.5f);
        [Tooltip("발사 시 뒤로 밀리는 거리")]
        [Min(0f)] [SerializeField] private float viewKick = 0.05f;

        [Header("3인칭 모델 (위치는 오른손, 방향은 몸 정면 기준)")]
        [SerializeField] private GameObject worldModelPrefab;
        [Tooltip("손 위치에서의 오프셋 (몸 기준 좌표, m)")]
        [SerializeField] private Vector3 worldOffset = Vector3.zero;
        [Tooltip("몸 정면 기준 회전. (0,0,0) = 총구가 캐릭터 정면")]
        [SerializeField] private Vector3 worldEuler = Vector3.zero;
        [SerializeField] private Vector3 worldMuzzle = new Vector3(0f, 0.05f, 0.5f);

        public string WeaponId => weaponId;
        public string DisplayName => displayName;
        public WeaponSlot Slot => slot;
        public float Damage => damage;
        public float FireInterval => fireInterval;
        public bool Automatic => automatic;
        public float Range => range;
        public float SpreadDegrees => spreadDegrees;
        public int MagazineSize => magazineSize;
        public int StartingReserve => startingReserve;
        public float ReloadTime => reloadTime;
        public float DrawTime => drawTime;
        public GameObject ViewModelPrefab => viewModelPrefab;
        public Vector3 ViewOffset => viewOffset;
        public Quaternion ViewRotation => Quaternion.Euler(viewEuler);
        public Vector3 ViewMuzzle => viewMuzzle;
        public float ViewKick => viewKick;
        public GameObject WorldModelPrefab => worldModelPrefab;
        public Vector3 WorldOffset => worldOffset;
        public Quaternion WorldRotation => Quaternion.Euler(worldEuler);
        public Vector3 WorldMuzzle => worldMuzzle;
    }
}
