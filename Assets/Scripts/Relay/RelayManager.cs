using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Wraps the Relay service (from the Multiplayer Services package) and starts
/// Netcode as host or client through Relay.
/// Lives on the NetworkManager GameObject in LoginScene (persists across scenes).
/// </summary>
public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance { get; private set; }

    private const string ConnectionType = "dtls"; // "udp", "dtls" or "wss" (WebGL)
    private const int    MaxConnections = 1;      // players besides the host

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

    /// <summary>Creates a Relay allocation, starts the host and returns the Relay join code.</summary>
    public async Task<string> StartHost()
    {
        await WaitForShutdown();

        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxConnections);
        string joinCode       = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        // Multiplayer Services API (replaces the old `new RelayServerData(allocation, "dtls")`)
        GetTransport().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, ConnectionType));

        if (!NetworkManager.Singleton.StartHost())
            throw new Exception("NetworkManager.StartHost() failed. Check the Console for Netcode errors.");

        Debug.Log($"[RelayManager] Host started. Relay code: {joinCode}");
        return joinCode;
    }

    /// <summary>Joins a Relay allocation and starts the client.</summary>
    public async Task StartClient(string joinCode)
    {
        await WaitForShutdown();

        JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
        GetTransport().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, ConnectionType));

        if (!NetworkManager.Singleton.StartClient())
            throw new Exception("NetworkManager.StartClient() failed. Check the Console for Netcode errors.");

        Debug.Log("[RelayManager] Client started.");
    }

    private static UnityTransport GetTransport()
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
            throw new Exception("Unity Transport component missing on the NetworkManager object.");
        return transport;
    }

    /// <summary>After returning from a match, wait for the previous session to finish shutting down.</summary>
    private static async Task WaitForShutdown()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
            throw new Exception("No NetworkManager found. Press Play from LoginScene.");

        while (nm.ShutdownInProgress)
            await Task.Yield();
    }
}
