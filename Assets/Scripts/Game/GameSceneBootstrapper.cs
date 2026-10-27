using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// In-scene NetworkObject in GameScene. When the host loads GameScene,
/// it spawns the GameManager first, then the Board (which spawns the cells).
/// In-scene objects like this one do NOT need to be in the Network Prefabs List.
///
/// FIX: the host now waits until EVERY player has finished loading GameScene
/// (SceneManager.OnLoadEventCompleted) before spawning. Spawning earlier, while the
/// client was still switching scenes, meant the client never got the board.
/// </summary>
public class GameSceneBootstrapper : NetworkBehaviour
{
    [SerializeField] private GameObject boardPrefab;        // Board.prefab
    [SerializeField] private GameObject gameManagerPrefab;  // GameManager.prefab

    private bool hasSpawned;
    private bool subscribed;

    public override void OnNetworkSpawn()
    {
        if (!IsServer || hasSpawned) return;

        var sceneManager = NetworkManager.SceneManager;
        if (sceneManager != null)
        {
            // Wait until all players have loaded this scene
            sceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            subscribed = true;
        }
        else
        {
            SpawnGame(); // scene management off: nothing to wait for
        }
    }

    public override void OnNetworkDespawn() => Unsubscribe();

    public override void OnDestroy()
    {
        Unsubscribe();
        base.OnDestroy();
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;
        if (NetworkManager != null && NetworkManager.SceneManager != null)
            NetworkManager.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
    }

    private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != gameObject.scene.name) return;
        Unsubscribe();
        if (clientsTimedOut != null && clientsTimedOut.Count > 0)
            Debug.LogWarning($"[GameSceneBootstrapper] {clientsTimedOut.Count} client(s) timed out loading {sceneName}.");
        SpawnGame();
    }

    private void SpawnGame()
    {
        if (hasSpawned) return;
        hasSpawned = true;

        if (gameManagerPrefab == null || boardPrefab == null)
        {
            Debug.LogError("[GameSceneBootstrapper] Assign Board Prefab and Game Manager Prefab in the Inspector.");
            return;
        }

        // Order matters: BoardSpawner registers cells with GameManager.Instance
        GameObject gm = Instantiate(gameManagerPrefab);
        gm.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);

        GameObject board = Instantiate(boardPrefab, Vector3.zero, Quaternion.identity);
        board.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);

        Debug.Log("[GameSceneBootstrapper] All players loaded - spawned GameManager and Board.");
    }
}
