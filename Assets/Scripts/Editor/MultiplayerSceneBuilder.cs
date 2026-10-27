// MultiplayerSceneBuilder.cs — EDITOR TOOL (put it in Assets/Editor/).
// Builds the scenes of the multiplayer project for you, so you don't have to place every button by hand:
//
//   Tools > Multiplayer Setup > 1. Build LoginScene      (camera, login UI, NetworkManager + managers, Network Prefabs List)
//   Tools > Multiplayer Setup > 2. Build LobbyScene      (camera, lobby UI, LobbyController wired up, loads BattleshipScene)
//   Tools > Multiplayer Setup > 3. Build BattleshipScene (camera, BattleshipManager + NetworkObject, BattleshipUI)
//   Tools > Multiplayer Setup > 4. Add Scenes to Build List
//
// It only CREATES scenes and objects and fills Inspector fields. It does not change any script.
// Run the steps in order. Each one saves its scene into Assets/Scenes.
using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MultiplayerSceneBuilder
{
    private const string ScenesFolder  = "Assets/Scenes";
    private const string PrefabsFolder = "Assets/Prefabs";
    private const string LoginPath     = ScenesFolder + "/LoginScene.unity";
    private const string LobbyPath     = ScenesFolder + "/LobbyScene.unity";
    private const string BattlePath    = ScenesFolder + "/BattleshipScene.unity";
    private const string GamePath      = ScenesFolder + "/GameScene.unity";
    private const string GameSceneToLoad = "BattleshipScene";      // the lobby loads this scene when both players are in

    private static readonly Color Navy     = Hex("1A237E");
    private static readonly Color BlueBtn  = Hex("1A73E8");
    private static readonly Color GreenBtn = Hex("43A047");
    private static readonly Color Divider  = Hex("DDDDDD");

    // =====================================================================
    [MenuItem("Tools/Multiplayer Setup/1. Build LoginScene", false, 1)]
    public static void BuildLoginScene()
    {
        if (!CheckTmp() || !ConfirmOverwrite(LoginPath)) return;
        var scene = NewScene();
        MakeCamera(Navy);
        MakeEventSystem();

        // ── NetworkManager + the 3 manager scripts (only in LoginScene!) ──
        var nmGO = new GameObject("NetworkManager");
        var nm = nmGO.AddComponent<NetworkManager>();
        var transport = nmGO.AddComponent<UnityTransport>();
        var auth = nmGO.AddComponent<AuthManager>();
        nmGO.AddComponent<LobbyManager>();
        nmGO.AddComponent<RelayManager>();

        if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
        nm.NetworkConfig.NetworkTransport = transport;
        nm.NetworkConfig.EnableSceneManagement = true;
        nm.RunInBackground = true;

        GameObject playerPrefab = FindOrCreatePlayerPrefab();
        nm.NetworkConfig.PlayerPrefab = playerPrefab;
        NetworkPrefabsList list = FindOrCreatePrefabsList(playerPrefab);
        if (nm.NetworkConfig.Prefabs == null) nm.NetworkConfig.Prefabs = new NetworkPrefabs();
        if (!nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(list)) nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
        EditorUtility.SetDirty(nm);

        // ── UI ──
        var canvas = MakeCanvas("LoginCanvas");
        Stretch(Img(canvas, "Background", Navy));
        var panel = Img(canvas, "LoginPanel", Color.white).rectTransform;
        Place(panel, Center, new Vector2(0, 0), new Vector2(720, 560));

        Txt(panel, "TitleText", "Battleship", 60, Navy, true, Top, -70, 660, 80);
        Txt(panel, "SubtitleText", "Multiplayer", 28, Hex("5C6BC0"), false, Top, -135, 600, 40);
        Txt(panel, "NameLabel", "Enter your display name", 24, Hex("333333"), false, Top, -215, 520, 36);
        var nameInput = InputField(panel, "NameInputField", "Your name...", 20, Top, new Vector2(0, -275), new Vector2(520, 64));
        var signIn = Btn(panel, "SignInButton", "Sign In", BlueBtn, Top, new Vector2(0, -375), new Vector2(520, 72));
        var status = Txt(panel, "StatusText", "", 22, Hex("C62828"), false, Top, -465, 640, 40);

        // ── LoginController wired up ──
        var ctrlGO = new GameObject("LoginController");
        var ctrl = ctrlGO.AddComponent<LoginController>();
        var so = new SerializedObject(ctrl);
        so.FindProperty("authManager").objectReferenceValue  = auth;
        so.FindProperty("nameInput").objectReferenceValue    = nameInput;
        so.FindProperty("statusText").objectReferenceValue   = status;
        so.FindProperty("signInButton").objectReferenceValue = signIn;
        so.ApplyModifiedPropertiesWithoutUndo();
        UnityEventTools.AddPersistentListener(signIn.onClick, new UnityAction(ctrl.OnSignInClicked));

        Save(scene, LoginPath);
        Debug.Log("[Setup] LoginScene built: NetworkManager (Auth/Lobby/Relay managers), Player Prefab = PlayerDataObject, login UI wired.");
    }

    // =====================================================================
    [MenuItem("Tools/Multiplayer Setup/2. Build LobbyScene", false, 2)]
    public static void BuildLobbyScene()
    {
        if (!CheckTmp() || !ConfirmOverwrite(LobbyPath)) return;
        var scene = NewScene();
        MakeCamera(Navy);
        MakeEventSystem();
        // NO NetworkManager here: the one from LoginScene carries over.

        var canvas = MakeCanvas("LobbyCanvas");
        Stretch(Img(canvas, "Background", Navy));
        var panel = Img(canvas, "LobbyPanel", Color.white).rectTransform;
        Place(panel, Center, Vector2.zero, new Vector2(820, 760));

        Txt(panel, "LobbyTitle", "Game Lobby", 54, Navy, true, Top, -70, 760, 70);
        var playerName = Txt(panel, "PlayerNameDisplay", "Playing as: ...", 26, Hex("555555"), false, Top, -135, 760, 40);
        Place(Img(panel, "Divider", Divider).rectTransform, Top, new Vector2(0, -175), new Vector2(700, 3));
        Txt(panel, "CreateLabel", "Create a new game", 26, Hex("222222"), true, Top, -220, 700, 36);
        var createBtn = Btn(panel, "CreateGameButton", "Create Game", BlueBtn, Top, new Vector2(0, -285), new Vector2(700, 72));
        var codeText = Txt(panel, "LobbyCodeDisplay", "", 30, BlueBtn, true, Top, -355, 700, 44);
        Place(Img(panel, "Divider2", Divider).rectTransform, Top, new Vector2(0, -400), new Vector2(700, 3));
        Txt(panel, "JoinLabel", "Join an existing game", 26, Hex("222222"), true, Top, -445, 700, 36);
        var joinInput = InputField(panel, "JoinCodeInput", "Enter code...", 6, Top, new Vector2(-120, -515), new Vector2(460, 68));
        var joinBtn = Btn(panel, "JoinGameButton", "Join", GreenBtn, Top, new Vector2(240, -515), new Vector2(220, 68));
        var lobbyStatus = Txt(panel, "LobbyStatus", "", 22, Hex("333333"), false, Top, -620, 740, 90);
        lobbyStatus.textWrappingMode = TextWrappingModes.Normal;

        var ctrlGO = new GameObject("LobbyController");
        var ctrl = ctrlGO.AddComponent<LobbyController>();
        var so = new SerializedObject(ctrl);
        so.FindProperty("playerNameDisplay").objectReferenceValue = playerName;
        so.FindProperty("createBtn").objectReferenceValue         = createBtn;
        so.FindProperty("lobbyCodeDisplay").objectReferenceValue  = codeText;
        so.FindProperty("joinBtn").objectReferenceValue           = joinBtn;
        so.FindProperty("joinCodeInput").objectReferenceValue     = joinInput;
        so.FindProperty("lobbyStatus").objectReferenceValue       = lobbyStatus;
        so.FindProperty("gameSceneName").stringValue             = GameSceneToLoad;   // Inspector value, not a script change
        so.ApplyModifiedPropertiesWithoutUndo();
        UnityEventTools.AddPersistentListener(createBtn.onClick, new UnityAction(ctrl.OnCreateGameClicked));
        UnityEventTools.AddPersistentListener(joinBtn.onClick,   new UnityAction(ctrl.OnJoinGameClicked));

        Save(scene, LobbyPath);
        Debug.Log($"[Setup] LobbyScene built. The host will load '{GameSceneToLoad}'.");
    }

    // =====================================================================
    [MenuItem("Tools/Multiplayer Setup/3. Build BattleshipScene", false, 3)]
    public static void BuildBattleshipScene()
    {
        if (!CheckTmp() || !ConfirmOverwrite(BattlePath)) return;
        var scene = NewScene();
        MakeCamera(Hex("0E1F3A"));
        MakeEventSystem();

        var mgr = new GameObject("BattleshipManager");
        mgr.AddComponent<NetworkObject>();          // in-scene network object (not in the prefabs list)
        mgr.AddComponent<BattleshipManager>();

        new GameObject("BattleshipUI").AddComponent<BattleshipUI>();

        Save(scene, BattlePath);
        Debug.Log("[Setup] BattleshipScene built (BattleshipManager + NetworkObject, BattleshipUI).");
    }

    // =====================================================================
    [MenuItem("Tools/Multiplayer Setup/4. Add Scenes to Build List", false, 4)]
    public static void AddScenesToBuild()
    {
        var list = new List<EditorBuildSettingsScene>();
        foreach (var path in new[] { LoginPath, LobbyPath, GamePath, BattlePath })
            if (File.Exists(path)) list.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();

        var names = new List<string>();
        foreach (var s in list) names.Add(Path.GetFileNameWithoutExtension(s.path));
        Debug.Log("[Setup] Build list: " + string.Join(", ", names) + "   (LoginScene must be first)");
        if (!File.Exists(LoginPath) || !File.Exists(LobbyPath) || !File.Exists(BattlePath))
            EditorUtility.DisplayDialog("Build list", "Some scenes are missing. Run steps 1-3 first, then this step again.", "OK");

        EditorSceneManager.OpenScene(LoginPath);   // always press Play from LoginScene
    }

    // =====================================================================
    // Prefab + prefab list
    // =====================================================================
    private static GameObject FindOrCreatePlayerPrefab()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null && go.GetComponent<PlayerData>() != null && go.GetComponent<NetworkObject>() != null) return go;
        }
        EnsureFolder(PrefabsFolder);
        var temp = new GameObject("PlayerDataObject");
        temp.AddComponent<NetworkObject>();
        temp.AddComponent<PlayerData>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(temp, PrefabsFolder + "/PlayerDataObject.prefab");
        Object.DestroyImmediate(temp);
        Debug.Log("[Setup] Created Assets/Prefabs/PlayerDataObject.prefab");
        return prefab;
    }

    private static NetworkPrefabsList FindOrCreatePrefabsList(GameObject playerPrefab)
    {
        NetworkPrefabsList list = null;
        foreach (string guid in AssetDatabase.FindAssets("t:NetworkPrefabsList"))
        {
            list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(AssetDatabase.GUIDToAssetPath(guid));
            if (list != null) break;
        }
        if (list == null)
        {
            list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            AssetDatabase.CreateAsset(list, "Assets/NetworkPrefabsList.asset");
        }

        bool has = false;
        foreach (var p in list.PrefabList) if (p.Prefab == playerPrefab) { has = true; break; }
        if (!has) list.Add(new NetworkPrefab { Prefab = playerPrefab });
        EditorUtility.SetDirty(list);
        AssetDatabase.SaveAssets();
        return list;
    }

    // =====================================================================
    // Scene helpers
    // =====================================================================
    private static bool CheckTmp()
    {
        if (TMP_Settings.defaultFontAsset != null) return true;
        EditorUtility.DisplayDialog("TextMeshPro needed",
            "Import the TextMeshPro essentials first:\nWindow > TextMeshPro > Import TMP Essential Resources.\nThen run this step again.", "OK");
        return false;
    }

    private static bool ConfirmOverwrite(string path)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
        if (!File.Exists(path)) return true;
        return EditorUtility.DisplayDialog("Scene exists", $"{path} already exists. Replace it with a new one?", "Replace", "Cancel");
    }

    private static Scene NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    private static void Save(Scene scene, string path)
    {
        EnsureFolder(ScenesFolder);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, path);
        AssetDatabase.Refresh();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }

    private static void MakeCamera(Color background)
    {
        var go = new GameObject("Main Camera") { tag = "MainCamera" };
        go.transform.position = new Vector3(0, 0, -10);
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = background;
        go.AddComponent<AudioListener>();
    }

    private static void MakeEventSystem()
    {
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    private static RectTransform MakeCanvas(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        return (RectTransform)go.transform;
    }

    // anchors: pivot is always the centre of the element (like Unity's defaults)
    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    private static readonly Vector2 Top    = new Vector2(0.5f, 1f);

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static void Stretch(Image img)
    {
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        img.raycastTarget = false;
    }

    private static Image Img(RectTransform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private static TextMeshProUGUI Txt(RectTransform parent, string name, string text, float size, Color color, bool bold,
                                       Vector2 anchor, float posY, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = color;
        t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        Place((RectTransform)go.transform, anchor, new Vector2(0, posY), new Vector2(w, h));
        return t;
    }

    private static TMP_DefaultControls.Resources UiResources() => new TMP_DefaultControls.Resources
    {
        standard   = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
        background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
        inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
        knob       = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
        checkmark  = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
        dropdown   = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
        mask       = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
    };

    private static Button Btn(RectTransform parent, string name, string label, Color color, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = TMP_DefaultControls.CreateButton(UiResources());
        go.name = name;
        go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, anchor, pos, size);
        go.GetComponent<Image>().color = color;
        var text = go.GetComponentInChildren<TextMeshProUGUI>();
        text.text = label; text.fontSize = 30; text.fontStyle = FontStyles.Bold; text.color = Color.white;
        return go.GetComponent<Button>();
    }

    private static TMP_InputField InputField(RectTransform parent, string name, string placeholder, int limit, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = TMP_DefaultControls.CreateInputField(UiResources());
        go.name = name;
        go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, anchor, pos, size);
        var field = go.GetComponent<TMP_InputField>();
        field.characterLimit = limit;
        field.pointSize = 28;
        if (field.placeholder is TMP_Text ph) { ph.text = placeholder; ph.fontSize = 28; }
        if (field.textComponent != null) field.textComponent.fontSize = 28;
        return field;
    }

    private static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }
}
