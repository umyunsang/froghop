using UnityEngine;

/// <summary>
/// Collectible fruit. Bobs in place, plays the shared "Collected" burst on pickup,
/// then removes itself.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class Fruit : MonoBehaviour
{
    public Sprite[] idleFrames;
    public Sprite[] collectedFrames;
    public float fps = 20f;

    [Header("Idle Motion")]
    public float bobAmplitude = 0.10f;
    public float bobSpeed = 2.4f;

    private SpriteAnim anim;
    private Collider2D col;
    private Vector3 basePos;
    private float phase;
    private bool taken;

    void Start()
    {
        anim = GetComponent<SpriteAnim>();
        if (anim == null) anim = gameObject.AddComponent<SpriteAnim>();
        col = GetComponent<Collider2D>();
        basePos = transform.position;
        phase = Random.Range(0f, Mathf.PI * 2f);

        if (idleFrames != null && idleFrames.Length > 0) anim.Play(idleFrames, true, fps);
        if (GameManager.Instance != null) GameManager.Instance.RegisterFruit();
    }

    void Update()
    {
        if (taken) return;
        phase += bobSpeed * Time.deltaTime;
        transform.position = basePos + Vector3.up * (Mathf.Sin(phase) * bobAmplitude);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (taken) return;
        if (other.GetComponent<Player>() == null) return;

        taken = true;
        if (col != null) col.enabled = false;

        AudioManagerProc.PlayCollect();
        if (GameManager.Instance != null) GameManager.Instance.CollectFruit();

        if (collectedFrames != null && collectedFrames.Length > 0 && anim != null)
            anim.Play(collectedFrames, false, fps, () => Destroy(gameObject));
        else
            Destroy(gameObject);
    }
}
