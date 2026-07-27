# MORNING CHECKLIST — Manual Unity Editor Steps

Do these in order. I inspected `Assets/Animation/Player.controller` and the punch
`.fbx` import settings read-only, so items below reflect the **actual current state**
of your project, not guesses. Note: only ONE animator controller exists in Assets
(`Player.controller`) — both mechs appear to share it, so the animator work below
happens once. If the AI secretly uses another controller from a package/scene I
couldn't see, repeat the animator steps there.

## 1. Animator — enable the 8-hit loop  *(confirmed missing)*

In `Player.controller`:

1. Add transition **punch4 → punch1**, condition: trigger **Melee1**, Has Exit Time
   **off**, transition duration small (~0.05–0.1). This is the Melee4→Melee1 loop
   transition — without it the string ends at 4 hits.

## 2. Animator — make step-cancel melee work  *(current setup is broken)*

The existing `Running(Faster) → punch1` transition requires **Melee1 AND DoStep**
together (conditions on one transition are ANDed). DoStep is consumed when the step
starts, so a rainbow-step melee usually can't fire this and the step pose freezes.

2. Edit that transition and **remove the `DoStep` condition**, leaving only `Melee1`.
   (Alternative: add an Any State → punch1 transition on `Melee1`, but then also guard
   against it retriggering from within punch states — the single-condition fix is
   simpler and safer.)

## 3. Animation events — add `EndAttack`  *(confirmed missing on ALL four clips)*

No punch clip currently has an `EndAttack` event, so every swing ends only via the
2.5 s `comboSafetyTimeout` failsafe — combos will feel very sluggish until this is done.

3. For **each of punch1, punch2, punch3, punch4** (select the .fbx → Animation tab →
   Events): add an event named **`EndAttack`** at (or a frame before) the final frame.
   Click Apply. The existing Enable/Disable fist events are already in place and correct
   (punch1/punch3: right fist, punch2: left fist, punch4: left foot).
4. Optional but recommended: **delete the camera events from the clips** —
   `SwitchToPunch1Camera` (punch1) and `SwitchToPunch4Camera` + `SwitchToNormalCamera`
   (punch4). The new code already drives cameras from `RegisterHit`, and with an 8-hit
   loop the punch4 clip event would fire the finisher cinematic mid-string (hit 4) too.

## 4. Animator — knockdown / death states  *(bigger than expected — see FINDINGS §2)*

The controller has **no `HitDown`, `Recover`, or `Die` parameters and no matching
states**, and the Animation folder has no knockdown/get-up/death clips at all. The new
MechHealth will still fly-and-land the mech correctly in code, but nothing plays
visually and Unity logs trigger warnings.

5. Source clips (e.g. Mixamo: "Knocked Down", "Getting Up", "Dying") for: knocked
   down/flying back, lying, getting up, dying.
6. Add trigger parameters `HitDown`, `Recover`, `Die` to the controller.
7. Add states: Any State → **HitDown** (trigger HitDown); HitDown → lying (exit time);
   lying → **GetUp** (trigger Recover) → Locomotion (exit time); Any State → **Die**
   (trigger Die, no exit).

## 5. Inspector — re-verify tuned values  *(renamed fields = defaults came back)*

Renamed serialized fields silently dropped your old scene values. Check on BOTH mechs
where applicable (see TUNING_GUIDE.md for every field):

8. **MechController** — "EXVS Boost Feel" section (dashBurstSpeed 30 / dashCruiseSpeed
   24 / dashBurstSettleTime 0.2 / dashHopImpulse 2 / dashVerticalSettle -2.5 /
   airInertiaDrag 2 / airControlFactor 0.85 / ascendRampSpeed 160) and "Landing Lag"
   (0.12 / 0.7 / 1.8).
9. **MechCombat** — Combo Length (`comboLoops` 2), Knockdown Bar Powers (6 / 30),
   Finisher Launch (16 / 8).
10. **MechHealth** — Knockdown Flight Physics (-22 / 1.1 / 1.6). Also confirm the
    player's `team` is **Team1** and the AI's is Team2 (default is Team2 for both!).
11. Suggested extra tweak: `gravity` is still serialized at -20; try **-24** on both
    MechController and SimpleMechAI for a snappier drop.
12. Sanity-check `MeleeHitbox.damage` (20): an 8-hit string = 160 damage vs 100 HP —
    the finisher will never be reached before the target dies. Suggest damage ~8–10 or
    maxHealth 300+ while testing the full string.

## 6. Play test list

- (a) Held dash **skims the floor** and actually **lands with lag** on release; lag is
  longer when boost is low, very long when overheated.
- (b) Rise (jump held) feels instant.
- (c) Full **8-hit combo** connects while mashing; victim stays in hitstun; final hit
  **launches them flying** and they land before lying down.
- (d) Mid-string **boost step → melee** starts a clean new string with no frozen step
  pose.
- (e) **Nobody ends up hovering** above the ground at any point — during dashes, after
  knockdowns, or while downed.
- (f) (added) Console shows **no "Parameter does not exist" warnings** once step 4 is
  done, and no combat NullReferenceExceptions.

## 7. Housekeeping notes

- The five new scripts were installed as UTF-8 (with BOM) + CRLF. The versions that
  were in `Assets/Scripts` before today are preserved verbatim in
  `CoworkHandoff_Output/Backup_OldScripts/` (they contained mojibake "??"-style
  characters in tooltips from an earlier ANSI-encoded copy — the reinstall fixed that).
- `CoworkHandoff_Output/` sits at the project root, **outside Assets**, so Unity will
  not compile the backups or drafts — that's intentional. Don't move it into Assets.
