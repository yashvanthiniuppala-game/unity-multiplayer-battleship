using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative game rules. Spawned by GameSceneBootstrapper.
/// Host plays X, the joining client plays O.
/// Clients only send move / rematch requests; the server validates them.
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }
    public static event Action<GameManager> Spawned;

    // Marks
    public const int Empty = 0, X = 1, O = 2;

    // Results
    public const int ResultNone = 0, ResultXWins = 1, ResultOWins = 2, ResultDraw = 3;

    public NetworkVariable<int> CurrentTurn = new NetworkVariable<int>(X);
    public NetworkVariable<int> Result      = new NetworkVariable<int>(ResultNone);
    public NetworkVariable<FixedString64Bytes> XPlayerName = new NetworkVariable<FixedString64Bytes>();
    public NetworkVariable<FixedString64Bytes> OPlayerName = new NetworkVariable<FixedString64Bytes>();

    // Server-only state
    private readonly int[]            board = new int[9];
    private readonly CellController[] cells = new CellController[9];
    private int nextStarter = X;

    private static readonly int[,] Lines =
    {
        { 0, 1, 2 }, { 3, 4, 5 }, { 6, 7, 8 }, // rows
        { 0, 3, 6 }, { 1, 4, 7 }, { 2, 5, 8 }, // columns
        { 0, 4, 8 }, { 2, 4, 6 }               // diagonals
    };

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    public override void OnNetworkSpawn()
    {
        Instance = this;

        if (IsServer)
            RefreshPlayerNames();

        Spawned?.Invoke(this);
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    // ── Helpers used by cells and UI ──────────────────────────────────────────
    public static int SymbolFor(ulong clientId) =>
        clientId == Unity.Netcode.NetworkManager.ServerClientId ? X : O;

    public int LocalSymbol => SymbolFor(Unity.Netcode.NetworkManager.Singleton.LocalClientId);

    public bool IsLocalTurn => Result.Value == ResultNone && CurrentTurn.Value == LocalSymbol;

    /// <summary>Server: BoardSpawner registers each cell it spawns.</summary>
    public void RegisterCell(int index, CellController cell)
    {
        if (index >= 0 && index < cells.Length)
            cells[index] = cell;
    }

    // ── RPCs (client → server) ────────────────────────────────────────────────
    [Rpc(SendTo.Server)]
    public void RequestMoveRpc(int index, RpcParams rpcParams = default)
    {
        int symbol = SymbolFor(rpcParams.Receive.SenderClientId);

        if (Result.Value != ResultNone)            return; // game over
        if (symbol != CurrentTurn.Value)           return; // not your turn
        if (index < 0 || index >= board.Length)    return; // bad index
        if (board[index] != Empty)                 return; // already taken

        board[index] = symbol;
        if (cells[index] != null) cells[index].Mark.Value = symbol;

        int winner = CheckWinner();
        if (winner != Empty)
            Result.Value = winner == X ? ResultXWins : ResultOWins;
        else if (IsBoardFull())
            Result.Value = ResultDraw;
        else
            CurrentTurn.Value = symbol == X ? O : X;
    }

    [Rpc(SendTo.Server)]
    public void RequestRematchRpc()
    {
        if (Result.Value == ResultNone) return; // only after a game ends

        for (int i = 0; i < board.Length; i++)
        {
            board[i] = Empty;
            if (cells[i] != null) cells[i].Mark.Value = Empty;
        }

        nextStarter       = nextStarter == X ? O : X; // alternate who starts
        CurrentTurn.Value = nextStarter;
        Result.Value      = ResultNone;
        RefreshPlayerNames();
    }

    // ── Server logic ──────────────────────────────────────────────────────────
    private int CheckWinner()
    {
        for (int i = 0; i < Lines.GetLength(0); i++)
        {
            int a = board[Lines[i, 0]];
            if (a != Empty && a == board[Lines[i, 1]] && a == board[Lines[i, 2]])
                return a;
        }
        return Empty;
    }

    private bool IsBoardFull()
    {
        foreach (int c in board)
            if (c == Empty) return false;
        return true;
    }

    private void RefreshPlayerNames()
    {
        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            var data = client.PlayerObject.GetComponent<PlayerData>();
            if (data == null) continue;

            if (SymbolFor(client.ClientId) == X) XPlayerName.Value = data.PlayerName.Value;
            else                                 OPlayerName.Value = data.PlayerName.Value;
        }
    }
}
