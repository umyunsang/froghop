using UnityEngine;

/// <summary>
/// The end-of-level trophy. Plays its pressed animation and hands off to
/// <see cref="GameManager.LevelClear"/>.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class LevelGoal : MonoBehaviour
{
    public Sprite[] idleFrames;
    public Sprite[] pressedFrames;
    public float fps = 20f;
    public ParticleSystem burst;

    private SpriteAnim anim;
    private bool done;

    void Start()
    {
        anim = GetComponent<SpriteAnim>();
        if (anim == null) anim = gameObject.AddComponent<SpriteAnim>();
        if (idleFrames != null && idleFrames.Length > 0) anim.Play(idleFrames, true, fps);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (done) return;
        Player p = other.GetComponent<Player>();
        if (p == null || p.IsGameOver) return;

        done = true;
        if (pressedFrames != null && pressedFrames.Length > 0 && anim != null)
            anim.Play(pressedFrames, false, fps);
        if (burst != null) burst.Play();

        Juice.Shake(0.2f, 0.15f);
        if (GameManager.Instance != null) GameManager.Instance.LevelClear();
    }
}
