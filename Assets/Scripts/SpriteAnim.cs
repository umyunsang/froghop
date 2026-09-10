using UnityEngine;

/// <summary>
/// A tiny frame-flipping animator for props. The Pixel Adventure art is plain sprite strips,
/// and wiring an Animator + controller onto every trap and pickup would be far more machinery
/// than the job needs.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteAnim : MonoBehaviour
{
    public Sprite[] frames;
    public float fps = 20f;
    public bool loop = true;
    public bool playOnAwake = true;

    private SpriteRenderer sr;
    private float timer;
    private int index;
    private bool playing;
    private System.Action onComplete;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        playing = playOnAwake;
        // Stagger identical props so a row of them does not pulse in lockstep.
        timer = Random.Range(0f, 1f / Mathf.Max(1f, fps));
    }

    void Update()
    {
        if (!playing || frames == null || frames.Length == 0) return;

        timer += Time.deltaTime;
        float frameTime = 1f / Mathf.Max(0.01f, fps);
        while (timer >= frameTime)
        {
            timer -= frameTime;
            index++;
            if (index >= frames.Length)
            {
                if (loop) index = 0;
                else
                {
                    index = frames.Length - 1;
                    playing = false;
                    if (onComplete != null) { System.Action cb = onComplete; onComplete = null; cb(); }
                    break;
                }
            }
        }
        if (sr != null) sr.sprite = frames[Mathf.Clamp(index, 0, frames.Length - 1)];
    }

    /// <summary>Swap the strip and restart. Pass <paramref name="done"/> for one-shot clips.</summary>
    public void Play(Sprite[] newFrames, bool looping, float newFps, System.Action done = null)
    {
        frames = newFrames;
        loop = looping;
        fps = newFps;
        index = 0;
        timer = 0f;
        playing = true;
        onComplete = done;
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        if (sr != null && frames != null && frames.Length > 0) sr.sprite = frames[0];
    }

    public void Stop() { playing = false; }
}
