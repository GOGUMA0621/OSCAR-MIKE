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
        [SerializeField] private Color muzzleColor = new Color(1f, 0.78f, 0.45f);
        [SerializeField] private float muzzleIntensity = 3f;
        [SerializeField] private float muzzleRange = 6f;
        [SerializeField] private float muzzleDuration = 0.05f;

        private PlayerWeaponLoadout loadout;
        private PlayerNetworkController controller;
        private PlayerHealth health;
        private readonly GameObject[] viewModels = new GameObject[2];
        private readonly GameObject[] worldModels = new GameObject[2];
        private Light muzzleLight;
        private float muzzleUntil;
        private float kick;
        private int localLayer;
        private bool ownerLayerApplied;

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
                if (worldModels[i] != null) worldModels[i].SetActive(isActive);
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

            if (muzzleLight != null && muzzleLight.enabled && Time.time >= muzzleUntil) muzzleLight.enabled = false;
        }

        private void OnShotFired(bool localPrediction)
        {
            var def = loadout.ActiveDefinition;
            if (def == null) return;
            int active = (int)loadout.ActiveSlot;
            if (localPrediction) kick = def.ViewKick;

            // 오너는 1인칭 총구, 원격은 3인칭 총구에 플래시
            GameObject model = loadout.IsOwner ? viewModels[active] : worldModels[active];
            if (model == null) return;
            Vector3 local = loadout.IsOwner ? def.ViewMuzzle : def.WorldMuzzle;
            Flash(model.transform, local);
        }

        private void Flash(Transform parent, Vector3 localPosition)
        {
            if (muzzleLight == null)
            {
                var go = new GameObject("머즐 플래시");
                muzzleLight = go.AddComponent<Light>();
                muzzleLight.type = LightType.Point;
                muzzleLight.shadows = LightShadows.None;
            }
            muzzleLight.color = muzzleColor;
            muzzleLight.intensity = muzzleIntensity;
            muzzleLight.range = muzzleRange;
            muzzleLight.transform.SetParent(parent, false);
            muzzleLight.transform.localPosition = localPosition;
            muzzleLight.enabled = true;
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
            Transform parent = worldAnchor != null ? worldAnchor : transform;
            var go = Instantiate(def.WorldModelPrefab, parent, false);
            go.name = def.DisplayName + " (3인칭)";
            go.transform.localPosition = worldAnchor != null ? def.WorldOffset : fallbackWorldOffset;
            go.transform.localRotation = def.WorldRotation;
            Strip(go, ShadowCastingMode.On);
            if (loadout.IsOwner && localLayer >= 0) SetLayer(go, localLayer);
            return go;
        }

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
            if (muzzleLight != null) muzzleLight.enabled = false;
        }
    }
}
