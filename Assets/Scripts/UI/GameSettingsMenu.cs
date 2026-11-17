using OskarMike.Core;
using OskarMike.Network.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OskarMike.UI
{
    /// <summary>
    /// 게임 중 Esc 로 여는 간단한 설정 창 (오너 전용, IMGUI — 씬에 EventSystem 이 없어도 동작).
    /// 열려 있는 동안 게임 입력을 끄고 커서를 풀며, 닫으면 다시 켠다.
    /// 값은 <see cref="GameSettings"/> (PlayerPrefs) 에 바로 저장된다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerNetworkController))]
    public sealed class GameSettingsMenu : MonoBehaviour
    {
        [Tooltip("한글 표시용 폰트 (Pretendard)")]
        [SerializeField] private Font font;
        [Tooltip("자체 Esc 메뉴가 있는 씬 (여기서는 열지 않는다)")]
        [SerializeField] private string[] excludedScenes = { "EnemyAITest" };

        private PlayerNetworkController controller;
        private PlayerHealth health;
        private bool open;
        private GUIStyle box, title, label, button, selected, value;

        public bool IsOpen => open;

        private void Awake()
        {
            controller = GetComponent<PlayerNetworkController>();
            health = GetComponent<PlayerHealth>();
        }

        private void Update()
        {
            if (!controller.IsSpawned || !controller.IsOwner) { open = false; return; }
            if (open && health != null && health.Dead) { open = false; return; }

            if (!EscapePressed()) return;
            if (open) Close();
            else if (controller.GameplayInputEnabled && !IsExcludedScene()) Open();
        }

        private void Open()
        {
            open = true;
            controller.DisableGameplayInput();
        }

        private void Close()
        {
            open = false;
            if (health == null || !health.Dead) controller.EnableGameplayInput();
        }

        private bool IsExcludedScene()
        {
            string scene = SceneManager.GetActiveScene().name;
            foreach (var s in excludedScenes) if (s == scene) return true;
            return false;
        }

        private static bool EscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private void OnGUI()
        {
            if (!open) return;
            BuildStyles();

            var previous = GUI.matrix;
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 720f), 0.6f, 1.5f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                float w = 460f, h = 250f;
                float x = (Screen.width / scale - w) * 0.5f, y = (Screen.height / scale - h) * 0.5f;
                GUI.Box(new Rect(x, y, w, h), GUIContent.none, box);
                GUI.Label(new Rect(x + 24, y + 16, w - 48, 34), "설정", title);

                // 조준 방식
                GUI.Label(new Rect(x + 24, y + 66, 140, 32), "조준 방식", label);
                bool hold = GameSettings.AimMode == AimInputMode.Hold;
                if (GUI.Button(new Rect(x + 170, y + 64, 120, 34), "누르고 있기", hold ? selected : button))
                    GameSettings.AimMode = AimInputMode.Hold;
                if (GUI.Button(new Rect(x + 300, y + 64, 120, 34), "토글", hold ? button : selected))
                    GameSettings.AimMode = AimInputMode.Toggle;

                // 조준 감도
                GUI.Label(new Rect(x + 24, y + 118, 140, 32), "조준 감도", label);
                float sens = GUI.HorizontalSlider(new Rect(x + 170, y + 130, 190, 20), GameSettings.AimSensitivity,
                    GameSettings.MinAimSensitivity, GameSettings.MaxAimSensitivity);
                GameSettings.AimSensitivity = Mathf.Round(sens * 20f) / 20f;
                GUI.Label(new Rect(x + 368, y + 118, 60, 32), GameSettings.AimSensitivity.ToString("0.00"), value);

                if (GUI.Button(new Rect(x + w - 144, y + h - 56, 120, 36), "닫기 (Esc)", button)) Close();
            }
            finally
            {
                GUI.matrix = previous;
            }
        }

        private void BuildStyles()
        {
            if (box != null) return;
            box = new GUIStyle(GUI.skin.box) { normal = { background = Solid(new Color(0.08f, 0.09f, 0.1f, 0.92f)) } };
            title = new GUIStyle(GUI.skin.label) { font = font, fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 18, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } };
            value = new GUIStyle(label) { alignment = TextAnchor.MiddleRight, normal = { textColor = Color.white } };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 16 };
            selected = new GUIStyle(button)
            {
                normal = { background = Solid(new Color(0.95f, 0.75f, 0.25f)), textColor = Color.black },
                hover = { background = Solid(new Color(1f, 0.82f, 0.35f)), textColor = Color.black },
                active = { background = Solid(new Color(0.85f, 0.65f, 0.2f)), textColor = Color.black }
            };
        }

        private static Texture2D Solid(Color color)
        {
            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private void OnDisable()
        {
            if (open) Close();
        }
    }
}
