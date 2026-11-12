using OskarMike.Network.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace OskarMike.Weapons
{
    /// <summary>
    /// 활성 무장 모델 표시 (네트워크 상태 없음, <see cref="PlayerWeaponLoadout"/> 를 읽기만 한다).
    /// - 오너: 카메라 하위 1인칭 모델 + 반동 킥 + 전환(드로우) 연출
    /// - 모든 피어: 오른손 본 하위 3인칭 모델 (오너 자신의 카메라에서는 LocalPlayerVisual 레이어로 숨김)
    /// - 발사 시 짧은 머즐 플래시 라이트
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerWeaponLoadout))]
    public sealed class PlayerWeaponView : MonoBehaviour
    {
        [Tooltip("비우면 플레이어 카메라")]
        [SerializeField] private Transform viewRoot;
        [Tooltip("비우면 휴머노이드 오른손 본 (없으면 플레이어 루트)")]
        [SerializeField] private Transform worldAnchor;
        [SerializeField] private Vector3 fallbackWorldOffset = new Vector3(0.25f, 1.25f, 0.35f);

        [Header("연출")]
        [Tooltip("전환 시 1인칭 모델이 아래에서 올라오는 거리")]
        [SerializeField] private float drawDrop = 0.25f;
        [SerializeField] private float kickRecoverSpeed = 0.8f;
        [SerializeField] private float kickPitchDegrees = 4f;
        [Tooltip("총구에 잠깐 표시할 플래시 메시 (카메라를 향한 가산 쿼드 등)")]
        [SerializeField] private GameObject muzzleFlashPrefab;
        [SerializeField] private Vector2 muzzleFlashScale = new Vector2(0.8f, 1.2f);
        [SerializeField] private Color muzzleColor = new Color(1f, 0.78f, 0.45f);
        [SerializeField] private float muzzleIntensity = 1.5f;
        [Tooltip("너무 크면 바닥(발밑)까지 비춰서 발에서 번쩍이는 것처럼 보인다")]
        [SerializeField] private float muzzleRange = 2f;
        [SerializeField] private float muzzleDuration = 0.05f;

        [Header("총알 궤적 (트레이서)")]
        [Tooltip("가산(Additive) 파티클 머티리얼. 비우면 궤적을 그리지 않는다.")]
        [SerializeField] private Material tracerMaterial;
        [Tooltip("궤적 이동 속도 (m/s). 실제 탄속보다 느려야 눈에 보인다.")]
        [Min(10f)] [SerializeField] private float tracerSpeed = 220f;
        [Tooltip("빛줄기 길이 (m)")]
        [Min(0.1f)] [SerializeField] private float tracerLength = 3f;
        [Min(0.001f)] [SerializeField] private float tracerWidth = 0.04f;
        [SerializeField] private Color tracerColor = new Color(1f, 0.75f, 0.35f);
        [Tooltip("이보다 가까운 탄착은 궤적 생략 (m)")]
        [Min(0f)] [SerializeField] private float tracerMinDistance = 1.5f;
        [Tooltip("발사 몇 발마다 궤적을 그릴지 (1 = 매 발)")]
        [Range(1, 5)] [SerializeField] private int tracerEvery = 1;
        [Range(4, 64)] [SerializeField] private int tracerPoolSize = 16;

        private PlayerWeaponLoadout loadout;
        private PlayerNetworkController controller;
        private PlayerHealth health;
        private readonly GameObject[] viewModels = new GameObject[2];
        private readonly GameObject[] worldModels = new GameObject[2];
        private Transform muzzleRoot;
        private Light muzzleLight;
        private GameObject muzzleFlash;
        private Transform worldScaleRoot;
        private float muzzleUntil;
        private float kick;
        private int localLayer;
        private bool ownerLayerApplied;
        private readonly System.Collections.Generic.List<BulletTracer> tracers = new System.Collections.Generic.List<BulletTracer>();
        private int tracerCounter;

        private void Awake()
        {
            loadout = GetComponent<PlayerWeaponLoadout>();
            controller = GetComponent<PlayerNetworkController>();
            health = GetComponent<PlayerHealth>();
            localLayer = LayerMask.NameToLayer("LocalPlayerVisual");

            if (viewRoot == null)
            {
                var cam = GetComponentInChildren<Camera>(true);
                if (cam != null) viewRoot = cam.transform;
            }
            if (worldAnchor == null)
            {
                var animator = GetComponentInChildren<Animator>(true);
                if (animator != null && animator.isHuman) worldAnchor = animator.GetBoneTransform(HumanBodyBones.RightHand);
            }
        }

        private void OnEnable() => loadout.ShotFired += OnShotFired;
        private void OnDisable()
        {
            loadout.ShotFired -= OnShotFired;
            SetAllInactive();
        }

        private void OnDestroy()
        {
            foreach (var t in tracers) if (t != null) Destroy(t.gameObject);
            tracers.Clear();
            for (int i = 0; i < 2; i++)
            {
                if (viewModels[i] != null) Destroy(viewModels[i]);
                if (worldModels[i] != null) Destroy(worldModels[i]);
            }
        }

        private void LateUpdate()
        {
            if (!loadout.IsSpawned || (health != null && health.Dead)) { SetAllInactive(); return; }

            bool owner = loadout.IsOwner;
            bool showView = owner && controller != null && controller.GameplayInputEnabled;
            int active = (int)loadout.ActiveSlot;

            if (owner && !ownerLayerApplied) ApplyOwnerLayer();

            for (int i = 0; i < 2; i++)
            {
                var def = loadout.GetDefinition((WeaponSlot)i);
                bool isActive = i == active && def != null;

                if (isActive && showView && viewModels[i] == null) viewModels[i] = CreateViewModel(def);
                if (viewModels[i] != null) viewModels[i].SetActive(isActive && showView);

                if (isActive && worldModels[i] == null) worldModels[i] = CreateWorldModel(def);
                if (worldModels[i] != null)
                {
                    worldModels[i].SetActive(isActive);
                    if (isActive) AlignWorldModel(worldModels[i].transform, def);
                }
            }

            kick = Mathf.MoveTowards(kick, 0f, kickRecoverSpeed * Time.deltaTime);
            var activeDef = loadout.ActiveDefinition;
            var view = active >= 0 && active < 2 ? viewModels[active] : null;
            if (view != null && view.activeSelf && activeDef != null)
            {
                float draw = loadout.IsDrawing ? drawDrop : 0f;
                float reloadDip = loadout.IsReloading ? Mathf.Sin(loadout.ReloadProgress * Mathf.PI) * 0.12f : 0f;
                view.transform.localPosition = activeDef.ViewOffset
                    + Vector3.down * (draw + reloadDip)
                    + Vector3.back * kick;
                float kickRatio = activeDef.ViewKick > 0f ? kick / activeDef.ViewKick : 0f;
                view.transform.localRotation = activeDef.ViewRotation
                    * Quaternion.Euler(-kickPitchDegrees * kickRatio - reloadDip * 120f, 0f, 0f);
            }

            if (muzzleRoot != null && muzzleRoot.gameObject.activeSelf && Time.time >= muzzleUntil) muzzleRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// 3인칭 총: 위치는 손(애니메이션)을 따라가고, 방향은 손 회전과 무관하게 몸 정면 기준으로 맞춘다.
        /// (Animator 갱신 이후인 LateUpdate 에서 호출)
        /// </summary>
        private void AlignWorldModel(Transform model, WeaponDefinition def)
        {
            if (worldAnchor == null) return;
            Quaternion body = transform.rotation;
            model.SetPositionAndRotation(worldAnchor.position + body * def.WorldOffset, body * def.WorldRotation);
        }

        private void OnShotFired(bool localPrediction, Vector3 end)
        {
            var def = loadout.ActiveDefinition;
            if (def == null) return;
            int active = (int)loadout.ActiveSlot;
            if (localPrediction) kick = def.ViewKick;

            // 오너는 1인칭 총구, 원격은 3인칭 총구에서 플래시 + 궤적
            GameObject model = loadout.IsOwner ? viewModels[active] : worldModels[active];
            if (model == null) return;
            Vector3 local = loadout.IsOwner ? def.ViewMuzzle : def.WorldMuzzle;
            Flash(model.transform, local);
            LaunchTracer(model.transform.TransformPoint(local), end);
        }

        private void LaunchTracer(Vector3 from, Vector3 to)
        {
            if (tracerMaterial == null) return;
            tracerCounter++;
            if (tracerEvery > 1 && tracerCounter % tracerEvery != 0) return;
            if ((to - from).sqrMagnitude < tracerMinDistance * tracerMinDistance) return;

            BulletTracer tracer = null;
            foreach (var t in tracers) if (t != null && !t.Busy) { tracer = t; break; }
            if (tracer == null)
            {
                if (tracers.Count >= tracerPoolSize) return; // 풀이 가득 차면 이번 궤적은 생략
                tracer = BulletTracer.Create(tracerMaterial);
                tracers.Add(tracer);
            }
            tracer.Launch(from, to, tracerSpeed, tracerLength, tracerWidth, tracerColor);
        }

        private void Flash(Transform parent, Vector3 localPosition)
        {
            if (muzzleRoot == null)
            {
                muzzleRoot = new GameObject("머즐 플래시").transform;
                muzzleLight = muzzleRoot.gameObject.AddComponent<Light>();
                muzzleLight.type = LightType.Point;
                muzzleLight.shadows = LightShadows.None;
                if (muzzleFlashPrefab != null)
                {
                    muzzleFlash = Instantiate(muzzleFlashPrefab, muzzleRoot, false);
                    Strip(muzzleFlash, ShadowCastingMode.Off);
                }
            }
            muzzleRoot.SetParent(parent, false);
            muzzleRoot.localPosition = localPosition;
            muzzleRoot.localRotation = Quaternion.identity;
            muzzleRoot.localScale = Vector3.one;
            muzzleRoot.gameObject.layer = parent.gameObject.layer;
            if (muzzleFlash != null)
            {
                SetLayer(muzzleFlash, parent.gameObject.layer);
                muzzleFlash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                muzzleFlash.transform.localScale = Vector3.one * Random.Range(muzzleFlashScale.x, muzzleFlashScale.y);
            }
            muzzleLight.color = muzzleColor;
            muzzleLight.intensity = muzzleIntensity;
            muzzleLight.range = muzzleRange;
            muzzleRoot.gameObject.SetActive(true);
            muzzleUntil = Time.time + muzzleDuration;
        }

        private GameObject CreateViewModel(WeaponDefinition def)
        {
            if (def.ViewModelPrefab == null || viewRoot == null) return null;
            var go = Instantiate(def.ViewModelPrefab, viewRoot, false);
            go.name = def.DisplayName + " (1인칭)";
            go.transform.localPosition = def.ViewOffset;
            go.transform.localRotation = def.ViewRotation;
            Strip(go, ShadowCastingMode.Off);
            return go;
        }

        private GameObject CreateWorldModel(WeaponDefinition def)
        {
            if (def.WorldModelPrefab == null) return null;
            Transform parent = worldAnchor != null ? WorldScaleRoot() : transform;
            var go = Instantiate(def.WorldModelPrefab, parent, false);
            go.name = def.DisplayName + " (3인칭)";
            go.transform.localPosition = worldAnchor != null ? def.WorldOffset : fallbackWorldOffset;
            go.transform.localRotation = def.WorldRotation;
            Strip(go, ShadowCastingMode.On);
            if (loadout.IsOwner && localLayer >= 0) SetLayer(go, localLayer);
            return go;
        }

        /// <summary>
        /// 리그 본에 큰 스케일(예: FBX 0.01 단위 → 손 본 lossyScale 100)이 걸려 있어도
        /// 무기가 실제 미터 크기로 보이도록 스케일을 상쇄하는 중간 노드.
        /// </summary>
        private Transform WorldScaleRoot()
        {
            if (worldScaleRoot != null) return worldScaleRoot;
            worldScaleRoot = new GameObject("무기 손 기준").transform;
            worldScaleRoot.SetParent(worldAnchor, false);
            Vector3 s = worldAnchor.lossyScale;
            worldScaleRoot.localScale = new Vector3(SafeInverse(s.x), SafeInverse(s.y), SafeInverse(s.z));
            return worldScaleRoot;
        }

        private static float SafeInverse(float v) => Mathf.Abs(v) > 0.0001f ? 1f / v : 1f;

        private void ApplyOwnerLayer()
        {
            ownerLayerApplied = true;
            if (localLayer < 0) return;
            foreach (var model in worldModels) if (model != null) SetLayer(model, localLayer);
        }

        private static void Strip(GameObject go, ShadowCastingMode shadows)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = shadows;
        }

        private static void SetLayer(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        private void SetAllInactive()
        {
            for (int i = 0; i < 2; i++)
            {
                if (viewModels[i] != null) viewModels[i].SetActive(false);
                if (worldModels[i] != null) worldModels[i].SetActive(false);
            }
            if (muzzleRoot != null) muzzleRoot.gameObject.SetActive(false);
        }
    }
}
