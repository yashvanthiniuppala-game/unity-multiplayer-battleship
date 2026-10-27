using Unity.Collections;
using Unity.Netcode;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// GameScene HUD: player names, turn indicator and the result overlay.
/// Plain MonoBehaviour (not networked) — it reads state from GameManager.
/// Wire: RematchButton → OnRematchClicked, MainMenuButton → OnMainMenuClicked
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("Top Bar")]
    [SerializeField] private TMP_Text player1NameText;
    [SerializeField] private TMP_Text player2NameText;
    [SerializeField] private TMP_Text turnIndicatorText;

    [Header("Result Overlay")]
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private TMP_Text   resultText;
    [SerializeField] private TMP_Text   resultSubText;
    [SerializeField] private Button     rematchButton;
    [SerializeField] private Button     mainMenuButton;

    [Header("Scenes")]
    [Tooltip("Main Menu returns here. LobbyScene, not LoginScene, so the NetworkManager isn't duplicated.")]
    [SerializeField] private string lobbySceneName = "LobbyScene";

    private GameManager gm;
    private bool opponentLeft;
    private bool leaving;

    private void Start()
    {
        ShowResult(false);
        SetText(turnIndicatorText, "Waiting for game...");

        if (GameManager.Instance != null) Bind(GameManager.Instance);
        else                              GameManager.Spawned += Bind;

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnDestroy()
    {
        GameManager.Spawned -= Bind;

        if (gm != null)
        {
            gm.CurrentTurn.OnValueChanged -= OnIntChanged;
            gm.Result.OnValueChanged      -= OnIntChanged;
            gm.XPlayerName.OnValueChanged -= OnNameChanged;
            gm.OPlayerName.OnValueChanged -= OnNameChanged;
        }

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void Bind(GameManager manager)
    {
        GameManager.Spawned -= Bind;
        gm = manager;

        gm.CurrentTurn.OnValueChanged += OnIntChanged;
        gm.Result.OnValueChanged      += OnIntChanged;
        gm.XPlayerName.OnValueChanged += OnNameChanged;
        gm.OPlayerName.OnValueChanged += OnNameChanged;

        Refresh();
    }

    private void OnIntChanged(int previous, int current) => Refresh();
    private void OnNameChanged(FixedString64Bytes previous, FixedString64Bytes current) => Refresh();

    private void Refresh()
    {
        if (gm == null || opponentLeft) return;

        string xName = NameOrDefault(gm.XPlayerName.Value, "Player 1");
        string oName = NameOrDefault(gm.OPlayerName.Value, "Player 2");

        SetText(player1NameText, $"X: {xName}");
        SetText(player2NameText, $"O: {oName}");

        int result = gm.Result.Value;
        if (result == GameManager.ResultNone)
        {
            string turnName = gm.CurrentTurn.Value == GameManager.X ? xName : oName;
            SetText(turnIndicatorText, gm.IsLocalTurn ? "Your turn" : $"{turnName}'s turn");
            ShowResult(false);
            return;
        }

        SetText(turnIndicatorText, "Game Over");

        if (result == GameManager.ResultDraw)
        {
            ShowResult(true, "It's a Draw!", "Game Over", allowRematch: true);
        }
        else
        {
            int  winner   = result == GameManager.ResultXWins ? GameManager.X : GameManager.O;
            bool localWon = winner == gm.LocalSymbol;
            string title  = $"{(winner == GameManager.X ? xName : oName)} Wins!";
            ShowResult(true, title, localWon ? "You win!" : "You lose", allowRematch: true);
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (leaving) return;

        // Host: the other player left. Client: we lost the host.
        opponentLeft = true;
        SetText(turnIndicatorText, "Disconnected");
        ShowResult(true, "Opponent left", "The match has ended.", allowRematch: false);
    }

    // ── Buttons ───────────────────────────────────────────────────────────────
    public void OnRematchClicked()
    {
        if (gm != null && !opponentLeft)
            gm.RequestRematchRpc();
    }

    public async void OnMainMenuClicked()
    {
        if (leaving) return;
        leaving = true;
        if (mainMenuButton != null) mainMenuButton.interactable = false;

        if (LobbyManager.Instance != null)
            await LobbyManager.Instance.LeaveLobby();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        SceneManager.LoadScene(lobbySceneName);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void ShowResult(bool show, string title = "", string sub = "", bool allowRematch = true)
    {
        if (resultPanel != null) resultPanel.SetActive(show);
        if (!show) return;

        SetText(resultText, title);
        SetText(resultSubText, sub);
        if (rematchButton != null) rematchButton.gameObject.SetActive(allowRematch);
    }

    private static string NameOrDefault(FixedString64Bytes value, string fallback)
    {
        string s = value.ToString();
        return string.IsNullOrWhiteSpace(s) ? fallback : s;
    }

    private static void SetText(TMP_Text label, string text)
    {
        if (label != null) label.text = text;
    }
}
