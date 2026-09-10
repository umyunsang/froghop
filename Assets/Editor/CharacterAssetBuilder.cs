using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Rebuilds the character animation clips and the Animator Controller from the Ninja Frog
/// sprite sheets.
///
/// The parameter names and the state graph follow the practice PDF exactly
/// (isIDLE / isRUN as bools, isJUMP / isHit as triggers, Any State -> Hit, Any State -> JUMP,
/// no exit time on the movement transitions). FALL is added on top: the PDF has the character
/// stuck in the jump pose all the way down, which reads as a bug once the camera follows.
/// </summary>
public static class CharacterAssetBuilder
{
    private const string Char = "Assets/Pixel Adventure 1/Assets/Main Characters/Ninja Frog/";

    // Pixel Adventure is authored at 20 fps; the run cycle carries a little more urgency.
    private const float IdleFps = 20f;
    private const float RunFps = 24f;
    private const float HitFps = 20f;

    public const string ControllerPath = "Assets/Character.controller";

    [MenuItem("Tools/2D Game/2. Rebuild Character Animations")]
    public static AnimatorController Build()
    {
        AnimationClip idle = MakeClip("Assets/IDLE.anim", Char + "Idle (32x32).png", IdleFps, true);
        AnimationClip run = MakeClip("Assets/RUN.anim", Char + "Run (32x32).png", RunFps, true);
        AnimationClip jump = MakeClip("Assets/JUMP.anim", Char + "Jump (32x32).png", 12f, true);
        AnimationClip fall = MakeClip("Assets/FALL.anim", Char + "Fall (32x32).png", 12f, true);
        AnimationClip hit = MakeClip("Assets/Hit.anim", Char + "Hit (32x32).png", HitFps, false);

        AnimatorController ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        ac.AddParameter("isHit", AnimatorControllerParameterType.Trigger);
        ac.AddParameter("isJUMP", AnimatorControllerParameterType.Trigger);
        ac.AddParameter("isIDLE", AnimatorControllerParameterType.Bool);
        ac.AddParameter("isRUN", AnimatorControllerParameterType.Bool);
        ac.AddParameter("isFALL", AnimatorControllerParameterType.Bool);
        ac.AddParameter("speedY", AnimatorControllerParameterType.Float);

        AnimatorStateMachine sm = ac.layers[0].stateMachine;
        sm.entryPosition = new Vector3(-260f, 0f);
        sm.anyStatePosition = new Vector3(-260f, -160f);

        AnimatorState sIdle = sm.AddState("IDLE", new Vector3(40f, 0f));
        AnimatorState sRun = sm.AddState("RUN", new Vector3(40f, 120f));
        AnimatorState sJump = sm.AddState("JUMP", new Vector3(300f, -120f));
        AnimatorState sFall = sm.AddState("FALL", new Vector3(300f, 0f));
        AnimatorState sHit = sm.AddState("Hit", new Vector3(300f, 140f));

        sIdle.motion = idle;
        sRun.motion = run;
        sJump.motion = jump;
        sFall.motion = fall;
        sHit.motion = hit;

        sm.defaultState = sIdle;

        // Ground locomotion: driven purely by the two bools, instantly.
        Link(sIdle, sRun, ("isIDLE", false), ("isRUN", true));
        Link(sRun, sIdle, ("isIDLE", true), ("isRUN", false));

        // Airborne.
        AnimatorStateTransition anyJump = sm.AddAnyStateTransition(sJump);
        Instant(anyJump);
        anyJump.AddCondition(AnimatorConditionMode.If, 0f, "isJUMP");
        anyJump.canTransitionToSelf = false;

        Link(sJump, sFall, ("isFALL", true));
        Link(sFall, sIdle, ("isIDLE", true));
        Link(sFall, sRun, ("isRUN", true));
        Link(sJump, sIdle, ("isIDLE", true));
        Link(sJump, sRun, ("isRUN", true));

        // Damage: interrupts anything, then falls back to idle when the clip ends.
        AnimatorStateTransition anyHit = sm.AddAnyStateTransition(sHit);
        Instant(anyHit);
        anyHit.AddCondition(AnimatorConditionMode.If, 0f, "isHit");
        anyHit.canTransitionToSelf = false;

        AnimatorStateTransition hitOut = sHit.AddTransition(sIdle);
        hitOut.hasExitTime = true;
        hitOut.exitTime = 0.95f;
        hitOut.duration = 0.05f;

        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        Debug.Log("[CharacterAssetBuilder] Rebuilt IDLE/RUN/JUMP/FALL/Hit + Character.controller.");
        return ac;
    }

    /// <summary>Zero-duration, no-exit-time transition with the given bool conditions.</summary>
    private static void Link(AnimatorState from, AnimatorState to, params (string name, bool value)[] conds)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        Instant(t);
        foreach ((string name, bool value) c in conds)
            t.AddCondition(c.value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, c.name);
    }

    private static void Instant(AnimatorStateTransition t)
    {
        t.hasExitTime = false;
        t.exitTime = 0f;
        t.duration = 0f;
        t.hasFixedDuration = true;
    }

    /// <summary>
    /// Build a sprite-swap clip. Sprite keys are stepped, so the frame rate here is the
    /// literal playback rate rather than something Unity interpolates between.
    /// </summary>
    private static AnimationClip MakeClip(string outPath, string sheetPath, float fps, bool loop)
    {
        Sprite[] frames = PixelArtImportFixer.LoadFrames(sheetPath);
        if (frames.Length == 0)
        {
            Debug.LogError($"[CharacterAssetBuilder] No frames at {sheetPath}");
            return null;
        }

        AnimationClip clip = new AnimationClip();
        clip.frameRate = fps;

        EditorCurveBinding binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = "",
            propertyName = "m_Sprite"
        };

        ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe { time = i / fps, value = frames[i] };
        }
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        AnimationClipSettings s = AnimationUtility.GetAnimationClipSettings(clip);
        s.loopTime = loop;
        // Hold the last frame for one tick so a non-looping clip does not blink.
        s.stopTime = frames.Length / fps;
        AnimationUtility.SetAnimationClipSettings(clip, s);

        AssetDatabase.DeleteAsset(outPath);
        AssetDatabase.CreateAsset(clip, outPath);
        return clip;
    }
}
