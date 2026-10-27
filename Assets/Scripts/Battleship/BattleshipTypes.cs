// BattleshipTypes.cs — the shared data types and rules for Battleship.
// Used by BOTH computers: the client uses the rules to show a blue/red preview,
// and the host uses the SAME rules to check every fleet again (the client could cheat).
using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>The three phases of a game (synced by the host in a NetworkVariable).</summary>
public enum BattlePhase : byte { Placement = 0, Battle = 1, GameOver = 2 }

/// <summary>Fixed game constants and small helpers.</summary>
public static class Ships
{
    public const int GridSize   = 10;   // 10 x 10 cells: columns A-J, rows 1-10
    public const int Count      = 3;    // ships per player
    public const int TotalCells = 9;    // 4 + 3 + 2

    public static readonly string[] Names   = { "Battleship", "Cruiser", "Destroyer" };
    public static readonly int[]    Lengths = { 4, 3, 2 };

    /// <summary>Quick-chat messages. Only the NUMBER (0-5) is sent over the network, never free text.</summary>
    public static readonly string[] Chat = { "Hello!", "Nice shot!", "Oops!", "Ha, missed!", "Watch out!", "Good game!" };

    /// <summary>x = column (0 = A), y = row (0 = row 1). Returns e.g. "C7".</summary>
    public static string CellName(int x, int y) => $"{(char)('A' + x)}{y + 1}";

    public static bool Inside(int x, int y) => x >= 0 && x < GridSize && y >= 0 && y < GridSize;

    public static int Index(int x, int y) => y * GridSize + x;
}

/// <summary>
/// One ship on the grid: which ship, its first cell and its direction.
/// Sent from the client to the host only once, inside the Ready ServerRpc.
/// </summary>
[Serializable]
public struct ShipPlacement : INetworkSerializable, IEquatable<ShipPlacement>
{
    public int  type;      // 0 = Battleship, 1 = Cruiser, 2 = Destroyer
    public int  x, y;      // first (top / left) cell
    public bool vertical;  // true = goes down, false = goes right
    public bool placed;    // false = still in the "Ships to place" list

    public int Length => Ships.Lengths[Mathf.Clamp(type, 0, Ships.Count - 1)];

    /// <summary>The i-th cell of this ship.</summary>
    public Vector2Int Cell(int i) => vertical ? new Vector2Int(x, y + i) : new Vector2Int(x + i, y);

    public bool Covers(int cx, int cy)
    {
        for (int i = 0; i < Length; i++)
        {
            var c = Cell(i);
            if (c.x == cx && c.y == cy) return true;
        }
        return false;
    }

    public bool InsideGrid()
    {
        for (int i = 0; i < Length; i++)
        {
            var c = Cell(i);
            if (!Ships.Inside(c.x, c.y)) return false;
        }
        return true;
    }

    public bool Overlaps(ShipPlacement other)
    {
        for (int i = 0; i < Length; i++)
        {
            var c = Cell(i);
            if (other.Covers(c.x, c.y)) return true;
        }
        return false;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref type);
        serializer.SerializeValue(ref x);
        serializer.SerializeValue(ref y);
        serializer.SerializeValue(ref vertical);
        serializer.SerializeValue(ref placed);
    }

    public bool Equals(ShipPlacement o) => type == o.type && x == o.x && y == o.y && vertical == o.vertical && placed == o.placed;
    public override bool Equals(object obj) => obj is ShipPlacement o && Equals(o);
    public override int GetHashCode() => (type * 397) ^ (x * 31) ^ (y * 7) ^ (vertical ? 1 : 0);
}

/// <summary>A whole fleet (3 ships). This is what the Ready button sends to the host.</summary>
public struct Fleet : INetworkSerializable
{
    public ShipPlacement battleship, cruiser, destroyer;

    public ShipPlacement this[int i]
    {
        get => i == 0 ? battleship : i == 1 ? cruiser : destroyer;
        set { if (i == 0) battleship = value; else if (i == 1) cruiser = value; else destroyer = value; }
    }

    public static Fleet From(ShipPlacement[] ships)
    {
        var f = new Fleet();
        for (int i = 0; i < Ships.Count; i++) f[i] = ships[i];
        return f;
    }

    public ShipPlacement[] ToArray() => new[] { battleship, cruiser, destroyer };

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        battleship.NetworkSerialize(serializer);
        cruiser.NetworkSerialize(serializer);
        destroyer.NetworkSerialize(serializer);
    }

    /// <summary>
    /// The placement rules. The client uses them for the preview; the host uses them again
    /// on every fleet it receives. Returns false and a reason if the fleet is not legal.
    /// </summary>
    public static bool Validate(ShipPlacement[] ships, out string reason)
    {
        reason = "";
        if (ships == null || ships.Length != Ships.Count) { reason = "A fleet must have exactly 3 ships."; return false; }

        for (int i = 0; i < Ships.Count; i++)
        {
            var s = ships[i];
            if (s.type != i)        { reason = $"Ship {i} has the wrong type.";                  return false; }
            if (!s.placed)          { reason = $"{Ships.Names[i]} is not placed.";                return false; }
            if (!s.InsideGrid())    { reason = $"{Ships.Names[i]} is outside the grid.";          return false; }
            for (int j = 0; j < i; j++)
                if (s.Overlaps(ships[j])) { reason = $"{Ships.Names[i]} overlaps the {Ships.Names[j]}."; return false; }
        }
        return true;
    }

    /// <summary>Can 'ship' go here, given the other ships that are already placed?</summary>
    public static bool CanPlace(ShipPlacement ship, ShipPlacement[] all)
    {
        if (!ship.InsideGrid()) return false;
        foreach (var other in all)
            if (other.placed && other.type != ship.type && ship.Overlaps(other)) return false;
        return true;
    }
}

/// <summary>
/// One shot and its result, decided by the host. Kept in a NetworkList, so both
/// players see every shot. A shot only says "hit" or "miss" — never where the ships are.
/// </summary>
public struct ShotInfo : INetworkSerializable, IEquatable<ShotInfo>
{
    public ulong shooter;   // client id of the player who fired
    public byte  x, y;
    public bool  hit;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref shooter);
        serializer.SerializeValue(ref x);
        serializer.SerializeValue(ref y);
        serializer.SerializeValue(ref hit);
    }

    public bool Equals(ShotInfo o) => shooter == o.shooter && x == o.x && y == o.y && hit == o.hit;
    public override bool Equals(object obj) => obj is ShotInfo o && Equals(o);
    public override int GetHashCode() => (int)shooter * 1000 + x * 10 + y;
}
