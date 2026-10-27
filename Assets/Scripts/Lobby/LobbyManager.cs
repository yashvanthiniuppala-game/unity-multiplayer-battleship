using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

/// <summary>
/// Wraps the Lobby service (from the Multiplayer Services package).
/// Lives on the NetworkManager GameObject in LoginScene (persists across scenes).
///
/// Host:   CreateLobby → SetRelayCode   (heartbeat runs automatically)
/// Client: JoinLobby   → WaitForRelayCode
/// </summary>
public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    private const string RelayCodeKey      = "RelayCode";
    private const int    MaxPlayers        = 2;
    private const float  HeartbeatInterval = 15f;   // lobby goes inactive after 30 s without one
    private const float  PollInterval      = 1.5f;  // stays under the GetLobby rate limit
    private const float  RelayWaitTimeout  = 30f;

    public Lobby CurrentLobby { get; private set; }
    public bool  IsLobbyHost  { get; private set; }

    private float heartbeatTimer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        if (transform.parent == null)
            DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!IsLobbyHost || CurrentLobby == null) return;

        heartbeatTimer -= Time.unscaledDeltaTime;
        if (heartbeatTimer <= 0f)
        {
            heartbeatTimer = HeartbeatInterval;
            SendHeartbeat(CurrentLobby.Id);
        }
    }

    private async void SendHeartbeat(string lobbyId)
    {
        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LobbyManager] Heartbeat failed: {e.Message}");
        }
    }

    // ── Host ──────────────────────────────────────────────────────────────────
    /// <summary>Creates a private 2-player lobby and returns its 6-character code.</summary>
    public async Task<string> CreateLobby(string lobbyName)
    {
        await LeaveLobby(); // clean up any previous lobby

        var options = new CreateLobbyOptions { IsPrivate = true }; // join by code only
        CurrentLobby   = await LobbyService.Instance.CreateLobbyAsync(lobbyName, MaxPlayers, options);
        IsLobbyHost    = true;
        heartbeatTimer = HeartbeatInterval;

        Debug.Log($"[LobbyManager] Created lobby {CurrentLobby.Id}, code {CurrentLobby.LobbyCode}");
        return CurrentLobby.LobbyCode;
    }

    /// <summary>Stores the Relay join code in the lobby so the client can read it.</summary>
    public async Task SetRelayCode(string relayCode)
    {
        if (CurrentLobby == null)
            throw new InvalidOperationException("No lobby to update. Create a lobby first.");

        var options = new UpdateLobbyOptions
        {
            Data = new Dictionary<string, DataObject>
            {
                { RelayCodeKey, new DataObject(DataObject.VisibilityOptions.Member, relayCode) }
            }
        };
        CurrentLobby = await LobbyService.Instance.UpdateLobbyAsync(CurrentLobby.Id, options);
    }

    // ── Client ────────────────────────────────────────────────────────────────
    public async Task JoinLobby(string lobbyCode)
    {
        await LeaveLobby();

        string code = lobbyCode.Trim().ToUpperInvariant();
        CurrentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(code);
        IsLobbyHost  = false;

        Debug.Log($"[LobbyManager] Joined lobby {CurrentLobby.Id}");
    }

    /// <summary>Polls the lobby until the host has posted the Relay join code.</summary>
    public async Task<string> WaitForRelayCode()
    {
        if (CurrentLobby == null)
            throw new InvalidOperationException("Not in a lobby.");

        float elapsed = 0f;
        while (elapsed < RelayWaitTimeout)
        {
            if (CurrentLobby.Data != null &&
                CurrentLobby.Data.TryGetValue(RelayCodeKey, out DataObject data) &&
                !string.IsNullOrEmpty(data.Value))
            {
                return data.Value;
            }

            await Task.Delay(TimeSpan.FromSeconds(PollInterval));
            elapsed += PollInterval;

            if (CurrentLobby == null)
                throw new InvalidOperationException("Left the lobby while waiting.");

            CurrentLobby = await LobbyService.Instance.GetLobbyAsync(CurrentLobby.Id);
        }

        throw new TimeoutException("The host didn't share a relay code in time. Try joining again.");
    }

    // ── Both ──────────────────────────────────────────────────────────────────
    /// <summary>Host deletes the lobby; client removes itself. Safe to call anytime.</summary>
    public async Task LeaveLobby()
    {
        if (CurrentLobby == null) return;

        Lobby lobby   = CurrentLobby;
        bool wasHost  = IsLobbyHost;
        CurrentLobby  = null;
        IsLobbyHost   = false;

        try
        {
            if (wasHost)
                await LobbyService.Instance.DeleteLobbyAsync(lobby.Id);
            else
                await LobbyService.Instance.RemovePlayerAsync(lobby.Id, AuthenticationService.Instance.PlayerId);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LobbyManager] Leave lobby failed (it may already be gone): {e.Message}");
        }
    }

    private void OnApplicationQuit()
    {
        _ = LeaveLobby(); // best effort
    }
}
