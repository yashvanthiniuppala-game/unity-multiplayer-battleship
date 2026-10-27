using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the Lobby Scene UI — Create Game / Join Game buttons.
/// Attach to an empty GameObject in LobbyScene.
/// Wire buttons in Inspector:
///   CreateGameButton.OnClick → OnCreateGameClicked()
///   JoinGameButton.OnClick  → OnJoinGameClicked()
/// </summary>
public class LobbyController : MonoBehaviour
{
    [Header("Player Info")]
    [SerializeField] private TMP_Text playerNameDisplay;

    [Header("Create Section")]
    [SerializeField] private Button   createBtn;
    [SerializeField] private TMP_Text lobbyCodeDisplay;

    [Header("Join Section")]
    [SerializeField] private Button         joinBtn;
    [SerializeField] private TMP_InputField joinCodeInput;

    [Header("Status")]
    [SerializeField] private TMP_Text lobbyStatus;

    [SerializeField] private string gameSceneName = "GameScene";

    private bool subscribed;

    private void Start()
    {
        if (playerNameDisplay != null)
            playerNameDisplay.text = $"Playing as: <b>{AuthManager.LocalPlayerName}</b>";

        if (lobbyCodeDisplay != null)
            lobbyCodeDisplay.text = string.Empty;

        SetStatus(string.Empty, false);

        // Most common setup mistake: pressing Play from LobbyScene instead of LoginScene.
        if (!ManagersReady(out string problem))
        {
            SetStatus(problem, true);
            SetButtonsInteractable(false);
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    // ── Create Game ───────────────────────────────────────────────────────────
    public async void OnCreateGameClicked()
    {
        if (!ManagersReady(out string problem))
        {
            SetStatus(problem, true);
            return;
        }

        SetButtonsInteractable(false);
        SetStatus("Creating lobby...", false);

        try
        {
            string lobbyCode = await LobbyManager.Instance
                .CreateLobby($"{AuthManager.LocalPlayerName}'s Game");

            if (lobbyCodeDisplay != null)
                lobbyCodeDisplay.text = $"Your code: <b>{lobbyCode}</b>";

            SetStatus("Starting relay...", false);
            string relayCode = await RelayManager.Instance.StartHost();
            await LobbyManager.Instance.SetRelayCode(relayCode);

            SetStatus("Waiting for opponent…", false);

            NetworkManager.Singleton.OnClientConnectedCallback += OnOpponentConnected;
            subscribed = true;

            // In case a client connected before we subscribed
            CheckShouldStartGame();
        }
        catch (Exception e)
        {
            Debug.LogError($"[LobbyController] Create error: {e}");
            SetStatus($"Error: {e.Message}", true);
            SetButtonsInteractable(true);
        }
    }

    private void OnOpponentConnected(ulong clientId)
    {
        CheckShouldStartGame();
    }

    private void CheckShouldStartGame()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        // ConnectedClients includes the host (id 0) + the joining client
        if (nm.ConnectedClients.Count >= 2)
        {
            Unsubscribe();
            SetStatus("Opponent connected! Starting game...", false);

            // Host loads the scene for ALL clients via Netcode's SceneManager
            nm.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    // ── Join Game ─────────────────────────────────────────────────────────────
    public async void OnJoinGameClicked()
    {
        if (!ManagersReady(out string problem))
        {
            SetStatus(problem, true);
            return;
        }

        string code = joinCodeInput != null ? joinCodeInput.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(code))
        {
            SetStatus("Please enter a lobby code.", true);
            return;
        }

        SetButtonsInteractable(false);
        SetStatus("Joining lobby...", false);

        try
        {
            await LobbyManager.Instance.JoinLobby(code);
            SetStatus("Joined! Waiting for relay code...", false);

            string relayCode = await LobbyManager.Instance.WaitForRelayCode();
            SetStatus("Connecting via relay...", false);

            await RelayManager.Instance.StartClient(relayCode);
            SetStatus("Connected! Waiting for host to start...", false);
            // Host calls NetworkManager.SceneManager.LoadScene — the client follows automatically
        }
        catch (Exception e)
        {
            Debug.LogError($"[LobbyController] Join error: {e}");
            SetStatus($"Error: {e.Message}", true);
            SetButtonsInteractable(true);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static bool ManagersReady(out string problem)
    {
        if (NetworkManager.Singleton == null || LobbyManager.Instance == null ||
            RelayManager.Instance == null || AuthManager.Instance == null)
        {
            problem = "Managers not found. Press Play from LoginScene (not LobbyScene).";
            return false;
        }
        problem = null;
        return true;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnOpponentConnected;
    }

    private void SetStatus(string msg, bool isError)
    {
        if (lobbyStatus == null) return;
        lobbyStatus.text  = msg;
        lobbyStatus.color = isError
            ? new Color(0.898f, 0.224f, 0.208f)
            : new Color(0.33f, 0.33f, 0.33f);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (createBtn     != null) createBtn.interactable     = interactable;
        if (joinBtn       != null) joinBtn.interactable       = interactable;
        if (joinCodeInput != null) joinCodeInput.interactable = interactable;
    }
}
