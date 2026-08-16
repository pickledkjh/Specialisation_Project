# ARMOUR CLASH

**Kong Junhao — Lab 02 — Admin No 241526K**

A third-person 3D GVG-like mech fighting game built in Unity 6 (URP). One hand-tuned
mech frame, a fully destructible city arena, and a hand-authored AI rival that dashes,
dodges, guards, punishes and flies by the same rules the player does.

Reference lineage: the Gundam Versus series and Starward.

## Modes

| Mode | What it is |
|---|---|
| **Mission** | Three consecutive stages against an escalating rival (TRIAL RUN → ASSAULT → ACE CUSTOM). Damage carries over; a visible field repair runs between stages. Clear all three for the S rank. |
| **2v2 Team Battle** | You + an AI wingmate against two hostiles. Friendly fire is on but softened (30% damage / 15% knockdown / 35% stagger on allies). TAB switches your lock between hostiles. |
| **Quick Battle** | One round, one rival, straight in. |

## Controls

| Input | Action |
|---|---|
| W A S D | Move (relative to the locked enemy) |
| SPACE (hold) | Boost rise / hover; while downed: fast get-up |
| SHIFT (hold) | Boost dash |
| Double-tap A/D (or W/S) | Boost step — breaks shot tracking; cancels melee (rainbow step) |
| TAB / gamepad RB | Switch lock-on target |
| LEFT CLICK | Melee (chain up to 4 hits) |
| RIGHT CLICK | Shoot; hold 1 s then release = charge shot (2 ammo) |
| F (during boost dash) | Dash tackle |
| RMB (during a melee string) | Branch combo: gun-smash ender |
| Q (hold) | Shield guard — parries melee, roots you, 5 s cooldown on drop |
| E | Gerobi laser + expanding blast sphere |
| R | Homing missile barrage |
| B | Awakening burst (gauge ≥ 50%); also works mid-combo as a burst escape |
| G | Throw a nearby street object (tree / barrel / car / mast) |
| T | Tutorial (19 steps) · ESC pause |

## Running the project

1. Unity **6000.2** (URP) — open the project folder, let it import.
2. Open `Assets/Scenes/SampleScene`.
3. Press Play. The menu, HUD, tutorial and all modes are built at runtime — no scene
   wiring is needed beyond the arena, the two mechs and the camera.

To rebuild the arena: `Tools → Battlefield → Build Simple Battlefield`.
To re-sync the enemy model with the player's tuned model: `Tools → Gundam → 8. Clone PLAYER Model → ENEMY`.

## Building the exe

`File → Build Profiles → Windows` (x64), add `SampleScene`, Build into a clean folder.

## Project layout

```
Assets/Scripts/          all gameplay code (one class per file)
Assets/Scripts/Editor/   content tools: battlefield builder, Gundam rig pipeline, missile pack setup
Assets/Model/            imported mech model (MMD → humanoid retarget)
Assets/Resources/        runtime-loaded missile pack (auto-copied by editor tool)
Docs/                    GDD, TDD, final presentation
Playtest/                Week 14 structured playtest report + survey data
```

## Third-party assets

- CartoonLowPolyCityLite (city environment)
- Origin Gundam MMD model (mech frame)
- Quick Effects Vol.1 (hit VFX)
- Homing Missile pack (missile model/exhaust/explosion)
- Mixamo (humanoid animation clips)

All audio, music, UI and remaining VFX are generated procedurally in code.
