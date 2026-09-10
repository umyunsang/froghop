using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the practice-PDF contract intact (HP / speed / JumpPower / GameOverObj / Hp_Text /
/// Hit_Prefab / Retry_Button() / "Obstacle" tag / isIDLE-isRUN-isJUMP-isHit parameters)
/// while layering on the feel a shipping platformer needs: coyote time, jump buffering,
/// variable jump height, real ground checks, i-frames, knockback and squash and stretch.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Player : MonoBehaviour
{
    // ---------- Practice-PDF contract fields (do not rename) ----------
    public int HP = 3;
    public GameObject Hit_Prefab;

    public float speed = 5f;
    public float JumpPower = 7f;

    public GameObject GameOverObj;
    public Text Hp_Text;

    private bool isJump = false;
    private bool isOver = false;

    private Rigidbody2D rigidbody;
    private SpriteRenderer spriteRenderer;
    Animator anim;

    // ---------- Movement feel ----------
    [Header("Movement Feel")]
    [Tooltip("Seconds to reach top speed from a standstill.")]
    public float accelTime = 0.06f;
    [Tooltip("Seconds to come to a stop from top speed.")]
    public float decelTime = 0.05f;
    [Tooltip("Multiplier applied to accel/decel while airborne.")]
    public float airControl = 0.65f;

    [Header("Jump Feel")]
    [Tooltip("Grace period after leaving a ledge during which a jump still registers.")]
    public float coyoteTime = 0.10f;
    [Tooltip("How long a jump press is remembered before landing.")]
    public float jumpBufferTime = 0.12f;
    [Tooltip("Upward velocity is multiplied by this when the jump key is released early.")]
    public float jumpCutMultiplier = 0.45f;
    [Tooltip("Extra gravity while falling so hang time does not feel floaty.")]
    public float fallGravityMultiplier = 1.6f;
    [Tooltip("Terminal velocity.")]
    public float maxFallSpeed = 18f;

    [Header("Ground Check")]
    public LayerMask groundMask;
    public float groundCheckWidth = 0.55f;
    public float groundCheckDepth = 0.10f;

    [Header("Damage")]
    [Tooltip("Invulnerability window after taking a hit.")]
    public float invulnerableTime = 1.1f;
    public float knockbackX = 5f;
    public float knockbackY = 7f;

    [Header("Bounds")]
    [Tooltip("Falling below this Y kills the player.")]
    public float killPlaneY = -14f;

    [Header("Refs")]
    public ParticleSystem dustJump;
    public ParticleSystem dustLand;
    public ParticleSystem dustRun;

    [Header("Debug")]
    [Tooltip("Optional IPlayerInput source. When set, input comes from it instead of the " +
             "keyboard - used by the play-mode traversal test. Leave empty for normal play.")]
    public MonoBehaviour inputOverride;

    // ---------- Internal state ----------
    private float baseGravity;
    private float coyoteCounter;
    private float jumpBufferCounter;
    private float invulnCounter;
    private float knockbackLock;
    private float inputX;
    private bool jumpHeld;
    private bool isGrounded;
    private bool wasGrounded;
    private float velocityXRef;
    private Vector3 baseScale;
    private float squashTimer;
    private float runDustTimer;
    private int startHP;
    private Vector2 squashTarget = Vector2.one;
    private bool externalBounce;

    public bool IsGameOver { get { return isOver; } }
    public bool IsInvulnerable { get { return invulnCounter > 0f; } }

    private void Start()
    {
        rigidbody = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        anim = GetComponent<Animator>();

        rigidbody.freezeRotation = true;
        rigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;
        rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        baseGravity = rigidbody.gravityScale;

        baseScale = transform.localScale;
        startHP = HP;

        if (groundMask.value == 0) groundMask = LayerMask.GetMask("Ground");

        RefreshHpText();
        if (GameManager.Instance != null) GameManager.Instance.RegisterPlayer(this);
    }

    void Update()
    {
        if (isOver == true)
        {
            // Stop simulating once the death fall is well off-screen, rather than integrating
            // a body toward negative infinity for the rest of the session.
            if (rigidbody != null && rigidbody.simulated && transform.position.y < killPlaneY - 8f)
                rigidbody.simulated = false;
            return;
        }

        // ---- Sample input ----
        bool wasJumpHeld = jumpHeld;
        bool jumpKeyDown;

        IPlayerInput src = inputOverride as IPlayerInput;
        if (src != null)
        {
            inputX = Mathf.Clamp(src.Horizontal, -1f, 1f);
            jumpKeyDown = src.JumpDown;
            jumpHeld = src.JumpHeld;
        }
        else
        {
            inputX = 0f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) inputX += 1f;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) inputX -= 1f;

            jumpKeyDown = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow)
                       || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Z);
            jumpHeld = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.UpArrow)
                    || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Z);
        }

        if (jumpKeyDown) jumpBufferCounter = jumpBufferTime;
        else jumpBufferCounter -= Time.deltaTime;

        bool jumpKeyUp = wasJumpHeld && !jumpHeld;
        if (jumpKeyUp && rigidbody != null && rigidbody.linearVelocity.y > 0f)
        {
            // Variable jump height: releasing early cuts the rise short.
            rigidbody.linearVelocity = new Vector2(rigidbody.linearVelocity.x,
                                                   rigidbody.linearVelocity.y * jumpCutMultiplier);
        }

        // ---- Timers ----
        if (invulnCounter > 0f)
        {
            invulnCounter -= Time.deltaTime;
            float blink = Mathf.Repeat(invulnCounter, 0.16f) < 0.08f ? 0.35f : 1f;
            SetAlpha(blink);
            if (invulnCounter <= 0f) SetAlpha(1f);
        }
        if (knockbackLock > 0f) knockbackLock -= Time.deltaTime;

        UpdateFacingAndAnimation();
        UpdateSquash();

        if (transform.position.y < killPlaneY) Die();
    }

    void FixedUpdate()
    {
        if (isOver == true) return;
        if (rigidbody == null) return;

        // ---- Ground check by real overlap, not by |velocity.y| < epsilon ----
        wasGrounded = isGrounded;
        isGrounded = CheckGrounded();

        if (isGrounded)
        {
            coyoteCounter = coyoteTime;
            externalBounce = false;
            if (!wasGrounded && rigidbody.linearVelocity.y <= 0.1f) OnLand();
        }
        else
        {
            coyoteCounter -= Time.fixedDeltaTime;
        }
        isJump = !isGrounded;

        // ---- Horizontal movement with accel / decel ----
        if (knockbackLock <= 0f)
        {
            float target = inputX * speed;
            float smooth = Mathf.Abs(target) > 0.01f ? accelTime : decelTime;
            if (!isGrounded) smooth /= Mathf.Max(0.01f, airControl);
            float vx = Mathf.SmoothDamp(rigidbody.linearVelocity.x, target, ref velocityXRef,
                                        Mathf.Max(0.0001f, smooth));
            rigidbody.linearVelocity = new Vector2(vx, rigidbody.linearVelocity.y);
        }

        // ---- Jump with coyote time and input buffering ----
        if (jumpBufferCounter > 0f && coyoteCounter > 0f && knockbackLock <= 0f)
        {
            DoJump(JumpPower);
            jumpBufferCounter = 0f;
            coyoteCounter = 0f;
        }

        // ---- Weightier fall, capped terminal velocity ----
        if (rigidbody.linearVelocity.y < 0f)
        {
            rigidbody.gravityScale = baseGravity * fallGravityMultiplier;
            externalBounce = false;
        }
        // Releasing jump mid-rise adds weight - but only for a jump the player started.
        // Applying it to a trampoline launch silently robbed the pad of a third of its height.
        else if (rigidbody.linearVelocity.y > 0f && !jumpHeld && !externalBounce)
            rigidbody.gravityScale = baseGravity * 1.25f;
        else
            rigidbody.gravityScale = baseGravity;

        if (rigidbody.linearVelocity.y < -maxFallSpeed)
            rigidbody.linearVelocity = new Vector2(rigidbody.linearVelocity.x, -maxFallSpeed);
    }

    private bool CheckGrounded()
    {
        Vector2 origin = (Vector2)transform.position + Vector2.up * 0.04f;
        Vector2 size = new Vector2(groundCheckWidth, groundCheckDepth);
        Collider2D hit = Physics2D.OverlapBox(origin + Vector2.down * (groundCheckDepth * 0.5f),
                                              size, 0f, groundMask);
        return hit != null && rigidbody.linearVelocity.y < 0.5f;
    }

    private void DoJump(float power)
    {
        if (anim != null) anim.SetTrigger("isJUMP");

        rigidbody.linearVelocity = new Vector2(rigidbody.linearVelocity.x, power);

        isJump = true;
        if (anim != null)
        {
            anim.SetBool("isIDLE", false);
            anim.SetBool("isRUN", false);
        }
        Squash(0.80f, 1.22f);
        if (dustJump != null) dustJump.Play();
        AudioManagerProc.PlayJump();
    }

    /// <summary>Launch the player upward from an external source such as a trampoline.</summary>
    public void Bounce(float power)
    {
        if (isOver) return;
        externalBounce = true;
        rigidbody.linearVelocity = new Vector2(rigidbody.linearVelocity.x, power);
        if (anim != null) anim.SetTrigger("isJUMP");
        isJump = true;
        Squash(0.72f, 1.32f);
        AudioManagerProc.PlayBounce();
    }

    private void OnLand()
    {
        Squash(1.25f, 0.78f);
        if (dustLand != null) dustLand.Play();
        AudioManagerProc.PlayLand();
    }

    private void UpdateFacingAndAnimation()
    {
        if (spriteRenderer != null && Mathf.Abs(inputX) > 0.01f)
            spriteRenderer.flipX = inputX < 0f;

        if (anim == null) return;

        bool moving = Mathf.Abs(inputX) > 0.01f
                   && rigidbody != null && Mathf.Abs(rigidbody.linearVelocity.x) > 0.15f;

        if (isJump == false)
        {
            anim.SetBool("isIDLE", !moving);
            anim.SetBool("isRUN", moving);

            if (moving && dustRun != null)
            {
                runDustTimer -= Time.deltaTime;
                if (runDustTimer <= 0f) { dustRun.Play(); runDustTimer = 0.14f; }
            }
        }
        else
        {
            anim.SetBool("isIDLE", false);
            anim.SetBool("isRUN", false);
        }
        anim.SetBool("isFALL", rigidbody != null && !isGrounded && rigidbody.linearVelocity.y < -0.1f);
        anim.SetFloat("speedY", rigidbody != null ? rigidbody.linearVelocity.y : 0f);
    }

    // ---------- Squash and stretch ----------
    private void Squash(float x, float y) { squashTarget = new Vector2(x, y); squashTimer = 0.16f; }

    private void UpdateSquash()
    {
        Vector3 want = baseScale;
        if (squashTimer > 0f)
        {
            squashTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(squashTimer / 0.16f);
            want = new Vector3(baseScale.x * Mathf.Lerp(1f, squashTarget.x, t),
                               baseScale.y * Mathf.Lerp(1f, squashTarget.y, t),
                               baseScale.z);
        }
        transform.localScale = Vector3.Lerp(transform.localScale, want,
                                            1f - Mathf.Exp(-25f * Time.deltaTime));
    }

    private void SetAlpha(float a)
    {
        if (spriteRenderer == null) return;
        Color c = spriteRenderer.color; c.a = a; spriteRenderer.color = c;
    }

    // ---------- Practice-PDF contract: retry button ----------
    public void Retry_Button()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("SampleScene");
    }

    // ---------- Practice-PDF contract: collision event ----------
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Obstacle"))
            TakeDamage(collision.gameObject.transform.position);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Obstacle")) TakeDamage(other.transform.position);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (other.CompareTag("Obstacle")) TakeDamage(other.transform.position);
    }

    /// <summary>Apply a hit. Ignored while the i-frame window is open.</summary>
    public void TakeDamage(Vector3 source)
    {
        if (isOver || invulnCounter > 0f) return;

        HP--;
        RefreshHpText();

        // Practice-PDF hit effect: spawn and destroy after one second.
        if (Hit_Prefab != null)
        {
            GameObject go = Instantiate(Hit_Prefab, transform.position, Quaternion.identity);
            Destroy(go, 1.0f);
        }

        if (anim != null) anim.SetTrigger("isHit");

        AudioManagerProc.PlayHurt();
        Juice.Shake(0.28f, 0.35f);
        Juice.HitStop(0.07f);

        if (HP <= 0)
        {
            Die();
            return;
        }

        invulnCounter = invulnerableTime;
        knockbackLock = 0.18f;
        float dir = Mathf.Sign(transform.position.x - source.x);
        if (Mathf.Approximately(dir, 0f))
            dir = (spriteRenderer != null && spriteRenderer.flipX) ? 1f : -1f;
        if (rigidbody != null)
            rigidbody.linearVelocity = new Vector2(dir * knockbackX, knockbackY);

        if (GameManager.Instance != null) GameManager.Instance.OnPlayerDamaged(HP);
    }

    private void Die()
    {
        if (isOver) return;
        isOver = true;
        HP = 0;
        RefreshHpText();
        SetAlpha(1f);

        if (rigidbody != null)
        {
            rigidbody.linearVelocity = new Vector2(0f, 6f);
            rigidbody.gravityScale = baseGravity * 1.4f;
            // Fall through the level during the death flourish.
            Collider2D[] cols = GetComponents<Collider2D>();
            for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
        }
        if (anim != null) anim.SetTrigger("isHit");

        AudioManagerProc.PlayDeath();
        Juice.Shake(0.5f, 0.5f);

        if (GameManager.Instance != null) GameManager.Instance.GameOver();
        else if (GameOverObj != null) GameOverObj.SetActive(true);
    }

    private void RefreshHpText()
    {
        if (Hp_Text != null) Hp_Text.text = "HP :" + HP.ToString();
        if (GameManager.Instance != null) GameManager.Instance.OnHpChanged(HP, startHP);
    }

    /// <summary>Freeze input during the level-clear flourish.</summary>
    public void LockControl()
    {
        inputX = 0f;
        jumpBufferCounter = 0f;
        if (rigidbody != null)
            rigidbody.linearVelocity = new Vector2(0f, rigidbody.linearVelocity.y);
        enabled = false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Vector3 o = transform.position + Vector3.up * 0.04f
                  + Vector3.down * (groundCheckDepth * 0.5f);
        Gizmos.DrawWireCube(o, new Vector3(groundCheckWidth, groundCheckDepth, 0f));
    }
}
