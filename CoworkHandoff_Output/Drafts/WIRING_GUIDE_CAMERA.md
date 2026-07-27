# WIRING GUIDE — Lock-On Battle Camera (Draft)

File: `LockOnBattleCamera.cs` (in `Drafts/`, NOT compiled). Nothing live was modified.

## What it does

EXVS framing: the camera rides a rig that stays **behind the player relative to the
enemy**, so the enemy is always ahead in frame and strafing orbits the enemy visually.
Vertical follow is softer than horizontal (dash skims/hops don't pump the frame), it
pulls back gradually past 15 u so long-range fights keep both mechs framed, and with no
target it degrades to a normal third-person follow behind the player's facing.

## Scene setup (Cinemachine 3.x — the project already uses Unity.Cinemachine)

1. Copy `LockOnBattleCamera.cs` from `Drafts/` into `Assets/Scripts/`.
2. Create an empty GameObject **`LockOnCameraRig`** at the player's position. Add the
   `LockOnBattleCamera` component:
   - `player` = player mech root
   - `targetManager` = the player's TargetManager (auto-found if on the player root)
   - leave `enemyOverride` empty (it's a debug/forcing hook)
3. Create a new **CinemachineCamera** (`CM_LockOnBattle`):
   - **Follow** = `LockOnCameraRig`
   - **LookAt** = the rig auto-creates a child aim anchor at runtime; easiest is:
     Position Control = **Follow** with zero/near-zero damping, Rotation Control =
     **Rotate With Follow Target** (the rig already faces the aim point). Then LookAt
     can stay empty. Alternative: assign LookAt at runtime — `vcam.LookAt =
     rig.AimAnchor;` from a one-line bootstrap, and use Hard Look At.
   - Set its `vcam` field on the rig (optional reference, used for nothing critical in
     the draft).
   - Keep the vcam's own damping ~0 — the rig does all smoothing; stacking both makes
     the camera feel drunk.
4. **Priority scheme** (matches what MechCombat already does):
   - `CM_LockOnBattle` priority **10** — the everyday battle camera.
   - `punch1Camera` / `punch4Camera` stay at **5** idle and jump to **20** during
     cinematics (MechCombat.SwitchToPunch1/4Camera already does exactly this).
   - Result: cinematics override the battle cam automatically, and
     `SwitchToNormalCamera()` (priority back to 5) hands control straight back. **No
     code changes needed in MechCombat.**

## Coexisting with (or replacing) CameraPivot.cs

`CameraPivot` is a much simpler version of the same idea: it snaps a pivot to the
player and yaws it toward the enemy; the main camera presumably hangs off that pivot
(directly or via a vcam following the pivot).

- **Both scripts fight if both drive the live camera** — they'll disagree every
  LateUpdate. Pick one at a time:
  - **Testing**: leave CameraPivot's GameObject enabled but give `CM_LockOnBattle`
    higher priority than whatever vcam uses the pivot (or disable that vcam). Toggle
    between them by flipping priorities — instant A/B comparison in play mode.
  - **Adopting**: disable/remove the `CameraPivot` component (owner's call — per
    handoff permissions this draft doesn't touch it). Keep the file until you're sure.
- What you gain over CameraPivot: vertical handling (CameraPivot flattens Y, so
  launched mechs leave frame — the new finisher makes this matter), range-based
  pull-back, no-target fallback, and Cinemachine blending into the punch cinematics
  instead of a hard switch.

## Interaction with the punch1/punch4 cinematic cameras

- MechCombat raises `punch1Camera`/`punch4Camera` to priority 20 on the relevant hits
  and drops them to 5 in `SwitchToNormalCamera()` (end lag / cancel). As long as
  `CM_LockOnBattle` sits between (priority 10), Cinemachine Brain blends: battle cam →
  cinematic → back. Set the Brain's Default Blend to ~0.3–0.5 s EaseInOut for the EXVS
  "impact zoom" feel.
- The punch cameras are scene cameras the owner placed; they're untouched by the draft.
- Heads-up from FINDINGS §2.6: the punch1/punch4 **clips** also fire camera-switch
  animation events every playthrough (including mid-string in an 8-hit combo). Once the
  battle camera is in, you'll notice these extra switches more — deleting the clip
  events (checklist item 3.4) fixes it since the code path already handles cinematics.

## Tuning quick reference

| Field | Default | Feel |
|---|---|---|
| baseDistance / height | 7 / 3 | how far behind / above the player the camera sits |
| pullbackPerUnit / nearRange / maxExtraPullback | 0.06 / 15 / 5 | long-range framing: extra pull-back beyond 15 u, capped at +5 |
| horizontalDamping | 6 | how fast the camera swings behind the player (4–8 weighty, 12+ twitchy) |
| verticalDamping | 3 | softer = dash hops don't bounce the frame |
| aimDamping | 8 | how fast the look-at point catches up |
| aimBias | 0.35 | 0 = frame the player, 1 = frame the enemy; 0.35 keeps player low in frame, enemy ahead |
| aimHeight | 1.5 | chest-height framing |
| freeFollowDistance / freeFollowHeight | 6 / 2.5 | no-target fallback follow |

## Known limitations (drafts stay minimal)

- No occlusion handling (walls between camera and mechs) — add a CinemachineDeoccluder
  extension on the vcam later if the arena gets cover.
- No target-switch snap smoothing beyond the normal damping — fine at 1v1; revisit for
  2v2 target switching (ROADMAP).
- The rig assumes a single local player.
