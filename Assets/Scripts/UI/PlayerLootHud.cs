using OskarMike.Items;
using OskarMike.Network.Player;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OskarMike.UI
{
    /// <summary>
    /// 오너 전용 파밍 HUD: 슬롯/손 상태, 더플백 가치, 상호작용 프롬프트.
    /// PlayerHealthHud 와 같은 방식으로 런타임에 TMP 를 생성한다.
    /// </summary>
    [RequireComponent(typeof(PlayerLootCarrier))]
    public sealed class PlayerLootHud : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset font;
        [Min(0.1f)] [SerializeField] private float lossFlashDuration = 1.2f;

        private PlayerLootCarrier carrier;
        private PlayerHealth health;
        private GameObject hud;
        private TMP_Text statusLabel;
        private TMP_Text promptLabel;
        private DuffelBag observedBag;
        private int lastLoss;
        private float lossFlashUntil;

        private void Awake()
        {
            carrier = GetComponent<PlayerLootCarrier>();
            health = GetComponent<PlayerHealth>();
        }

        private void LateUpdate()
        {
            string scene = SceneManager.GetActiveScene().name;
            bool visible = carrier.IsSpawned && carrier.IsOwner && (health == null || !health.Dead) &&
                (scene == "GameMap" || scene == "Hideout" || scene == "EnemyAITest" ||
                 scene == "MovementTest" || scene.StartsWith("RangeCourse"));
            if (!visible) { if (hud != null) hud.SetActive(false); return; }
            if (hud == null) BuildHud();
            hud.SetActive(true);

            DuffelBag bag = carrier.CarriedBag;
            ObserveBag(bag != null ? bag : carrier.LookedAtBag);

            var sb = new System.Text.StringBuilder(128);
            sb.Append("슬롯 ").Append(carrier.UsedSlots).Append('/').Append(carrier.SlotCount);
            sb.Append("   ·   손: ").Append(carrier.HasHandItem ? "아이템 있음" : "비어 있음");
            if (carrier.HasAnyItem) sb.Append("   ·   소지 가치 ").Append(carrier.CarriedItemsValue.ToString("N0")).Append("원");
            if (bag != null)
            {
                sb.Append("\n더플백 ").Append(bag.StoredValue.ToString("N0")).Append("원  ·  이동 속도 감소 / 권총만 사용 가능");
            }
            if (Time.time < lossFlashUntil) sb.Append("\n<color=#FF6060>더플백 피격! -").Append(lastLoss.ToString("N0")).Append("원</color>");
            statusLabel.text = sb.ToString();

            promptLabel.text = BuildPrompt();
        }

        private string BuildPrompt()
        {
            DuffelBag looked = carrier.LookedAtBag;
            string bagValue = looked != null ? $"  ({looked.StoredValue:N0}원)" : string.Empty;
            return carrier.Prompt switch
            {
                PlayerLootCarrier.InteractPromptKind.PickUp => "[F] 줍기",
                PlayerLootCarrier.InteractPromptKind.PickUpToHand => "[F] 손으로 들기 (슬롯 가득 참)",
                PlayerLootCarrier.InteractPromptKind.SlotsFull => "슬롯과 손이 가득 찼습니다",
                PlayerLootCarrier.InteractPromptKind.Deposit => "[F] 더플백에 넣기" + bagValue,
                PlayerLootCarrier.InteractPromptKind.Shoulder => "[F] 더플백 메기" + bagValue,
                PlayerLootCarrier.InteractPromptKind.Throw => "[G] 더플백 던지기  ·  앉은 상태에서 [G] 내려놓기",
                _ => carrier.HasHandItem && !carrier.IsCarryingBag ? "[G] 손에 든 아이템 내려놓기" : string.Empty
            };
        }

        private void ObserveBag(DuffelBag bag)
        {
            if (observedBag == bag) return;
            if (observedBag != null) observedBag.ValueLost -= OnBagValueLost;
            observedBag = bag;
            if (observedBag != null) observedBag.ValueLost += OnBagValueLost;
        }

        private void OnBagValueLost(int loss)
        {
            lastLoss = loss;
            lossFlashUntil = Time.time + lossFlashDuration;
        }

        private void BuildHud()
        {
            hud = new GameObject("파밍 표시", typeof(RectTransform), typeof(Canvas));
            hud.transform.SetParent(transform, false);
            var canvas = hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 22;

            statusLabel = CreateLabel("슬롯", new Vector2(.5f, 0), new Vector2(0, 160), new Vector2(900, 90), 20);
            statusLabel.verticalAlignment = VerticalAlignmentOptions.Bottom;
            promptLabel = CreateLabel("상호작용", new Vector2(.5f, .5f), new Vector2(0, -70), new Vector2(700, 40), 22);
        }

        private TMP_Text CreateLabel(string name, Vector2 anchor, Vector2 position, Vector2 size, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(hud.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(.5f, anchor.y);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var label = go.GetComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.richText = true;
            return label;
        }

        private void OnDisable()
        {
            if (hud != null) hud.SetActive(false);
            ObserveBag(null);
        }
    }
}
