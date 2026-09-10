using System.Collections;
using UnityEngine;

/// <summary>
/// Screen shake and hit-stop. Both are global effects with no sensible owner, so they live
/// behind a static facade backed by a lazily created runtime object.
/// </summary>
public class Juice : MonoBehaviour
{
    private static Juice instance;

    private float shakeAmplitude;
    private float shakeDuration;
    private float shakeTimer;
    private Vector2 shakeOffset;
    private float hitStopTimer;
    private float storedTimeScale = 1f;

    /// <summary>Current shake displacement, consumed by the camera each frame.</summary>
    public static Vector2 ShakeOffset
    {
        get { return instance != null ? instance.shakeOffset : Vector2.zero; }
    }

    private static Juice Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("~Juice");
                instance = go.AddComponent<Juice>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    /// <summary>Shake the camera for <paramref name="duration"/> seconds.</summary>
    public static void Shake(float duration, float amplitude)
    {
        Juice j = Instance;
        // A stronger shake overrides a weaker one still in flight.
        if (amplitude >= j.shakeAmplitude || j.shakeTimer <= 0f)
        {
            j.shakeAmplitude = amplitude;
            j.shakeDuration = Mathf.Max(0.0001f, duration);
            j.shakeTimer = j.shakeDuration;
        }
    }

    /// <summary>Freeze time briefly so an impact reads as heavy.</summary>
    public static void HitStop(float seconds)
    {
        Juice j = Instance;
        if (j.hitStopTimer > 0f) return;
        j.StartCoroutine(j.HitStopRoutine(seconds));
    }

    private IEnumerator HitStopRoutine(float seconds)
    {
        hitStopTimer = seconds;
        storedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(seconds);
        // Do not stomp a pause that began during the freeze.
        if (Mathf.Approximately(Time.timeScale, 0f)) Time.timeScale = storedTimeScale;
        hitStopTimer = 0f;
    }

    void LateUpdate()
    {
        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.unscaledDeltaTime;
            float falloff = Mathf.Clamp01(shakeTimer / shakeDuration);
            float a = shakeAmplitude * falloff * falloff;
            shakeOffset = new Vector2(Random.Range(-a, a), Random.Range(-a, a));
        }
        else
        {
            shakeOffset = Vector2.zero;
            shakeAmplitude = 0f;
        }
    }
}
