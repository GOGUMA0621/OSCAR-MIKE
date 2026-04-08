using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Scene Names")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private string lobbySceneName = "Lobby";
    [SerializeField] private string gameSceneName = "GameMap";
    [SerializeField] private string hideoutSceneName = "Hideout";

    public string MainMenuSceneName => mainMenuSceneName;
    public string LobbySceneName => lobbySceneName;
    public string GameSceneName => gameSceneName;
    public string HideoutSceneName => hideoutSceneName;

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

    public void LoadMainMenu()
    {
        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void LoadLobby()
    {
        SceneManager.LoadScene(lobbySceneName);
    }

    public void LoadGameMap()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    public void LoadHideout()
    {
        SceneManager.LoadScene(hideoutSceneName);
    }
}
