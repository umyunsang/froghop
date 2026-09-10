using System.Collections;
using UnityEngine;

/// <summary>
/// Jump up through it, stand on top, press Down to drop through.
/// </summary>
[RequireComponent(typeof(PlatformEffector2D))]
[RequireComponent(typeof(Collider2D))]
public class OneWayPlatform : MonoBehaviour
{
    public float dropThroughTime = 0.35f;

    private Collider2D col;
    private bool playerOn;
    private bool dropping;

    void Awake()
    {
        col = GetComponent<Collider2D>();
        col.usedByEffector = true;

        PlatformEffector2D fx = GetComponent<PlatformEffector2D>();
        fx.useOneWay = true;
        fx.surfaceArc = 150f;
        fx.rotationalOffset = 0f;
    }

    void Update()
    {
        if (!playerOn || dropping) return;
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            StartCoroutine(DropThrough());
    }

    private IEnumerator DropThrough()
    {
        dropping = true;
        col.enabled = false;
        yield return new WaitForSeconds(dropThroughTime);
        col.enabled = true;
        dropping = false;
    }

    void OnCollisionEnter2D(Collision2D c)
    {
        if (c.collider.GetComponent<Player>() != null) playerOn = true;
    }

    void OnCollisionExit2D(Collision2D c)
    {
        if (c.collider.GetComponent<Player>() != null) playerOn = false;
    }
}
