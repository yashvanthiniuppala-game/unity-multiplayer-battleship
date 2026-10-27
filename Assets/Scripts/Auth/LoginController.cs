using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Drives the LoginScene UI.
/// Wire: SignInButton.OnClick → LoginController.OnSignInClicked()
/// </summary>
public class LoginController : MonoBehaviour
{
    [SerializeField] private AuthManager    authManager;   // drag the NetworkManager object here
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private TMP_Text       statusText;
    [SerializeField] private Button         signInButton;
    [SerializeField] private string         lobbySceneName = "LobbyScene";

    private void Start()
    {
        SetStatus(string.Empty, false);
    }

    public async void OnSignInClicked()
    {
        AuthManager auth = authManager != null ? authManager : AuthManager.Instance;
        if (auth == null)
        {
            SetStatus("AuthManager missing. Add it to the NetworkManager object in LoginScene.", true);
            return;
        }

        string playerName = nameInput != null ? nameInput.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(playerName))
        {
            SetStatus("Please enter a display name.", true);
            return;
        }

        SetInteractable(false);
        SetStatus("Signing in...", false);

        try
        {
            await auth.SignInAsync(playerName);
            SceneManager.LoadScene(lobbySceneName);
        }
        catch (Exception e)
        {
            Debug.LogError($"[LoginController] Sign-in error: {e}");
            SetStatus($"Sign-in failed: {e.Message}", true);
            SetInteractable(true);
        }
    }

    private void SetInteractable(bool value)
    {
        if (signInButton != null) signInButton.interactable = value;
        if (nameInput    != null) nameInput.interactable    = value;
    }

    private void SetStatus(string msg, bool isError)
    {
        if (statusText == null) return;
        statusText.text  = msg;
        statusText.color = isError ? new Color(0.898f, 0.224f, 0.208f) : new Color(0.33f, 0.33f, 0.33f);
    }
}
