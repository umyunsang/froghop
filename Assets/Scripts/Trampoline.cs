using UnityEngine;

/// <summary>
/// Bounce pad. Only fires when the player is coming down onto it, so brushing the side
/// while running does not launch them.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class Trampoline : MonoBehaviour
{
    public float bouncePower = 15f;
    [Tooltip("The pad fires unless the player is already rising faster than this, which lets it " +
             "trigger for someone who simply walks on as well as someone who lands on it.")]
    public float maxRiseSpeed = 0.5f;
    public float rearmTime = 0.15f;

    [Header("Sprites")]
    public Sprite idle;
    public Sprite[] jumpFrames;
    public float fps = 20f;

    private SpriteRenderer sr;
    private SpriteAnim anim;
    private float cooldown;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        anim = GetComponent<SpriteAnim>();
        if (anim == null) anim = gameObject.AddComponent<SpriteAnim>();
        if (idle != null && sr != null) sr.sprite = idle;
        anim.Stop();
    }

    void Update()
    {
        if (cooldown > 0f) cooldown -= Time.deltaTime;
    }

    void OnTriggerEnter2D(Collider2D other) { TryBounce(other); }
    void OnTriggerStay2D(Collider2D other) { TryBounce(other); }

    private void TryBounce(Collider2D other)
    {
        if (cooldown > 0f) return;

        Player p = other.GetComponent<Player>();
        if (p == null) return;

        Rigidbody2D rb = other.GetComponent<Rigidbody2D>();
        if (rb == null || rb.linearVelocity.y > maxRiseSpeed) return;
        // Feet must be at or above the pad, so brushing the side while running does not launch.
        if (other.transform.position.y < transform.position.y - 0.15f) return;

        cooldown = rearmTime;
        p.Bounce(bouncePower);

        if (anim != null && jumpFrames != null && jumpFrames.Length > 0)
        {
            anim.Play(jumpFrames, false, fps, () =>
            {
                if (sr != null && idle != null) sr.sprite = idle;
            });
        }
    }
}
