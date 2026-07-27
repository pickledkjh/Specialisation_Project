# WIRING GUIDE — EXVS-Style Shooting (Drafts)

Files: `HomingProjectile.cs`, `MechShooterV2.cs` (both in `Drafts/`, NOT compiled).
Nothing in the live project was modified — `MechShooter.cs` is untouched.

## 1. Projectile prefab setup

1. Create an empty GameObject `Projectile_Beam`. Add a visual child (stretched capsule
   / trail renderer — a 1×1×3 emissive capsule reads instantly as a beam).
2. On the ROOT add:
   - **Capsule or Sphere Collider**, `Is Trigger = ON`, sized a bit generously
     (radius ~0.3) — EXVS shots are forgiving.
   - **Rigidbody**: `Is Kinematic = ON`, `Use Gravity = OFF`. (A kinematic rigidbody +
     trigger collider is required for OnTriggerEnter to fire reliably against the
     mechs' CharacterControllers while the script moves the transform manually.)
   - **HomingProjectile** component.
3. Do NOT tag it "Player"/"Enemy" — the mechs' MeleeHitbox filters by those tags, and a
   projectile wearing them could be punched.

## 2. Layers so it can't hit its own shooter

The script already has two guards (Physics.IgnoreCollision against every shooter
collider in `Init`, plus an `IsChildOf(shooterRoot)` check on hit), so it works even
with zero layer setup. Layers make it airtight and cheaper:

1. Project Settings → Tags and Layers: add layer **`Projectile`**.
2. Put the projectile prefab root on `Projectile`.
3. Physics matrix (Project Settings → Physics): uncheck **Projectile × Projectile**
   (shots pass through each other). Leave Projectile × Default (world) and Projectile ×
   whatever layer the mechs use ON.
4. Optional future-proofing: give each mech team its own layer and spawn player shots
   on a `PlayerProjectile` layer that doesn't collide with the player's layer at all —
   then the runtime IgnoreCollision loop becomes redundant.

## 3. Muzzle

1. On the mech, find the right-hand gun bone (or the right hand itself) and add an
   empty child `Muzzle` at the barrel tip, blue axis (+Z) pointing out of the barrel.
2. If there's no gun mesh yet, parent it to the right forearm — position matters less
   than you'd think, because each shot is aimed at the target on spawn.

## 4. Inspector fields (MechShooterV2)

| Field | Suggested | Notes |
|---|---|---|
| spineBone / armBone / offsets / weights | copy from current MechShooter | identical meaning |
| shootDuration | 0.5 | aim-pose hold, identical to before |
| projectilePrefab | the prefab from §1 | |
| muzzle | the Transform from §3 | |
| redLockRange | 40 | must match `MechCombat.redLockRange` — if you retune one, retune both |
| maxAmmo | 8 | |
| shotCooldown | 0.6 | ~EXVS rifle rhythm |
| reloadTime | 5 | full-magazine refill when empty |

On `HomingProjectile` (prefab): speed 60, lifetime 4, turnRate 120°/s, homingDuration
0.5, aimHeight 1.5, damage 10, knockdownPower 30 (≈4 unanswered shots = knockdown,
since maxKnockdownValue is 100 and the bar decays 15/s).

## 5. Safe swap procedure (MechShooter → MechShooterV2)

Do this AFTER the melee/movement playtest is green, so you never debug two systems at
once:

1. Copy `HomingProjectile.cs` and `MechShooterV2.cs` from `Drafts/` into
   `Assets/Scripts/`. Let Unity compile. (Drafts folder itself is outside Assets on
   purpose; nothing compiles until you copy.)
2. Build the prefab + muzzle (§1–§3).
3. On the player mech: **Add** MechShooterV2 and fill its fields (copy the bone
   references off the old component: click the ⋮ on MechShooter → Copy Component →
   on MechShooterV2 → Paste Component Values — the shared field names carry over).
4. **Disable (don't delete) MechShooter** with the checkbox.
5. One-line change in `MechCombat.cs`: the private field `mechShooter` is typed
   `MechShooter`. Change the type to `MechShooterV2` (declaration +
   `GetComponent<MechShooterV2>()` in Awake). Nothing else calls it — `FireWeapon()`
   keeps the same name on purpose.
6. Playtest: red-lock shots curve for ~0.5 s then straighten; green-lock (>40 u) shots
   fly dead straight; shots vanish on downed mechs without exploding; 8 shots then a
   5 s dry spell.
7. Only when happy: delete the old MechShooter component, then the file.

Rollback at any point: re-enable MechShooter, revert the one-line type change in
MechCombat, disable MechShooterV2.

## 6. Known gaps (deliberate, drafts stay minimal)

- No shot flinch on the victim (melee-style TakeHit/hit-stop) — noted as a TODO inside
  HomingProjectile; wire it the way MeleeHitbox does, AFTER checking isYellowLocked.
- No muzzle flash / hit VFX / SFX (ROADMAP).
- No ammo HUD — currentAmmo is public for the future HUD (ROADMAP).
- AI can't shoot yet — the exact steps are in the MechShooterV2 header comment.
