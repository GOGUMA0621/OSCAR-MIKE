using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OskarMike.Core
{
    /// <summary>
    /// 비동기 씬 로딩 + 로딩 스크린 관리.
    /// - 네트워크 씬 전환 (GameMap) 시에도 로딩 화면 표시
    /// - 로컬 씬 전환 (MainMenu, Lobby) 시에도 사용 가능
    /// </summary>
    public class LoadingScreenManager : MonoBehaviour
    {
        public static LoadingScreenManager Instance { get; private set; }

        [SerializeField] private GameObject loadingCanvasPrefab;

        private GameObject loadingOverlay;
        private Slider progressBar;
        private TextMeshProUGUI statusText;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>로컬 씬 로드 (MainMenu, Lobby 등)</summary>
        public void LoadScene(string sceneName)
        {
            StartCoroutine(LoadSceneAsync(sceneName));
        }

        /// <summary>네트워크 씬 로드 (GameMap 등) — NetworkLobbyFlow 내부에서 호출</summary>
        public void LoadNetworkScene(string sceneName)
        {
            ShowLoading("네트워크 씬 로딩 중...");
            // NetworkSceneManager는 외부에서 호출하므로 로딩만 표시
        }

        public void ShowLoading(string message = "로딩 중...")
        {
            EnsureOverlay();
            if (statusText != null) statusText.text = message;
            if (loadingOverlay != null) loadingOverlay.SetActive(true);
            if (progressBar != null) progressBar.value = 0f;
        }

        public void HideLoading()
        {
            if (loadingOverlay != null) loadingOverlay.SetActive(false);
        }

        public void SetProgress(float t)
        {
            if (progressBar != null) progressBar.value = t;
        }

        public void SetStatus(string msg)
        {
            if (statusText != null) statusText.text = msg;
        }

        private IEnumerator LoadSceneAsync(string sceneName)
        {
            ShowLoading($"'{sceneName}' 로딩 중...");
            yield return null;

            var asyncOp = SceneManager.LoadSceneAsync(sceneName);
            asyncOp.allowSceneActivation = false;

            while (asyncOp.progress < 0.9f)
            {
                SetProgress(asyncOp.progress);
                yield return null;
            }

            SetProgress(1f);
            SetStatus("완료!");

            asyncOp.allowSceneActivation = true;
            yield return new WaitForSeconds(0.3f);

            HideLoading();
        }

        private void EnsureOverlay()
        {
            if (loadingOverlay != null) return;

            loadingOverlay = new GameObject("LoadingOverlay", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = loadingOverlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;

            var scaler = loadingOverlay.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var root = loadingOverlay.transform as RectTransform;

            // 배경
            var bg = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(root, false);
            var brt = bg.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.sizeDelta = Vector2.zero;
            var bimg = bg.GetComponent<Image>();
            bimg.color = new Color(0, 0, 0, 0.7f);

            // 상태 텍스트
            var st = new GameObject("StatusText", typeof(RectTransform), typeof(TextMeshProUGUI));
            st.transform.SetParent(root, false);
            var srt = st.GetComponent<RectTransform>();
            srt.anchoredPosition = Vector2.zero;
            srt.sizeDelta = new Vector2(600, 80);
            statusText = st.GetComponent<TextMeshProUGUI>();
            statusText.text = "로딩 중...";
            statusText.alignment = TextAlignmentOptions.Center;
            statusText.fontSize = 32;
            statusText.color = Color.white;

            // 프로그레스 바
            var pb = new GameObject("ProgressBar", typeof(RectTransform), typeof(Slider));
            pb.transform.SetParent(root, false);
            var prt = pb.GetComponent<RectTransform>();
            prt.anchoredPosition = new Vector2(0, -60);
            prt.sizeDelta = new Vector2(400, 20);
            progressBar = pb.GetComponent<Slider>();
            progressBar.interactable = false;

            // 프로그레스 바 배경
            var fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(pb.transform, false);
            var fart = fillArea.GetComponent<RectTransform>();
            fart.anchorMin = Vector2.zero;
            fart.anchorMax = Vector2.one;
            fart.sizeDelta = Vector2.zero;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            var frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.sizeDelta = Vector2.zero;
            var fimg = fill.GetComponent<Image>();
            fimg.color = new Color(0.2f, 0.5f, 1f);

            progressBar.fillRect = frt;
            progressBar.value = 0f;

            DontDestroyOnLoad(loadingOverlay);
            loadingOverlay.SetActive(false);
        }
    }
}
