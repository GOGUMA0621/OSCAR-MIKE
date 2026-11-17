using OskarMike.Network.Player;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OskarMike.UI
{
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class PlayerHealthHud : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset font;
        private PlayerHealth health;
        private GameObject hud;
        private TMP_Text label;
        private void Awake() => health = GetComponent<PlayerHealth>();

        private void LateUpdate()
        {
            string scene = SceneManager.GetActiveScene().name;
            bool visible = health.IsSpawned && health.IsOwner &&
                (scene == "GameMap" || scene == "Hideout" || scene == "EnemyAITest" ||
                 scene == "MovementTest" || scene.StartsWith("RangeCourse"));
            if (!visible) { if (hud != null) hud.SetActive(false); return; }
            if (hud == null)
            {
                hud = new GameObject("체력 표시", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
                hud.transform.SetParent(transform, false);
                var canvas = hud.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 21;
                // 스태미나 HUD 와 같은 1080p 기준 스케일 (왼쪽 하단에 함께 배치)
                var scaler = hud.GetComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                var text = new GameObject("체력", typeof(RectTransform), typeof(TextMeshProUGUI));
                text.transform.SetParent(hud.transform, false);
                var rect = text.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = Vector2.zero;
                rect.anchoredPosition = new Vector2(40, 98);
                rect.sizeDelta = new Vector2(600, 40);
                label = text.GetComponent<TextMeshProUGUI>();
                label.font = font;
                label.fontSize = 24;
                label.alignment = TextAlignmentOptions.BottomLeft;
                label.raycastTarget = false;
            }
            hud.SetActive(true);
            label.color = health.Dead ? new Color(1f, .3f, .3f) : Color.white;
            if (health.Dead) label.text = "사망 — 세션 메뉴에서 나갈 수 있습니다.";
            else label.SetText("체력  {0:0} / {1:0}", health.Current, health.Maximum);
        }

        private void OnDisable() { if (hud != null) hud.SetActive(false); }
    }
}
