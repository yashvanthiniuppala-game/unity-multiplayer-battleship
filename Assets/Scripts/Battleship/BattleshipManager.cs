// BattleshipManager.cs — the REFEREE. Runs the rules on the host (server) only.
//
// What goes over the network, and why:
//   NetworkVariable  CurrentPhase, TurnClientId, HostReady, ClientReady, WinnerClientId
//                    -> small public facts both players must agree on (whose turn, who won).
//   NetworkList      Shots -> every shot and its result (hit/miss). Public: both players see it.
//   ServerRpc        SubmitFleetServerRpc, FireServerRpc, RequestRematchServerRpc
//                    -> the client only ASKS. The host checks everything.
//   ClientRpc        ShipSunkClientRpc  -> both players: "a ship was sunk" (its cells are no longer secret)
//                    RevealFleetClientRpc -> only to the LOSER at game over: where the enemy ships were
//                    FleetRejectedClientRpc -> only to the player whose fleet was not legal
//   Quick chat:      SendChatServerRpc(id) -> host checks it -> ChatClientRpc(sender, id) to both players
//                    (only a message NUMBER is sent, and the host limits it to one per second per player)
//   NEVER sent:      ship positions while the game is running. The host keeps both fleets
//                    in normal C# variables (not NetworkVariables), so no client can read them.
//
// HOW TO USE: in BattleshipScene, Create Empty "BattleshipManager" > Add Component > Network Object
//             > Add Component > Battleship Manager. (In-scene object: NOT in the Network Prefabs List.)
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BattleshipManager : NetworkBehaviour
{
    public static BattleshipManager Instance { get; private set; }
    public static event Action<BattleshipManager> Spawned;

    // ── Public, synced state (everyone can read, only the host writes) ─────────
    public NetworkVariable<BattlePhase> CurrentPhase   = new NetworkVariable<BattlePhase>(BattlePhase.Placement);
    public NetworkVariable<ulong>       TurnClientId   = new NetworkVariable<ulong>(0);
    public NetworkVariable<bool>        HostReady      = new NetworkVariable<bool>(false);
    public NetworkVariable<bool>        ClientReady    = new NetworkVariable<bool>(false);
    public NetworkVariable<ulong>       WinnerClientId = new NetworkVariable<ulong>(NoWinner);
    public NetworkList<ShotInfo>        Shots;

    public const ulong NoWinner = ulong.MaxValue;

    // ── Events for the UI (raised on every computer) ───────────────────────────
    public event Action<ulong, ShipPlacement> ShipSunk;     // shooter, the sunk ship
    public event Action<Fleet>                FleetRevealed; // loser only: the winner's fleet
    public event Action<string>               FleetRejected; // my fleet was not accepted
    public event Action<ulong, int>           ChatReceived;  // sender, quick-chat message number

    // ── Host-only secret state (plain C#, never synced) ────────────────────────
    private class ServerFleet
    {
        public ShipPlacement[] ships;
        public readonly int[]  owner = new int[Ships.GridSize * Ships.GridSize]; // -1 = water, else ship type
        public readonly bool[] hit   = new bool[Ships.GridSize * Ships.GridSize];
        public readonly bool[] sunk  = new bool[Ships.Count];
        public int hits;
    }
    private readonly Dictionary<ulong, ServerFleet> fleets = new Dictionary<ulong, ServerFleet>();
    private readonly Dictionary<ulong, bool[]>      fired  = new Dictionary<ulong, bool[]>();
    private readonly Dictionary<ulong, float>       lastChat = new Dictionary<ulong, float>();

    private void Awake()
    {
        Shots = new NetworkList<ShotInfo>();
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (IsServer) ResetGame();
        Spawned?.Invoke(this);
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    public static ulong LocalId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
    public static bool  LocalIsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    public bool IsMyTurn => CurrentPhase.Value == BattlePhase.Battle && TurnClientId.Value == LocalId;
    public bool MeReady       => LocalIsHost ? HostReady.Value   : ClientReady.Value;
    public bool OpponentReady => LocalIsHost ? ClientReady.Value : HostReady.Value;

    /// <summary>Host only: the other connected player.</summary>
    private ulong OpponentOf(ulong clientId)
    {
        foreach (ulong id in NetworkManager.ConnectedClientsIds)
            if (id != clientId) return id;
        return clientId;
    }

    private static ClientRpcParams To(ulong clientId) => new ClientRpcParams
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
    };

    // ── Step 4: Ready -> send the fleet to the host ────────────────────────────
    [ServerRpc(RequireOwnership = false)]
    public void SubmitFleetServerRpc(Fleet fleet, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        if (CurrentPhase.Value != BattlePhase.Placement) return;   // too late
        if (fleets.ContainsKey(sender)) return;                       // already ready

        // Check the fleet AGAIN on the host: the client could have been modified to cheat.
        ShipPlacement[] ships = fleet.ToArray();
        if (!Fleet.Validate(ships, out string reason))
        {
            Debug.LogWarning($"[Battleship] Fleet from client {sender} rejected: {reason}");
            FleetRejectedClientRpc(reason, To(sender));
            return;
        }

        var sf = new ServerFleet { ships = ships };
        for (int i = 0; i < sf.owner.Length; i++) sf.owner[i] = -1;
        foreach (var s in ships)
            for (int c = 0; c < s.Length; c++)
            {
                var cell = s.Cell(c);
                sf.owner[Ships.Index(cell.x, cell.y)] = s.type;
            }
        fleets[sender] = sf;
        fired[sender]  = new bool[Ships.GridSize * Ships.GridSize];

        if (sender == NetworkManager.ServerClientId) HostReady.Value = true;
        else                                          ClientReady.Value = true;
        Debug.Log($"[Battleship] Fleet from client {sender} accepted.");

        // Both fleets in? Start the battle. The host fires first.
        if (fleets.Count >= 2 && HostReady.Value && ClientReady.Value)
        {
            TurnClientId.Value = NetworkManager.ServerClientId;
            CurrentPhase.Value = BattlePhase.Battle;
            Debug.Log("[Battleship] Both fleets ready - battle starts. Host fires first.");
        }
    }

    // ── Step 5: Battle -> the client asks to fire, the host decides ───────────
    [ServerRpc(RequireOwnership = false)]
    public void FireServerRpc(int x, int y, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        // The four checks from the assignment:
        if (CurrentPhase.Value != BattlePhase.Battle) return;   // 1. battle phase
        if (TurnClientId.Value != sender)             return;   // 2. this player's turn
        if (!Ships.Inside(x, y))                      return;   // 3. inside the grid
        if (!fired.TryGetValue(sender, out bool[] used)) return;
        int index = Ships.Index(x, y);
        if (used[index])                              return;   // 4. not used before

        ulong target = OpponentOf(sender);
        if (target == sender || !fleets.TryGetValue(target, out ServerFleet enemy)) return;

        used[index] = true;
        int shipType = enemy.owner[index];
        bool hit = shipType >= 0;
        enemy.hit[index] = true;
        if (hit) enemy.hits++;

        // Public result: both players see hit or miss at this cell (nothing else).
        Shots.Add(new ShotInfo { shooter = sender, x = (byte)x, y = (byte)y, hit = hit });

        // Sunk? Once a ship is sunk its cells are not secret any more, so we can tell both players.
        if (hit && !enemy.sunk[shipType])
        {
            var ship = enemy.ships[shipType];
            bool allHit = true;
            for (int c = 0; c < ship.Length; c++)
            {
                var cell = ship.Cell(c);
                if (!enemy.hit[Ships.Index(cell.x, cell.y)]) { allHit = false; break; }
            }
            if (allHit)
            {
                enemy.sunk[shipType] = true;
                ShipSunkClientRpc(sender, ship);
            }
        }

        // Game over? First player to hit all 9 enemy cells wins.
        if (enemy.hits >= Ships.TotalCells)
        {
            WinnerClientId.Value = sender;
            CurrentPhase.Value   = BattlePhase.GameOver;
            // Show the LOSER where the winner's ships were (sent only to the loser).
            RevealFleetClientRpc(Fleet.From(fleets[sender].ships), To(target));
            Debug.Log($"[Battleship] Game over - client {sender} wins.");
            return;
        }

        // The turn passes to the other player.
        TurnClientId.Value = target;
    }

    // ── Step 6: Rematch ────────────────────────────────────────────────────────
    [ServerRpc(RequireOwnership = false)]
    public void RequestRematchServerRpc(ServerRpcParams rpcParams = default)
    {
        if (CurrentPhase.Value != BattlePhase.GameOver) return;
        ResetGame();
        Debug.Log($"[Battleship] Rematch requested by client {rpcParams.Receive.SenderClientId}.");
    }

    /// <summary>Host only: clears both boards and goes back to the placement phase.</summary>
    private void ResetGame()
    {
        fleets.Clear();
        fired.Clear();
        Shots.Clear();
        HostReady.Value      = false;
        ClientReady.Value    = false;
        WinnerClientId.Value = NoWinner;
        TurnClientId.Value   = NetworkManager.ServerClientId;
        CurrentPhase.Value   = BattlePhase.Placement;
    }

    // ── Creativity: quick chat ────────────────────────────────────────────────
    [ServerRpc(RequireOwnership = false)]
    public void SendChatServerRpc(int messageId, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (messageId < 0 || messageId >= Ships.Chat.Length) return;                         // unknown message
        if (lastChat.TryGetValue(sender, out float last) && Time.time - last < 1f) return;   // no spamming
        lastChat[sender] = Time.time;
        ChatClientRpc(sender, messageId);
    }

    [ClientRpc]
    private void ChatClientRpc(ulong sender, int messageId) => ChatReceived?.Invoke(sender, messageId);

    // ── ClientRpcs (host -> players) ──────────────────────────────────────────
    [ClientRpc]
    private void ShipSunkClientRpc(ulong shooter, ShipPlacement ship) => ShipSunk?.Invoke(shooter, ship);

    [ClientRpc]
    private void RevealFleetClientRpc(Fleet winnerFleet, ClientRpcParams rpcParams = default) => FleetRevealed?.Invoke(winnerFleet);

    [ClientRpc]
    private void FleetRejectedClientRpc(string reason, ClientRpcParams rpcParams = default) => FleetRejected?.Invoke(reason);
}
