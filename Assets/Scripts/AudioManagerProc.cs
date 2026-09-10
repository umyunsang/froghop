using UnityEngine;

/// <summary>
/// The project ships with no audio files, so every sound here is synthesised into an
/// AudioClip at startup. No external assets, no API keys, no licensing questions.
/// </summary>
public class AudioManagerProc : MonoBehaviour
{
    private const int SampleRate = 44100;

    private static AudioManagerProc instance;

    private AudioClip clipJump, clipLand, clipBounce, clipHurt, clipCollect, clipDeath, clipClear;
    private AudioClip clipBgm;
    private AudioSource sfxSource;
    private AudioSource bgmSource;
    private float lastLandTime;

    [Range(0f, 1f)] public float sfxVolume = 0.45f;
    [Range(0f, 1f)] public float bgmVolume = 0.20f;

    private static AudioManagerProc Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("~AudioManagerProc");
                instance = go.AddComponent<AudioManagerProc>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.playOnAwake = false;
        bgmSource.loop = true;
        bgmSource.spatialBlend = 0f;
        bgmSource.volume = bgmVolume;

        BuildClips();

        bgmSource.clip = clipBgm;
        bgmSource.Play();
    }

    // ------------------------------------------------------------------ public API

    public static void PlayJump() { Instance.Play(Instance.clipJump, 0.9f); }
    public static void PlayBounce() { Instance.Play(Instance.clipBounce, 1f); }
    public static void PlayHurt() { Instance.Play(Instance.clipHurt, 1f); }
    public static void PlayCollect() { Instance.Play(Instance.clipCollect, 0.85f); }
    public static void PlayDeath() { Instance.Play(Instance.clipDeath, 1f); }
    public static void PlayClear() { Instance.Play(Instance.clipClear, 1f); }

    public static void PlayLand()
    {
        AudioManagerProc a = Instance;
        // Landing can retrigger on the same frame the ground check flickers; rate-limit it.
        if (Time.unscaledTime - a.lastLandTime < 0.08f) return;
        a.lastLandTime = Time.unscaledTime;
        a.Play(a.clipLand, 0.55f);
    }

    public static void SetBgmVolume(float v)
    {
        AudioManagerProc a = Instance;
        a.bgmVolume = Mathf.Clamp01(v);
        if (a.bgmSource != null) a.bgmSource.volume = a.bgmVolume;
    }

    public static void DuckBgm(float volume)
    {
        AudioManagerProc a = Instance;
        if (a.bgmSource != null) a.bgmSource.volume = Mathf.Clamp01(volume);
    }

    /// <summary>
    /// Undo any ducking. This object survives scene loads, so without an explicit restore the
    /// quiet set on game over would persist through every retry for the rest of the session.
    /// </summary>
    public static void RestoreBgm()
    {
        AudioManagerProc a = Instance;
        if (a.bgmSource == null) return;
        a.bgmSource.volume = a.bgmVolume;
        if (!a.bgmSource.isPlaying) a.bgmSource.Play();
    }

    private void Play(AudioClip clip, float volume)
    {
        if (clip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(clip, volume * sfxVolume);
    }

    // ------------------------------------------------------------------ synthesis

    private void BuildClips()
    {
        clipJump = Blip("sfx_jump", 0.16f, 420f, 880f, WaveKind.Square, 0.02f);
        clipBounce = Blip("sfx_bounce", 0.24f, 300f, 1200f, WaveKind.Square, 0.02f);
        clipHurt = Blip("sfx_hurt", 0.28f, 520f, 110f, WaveKind.Saw, 0.01f);
        clipLand = Thud("sfx_land", 0.12f);
        clipCollect = Arp("sfx_collect", new float[] { 880f, 1174f, 1568f }, 0.055f, WaveKind.Square);
        clipDeath = Arp("sfx_death", new float[] { 660f, 550f, 440f, 330f, 220f }, 0.09f, WaveKind.Square);
        clipClear = Arp("sfx_clear", new float[] { 523f, 659f, 784f, 1046f, 784f, 1046f, 1318f },
                        0.11f, WaveKind.Square);
        clipBgm = BuildBgm();
    }

    private enum WaveKind { Square, Saw, Triangle, Noise }

    private static float Wave(WaveKind kind, float phase, ref uint rng)
    {
        switch (kind)
        {
            case WaveKind.Square: return Mathf.Repeat(phase, 1f) < 0.5f ? 1f : -1f;
            case WaveKind.Saw: return Mathf.Repeat(phase, 1f) * 2f - 1f;
            case WaveKind.Triangle: return Mathf.PingPong(Mathf.Repeat(phase, 1f) * 2f, 1f) * 2f - 1f;
            default:
                rng = rng * 1664525u + 1013904223u;
                return ((rng >> 9) & 0xFFFF) / 32767.5f - 1f;
        }
    }

    /// <summary>A single tone that slides from one pitch to another.</summary>
    private AudioClip Blip(string name, float seconds, float f0, float f1, WaveKind kind, float attack)
    {
        int n = Mathf.CeilToInt(seconds * SampleRate);
        float[] data = new float[n];
        float phase = 0f;
        uint rng = 12345u;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float f = Mathf.Lerp(f0, f1, t * t);
            phase += f / SampleRate;
            float env = Envelope(i / (float)SampleRate, seconds, attack, 0.06f);
            data[i] = Wave(kind, phase, ref rng) * env * 0.5f;
        }
        return Make(name, data);
    }

    /// <summary>Filtered noise burst for footsteps and landings.</summary>
    private AudioClip Thud(string name, float seconds)
    {
        int n = Mathf.CeilToInt(seconds * SampleRate);
        float[] data = new float[n];
        uint rng = 987u;
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            rng = rng * 1664525u + 1013904223u;
            float noise = ((rng >> 9) & 0xFFFF) / 32767.5f - 1f;
            lp = Mathf.Lerp(lp, noise, 0.12f); // cheap one-pole low pass
            float env = Mathf.Exp(-28f * t);
            data[i] = lp * env * 0.9f;
        }
        return Make(name, data);
    }

    /// <summary>A short sequence of tones.</summary>
    private AudioClip Arp(string name, float[] freqs, float noteLen, WaveKind kind)
    {
        int noteSamples = Mathf.CeilToInt(noteLen * SampleRate);
        float[] data = new float[noteSamples * freqs.Length];
        uint rng = 5150u;
        for (int k = 0; k < freqs.Length; k++)
        {
            float phase = 0f;
            for (int i = 0; i < noteSamples; i++)
            {
                phase += freqs[k] / SampleRate;
                float t = i / (float)SampleRate;
                float env = Envelope(t, noteLen, 0.005f, noteLen * 0.5f);
                data[k * noteSamples + i] = Wave(kind, phase, ref rng) * env * 0.42f;
            }
        }
        return Make(name, data);
    }

    private static float Envelope(float t, float total, float attack, float release)
    {
        if (t < attack) return t / Mathf.Max(0.0001f, attack);
        float remain = total - t;
        if (remain < release) return Mathf.Clamp01(remain / Mathf.Max(0.0001f, release));
        return 1f;
    }

    /// <summary>A looping chiptune bed: square lead, triangle bass, noise hats.</summary>
    private AudioClip BuildBgm()
    {
        const float bpm = 132f;
        float beat = 60f / bpm;
        float step = beat * 0.5f;            // eighth notes
        int stepsPerBar = 8;
        int bars = 8;
        int totalSteps = stepsPerBar * bars;
        int stepSamples = Mathf.CeilToInt(step * SampleRate);
        float[] data = new float[stepSamples * totalSteps];

        // A minor pentatonic-ish loop that does not get tiring on repeat.
        int[] lead = {
            9, 12, 16, 12,  9, 12, 16, 19,
            17, 16, 12, 16,  9, 12, 16, 12,
            7, 11, 14, 11,  7, 11, 14, 18,
            16, 14, 11, 14,  7, 11, 14, 11,
            9, 12, 16, 12,  9, 12, 16, 19,
            21, 19, 16, 19, 16, 12, 16, 12,
            5,  9, 12,  9,  7, 11, 14, 11,
            9, 12, 16, 19, 16, 12,  9, -1
        };
        int[] bass = {
            -3, -3, -3, -3,  4, 4, 4, 4,
            -3, -3, -3, -3,  4, 4, 4, 4,
            -5, -5, -5, -5,  2, 2, 2, 2,
            -5, -5, -5, -5,  2, 2, 2, 2,
            -3, -3, -3, -3,  4, 4, 4, 4,
            -3, -3, -3, -3,  0, 0, 0, 0,
            -7, -7, -7, -7, -5, -5, -5, -5,
            -3, -3, -3, -3, -3, -3, -3, -3
        };

        uint rng = 424242u;
        float leadPhase = 0f, bassPhase = 0f;

        for (int s = 0; s < totalSteps; s++)
        {
            float leadFreq = lead[s % lead.Length] < 0 ? 0f : Midi(lead[s % lead.Length] + 60);
            float bassFreq = Midi(bass[s % bass.Length] + 36);
            bool hat = (s % 2) == 1;
            bool kick = (s % 4) == 0;

            for (int i = 0; i < stepSamples; i++)
            {
                int idx = s * stepSamples + i;
                if (idx >= data.Length) break;
                float t = i / (float)SampleRate;
                float sample = 0f;

                if (leadFreq > 0f)
                {
                    leadPhase += leadFreq / SampleRate;
                    float env = Envelope(t, step, 0.006f, step * 0.45f);
                    sample += Wave(WaveKind.Square, leadPhase, ref rng) * env * 0.16f;
                }

                bassPhase += bassFreq / SampleRate;
                float benv = Envelope(t, step, 0.004f, step * 0.3f);
                sample += Wave(WaveKind.Triangle, bassPhase, ref rng) * benv * 0.22f;

                if (hat)
                {
                    rng = rng * 1664525u + 1013904223u;
                    float noise = ((rng >> 9) & 0xFFFF) / 32767.5f - 1f;
                    sample += noise * Mathf.Exp(-90f * t) * 0.05f;
                }
                if (kick)
                {
                    float kf = Mathf.Lerp(120f, 45f, Mathf.Clamp01(t * 22f));
                    sample += Mathf.Sin(2f * Mathf.PI * kf * t) * Mathf.Exp(-16f * t) * 0.30f;
                }

                data[idx] = Mathf.Clamp(sample, -0.95f, 0.95f);
            }
        }
        return Make("bgm_loop", data);
    }

    private static float Midi(int note) { return 440f * Mathf.Pow(2f, (note - 69) / 12f); }

    private static AudioClip Make(string name, float[] data)
    {
        AudioClip c = AudioClip.Create(name, data.Length, 1, SampleRate, false);
        c.SetData(data, 0);
        return c;
    }
}
