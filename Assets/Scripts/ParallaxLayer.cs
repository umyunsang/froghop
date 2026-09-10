using UnityEngine;

/// <summary>
/// Depth-faking background layer for a tiled sprite.
///
/// The quad is parked at <c>cam.x * (1 - parallaxFactor)</c> so a factor of 0 pins it to the
/// camera (infinitely far, no relative motion) and 1 leaves it fixed in the world (full motion).
/// It is then nudged by whole tiles to stay centred on the camera, which is invisible because
/// the draw mode repeats, and gives an endless backdrop without touching any material.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[ExecuteAlways]
public class ParallaxLayer : MonoBehaviour
{
    [Tooltip("0 = pinned to the camera (infinitely far), 1 = fixed in the world (full motion).")]
    [Range(0f, 1f)] public float parallaxFactor = 0.25f;

    [Tooltip("Constant drift in world units per second. Gives a static sky some life.")]
    public Vector2 autoScroll = new Vector2(-0.35f, 0f);

    [Tooltip("World size of one texture tile. 64px art at 16 PPU is 4 units.")]
    public float tileWorldSize = 4f;

    [Tooltip("How much larger than the viewport the quad is drawn, so shake never exposes an edge.")]
    public float overscan = 2.6f;

    private SpriteRenderer sr;
    private Camera cam;
    private Vector2 drift;
    private float fittedAspect = -1f;
    private float fittedSize = -1f;

    void OnEnable()
    {
        sr = GetComponent<SpriteRenderer>();
        cam = Camera.main;
        fittedAspect = -1f;   // force a refit; the editor may have cached a different viewport
        Fit();
    }

    void LateUpdate()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }
        if (sr == null) sr = GetComponent<SpriteRenderer>();

        Fit();

        if (Application.isPlaying) drift += autoScroll * Time.deltaTime;

        Vector3 c = cam.transform.position;
        float wantX = c.x * (1f - parallaxFactor) + drift.x;
        float wantY = c.y * (1f - parallaxFactor) + drift.y;

        // Re-centre on the camera in whole-tile steps; tiling makes the jump invisible.
        float tile = Mathf.Max(0.01f, tileWorldSize);
        wantX += Mathf.Round((c.x - wantX) / tile) * tile;
        wantY += Mathf.Round((c.y - wantY) / tile) * tile;

        transform.position = new Vector3(wantX, wantY, transform.position.z);
    }

    private void Fit()
    {
        if (cam == null || sr == null || !cam.orthographic) return;
        // Only rebuild the quad when the viewport actually changed.
        if (Mathf.Approximately(fittedAspect, cam.aspect)
         && Mathf.Approximately(fittedSize, cam.orthographicSize)) return;

        fittedAspect = cam.aspect;
        fittedSize = cam.orthographicSize;

        float h = cam.orthographicSize * 2f * overscan;
        float w = h * cam.aspect;
        float tile = Mathf.Max(0.01f, tileWorldSize);

        sr.drawMode = SpriteDrawMode.Tiled;
        sr.tileMode = SpriteTileMode.Continuous;
        // Snap to whole tiles so the seam never lands mid-tile.
        sr.size = new Vector2(Mathf.Ceil(w / tile) * tile, Mathf.Ceil(h / tile) * tile);
    }
}
