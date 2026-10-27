using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// One board cell (Cell prefab). Spawned by BoardSpawner.
/// Shows its mark and sends a move request when clicked on your turn.
///
/// Works with the new Input System (Unity 6 default) and the old Input Manager.
/// </summary>
// Needs a Box Collider 2D on the same GameObject (add it before this script).
public class CellController : NetworkBehaviour
{
    [Header("Renderers")]
    [Tooltip("Renderer on the Cell root (the background square).")]
    [SerializeField] private SpriteRenderer backgroundRenderer;
    [Tooltip("Optional child renderer for the X/O mark. If empty, the background sprite is swapped instead.")]
    [SerializeField] private SpriteRenderer markRenderer;

    [Header("Sprites")]
    [SerializeField] private Sprite emptySprite;
    [SerializeField] private Sprite xSprite;
    [SerializeField] private Sprite oSprite;

    [Header("Colours")]
    [SerializeField] private Color emptyColor = new Color(0.91f, 0.92f, 0.96f); // #E8EAF6
    [SerializeField] private Color xColor     = new Color(0.10f, 0.45f, 0.91f); // #1A73E8
    [SerializeField] private Color oColor     = new Color(0.90f, 0.22f, 0.21f); // #E53935

    /// <summary>0 = empty, 1 = X, 2 = O. Written by the server only.</summary>
    public NetworkVariable<int> Mark = new NetworkVariable<int>(GameManager.Empty);

    /// <summary>Board position 0–8. Set by BoardSpawner on the server before spawning.</summary>
    public NetworkVariable<int> Index = new NetworkVariable<int>(-1);

    private Collider2D cellCollider;
    private Camera     cam;

    private void Awake()
    {
        cellCollider = GetComponent<Collider2D>();
        if (cellCollider == null)
            Debug.LogError($"[CellController] {name} needs a Box Collider 2D to be clickable.", this);
        if (backgroundRenderer == null) backgroundRenderer = GetComponent<SpriteRenderer>();
    }

    public override void OnNetworkSpawn()
    {
        Mark.OnValueChanged += OnMarkChanged;
        Refresh(Mark.Value);
    }

    public override void OnNetworkDespawn()
    {
        Mark.OnValueChanged -= OnMarkChanged;
    }

    private void OnMarkChanged(int previous, int current) => Refresh(current);

    private void Refresh(int mark)
    {
        Sprite markSprite = mark == GameManager.X ? xSprite : oSprite;
        Color  markColor  = mark == GameManager.X ? xColor  : oColor;

        if (markRenderer != null)
        {
            // Two-renderer setup: background stays, mark appears on top
            if (backgroundRenderer != null)
            {
                if (emptySprite != null) backgroundRenderer.sprite = emptySprite;
                backgroundRenderer.color = emptyColor;
            }
            markRenderer.enabled = mark != GameManager.Empty;
            markRenderer.sprite  = markSprite;
            markRenderer.color   = markColor;
        }
        else if (backgroundRenderer != null)
        {
            // Single-renderer setup: swap the sprite itself
            backgroundRenderer.sprite = mark == GameManager.Empty ? emptySprite : markSprite;
            backgroundRenderer.color  = mark == GameManager.Empty ? emptyColor  : markColor;
        }
    }

    private void Update()
    {
        if (!IsSpawned || cellCollider == null) return;
        if (!TryGetPointerDown(out Vector2 screenPos)) return;

        GameManager gm = GameManager.Instance;
        if (gm == null || !gm.IsLocalTurn || Mark.Value != GameManager.Empty) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector2 worldPos = cam.ScreenToWorldPoint(screenPos);
        if (cellCollider.OverlapPoint(worldPos))
            gm.RequestMoveRpc(Index.Value);
    }

    private static bool TryGetPointerDown(out Vector2 position)
    {
#if ENABLE_INPUT_SYSTEM
        Pointer pointer = Pointer.current;
        if (pointer != null && pointer.press.wasPressedThisFrame)
        {
            position = pointer.position.ReadValue();
            return true;
        }
#else
        if (Input.GetMouseButtonDown(0))
        {
            position = Input.mousePosition;
            return true;
        }
#endif
        position = default;
        return false;
    }
}
