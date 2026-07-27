# TUNING GUIDE — Serialized Fields of the Five New Scripts

Every value here is editable in the Inspector. Because several fields were renamed in
the rewrite, the scene will show these **defaults**, not your old tuned values — treat
this table as the starting point.

## MechController (player movement)

### Base Movement

| Field | Default | What it controls |
|---|---|---|
| `walkSpeed` | 8 | Ground/air strafe speed outside of dashes. |
| `stepSpeed` | 45 | Initial burst speed of a boost step (dodge). Higher = snappier sidestep. |
| `stepDuration` | 0.25 | How long a boost step lasts. Longer = travels farther but commits harder. |
| `ascendSpeed` | 15 | Target vertical speed while holding rise. The "how fast do I climb" number. |
| `gravity` | -20 | Fall acceleration. Suggested tweak: try **-24** for a snappier drop. |
| `bodyRotationSpeed` | 15 | How quickly the mech turns to its movement direction. |

### EXVS Boost Feel (dash / skim / glide)

| Field | Default | What it controls |
|---|---|---|
| `dashBurstSpeed` | 30 | Speed of the initial dash snap. The "kick" at dash start. |
| `dashCruiseSpeed` | 24 | Speed held while the dash button stays down. Keep HIGH — an EXVS dash must not bleed speed while held. |
| `dashBurstSettleTime` | 0.2 | Seconds for burst to settle into cruise. Short = dash never feels like it's dying. |
| `dashHopImpulse` | 2 | Small upward pop when dashing from the ground (the skim hop). |
| `dashVerticalSettle` | -2.5 | Vertical speed the dash settles toward after the hop. Slightly negative = mech skims back DOWN onto the floor instead of hovering. More negative = hugs the floor harder. |
| `airInertiaDrag` | 2 | How fast leftover momentum bleeds in the AIR after releasing a dash. **Lower = longer inertia glide.** |
| `groundMomentumDrag` | 6 | Same, on the ground. Higher = stops quickly once landed. |
| `airControlFactor` | 0.85 | Stick authority while airborne (1 = same as ground). |
| `maxFallSpeed` | 25 | Terminal fall speed. Lower = floatier EXVS drop. |
| `ascendRampSpeed` | 160 | How fast vertical speed snaps toward `ascendSpeed` when rising. High = crisp, near-instant rise reversal out of a fall. |

### Landing Lag (scales with remaining boost)

| Field | Default | What it controls |
|---|---|---|
| `landingLagAtFullBoost` | 0.12 | Lag when landing with a full gauge — barely noticeable. |
| `landingLagAtEmptyBoost` | 0.7 | Lag when landing on fumes. The boost-management rhythm lives between these two. |
| `overheatLandingLag` | 1.8 | Punishment lag when landing overheated. Long on purpose. |

### Ground check (unchanged names)

| Field | Default | What it controls |
|---|---|---|
| `groundCheckPoint` / `groundCheckRadius` / `groundLayer` | — / 0.4 / — | Sphere-check for grounded state. Radius too big = "lands" early on slopes. |

## MechCombat (player melee)

### Lock-On Ranges

| Field | Default | What it controls |
|---|---|---|
| `redLockRange` | 40 | Inside this = red lock: melee homing rush and (drafted) projectile homing. |
| `meleeHitRange` | 3.5 | Close enough to swing without rushing first. |

### Melee Lunge (homing rush)

| Field | Default | What it controls |
|---|---|---|
| `meleeLungeSpeed` | 65 | Rush start speed. The "teleport across the arena" feel. |
| `minLungeSpeed` | 10 | Speed the rush decays toward if it takes long. |
| `lungeSpeedDrag` | 1.5 | How fast rush speed decays toward `minLungeSpeed`. |
| `maxLungeTime` | 0.8 | Rush gives up after this long. |
| `lungeStopDistance` | 1.8 | Distance from target where the rush stops and the swing starts. |
| `lungeAimHeight` | 0 | Extra aim height above target pivot. **Keep 0** — positive values recreate the floating-fight bug. |
| `lungePoseDelay` | 0.15 | How far into the Melee1 windup the pose freezes during the rush. |

### Combo Length / Flow

| Field | Default | What it controls |
|---|---|---|
| `comboLoops` | 2 | The Melee1-4 string loops this many times: 2 = 8-hit combo. Needs the Melee4→Melee1 animator transition when > 1. |
| `inputBufferWindow` | 0.5 | How long a melee press is remembered (EXVS-style buffering). Bigger = mashing always chains. |
| `chainDelayAfterHit` | 0.08 | Pause after a hit registers before the next swing may cancel in — lets impacts read. |
| `whiffChainInterval` | 0.3 | Minimum gap between swings when chaining whiffs outside red lock. |
| `comboSafetyTimeout` | 2.5 | Failsafe only: ends the string if no `EndAttack` event fires. Keep LONGER than the longest melee clip. (Until EndAttack events are added, this is unfortunately the real end-of-swing timer — see MORNING_CHECKLIST.) |
| `endLagDuration` | 0.6 | Recovery after the string ends — the punishable window. |

### Knockdown Bar Powers / Finisher Launch

| Field | Default | What it controls |
|---|---|---|
| `hitKnockdownPower` | 6 | Bar power per normal hit. 7 non-finisher hits × 6 = 42 < 100 (`MechHealth.maxKnockdownValue`), so a full string never drops the enemy early. Raise carefully. |
| `finisherKnockdownPower` | 30 | Bar power of the true final hit (the launcher always knocks down via `TriggerKnockdown` anyway). |
| `finisherLaunchForward` | 16 | Horizontal fling speed of the final launcher. Distance of the "sent flying" arc. |
| `finisherLaunchUp` | 8 | Vertical fling speed. Height of the arc. |

### Tracking

| Field | Default | What it controls |
|---|---|---|
| `redLockTurnSpeed` | 30 | How hard the attacker steers toward the target mid-string (only when the string started in red lock). |

### Cameras (references, not tuning)
`punch1Camera`, `punch4Camera`, `impulseSource` — assign the two cinematic Cinemachine
cameras and the impulse source for hit shake.

## MechHealth (both mechs)

| Field | Default | What it controls |
|---|---|---|
| `maxHealth` | 100 | HP. (Duplicate fields on MechController/SimpleMechAI are dead — this one is real.) |
| `team` | Team2 | Which cost pool this mech's death drains. Player should be Team1! |
| `unitCost` | 2000 | Cost deducted from the team pool on death. |
| `maxKnockdownValue` | 100 | Hidden knockdown bar size. Combo design lives against this number. |
| `knockdownDecayRate` | 15 | Bar drain per second when not being hit. Higher = harder to knock down with pokes. |
| `downedDuration` | 3.0 | Lie-on-floor time (starts AFTER landing). |
| `wakeUpProtectionDuration` | 3.0 | Invincibility window after standing, broken by acting. |
| `knockdownLaunchGravity` | -22 | Gravity during the launch flight. Less negative = longer, more dramatic arc. |
| `knockdownLaunchDrag` | 1.1 | How fast horizontal launch speed decays mid-flight. Lower = flies farther. |
| `knockdownMaxAirTime` | 1.6 | Safety cap only — flight normally ends on ground contact (+3 s hard cap in code). |

## MeleeHitbox (each fist/foot collider)

| Field | Default | What it controls |
|---|---|---|
| `targetTag` | "Enemy" | Who this hitbox hurts ("Enemy" on the player's fists, "Player" on the AI's). |
| `playerCombatScript` / `aiCombatScript` | — | Assign exactly ONE — the owner of the hitbox. |
| `hitStopDuration` | 0.1 | Freeze-frame length on impact, both sides. The "crunch". |
| `damage` | 20 | HP per hit. 8-hit combo = 160 = lethal vs 100 HP — tune one of the two. |
| `defaultKnockdownPower` | 10 | Bar power fallback when no MechCombat drives it (i.e. AI hitboxes). |
| `hitStunDuration` | 0.9 | Stagger per hit. Keep comfortably longer than the gap between punches so strings hold the victim. |
| `launchForwardSpeed` | 9 | Launch applied only if a MID-STRING hit happens to fill the knockdown bar. |
| `launchUpSpeed` | 5 | Vertical part of the same. |
| `hitCooldown` | 0.4 | Per-hitbox re-hit cooldown; reset each swing by `ResetCooldown()`, so it only prevents double-hits within one swing. |

## SimpleMechAI (enemy)

| Field | Default | What it controls |
|---|---|---|
| `walkSpeed` | 8 | Chase speed. |
| `dashSpeed` | 22 | AI boost-dash approach speed. |
| `stepSpeed` / `stepDuration` | 45 / 0.25 | AI dodge step, same meaning as player. |
| `dashRange` | 20 | Between `attackInitiationRange` and this, the AI dashes to close. |
| `gravity` | -20 | Match the player's value (and the -24 tweak if you take it). |
| `landingRecoveryTime` | 0.8 | AI's fixed landing lag (AI doesn't scale it by boost). |
| `groundCheckPoint` / `groundCheckRadius` / `groundLayer` | — / 0.4 / — | Same as player. |
| `attackInitiationRange` | 15 | AI starts a melee string inside this. |
| `meleeHitRange` | 3.5 | AI rush stop distance / chain range check (+2 slack while chaining). |
| `meleeLungeSpeed` | 65 | AI homing rush speed (drag 1.5 and max time 0.8 are hardcoded to match the player). |
| `attackCooldown` | 1.2 | Minimum gap between AI strings. Lower = more aggressive. |
| `lungePoseDelay` | 0.15 | Same pose freeze delay as the player. |
| `comboLoops` | 2 | AI string length; needs the same Melee4→Melee1 animator transition. |
| `finisherLaunchForward` / `finisherLaunchUp` | 16 / 8 | AI's launcher fling — mirrors the player's. |
| `rightFistCollider` / `leftFistCollider` / `leftFootCollider` | — | AI hitboxes (with MeleeHitbox set to targetTag "Player" and `aiCombatScript` assigned). |
