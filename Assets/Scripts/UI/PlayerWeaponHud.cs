using OskarMike.Network.Player;
using OskarMike.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OskarMike.UI
{
    /// <summary>
    /// 오너 전용 무장 HUD: 무기 이름/탄약, 1·2 슬롯 표시, 크로스헤어, 히트마커.
    /// PlayerLootHud 와 같은 방식으로 런타임에 TMP 를 생성한다.
    /// </summary>
    [RequireComponent(typeof(PlayerWeaponLoadout))]
    public sealed class PlayerWeaponHud : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset font;
        [Min(0.05f)] [SerializeField] private float hitMarkerDuration = 0.15f;

        private PlayerWeaponLoadout loadout;
        private PlayerNetworkController controller;
        private PlayerHealth health;
        private GameObject hud;
        private TMP_Text ammoLabel;
        private TMP_Text slotLabel;
        private TMP_Text crosshair;
        private float hitMarkerUntil;
        private bool lastHitHead;

        private void Awake()
        {
            loadout = GetComponent<PlayerWeaponLoadout>();
            controller = GetComponent<PlayerNetworkController>();
            health = GetComponent<PlayerHealth>();
        }

        private void OnEnable() => loadout.HitConfirmed += OnHitConfirmed;

        private void OnDisable()
        {
            loadout.HitConfirmed -= OnHitConfirmed;
            if (hud != null) hud.SetActive(false);
        }

        private void OnHitConfirmed(bool head)
        {
            lastHitHead = head;
            hitMarkerUntil = Time.time + hitMarkerDuration;
        }

        private void LateUpdate()
        {
            string scene = SceneManager.GetActiveScene().name;
            bool visible = loadout.IsSpawned && loadout.IsOwner && (health == null || !health.Dead) &&
                (controller == null || controller.GameplayInputEnabled) &&
                (scene == "GameMap" || scene == "Hideout" || scene == "EnemyAITest" ||
                 scene == "MovementTest" || scene.StartsWith("RangeCourse"));
            if (!visible) { if (hud != null) hud.SetActive(false); return; }
            if (hud == null) BuildHud();
            hud.SetActive(true);

            var def = loadout.ActiveDefinition;
            var ammo = loadout.ActiveAmmo;
            if (def == null) ammoLabel.text = "무장 없음";
            else
            {
                string magColor = ammo.Magazine == 0 ? "#FF6060" : ammo.Magazine <= def.MagazineSize / 4 ? "#FFC860" : "#FFFFFF";
                string status = loadout.IsReloading ? "\n<size=70%>재장전 중...</size>"
                    : ammo.Magazine == 0 ? (ammo.Reserve > 0 ? "\n<size=70%>[R] 재장전</size>" : "\n<size=70%>탄약 없음</size>")
                    : string.Empty;
                ammoLabel.text = $"<size=70%>{def.DisplayName}</size>\n<color={magColor}>{ammo.Magazine}</color> <size=70%>/ {ammo.Reserve}</size>{status}";
            }

            slotLabel.text = SlotText(WeaponSlot.Primary, "주무장") + "     " + SlotText(WeaponSlot.Secondary, "보조무장");

            bool hit = Time.time < hitMarkerUntil;
            crosshair.text = hit ? "×" : "+";
            crosshair.color = hit ? (lastHitHead ? new Color(1f, .35f, .35f) : Color.white) : new Color(1f, 1f, 1f, .8f);
        }

        private string SlotText(WeaponSlot slot, string label)
        {
            var def = loadout.GetDefinition(slot);
            string key = slot == WeaponSlot.Primary ? "1" : "2";
            if (def == null) return $"<color=#666666>[{key}] {label} 없음</color>";
            if (slot == WeaponSlot.Primary && loadout.PrimaryBlocked)
                return $"<color=#777777>[{key}] {def.DisplayName} (더플백 착용 중)</color>";
            bool active = loadout.ActiveSlot == slot;
            return active ? $"<color=#FFD54A>[{key}] {def.DisplayName}</color>" : $"<color=#BBBBBB>[{key}] {def.DisplayName}</color>";
        }

        private void BuildHud()
        {
            hud = new GameObject("무장 표시", typeof(RectTransform), typeof(Canvas));
            hud.transform.SetParent(transform, false);
            var canvas = hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 23;

            ammoLabel = CreateLabel("탄약", new Vector2(1, 0), new Vector2(-40, 40), new Vector2(360, 120), 34, TextAlignmentOptions.BottomRight);
            slotLabel = CreateLabel("슬롯", new Vector2(1, 0), new Vector2(-40, 165), new Vector2(600, 36), 20, TextAlignmentOptions.BottomRight);
            crosshair = CreateLabel("조준점", new Vector2(.5f, .5f), Vector2.zero, new Vector2(40, 40), 26, TextAlignmentOptions.Center);
            ((RectTransform)crosshair.transform).pivot = new Vector2(.5f, .5f);
        }

        private TMP_Text CreateLabel(string name, Vector2 anchor, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(hud.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var label = go.GetComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.richText = true;
            return label;
        }
    }
}
