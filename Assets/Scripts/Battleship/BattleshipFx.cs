// BattleshipFx.cs — CREATIVITY: explosions, splashes, screen shake and sound effects.
//   * Hit   -> cannon shot, then an explosion: white flash, fireball, flying sparks and smoke, a small screen shake.
//   * Miss  -> cannon shot, then a water splash: two expanding rings and water droplets.
//   * Sunk  -> a ship horn and a chain of explosions along the whole ship, a big screen shake.
//   * Game over -> victory or defeat jingle. Placing / rotating ships and Ready have small click sounds.
// All sounds are GENERATED IN CODE (no audio files needed). All effects are UI images, so they work on both computers.
// You don't add this to anything yourself: BattleshipUI creates it.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BattleshipFx : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.8f;

    private RectTransform layer, shakeTarget;
    private AudioSource audioSource;
    private AudioClip cannon, incoming, explosion, splash, horn, victory, defeat, clunk, tick, blip, pop;

    private class Anim { public float delay, t, dur; public Action<float> step; public Action end; }
    private readonly List<Anim> anims = new List<Anim>();
    private float shakeAmp, shakeLeft, shakeDur;

    /// <summary>layer = where effect images are drawn; shakeTarget = the part of the screen that shakes.</summary>
    public static BattleshipFx Create(RectTransform layer, RectTransform shakeTarget)
    {
        var fx = layer.gameObject.AddComponent<BattleshipFx>();
        fx.layer = layer;
        fx.shakeTarget = shakeTarget;
        fx.audioSource = layer.gameObject.AddComponent<AudioSource>();
        fx.audioSource.playOnAwake = false;
        fx.audioSource.spatialBlend = 0f;
        fx.MakeSounds();
        return fx;
    }

    // =====================================================================
    // Public effects
    // =====================================================================
    public void Clunk()  => Play(clunk, 0.6f);
    public void Tick()   => Play(tick, 0.5f);
    public void Blip()   => Play(blip, 0.6f);
    public void Pop()    => Play(pop, 0.6f);
    public void Fire()   => Play(cannon, 0.9f);
    public void Incoming() => Play(incoming, 0.7f);   // falling-bomb whistle before the enemy's shot lands
    public void Victory() => Play(victory, 0.8f);
    public void Defeat()  => Play(defeat, 0.8f);

    /// <summary>Hit: explosion at a cell centre (reference pixels).</summary>
    public void Hit(Vector2 at, float delay = 0f, float size = 1f)
    {
        After(delay, () =>
        {
            Play(explosion, 0.9f);
            Shake(7f * size, 0.3f);
            Burst(at, size);
            if (BsUI.Comic) ComicWord(at, Pick("KA-BOOM!", "BLAM!", "POW!", "KRAKA-BOOM!"), BsUI.Hex("FFE600"), BsUI.Hex("E10600"));
        });
    }

    /// <summary>Miss: water splash at a cell centre.</summary>
    public void Miss(Vector2 at, float delay = 0f)
    {
        After(delay, () =>
        {
            Play(splash, 0.8f);
            if (BsUI.Comic) ComicWord(at, Pick("SPLASH!", "SPLOOSH!", "PLOP!", "MISS!"), BsUI.Hex("FFFFFF"), BsUI.Hex("1E5BFF"));
            Ring(at, 0f, 0.65f, 18f, 120f, new Color(0.85f, 0.95f, 1f, 0.95f));
            Ring(at, 0.12f, 0.6f, 10f, 80f, new Color(0.55f, 0.8f, 1f, 0.9f));
            var foam = Spawn(BsUI.DotSprite, Color.white, at, 26f);
            Animate(0f, 0.3f, k => { Size(foam, Mathf.Lerp(26f, 60f, k)); Alpha(foam, 1f - k); }, () => Kill(foam));
            for (int i = 0; i < 9; i++)
            {
                var drop = Spawn(BsUI.DotSprite, new Color(0.7f, 0.88f, 1f, 1f), at, UnityEngine.Random.Range(5f, 9f));
                var v = new Vector2(UnityEngine.Random.Range(-90f, 90f), -UnityEngine.Random.Range(160f, 330f));
                Projectile(drop, at, v, 750f, 0.65f);
            }
        });
    }

    /// <summary>A ship was sunk: horn + a chain of explosions along the ship + a flash over the hull.</summary>
    public void Sunk(GridView grid, ShipPlacement ship, float delay = 0f)
    {
        After(delay, () =>
        {
            Play(horn, 0.7f);
            Shake(14f, 0.6f);
            if (BsUI.Comic) ComicWord(grid.ShipCenter(ship) + new Vector2(0, -50), "SUNK!!", BsUI.Hex("FF8A00"), BsUI.Hex("111111"), 1.5f);
            for (int c = 0; c < ship.Length; c++)
            {
                var cell = ship.Cell(c);
                Vector2 p = grid.CellCenter(cell.x, cell.y);
                After(0.13f * c, () => { Burst(p, 1.25f); Play(explosion, 0.5f); });
            }
            // white flash over the whole ship, three times
            Vector2 centre = grid.ShipCenter(ship);
            var flash = Spawn(BsUI.RoundedSprite, new Color(1f, 1f, 1f, 0f), centre, 10f);
            flash.type = Image.Type.Sliced;
            float len = ship.Length * grid.CellSize, thick = grid.CellSize;
            flash.rectTransform.sizeDelta = ship.vertical ? new Vector2(thick, len) : new Vector2(len, thick);
            Animate(0f, 0.9f, k => Alpha(flash, 0.75f * Mathf.Abs(Mathf.Sin(k * Mathf.PI * 3f))), () => Kill(flash));
        });
    }

    // =====================================================================
    // THEME Comic Book: pop-up comic words in a spiky bubble
    // =====================================================================
    private static string Pick(params string[] words) => words[UnityEngine.Random.Range(0, words.Length)];

    private void ComicWord(Vector2 at, string word, Color bubble, Color ink, float size = 1f)
    {
        Vector2 pos = at + new Vector2(UnityEngine.Random.Range(-20f, 20f), -60f);
        float tilt = UnityEngine.Random.Range(-14f, 14f);

        var shadow = Spawn(BsUI.BurstSprite, new Color(0, 0, 0, 0.9f), pos + new Vector2(6, 6), 10f);
        var burst  = Spawn(BsUI.BurstSprite, bubble, pos, 10f);
        var text = new GameObject("Word", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        text.rectTransform.SetParent(burst.rectTransform, false);
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        text.text = word;
        text.color = ink;
        text.fontStyle = FontStyles.Bold | FontStyles.Italic;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true; text.fontSizeMin = 10; text.fontSizeMax = 60;
        text.raycastTarget = false;
        text.margin = new Vector4(30, 30, 30, 30);

        float full = 190f * size;
        Animate(0f, 1.1f, k =>
        {
            float pop = k < 0.15f ? Mathf.Lerp(0.2f, 1.15f, k / 0.15f) : k < 0.25f ? Mathf.Lerp(1.15f, 1f, (k - 0.15f) / 0.1f) : 1f;
            float s = full * pop;
            burst.rectTransform.sizeDelta = new Vector2(s, s * 0.72f);
            shadow.rectTransform.sizeDelta = new Vector2(s, s * 0.72f);
            burst.rectTransform.localEulerAngles = shadow.rectTransform.localEulerAngles = new Vector3(0, 0, tilt);
            Pos(burst, pos + new Vector2(0, -25f * k));
            Pos(shadow, pos + new Vector2(6, 6 - 25f * k));
            float a = k < 0.75f ? 1f : 1f - (k - 0.75f) / 0.25f;
            Alpha(burst, a); Alpha(shadow, 0.9f * a); text.alpha = a;
        }, () => { Kill(shadow); Kill(burst); });
    }

    // =====================================================================
    // Building blocks
    // =====================================================================
    private void Burst(Vector2 at, float size)
    {
        var flash = Spawn(BsUI.DotSprite, Color.white, at, 30f * size);
        Animate(0f, 0.22f, k => { Size(flash, Mathf.Lerp(30f, 120f, k) * size); Alpha(flash, 1f - k); }, () => Kill(flash));

        var smoke = Spawn(BsUI.DotSprite, new Color(0.25f, 0.25f, 0.28f, 0.55f), at, 40f * size);
        Animate(0.1f, 1.2f, k => { Size(smoke, Mathf.Lerp(40f, 150f, k) * size); Alpha(smoke, 0.55f * (1f - k));
                                    Pos(smoke, at + new Vector2(0, -40f * k)); }, () => Kill(smoke));

        var fire = Spawn(BsUI.DotSprite, new Color(1f, 0.85f, 0.3f, 1f), at, 20f * size);
        Animate(0f, 0.55f, k =>
        {
            Size(fire, Mathf.Lerp(20f, 105f, Mathf.Sqrt(k)) * size);
            fire.color = Color.Lerp(new Color(1f, 0.9f, 0.35f, 1f), new Color(0.85f, 0.18f, 0.05f, 0f), k);
        }, () => Kill(fire));

        for (int i = 0; i < 12; i++)
        {
            var spark = Spawn(BsUI.DotSprite, new Color(1f, UnityEngine.Random.Range(0.5f, 0.9f), 0.2f, 1f), at, UnityEngine.Random.Range(5f, 10f) * size);
            float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f), speed = UnityEngine.Random.Range(170f, 380f) * size;
            Projectile(spark, at, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * speed, 600f, UnityEngine.Random.Range(0.5f, 0.8f));
        }
    }

    private void Ring(Vector2 at, float delay, float dur, float from, float to, Color color)
    {
        After(delay, () =>
        {
            var ring = Spawn(BsUI.RingSprite, color, at, from);
            Animate(0f, dur, k => { Size(ring, Mathf.Lerp(from, to, 1f - (1f - k) * (1f - k))); Alpha(ring, color.a * (1f - k)); }, () => Kill(ring));
        });
    }

    private void Projectile(Image img, Vector2 start, Vector2 velocity, float gravity, float life)
    {
        float a0 = img.color.a;
        Animate(0f, life, k =>
        {
            float t = k * life;
            Pos(img, start + velocity * t + new Vector2(0, 0.5f * gravity * t * t));
            Alpha(img, a0 * (1f - k));
        }, () => Kill(img));
    }

    public void Shake(float amplitude, float duration)
    {
        if (amplitude < shakeAmp * (shakeLeft / Mathf.Max(0.01f, shakeDur))) return;   // a bigger shake is running
        shakeAmp = amplitude; shakeLeft = shakeDur = duration;
    }

    // ── tiny animation system ──
    private void After(float delay, Action action)
    {
        if (delay <= 0f) { action(); return; }
        anims.Add(new Anim { delay = delay, dur = 0f, end = action });
    }

    private void Animate(float delay, float dur, Action<float> step, Action end = null) =>
        anims.Add(new Anim { delay = delay, dur = dur, step = step, end = end });

    private void Update()
    {
        float dt = Time.deltaTime;
        for (int i = anims.Count - 1; i >= 0; i--)
        {
            if (i >= anims.Count) continue;
            var a = anims[i];
            if (a.delay > 0f) { a.delay -= dt; continue; }
            a.t += dt;
            float k = a.dur <= 0f ? 1f : Mathf.Clamp01(a.t / a.dur);
            a.step?.Invoke(k);
            if (k >= 1f)
            {
                anims.Remove(a);
                a.end?.Invoke();        // may add new animations
            }
        }

        if (shakeTarget != null)
        {
            if (shakeLeft > 0f)
            {
                shakeLeft -= dt;
                float amp = shakeAmp * Mathf.Clamp01(shakeLeft / shakeDur);
                shakeTarget.anchoredPosition = UnityEngine.Random.insideUnitCircle * amp;
            }
            else shakeTarget.anchoredPosition = Vector2.zero;
        }
    }

    private Image Spawn(Sprite sprite, Color color, Vector2 at, float size)
    {
        var go = new GameObject("Fx", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(layer, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(at.x, -at.y);
        rt.sizeDelta = new Vector2(size, size);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static void Size(Image img, float s) { if (img != null) img.rectTransform.sizeDelta = new Vector2(s, s); }
    private static void Pos(Image img, Vector2 p) { if (img != null) img.rectTransform.anchoredPosition = new Vector2(p.x, -p.y); }
    private static void Alpha(Image img, float a) { if (img != null) { var c = img.color; c.a = Mathf.Clamp01(a); img.color = c; } }
    private static void Kill(Image img) { if (img != null) Destroy(img.gameObject); }

    private void Play(AudioClip clip, float vol)
    {
        if (clip != null && audioSource != null) audioSource.PlayOneShot(clip, vol * volume);
    }

    // =====================================================================
    // Sounds, generated from maths (sine waves + noise)
    // =====================================================================
    private const int Rate = 44100;
    private readonly System.Random rng = new System.Random(7);
    private float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

    private void MakeSounds()
    {
        // FIRE: deep cannon "THOOMP" (pitch-dropping sub + noisy muzzle blast) with an echo
        {
            float phase = 0f, lp = 0f;
            cannon = Clip("Cannon", 1.3f, t =>
            {
                float f = 42f + 90f * Mathf.Exp(-t * 18f);
                phase += 2f * Mathf.PI * f / Rate;
                lp += 0.12f * (Noise() - lp);
                float body = Mathf.Sin(phase) * Mathf.Exp(-t * 6f) * 1.1f;
                float blast = lp * 2.2f * Mathf.Exp(-t * 22f);
                float crack = t < 0.012f ? Noise() * 0.9f : 0f;
                return body + blast + crack;
            }, 0.35f);
        }
        // INCOMING: falling "bomb whistle" (high to low) - plays before the enemy's shot lands on you
        incoming = Clip("Whistle", 0.38f, t =>
        {
            float f = 1900f - 3600f * t;                                   // 1900 Hz -> ~530 Hz
            return Mathf.Sin(2f * Mathf.PI * (1900f * t - 1800f * t * t)) * 0.28f * Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((0.38f - t) / 0.04f) * (0.6f + 0.4f * f / 1900f);
        });
        // HIT: big layered explosion - sharp crack, rumbling body, deep sub drop, crackling debris, echo
        {
            float brown = 0f, lp = 0f, phase = 0f;
            explosion = Clip("Explosion", 2.0f, t =>
            {
                float n = Noise();
                brown = Mathf.Clamp(brown * 0.992f + n * 0.12f, -1f, 1f);
                lp += (0.05f + 0.25f * Mathf.Exp(-t * 6f)) * (n - lp);           // starts bright, gets darker
                phase += 2f * Mathf.PI * (30f + 70f * Mathf.Exp(-t * 8f)) / Rate;
                float env = (1f - Mathf.Exp(-t * 300f)) * Mathf.Exp(-t * 2.4f);
                float crack = t < 0.025f ? n * (1f - t / 0.025f) * 1.2f : 0f;
                float debris = rng.NextDouble() < 0.0035 * Mathf.Exp(-t * 1.5f) ? Noise() * 0.8f : 0f;
                return (brown * 1.6f + lp * 0.9f) * env + Mathf.Sin(phase) * Mathf.Exp(-t * 3f) * 0.9f + crack + debris;
            }, 0.3f);
        }
        // MISS: water "SPLOOSH" - a whoosh whose tone falls, plus little rising bubble blips
        {
            float lp = 0f, lp2 = 0f;
            float[] bubbleStart = new float[14], bubbleFreq = new float[14];
            for (int i = 0; i < bubbleStart.Length; i++) { bubbleStart[i] = 0.08f + (float)rng.NextDouble() * 0.6f; bubbleFreq[i] = 500f + (float)rng.NextDouble() * 900f; }
            splash = Clip("Splash", 1.0f, t =>
            {
                float n = Noise();
                float cutoff = 0.45f * Mathf.Exp(-t * 3.5f) + 0.03f;
                lp += cutoff * (n - lp);
                lp2 += cutoff * 0.5f * (lp - lp2);
                float env = t < 0.02f ? t / 0.02f : Mathf.Exp(-(t - 0.02f) * 3.2f);
                float v = (lp - lp2 * 0.6f) * env * 1.6f;
                for (int i = 0; i < bubbleStart.Length; i++)                       // bubbles: short rising "blip"
                {
                    float bt = t - bubbleStart[i];
                    if (bt > 0f && bt < 0.06f)
                        v += Mathf.Sin(2f * Mathf.PI * bubbleFreq[i] * (bt + bt * bt * 12f)) * Mathf.Exp(-bt * 60f) * 0.25f;
                }
                return v;
            }, 0.15f);
        }
        // SUNK: deep ship horn (two notes with vibrato) + echo
        horn = Clip("Horn", 1.8f, t =>
        {
            float env = Mathf.Clamp01(t / 0.1f) * Mathf.Clamp01((1.8f - t) / 0.5f);
            float vib = 1f + 0.006f * Mathf.Sin(2f * Mathf.PI * 5f * t);
            float v = 0f;
            for (int h = 1; h <= 8; h++)
                v += (Mathf.Sin(2f * Mathf.PI * 87f * vib * h * t) + 0.7f * Mathf.Sin(2f * Mathf.PI * 130.5f * vib * h * t)) / (h * 1.2f);
            return v * 0.14f * env;
        }, 0.3f);
        // WIN: bright fanfare (chord arpeggio, then a held major chord)
        victory = Clip("Fanfare", 1.6f, t =>
        {
            float[] notes = { 523.3f, 659.3f, 784f, 1046.5f };
            float v = 0f;
            for (int i = 0; i < 4; i++)
            {
                float start = i * 0.12f, lt = t - start;
                if (lt < 0f) continue;
                float hold = i == 3 ? 1.2f : 0.5f;
                float env = Mathf.Clamp01(lt / 0.01f) * Mathf.Exp(-lt * (i == 3 ? 1.6f : 4f)) * (lt < hold ? 1f : 0f);
                float x = 2f * Mathf.PI * notes[i] * lt;
                v += (Mathf.Sin(x) + 0.5f * Mathf.Sin(2f * x) + 0.25f * Mathf.Sin(3f * x)) * env * 0.22f;
            }
            return v;
        }, 0.25f);
        // LOSE: sad trombone "wah wah wah waaah" (falling notes, the last one wobbles)
        {
            float phase = 0f;
            defeat = Clip("SadTrombone", 2.4f, t =>
            {
                float[] notes = { 293.7f, 277.2f, 261.6f, 246.9f };
                float[] starts = { 0f, 0.45f, 0.9f, 1.35f };
                int i = t < 0.45f ? 0 : t < 0.9f ? 1 : t < 1.35f ? 2 : 3;
                float lt = t - starts[i];
                float f = notes[i];
                if (i == 3) f *= (1f + 0.025f * Mathf.Sin(2f * Mathf.PI * 6f * lt)) * (1f - 0.03f * lt);
                phase += 2f * Mathf.PI * f / Rate;                              // smooth pitch changes (no clicks)
                float env = Mathf.Clamp01(lt / 0.05f) * (i == 3 ? Mathf.Clamp01((1.05f - lt) / 0.45f) : Mathf.Clamp01((0.43f - lt) / 0.08f));
                float v = 0f;
                for (int h = 1; h <= 6; h++) v += Mathf.Sin(h * phase) / h;      // brassy tone
                return v * 0.16f * env;
            }, 0.15f);
        }
        clunk = Clip("Clunk", 0.25f, t => Mathf.Sin(2f * Mathf.PI * (80f + 90f * Mathf.Exp(-t * 35f)) * t) * Mathf.Exp(-t * 18f) * 0.9f
                                        + Noise() * Mathf.Exp(-t * 90f) * 0.35f, 0.1f);
        tick  = Clip("Tick", 0.06f, t => Mathf.Sin(2f * Mathf.PI * 1800f * t) * Mathf.Exp(-t * 80f) * 0.45f);
        pop   = Clip("Pop", 0.15f, t => Mathf.Sin(2f * Mathf.PI * (300f * t + 6000f * t * t)) * Mathf.Exp(-t * 30f) * 0.55f);
        blip  = Clip("Ready", 0.4f, t =>
        {
            float f = t < 0.12f ? 784f : t < 0.24f ? 1046.5f : 1568f;
            float lt = t % 0.12f;
            return (Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * f * t)) * Mathf.Exp(-lt * 14f) * Mathf.Clamp01(lt / 0.004f) * Mathf.Clamp01((0.4f - t) / 0.05f) * 0.35f;
        });
    }

    /// <summary>Use your own sound files instead of the generated ones (any slot left empty keeps the generated sound).</summary>
    public void UseCustomSounds(AudioClip fire, AudioClip hit, AudioClip miss, AudioClip sunk, AudioClip win, AudioClip lose, AudioClip chat)
    {
        if (fire != null) cannon = fire;
        if (hit  != null) explosion = hit;
        if (miss != null) splash = miss;
        if (sunk != null) horn = sunk;
        if (win  != null) victory = win;
        if (lose != null) defeat = lose;
        if (chat != null) pop = chat;
    }

    private static AudioClip Clip(string name, float seconds, Func<float, float> wave, float echo = 0f)
    {
        int n = Mathf.CeilToInt(seconds * Rate);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = wave(i / (float)Rate);
        if (echo > 0f)                                                         // simple room echo: a few delayed, quieter copies
        {
            int[] taps = { (int)(0.043f * Rate), (int)(0.071f * Rate), (int)(0.113f * Rate) };
            for (int i = n - 1; i >= 0; i--)
                foreach (int d in taps)
                    if (i - d >= 0) data[i] += data[i - d] * echo * 0.5f;
        }
        float peak = 0f;
        for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
        float gain = peak > 0.95f ? 0.95f / peak : 1f;                         // never clip / crackle
        for (int i = 0; i < n; i++) data[i] *= gain;
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}

/// <summary>
/// CREATIVITY: an animated ocean behind the whole screen. Two layers of soft wave lines
/// drift sideways at different speeds, so the sea looks like it is moving.
/// </summary>
public class OceanBackground : MonoBehaviour
{
    private RawImage near, far;

    public static OceanBackground Create(RectTransform parent)
    {
        var holder = new GameObject("Ocean", typeof(RectTransform));
        var rt = (RectTransform)holder.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var ocean = holder.AddComponent<OceanBackground>();
        var tex = WaveTexture();
        ocean.far  = Layer(rt, "Far Waves",  tex, 0.05f);
        ocean.near = Layer(rt, "Near Waves", tex, 0.09f);
        return ocean;
    }

    private static RawImage Layer(RectTransform parent, string name, Texture2D tex, float alpha)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<RawImage>();
        img.texture = tex;
        img.color = new Color(0.45f, 1f, 0.65f, alpha);   // THEME: green-tinted waves
        img.raycastTarget = false;
        return img;
    }

    private void Update()
    {
        float t = Time.time;
        far.uvRect  = new Rect(t * 0.010f, Mathf.Sin(t * 0.3f) * 0.02f, 5f, 9f);
        near.uvRect = new Rect(-t * 0.022f, 0.5f + Mathf.Sin(t * 0.45f) * 0.03f, 3.5f, 6f);
    }

    /// <summary>A tileable picture of soft wave crests (white lines on transparent).</summary>
    private static Texture2D WaveTexture()
    {
        const int W = 256, H = 64;
        var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float crest = H * 0.5f + 7f * Mathf.Sin(x / (float)W * Mathf.PI * 4f) + 3f * Mathf.Sin(x / (float)W * Mathf.PI * 10f);
                float d = y - crest;
                float a = Mathf.Exp(-d * d / 6f) * (0.6f + 0.4f * Mathf.Sin(x / (float)W * Mathf.PI * 6f));
                t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
        t.Apply();
        return t;
    }
}

/// <summary>THEME: faint CRT monitor scanlines that slowly roll down the screen.</summary>
public class Scanlines : MonoBehaviour
{
    private RawImage img;

    public static Scanlines Create(RectTransform parent)
    {
        var go = new GameObject("Scanlines", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var s = go.AddComponent<Scanlines>();
        s.img = go.AddComponent<RawImage>();
        var tex = new Texture2D(1, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        tex.SetPixel(0, 0, new Color(0, 0, 0, 0.22f));
        tex.SetPixel(0, 1, new Color(0, 0, 0, 0f));
        tex.SetPixel(0, 2, new Color(0, 0, 0, 0f));
        tex.SetPixel(0, 3, new Color(0, 0, 0, 0f));
        tex.Apply();
        s.img.texture = tex;
        s.img.raycastTarget = false;
        return s;
    }

    private void Update()
    {
        img.uvRect = new Rect(0f, Time.time * 0.6f, 1f, 270f);   // 270 lines, rolling slowly
    }
}

/// <summary>THEME Comic Book: a slowly turning sunburst and drifting halftone dots behind the whole screen.</summary>
public class ComicBackground : MonoBehaviour
{
    private RectTransform rays;
    private RawImage dots;

    public static ComicBackground Create(RectTransform parent)
    {
        var holder = new GameObject("Comic Background", typeof(RectTransform));
        var rt = (RectTransform)holder.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var bg = holder.AddComponent<ComicBackground>();

        // sunburst rays (big rotating image, centred)
        var raysGO = new GameObject("Sunburst", typeof(RectTransform));
        bg.rays = (RectTransform)raysGO.transform;
        bg.rays.SetParent(rt, false);
        bg.rays.anchorMin = bg.rays.anchorMax = bg.rays.pivot = new Vector2(0.5f, 0.5f);
        bg.rays.sizeDelta = new Vector2(2600, 2600);
        var raysImg = raysGO.AddComponent<RawImage>();
        raysImg.texture = RaysTexture(256, 24);
        raysImg.color = new Color(1f, 0.6f, 0f, 0.22f);
        raysImg.raycastTarget = false;

        // halftone dots (tiled)
        var dotsGO = new GameObject("Halftone", typeof(RectTransform));
        var drt = (RectTransform)dotsGO.transform;
        drt.SetParent(rt, false);
        drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
        drt.offsetMin = drt.offsetMax = Vector2.zero;
        bg.dots = dotsGO.AddComponent<RawImage>();
        bg.dots.texture = DotTexture(24);
        bg.dots.color = new Color(0.9f, 0.25f, 0.1f, 0.22f);
        bg.dots.raycastTarget = false;
        return bg;
    }

    private void Update()
    {
        float t = Time.time;
        rays.localEulerAngles = new Vector3(0, 0, t * 4f);
        dots.uvRect = new Rect(t * 0.15f, t * 0.08f, 1920f / 24f, 1080f / 24f);
    }

    private static Texture2D RaysTexture(int n, int count)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        float c = n * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float ang = Mathf.Atan2(y + 0.5f - c, x + 0.5f - c);
                float k = ((ang / (2f * Mathf.PI) * count) % 1f + 1f) % 1f;
                tex.SetPixel(x, y, new Color(1, 1, 1, k < 0.5f ? 1f : 0f));
            }
        tex.Apply();
        return tex;
    }

    private static Texture2D DotTexture(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        float c = n * 0.5f, r = n * 0.22f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d + 0.5f)));
            }
        tex.Apply();
        return tex;
    }
}

/// <summary>
/// THEME Tropical Islands: a bright lagoon gradient with little palm islands that bob,
/// fish that swim across, bubbles rising and sparkles twinkling on the water.
/// Everything is positioned with anchors (0..1), so it fits any screen or menu size.
/// </summary>
public class TropicalBackground : MonoBehaviour
{
    private class Mover { public RectTransform rt; public float baseX, baseY, speed, phase, amp, size; public int kind; public Image img; }
    private readonly System.Collections.Generic.List<Mover> movers = new System.Collections.Generic.List<Mover>();
    private RawImage waves;

    private const int Island = 0, Fish = 1, Bubble = 2, Sparkle = 3;

    public static TropicalBackground Create(RectTransform parent)
    {
        var holder = new GameObject("Tropical Background", typeof(RectTransform));
        var rt = (RectTransform)holder.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var bg = holder.AddComponent<TropicalBackground>();

        // 1) lagoon gradient (light turquoise at the top, deeper blue at the bottom)
        var grad = Stretch(rt, "Lagoon").gameObject.AddComponent<RawImage>();
        grad.texture = GradientTexture(new Color(0.44f, 0.89f, 0.95f), new Color(0.10f, 0.61f, 0.75f));
        grad.raycastTarget = false;

        // 2) gentle white wave lines drifting sideways
        bg.waves = Stretch(rt, "Waves").gameObject.AddComponent<RawImage>();
        bg.waves.texture = WaveTexture();
        bg.waves.color = new Color(1f, 1f, 1f, 0.10f);
        bg.waves.raycastTarget = false;

        // 3) palm islands in the screen edges / gaps
        var islandSprite = MakeSprite(IslandTexture(256));
        bg.Add(rt, "Island L",  islandSprite, Island, 0.06f, 0.14f, 230f, Color.white, 0f, 0.0f);
        bg.Add(rt, "Island R",  islandSprite, Island, 0.95f, 0.20f, 190f, Color.white, 0f, 1.7f);
        bg.Add(rt, "Island M",  islandSprite, Island, 0.50f, 0.05f, 140f, Color.white, 0f, 3.1f);

        // 4) fish swimming across (orange / yellow / pink)
        var fishSprite = MakeSprite(FishTexture(64, 32));
        bg.Add(rt, "Fish 1", fishSprite, Fish, 0.0f, 0.30f, 70f, new Color(1f, 0.55f, 0.15f), 0.045f, 0.0f);
        bg.Add(rt, "Fish 2", fishSprite, Fish, 0.4f, 0.62f, 55f, new Color(1f, 0.85f, 0.2f), 0.030f, 2.0f);
        bg.Add(rt, "Fish 3", fishSprite, Fish, 0.8f, 0.85f, 48f, new Color(1f, 0.45f, 0.65f), -0.038f, 4.0f);
        bg.Add(rt, "Fish 4", fishSprite, Fish, 0.2f, 0.10f, 60f, new Color(0.6f, 1f, 0.5f), -0.025f, 1.0f);

        // 5) bubbles rising + 6) sparkles twinkling
        for (int i = 0; i < 14; i++)
            bg.Add(rt, "Bubble " + i, BsUI.RingSprite, Bubble, UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.Range(12f, 26f),
                   new Color(1f, 1f, 1f, 0.55f), UnityEngine.Random.Range(0.03f, 0.07f), UnityEngine.Random.value * 10f);
        for (int i = 0; i < 18; i++)
            bg.Add(rt, "Sparkle " + i, BsUI.DotSprite, Sparkle, UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.Range(6f, 12f),
                   Color.white, 0f, UnityEngine.Random.value * 10f);
        return bg;
    }

    private void Add(RectTransform parent, string name, Sprite sprite, int kind, float x, float y, float size, Color color, float speed, float phase)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(x, y);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = kind == Fish ? new Vector2(size, size * 0.5f) : new Vector2(size, size);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        if (kind == Fish && speed < 0f) rt.localScale = new Vector3(-1f, 1f, 1f);   // swim left = face left
        movers.Add(new Mover { rt = rt, baseX = x, baseY = y, speed = speed, phase = phase, size = size, kind = kind, img = img });
    }

    private void Update()
    {
        float t = Time.time;
        if (waves != null) waves.uvRect = new Rect(t * 0.015f, Mathf.Sin(t * 0.4f) * 0.03f, 4f, 8f);

        for (int i = 0; i < movers.Count; i++)
        {
            var m = movers[i];
            float x = m.baseX, y = m.baseY;
            switch (m.kind)
            {
                case Island:   // bob and rock a little
                    y += Mathf.Sin(t * 0.9f + m.phase) * 0.006f;
                    m.rt.localEulerAngles = new Vector3(0, 0, Mathf.Sin(t * 0.7f + m.phase) * 2.5f);
                    break;
                case Fish:     // swim across and wrap around, with a wiggle
                    x = Wrap(m.baseX + t * m.speed);
                    y += Mathf.Sin(t * 1.3f + m.phase) * 0.015f;
                    m.rt.localEulerAngles = new Vector3(0, 0, Mathf.Sin(t * 8f + m.phase) * 6f);
                    break;
                case Bubble:   // rise, drift side to side, fade near the top
                    y = Wrap(m.baseY + t * m.speed);
                    x += Mathf.Sin(t * 2f + m.phase) * 0.006f;
                    var c = m.img.color; c.a = 0.55f * (1f - y); m.img.color = c;
                    break;
                case Sparkle:  // twinkle
                    float k = Mathf.Max(0f, Mathf.Sin(t * 2.2f + m.phase));
                    k = k * k * k;
                    m.rt.localScale = Vector3.one * (0.3f + k);
                    var s = m.img.color; s.a = k * 0.9f; m.img.color = s;
                    break;
            }
            m.rt.anchorMin = m.rt.anchorMax = new Vector2(x, y);
        }
    }

    private static float Wrap(float v) { v %= 1.1f; if (v < 0f) v += 1.1f; return v - 0.05f; }

    private static RectTransform Stretch(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static Sprite MakeSprite(Texture2D tex)
    {
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Texture2D GradientTexture(Color top, Color bottom)
    {
        var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 64; y++) tex.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
        tex.Apply();
        return tex;
    }

    private static Texture2D WaveTexture()
    {
        const int W = 256, H = 64;
        var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float crest = H * 0.5f + 6f * Mathf.Sin(x / (float)W * Mathf.PI * 4f);
                float d = y - crest;
                t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Exp(-d * d / 5f))));
            }
        t.Apply();
        return t;
    }

    /// <summary>A little island: sand mound, curved palm trunk and five green leaves (drawn pixel by pixel).</summary>
    private static Texture2D IslandTexture(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var clear = new Color(0, 0, 0, 0);
        var sand  = new Color(0.98f, 0.86f, 0.55f);
        var sandD = new Color(0.90f, 0.72f, 0.40f);
        var trunk = new Color(0.55f, 0.35f, 0.17f);
        var leaf  = new Color(0.20f, 0.70f, 0.30f);
        var leafD = new Color(0.10f, 0.50f, 0.22f);
        var foam  = new Color(1f, 1f, 1f, 0.85f);

        float cx = n * 0.5f;
        // leaf directions (angle in radians from the crown) and lengths
        float[] ang = { 0.25f, 0.9f, 1.57f, 2.25f, 2.9f };
        Vector2 crown = new Vector2(n * 0.56f, n * 0.80f);

        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Color c = clear;
                float fx = x + 0.5f, fy = y + 0.5f;

                // foam ring around the sand
                float ex = (fx - cx) / (n * 0.48f), ey = (fy - n * 0.18f) / (n * 0.13f);
                float e = ex * ex + ey * ey;
                if (e < 1f && fy < n * 0.20f) c = foam;

                // sand mound (top half of an ellipse)
                ex = (fx - cx) / (n * 0.42f); ey = (fy - n * 0.18f) / (n * 0.16f);
                e = ex * ex + ey * ey;
                if (e < 1f && fy >= n * 0.12f) c = Color.Lerp(sand, sandD, Mathf.Clamp01((n * 0.26f - fy) / (n * 0.14f)) * 0.6f);

                // curved trunk from the sand up to the crown
                if (fy > n * 0.22f && fy < crown.y)
                {
                    float k = (fy - n * 0.22f) / (crown.y - n * 0.22f);
                    float tx = cx - n * 0.04f + (crown.x - cx + n * 0.04f) * k * k;
                    float half = n * (0.035f - 0.012f * k);
                    if (Mathf.Abs(fx - tx) < half)
                        c = ((int)(fy / (n * 0.05f)) % 2 == 0) ? trunk : trunk * 0.85f + new Color(0, 0, 0, 0.15f);
                }

                // leaves: thick arcs drooping out from the crown
                for (int i = 0; i < ang.Length; i++)
                {
                    float a = ang[i];
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Vector2 p = new Vector2(fx, fy) - crown;
                    float along = p.x * dir.x + p.y * dir.y;
                    float len = n * 0.30f;
                    if (along < 0f || along > len) continue;
                    float u = along / len;
                    float droop = u * u * n * 0.12f;                     // leaves bend downwards
                    Vector2 centre = crown + dir * along + new Vector2(0, -droop);
                    float width = n * 0.055f * Mathf.Sin(u * Mathf.PI);   // fat in the middle, thin at the tips
                    float dist = (new Vector2(fx, fy) - centre).magnitude;
                    if (dist < width) c = dist < width * 0.35f ? leafD : leaf;
                }

                // coconuts
                if ((new Vector2(fx, fy) - (crown + new Vector2(-n * 0.03f, -n * 0.03f))).magnitude < n * 0.03f ||
                    (new Vector2(fx, fy) - (crown + new Vector2(n * 0.03f, -n * 0.035f))).magnitude < n * 0.03f)
                    c = new Color(0.40f, 0.25f, 0.10f);

                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }

    /// <summary>A white fish (body, tail, fin, eye). It is tinted by the Image colour; the eye stays dark.</summary>
    private static Texture2D FishTexture(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fx = (x + 0.5f) / w, fy = (y + 0.5f) / h;
                Color c = new Color(0, 0, 0, 0);
                // body ellipse (head on the right)
                float ex = (fx - 0.58f) / 0.34f, ey = (fy - 0.5f) / 0.36f;
                if (ex * ex + ey * ey < 1f) c = Color.white;
                // tail triangle on the left
                if (fx < 0.30f && fx > 0.04f && Mathf.Abs(fy - 0.5f) < (0.30f - fx) * 1.6f + 0.05f && Mathf.Abs(fy - 0.5f) > (0.30f - fx) * 0.4f - 0.02f)
                    c = Color.white;
                // stripe
                if (ex * ex + ey * ey < 1f && Mathf.Abs(fx - 0.50f) < 0.03f) c = new Color(1f, 1f, 1f, 1f) * 0.75f + new Color(0, 0, 0, 0.25f);
                // eye
                float dx = fx - 0.78f, dy = (fy - 0.58f) * h / w;
                if (dx * dx + dy * dy < 0.0016f) c = new Color(0.05f, 0.05f, 0.1f);
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }
}
