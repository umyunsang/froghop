using UnityEngine;

/// <summary>
/// The Pixel Adventure spike head: it slides between two points, slams into the wall at each
/// end with a directional impact frame, blinks, then reverses. Tagged "Obstacle" so the
/// practice-PDF collision path in <see cref="Player"/> handles the damage.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpikeHead : MonoBehaviour
{
    public enum Axis { Vertical, Horizontal }

    [Header("Patrol")]
    public Axis axis = Axis.Vertical;
    [Tooltip("Travel distance from the start position, in world units.")]
    public float distance = 4f;
    public float speed = 6f;
    [Tooltip("Pause at each end while the impact and blink play.")]
    public float pauseTime = 0.55f;
    [Tooltip("Seconds spent easing out of rest before reaching full speed.")]
    public float windUp = 0.35f;

    [Header("Sprites")]
    public Sprite idle;
    public Sprite[] blink;
    public Sprite[] hitTop;
    public Sprite[] hitBottom;
    public Sprite[] hitLeft;
    public Sprite[] hitRight;

    private SpriteRenderer sr;
    private SpriteAnim anim;
    private Vector3 pointA, pointB;
    private Vector3 fromPoint, toPoint;
    private float pauseTimer;
    private float travelT;
    private bool towardB = true;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        anim = GetComponent<SpriteAnim>();
        if (anim == null) anim = gameObject.AddComponent<SpriteAnim>();

        Vector3 dir = axis == Axis.Vertical ? Vector3.up : Vector3.right;
        pointA = transform.position;
        pointB = transform.position + dir * distance;
        fromPoint = pointA;
        toPoint = pointB;

        if (idle != null && sr != null) sr.sprite = idle;
        anim.Play(blink != null && blink.Length > 0 ? blink : new[] { idle }, true, 14f);

        // A row of spike heads should not move in lockstep.
        pauseTimer = Random.Range(0f, pauseTime);
    }

    void Update()
    {
        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.deltaTime;
            return;
        }

        float span = Vector3.Distance(fromPoint, toPoint);
        if (span < 0.001f) return;

        // Ease out of the pause so the slam reads as an accelerating charge.
        float ramp = windUp <= 0f ? 1f : Mathf.Clamp01(travelT * span / windUp);
        travelT += (speed * Mathf.Lerp(0.25f, 1f, ramp) / span) * Time.deltaTime;

        if (travelT >= 1f)
        {
            travelT = 0f;
            transform.position = toPoint;
            Slam();
            towardB = !towardB;
            fromPoint = towardB ? pointA : pointB;
            toPoint = towardB ? pointB : pointA;
            pauseTimer = pauseTime;
            return;
        }
        transform.position = Vector3.Lerp(fromPoint, toPoint, travelT);
    }

    private void Slam()
    {
        Sprite[] frames = null;
        if (axis == Axis.Vertical) frames = towardB ? hitTop : hitBottom;
        else frames = towardB ? hitRight : hitLeft;

        if (frames != null && frames.Length > 0 && anim != null)
        {
            anim.Play(frames, false, 20f, () =>
            {
                if (anim != null)
                    anim.Play(blink != null && blink.Length > 0 ? blink : new[] { idle }, true, 14f);
            });
        }

        Juice.Shake(0.16f, 0.10f);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 dir = axis == Axis.Vertical ? Vector3.up : Vector3.right;
        Vector3 a = Application.isPlaying ? pointA : transform.position;
        Vector3 b = a + dir * distance;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(a, b);
        Gizmos.DrawWireSphere(a, 0.2f);
        Gizmos.DrawWireSphere(b, 0.2f);
    }
}
