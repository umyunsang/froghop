using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns run state (health, fruit, clock), the HUD, and the win/lose/pause flow.
/// The practice-PDF objects are still here and still work: <see cref="hpText"/> is the
/// legacy HP label and <see cref="gameOverObj"/> is the GameOver panel the Player toggles.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum State { Playing, Paused, GameOver, Cleared }
    public State CurrentState { get; private set; }

    [Header("HUD - practice PDF")]
    public Text hpText;
    public GameObject gameOverObj;

    [Header("HUD - added")]
    public Image[] hearts;
    public Sprite heartFull;
    public Sprite heartEmpty;
    public Text fruitText;
    public Text timerText;
    public Text toastText;

    [Header("Panels")]
    public GameObject clearPanel;
    public Text clearStatsText;
    public GameObject pausePanel;
    public Image fadeImage;

    [Header("Tuning")]
    public float fadeInTime = 0.6f;
    public float toastTime = 1.6f;

    private Player player;
    private int fruitTotal;
    private int fruitTaken;
    private float elapsed;
    private float toastTimer;
    private int maxHp = 3;

    public int FruitTaken { get { return fruitTaken; } }
    public int FruitTotal { get { return fruitTotal; } }
    public float Elapsed { get { return elapsed; } }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Time.timeScale = 1f;
        CurrentState = State.Playing;
    }

    void Start()
    {
        if (gameOverObj != null) gameOverObj.SetActive(false);
        if (clearPanel != null) clearPanel.SetActive(false);
        if (pausePanel != null) pausePanel.SetActive(false);
        if (toastText != null) toastText.text = "";

        RefreshFruitLabel();
        StartCoroutine(FadeIn());
    }

    void Update()
    {
        if (CurrentState == State.Playing)
        {
            elapsed += Time.deltaTime;
            if (timerText != null) timerText.text = FormatTime(elapsed);
        }

        if (Input.GetKeyDown(KeyCode.Escape)) TogglePause();

        // R restarts from anywhere, which is what players reach for after a death.
        if (Input.GetKeyDown(KeyCode.R) && CurrentState != State.Paused) Restart();

        if (toastTimer > 0f)
        {
            toastTimer -= Time.unscaledDeltaTime;
            if (toastText != null)
            {
                Color c = toastText.color;
                c.a = Mathf.Clamp01(toastTimer / 0.5f);
                toastText.color = c;
            }
            if (toastTimer <= 0f && toastText != null) toastText.text = "";
        }
    }

    // ------------------------------------------------------------------ registration

    public void RegisterPlayer(Player p)
    {
        player = p;
        maxHp = Mathf.Max(1, p.HP);
        OnHpChanged(p.HP, maxHp);
        if (gameOverObj != null && p.GameOverObj == null) p.GameOverObj = gameOverObj;
        if (hpText != null && p.Hp_Text == null) p.Hp_Text = hpText;
    }

    public void RegisterFruit() { fruitTotal++; RefreshFruitLabel(); }

    // ------------------------------------------------------------------ events

    public void CollectFruit()
    {
        fruitTaken++;
        RefreshFruitLabel();
        if (fruitTaken == fruitTotal && fruitTotal > 0) Toast("ALL FRUIT COLLECTED!");
    }

    public void OnHpChanged(int hp, int max)
    {
        maxHp = Mathf.Max(maxHp, max);
        if (hearts == null) return;
        for (int i = 0; i < hearts.Length; i++)
        {
            if (hearts[i] == null) continue;
            bool used = i < maxHp;
            hearts[i].enabled = used;
            if (!used) continue;
            hearts[i].sprite = i < hp ? heartFull : heartEmpty;
            hearts[i].color = i < hp ? Color.white : new Color(1f, 1f, 1f, 0.28f);
        }
    }

    public void OnPlayerDamaged(int hpLeft)
    {
        Toast(hpLeft == 1 ? "LAST CHANCE!" : "OUCH!");
    }

    public void GameOver()
    {
        if (CurrentState == State.GameOver || CurrentState == State.Cleared) return;
        CurrentState = State.GameOver;
        AudioManagerProc.DuckBgm(0.05f);
        StartCoroutine(ShowGameOver());
    }

    public void LevelClear()
    {
        if (CurrentState == State.Cleared || CurrentState == State.GameOver) return;
        CurrentState = State.Cleared;

        if (player != null) player.LockControl();
        AudioManagerProc.PlayClear();
        AudioManagerProc.DuckBgm(0.06f);

        if (clearStatsText != null)
        {
            string rank = Rank();
            clearStatsText.text =
                "TIME   " + FormatTime(elapsed) + "\n" +
                "FRUIT  " + fruitTaken + " / " + fruitTotal + "\n" +
                "RANK   " + rank;
        }
        StartCoroutine(ShowAfterDelay(clearPanel, 0.7f));
    }

    // ------------------------------------------------------------------ flow

    public void TogglePause()
    {
        if (CurrentState == State.GameOver || CurrentState == State.Cleared) return;

        if (CurrentState == State.Playing)
        {
            CurrentState = State.Paused;
            Time.timeScale = 0f;
            if (pausePanel != null) pausePanel.SetActive(true);
        }
        else if (CurrentState == State.Paused)
        {
            CurrentState = State.Playing;
            Time.timeScale = 1f;
            if (pausePanel != null) pausePanel.SetActive(false);
        }
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void Toast(string message)
    {
        if (toastText == null) return;
        toastText.text = message;
        toastText.color = new Color(toastText.color.r, toastText.color.g, toastText.color.b, 1f);
        toastTimer = toastTime;
    }

    // ------------------------------------------------------------------ helpers

    private string Rank()
    {
        bool allFruit = fruitTotal > 0 && fruitTaken >= fruitTotal;
        bool noDamage = player != null && player.HP >= maxHp;
        if (allFruit && noDamage && elapsed < 45f) return "S";
        if (allFruit && noDamage) return "A";
        if (allFruit || noDamage) return "B";
        return "C";
    }

    private void RefreshFruitLabel()
    {
        if (fruitText != null) fruitText.text = fruitTaken + " / " + fruitTotal;
    }

    private static string FormatTime(float t)
    {
        int m = Mathf.FloorToInt(t / 60f);
        int s = Mathf.FloorToInt(t % 60f);
        int cs = Mathf.FloorToInt((t * 100f) % 100f);
        return string.Format("{0:00}:{1:00}.{2:00}", m, s, cs);
    }

    private IEnumerator ShowGameOver()
    {
        yield return new WaitForSeconds(0.9f);
        if (gameOverObj != null) gameOverObj.SetActive(true);
    }

    private IEnumerator ShowAfterDelay(GameObject go, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (go != null) go.SetActive(true);
    }

    private IEnumerator FadeIn()
    {
        if (fadeImage == null) yield break;
        fadeImage.gameObject.SetActive(true);
        float t = fadeInTime;
        while (t > 0f)
        {
            t -= Time.unscaledDeltaTime;
            Color c = fadeImage.color;
            c.a = Mathf.Clamp01(t / fadeInTime);
            fadeImage.color = c;
            yield return null;
        }
        fadeImage.gameObject.SetActive(false);
    }
}
