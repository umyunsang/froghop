using UnityEngine;

/// <summary>
/// A spinning saw that slides back and forth along a track. Tagged "Obstacle".
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class Saw : MonoBehaviour
{
    public enum Axis { Horizontal, Vertical }

    [Header("Patrol")]
    public Axis axis = Axis.Horizontal;
    public float distance = 5f;
    public float speed = 2.6f;
    [Tooltip("Ease in and out at the ends of the track instead of snapping direction.")]
    public bool easeEnds = true;

    [Header("Look")]
    public float spinSpeed = -520f;
    public Sprite[] frames;
    public float fps = 20f;

    private Vector3 origin;
    private float t;

    void Start()
    {
        origin = transform.position;
        t = Random.Range(0f, Mathf.PI * 2f);

        if (frames != null && frames.Length > 1)
        {
            SpriteAnim a = GetComponent<SpriteAnim>();
            if (a == null) a = gameObject.AddComponent<SpriteAnim>();
            a.Play(frames, true, fps);
        }
    }

    void Update()
    {
        t += (speed / Mathf.Max(0.01f, distance)) * Time.deltaTime * Mathf.PI;

        // A sine sweep gives free ease-in/ease-out; a triangle wave gives constant speed.
        float u = easeEnds ? (Mathf.Sin(t) * 0.5f + 0.5f)
                           : Mathf.PingPong(t / Mathf.PI, 1f);

        Vector3 dir = axis == Axis.Horizontal ? Vector3.right : Vector3.up;
        transform.position = origin + dir * (u * distance);
        transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 dir = axis == Axis.Horizontal ? Vector3.right : Vector3.up;
        Vector3 a = Application.isPlaying ? origin : transform.position;
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawLine(a, a + dir * distance);
    }
}
