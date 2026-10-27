using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Initializes Unity Gaming Services and signs the player in anonymously.
/// Lives on the NetworkManager GameObject in LoginScene (persists across scenes).
///
/// Each display name gets its own Authentication *profile*, so two Multiplayer
/// Play Mode windows on the same machine sign in as two different players.
/// (Without this, both windows share one player ID and the Join step fails.)
/// </summary>
public class AuthManager : MonoBehaviour
{
    public static AuthManager Instance { get; private set; }

    /// <summary>Display name typed on the login screen.</summary>
    public static string LocalPlayerName { get; private set; } = "Player";

    private const int MaxNameLength = 20;

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

    public async Task SignInAsync(string displayName)
    {
        displayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName.Trim();
        if (displayName.Length > MaxNameLength)
            displayName = displayName.Substring(0, MaxNameLength);

        LocalPlayerName = displayName;
        string profile = ToProfileName(displayName);

        // 1. Initialize Unity Services (once per app run)
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            var options = new InitializationOptions();
            options.SetProfile(profile);
            await UnityServices.InitializeAsync(options);
        }
        while (UnityServices.State == ServicesInitializationState.Initializing)
            await Task.Yield();

        // 2. Make sure we're on the right profile, then sign in
        var auth = AuthenticationService.Instance;

        if (auth.IsSignedIn && auth.Profile == profile)
            return; // already signed in as this player

        if (auth.IsSignedIn)
            auth.SignOut();

        if (auth.Profile != profile)
            auth.SwitchProfile(profile);

        await auth.SignInAnonymouslyAsync();
        Debug.Log($"[AuthManager] Signed in as '{LocalPlayerName}' (profile '{profile}', id {auth.PlayerId})");
    }

    /// <summary>Profiles allow only letters, digits, '-' and '_' (max 30 chars).</summary>
    private static string ToProfileName(string name)
    {
        string clean = Regex.Replace(name, "[^a-zA-Z0-9_-]", "");
        if (string.IsNullOrEmpty(clean)) clean = "player";
        if (clean.Length > 30) clean = clean.Substring(0, 30);
        return clean;
    }
}
