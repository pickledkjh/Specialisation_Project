# ROADMAP — Gap Analysis vs Gundam EXVS / Starward

Prioritized by (impact on "does this feel like EXVS") × (how unblocked it is in this
codebase today). Each item: what's missing, and what building it here involves.

## P0 — the game doesn't read as EXVS without these

### 1. Shooting (projectiles, homing, ammo)
The defining half of EXVS combat is absent: `MechShooter` raises the arm and nothing
comes out. **Drafted** — `Drafts/HomingProjectile.cs` + `Drafts/MechShooterV2.cs` +
wiring guide. In this codebase it's a contained job: MechCombat already has the fire
input, the yellow-lock gate, and `redLockRange`; MechHealth.TakeDamage already accepts
(damage, knockdownPower). Remaining work after the drafts: prefab + muzzle setup, shot
flinch on the victim, AI usage (steps written in the MechShooterV2 header), and later
beam VFX. Estimate: half a day to first working shot, using the guides.

### 2. Knockdown/death animations actually playing
Not an EXVS-gap in the design but a real one on screen: the animator has no HitDown /
Recover / Die states or parameters, and no such clips exist in Assets (FINDINGS §2).
The new MechHealth flight physics works, but the mech never visually lies down or dies.
Involves: sourcing 3–4 clips (Mixamo), three trigger parameters, four states — an
hour in the editor, no code. Checklist item 4 has the exact wiring.

### 3. Lock-on battle camera
`CameraPivot` flattens Y, so the new launcher literally sends the victim out of frame —
the best moment in the game is invisible. **Drafted** — `Drafts/LockOnBattleCamera.cs`
sits at priority 10 under the existing punch cinematics (20) with no MechCombat
changes. Remaining: scene wiring per the guide and taste-tuning damping. Estimate:
1–2 hours including A/B against CameraPivot.

## P1 — the core loop is there but shallow without these

### 4. HUD (boost gauge, ammo, health, team cost)
All four values exist and are public (`BoostManager.currentBoost/maxBoost`,
`MechShooterV2.currentAmmo`, `MechHealth.currentHealth`, CostManager's pools — the
pools are private but serialized; add two getters). The scene already has a Canvas
(LockOnUI / TargetManager reticles), so this is pure uGUI work: a radial or segmented
boost bar (EXVS overheat flash when `isOverheated`), ammo counter that greys during
reload, HP bars for both mechs, cost display per team. No architecture in the way;
biggest cost is layout taste. Great first task for a morning.

### 5. Hit VFX / SFX feedback
Hit-stop and camera impulse exist (already very EXVS), but there's no spark on hit, no
boost trail, no swing whoosh, no beam sound. In this codebase: spawn points are obvious
(MeleeHitbox.OnTriggerEnter for hit sparks, dash state entry for boost trails,
HomingProjectile impact for beam hits). A handful of particle prefabs + one
`AudioSource.PlayClipAtPoint` helper goes a long way. Cheap, huge feel gain.

### 6. Arena boundaries
Nothing stops either mech leaving the fight space; a whiffed homing rush travels 40+ u.
EXVS uses a hard cylindrical wall. Simplest here: an invisible collider ring the
CharacterControllers can't pass (works with zero code), plus a soft rubber-band in
MechController/SimpleMechAI clamping position radius as a fallback. Also clamp the
knockdown flight in MechHealth's loop so a launcher can't fling a mech through the
wall (it uses cc.Move, so colliders already stop it — just make the wall tall).

## P2 — real EXVS structure

### 7. Target switching & true lock-on flow
TargetManager holds a single `currentTarget` with no way to change it and no range
logic — green/red is derived elsewhere (twice, inconsistently: LockOnUI flattens Y,
MechCombat doesn't — FINDINGS §3). Involves: a target list (all enemy MechHealth in
scene), a switch-target input (EXVS's most-used button), choosing nearest-to-crosshair
on switch, and centralizing red-lock range checks in one place that LockOnUI,
MechCombat and MechShooterV2 all read. Prereq for 2v2.

### 8. Sub-weapons & charge shot
EXVS depth comes from the weapon kit: sub-shot (e.g. bazooka/funnels), charge shot,
melee-button variants (side/back melee). The codebase hook points are clean —
MechCombat.HandleInputs is the single input funnel; MechShooterV2's ammo pattern
copy-pastes per weapon. Involves: an input scheme decision (hold-to-charge on the shoot
button fits current bindings), a small WeaponSlot abstraction if more than two weapons,
and per-weapon projectile prefabs. Build after 4 & 5 so new weapons get HUD + feedback.

### 9. 2v2 team battle
The cost system (6000/2000) is already EXVS-shaped and team-aware, but there's no
respawn (FINDINGS §3: first death currently soft-ends the match), only one AI, and all
targeting is 1v1. Involves: a spawn manager that respawns a dead mech (reset
MechHealth/position, deduct cost — CostManager already does the deduction), instancing
a second pair of mechs, SimpleMechAI target selection (currently hardwired to
`playerTarget`), and item 7 for switching. Also the win/lose flow when a pool empties
(CheckWinCondition currently only logs). This is the biggest single item on the list.

### 10. Awakening / EX burst
A meter that fills from damage dealt/taken (hook: MechHealth.TakeDamage both sides), a
burst activation (input in MechCombat.HandleInputs) granting a timed buff — boost refill
(BoostManager has everything needed), damage multiplier on MeleeHitbox/projectiles, a
burst-attack super. Mostly a numbers-and-VFX feature over existing systems; save for
after HUD exists (a meter you can't see isn't a mechanic).

## P3 — polish / completeness

### 11. Menus & match flow
No start menu, no victory/defeat screen (CostManager TODO comments), no rematch. Small
scene-management job; needed before anyone else playtests it.

### 12. Better AI
SimpleMechAI mirrors the player's melee well but never shoots (see MechShooterV2
notes), dodges rarely (2% roll inside 10 u), and doesn't manage boost or landing lag
strategically. After shooting lands, give it: shoot at mid-range, dodge-step on
incoming melee rush, overheat awareness (it already reads BoostManager).

### 13. Cleanup debt (from FINDINGS)
Dead duplicate health fields on MechController/SimpleMechAI; LockOnUI null-image guard;
TargetManager behind-camera reticle; MechShooter not disabled while downed; unify the
two red-lock distance checks. None block features; batch them into one cleanup pass.
