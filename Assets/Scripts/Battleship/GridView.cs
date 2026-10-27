// GridView.cs — one 10x10 grid (My Fleet or Enemy Waters), built completely from code,
// with the A-J and 1-10 labels. Also contains BsUI: small helpers that build UI from code.
// You don't add this to anything yourself: BattleshipUI creates both grids.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>The look of the game. Pick it on the BattleshipUI object in the Inspector.</summary>
public enum BattleshipTheme { ComicBook, NavalCommand, TropicalIslands }

public class GridView : MonoBehaviour
{
    public enum Mark { None, Hit, Miss }

    public string GridName { get; private set; }     // "Fleet" or "Enemy" (used in the click log)
    public event Action<int, int> CellHovered;
    public event Action<int, int> CellClicked;
    public event Action           PointerLeft;

    private Image[] cells, dots, crosses, aims;
    private RectTransform root, shipLayer, markLayer, radarSweep;
    private TextMeshProUGUI aimLabel;
    private Vector2 topLeft;
    private float cell;
    private readonly List<Image> hulls = new List<Image>();
    private readonly List<float> hullAngle = new List<float>();
    private int hullsUsed;
    private Color[] baseColors;                       // the colour each cell should have; Update adds the moving shimmer

    public float CellSize => cell;

    /// <summary>Builds a grid with its title, subtitle and labels. topLeft = top-left of the cells (reference pixels).</summary>
    public static GridView Create(RectTransform parent, string gridName, string title, string subtitle, Vector2 topLeft, float cellSize)
    {
        var holder = BsUI.Rect(parent, gridName + " Grid", Vector2.zero, new Vector2(1, 1));
        var view = holder.gameObject.AddComponent<GridView>();
        view.GridName = gridName;
        view.root = holder;
        view.Build(title, subtitle, topLeft, cellSize);
        return view;
    }

    private void Build(string title, string subtitle, Vector2 topLeft, float cell)
    {
        this.topLeft = topLeft;
        this.cell = cell;
        int n = Ships.GridSize;
        float size = cell * n;

        BsUI.Text(root, "Title", title, topLeft + new Vector2(0, -122), new Vector2(size, 50), 40, BsUI.White, TextAlignmentOptions.Left, true);
        if (!string.IsNullOrEmpty(subtitle))
            BsUI.Text(root, "Subtitle", subtitle, topLeft + new Vector2(0, -74), new Vector2(size + 120, 32), 22, BsUI.SoftText, TextAlignmentOptions.Left);

        // column letters A-J and row numbers 1-10
        for (int i = 0; i < n; i++)
        {
            BsUI.Text(root, "Col " + (char)('A' + i), ((char)('A' + i)).ToString(), topLeft + new Vector2(i * cell, -38), new Vector2(cell, 34), 24, BsUI.White, TextAlignmentOptions.Center, true);
            BsUI.Text(root, "Row " + (i + 1), (i + 1).ToString(), topLeft + new Vector2(-56, i * cell), new Vector2(46, cell), 24, BsUI.White, TextAlignmentOptions.Right, true);
        }

        // the dark frame behind the cells shows through the gaps as grid lines
        var frame = BsUI.Image(root, "Frame", topLeft + new Vector2(-3, -3), new Vector2(size + 6, size + 6), BsUI.GridLine);
        frame.raycastTarget = false;
        if (BsUI.Naval) BsUI.Brackets(root, topLeft + new Vector2(-14, -14), new Vector2(size + 28, size + 28), BsUI.PanelLine);   // HUD corners
        else frame.rectTransform.sizeDelta = new Vector2(size + 10, size + 10);                                                    // thick comic ink frame
        if (!BsUI.Naval) frame.rectTransform.anchoredPosition = new Vector2(topLeft.x - 5, -(topLeft.y - 5));

        cells   = new Image[n * n];
        baseColors = new Color[n * n];
        dots    = new Image[n * n];
        crosses = new Image[n * n];
        aims    = new Image[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = Ships.Index(x, y);
                var img = BsUI.Image(root, $"{GridName} {Ships.CellName(x, y)}", topLeft + new Vector2(x * cell + 1.5f, y * cell + 1.5f), new Vector2(cell - 3, cell - 3), BsUI.Water);
                cells[i] = img;
                baseColors[i] = BsUI.Water;

                var input = img.gameObject.AddComponent<GridCellInput>();
                input.Setup(this, x, y);
            }

        // layers on top of the water: ship hulls first, then the hit / miss / aim marks
        shipLayer = BsUI.Rect(root, "Ships", Vector2.zero, new Vector2(1, 1));
        markLayer = BsUI.Rect(root, "Marks", Vector2.zero, new Vector2(1, 1));
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = Ships.Index(x, y);
                var slot = BsUI.Rect(markLayer, Ships.CellName(x, y), topLeft + new Vector2(x * cell + 1.5f, y * cell + 1.5f), new Vector2(cell - 3, cell - 3));
                dots[i]    = BsUI.Child(slot, "Miss", 0.34f, BsUI.DotSprite, BsUI.MissColor);
                crosses[i] = BsUI.Child(slot, "Hit",  0.78f, BsUI.CrossSprite, BsUI.HitColor);
                aims[i]    = BsUI.Child(slot, "Aim",  1.12f, BsUI.FrameSprite, BsUI.AimColor);
                dots[i].enabled = crosses[i].enabled = aims[i].enabled = false;
            }

        aimLabel = BsUI.Text(markLayer, "Target Label", "", Vector2.zero, new Vector2(160, 26), 18, BsUI.Amber, TextAlignmentOptions.Center, true);
        aimLabel.gameObject.SetActive(false);
    }

    // ── THEME: rotating radar sweep (used on Enemy Waters) ─────────────────
    public void EnableRadar()
    {
        if (radarSweep != null) return;
        float size = cell * Ships.GridSize;
        var clip = BsUI.Rect(root, "Radar", topLeft, new Vector2(size, size));
        clip.gameObject.AddComponent<RectMask2D>();                       // keeps the sweep inside the grid
        clip.SetSiblingIndex(shipLayer.GetSiblingIndex());                 // above the water, below ships and marks
        Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
        for (int k = 1; k <= 3; k++)                                        // range rings
        {
            var ring = BsUI.Child(clip, "Ring" + k, 1f, BsUI.RadarRingSprite, new Color(0.3f, 1f, 0.55f, 0.13f));
            ring.rectTransform.sizeDelta = Vector2.one * size * 0.36f * k;
        }
        var h = BsUI.Image(clip, "Cross H", new Vector2(0, c.y - 1), new Vector2(size, 2), new Color(0.3f, 1f, 0.55f, 0.12f));
        var v = BsUI.Image(clip, "Cross V", new Vector2(c.x - 1, 0), new Vector2(2, size), new Color(0.3f, 1f, 0.55f, 0.12f));
        h.raycastTarget = v.raycastTarget = false;
        var sweep = BsUI.Child(clip, "Sweep", 1f, BsUI.SweepSprite, new Color(0.35f, 1f, 0.55f, 1f));
        sweep.rectTransform.sizeDelta = Vector2.one * size * 1.45f;
        radarSweep = sweep.rectTransform;
    }

    // ── Ship hulls (real ship shapes instead of coloured squares) ────────────
    /// <summary>Centre of a cell in reference pixels (x right, y down from the top-left of the screen).</summary>
    public Vector2 CellCenter(int x, int y) => topLeft + new Vector2((x + 0.5f) * cell, (y + 0.5f) * cell);

    /// <summary>Centre of a whole ship in reference pixels.</summary>
    public Vector2 ShipCenter(ShipPlacement s)
    {
        var a = s.Cell(0); var b = s.Cell(s.Length - 1);
        return (CellCenter(a.x, a.y) + CellCenter(b.x, b.y)) * 0.5f;
    }

    public void ClearShips()
    {
        foreach (var h in hulls) h.gameObject.SetActive(false);
        hullsUsed = 0;
    }

    /// <summary>Draws one ship as a hull with a pointed bow, a deck and two gun turrets.</summary>
    public Image AddShip(ShipPlacement s, Color color)
    {
        Image hull;
        if (hullsUsed < hulls.Count) hull = hulls[hullsUsed];
        else { hull = MakeHull(); hulls.Add(hull); hullAngle.Add(0f); }
        hullAngle[hullsUsed] = s.vertical ? -90f : 0f;
        hullsUsed++;
        hull.gameObject.SetActive(true);

        var rt = hull.rectTransform;
        Vector2 c = ShipCenter(s);
        rt.anchoredPosition = new Vector2(c.x, -c.y);
        rt.sizeDelta = new Vector2(s.Length * cell - 8f, cell - 12f);
        rt.localEulerAngles = new Vector3(0, 0, s.vertical ? -90f : 0f);   // bow points right, or down
        hull.color = color;

        Color deck = Color.Lerp(color, Color.white, 0.32f); deck.a = color.a;
        Color gun  = Color.Lerp(color, Color.black, 0.35f); gun.a = color.a;
        rt.GetChild(0).GetComponent<Image>().color = deck;
        rt.GetChild(1).GetComponent<Image>().color = gun;
        rt.GetChild(2).GetComponent<Image>().color = gun;
        return hull;
    }

    private Image MakeHull()
    {
        var go = new GameObject("Hull", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(shipLayer, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0.5f, 0.5f);
        var img = go.AddComponent<Image>();
        img.sprite = BsUI.HullSprite;
        img.type = UnityEngine.UI.Image.Type.Sliced;
        img.raycastTarget = false;

        Part(rt, "Deck",    new Vector2(0.30f, 0.30f), new Vector2(0.62f, 0.70f), BsUI.RoundedSprite, true);
        Part(rt, "Turret1", new Vector2(0.12f, 0.36f), new Vector2(0.12f, 0.64f), BsUI.DotSprite, false);
        Part(rt, "Turret2", new Vector2(0.74f, 0.36f), new Vector2(0.74f, 0.64f), BsUI.DotSprite, false);
        return img;
    }

    private static void Part(RectTransform parent, string name, Vector2 min, Vector2 max, Sprite sprite, bool sliced)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        if (!sliced)                                   // round turret: as wide as it is tall
        {
            rt.sizeDelta = new Vector2(14, 0);
        }
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        if (sliced) img.type = UnityEngine.UI.Image.Type.Sliced;
    }

    // ── Drawing ────────────────────────────────────────────────────────────────
    public void SetColor(int x, int y, Color c)
    {
        if (!Ships.Inside(x, y)) return;
        int i = Ships.Index(x, y);
        baseColors[i] = c;
        cells[i].color = c;
    }

    public void FillAll(Color c)
    {
        for (int i = 0; i < cells.Length; i++) { baseColors[i] = c; cells[i].color = c; }
    }

    // ── Creativity: animated ocean ─────────────────────────────────────────
    // A soft light wave rolls across the water, and the ships gently rock on it.
    private void Update()
    {
        if (cells == null) return;
        float t = Time.time;
        int n = Ships.GridSize;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = Ships.Index(x, y);
                float wave = 0.5f + 0.5f * Mathf.Sin(t * 1.7f - x * 0.65f - y * 0.45f);
                cells[i].color = Color.Lerp(baseColors[i], Color.white, 0.09f * wave * wave);
            }
        if (radarSweep != null) radarSweep.localEulerAngles = new Vector3(0, 0, -t * 100f);   // clockwise sweep
        if (aimLabel != null && aimLabel.gameObject.activeSelf) aimLabel.alpha = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(t * 5f));
        for (int k = 0; k < hullsUsed; k++)
            hulls[k].rectTransform.localEulerAngles = new Vector3(0, 0, hullAngle[k] + Mathf.Sin(t * 1.3f + k * 1.7f) * 1.4f);
    }

    public void SetMark(int x, int y, Mark mark)
    {
        if (!Ships.Inside(x, y)) return;
        int i = Ships.Index(x, y);
        dots[i].enabled    = mark == Mark.Miss;
        crosses[i].enabled = mark == Mark.Hit;
    }

    public void ClearMarks()
    {
        for (int i = 0; i < cells.Length; i++) dots[i].enabled = crosses[i].enabled = false;
    }

    public void SetAim(int x, int y, bool on)
    {
        if (!Ships.Inside(x, y)) return;
        aims[Ships.Index(x, y)].enabled = on;
        if (aimLabel == null) return;
        aimLabel.gameObject.SetActive(on);
        if (on)
        {
            aimLabel.text = "TARGET " + Ships.CellName(x, y);
            Vector2 p = CellCenter(x, y);
            aimLabel.rectTransform.anchoredPosition = new Vector2(p.x - 80f, -(p.y - cell * 0.5f - 28f));
        }
    }

    public void ClearAim()
    {
        foreach (var a in aims) a.enabled = false;
        if (aimLabel != null) aimLabel.gameObject.SetActive(false);
    }

    public void SetVisible(bool visible) => root.gameObject.SetActive(visible);

    // ── Input (called by GridCellInput) ────────────────────────────────────────
    internal void OnEnter(int x, int y) => CellHovered?.Invoke(x, y);
    internal void OnExit() => PointerLeft?.Invoke();
    internal void OnClick(int x, int y)
    {
        Debug.Log($"{GridName} {Ships.CellName(x, y)}");   // Step 2 test: e.g. "Enemy C7"
        CellClicked?.Invoke(x, y);
    }
}

/// <summary>Sends mouse events of one cell to its GridView (works with the old and the new Input System via the EventSystem).</summary>
public class GridCellInput : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private GridView grid;
    private int x, y;

    public void Setup(GridView g, int cx, int cy) { grid = g; x = cx; y = cy; }

    public void OnPointerEnter(PointerEventData e) => grid.OnEnter(x, y);
    public void OnPointerExit(PointerEventData e)  => grid.OnExit();
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left) grid.OnClick(x, y);
    }
}

/// <summary>Tiny helpers that build UI from code (everything is laid out on a 1920x1080 reference canvas, from the top-left).</summary>
public static class BsUI
{
    // ── THEMES ──────────────────────────────────────────────────────────────
    // All colours come from the chosen theme (BattleshipUI > Theme in the Inspector).
    public static BattleshipTheme Current { get; private set; }
    public static bool  Comic    => Current == BattleshipTheme.ComicBook;
    public static bool  Naval    => Current == BattleshipTheme.NavalCommand;
    public static bool  Tropical => Current == BattleshipTheme.TropicalIslands;
    public static float ShadowOffset;                 // solid drop shadow under panels / buttons (0 = none)
    public static Color ShadowColor, TitleColor, TitleOutline, Accent, BurstFill, BurstInk, SeaStrip;
    public static float LineWidth = 3f;          // outline thickness of panels and buttons

    public static Color Background, Panel, PanelLine, White, SoftText, DarkText, SmallText, Water, GridLine, Ship,
                        PreviewOk, PreviewBad, PreviewCellOk, PreviewCellBad, SunkEnemy, Revealed, HitColor, MissColor,
                        AimColor, Amber, Blue, LightBlue, ButtonFill, ButtonText, PillOff, CardWin, CardLose, Green, Red, Grey;

    static BsUI() { ApplyTheme(BattleshipTheme.NavalCommand); }

    public static void ApplyTheme(BattleshipTheme theme)
    {
        Current = theme;
        if (theme == BattleshipTheme.TropicalIslands)
        {
            // sunny tropical sea: turquoise water, sandy panels, wooden boats, coral buttons
            LineWidth  = 4f;
            Background = Hex("2BB7D6"); Panel = Hex("F6E3B4"); PanelLine = Hex("8B5A2B");
            White = Hex("FFFFFF"); SoftText = Hex("E6FBFF"); DarkText = Hex("4A2F0F"); SmallText = Hex("6B4A1F");
            Water = Hex("52D6EC"); GridLine = Hex("EFFFFF"); Ship = Hex("8B5A2B");
            PreviewOk = Hex("2ECC71"); PreviewBad = Hex("FF5252"); PreviewCellOk = Hex("A7F3D0"); PreviewCellBad = Hex("FFB3B3");
            SunkEnemy = Hex("FF6F59"); Revealed = Hex("A08A6A"); HitColor = Hex("E53935"); MissColor = Hex("FFFFFF");
            AimColor = Hex("FF4FA0"); Amber = Hex("FF4FA0");
            Blue = Hex("FF6F59"); LightBlue = Hex("FFF1C1"); ButtonFill = Hex("FFF8E7"); ButtonText = Hex("FFFFFF");
            PillOff = Hex("F3E7C7"); CardWin = Hex("E8FFF1"); CardLose = Hex("FFF1E6");
            Green = Hex("14A86B"); Red = Hex("E53935"); Grey = Hex("B8A27F");
            ShadowOffset = 6f; ShadowColor = new Color(0f, 0.3f, 0.36f, 0.35f);
            TitleColor = Hex("FFFFFF"); TitleOutline = Hex("00796B"); Accent = Hex("0277BD");
            BurstFill = Hex("FFD54F"); BurstInk = Hex("E64A19"); SeaStrip = Hex("1FA6C4");
        }
        else if (theme == BattleshipTheme.ComicBook)
        {
            // bright comic-book page: yellow paper, thick black ink, red / blue / yellow
            LineWidth  = 5f;
            Background = Hex("FFD93B"); Panel = Hex("FFFFFF"); PanelLine = Hex("111111");
            White = Hex("111111"); SoftText = Hex("3A2E00"); DarkText = Hex("111111"); SmallText = Hex("444444");
            Water = Hex("7FD3F7"); GridLine = Hex("111111"); Ship = Hex("1E5BFF");
            PreviewOk = Hex("2EBD4E"); PreviewBad = Hex("FF3B30"); PreviewCellOk = Hex("C8F7C5"); PreviewCellBad = Hex("FFC9C4");
            SunkEnemy = Hex("FF8A00"); Revealed = Hex("8E8E8E"); HitColor = Hex("E10600"); MissColor = Hex("111111");
            AimColor = Hex("111111"); Amber = Hex("E10600");
            Blue = Hex("E10600"); LightBlue = Hex("FFF3A6"); ButtonFill = Hex("FFFFFF"); ButtonText = Hex("FFFFFF");
            PillOff = Hex("F2F2F2"); CardWin = Hex("FFF7C2"); CardLose = Hex("E9E9E9");
            Green = Hex("1BA34A"); Red = Hex("E10600"); Grey = Hex("9E9E9E");
            ShadowOffset = 8f; ShadowColor = new Color(0f, 0f, 0f, 0.9f);
            TitleColor = Hex("E10600"); TitleOutline = Hex("111111"); Accent = Hex("1E5BFF");
            BurstFill = Hex("FFE600"); BurstInk = Hex("E10600"); SeaStrip = Hex("7FD3F7");
        }
        else
        {
            // Naval Command: dark command room, glowing green radar, amber targeting
            LineWidth  = 3f;
            Background = Hex("040B08"); Panel = Hex("0A1913"); PanelLine = Hex("2E9E5B");
            White = Hex("C9FFD9"); SoftText = Hex("6FAF86"); DarkText = Hex("B8F5C9"); SmallText = Hex("8FC7A2");
            Water = Hex("0E241A"); GridLine = Hex("1C5C38"); Ship = Hex("8C9AA3");
            PreviewOk = Hex("3DDC77"); PreviewBad = Hex("FF5252"); PreviewCellOk = Hex("17402A"); PreviewCellBad = Hex("4A1A1A");
            SunkEnemy = Hex("FF7043"); Revealed = Hex("5A6B73"); HitColor = Hex("FF3B30"); MissColor = Hex("9FE8B8");
            AimColor = Hex("FFC233"); Amber = Hex("FFC233");
            Blue = Hex("1F9D55"); LightBlue = Hex("103A26"); ButtonFill = Hex("0F2A1D"); ButtonText = Hex("EAFFF0");
            PillOff = Hex("0C1712"); CardWin = Hex("0D2A1A"); CardLose = Hex("1C1212");
            Green = Hex("4CFF8A"); Red = Hex("FF5A4F"); Grey = Hex("3E5247");
            ShadowOffset = 0f; ShadowColor = Color.clear;
            TitleColor = White; TitleOutline = Background; Accent = Amber;
            BurstFill = Amber; BurstInk = Background; SeaStrip = Hex("0E241A");
        }
    }

    public static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }

    // ── generated sprites (no art files needed) ────────────────────────────────
    private static Sprite rounded, dot, cross, frame, hull, ring, sweep;
    private static Sprite radarRing;
    public static Sprite RadarRingSprite => radarRing != null ? radarRing : (radarRing = MakeRing(256, 3f));
    public static Sprite SweepSprite => sweep != null ? sweep : (sweep = MakeSweep(256));
    public static Sprite HullSprite => hull != null ? hull : (hull = MakeHull(96, 42));
    public static Sprite RingSprite => ring != null ? ring : (ring = MakeRing(64, 5f));
    public static Sprite RoundedSprite => rounded != null ? rounded : (rounded = MakeRounded(64, 14, 0));
    public static Sprite FrameSprite   => frame   != null ? frame   : (frame   = MakeRounded(64, 10, 7));
    public static Sprite DotSprite     => dot     != null ? dot     : (dot     = MakeDot(48));
    public static Sprite CrossSprite   => cross   != null ? cross   : (cross   = MakeCross(64));

    private static Sprite MakeRounded(int n, float radius, float ring)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float cx = Mathf.Clamp(px, radius, n - radius), cy = Mathf.Clamp(py, radius, n - radius);
                float d = radius - Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));   // >0 inside
                float a = Mathf.Clamp01(d + 0.5f);
                if (ring > 0f) a *= Mathf.Clamp01(ring - d + 0.5f);                               // hollow frame
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        t.Apply();
        float b = radius + 2f;
        return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
    }

    /// <summary>A ship hull seen from above: round stern on the left, pointed bow on the right.</summary>
    private static Sprite MakeHull(int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        float half = h * 0.5f, stern = 12f, bow = 34f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float px = x + 0.5f, py = y + 0.5f, dy = Mathf.Abs(py - half);
                float limit;                                                  // half height of the hull at this x
                if (px < stern)
                {
                    float dx = stern - px;                                    // rounded stern corners
                    limit = (half - stern) + Mathf.Sqrt(Mathf.Max(0f, stern * stern - dx * dx));
                }
                else if (px > w - bow)
                {
                    float k = (px - (w - bow)) / bow;                         // 0 -> 1 towards the tip
                    limit = Mathf.Lerp(half, 2.5f, Mathf.Pow(k, 1.7f));
                }
                else limit = half;
                float a = Mathf.Clamp01(limit - dy + 0.5f);
                if (px > w - 1f) a = 0f;
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(stern + 2f, 0, bow + 2f, 0));
    }

    /// <summary>Radar sweep: a bright leading line with a fading green trail behind it.</summary>
    private static Sprite MakeSweep(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        float r = n * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > r) { t.SetPixel(x, y, new Color(1, 1, 1, 0)); continue; }
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;              // 0 = leading edge, trail goes counter-clockwise
                if (ang < 0f) ang += 360f;
                float trail = ang <= 80f ? Mathf.Pow(1f - ang / 80f, 2f) * 0.38f : 0f;
                float edge = ang < 1.6f || ang > 359.4f ? 0.85f : 0f;
                t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Max(trail, edge)));
            }
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    /// <summary>HUD corner brackets around a rectangle.</summary>
    public static void Brackets(RectTransform parent, Vector2 pos, Vector2 size, Color color, float len = 30f, float thick = 4f)
    {
        float x2 = pos.x + size.x, y2 = pos.y + size.y;
        Bar(parent, pos, new Vector2(len, thick), color);                              Bar(parent, pos, new Vector2(thick, len), color);
        Bar(parent, new Vector2(x2 - len, pos.y), new Vector2(len, thick), color);     Bar(parent, new Vector2(x2 - thick, pos.y), new Vector2(thick, len), color);
        Bar(parent, new Vector2(pos.x, y2 - thick), new Vector2(len, thick), color);   Bar(parent, new Vector2(pos.x, y2 - len), new Vector2(thick, len), color);
        Bar(parent, new Vector2(x2 - len, y2 - thick), new Vector2(len, thick), color); Bar(parent, new Vector2(x2 - thick, y2 - len), new Vector2(thick, len), color);
    }

    private static void Bar(RectTransform parent, Vector2 pos, Vector2 size, Color color)
    {
        BsUI.Image(parent, "Bracket", pos, size, color).raycastTarget = false;
    }

    private static Sprite burst;
    public static Sprite BurstSprite => burst != null ? burst : (burst = MakeBurst(256, 14));

    /// <summary>Comic "explosion bubble": a spiky star shape.</summary>
    private static Sprite MakeBurst(int n, int spikes)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        float c = n * 0.5f, R = n * 0.5f - 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                float k = Mathf.Abs(((ang / (2f * Mathf.PI) * spikes) % 1f + 1f) % 1f - 0.5f) * 2f;   // 0 at spike tip, 1 between
                float r = R * Mathf.Lerp(1f, 0.68f, k);
                t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d + 0.5f)));
            }
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    private static Sprite MakeRing(int n, float thickness)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = n * 0.5f - 1f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f));
                float a = Mathf.Clamp01(r - d + 0.5f) * Mathf.Clamp01(d - (r - thickness) + 0.5f);
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    private static Sprite MakeDot(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = n * 0.5f - 1f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f));
                t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d + 0.5f)));
            }
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    private static Sprite MakeCross(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float w = n * 0.09f, m = n * 0.12f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                bool inBox = px > m && px < n - m && py > m && py < n - m;
                float d1 = Mathf.Abs(px - py) / 1.4142f, d2 = Mathf.Abs(px + py - n) / 1.4142f;
                float a = inBox ? Mathf.Clamp01(w - Mathf.Min(d1, d2) + 0.5f) : 0f;
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    // ── building blocks (pos = top-left corner in reference pixels, y going down) ──
    public static RectTransform Rect(RectTransform parent, string name, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(pos.x, -pos.y);
        rt.sizeDelta = size;
        return rt;
    }

    public static Image Image(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color, bool rounded = false)
    {
        var rt = Rect(parent, name, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        if (rounded) { img.sprite = RoundedSprite; img.type = UnityEngine.UI.Image.Type.Sliced; }
        return img;
    }

    /// <summary>A centred child image (e.g. the hit X inside a cell), sized as a fraction of the parent.</summary>
    public static Image Child(RectTransform parent, string name, float scale, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = parent.sizeDelta * scale;
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        if (sprite == FrameSprite) img.type = UnityEngine.UI.Image.Type.Sliced;
        return img;
    }

    public static TextMeshProUGUI Text(RectTransform parent, string name, string text, Vector2 pos, Vector2 size, float fontSize, Color color,
                                       TextAlignmentOptions align = TextAlignmentOptions.Left, bool bold = false)
    {
        var rt = Rect(parent, name, pos, size);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = align;
        t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        if (bold)
        {
            if (Comic) { t.fontStyle = FontStyles.Bold | FontStyles.Italic; t.characterSpacing = 1f; }   // comic lettering
            else if (Tropical) t.characterSpacing = 1.5f;                                               // friendly rounded look
            else t.characterSpacing = 3f;                                                               // military stencil feel
        }
        t.raycastTarget = false;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    /// <summary>A rounded panel with a dark outline (outline = slightly bigger image behind it).</summary>
    public static Image Box(RectTransform parent, string name, Vector2 pos, Vector2 size, Color fill, Color outline, float line = 3f)
    {
        line = Mathf.Max(line, LineWidth);
        if (ShadowOffset > 0f) Image(parent, name + " Shadow", pos + Vector2.one * ShadowOffset, size, ShadowColor, true).raycastTarget = false;
        var border = Image(parent, name, pos, size, outline, true);
        var inner = Image(border.rectTransform, "Fill", new Vector2(line, line), size - new Vector2(line * 2, line * 2), fill, true);
        inner.raycastTarget = false;
        border.raycastTarget = false;
        return inner;
    }

    public static Button Button(RectTransform parent, string name, string label, Vector2 pos, Vector2 size, Color fill, Color textColor,
                                Color outline, float fontSize, Action onClick, out TextMeshProUGUI labelText)
    {
        if (ShadowOffset > 0f) Image(parent, name + " Shadow", pos + Vector2.one * (ShadowOffset * 0.75f), size, ShadowColor, true).raycastTarget = false;
        var border = Image(parent, name, pos, size, outline, true);
        float w = LineWidth;
        var inner = Image(border.rectTransform, "Fill", new Vector2(w, w), size - new Vector2(w * 2, w * 2), fill, true);
        inner.raycastTarget = false;
        var btn = border.gameObject.AddComponent<UnityEngine.UI.Button>();
        btn.targetGraphic = border;
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.85f, 0.9f, 1f);
        colors.pressedColor = new Color(0.7f, 0.75f, 0.85f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
        btn.colors = colors;
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        labelText = Text(border.rectTransform, "Label", label, Vector2.zero, size, fontSize, textColor, TextAlignmentOptions.Center, true);
        return btn;
    }
}
