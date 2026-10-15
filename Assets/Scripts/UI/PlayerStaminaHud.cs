using OskarMike.Network.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OskarMike.UI
{
    // Presentation only: stamina remains owned and updated by the server.
    [RequireComponent(typeof(PlayerStamina), typeof(PlayerNetworkController))]
    public sealed class PlayerStaminaHud : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset font;
        private PlayerStamina stamina;
        private PlayerNetworkController controller;
        private GameObject hud;
        private RectTransform fill;
        private Image fillImage;
        private TMP_Text label;

        private void Awake()
        {
            stamina = GetComponent<PlayerStamina>();
            controller = GetComponent<PlayerNetworkController>();
        }

        private void LateUpdate()
        {
            bool visible = controller.IsSpawned && controller.IsOwner && controller.GameplayInputEnabled;
            if (!visible)
            {
                if (hud != null) hud.SetActive(false);
                return;
            }

            if (hud == null) CreateHud();
            hud.SetActive(true);
            float ratio = Mathf.Clamp01(stamina.Ratio);
            fill.anchorMax = new Vector2(ratio, 1f);
            fillImage.color = stamina.IsExhausted
                ? new Color(1f, 0.3f, 0.22f)
                : new Color(0.3f, 0.85f, 0.65f);
            label.SetText("스태미나  {0:0}%", ratio * 100f);
        }

        private void CreateHud()
        {
            hud = new GameObject("Local Stamina HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            hud.transform.SetParent(transform, false);
            var canvas = hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = hud.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var track = new GameObject("Stamina Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(hud.transform, false);
            var trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = trackRect.anchorMax = new Vector2(0.5f, 0f);
            trackRect.pivot = new Vector2(0.5f, 0f);
            trackRect.anchoredPosition = new Vector2(0f, 60f);
            trackRect.sizeDelta = new Vector2(300f, 16f);
            var background = track.GetComponent<Image>();
            background.color = new Color(0.04f, 0.06f, 0.08f, 0.9f);
            background.raycastTarget = false;

            var bar = new GameObject("Stamina Fill", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(track.transform, false);
            fill = bar.GetComponent<RectTransform>();
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fillImage = bar.GetComponent<Image>();
            fillImage.raycastTarget = false;

            var text = new GameObject("Stamina Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(track.transform, false);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0f);
            textRect.anchoredPosition = new Vector2(0f, 6f);
            textRect.sizeDelta = new Vector2(0f, 30f);
            label = text.GetComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = 22f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
        }

        private void OnDisable()
        {
            if (hud != null) hud.SetActive(false);
        }
    }
}
