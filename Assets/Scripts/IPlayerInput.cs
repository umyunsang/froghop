/// <summary>
/// Input source for <see cref="Player"/>. Implemented by the play-mode traversal test so the
/// level can be driven and verified without a human at the keyboard.
/// </summary>
public interface IPlayerInput
{
    /// <summary>-1 left, 0 idle, +1 right.</summary>
    float Horizontal { get; }

    /// <summary>True on the single frame the jump is pressed.</summary>
    bool JumpDown { get; }

    /// <summary>True for as long as the jump is held (drives variable jump height).</summary>
    bool JumpHeld { get; }
}
