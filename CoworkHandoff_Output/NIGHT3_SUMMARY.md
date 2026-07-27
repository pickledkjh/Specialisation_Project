# NIGHT 3 SUMMARY — Session Conclusion (2026-07-22)

All three requests done and committed. One action needed from you: **re-run the
battlefield** (menu item) to get the denser city — everything else works on the next
Play press. As always: open Unity, let it compile, Console should be clean.

## 1. Battlefield — more buildings

`BattlefieldBuilder.cs` (the editor tool) now builds a much fuller city:

- inner cover ring 8 → **10** houses, outer ring 12 → **18**, backdrop 14 → **22**
- NEW **skyline ring**: 18 houses at ~84u scaled tall (14–28u) so the horizon reads
  as a real city instead of an empty plain
- more street dressing: 10 streetlights, 8 cars, 9 trash piles
- ~77 placed objects total (was ~45); center still completely clear to 22u,
  boundary walls unchanged at 55u

**To apply: `Tools → Battlefield → Build Simple Battlefield` again** (it replaces the
old layout), then Ctrl+S. Different arrangement: change `Seed` at the top of the file.

## 2. Shield move — hold **Q** (with guard-stun punish)

EXVS-style guard, exactly as you asked: block a melee and the ATTACKER gets stunned.

- **Hold Q** to guard: a translucent energy panel appears, your mech is rooted in
  place (facing the enemy as always), and boost drains 8/s while it's up. You can't
  guard while overheated, and you can't attack out of a raised guard — drop it first.
- **Blocks frontal attacks only** (~155° arc, `shieldFrontDot` to tune). Getting hit
  from behind goes through normally — positioning matters.
- **Blocked melee = attacker stunned 1.5 s** (`shieldStunSeconds` on the hitboxes)
  with a clang hit-stop — a guaranteed punish window: drop shield, full combo.
- **Blocked shots** are absorbed silently (no damage, but no stun for the shooter —
  reference behavior; melee is the risky thing to throw at a shield).
- **The AI guards too**: ~40% of its threat reactions now raise the shield instead of
  sidestepping (`shieldChance`). Respect it — swinging into the AI's shield gets YOU
  stunned and punished. Bait it out with a shot, or circle behind.
- Guarding counts as an action: it breaks wake-up protection like everything else.

## 3. Tutorial — press **T** in Play mode

A full guided tutorial that runs inside the battle scene — no separate scene needed,
nothing to set up. A faint "Press T — Tutorial" hint sits bottom-left in Play mode.

- Press **T** to start; the enemy AI goes **passive** (strafes, never attacks) for the
  whole lesson. Press T again anytime to quit back to a real fight.
- **11 steps, each one DETECTS that you actually did it** before advancing:
  movement → rise → boost dash → landing lag (it makes you feel the lag) → boost step
  → shooting → charge shot → melee combos → **rainbow step** (step-cancel mid-combo —
  it genuinely detects the Attacking→BoostStep cancel) → shield → and finally filling
  the knockdown bar for a real knockdown.
- Finishes with a completion panel; the AI wakes back up when you exit.
- All step text lives at the top of `TutorialManager.cs` — edit freely.

## Files touched tonight

| File | Change |
|---|---|
| `Editor/BattlefieldBuilder.cs` | denser rings + skyline |
| `ShieldVisual.cs` | NEW — shared runtime shield panel |
| `TutorialManager.cs` | NEW — tutorial mode + bootstrap |
| `MechCombat.cs` | hold-Q shield, guard rules, block queries |
| `MechController.cs` | movement rooted while guarding |
| `SimpleMechAI.cs` | AI shield reactions, passive tutorial mode |
| `MeleeHitbox.cs` | block resolution — blocked melee stuns the attacker |
| `HomingProjectile.cs` | shields absorb shots |
| `BattleHUD.cs` | font helper shared with the tutorial |

## Morning test list

1. Re-run the battlefield menu item, Ctrl+S, Play.
2. Hold Q — shield panel appears, boost drains, you're rooted. Let the AI melee into
   it: the AI freezes stunned → punish with a full combo.
3. Swing into the AI's shield on purpose — you eat the 1.5 s stun. Fair both ways.
4. Press T — run the full tutorial start to finish (it doubles as a regression test
   of every mechanic we've built).
5. Verdict wanted: shield too strong? The knobs are `shieldBoostDrainPerSec`,
   `shieldStunSeconds`, `shieldChance` (AI), `shieldFrontDot` (arc width).

## Known limits / next up

- No guard animation (the panel + rooted pose carries it); no guard-break mechanic
  yet — EXVS breaks guard when boost empties, which the drain already approximates.
- Tutorial is in-scene rather than a separate scene asset — cleanest with the current
  single-scene setup. A real Tutorial.unity copy is easy to add later if you want a
  menu flow (goes with the menus/match-flow ROADMAP item).
- Still outstanding from before: knockdown/death animations (Mixamo), strafe clips
  for the blend tree, hit VFX/SFX, team cost HUD getters, 2v2. ROADMAP.md order holds.

## Rollback

Every changed file's previous version is in git (`git diff` / `git checkout`), and
feature files are self-contained: delete `TutorialManager.cs` or `ShieldVisual.cs`
(plus the shield blocks in MeleeHitbox/HomingProjectile if you want it fully gone) to
remove a feature; `Tools → Battlefield → Remove Battlefield` clears the city.
