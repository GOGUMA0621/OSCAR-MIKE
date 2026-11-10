# AI combat integration

AI 전체 실행·구조·검증 안내는 [AI_README](Assets/AI_README.md)를 참조한다.
이 문서는 전투 상세 설정·피해 API·전투 검증을 관리한다. 공통 실행 안내는 대표 README에서 관리하며, 전투 동작 변경 시 대표 README의 요약도 함께 갱신한다.

`PatrolEnemy.prefab` now contains `EnemyHealth` and `EnemyCombatController`.
The existing Behavior graph keeps its patrol, investigation and warning branches.
Its aim/follow actions delegate to the companion controller, and `TryFire` has a
final combat gate. During cover waiting/recovery the graph is paused while sensing
continues; it restarts when the companion returns control. Existing non-combat
enemies without the new component retain their original behavior.

## Server integration

Call `enemy.GetComponent<EnemyHealth>().ApplyDamage(amount, attackerNetworkObject)`
from authoritative weapon simulation. Invalid/nonpositive damage, client calls,
unspawned enemies and repeated damage after death are rejected. Current health is
a server-write NetworkVariable. `Damaged` and `Died` are server events. Weapons,
projectiles, damage delivery, death animation and corpse despawning are not added.
Death stops the AI, cancels reservations and releases the population budget once.

Player validity also excludes dead `PlayerHealth` components. Dead player objects
remain spawned for camera/session continuity. Both health components expose
`ApplyHit(baseDamage, HitRegion, attacker)` for head x2 / other x1 damage.
`ApplyDamage` still accepts final damage for existing callers.
See [basic health](Docs/BasicHealth.md) for player HUD, death and verification.

## Defaults and tuning

Settings are on the companion component, not duplicated in the existing Blackboard.
Existing sight, chase speed, fire range and fire interval remain Blackboard settings.

| Setting | Default |
| --- | --- |
| Max health | 100 |
| Recovery trigger / end | 50% / 75% |
| Recovery speed / quiet time | 10% max health per second / 3 seconds |
| Recovery count | Once per lifetime; pauses resume the same use |
| Cover search radius / maximum route length | 5m / twice the radius |
| Attack position sampling | 16 directions on rings at 65% and 85% firing range |
| Cover sampling | 16 directions at half and full cover radius |
| Position separation | 1.2m |
| Aim time / horizontal angle | 0.75s / 5 degrees |
| Blocked threshold / side steps | 0.5s / left-right 1m then 2m |
| Failed search / recovery search retry | 0.5s / 1s |
| Damage reaction / target lock | 0.75s / 3s |
| Movement progress timeout | 3s without 0.1m progress |
| Pending query target-position tolerance | 1.5m (does not invalidate an assigned slot) |
| Query budget | 2 candidate searches per frame across enemies |

Cover is checked against remembered positions of all visually detected players;
unseen damage does not disclose attacker position. Reservations include both
cover and attack positions. Waiting order survives cover searches and retries;
an earlier reachable claimant prevents a later waiter from taking that position.
All position results carry request id, target id, origin and timestamp. Stale
results are rejected. `ICombatPositionExecutor` is the future execution boundary;
the current implementation uses Unity physics/NavMesh on the host and does not
support off-thread execution or remote trust without an additional adapter.

After arriving and acquiring visual contact, an enemy holds its attack position
while the target remains visible, regardless of target displacement. Outside firing
range it holds fire without requesting another slot. Losing visual contact releases
the attack slot and returns control to the graph's last-known-position investigation.
Initial slot travel still allows a short reacquisition at arrival so turning during
navigation does not repeatedly cancel the route. Ally obstruction, recovery, target
changes and failed routes retain their existing repositioning rules.

## Verification

Run **Tools > OSKAR MIKE > AI > 전투 구조 및 사선 검증** for prefab wiring,
AI/wall/trigger/player ray behavior, duplicate reservations/release and rejected
damage checks. This creates temporary objects and does not save scenes.
Batch entrypoint: `OskarMike.AI.Editor.EnemyCombatValidation.Run`.

**서버 체력 검증** opens the existing test scene and runs a real NGO host to check
health initialization, invalid damage, the 50% boundary, recovery state/ceiling,
one-use recovery, lethal damage and duplicate death rejection. Batch entrypoint:
`OskarMike.AI.Editor.EnemyCombatHealthValidation.Run` (omit `-quit`; the harness
exits after play-mode validation). It isolates the health API; it does not replace
the tactical recovery timing/cover and four-peer scenarios below.

**엄폐 회복 및 타깃 전환 검증** runs a real host with a temporary cover wall and
a spawned intruder. It verifies actual controller-driven cover arrival, no healing
before the quiet period, interrupted recovery, 75% completion, no firing during
recovery, delayed attacker switching and target lock. Batch entrypoint:
`OskarMike.AI.Editor.EnemyCombatScenarioValidation.Run` (omit `-quit`).
The intruder is a spawned test object, not a second connected client.

### Recorded verification

- Unity 6000.3.9f1 imports and compiles the worktree successfully.
- Structural/raycast/reservation checks: 12 passed.
- AI play regression (2026-10-20): 44 passed, including patrol, warning timing,
  reinforcement budgets, firing, hearing/investigation and host shutdown.
  New checks verify holding the assigned position when a visible target moves,
  holding fire without moving outside firing range, and releasing the slot to
  investigate when sight is lost.
- Tactical cover/recovery/retarget scenario: 7 checkpoints passed, with continuous
  assertions for the quiet period, reaction delay and target lock.
- The previous no-shot regression was caused by capsule-centred player roots
  placing attack candidates above the NavMesh sampling radius. Candidate sampling
  now allows an agent-height projection; the real-host firing test covers this.
- Four connected peers, max-population CPU/GC/network profiling and the complete
  MainMenu/Lobby/GameMap UI flow still require the manual acceptance run below.
- The headless editor logs an unrelated Unity Search startup indexing exception;
  batch tests explicitly keep the player loop running and report their own failures.

Play-mode acceptance in the existing AI test scene:

1. Place enemies in a line: rear enemy holds fire, then moves sideways; blocking
   both sides makes it release its slot and wait in nearby cover.
2. Occupy every available attack position: queued enemies never shoot from cover;
   freeing a reachable position admits the oldest eligible waiter.
3. Apply 50 server damage: find cover, wait 3s since damage, heal at 10/s to 75.
   Damage again during recovery: healing pauses for another 3s; moving/exposure
   never heals. After completion, damage below 50 does not grant another recovery.
4. Remove nearby cover: continue normal combat and retry cover after 1s. Close
   doors or block a route: reservations are released on failure/stalled movement.
5. A second player merely enters view: no switch. Visible damage switches after
   0.75s; subsequent hits do not reset the timer, and the new target locks for 3s.
   An unseen attacker must not disclose its location. A second visible threat
   can invalidate cover without becoming the attack target.
6. Test death/despawn, disconnect and target change during movement, waiting and
   healing. Verify no reservations remain and no late query resumes a dead AI.
7. Use 4 peers to check health synchronization, spawn, join code, MainMenu/Lobby/
   GameMap transitions and host shutdown. Profile AI CPU time, query count, GC and
   network traffic at the intended maximum enemy count before setting performance
   expectations. The candidate budget does not bound every ray/path operation.
