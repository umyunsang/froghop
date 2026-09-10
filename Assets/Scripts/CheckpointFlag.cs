using UnityEngine;

/// <summary>
/// Mid-level checkpoint. Raises its flag on first touch and becomes the respawn anchor,
/// so a long stage does not send the player back to the start on every mistake.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class CheckpointFlag : MonoBehaviour
{
    public Sprite noFlag;
    public Sprite[] flagOutFrames;
    public Sprite[] flagIdleFrames;
    public float fps = 20f;

    /// <summary>World position of the most recently activated checkpoint, if any.</summary>
    public static Vector3? Active { get; private set; }

    private SpriteAnim anim;
    private SpriteRenderer sr;
    private bool taken;

    void Awake()
    {
        // Static state survives scene reloads, so clear it as the level rebuilds.
        Active = null;
    }

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        anim = GetComponent<SpriteAnim>();
        if (anim == null) anim = gameObject.AddComponent<SpriteAnim>();
        if (noFlag != null) sr.sprite = noFlag;
        anim.Stop();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (taken) return;
        if (other.GetComponent<Player>() == null) return;

        taken = true;
        Active = transform.position + Vector3.up * 0.5f;

        if (flagOutFrames != null && flagOutFrames.Length > 0 && anim != null)
        {
            anim.Play(flagOutFrames, false, fps, () =>
            {
                if (flagIdleFrames != null && flagIdleFrames.Length > 0 && anim != null)
                    anim.Play(flagIdleFrames, true, fps);
            });
        }

        AudioManagerProc.PlayCollect();
        if (GameManager.Instance != null) GameManager.Instance.Toast("CHECKPOINT");
    }
}
