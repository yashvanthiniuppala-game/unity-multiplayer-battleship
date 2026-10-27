using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// Player prefab (PlayerDataObject). Netcode spawns one per connected player.
/// The owner writes its display name so the host can show both names.
/// </summary>
public class PlayerData : NetworkBehaviour
{
    private const int MaxNameLength = 20; // keeps the name inside FixedString64Bytes

    public NetworkVariable<FixedString64Bytes> PlayerName = new NetworkVariable<FixedString64Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        string name = AuthManager.LocalPlayerName ?? "Player";
        if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
        PlayerName.Value = new FixedString64Bytes(name);
    }
}
