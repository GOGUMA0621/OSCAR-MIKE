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
        [Tooltip("탄 퍼짐 원뿔 반각(도). 스프레이 곡선이 비어 있을 때만 사용.")]
        [Range(0f, 10f)] [SerializeField] private float spreadDegrees = 0.4f;

        [Header("리코일 패턴 (조준점이 튀는 궤적)")]
        [Tooltip("가로: 발 순번(0 = 첫 발 직전), 세로: 누적 위쪽 반동(도). 곡선 모양 = 연사 시 조준점이 올라가는 궤적.")]
        [SerializeField] private AnimationCurve recoilPitchCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(30f, 6f));
        [Tooltip("가로: 발 순번, 세로: 누적 좌우 반동(도, + = 오른쪽).")]
        [SerializeField] private AnimationCurve recoilYawCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(30f, 0f));
        [Tooltip("두 곡선 전체 배율")]
        [Min(0f)] [SerializeField] private float recoilCurveScale = 1f;
        [Tooltip("곡선 위에 매 발 더하는 무작위 좌우 반동(도)")]
        [Min(0f)] [SerializeField] private float recoilRandomYaw = 0.15f;
        [Tooltip("쏘지 않을 때 조준점이 돌아오는 속도(도/초)")]
        [Min(0f)] [SerializeField] private float recoilRecovery = 8f;
        [Tooltip("누적 반동 중 자동으로 돌아오는 비율. 1 = 완전히 원위치, 0 = 플레이어가 직접 끌어내려야 함.")]
        [Range(0f, 1f)] [SerializeField] private float recoilReturnRatio = 0.8f;

        [Header("스프레이 (조준점에서 탄이 흩어지는 정도)")]
        [Tooltip("가로: 발 순번, 세로: 퍼짐 원뿔 반각(도). 비어 있으면 spreadDegrees 고정값 사용.")]
        [SerializeField] private AnimationCurve spreadCurve = new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(30f, 1.4f));
        [Tooltip("마지막 발사 후 이 시간이 지나면 연사 카운터가 0으로 (초)")]
        [Min(0.05f)] [SerializeField] private float patternResetTime = 0.35f;

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

        [Header("조준 (ADS, 우클릭)")]
        [Tooltip("모델 기준 가늠자 위치 (구멍/노치 중심). 가늠쇠와 함께 설정하면 조준 자세를 자동 계산한다.")]
        [SerializeField] private Vector3 sightRear;
        [Tooltip("모델 기준 가늠쇠 위치 (가늠쇠 끝/점 중심).")]
        [SerializeField] private Vector3 sightFront;
        [Tooltip("조준 시 눈(카메라)에서 가늠자까지 거리 (m)")]
        [Min(0.02f)] [SerializeField] private float aimEyeRelief = 0.12f;
        [Tooltip("가늠자·가늠쇠를 설정하지 않았을 때만 쓰는 조준 위치 (카메라 기준, 회전 없음)")]
        [SerializeField] private Vector3 aimViewOffset = new Vector3(0f, -0.12f, 0.3f);
        [Tooltip("조준 시 카메라 시야각(도)")]
        [Range(20f, 90f)] [SerializeField] private float aimFov = 50f;
        [Tooltip("조준 전환에 걸리는 시간(초)")]
        [Min(0.01f)] [SerializeField] private float aimTime = 0.18f;
        [Tooltip("조준 중 탄 퍼짐 배율 (스프레이 곡선 × 자세 배율 위에 곱)")]
        [Range(0f, 1f)] [SerializeField] private float aimSpreadMultiplier = 0.35f;
        [Tooltip("조준 중 이동 속도 배율 (서버 적용, 조준 중 질주 불가)")]
        [Range(0.1f, 1f)] [SerializeField] private float aimMoveSpeedMultiplier = 0.6f;
        [Tooltip("조준 중 1인칭 모델 반동 킥 배율")]
        [Range(0f, 1f)] [SerializeField] private float aimKickMultiplier = 0.4f;

        [Header("3인칭 모델 (위치는 오른손, 방향은 몸 정면 기준)")]
        [SerializeField] private GameObject worldModelPrefab;
        [Tooltip("손 위치에서의 오프셋 (몸 기준 좌표, m)")]
        [SerializeField] private Vector3 worldOffset = Vector3.zero;
        [Tooltip("몸 정면 기준 회전. (0,0,0) = 총구가 캐릭터 정면")]
        [SerializeField] private Vector3 worldEuler = Vector3.zero;
        [SerializeField] private Vector3 worldMuzzle = new Vector3(0f, 0.05f, 0.5f);
        [Tooltip("켜면 위 오프셋 대신 오른손 '손 프레임'(손목→중지 뿌리 = +Z, 손바닥 법선 = +Y) 기준으로 붙인다. " +
                 "무기 애니메이션 FBX 의 총-손 관계를 그대로 재현할 때 사용.")]
        [SerializeField] private bool useHandGrip;
        [Tooltip("손 프레임 기준 총 원점(그립) 위치 (m)")]
        [SerializeField] private Vector3 handGripOffset = Vector3.zero;
        [Tooltip("손 프레임 기준 총 회전")]
        [SerializeField] private Vector3 handGripEuler = Vector3.zero;
        [Tooltip("켜면 3인칭에서 왼손을 IK 로 총의 보조손 위치에 붙인다 (PlayerWeaponHandIK). 체형이 다른 모델에서도 왼손이 총에서 뜨지 않게 함.")]
        [SerializeField] private bool useSupportHandIK;
        [Tooltip("총 모델 기준 왼손(LeftHand 본) 위치 (m)")]
        [SerializeField] private Vector3 supportHandOffset = Vector3.zero;
        [Tooltip("총 모델 기준 왼손 '손 프레임' 회전 (+Z = 손목→중지 뿌리, +Y = 손바닥 법선 계열)")]
        [SerializeField] private Vector3 supportHandEuler = Vector3.zero;

        public string WeaponId => weaponId;
        public string DisplayName => displayName;
        public WeaponSlot Slot => slot;
        public float Damage => damage;
        public float FireInterval => fireInterval;
        public bool Automatic => automatic;
        public float Range => range;
        public float SpreadDegrees => spreadDegrees;
        public AnimationCurve RecoilPitchCurve => recoilPitchCurve;
        public AnimationCurve RecoilYawCurve => recoilYawCurve;
        public float RecoilCurveScale => recoilCurveScale;
        public float RecoilRandomYaw => recoilRandomYaw;
        public float RecoilRecovery => recoilRecovery;
        public float RecoilReturnRatio => recoilReturnRatio;
        public float PatternResetTime => patternResetTime;

        /// <summary>shotIndex 번째 발의 누적 리코일 (x = 위쪽 pitch, y = 오른쪽 yaw, 도).</summary>
        public Vector2 RecoilAt(float shotIndex)
        {
            float pitch = recoilPitchCurve != null && recoilPitchCurve.length > 0 ? recoilPitchCurve.Evaluate(shotIndex) : 0f;
            float yaw = recoilYawCurve != null && recoilYawCurve.length > 0 ? recoilYawCurve.Evaluate(shotIndex) : 0f;
            return new Vector2(pitch, yaw) * recoilCurveScale;
        }

        /// <summary>shotIndex 번째 발을 쏜 직후 더해지는 반동 = 곡선(n+1) - 곡선(n).</summary>
        public Vector2 RecoilKick(int shotIndex) => RecoilAt(shotIndex + 1) - RecoilAt(shotIndex);

        /// <summary>shotIndex 번째 발의 기본 퍼짐(도, 자세·이동 배율 적용 전).</summary>
        public float SpreadAt(int shotIndex) =>
            spreadCurve != null && spreadCurve.length > 0 ? Mathf.Max(0f, spreadCurve.Evaluate(shotIndex)) : spreadDegrees;

        public int MagazineSize => magazineSize;
        public int StartingReserve => startingReserve;
        public float ReloadTime => reloadTime;
        public float DrawTime => drawTime;
        public GameObject ViewModelPrefab => viewModelPrefab;
        public Vector3 ViewOffset => viewOffset;
        public Quaternion ViewRotation => Quaternion.Euler(viewEuler);
        public Vector3 ViewMuzzle => viewMuzzle;
        public float ViewKick => viewKick;
        public Vector3 AimViewOffset => aimViewOffset;
        public Vector3 SightRear => sightRear;
        public Vector3 SightFront => sightFront;
        public bool HasSightLine => (sightFront - sightRear).sqrMagnitude > 1e-6f;

        /// <summary>
        /// 조준 시 1인칭 모델의 카메라 기준 위치·회전.
        /// 가늠자→가늠쇠 선이 카메라 정면(+Z) 축과 일치하도록 계산한다 — 조준선이 총열과 기울어 있어도
        /// 가늠쇠가 정확히 화면 중앙(탄이 나가는 방향)에 온다.
        /// </summary>
        public void GetAimPose(out Vector3 position, out Quaternion rotation)
        {
            if (!HasSightLine)
            {
                position = aimViewOffset;
                rotation = ViewRotation;
                return;
            }
            Vector3 sightDirection = (sightFront - sightRear).normalized;
            rotation = Quaternion.Inverse(Quaternion.LookRotation(sightDirection, Vector3.up));
            position = new Vector3(0f, 0f, aimEyeRelief) - rotation * sightRear;
        }
        public float AimFov => aimFov;
        public float AimTime => aimTime;
        public float AimSpreadMultiplier => aimSpreadMultiplier;
        public float AimMoveSpeedMultiplier => aimMoveSpeedMultiplier;
        public float AimKickMultiplier => aimKickMultiplier;
        public GameObject WorldModelPrefab => worldModelPrefab;
        public Vector3 WorldOffset => worldOffset;
        public Quaternion WorldRotation => Quaternion.Euler(worldEuler);
        public Vector3 WorldMuzzle => worldMuzzle;
        public bool UseHandGrip => useHandGrip;
        public Vector3 HandGripOffset => handGripOffset;
        public Quaternion HandGripRotation => Quaternion.Euler(handGripEuler);
        public bool UseSupportHandIK => useSupportHandIK;
        public Vector3 SupportHandOffset => supportHandOffset;
        public Quaternion SupportHandRotation => Quaternion.Euler(supportHandEuler);
    }
}
