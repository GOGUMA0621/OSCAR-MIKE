using OskarMike.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OskarMike.UI
{
    /// <summary>
    /// 1회성 UI 빌더. 에디터에서 이 스크립트를 Canvas에 붙이고
    /// "Create Lobby UI" 컨텍스트 메뉴를 실행하면 UI가 생성된다.
    /// 생성 후 프리팹 저장하고 이 컴포넌트는 제거하면 된다.
    ///
    /// 런타임에서는 아무것도 생성하지 않는다 (Awake 무시).
    /// </summary>
    [ExecuteAlways]
    public class LobbyUIInstaller : MonoBehaviour
    {
        [ContextMenu("Create Lobby UI")]
        public void CreateLobbyUI()
        {
            var lobby = GetComponent<LobbyUIController>();
            if (lobby == null)
            {
                Debug.LogError("LobbyUIController가 필요합니다.");
                return;
            }

            var root = transform as RectTransform;
            if (root == null) return;

            // 기존 자식 제거
            while (root.childCount > 0)
                DestroyImmediate(root.GetChild(0).gameObject);

            float y = 220;

            var sessionState = CreateLabel(root, "SessionState", "세션: -", ref y);
            lobby.SetSessionStateText(sessionState);

            var joinCode = CreateLabel(root, "JoinCode", "참가 코드: -", ref y);
            lobby.SetJoinCodeText(joinCode);

            var readyState = CreateLabel(root, "ReadyState", "상태: 준비 안됨", ref y);
            lobby.SetLocalReadyStateText(readyState);

            y -= 20;

            var startBtn = CreateButton(root, "StartGame", "게임 시작", ref y);
            lobby.SetStartGameButton(startBtn);

            var readyBtn = CreateButton(root, "ReadyToggle", "준비", ref y);
            lobby.SetReadyButton(readyBtn);

            var readyLbl = CreateLabel(root, "ReadyButtonLabel", "준비", ref y);
            lobby.SetReadyButtonLabel(readyLbl);

            var leaveBtn = CreateButton(root, "Leave", "나가기", ref y);
            lobby.SetLeaveButton(leaveBtn);

            // 플레이어 목록 (오른쪽)
            var playerList = CreateLabel(root, "PlayerList", "플레이어\n- 없음", ref y,
                new Vector2(400, 300), TextAlignmentOptions.TopLeft);
            var prt = playerList.GetComponent<RectTransform>();
            prt.anchoredPosition = new Vector2(400, 400);
            lobby.SetPlayerListText(playerList);

            // NetworkLobbyFlow 찾기
            var flow = FindFirstObjectByType<NetworkLobbyFlow>();
            if (flow != null) lobby.SetNetworkLobbyFlow(flow);

            Debug.Log("Lobby UI 생성 완료. 프리팹 저장 후 LobbyUIInstaller를 제거하세요.");
        }

        private TextMeshProUGUI CreateLabel(RectTransform parent, string name, string text, ref float y,
            Vector2? size = null, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject($"Lbl_{name}", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size ?? new Vector2(300, 36);
            rt.anchoredPosition = new Vector2(0, y);

            y -= (rt.sizeDelta.y + 8);

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.alignment = align;
            tmp.fontSize = 20;
            tmp.color = Color.white;

            return tmp;
        }

        private Button CreateButton(RectTransform parent, string name, string label, ref float y)
        {
            var go = new GameObject($"Btn_{name}", typeof(RectTransform), typeof(Button), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(240, 44);
            rt.anchoredPosition = new Vector2(0, y);
            y -= 56;

            var img = go.GetComponent<Image>();
            img.color = new Color(0.22f, 0.25f, 0.28f, 0.95f);

            var btn = go.GetComponent<Button>();
            btn.navigation = new Navigation { mode = Navigation.Mode.None };

            var lbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            lbl.transform.SetParent(go.transform, false);
            var lrt = lbl.GetComponent<RectTransform>();
            lrt.sizeDelta = rt.sizeDelta;
            lrt.anchoredPosition = Vector2.zero;
            var tmp = lbl.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 22;
            tmp.color = Color.white;

            return btn;
        }
    }
}
