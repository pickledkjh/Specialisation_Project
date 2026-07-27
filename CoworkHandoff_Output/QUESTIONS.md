# QUESTIONS / JUDGMENT CALLS

Decisions I made without you, and things only you can answer.

## Judgment calls made (review these)

1. **The five scripts in `Assets/Scripts` ALREADY contained the new rewrite** when I
   arrived — their content matched the handoff copies, but they were saved as ANSI
   (Windows-1252) with CRLF, which mangled every em-dash in the tooltips into mojibake
   ("??"-looking bytes), and their timestamps were ~7.5 h NEWER than the handoff files.
   I assume you (or a previous session) already pasted them in via a Windows editor.
   I still performed the install as instructed: backed up the on-disk versions to
   `Backup_OldScripts/` (verbatim, mojibake and all) and overwrote with the clean
   handoff versions re-encoded as **UTF-8 with BOM + CRLF** (Unity/Windows-safe, fixes
   the tooltip text). Functionally identical code — if you had made a manual tweak
   beyond the paste, it would only exist if you edited AFTER the paste; I diffed and
   found no such tweak (only encoding/line-ending differences).

2. **Encoding choice**: UTF-8 with BOM + CRLF rather than the handoff's bare UTF-8+LF,
   to match Unity's own script template output on Windows and keep Visual Studio from
   guessing encodings. Content is byte-for-byte identical otherwise.

3. **`CoworkHandoff_Output/` is at the project root, NOT inside `Assets`** — deliberate,
   so Unity never compiles the backups/drafts (duplicate class names would be compile
   errors). The handoff said "project root", which I read as intended for exactly this
   reason.

4. **MORNING_CHECKLIST deviates from the handoff where the project's reality differs**:
   the handoff assumed states named Melee1–4, an existing HitDown/Recover/Die setup, and
   EndAttack events partially present. Actual: states are punch1–4, no knockdown/death
   states or clips exist at all, and NO clip has EndAttack. The checklist describes what
   is actually needed. FINDINGS.md §2 has the evidence.

## Questions for you

5. **Does the AI use `Player.controller` too?** It's the only animator controller in
   `Assets` — I assume both mechs share it (checklist written accordingly). If the AI's
   Animator points at something I couldn't see (e.g. a controller inside a package or
   created at runtime), the animator checklist items must be repeated there.

6. **`Running(Faster)` doubles as the boost-step animation** (DoStep transitions into
   it, and StepX/StepY parameters exist but no transition uses them). Was a proper
   4-direction step blend tree planned? The rainbow-step fix in checklist item 2 works
   either way, but a real step animation would sell it better.

7. **MeleeHitbox.damage (20) × 8-hit combo = 160 vs 100 max HP** — the full string kills
   at hit 5 and the finisher never happens. I flagged it in the checklist (suggest
   damage ~8–10 or maxHealth 300+) but didn't change anything: damage per hit vs combo
   length is a design call that also affects the (drafted) projectile damage of 10.
   What HP scale do you want — EXVS-style ~600-700, or keep 100?

8. **Player's MechHealth.team**: the default in code is `Team2` for everyone. The
   checklist tells you to set the player to Team1 in the Inspector — but if the scene
   already had it set correctly before the field defaults reloaded, this is a no-op.
   Worth 10 seconds to confirm on both mechs (death currently drains the wrong pool if
   both are Team2).

9. **Drafts intentionally not installed**: HomingProjectile/MechShooterV2/
   LockOnBattleCamera stayed in `Drafts/` per the handoff ("drafts must not replace or
   modify any existing script"). Say the word in the morning and copying them into
   `Assets/Scripts` + prefab setup is a ~30-minute follow-up session.

10. **`Assets/_Recovery/` contains two scene backups** (`0.unity`, `0 (1).unity`) from
    crash recovery. Not touched, but if `SampleScene.unity` is current you may want to
    clean these up yourself someday — I'm not allowed to delete anything (and didn't).
