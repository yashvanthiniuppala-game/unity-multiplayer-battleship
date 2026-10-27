// MenuStyler.cs — makes the LOGIN and LOBBY screens match the game's theme, automatically.
// It does NOT change LoginController / LobbyController or any protected script: when the scene starts,
// it finds your existing UI objects (by type and name) and restyles them:
//   * themed background (Comic: yellow sunburst + halftone dots / Naval: moving green waves + scanlines),
//   * panels with thick outlines + comic drop shadow, popping in when the screen opens,
//   * a big wobbling title, bouncy buttons (grow on hover, squash on click), styled input boxes,
//   * a "2 PLAYER!" comic burst on the panel corner, and little ships sailing across the bottom of the screen,
//   * the lobby code pops when it appears.
//
// HOW TO USE: in LoginScene AND in LobbyScene: GameObject > Create Empty, name it "MenuStyler",
// Add Component > Menu Styler. Pick the Theme (same as BattleshipUI). Optional: type a Title Text. Press Play.
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MenuStyler : MonoBehaviour
{
    [SerializeField] private BattleshipTheme theme = BattleshipTheme.ComicBook;
    [Tooltip("Optional: replaces the title text (e.g. SEA BATTLE). Leave empty to keep your own title.")]
    [SerializeField] private string titleText = "";
    [Tooltip("Text in the comic burst on the corner of the panel (empty = no burst)")]
    [SerializeField] private string cornerBurstText = "2 PLAYER!";
    [SerializeField] private bool sailingShips = true;

    private void Start()
    {
        BsUI.ApplyTheme(theme);
        var canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) { Debug.LogWarning("[MenuStyler] No Canvas found in this scene."); return; }
        var root = (RectTransform)canvas.transform;

        var cam = Camera.main;
        if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = BsUI.Background; }

        // ── background ──
        int insertAt = 0;
        foreach (var img in root.GetComponentsInChildren<Image>(true))
            if (img.gameObject.name == "Background")
            {
                img.color = BsUI.Background;
                insertAt = img.transform.GetSiblingIndex() + 1;
                break;
            }
        Transform anim = BsUI.Comic ? ComicBackground.Create(root).transform
                       : BsUI.Tropical ? TropicalBackground.Create(root).transform
                       : OceanBackground.Create(root).transform;
        anim.SetSiblingIndex(insertAt);
        if (sailingShips) SailingShips.Create(root).transform.SetSiblingIndex(insertAt + 1);

        // ── panels, dividers ──
        var panels = new List<RectTransform>();
        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            string n = img.gameObject.name;
            if (n.EndsWith("Panel")) { StylePanel(img); panels.Add(img.rectTransform); }
            else if (n.StartsWith("Divider")) { img.color = BsUI.PanelLine; var rt = img.rectTransform; rt.sizeDelta = new Vector2(rt.sizeDelta.x, 4f); }
        }

        // ── buttons, input fields, texts ──
        foreach (var b in root.GetComponentsInChildren<Button>(true)) StyleButton(b);
        foreach (var f in root.GetComponentsInChildren<TMP_InputField>(true)) StyleInput(f);
        foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (t.GetComponentInParent<Button>() != null || t.GetComponentInParent<TMP_InputField>() != null) continue;
            string n = t.gameObject.name;
            if (n.Contains("Subtitle")) StyleSubtitle(t);
            else if (n.Contains("Title") || n == "TileText") StyleTitle(t);
            else if (n.Contains("Status")) t.fontStyle |= FontStyles.Bold;                 // its colour is set by the controller
            else if (n == "LobbyCodeDisplay") { t.fontStyle = FontStyles.Bold; t.color = BsUI.Accent; t.gameObject.AddComponent<UiPopOnChange>(); CopyCodeButton.Attach(t); }
            else { t.color = BsUI.DarkText; if (!BsUI.Naval) t.fontStyle |= FontStyles.Bold; }
        }

        // ── comic burst on the first panel's corner ──
        if (panels.Count > 0 && !string.IsNullOrEmpty(cornerBurstText)) CornerBurst(panels[0]);
    }

    // =====================================================================
    private static void StylePanel(Image img)
    {
        img.color = BsUI.Panel;
        var outline = img.gameObject.AddComponent<Outline>();
        outline.effectColor = BsUI.PanelLine;
        outline.effectDistance = new Vector2(BsUI.LineWidth, -BsUI.LineWidth);
        if (BsUI.ShadowOffset > 0f)
        {
            var shadow = img.gameObject.AddComponent<Shadow>();
            shadow.effectColor = BsUI.ShadowColor;
            shadow.effectDistance = new Vector2(BsUI.ShadowOffset * 2f, -BsUI.ShadowOffset * 2f);
        }
        if (BsUI.Comic) img.rectTransform.localEulerAngles = new Vector3(0, 0, -1.2f);       // comic panels are a bit crooked
        img.gameObject.AddComponent<UiPopIn>();
    }

    private static void StyleButton(Button b)
    {
        var img = b.GetComponent<Image>();
        bool join = b.gameObject.name.Contains("Join");
        if (img != null)
        {
            img.color = join ? BsUI.Green : BsUI.Blue;
            var outline = b.gameObject.AddComponent<Outline>();
            outline.effectColor = BsUI.PanelLine;
            outline.effectDistance = new Vector2(BsUI.LineWidth * 0.8f, -BsUI.LineWidth * 0.8f);
            if (BsUI.ShadowOffset > 0f)
            {
                var shadow = b.gameObject.AddComponent<Shadow>();
                shadow.effectColor = BsUI.ShadowColor;
                shadow.effectDistance = new Vector2(BsUI.ShadowOffset, -BsUI.ShadowOffset);
            }
        }
        var text = b.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
        {
            text.color = !BsUI.Naval ? Color.white : (join ? BsUI.Background : BsUI.ButtonText);
            text.fontStyle = BsUI.Comic ? FontStyles.Bold | FontStyles.Italic : BsUI.Tropical ? FontStyles.Bold : FontStyles.Bold | FontStyles.UpperCase;
            text.characterSpacing = BsUI.Naval ? 4f : 1f;
        }
        b.gameObject.AddComponent<UiBounce>();
    }

    private static void StyleInput(TMP_InputField f)
    {
        var img = f.GetComponent<Image>();
        if (img != null)
        {
            img.color = !BsUI.Naval ? Color.white : BsUI.ButtonFill;
            var outline = f.gameObject.AddComponent<Outline>();
            outline.effectColor = BsUI.PanelLine;
            outline.effectDistance = new Vector2(3, -3);
        }
        if (f.textComponent != null) { f.textComponent.color = BsUI.DarkText; f.textComponent.fontStyle = FontStyles.Bold; }
        if (f.placeholder is TMP_Text ph) ph.color = new Color(BsUI.SoftText.r, BsUI.SoftText.g, BsUI.SoftText.b, 0.7f);
    }

    private void StyleTitle(TextMeshProUGUI t)
    {
        if (!string.IsNullOrEmpty(titleText)) t.text = titleText;
        t.enableAutoSizing = false;
        t.fontSize = Mathf.Max(t.fontSize, 46f);
        if (BsUI.Tropical)
        {
            t.color = BsUI.TitleColor;
            t.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            t.characterSpacing = 2f;
            t.outlineWidth = 0.25f;                                   // teal outline so the white title pops
            t.outlineColor = (Color32)BsUI.TitleOutline;
        }
        else if (BsUI.Comic)
        {
            t.color = BsUI.Hex("E10600");
            t.fontStyle = FontStyles.Bold | FontStyles.Italic | FontStyles.UpperCase;
            t.outlineWidth = 0.22f;                                   // black ink around the letters
            t.outlineColor = new Color32(17, 17, 17, 255);
        }
        else
        {
            t.color = BsUI.White;
            t.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            t.characterSpacing = 8f;
        }
        t.gameObject.AddComponent<UiWobble>();
    }

    private static void StyleSubtitle(TextMeshProUGUI t)
    {
        t.color = BsUI.Naval ? BsUI.SoftText : BsUI.Accent;
        t.fontStyle = FontStyles.Bold | FontStyles.Italic;
        t.fontSize = Mathf.Max(t.fontSize, 22f);
    }

    private void CornerBurst(RectTransform panel)
    {
        var go = new GameObject("Corner Burst", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(panel, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-10f, -6f);
        rt.sizeDelta = new Vector2(190, 130);
        rt.localEulerAngles = new Vector3(0, 0, 14f);
        var img = go.AddComponent<Image>();
        img.sprite = BsUI.BurstSprite;
        img.color = BsUI.BurstFill;
        img.raycastTarget = false;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = BsUI.PanelLine;
        outline.effectDistance = new Vector2(3, -3);

        var text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        text.rectTransform.SetParent(rt, false);
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        text.text = cornerBurstText;
        text.color = BsUI.BurstInk;
        text.fontStyle = FontStyles.Bold | FontStyles.Italic;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true; text.fontSizeMin = 10; text.fontSizeMax = 30;
        text.margin = new Vector4(30, 28, 30, 28);
        text.raycastTarget = false;
        go.AddComponent<UiPulse>();
    }
}

// =========================================================================
// Small animation helpers (each is added by MenuStyler)
// =========================================================================

/// <summary>Panel pops in when the screen opens.</summary>
public class UiPopIn : MonoBehaviour
{
    private float t;
    private void OnEnable() { t = 0f; transform.localScale = Vector3.one * 0.6f; }
    private void Update()
    {
        if (t > 0.5f) return;
        t += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(t / 0.45f) - 1f;
        float back = 1f + 2.70158f * k * k * k + 1.70158f * k * k;        // "ease out back": overshoots a little, then settles
        transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, back);
    }
}

/// <summary>Button grows a little on hover and squashes when pressed.</summary>
public class UiBounce : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private float target = 1f, current = 1f;
    public void OnPointerEnter(PointerEventData e) => target = 1.06f;
    public void OnPointerExit(PointerEventData e)  => target = 1f;
    public void OnPointerDown(PointerEventData e)  => target = 0.93f;
    public void OnPointerUp(PointerEventData e)    => target = 1.06f;
    private void Update()
    {
        current = Mathf.Lerp(current, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
        transform.localScale = Vector3.one * current;
    }
}

/// <summary>Title gently rocks and breathes.</summary>
public class UiWobble : MonoBehaviour
{
    private void Update()
    {
        float t = Time.unscaledTime;
        transform.localEulerAngles = new Vector3(0, 0, BsUI.Comic ? Mathf.Sin(t * 2f) * 3f : BsUI.Tropical ? Mathf.Sin(t * 1.4f) * 2f : 0f);
        float s = 1f + Mathf.Sin(t * 3f) * 0.03f;
        transform.localScale = new Vector3(s, s, 1f);
    }
}

/// <summary>Corner burst pulses.</summary>
public class UiPulse : MonoBehaviour
{
    private void Update()
    {
        float s = 1f + Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.6f)) * 0.12f;
        transform.localScale = new Vector3(s, s, 1f);
    }
}

/// <summary>When the text changes (e.g. the lobby code appears), it pops.</summary>
public class UiPopOnChange : MonoBehaviour
{
    private TMP_Text text;
    private string last = "";
    private float t = 10f;
    private void Awake() { text = GetComponent<TMP_Text>(); }
    private void Update()
    {
        if (text == null) return;
        if (text.text != last) { last = text.text; if (!string.IsNullOrEmpty(last)) t = 0f; }
        t += Time.unscaledDeltaTime;
        float s = t < 0.12f ? Mathf.Lerp(1f, 1.4f, t / 0.12f) : t < 0.35f ? Mathf.Lerp(1.4f, 1f, (t - 0.12f) / 0.23f) : 1f;
        transform.localScale = new Vector3(s, s, 1f);
    }
}

/// <summary>
/// CREATIVITY: a small copy icon that appears next to the lobby code as soon as a code is shown.
/// It copies only the code (e.g. "K7QX2P") to the clipboard, so player 2 can paste it with Ctrl+V.
/// Added from MenuStyler, so LobbyController does not need to change.
/// </summary>
public class CopyCodeButton : MonoBehaviour
{
    private TMP_Text codeText;
    private RectTransform buttonRT;
    private Image icon;
    private string code = "";
    private float copiedTimer;
    private static Sprite copySprite, checkSprite;

    public static void Attach(TMP_Text codeText)
    {
        var c = codeText.gameObject.AddComponent<CopyCodeButton>();
        c.codeText = codeText;
        c.Build();
    }

    private void Build()
    {
        if (copySprite == null) copySprite = MakeIcon(false);
        if (checkSprite == null) checkSprite = MakeIcon(true);

        var go = new GameObject("Copy Code Icon", typeof(RectTransform));
        buttonRT = (RectTransform)go.transform;
        buttonRT.SetParent(codeText.rectTransform, false);
        buttonRT.anchorMin = buttonRT.anchorMax = codeText.rectTransform.pivot;   // placed next to the text in Update
        buttonRT.pivot = new Vector2(0f, 0.5f);

        icon = go.AddComponent<Image>();
        icon.sprite = copySprite;
        icon.color = codeText.color;              // same colour as the code itself
        icon.preserveAspect = true;

        var button = go.AddComponent<Button>();
        button.targetGraphic = icon;
        button.onClick.AddListener(Copy);
        go.AddComponent<UiBounce>();              // grows a little on hover
        go.SetActive(false);
    }

    private void Update()
    {
        code = ExtractCode(codeText.text);
        bool show = code.Length > 0;
        if (buttonRT.gameObject.activeSelf != show) buttonRT.gameObject.SetActive(show);
        if (!show) return;

        // same height as the code text, sitting right after the last letter on the same line
        var b = codeText.textBounds;
        float h = b.size.y > 1f ? b.size.y * 1.25f : codeText.fontSize * 1.2f;
        buttonRT.sizeDelta = new Vector2(h, h);
        buttonRT.anchoredPosition = new Vector2(b.max.x + h * 0.35f, b.center.y);

        if (copiedTimer > 0f)
        {
            copiedTimer -= Time.unscaledDeltaTime;
            if (copiedTimer <= 0f) { icon.sprite = copySprite; icon.color = codeText.color; }
        }
    }

    private void Copy()
    {
        if (code.Length == 0) return;
        GUIUtility.systemCopyBuffer = code;
        icon.sprite = checkSprite;                     // feedback: icon becomes a green tick
        icon.color = new Color(0.15f, 0.65f, 0.25f);
        copiedTimer = 1.5f;
        Debug.Log("[Lobby] Copied lobby code " + code + " to the clipboard.");
    }

    /// <summary>Draws the copy icon (two overlapping rounded squares) or a tick, white on transparent.</summary>
    private static Sprite MakeIcon(bool tick)
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f, a;
                if (tick)
                {
                    float d1 = SegDist(fx, fy, 12f, 32f, 26f, 16f);
                    float d2 = SegDist(fx, fy, 26f, 16f, 52f, 48f);
                    a = Mathf.Clamp01(5.5f - Mathf.Min(d1, d2));
                }
                else
                {
                    // front square (bottom-left) and back square (top-right); the back one is hidden behind the front
                    float front = BoxRing(fx, fy, 8f, 6f, 40f, 38f);
                    bool insideFront = fx > 8f && fx < 40f && fy > 6f && fy < 38f;
                    float back = insideFront ? 0f : BoxRing(fx, fy, 22f, 22f, 56f, 56f);
                    a = Mathf.Max(front, back);
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    /// <summary>Anti-aliased outline (5 px thick, rounded corners) of the box x0..x1, y0..y1.</summary>
    private static float BoxRing(float x, float y, float x0, float y0, float x1, float y1)
    {
        const float r = 6f, thick = 5f;
        float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
        float hx = (x1 - x0) * 0.5f - r, hy = (y1 - y0) * 0.5f - r;
        float qx = Mathf.Max(Mathf.Abs(x - cx) - hx, 0f), qy = Mathf.Max(Mathf.Abs(y - cy) - hy, 0f);
        float dist = Mathf.Sqrt(qx * qx + qy * qy) - r;            // signed distance to the rounded box edge
        return Mathf.Clamp01(thick * 0.5f + 0.5f - Mathf.Abs(dist + thick * 0.5f));
    }

    private static float SegDist(float px, float py, float ax, float ay, float bx, float by)
    {
        float vx = bx - ax, vy = by - ay;
        float t = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy));
        float dx = px - (ax + vx * t), dy = py - (ay + vy * t);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>"Lobby Code: K7QX2P" -> "K7QX2P". Takes the last word that looks like a code.</summary>
    private static string ExtractCode(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // strip TMP rich-text tags like <b> or <color=...>
        var sb = new System.Text.StringBuilder();
        bool inTag = false;
        foreach (char ch in s)
        {
            if (ch == '<') inTag = true;
            else if (ch == '>') inTag = false;
            else if (!inTag) sb.Append(ch);
        }
        string[] words = sb.ToString().Split(new[] { ' ', ':', '\n', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = words.Length - 1; i >= 0; i--)
        {
            string w = words[i].Trim();
            if (w.Length < 4 || w.Length > 12) continue;
            bool ok = true, hasUpperOrDigit = false;
            foreach (char ch in w)
            {
                if (!char.IsLetterOrDigit(ch)) { ok = false; break; }
                if (char.IsLower(ch)) { ok = false; break; }   // lobby codes are UPPERCASE letters/digits
                hasUpperOrDigit = true;
            }
            if (ok && hasUpperOrDigit) return w;
        }
        return "";
    }
}

/// <summary>A strip of sea along the bottom of the screen with little ships sailing across.</summary>
public class SailingShips : MonoBehaviour
{
    private class Boat { public RectTransform rt; public float speed, x, y, phase; }
    private readonly List<Boat> boats = new List<Boat>();

    public static SailingShips Create(RectTransform parent)
    {
        var go = new GameObject("Sailing Ships", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0, 150);
        var sea = go.AddComponent<Image>();
        sea.color = BsUI.SeaStrip;
        sea.raycastTarget = false;
        var line = new GameObject("Shore Line", typeof(RectTransform)).AddComponent<Image>();
        line.rectTransform.SetParent(rt, false);
        line.rectTransform.anchorMin = new Vector2(0, 1); line.rectTransform.anchorMax = new Vector2(1, 1);
        line.rectTransform.pivot = new Vector2(0.5f, 1f);
        line.rectTransform.sizeDelta = new Vector2(0, BsUI.LineWidth);
        line.color = BsUI.PanelLine;
        line.raycastTarget = false;

        var ships = go.AddComponent<SailingShips>();
        float[] sizes = { 1f, 0.7f, 0.85f };
        float[] speeds = { 60f, 95f, 45f };
        float[] starts = { 200f, 900f, 1500f };
        float[] heights = { 70f, 30f, 105f };
        for (int i = 0; i < 3; i++)
        {
            var hull = new GameObject("Boat", typeof(RectTransform)).AddComponent<Image>();
            hull.sprite = BsUI.HullSprite;
            hull.type = Image.Type.Sliced;
            hull.color = BsUI.Ship;
            hull.raycastTarget = false;
            var hrt = hull.rectTransform;
            hrt.SetParent(rt, false);
            hrt.anchorMin = hrt.anchorMax = new Vector2(0, 0);
            hrt.pivot = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = new Vector2(200f * sizes[i], 42f * sizes[i]);
            hrt.anchoredPosition = new Vector2(starts[i], heights[i]);
            if (!BsUI.Naval) { var o = hull.gameObject.AddComponent<Outline>(); o.effectColor = BsUI.PanelLine; o.effectDistance = new Vector2(2, -2); }
            ships.boats.Add(new Boat { rt = hrt, speed = speeds[i], x = starts[i], y = heights[i], phase = i * 1.7f });
        }
        return ships;
    }

    private void Update()
    {
        float t = Time.unscaledTime;
        float width = ((RectTransform)transform).rect.width + 400f;
        foreach (var b in boats)
        {
            b.x += b.speed * Time.unscaledDeltaTime;
            if (b.x > width) b.x = -200f;
            b.rt.anchoredPosition = new Vector2(b.x, b.y + Mathf.Sin(t * 2f + b.phase) * 4f);
            b.rt.localEulerAngles = new Vector3(0, 0, Mathf.Sin(t * 1.6f + b.phase) * 3f);
        }
    }
}
