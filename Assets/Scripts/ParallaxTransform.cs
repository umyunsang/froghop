using UnityEngine;

/// <summary>
/// Parallax for a whole object hierarchy (used by the background hill silhouette), as opposed
/// to <see cref="ParallaxLayer"/> which tiles a single sprite across the viewport.
/// </summary>
[ExecuteAlways]
public class ParallaxTransform : MonoBehaviour
{
    [Tooltip("0 = fixed in the world (full parallax), 1 = pinned to the camera (no parallax).")]
    [Range(0f, 1f)] public float followCamera = 0.45f;

    [Tooltip("Vertical parallax is usually weaker than horizontal or the horizon swims.")]
    [Range(0f, 1f)] public float verticalScale = 0.35f;

    private Vector3 origin;
    private Camera cam;
    private bool captured;

    void OnEnable()
    {
        if (!captured) { origin = transform.position; captured = true; }
        cam = Camera.main;
    }

    void LateUpdate()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }
        Vector3 c = cam.transform.position;
        transform.position = new Vector3(
            origin.x + c.x * followCamera,
            origin.y + c.y * followCamera * verticalScale,
            origin.z);
    }
}
