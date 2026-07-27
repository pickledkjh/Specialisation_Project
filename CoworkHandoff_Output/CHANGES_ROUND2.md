# CHANGES — Round 2 (2026-07-21)

You reported: far-range punch1 sometimes fails to start the combo, the animation looks
weird, and you asked for better camera + animation control applied directly. Root
causes found and fixed. Everything changed here is backed up in
`Backup_OldScripts/Round2/`.

## What was wrong (the far-punch bug)

Chasing an enemy usually means you're dashing, so the animator is in the
`Running(Faster)` state. Its transition into `punch1` required **Melee1 AND DoStep at
the same time** — DoStep is consumed when the step starts, so the transition almost
never fired. Result: the melee lunge homed in with the mech stuck in the run pose,
which the code then froze (`animator.speed = 0`), giving you a frozen running statue
sliding at the enemy; the punch started late or the string died. Two more compounders:
the code waited for a state named "Melee1" but yours is named "punch1" (the windup
freeze always mistimed), and the punch1 clip fired a `SwitchToPunch1Camera` animation
event on every playthrough — even whiffs — hijacking the camera mid-rush.

## Files changed

### Assets/Animation/Player.controller  *(4 edits)*
1. `Running(Faster) → punch1` now needs **Melee1 only** (DoStep condition removed) —
   melee cancels the dash/step animation instantly. Fixes the far-punch bug.
2. **New transition `punch4 → punch1`** (condition Melee1, no exit time, 0.1s blend) —
   the 8-hit loop now works. This was checklist item 1; done for you.
3. Chain transitions `punch1→2→3→4` switched from wait-for-clip-end to
   **trigger-time transitions** (exit time off, 0.1s blend) — the code's cancel-on-hit
   buffering now actually cancels the moment a hit lands, EXVS-style, instead of
   waiting out each clip.
4. Added trigger parameters **HitDown, Recover, Die** — kills the console warnings from
   MechHealth. (States/clips for them still don't exist — checklist item 4 remains the
   one manual task left.)

### Assets/Animation/punch1-4.fbx.meta  *(events)*
- **`EndAttack` added at 97% of every punch clip** — combos now end properly instead of
  via the 2.5 s failsafe. This was checklist item 3; done for you.
- Removed the camera events from the clips (`SwitchToPunch1Camera` on punch1,
  `SwitchToPunch4Camera`/`SwitchToNormalCamera` on punch4). The code already switches
  cameras on real hits (`RegisterHit`); the clip events fired on whiffs and mid-string
  hit 4, which was part of the "camera does weird things" feel.

### Assets/Scripts/MechCombat.cs  *(2 fixes)*
- Lunge pose-freeze now waits for the actual state name (`punch1`, with `Melee1` also
  accepted) and **skips the freeze entirely if the windup was never reached** — no more
  frozen-pose rushes under any circumstances.
- Each new swing force-disables all hitboxes first — with trigger-time cancels, a
  cancelled clip could skip its Disable-fist event and leave a hitbox on.

### Assets/Scripts/SimpleMechAI.cs  *(3 fixes)*
- Same guarded pose-freeze fix for the AI's rush.
- Same per-swing hitbox clearing.
- The AI's `EndAttack` now only ends its attack state when its combo chain is actually
  finished — without this, the newly added EndAttack events would have limited the AI
  to a single swing per string.

### Assets/Scripts/LockOnBattleCamera.cs + LockOnCameraBootstrap.cs  *(NEW)*
The battle camera from the drafts, installed and made zero-setup:
- On play start, the bootstrap auto-creates a CinemachineCamera at **priority 15** —
  above your normal camera (10), below the punch cinematics (20, raised by MechCombat
  during hits). So: normal cam < battle cam < cinematics, and all existing camera code
  keeps working untouched. **No scene changes were made.**
- EXVS framing: camera stays behind the player relative to the enemy, soft vertical
  damping (dash skims don't pump the frame), long-range pull-back, aim tilts up when a
  mech gets launched, normal follow when there's no target.
- **To turn it off**: delete `LockOnCameraBootstrap.cs` (or set `Enabled = false`
  inside it). The old CameraPivot view is still fully intact underneath.
- To tweak it in the Inspector: create an empty GameObject with CinemachineCamera
  (priority 15) + LockOnBattleCamera in the scene — the bootstrap detects a hand-placed
  one and steps aside. Tuning fields are documented in the script headers and
  `Drafts/WIRING_GUIDE_CAMERA.md`.

## What to check in the editor (2 minutes)

1. Let Unity reimport (the four punch FBX metas changed — you'll see a short import).
2. Console should be clean: no "Parameter does not exist", no compile errors.
3. Play: dash at the enemy from far → melee. The dash anim should snap straight into
   the punch windup, rush in, and connect — no frozen pose.
4. Mash through the full 8-hit string → launcher on hit 8.
5. Camera: pulls behind you facing the enemy; punch cinematics still take over on
   punch1's first hit and the finisher, then hand back.

## Rollback

Copy the matching file(s) from `Backup_OldScripts/Round2/` back over:
- `Player.controller` → `Assets/Animation/`
- `punch1-4.fbx.meta` → `Assets/Animation/`
- `MechCombat.cs`, `SimpleMechAI.cs` → `Assets/Scripts/`
- Delete `LockOnBattleCamera.cs` + `LockOnCameraBootstrap.cs` to remove the camera.

## Still manual (unchanged from MORNING_CHECKLIST)

- Knockdown/death **clips and states** (checklist item 4) — needs animation files that
  don't exist in the project yet (Mixamo: Knocked Down / Getting Up / Dying).
- Inspector value verification (checklist item 5) — especially `MechHealth.team` on
  the player (Team1!) and the damage-vs-HP math (item 12: 20 dmg × 8 hits vs 100 HP).
