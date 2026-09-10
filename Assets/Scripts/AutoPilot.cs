using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Drives the player from a script so the level can be proved traversable without a human at
/// the keyboard. Legacy <c>Input</c> cannot be injected, so this feeds <see cref="Player"/>
/// through <see cref="IPlayerInput"/> instead.
///
/// Command syntax, whitespace separated:
///   R&lt;x&gt;      run right until x &gt;= value
///   L&lt;x&gt;      run left  until x &lt;= value
///   J&lt;t&gt;      jump, holding for t seconds (longer hold = higher jump)
///   W&lt;t&gt;      wait t seconds, no input
///   S         stop and stand still until the next command
/// Example: "R17 J0.4 R26 W0.6 R38"
/// </summary>
public class AutoPilot : MonoBehaviour, IPlayerInput
{
    public string script = "";
    public float commandTimeout = 12f;
    public bool Done { get; private set; }
    public bool Failed { get; private set; }
    public string Log { get { return log.ToString(); } }

    private readonly StringBuilder log = new StringBuilder();
    private readonly List<string> commands = new List<string>();
    private int cursor;
    private float horizontal;
    private bool jumpDown;
    private bool jumpHeld;
    private float jumpHoldRemaining;
    private float waitRemaining;
    private float commandElapsed;
    private Transform body;

    public float Horizontal { get { return horizontal; } }

    /// <summary>
    /// Consume-on-read. Script execution order between this component and <see cref="Player"/>
    /// is undefined, so a flag that is merely cleared at the top of the next Update can be
    /// missed entirely; latching until someone reads it makes the jump order-independent.
    /// </summary>
    public bool JumpDown
    {
        get { if (!jumpDown) return false; jumpDown = false; return true; }
    }

    public bool JumpHeld { get { return jumpHeld; } }

    public void Begin(string commandScript)
    {
        script = commandScript;
        commands.Clear();
        commands.AddRange(script.Split(new[] { ' ', '\n', '\t' },
                                       System.StringSplitOptions.RemoveEmptyEntries));
        cursor = 0;
        Done = false;
        Failed = false;
        commandElapsed = 0f;
        log.Length = 0;
        body = transform;
    }

    void Update()
    {
        if (jumpHoldRemaining > 0f)
        {
            jumpHoldRemaining -= Time.deltaTime;
            jumpHeld = jumpHoldRemaining > 0f;
        }
        else jumpHeld = false;

        if (Done || commands.Count == 0) { horizontal = 0f; return; }

        if (waitRemaining > 0f)
        {
            waitRemaining -= Time.deltaTime;
            horizontal = 0f;
            if (waitRemaining > 0f) return;
            Advance("wait done");
            return;
        }

        if (cursor >= commands.Count)
        {
            horizontal = 0f;
            Done = true;
            log.Append("[complete] x=").Append(body.position.x.ToString("0.0")).Append('\n');
            return;
        }

        string cmd = commands[cursor];
        commandElapsed += Time.deltaTime;
        if (commandElapsed > commandTimeout)
        {
            Failed = true;
            Done = true;
            log.Append("[TIMEOUT] on '").Append(cmd).Append("' at x=")
               .Append(body.position.x.ToString("0.0"))
               .Append(" y=").Append(body.position.y.ToString("0.0")).Append('\n');
            horizontal = 0f;
            return;
        }

        char kind = cmd[0];
        float arg;
        float.TryParse(cmd.Substring(1), out arg);

        switch (kind)
        {
            case 'R':
                horizontal = 1f;
                if (body.position.x >= arg) Advance("reached x=" + arg);
                break;
            case 'L':
                horizontal = -1f;
                if (body.position.x <= arg) Advance("reached x=" + arg);
                break;
            case 'J':
                jumpDown = true;
                jumpHoldRemaining = Mathf.Max(0.02f, arg);
                jumpHeld = true;
                Advance("jump hold " + arg);
                break;
            case 'W':
                waitRemaining = arg;
                horizontal = 0f;
                break;
            case 'S':
                horizontal = 0f;
                Advance("stop");
                break;
            default:
                Advance("unknown '" + cmd + "'");
                break;
        }
    }

    private void Advance(string note)
    {
        log.Append(cursor < commands.Count ? commands[cursor] : "?")
           .Append(" -> ").Append(note)
           .Append("  @(").Append(body.position.x.ToString("0.0")).Append(", ")
           .Append(body.position.y.ToString("0.0")).Append(")\n");
        cursor++;
        commandElapsed = 0f;
    }
}
