using System.Collections.Generic;
using OskarMike.Items;
using OskarMike.Network.Player;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OskarMike.UI
{
    /// <summary>
    /// 오너 전용 파밍 HUD (런타임 생성, 1920×1080 기준 스케일).
    /// - 가운데 하단: 정사각형 아이템 슬롯 그리드 (키 3~6, 아이템 아이콘·가치, 손에 든 칸 강조)
    ///   아이콘은 <see cref="LootItemIconCache"/> 가 아이템 모델을 찍어 만든다. 이름은 손에 든 아이템만 그리드 위에 표시.
    /// - 그리드 위: 손에 든 아이템 / 소지 가치 / 더플백 상태
    /// - 화면 중앙 아래: 상호작용 프롬프트
    /// </summary>
    [RequireComponent(typeof(PlayerLootCarrier))]
    public sealed class PlayerLootHud : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset font;
        [Min(0.1f)] [SerializeField] private float lossFlashDuration = 1.2f;

        [Header("그리드")]
        [Tooltip("정사각형 칸 한 변 (1920×1080 기준 px)")]
        [Min(32f)] [SerializeField] private float slotSize = 88f;
        [Min(0f)] [SerializeField] private float cellSpacing = 8f;
        [Min(0f)] [SerializeField] private float bottomMargin = 28f;
        [Tooltip("슬롯 1번에 대응하는 키 숫자 (3 → 3,4,5,6)")]
        [SerializeField] private int firstSlotKey = 3;

        private static readonly Color CellColor = new Color(0.05f, 0.07f, 0.09f, 0.72f);
        private static readonly Color CellEmptyColor = new Color(0.05f, 0.07f, 0.09f, 0.45f);
        private static readonly Color FrameColor = new Color(1f, 0.84f, 0.29f, 1f);
        private static readonly Color PriceColor = new Color(1f, 0.84f, 0.45f, 1f);

        private sealed class Cell
        {
            public Image Background;
            public Image Frame;
            public RawImage Icon;
            public TMP_Text Key;
            public TMP_Text Name;   // 아이콘을 만들 수 없을 때만 이름 표시
            public TMP_Text Price;
            public string Signature;
        }

        private PlayerLootCarrier carrier;
        private PlayerHealth health;
        private GameObject hud;
        private RectTransform gridRoot;
        private readonly List<Cell> cells = new List<Cell>();
        private TMP_Text statusLabel;
        private TMP_Text promptLabel;
        private DuffelBag observedBag;
        private int lastLoss;
        private float lossFlashUntil;
        private readonly Dictionary<string, string> nameCache = new Dictionary<string, string>();
        private readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(160);

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
            if (cells.Count != carrier.SlotCount) RebuildGrid();

            DuffelBag bag = carrier.CarriedBag;
            ObserveBag(bag != null ? bag : carrier.LookedAtBag);

            UpdateGrid();
            SetText(statusLabel, BuildStatus(bag));
            SetText(promptLabel, BuildPrompt());
        }

        // ══════════════════════════════════════════════════
        // 내용
        // ══════════════════════════════════════════════════

        private void UpdateGrid()
        {
            int selected = carrier.SelectedSlot;
            for (int i = 0; i < cells.Count; i++)
            {
                Cell cell = cells[i];
                LootSlotEntry entry = carrier.GetSlot(i);
                bool isSelected = i == selected;
                string signature = entry.IsEmpty ? (isSelected ? "E*" : "E") : entry.ItemId + "|" + entry.Price + (isSelected ? "*" : "");
                if (signature == cell.Signature) continue;
                cell.Signature = signature;

                cell.Frame.enabled = isSelected;
                cell.Background.color = entry.IsEmpty ? CellEmptyColor : CellColor;
                cell.Key.color = isSelected ? FrameColor : new Color(1f, 1f, 1f, 0.6f);
                if (entry.IsEmpty)
                {
                    cell.Icon.texture = null;
                    cell.Icon.enabled = false;
                    cell.Name.text = isSelected ? "빈손" : string.Empty;
                    cell.Name.color = new Color(1f, 1f, 1f, 0.7f);
                    cell.Price.text = string.Empty;
                }
                else
                {
                    Texture icon = LootItemIconCache.Get(carrier.FindDefinition(entry.ItemId.ToString()));
                    cell.Icon.texture = icon;
                    cell.Icon.enabled = icon != null;
                    cell.Name.text = icon != null ? string.Empty : DisplayName(entry);
                    cell.Name.color = Color.white;
                    cell.Price.text = entry.Price.ToString("N0");
                }
            }
        }

        private string BuildStatus(DuffelBag bag)
        {
            sb.Clear();
            if (carrier.HasHandItem)
            {
                LootSlotEntry held = carrier.HandItem;
                sb.Append("손: <b>").Append(DisplayName(held)).Append("</b>  <color=#FFD673>")
                  .Append(held.Price.ToString("N0")).Append("원</color>   ·   [G] 내려놓기  [X] 무기");
            }
            else if (carrier.WeaponHolstered)
            {
                sb.Append("빈손   ·   [F] 바로 들기  [X] 무기");
            }
            if (carrier.HasAnyItem)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("<size=85%>소지 ").Append(carrier.UsedSlots).Append('/').Append(carrier.SlotCount)
                  .Append("  ·  가치 ").Append(carrier.CarriedItemsValue.ToString("N0")).Append("원</size>");
            }
            if (bag != null)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("<size=85%>더플백 ").Append(bag.StoredValue.ToString("N0")).Append("원  ·  이동 속도 감소 / 권총만 사용 가능</size>");
            }
            if (Time.time < lossFlashUntil)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("<color=#FF6060>더플백 피격! -").Append(lastLoss.ToString("N0")).Append("원</color>");
            }
            return sb.ToString();
        }

        private string BuildPrompt()
        {
            DuffelBag looked = carrier.LookedAtBag;
            string bagValue = looked != null ? $"  ({looked.StoredValue:N0}원)" : string.Empty;
            return carrier.Prompt switch
            {
                PlayerLootCarrier.InteractPromptKind.PickUp => "[F] 줍기",
                PlayerLootCarrier.InteractPromptKind.PickUpToHand => "[F] 주워서 들기",
                PlayerLootCarrier.InteractPromptKind.SlotsFull => "슬롯이 가득 찼습니다",
                PlayerLootCarrier.InteractPromptKind.Deposit => "[F] 더플백에 넣기" + bagValue,
                PlayerLootCarrier.InteractPromptKind.Shoulder => "[F] 더플백 메기" + bagValue,
                PlayerLootCarrier.InteractPromptKind.Throw => "[G] 더플백 던지기  ·  앉은 상태에서 [G] 내려놓기",
                _ => string.Empty
            };
        }

        private string DisplayName(LootSlotEntry entry)
        {
            string id = entry.ItemId.ToString();
            if (nameCache.TryGetValue(id, out string cached)) return cached;
            var definition = carrier.FindDefinition(id);
            string name = definition != null && !string.IsNullOrEmpty(definition.DisplayName) ? definition.DisplayName : id;
            nameCache[id] = name;
            return name;
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label.text != text) label.text = text;
        }

        // ══════════════════════════════════════════════════
        // 더플백 피격 알림
        // ══════════════════════════════════════════════════

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

        // ══════════════════════════════════════════════════
        // 생성
        // ══════════════════════════════════════════════════

        private void BuildHud()
        {
            hud = new GameObject("파밍 표시", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            hud.transform.SetParent(transform, false);
            var canvas = hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 22;
            var scaler = hud.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var grid = new GameObject("슬롯 그리드", typeof(RectTransform));
            gridRoot = grid.GetComponent<RectTransform>();
            gridRoot.SetParent(hud.transform, false);
            gridRoot.anchorMin = gridRoot.anchorMax = new Vector2(.5f, 0f);
            gridRoot.pivot = new Vector2(.5f, 0f);
            gridRoot.anchoredPosition = new Vector2(0f, bottomMargin);

            statusLabel = CreateLabel(hud.transform, "상태", new Vector2(.5f, 0f), new Vector2(.5f, 0f),
                new Vector2(0f, bottomMargin + slotSize + 10f), new Vector2(900f, 100f), 20f, TextAlignmentOptions.Bottom);
            promptLabel = CreateLabel(hud.transform, "상호작용", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(0f, -70f), new Vector2(700f, 40f), 22f, TextAlignmentOptions.Center);
        }

        private void RebuildGrid()
        {
            foreach (Transform child in gridRoot) Destroy(child.gameObject);
            cells.Clear();

            int count = carrier.SlotCount;
            float width = count * slotSize + Mathf.Max(0, count - 1) * cellSpacing;
            gridRoot.sizeDelta = new Vector2(width, slotSize);

            for (int i = 0; i < count; i++)
            {
                var cellGo = new GameObject($"슬롯 {i + 1}", typeof(RectTransform));
                var rect = cellGo.GetComponent<RectTransform>();
                rect.SetParent(gridRoot, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = new Vector2(i * (slotSize + cellSpacing), 0f);
                rect.sizeDelta = new Vector2(slotSize, slotSize);

                var cell = new Cell
                {
                    Frame = CreateImage(rect, "선택 테두리", FrameColor, -3f),
                    Background = CreateImage(rect, "배경", CellColor, 0f)
                };
                var iconGo = new GameObject("아이콘", typeof(RectTransform), typeof(RawImage));
                var iconRect = iconGo.GetComponent<RectTransform>();
                iconRect.SetParent(rect, false);
                iconRect.anchorMin = Vector2.zero;
                iconRect.anchorMax = Vector2.one;
                iconRect.offsetMin = new Vector2(6f, 6f);
                iconRect.offsetMax = new Vector2(-6f, -6f);
                cell.Icon = iconGo.GetComponent<RawImage>();
                cell.Icon.raycastTarget = false;
                cell.Icon.enabled = false;

                cell.Key = CreateLabel(rect, "키", Vector2.up, Vector2.up, new Vector2(6f, -3f), new Vector2(30f, 22f), 15f, TextAlignmentOptions.TopLeft);
                cell.Key.text = (firstSlotKey + i).ToString();
                cell.Name = CreateLabel(rect, "이름", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(slotSize - 10f, slotSize - 24f), 14f, TextAlignmentOptions.Center);
                cell.Name.textWrappingMode = TextWrappingModes.Normal;
                cell.Name.overflowMode = TextOverflowModes.Ellipsis;
                cell.Price = CreateLabel(rect, "가치", Vector2.right, Vector2.right, new Vector2(-6f, 4f), new Vector2(slotSize - 12f, 20f), 13f, TextAlignmentOptions.BottomRight);
                cell.Price.color = PriceColor;
                cell.Price.outlineWidth = 0.2f;
                cell.Price.outlineColor = new Color32(0, 0, 0, 200);
                cells.Add(cell);
            }
        }

        /// <summary>셀을 채우는 이미지. inset 이 음수면 셀보다 바깥으로 커진다 (선택 테두리용).</summary>
        private static Image CreateImage(RectTransform parent, string name, Color color, float inset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private TMP_Text CreateLabel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size,
            float fontSize, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
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

        private void OnDisable()
        {
            if (hud != null) hud.SetActive(false);
            ObserveBag(null);
        }
    }
}
