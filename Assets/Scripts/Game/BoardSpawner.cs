using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Board prefab. On the server, spawns the 9 networked cells in a 3×3 grid
/// and registers them with the GameManager.
/// </summary>
public class BoardSpawner : NetworkBehaviour
{
    [SerializeField] private GameObject cellPrefab;          // Cell.prefab
    [SerializeField] private float      cellSpacing = 2.1f;  // matches the grid lines at ±1.05

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        if (cellPrefab == null)
        {
            Debug.LogError("[BoardSpawner] Cell Prefab is not assigned.");
            return;
        }

        GameManager gm = GameManager.Instance;
        if (gm == null)
            Debug.LogError("[BoardSpawner] GameManager not spawned yet. Spawn it before the Board.");

        for (int i = 0; i < 9; i++)
        {
            int row = i / 3;
            int col = i % 3;
            Vector3 pos = transform.position + new Vector3((col - 1) * cellSpacing, (1 - row) * cellSpacing, 0f);

            GameObject go   = Instantiate(cellPrefab, pos, Quaternion.identity);
            var        cell = go.GetComponent<CellController>();

            go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            cell.Index.Value = i; // set AFTER spawning (removes the "NetworkVariable is written to..." warnings)

            if (gm != null) gm.RegisterCell(i, cell);
        }
    }
}
