# FrogHop

A 2D pixel-art platformer built in Unity 6 (6000.3.15f1, URP 2D).

Run, jump and bounce your way across a hand-authored stage of gaps, spike heads, saws and
trampolines to reach the trophy — collecting fruit and chasing a rank on the way.

![Unity](https://img.shields.io/badge/Unity-6000.3.15f1-000?logo=unity)
![Pipeline](https://img.shields.io/badge/render-URP%202D-1a7f37)
![License](https://img.shields.io/badge/code-MIT-blue)

---

## Controls

| Input | Action |
|---|---|
| `←` `→` (or `A` `D`) | Move |
| `Space` / `↑` / `W` | Jump — hold longer to jump higher |
| `↓` / `S` | Drop through a one-way platform |
| `Esc` | Pause |
| `R` | Restart |

---

## Coursework requirements

This project began as the **[실습] 2D game** practice and still satisfies every step of it.
The original contract is deliberately intact so the submission remains gradeable:

| Practice step | Where it lives | Kept as specified |
|---|---|---|
| 01 2D 물리엔진 (중력 Rigidbody) | `Character` → Rigidbody2D **Gravity Scale = 2**, Box Collider 2D; Tilemap → Tilemap Collider 2D | ✅ |
| 02 캐릭터 이동 구현 | `Player.cs` — `speed`, arrow keys, `spriteRenderer.flipX` | ✅ |
| 03 점프 구현 | `Player.cs` — `JumpPower`, `Space`, `isJump` | ✅ |
| 04 충돌 이벤트 | `OnCollisionEnter2D` → tag `"Obstacle"` → `HP--`, `Hit_Prefab` spawned and destroyed after 1s | ✅ |
| 05 UI | `Canvas` → `Text (Legacy)` HP label, `GameOver` panel (black α=210, `GAMEOVER` size 130 + Shadow 10/−10, `Retry?`, Button → `Player.Retry_Button()`) | ✅ |
| 06 애니메이션 | `Character.controller` with `isIDLE` / `isRUN` (Bool), `isJUMP` / `isHit` (Trigger); Any State → Hit, Any State → JUMP, no exit time | ✅ |

Public fields keep their original names (`HP`, `speed`, `JumpPower`, `Hit_Prefab`,
`GameOverObj`, `Hp_Text`, `Retry_Button()`), so the Inspector matches the handout.

---

## What was added on top

**Movement feel** — coyote time, jump buffering, variable jump height, acceleration/deceleration
with reduced air control, extra gravity on descent, terminal velocity, squash-and-stretch, and
foot dust. Grounding uses a real `OverlapBox` against a `Ground` layer rather than the
`|velocity.y| < 0.01` test from the handout, which reports "grounded" at the apex of every jump.

**Level** — a 96-tile stage defined as data in `LevelBuilder.cs` and painted through a nine-slice
auto-tiler, with a composite collider so the player never snags on a tile seam.

**Camera** — smooth-damped follow with velocity look-ahead, a vertical dead zone, level-bounds
clamping and shake.

**Hazards & objects** — patrolling spike heads that slam and blink, sliding saws, static spikes,
trampolines, one-way platforms, fruit pickups, checkpoints and a goal trophy.

**Presentation** — parallax sky plus two scaled, tinted terrain-silhouette ridges; pixel-perfect
camera at a 320×180 reference (an exact 6× upscale to 1080p); HUD with hearts, fruit counter and
timer; clear / game-over / pause panels; rank on completion.

**Audio** — the asset pack ships no sound, so every effect and the background loop are
**synthesised in C# at runtime** (`AudioManagerProc.cs`): square/triangle/noise oscillators with
ADSR envelopes and a chiptune bed. No external files, no API keys.

---

## Rebuilding the scene

The scene is generated, not hand-dragged, so it can be reviewed and reproduced:

```
Tools ▸ 2D Game ▸ 0. Build Everything
```

or step by step:

| Menu item | Does |
|---|---|
| `1. Fix Pixel Art Import Settings` | Normalises the art pack to 16 PPU, point filter, no compression; bottom-centre pivots |
| `2. Rebuild Character Animations` | Regenerates IDLE/RUN/JUMP/FALL/Hit clips and `Character.controller` |
| `3. Build Level` | Rebuilds `SampleScene` end to end |
| `4. Set Game View to 1920x1080` | Pins the Game view so Pixel Perfect Camera picks an integer zoom |

> The art pack shipped with mixed 16 and 100 pixels-per-unit settings, which is why the character
> originally rendered many times the size of the terrain. Step 1 is what fixes that class of bug
> at the source instead of compensating with per-object scale.

---

## Project layout

```
Assets/
  Scripts/
    Player.cs              Practice-PDF contract + production movement
    GameManager.cs         Run state, HUD, win/lose/pause
    CameraFollow2D.cs      Look-ahead follow, dead zone, bounds, shake
    AudioManagerProc.cs    Procedurally synthesised SFX and BGM
    Juice.cs               Screen shake and hit-stop
    SpriteAnim.cs          Lightweight frame animator for props
    SpikeHead.cs Saw.cs Trampoline.cs Fruit.cs
    LevelGoal.cs CheckpointFlag.cs OneWayPlatform.cs
    ParallaxLayer.cs ParallaxTransform.cs
    IPlayerInput.cs AutoPilot.cs   Scripted input for the traversal test
  Editor/
    LevelBuilder.cs        The level, as data + a builder
    CharacterAssetBuilder.cs
    PixelArtImportFixer.cs
    GameViewSizeSetup.cs
```

## Verification

The stage is proved completable by `AutoPilot`, which feeds `Player` through `IPlayerInput` and
runs a scripted route end to end — the level was rebuilt several times to fix genuine geometry
faults it exposed (a jump arc that clipped a crate, platforms whose undersides were head-bonked
from below, a trampoline that could not reach its target platform).

Latest clean run: **cleared in 14.2s, 3/3 HP, 8/10 fruit, rank B**, Unity console free of errors
and warnings.

---

## Credits

- Art — [Pixel Adventure 1](https://pixelfrog-assets.itch.io/pixel-adventure-1) by Pixel Frog (CC0)
- Additional props — Cainos *Pixel Art Top Down – Basic*
- Coursework — 동아대학교 Media for Machine Lab, 가상현실 [실습] 2D game

Game code is MIT licensed; bundled art keeps its own licence.
