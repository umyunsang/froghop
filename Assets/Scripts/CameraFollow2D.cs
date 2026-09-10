using UnityEngine;

/// <summary>
/// Side-scroller camera: smooth-damped follow with velocity look-ahead, a vertical dead zone
/// so small hops do not pump the view, level-bounds clamping, and shake from <see cref="Juice"/>.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow2D : MonoBehaviour
{
    public Transform target;

    [Header("Follow")]
    public float smoothTimeX = 0.16f;
    public float smoothTimeY = 0.26f;
    public Vector2 offset = new Vector2(0f, 1.2f);

    [Tooltip("How far ahead of the player the camera leads, per unit/sec of speed.")]
    public float lookAhead = 0.22f;
    public float lookAheadMax = 3.0f;
    public float lookAheadSmooth = 0.35f;

    [Tooltip("Vertical movement inside this band does not move the camera.")]
    public float verticalDeadZone = 1.4f;
    [Tooltip("While falling fast the dead zone is ignored so the landing stays visible.")]
    public float fallSnapSpeed = -9f;

    [Header("Level Bounds")]
    public bool useBounds = true;
    public Vector2 boundsMin = new Vector2(0f, -6f);
    public Vector2 boundsMax = new Vector2(90f, 20f);

    private Camera cam;
    private Vector3 velocity;
    private float lookAheadCurrent;
    private float lookAheadVel;
    private float anchorY;
    private Rigidbody2D targetBody;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.orthographic = true;
    }

    void Start()
    {
        if (target == null)
        {
            Player p = FindFirstObjectByType<Player>();
            if (p != null) target = p.transform;
        }
        if (target != null)
        {
            targetBody = target.GetComponent<Rigidbody2D>();
            anchorY = target.position.y;
            transform.position = Clamp(new Vector3(target.position.x + offset.x,
                                                   anchorY + offset.y,
                                                   transform.position.z));
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        float vx = targetBody != null ? targetBody.linearVelocity.x : 0f;
        float vy = targetBody != null ? targetBody.linearVelocity.y : 0f;

        // Lead the camera in the direction of travel.
        float wantLookAhead = Mathf.Clamp(vx * lookAhead, -lookAheadMax, lookAheadMax);
        lookAheadCurrent = Mathf.SmoothDamp(lookAheadCurrent, wantLookAhead,
                                            ref lookAheadVel, lookAheadSmooth);

        // Vertical dead zone: only re-anchor once the player leaves the band,
        // or immediately when they are falling fast.
        float dy = target.position.y - anchorY;
        if (vy < fallSnapSpeed) anchorY = target.position.y;
        else if (dy > verticalDeadZone) anchorY = target.position.y - verticalDeadZone;
        else if (dy < -verticalDeadZone) anchorY = target.position.y + verticalDeadZone;

        Vector3 desired = new Vector3(target.position.x + offset.x + lookAheadCurrent,
                                      anchorY + offset.y,
                                      transform.position.z);

        Vector3 next = transform.position;
        next.x = Mathf.SmoothDamp(next.x, desired.x, ref velocity.x, smoothTimeX);
        next.y = Mathf.SmoothDamp(next.y, desired.y, ref velocity.y, smoothTimeY);

        next = Clamp(next);

        Vector2 shake = Juice.ShakeOffset;
        transform.position = new Vector3(next.x + shake.x, next.y + shake.y, next.z);
    }

    private Vector3 Clamp(Vector3 p)
    {
        if (!useBounds || cam == null) return p;
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;

        float minX = boundsMin.x + halfW;
        float maxX = boundsMax.x - halfW;
        float minY = boundsMin.y + halfH;
        float maxY = boundsMax.y - halfH;

        // A level narrower than the viewport centres instead of clamping to a bad range.
        p.x = minX > maxX ? (boundsMin.x + boundsMax.x) * 0.5f : Mathf.Clamp(p.x, minX, maxX);
        p.y = minY > maxY ? (boundsMin.y + boundsMax.y) * 0.5f : Mathf.Clamp(p.y, minY, maxY);
        return p;
    }

    void OnDrawGizmosSelected()
    {
        if (!useBounds) return;
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.6f);
        Vector3 c = new Vector3((boundsMin.x + boundsMax.x) * 0.5f, (boundsMin.y + boundsMax.y) * 0.5f, 0f);
        Gizmos.DrawWireCube(c, new Vector3(boundsMax.x - boundsMin.x, boundsMax.y - boundsMin.y, 0.1f));
    }
}
