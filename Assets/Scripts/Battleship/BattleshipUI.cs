// BattleshipUI.cs — everything the player SEES and CLICKS in BattleshipScene.
// It builds the whole screen from code (top bar, both grids, ship list, buttons, message bar, game-over panel).
//
//  * Placement runs only on THIS computer (preview, rotate, random, clear). Nothing is sent until Ready.
//  * Ready sends the fleet ONCE to the host (SubmitFleetServerRpc).
//  * A click on Enemy Waters only ASKS the host (FireServerRpc). The cell changes only when the host
//    answers (a new entry in the Shots NetworkList).
//
// HOW TO USE: in BattleshipScene, Create Empty "BattleshipUI" > Add Component > Battleship UI. That's all.
using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class BattleshipUI : MonoBehaviour
{
    [Tooltip("Main Menu returns here (LobbyScene, so the NetworkManager is not duplicated).")]
    [SerializeField] private string lobbySceneName = "LobbyScene";
    [SerializeField] private float cellSize = 54f;
    [Tooltip("The look of the game: Comic Book or Naval Command")]
    [SerializeField] private BattleshipTheme theme = BattleshipTheme.ComicBook;

    [Header("Your own sounds (optional - empty slots use the built-in sounds)")]
    [SerializeField] private AudioClip fireSound;
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private AudioClip missSound;
    [SerializeField] private AudioClip sunkSound;
    [SerializeField] private AudioClip winSound;
    [SerializeField] private AudioClip loseSound;
    [SerializeField] private AudioClip chatSound;

    // ── layout (1920 x 1080 reference) ────────────────────────────────────────
    private static readonly Vector2 FleetTopLeft = new Vector2(200, 330);
    private static readonly Vector2 EnemyTopLeft = new Vector2(1130, 330);

    // ── UI references ─────────────────────────────────────────────────────────
    private RectTransform canvasRT, content;
    private BattleshipFx fx;
    // quick chat: [0] = my bubble (left), [1] = opponent's bubble (right)
    private readonly RectTransform[] bubble = new RectTransform[2];
    private readonly TextMeshProUGUI[] bubbleText = new TextMeshProUGUI[2];
    private readonly CanvasGroup[] bubbleGroup = new CanvasGroup[2];
    private readonly float[] bubbleTime = { -1f, -1f };
    private float lastChatClick = -10f;
    private bool endSoundPlayed;
    private GridView myGrid, enemyGrid;
    private TextMeshProUGUI meText, phaseText, opponentText, hintText, messageText;
    private Image phasePill;
    private RectTransform shipPanel, bottomBar, overlay;
    private readonly Image[] shipRowFill = new Image[Ships.Count];
    private readonly TextMeshProUGUI[] shipRowStatus = new TextMeshProUGUI[Ships.Count];
    private Button readyButton, rematchButton, mainMenuButton;
    private Image readyFill, overlayCard, overlayCardBorder;
    private TextMeshProUGUI readyLabel, overlayTitle, overlaySub, overlaySmall;

    // ── local state ──────────────────────────────────────────────────────────
    private BattleshipManager mgr;
    private readonly ShipPlacement[] myShips = new ShipPlacement[Ships.Count];
    private int  selected = 0;
    private bool vertical;
    private Vector2Int hover = new Vector2Int(-1, -1);
    private bool locked;                 // pressed Ready, waiting for the host
    private readonly bool[] enemyUsed = new bool[Ships.GridSize * Ships.GridSize];
    private readonly bool[] myHit     = new bool[Ships.GridSize * Ships.GridSize];
    private readonly List<ShipPlacement> sunkEnemy = new List<ShipPlacement>();
    private Fleet? revealed;
    private bool  pendingShot;
    private float pendingTime;
    private float messageHold;
    private bool  opponentLeft, leaving;
    private BattlePhase shownPhase = (BattlePhase)255;
    private string opponentName = "Opponent";
    private float nameTimer;
    private string rejectReason = "";

    // =====================================================================
    private void Start()
    {
        for (int i = 0; i < Ships.Count; i++) myShips[i] = new ShipPlacement { type = i };
        EnsureEventSystem();
        BsUI.ApplyTheme(theme);
        BuildUI();

        if (BattleshipManager.Instance != null) Bind(BattleshipManager.Instance);
        else BattleshipManager.Spawned += Bind;

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        SetMessage("Waiting for the game to start...", BsUI.SoftText);
    }

    private void OnDestroy()
    {
        BattleshipManager.Spawned -= Bind;
        if (mgr != null)
        {
            mgr.CurrentPhase.OnValueChanged   -= OnPhaseChanged;
            mgr.TurnClientId.OnValueChanged   -= OnUlongChanged;
            mgr.WinnerClientId.OnValueChanged -= OnUlongChanged;
            mgr.HostReady.OnValueChanged      -= OnBoolChanged;
            mgr.ClientReady.OnValueChanged    -= OnBoolChanged;
            mgr.Shots.OnListChanged           -= OnShotsChanged;
            mgr.ShipSunk      -= OnShipSunk;
            mgr.FleetRevealed -= OnFleetRevealed;
            mgr.FleetRejected -= OnFleetRejected;
            mgr.ChatReceived  -= OnChatReceived;
        }
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void Bind(BattleshipManager m)
    {
        BattleshipManager.Spawned -= Bind;
        mgr = m;
        mgr.CurrentPhase.OnValueChanged   += OnPhaseChanged;
        mgr.TurnClientId.OnValueChanged   += OnUlongChanged;
        mgr.WinnerClientId.OnValueChanged += OnUlongChanged;
        mgr.HostReady.OnValueChanged      += OnBoolChanged;
        mgr.ClientReady.OnValueChanged    += OnBoolChanged;
        mgr.Shots.OnListChanged           += OnShotsChanged;
        mgr.ShipSunk      += OnShipSunk;
        mgr.FleetRevealed += OnFleetRevealed;
        mgr.FleetRejected += OnFleetRejected;
        mgr.ChatReceived  += OnChatReceived;
        RebuildShots();
        Refresh();
    }

    private void OnPhaseChanged(BattlePhase a, BattlePhase b) => Refresh();
    private void OnUlongChanged(ulong a, ulong b) { pendingShot = false; Refresh(); }
    private void OnBoolChanged(bool a, bool b) => Refresh();

    // =====================================================================
    private void Update()
    {
        // R key rotates the selected ship
        if (RotateKeyPressed()) OnRotate();

        // opponent name (from their PlayerData object)
        nameTimer -= Time.deltaTime;
        if (nameTimer <= 0f)
        {
            nameTimer = 0.5f;
            string n = FindOpponentName();
            if (n != opponentName) { opponentName = n; RefreshTopBar(); }
        }

        // lost the connection without a callback (e.g. the host closed the game)
        var nm = NetworkManager.Singleton;
        if (!leaving && !opponentLeft && mgr != null && (nm == null || !nm.IsListening)) ShowOpponentLeft();

        if (pendingShot && Time.time - pendingTime > 4f) pendingShot = false;   // host never answered
        UpdateBubbles();
    }

    // =====================================================================
    // Refresh: show the right panels and texts for the current phase
    // =====================================================================
    private void Refresh()
    {
        if (mgr == null || opponentLeft) return;
        BattlePhase phase = mgr.CurrentPhase.Value;

        if (phase != shownPhase)
        {
            if (phase == BattlePhase.Placement) ResetLocalGame();     // first time, and after a Rematch
            shownPhase = phase;
        }

        bool placing = phase == BattlePhase.Placement;
        shipPanel.gameObject.SetActive(placing);
        hintText.gameObject.SetActive(placing);
        enemyGrid.SetVisible(!placing);
        bottomBar.gameObject.SetActive(!placing);

        if (phase == BattlePhase.Battle && !mgr.IsMyTurn) enemyGrid.ClearAim();
        if (phase == BattlePhase.GameOver) ShowGameOver(mgr.WinnerClientId.Value == BattleshipManager.LocalId);
        else overlay.gameObject.SetActive(false);

        if (phase == BattlePhase.Battle && messageHold < Time.time && string.IsNullOrEmpty(messageText.text))
            SetMessage(mgr.IsMyTurn ? "Fire at Enemy Waters!" : "Waiting for the enemy to fire...", BsUI.DarkText);

        RefreshTopBar();
        RefreshShipList();
        DrawMyGrid();
        DrawEnemyGrid();
    }

    private void RefreshTopBar()
    {
        meText.text = BsUI.Naval ? $"ADM. {AuthManager.LocalPlayerName} (you)"
                    : BsUI.Tropical ? $"Capt. {AuthManager.LocalPlayerName} (you)" : $"{AuthManager.LocalPlayerName} (you)";
        if (opponentLeft) { SetPhasePill("Opponent left", false); opponentText.text = ""; return; }
        if (mgr == null)  { SetPhasePill("Connecting...", false); opponentText.text = ""; return; }

        switch (mgr.CurrentPhase.Value)
        {
            case BattlePhase.Placement:
                SetPhasePill(locked ? "Waiting for opponent..." : "Placement phase", !locked);
                opponentText.text = $"{opponentName}: " + (mgr.OpponentReady ? "ready" : "still placing");
                break;
            case BattlePhase.Battle:
                SetPhasePill(mgr.IsMyTurn ? "Your turn: fire at Enemy Waters" : $"{opponentName}'s turn...", mgr.IsMyTurn);
                opponentText.text = $"Opponent: {opponentName}";
                break;
            default:
                SetPhasePill("Game over", false);
                opponentText.text = $"Opponent: {opponentName}";
                break;
        }
    }

    private void SetPhasePill(string text, bool active)
    {
        phaseText.text = text;
        phasePill.color = active ? BsUI.LightBlue : BsUI.PillOff;
        phasePill.transform.parent.GetComponent<Image>().color = active ? BsUI.Blue : BsUI.Grey;
    }

    // =====================================================================
    // PLACEMENT (only on this computer)
    // =====================================================================
    private void ResetLocalGame()
    {
        for (int i = 0; i < Ships.Count; i++) myShips[i] = new ShipPlacement { type = i };
        selected = 0; vertical = false; locked = false; pendingShot = false; revealed = null;
        Array.Clear(enemyUsed, 0, enemyUsed.Length);
        Array.Clear(myHit, 0, myHit.Length);
        sunkEnemy.Clear();
        endSoundPlayed = false;
        myGrid.ClearMarks(); enemyGrid.ClearMarks(); enemyGrid.ClearAim();
        SetMessage("", BsUI.DarkText);
    }

    private bool CanEditFleet => mgr != null && !opponentLeft && mgr.CurrentPhase.Value == BattlePhase.Placement && !locked;

    private void OnShipRowClicked(int i)
    {
        if (!CanEditFleet) return;
        if (myShips[i].placed) { vertical = myShips[i].vertical; myShips[i].placed = false; }   // pick it up again
        selected = i;
        fx.Tick();
        RefreshShipList(); DrawMyGrid();
    }

    private void OnRotate()
    {
        if (!CanEditFleet || selected < 0) return;
        vertical = !vertical;
        fx.Tick();
        DrawMyGrid();
    }

    private void OnRandom()
    {
        if (!CanEditFleet) return;
        for (int i = 0; i < Ships.Count; i++) myShips[i] = new ShipPlacement { type = i };
        for (int i = 0; i < Ships.Count; i++)
        {
            for (int tries = 0; tries < 1000; tries++)
            {
                var s = new ShipPlacement { type = i, x = UnityEngine.Random.Range(0, Ships.GridSize), y = UnityEngine.Random.Range(0, Ships.GridSize),
                                            vertical = UnityEngine.Random.value < 0.5f, placed = true };
                if (Fleet.CanPlace(s, myShips)) { myShips[i] = s; break; }
            }
        }
        selected = -1;
        fx.Clunk();
        RefreshShipList(); DrawMyGrid();
    }

    private void OnClear()
    {
        if (!CanEditFleet) return;
        for (int i = 0; i < Ships.Count; i++) myShips[i] = new ShipPlacement { type = i };
        selected = 0; vertical = false;
        RefreshShipList(); DrawMyGrid();
    }

    private void OnReady()
    {
        if (!CanEditFleet || !AllPlaced()) return;
        if (!Fleet.Validate(myShips, out string why)) { SetMessage(why, BsUI.Red); return; }
        locked = true;
        selected = -1;
        rejectReason = "";
        mgr.SubmitFleetServerRpc(Fleet.From(myShips));       // the ONLY time ship positions leave this computer
        fx.Blip();
        RefreshTopBar(); RefreshShipList(); DrawMyGrid();
    }

    private void OnFleetRejected(string reason)
    {
        locked = false;
        rejectReason = reason;
        Refresh();
    }

    private bool AllPlaced()
    {
        foreach (var s in myShips) if (!s.placed) return false;
        return true;
    }

    private int NextUnplaced()
    {
        for (int i = 0; i < Ships.Count; i++) if (!myShips[i].placed) return i;
        return -1;
    }

    private ShipPlacement PreviewShip() =>
        new ShipPlacement { type = selected, x = hover.x, y = hover.y, vertical = vertical, placed = true };

    private void OnMyGridHover(int x, int y) { hover = new Vector2Int(x, y); DrawMyGrid(); }
    private void OnMyGridLeave()             { hover = new Vector2Int(-1, -1); DrawMyGrid(); }

    private void OnMyGridClick(int x, int y)
    {
        hover = new Vector2Int(x, y);
        if (!CanEditFleet || selected < 0) return;
        var ship = PreviewShip();
        if (!Fleet.CanPlace(ship, myShips)) return;           // red preview: nothing happens
        myShips[selected] = ship;
        fx.Clunk();
        selected = NextUnplaced();
        RefreshShipList(); DrawMyGrid();
    }

    private void RefreshShipList()
    {
        for (int i = 0; i < Ships.Count; i++)
        {
            bool isSel = i == selected && CanEditFleet;
            shipRowFill[i].color = isSel ? BsUI.LightBlue : BsUI.ButtonFill;
            shipRowFill[i].transform.parent.GetComponent<Image>().color = isSel ? BsUI.Blue : BsUI.PanelLine;
            shipRowStatus[i].text  = isSel ? "SELECTED" : myShips[i].placed ? "PLACED" : "";
            shipRowStatus[i].color = isSel ? BsUI.Amber : BsUI.Green;
        }

        bool canReady = AllPlaced() && CanEditFleet;
        readyButton.interactable = canReady;
        readyFill.color  = canReady ? BsUI.Blue : BsUI.PillOff;
        readyLabel.color = canReady ? BsUI.ButtonText : BsUI.Grey;
        readyLabel.text  = locked ? "Waiting for opponent..." : AllPlaced() ? "Ready" : "Ready (place all 3 ships first)";
        hintText.text = locked ? "Your fleet is locked and was sent to the host."
                      : !string.IsNullOrEmpty(rejectReason) ? $"<color=#E57373>The host rejected your fleet: {rejectReason}</color>"
                      : "Hover to preview, R to rotate, click to place. A red preview means illegal.";
    }

    // =====================================================================
    // DRAWING
    // =====================================================================
    private void DrawMyGrid()
    {
        if (myGrid == null) return;
        myGrid.FillAll(BsUI.Water);
        myGrid.ClearShips();
        foreach (var s in myShips)
            if (s.placed) myGrid.AddShip(s, BsUI.Ship);

        // placement preview: blue = legal, red = not legal
        if (CanEditFleet && selected >= 0 && hover.x >= 0)
        {
            var ship = PreviewShip();
            bool ok = Fleet.CanPlace(ship, myShips);
            Color col = ok ? BsUI.PreviewOk : BsUI.PreviewBad;
            col.a = 0.85f;
            myGrid.AddShip(ship, col);
            for (int c = 0; c < ship.Length; c++) { var p = ship.Cell(c); myGrid.SetColor(p.x, p.y, ok ? BsUI.PreviewCellOk : BsUI.PreviewCellBad); }
        }
    }

    private void DrawEnemyGrid()
    {
        if (enemyGrid == null) return;
        enemyGrid.FillAll(BsUI.Water);
        enemyGrid.ClearShips();
        if (revealed.HasValue)                                    // game over, loser: where the enemy ships were
            foreach (var s in revealed.Value.ToArray())
                if (!sunkEnemy.Exists(k => k.type == s.type)) enemyGrid.AddShip(s, BsUI.Revealed);
        foreach (var s in sunkEnemy) enemyGrid.AddShip(s, BsUI.SunkEnemy);
    }

    // =====================================================================
    // BATTLE
    // =====================================================================
    private void OnEnemyHover(int x, int y)
    {
        enemyGrid.ClearAim();
        if (mgr != null && mgr.IsMyTurn && !enemyUsed[Ships.Index(x, y)] && !pendingShot) enemyGrid.SetAim(x, y, true);
    }

    private void OnEnemyLeave() { enemyGrid.ClearAim(); }

    private void OnEnemyClick(int x, int y)
    {
        if (mgr == null || opponentLeft || !mgr.IsMyTurn || pendingShot) return;
        if (enemyUsed[Ships.Index(x, y)]) return;
        pendingShot = true; pendingTime = Time.time;
        enemyGrid.ClearAim();
        fx.Fire();                                                // cannon sound right away (the result comes from the host)
        mgr.FireServerRpc(x, y);                                  // only ASK; the cell changes when the host answers
    }

    /// <summary>The host added a shot to the NetworkList (or cleared it for a rematch).</summary>
    private void OnShotsChanged(NetworkListEvent<ShotInfo> e)
    {
        if (e.Type == NetworkListEvent<ShotInfo>.EventType.Add) ApplyShot(e.Value, true);
        else RebuildShots();
    }

    private void RebuildShots()
    {
        if (mgr == null) return;
        Array.Clear(enemyUsed, 0, enemyUsed.Length);
        Array.Clear(myHit, 0, myHit.Length);
        myGrid.ClearMarks(); enemyGrid.ClearMarks();
        foreach (var s in mgr.Shots) ApplyShot(s, false);
    }

    private void ApplyShot(ShotInfo s, bool announce)
    {
        string cell = Ships.CellName(s.x, s.y);
        var mark = s.hit ? GridView.Mark.Hit : GridView.Mark.Miss;
        if (s.shooter == BattleshipManager.LocalId)
        {
            enemyUsed[Ships.Index(s.x, s.y)] = true;
            enemyGrid.SetMark(s.x, s.y, mark);
            pendingShot = false;
            if (announce)
            {
                Vector2 at = enemyGrid.CellCenter(s.x, s.y);
                if (s.hit) fx.Hit(at); else fx.Miss(at);
            }
            if (announce) SetMessage(s.hit ? $"Hit at {cell}!" : $"Miss at {cell}.", s.hit ? BsUI.Red : BsUI.DarkText);
        }
        else
        {
            if (s.hit) myHit[Ships.Index(s.x, s.y)] = true;
            myGrid.SetMark(s.x, s.y, mark);
            if (announce)
            {
                fx.Incoming();                                    // falling-bomb whistle, then the impact on my fleet
                Vector2 at = myGrid.CellCenter(s.x, s.y);
                if (s.hit) fx.Hit(at, 0.38f, 1.15f); else fx.Miss(at, 0.38f);
            }
            if (announce) SetMessage(s.hit ? $"{opponentName} hit your {ShipAt(s.x, s.y)} at {cell}!" : $"{opponentName} missed at {cell}.",
                                     s.hit ? BsUI.Red : BsUI.DarkText);
        }
    }

    private string ShipAt(int x, int y)
    {
        foreach (var s in myShips) if (s.placed && s.Covers(x, y)) return Ships.Names[s.type];
        return "ship";
    }

    private void OnShipSunk(ulong shooter, ShipPlacement ship)
    {
        if (shooter == BattleshipManager.LocalId)
        {
            sunkEnemy.Add(ship);
            DrawEnemyGrid();
            fx.Sunk(enemyGrid, ship, 0.45f);
            SetMessage($"You sank their {Ships.Names[ship.type]}!", BsUI.Green, 2.5f);
        }
        else
        {
            SetMessage($"They sank your {Ships.Names[ship.type]}!", BsUI.Red, 2.5f);
            fx.Sunk(myGrid, ship, 0.8f);
        }
    }

    private void OnFleetRevealed(Fleet enemyFleet)
    {
        revealed = enemyFleet;
        DrawEnemyGrid();
    }

    private void SetMessage(string text, Color color, float hold = 0f)
    {
        if (messageText == null) return;
        if (hold <= 0f && Time.time < messageHold) return;        // keep an important message ("You sank...") for a moment
        messageText.text = text;
        messageText.color = color;
        if (hold > 0f) messageHold = Time.time + hold;
    }

    // =====================================================================
    // CREATIVITY: QUICK CHAT (ServerRpc -> host checks -> ClientRpc to both)
    // =====================================================================
    private void OnChatClicked(int id)
    {
        if (mgr == null || opponentLeft || Time.time - lastChatClick < 1f) return;
        lastChatClick = Time.time;
        mgr.SendChatServerRpc(id);
    }

    private void OnChatReceived(ulong sender, int id)
    {
        if (id < 0 || id >= Ships.Chat.Length) return;
        int side = sender == BattleshipManager.LocalId ? 0 : 1;
        bubbleText[side].text = side == 0 ? Ships.Chat[id] : $"{opponentName}: {Ships.Chat[id]}";
        bubbleTime[side] = 0f;
        bubble[side].gameObject.SetActive(true);
        fx.Pop();
    }

    private void UpdateBubbles()
    {
        for (int i = 0; i < 2; i++)
        {
            if (bubble[i] == null || bubbleTime[i] < 0f) continue;
            bubbleTime[i] += Time.deltaTime;
            float t = bubbleTime[i];
            float pop = t < 0.18f ? Mathf.Lerp(0.6f, 1.08f, t / 0.18f) : t < 0.3f ? Mathf.Lerp(1.08f, 1f, (t - 0.18f) / 0.12f) : 1f;
            bubble[i].localScale = new Vector3(pop, pop, 1f);
            bubbleGroup[i].alpha = Mathf.Clamp01((3.2f - t) / 0.5f);
            if (t > 3.2f) { bubbleTime[i] = -1f; bubble[i].gameObject.SetActive(false); }
        }
    }

    // =====================================================================
    // GAME OVER / LEAVING
    // =====================================================================
    private void ShowGameOver(bool won)
    {
        overlay.gameObject.SetActive(true);
        if (!endSoundPlayed) { endSoundPlayed = true; if (won) fx.Victory(); else fx.Defeat(); }
        overlayCardBorder.color = won ? BsUI.Green : BsUI.PanelLine;
        overlayCardBorder.color = won ? BsUI.Green : BsUI.Red;
        overlayCard.color = won ? BsUI.CardWin : BsUI.CardLose;
        overlayTitle.text  = won ? "You win!" : "You lose";
        overlayTitle.color = won ? BsUI.Green : BsUI.Red;
        overlaySub.text    = won ? "All 3 enemy ships sunk" : "All 3 of your ships were sunk";
        overlaySmall.text  = BsUI.Tropical
            ? (won ? "ALOHA, CAPTAIN!  The final boards stay visible behind this panel" : "WIPEOUT!  Remaining enemy ships are now revealed")
            : BsUI.Comic
            ? (won ? "HOORAY!  The final boards stay visible behind this panel" : "CURSES!  Remaining enemy ships are now revealed")
            : (won ? "MISSION ACCOMPLISHED  -  the final boards stay visible" : "FLEET DESTROYED  -  remaining enemy ships are revealed");
        rematchButton.gameObject.SetActive(true);
        SetMessage(won ? "Victory! All enemy ships sunk." : "Defeat. Their remaining ships are shown in grey.", won ? BsUI.Green : BsUI.Red, 0.1f);
    }

    private void ShowOpponentLeft()
    {
        opponentLeft = true;
        overlay.gameObject.SetActive(true);
        overlayCardBorder.color = BsUI.PanelLine;
        overlayCard.color = BsUI.CardLose;
        overlayTitle.text = "Opponent left";
        overlayTitle.color = BsUI.DarkText;
        overlaySub.text = "The match has ended.";
        overlaySmall.text = "Go back to the lobby to start a new game.";
        rematchButton.gameObject.SetActive(false);
        RefreshTopBar();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (leaving) return;
        // Host: the other player left. Client: we lost the host.
        ShowOpponentLeft();
    }

    private void OnRematch()
    {
        if (mgr != null && !opponentLeft) mgr.RequestRematchServerRpc();
    }

    private async void OnMainMenu()
    {
        if (leaving) return;
        leaving = true;
        mainMenuButton.interactable = false;
        if (LobbyManager.Instance != null) await LobbyManager.Instance.LeaveLobby();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene(lobbySceneName);
    }

    // =====================================================================
    // Helpers
    // =====================================================================
    private string FindOpponentName()
    {
        foreach (var p in FindObjectsByType<PlayerData>(FindObjectsSortMode.None))
            if (p.IsSpawned && !p.IsOwner)
            {
                string n = p.PlayerName.Value.ToString();
                if (!string.IsNullOrEmpty(n)) return n;
            }
        return "Opponent";
    }

    private static bool RotateKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        return kb != null && kb.rKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.R);
#else
        return false;
#endif
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    // =====================================================================
    // BUILD THE SCREEN (all from code)
    // =====================================================================
    private void BuildUI()
    {
        var canvasGO = new GameObject("Battleship Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRT = (RectTransform)canvasGO.transform;

        // background
        var bg = BsUI.Image(canvasRT, "Background", Vector2.zero, new Vector2(1920, 1080), BsUI.Background);
        bg.rectTransform.anchorMin = Vector2.zero; bg.rectTransform.anchorMax = Vector2.one;
        bg.rectTransform.offsetMin = bg.rectTransform.offsetMax = Vector2.zero;
        bg.raycastTarget = false;

        if (BsUI.Comic) ComicBackground.Create(canvasRT);           // THEME: halftone dots + rotating sunburst
        else if (BsUI.Tropical) TropicalBackground.Create(canvasRT); // THEME: islands, fish, bubbles, sparkles
        else OceanBackground.Create(canvasRT);                      // THEME: moving green waves

        // everything below goes into "content", so the screen can shake (the background and overlay stay still)
        content = BsUI.Rect(canvasRT, "Content", Vector2.zero, new Vector2(1920, 1080));
        content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one;
        content.offsetMin = content.offsetMax = Vector2.zero;

        // ── top bar ──
        BsUI.Box(content, "Top Bar", new Vector2(60, 30), new Vector2(1800, 100), BsUI.Panel, BsUI.PanelLine);
        if (BsUI.Naval) BsUI.Brackets(content, new Vector2(48, 18), new Vector2(1824, 124), BsUI.Amber, 34f, 4f);
        meText       = BsUI.Text(content, "Me", "", new Vector2(100, 30), new Vector2(560, 100), 34, BsUI.DarkText, TextAlignmentOptions.Left);
        opponentText = BsUI.Text(content, "Opponent", "", new Vector2(1260, 30), new Vector2(560, 100), 34, BsUI.DarkText, TextAlignmentOptions.Right);
        phasePill    = BsUI.Box(content, "Phase Pill", new Vector2(640, 48), new Vector2(640, 64), BsUI.LightBlue, BsUI.Blue, 4f);
        phaseText    = BsUI.Text(content, "Phase", "", new Vector2(640, 48), new Vector2(640, 64), 30, BsUI.DarkText, TextAlignmentOptions.Center, true);

        // ── grids ──
        myGrid = GridView.Create(content, "Fleet", "MY FLEET", "Your ships and the enemy shots on them", FleetTopLeft, cellSize);
        myGrid.CellHovered += OnMyGridHover;
        myGrid.PointerLeft += OnMyGridLeave;
        myGrid.CellClicked += OnMyGridClick;

        enemyGrid = GridView.Create(content, "Enemy", "ENEMY WATERS",
                                    BsUI.Comic ? "Your shots - their ships stay hidden!"
                                    : BsUI.Tropical ? "Your shots - their ships hide in the waves"
                                    : "Radar contact: your shots - their ships stay hidden", EnemyTopLeft, cellSize);
        if (BsUI.Naval) enemyGrid.EnableRadar();          // THEME: rotating radar sweep
        enemyGrid.CellHovered += OnEnemyHover;
        enemyGrid.PointerLeft += OnEnemyLeave;
        enemyGrid.CellClicked += OnEnemyClick;

        // effects layer (explosions, splashes) on top of both grids
        var fxLayer = BsUI.Rect(content, "Effects", Vector2.zero, new Vector2(1, 1));
        fx = BattleshipFx.Create(fxLayer, content);
        fx.UseCustomSounds(fireSound, hitSound, missSound, sunkSound, winSound, loseSound, chatSound);

        hintText = BsUI.Text(content, "Hint", "", new Vector2(200, 900), new Vector2(900, 40), 24, BsUI.SoftText);

        // ── ships to place (placement phase) ──
        shipPanel = BsUI.Rect(content, "Ship Panel", new Vector2(1130, 0), new Vector2(1, 1));
        BsUI.Text(shipPanel, "Title", "SHIPS TO PLACE", new Vector2(0, 208), new Vector2(700, 50), 40, BsUI.White, TextAlignmentOptions.Left, true);
        for (int i = 0; i < Ships.Count; i++)
        {
            int index = i;
            float y = 280 + i * 110;
            BsUI.Button(shipPanel, Ships.Names[i], "", new Vector2(0, y), new Vector2(700, 92), BsUI.ButtonFill, BsUI.DarkText, BsUI.PanelLine, 30,
                        () => OnShipRowClicked(index), out _);
            var row = (RectTransform)shipPanel.Find(Ships.Names[i]);
            shipRowFill[i] = row.Find("Fill").GetComponent<Image>();
            BsUI.Text(row, "Name", Ships.Names[i], new Vector2(36, 0), new Vector2(260, 92), 32, BsUI.DarkText, TextAlignmentOptions.Left, true);
            for (int c = 0; c < Ships.Lengths[i]; c++)
                BsUI.Image(row, "Block", new Vector2(300 + c * 40, 28), new Vector2(34, 34), BsUI.Ship, true).raycastTarget = false;
            shipRowStatus[i] = BsUI.Text(row, "Status", "", new Vector2(460, 0), new Vector2(210, 92), 30, BsUI.Green, TextAlignmentOptions.Right, true);
        }
        BsUI.Button(shipPanel, "Rotate", "Rotate (R)", new Vector2(0, 620), new Vector2(220, 80), BsUI.ButtonFill, BsUI.DarkText, BsUI.PanelLine, 30, OnRotate, out _);
        BsUI.Button(shipPanel, "Random", "Random", new Vector2(240, 620), new Vector2(220, 80), BsUI.ButtonFill, BsUI.DarkText, BsUI.PanelLine, 30, OnRandom, out _);
        BsUI.Button(shipPanel, "Clear", "Clear", new Vector2(480, 620), new Vector2(220, 80), BsUI.ButtonFill, BsUI.DarkText, BsUI.PanelLine, 30, OnClear, out _);
        readyButton = BsUI.Button(shipPanel, "Ready", "Ready", new Vector2(0, 730), new Vector2(700, 90), BsUI.Blue, BsUI.ButtonText, BsUI.PanelLine, 32, OnReady, out readyLabel);
        readyFill = readyButton.transform.Find("Fill").GetComponent<Image>();

        // ── creativity: quick chat buttons (always visible) + speech bubbles ──
        BsUI.Text(content, "Chat Label", "QUICK CHAT", new Vector2(1130, 878), new Vector2(200, 24), 18, BsUI.SoftText);
        for (int i = 0; i < Ships.Chat.Length; i++)
        {
            int id = i;
            BsUI.Button(content, "Chat " + Ships.Chat[i], Ships.Chat[i], new Vector2(1130 + i * 118, 900), new Vector2(112, 40),
                        BsUI.ButtonFill, BsUI.DarkText, BsUI.GridLine, 17, () => OnChatClicked(id), out _);
        }
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? 80 : 1460;
            var b = BsUI.Rect(content, side == 0 ? "My Bubble" : "Their Bubble", new Vector2(x, 140), new Vector2(380, 56));
            b.pivot = new Vector2(0.5f, 1f);
            b.anchoredPosition = new Vector2(x + 190, -140);
            BsUI.Box(b, "Box", Vector2.zero, new Vector2(380, 56), BsUI.Panel, side == 0 ? BsUI.Green : BsUI.Amber, 3f);
            bubbleText[side] = BsUI.Text(b, "Text", "", Vector2.zero, new Vector2(380, 56), 24, BsUI.DarkText, TextAlignmentOptions.Center, true);
            bubbleGroup[side] = b.gameObject.AddComponent<CanvasGroup>();
            bubbleGroup[side].blocksRaycasts = false;
            bubble[side] = b;
            b.gameObject.SetActive(false);
        }

        // ── bottom bar: message + legend (battle) ──
        bottomBar = BsUI.Rect(content, "Bottom Bar", new Vector2(60, 950), new Vector2(1800, 100));
        BsUI.Box(bottomBar, "Box", Vector2.zero, new Vector2(1800, 100), BsUI.Panel, BsUI.PanelLine);
        messageText = BsUI.Text(bottomBar, "Message", "", new Vector2(40, 0), new Vector2(820, 100), 32, BsUI.DarkText, TextAlignmentOptions.Left, true);
        LegendItem(bottomBar, 880,  BsUI.CrossSprite, BsUI.HitColor, "Hit", false);
        LegendItem(bottomBar, 1010, BsUI.DotSprite, BsUI.MissColor, "Miss", false);
        LegendItem(bottomBar, 1150, BsUI.RoundedSprite, BsUI.Ship, "Your ship", true);
        LegendItem(bottomBar, 1350, BsUI.RoundedSprite, BsUI.SunkEnemy, "Sunk enemy", true);
        LegendItem(bottomBar, 1590, BsUI.FrameSprite, BsUI.AimColor, "Your aim", true);

        // THEME: CRT scanlines over the whole screen (under the game-over panel)
        if (BsUI.Naval) Scanlines.Create(canvasRT);

        // ── game over / opponent left overlay ──
        overlay = BsUI.Rect(canvasRT, "Overlay", Vector2.zero, new Vector2(1920, 1080));
        var dim = overlay.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.45f);                    // also blocks clicks on the boards
        overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        overlayCardBorder = BsUI.Image(overlay, "Card", new Vector2(510, 260), new Vector2(900, 560), BsUI.Green, true);
        overlayCard = BsUI.Image(overlayCardBorder.rectTransform, "Fill", new Vector2(6, 6), new Vector2(888, 548), BsUI.CardWin, true);
        var card = overlayCardBorder.rectTransform;
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);   // always in the middle of the screen
        card.anchoredPosition = Vector2.zero;
        overlayTitle = BsUI.Text(card, "Title", "", new Vector2(0, 70), new Vector2(900, 130), 110, BsUI.Green, TextAlignmentOptions.Center, true);
        overlaySub   = BsUI.Text(card, "Sub", "", new Vector2(0, 215), new Vector2(900, 50), 40, BsUI.DarkText, TextAlignmentOptions.Center, true);
        overlaySmall = BsUI.Text(card, "Small", "", new Vector2(0, 280), new Vector2(900, 40), 24, BsUI.Amber, TextAlignmentOptions.Center);
        rematchButton  = BsUI.Button(card, "Rematch", "Rematch", new Vector2(120, 420), new Vector2(300, 90), BsUI.Blue, BsUI.ButtonText, BsUI.Green, 36, OnRematch, out _);
        mainMenuButton = BsUI.Button(card, "Main Menu", "Main Menu", new Vector2(480, 420), new Vector2(300, 90), BsUI.ButtonFill, BsUI.DarkText, BsUI.PanelLine, 36, OnMainMenu, out _);
        overlay.gameObject.SetActive(false);

        // before the game manager spawns
        enemyGrid.SetVisible(false);
        bottomBar.gameObject.SetActive(true);
        RefreshTopBar();
        RefreshShipList();
        DrawMyGrid();
    }

    private static void LegendItem(RectTransform parent, float x, Sprite sprite, Color color, string label, bool sliced)
    {
        var img = BsUI.Image(parent, label + " Icon", new Vector2(x, 32), new Vector2(36, 36), color);
        img.sprite = sprite;
        if (sliced) img.type = Image.Type.Sliced;
        img.raycastTarget = false;
        BsUI.Text(parent, label, label, new Vector2(x + 46, 0), new Vector2(200, 100), 28, BsUI.DarkText, TextAlignmentOptions.Left);
    }
}
