using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace OskarMike.AI
{
    [DisallowMultipleComponent, RequireComponent(typeof(EnemyHealth))]
    public sealed class EnemyCombatController : MonoBehaviour
    {
        public Transform muzzle;
        [Min(.1f)] public float coverRadius = 5, slotSpacing = 1.2f;
        [Min(0)] public float aimDelay = .75f, aimAngle = 5, blockedDelay = .5f;
        [Min(.05f)] public float retryInterval = .5f, recoveryRetry = 1;
        [Range(0, 1)] public float recoveryThreshold = .5f, recoveryCeiling = .75f;
        [Min(0)] public float recoveryRate = .1f, recoveryQuietTime = 3, reactionDelay = .75f, targetLockTime = 3;
        [Min(.1f)] public float stuckTimeout = 3;
        [Tooltip("아직 배정되지 않은 쿼리 결과의 대상 위치 오차 허용값. 배정된 슬롯은 시야가 유지되면 보존합니다.")]
        [Min(.1f)] public float targetMoveTolerance = 1.5f;
        public EnemyAgent Enemy { get; private set; }
        public NavMeshAgent Mover { get; private set; }
        public bool Recovering => recovering;
        public bool HoldingCover => hasPosition && positionKind == CombatQueryKind.Cover;
        public bool LeavingCover => leavingCover;
        public bool AwaitingRecoveryCover => pending && pendingKind == CombatQueryKind.Cover &&
            health.Recovery != RecoveryState.Complete && (health.Recovery == RecoveryState.InProgress || health.Current <= health.maximum * recoveryThreshold);
        public bool OwnsMovement => recovering || Enemy.HasVisual && Enemy.Engaged;
        public bool HasAttackSlot => hasPosition && positionKind != CombatQueryKind.Cover;
        public Vector3 ReservedPosition => destination;
        public string DiagnosticState => $"slot={HasAttackSlot}, cover={HoldingCover}, pending={pending}, recovering={recovering}, destination={destination}, aim={aimedAt}, blocked={blockedAt}, velocity={Mover.velocity}, canFire={CanFire}";
        public Vector3 AttackTargetPosition => target != null && Enemy.HasVisual ? target.transform.position : Enemy.LastKnownPosition;
        private EnemyHealth health;
        private readonly CombatPositionQuery query = new();
        private readonly Dictionary<ulong, Vector3> threats = new();
        private readonly List<ulong> expiredThreats = new();
        private NetworkObject target, pendingAttacker, lockedTarget;
        private Vector3 destination, progressPosition;
        private CombatQueryKind positionKind, pendingKind;
        private bool hasPosition, pending, recovering, wantCover, initialized, leavingCover;
        private bool holdingFiringPosition;
        private uint revision;
        private double nextQuery, nextRecovery, nextThreats, aimedAt = -1, blockedAt = -1, progressAt, switchAt, lockedUntil;
        private int tickFrame = -1;
        public Vector3 Origin => muzzle != null ? muzzle.position : transform.position + Vector3.up * Enemy.Read("EyeHeight", 1.6f);
        public static Vector3 AimPoint(NetworkObject player)
        { var body = player.GetComponent<CharacterController>(); return body != null ? body.bounds.center : player.transform.position + Vector3.up; }

        private void Awake()
        {
            Enemy = GetComponent<EnemyAgent>(); Mover = GetComponent<NavMeshAgent>(); health = GetComponent<EnemyHealth>();
            health.Damaged += OnDamage;
        }
        private void OnDestroy() { if (health != null) health.Damaged -= OnDamage; Cancel(); }
        private void OnDisable() => Cancel();
        public void Cancel()
        {
            CombatSlotManager.Release(this); CombatSlotManager.LeaveQueue(this); revision++; pending = false; hasPosition = false;
            aimedAt = blockedAt = -1; pendingAttacker = null;
            leavingCover = false;
            holdingFiringPosition = false;
        }
        private void OnDamage(NetworkObject attacker)
        {
            nextThreats = 0;
            if (Time.timeAsDouble < lockedUntil || pendingAttacker != null || attacker == null || attacker == target || !Enemy.CanSee(attacker)) return;
            pendingAttacker = attacker; switchAt = Time.timeAsDouble + reactionDelay;
        }
        public NetworkObject SelectTarget(NetworkObject current)
        {
            double now = Time.timeAsDouble;
            if (!Alive(lockedTarget)) lockedUntil = 0;
            if (pendingAttacker != null && now >= switchAt)
            {
                var candidate = pendingAttacker; pendingAttacker = null;
                if (Alive(candidate) && Enemy.CanSee(candidate)) { lockedTarget = candidate; lockedUntil = now + targetLockTime; return candidate; }
            }
            if (now < lockedUntil && Alive(lockedTarget)) return lockedTarget;
            return current;
        }
        public bool LockActive => Time.timeAsDouble < lockedUntil && Alive(lockedTarget);
        private static bool Alive(NetworkObject value) => value != null && value.IsSpawned && value.gameObject.activeInHierarchy &&
            (!value.TryGetComponent<OskarMike.Network.Player.PlayerHealth>(out var playerHealth) || !playerHealth.Dead);

        public void Tick()
        {
            if (tickFrame == Time.frameCount || !Enemy.ServerActive || health.Dead || !Mover.enabled || !Mover.isOnNavMesh) return;
            tickFrame = Time.frameCount;
            double now = Time.timeAsDouble;
            if (!initialized) { nextQuery = now + (Enemy.NetworkObjectId % 7) * .035; initialized = true; }
            if (now >= nextThreats)
            {
                nextThreats = now + Enemy.senseInterval;
                expiredThreats.Clear();
                foreach (var item in threats)
                    if (!Enemy.NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(item.Key, out var obj) || !Alive(obj)) expiredThreats.Add(item.Key);
                foreach (ulong id in expiredThreats) threats.Remove(id);
                foreach (var client in Enemy.NetworkManager.ConnectedClientsList)
                    if (Enemy.CanSee(client.PlayerObject)) threats[client.PlayerObject.NetworkObjectId] = AimPoint(client.PlayerObject);
            }
            if (Enemy.Target != null && Enemy.Target != target)
            {
                Cancel(); target = Enemy.Target; wantCover = false;
            }
            if (Enemy.HasVisual && target != null) threats[target.NetworkObjectId] = AimPoint(target);
            if (health.Recovery != RecoveryState.Complete && (health.Recovery == RecoveryState.InProgress || health.Current <= health.maximum * recoveryThreshold) && !recovering && now >= nextRecovery)
            {
                nextRecovery = now + recoveryRetry;
                Request(CombatQueryKind.Cover);
            }
            CombatSlotManager.Tick();
            if (recovering)
            {
                if (!hasPosition || !SafeCover(destination))
                {
                    if (hasPosition) DropPosition();
                    if (now >= nextRecovery) { nextRecovery = now + recoveryRetry; Request(CombatQueryKind.Cover); }
                    Enemy.StopMoving(); return;
                }
                if (!MoveToPosition()) return;
                Enemy.StopMoving();
                if (now - health.LastHitAt >= recoveryQuietTime && SafeCover(transform.position))
                    health.Recover(health.maximum * recoveryRate * Time.deltaTime, recoveryCeiling);
                if (health.Recovery == RecoveryState.Complete) { recovering = false; DropPosition(); nextQuery = now; }
                return;
            }
            if (AwaitingRecoveryCover && !Enemy.HasVisual) { Enemy.StopMoving(); return; }
            if (Enemy.Read("AlertInProgress", false)) return;
            if ((!Enemy.HasVisual && !HoldingCover && !HasAttackSlot) || !Alive(target)) { DropPosition(); CombatSlotManager.LeaveQueue(this); return; }
            if (!Enemy.Engaged) return;
            // Once established, keep this position while the target is visible.
            // Range still gates TryFire, but no longer causes positional churn.
            if (HasAttackSlot && holdingFiringPosition && !Enemy.HasVisual)
            {
                DropPosition(); CombatSlotManager.LeaveQueue(this); return;
            }
            if (!HasAttackSlot)
            {
                if (now >= nextQuery) Request(wantCover && !hasPosition ? CombatQueryKind.Cover : CombatQueryKind.Attack);
                if (hasPosition && positionKind == CombatQueryKind.Cover)
                {
                    if (SafeCover(destination)) MoveToPosition(); else DropPosition();
                }
                else Enemy.StopMoving();
                return;
            }
            if (positionKind == CombatQueryKind.SideStep && Enemy.HasVisual && ValidAttack(transform.position, AttackTargetPosition) && CombatSlotManager.Reserve(this, transform.position)) destination = transform.position;
            if (!MoveToPosition()) return;
            if (!Enemy.HasVisual)
            {
                Enemy.Face(Enemy.LastKnownPosition);
                if (aimedAt < 0) aimedAt = now;
                if (now - aimedAt < aimDelay) return;
            }
            leavingCover = false;
            if (!Enemy.HasVisual) { DropPosition(); return; }
            holdingFiringPosition = true;
            Enemy.StopMoving(); Enemy.Face(target.transform.position);
            bool blocked = query.Blocked(Origin, AimPoint(target), Enemy, true, out bool actor);
            if (blocked)
            {
                aimedAt = -1;
                if (blockedAt < 0) blockedAt = now;
                if (now - blockedAt >= blockedDelay && now >= nextQuery)
                {
                    if (actor) Request(CombatQueryKind.SideStep);
                    else { DropPosition(); wantCover = true; }
                }
                return;
            }
            blockedAt = -1;
            if (Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up)) > aimAngle) { aimedAt = -1; return; }
            if (aimedAt < 0) aimedAt = now;
            if (now - aimedAt >= aimDelay) Enemy.TryFire();
        }

        public bool CanFire => !recovering && HasAttackSlot && Alive(target) && Enemy.Target == target &&
            Vector3.Distance(transform.position, destination) <= Mover.stoppingDistance + .15f &&
            Mover.velocity.sqrMagnitude < .04f && aimedAt >= 0 && Time.timeAsDouble - aimedAt >= aimDelay &&
            Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up)) <= aimAngle &&
            !query.Blocked(Origin, AimPoint(target), Enemy, true, out _);

        private bool MoveToPosition()
        {
            if (Vector3.Distance(transform.position, destination) <= Mover.stoppingDistance + .15f) { Enemy.StopMoving(); return true; }
            aimedAt = -1;
            if (Vector3.Distance(transform.position, progressPosition) > .1f) { progressPosition = transform.position; progressAt = Time.timeAsDouble; }
            if (Time.timeAsDouble - progressAt > stuckTimeout || !Enemy.Navigate(destination, Enemy.Read("ChaseSpeed", 4f)))
            { DropPosition(); nextQuery = Time.timeAsDouble + retryInterval; }
            return false;
        }
        private void DropPosition()
        {
            bool wasMoving = hasPosition || leavingCover;
            CombatSlotManager.Release(this); hasPosition = pending = leavingCover = false; revision++; aimedAt = blockedAt = -1;
            holdingFiringPosition = false;
            if (wasMoving) Enemy.StopMoving();
        }
        private void Request(CombatQueryKind kind)
        {
            if (pending) return;
            if (target == null && threats.Count == 0) return;
            Vector3 targetPosition = target != null && Enemy.HasVisual ? target.transform.position : Enemy.LastKnownPosition;
            pending = true; pendingKind = kind; revision++;
            if (kind == CombatQueryKind.Attack && target != null) CombatSlotManager.WaitForSlot(this, target.NetworkObjectId);
            CombatSlotManager.Enqueue(this, new CombatPositionRequest(revision, target != null ? target.NetworkObjectId : ulong.MaxValue, transform.position, targetPosition, kind));
        }
        public void Receive(CombatPositionResult result)
        {
            if (result.Request.Id != revision) return;
            pending = false;
            if (result.Request.TargetId != (target != null ? target.NetworkObjectId : ulong.MaxValue) ||
                Time.timeAsDouble - result.Request.CreatedAt > 2 || Vector3.Distance(transform.position, result.Request.Origin) > 1.5f) return;
            if (Enemy.HasVisual && target != null && Vector3.Distance(target.transform.position, result.Request.Target) > targetMoveTolerance) return;
            nextQuery = Time.timeAsDouble + retryInterval;
            if (!result.Found || !CombatSlotManager.Reserve(this, result.Position))
            {
                if (result.Request.Kind == CombatQueryKind.SideStep) { DropPosition(); wantCover = true; }
                else if (result.Request.Kind == CombatQueryKind.Attack) wantCover = true;
                else { wantCover = false; recovering = false; }
                return;
            }
            leavingCover = HoldingCover && result.Request.Kind != CombatQueryKind.Cover;
            destination = result.Position; positionKind = result.Request.Kind;
            holdingFiringPosition = false;
            hasPosition = true; aimedAt = blockedAt = -1; progressAt = Time.timeAsDouble; progressPosition = transform.position;
            if (positionKind != CombatQueryKind.Cover) CombatSlotManager.LeaveQueue(this);
            if (positionKind == CombatQueryKind.Cover && health.Recovery != RecoveryState.Complete &&
                (recovering || health.Recovery == RecoveryState.InProgress || health.Current <= health.maximum * recoveryThreshold)) { recovering = true; CombatSlotManager.LeaveQueue(this); }
            wantCover = false;
        }
        public bool ValidAttack(Vector3 point, Vector3 targetPosition) => Vector3.Distance(point, targetPosition) <= Enemy.firingRange &&
            !query.Blocked(point + (Origin - transform.position), target != null && Enemy.HasVisual ? AimPoint(target) : targetPosition + Vector3.up, Enemy, true, out _);
        public bool SafeCover(Vector3 point)
        {
            if (threats.Count == 0) return false;
            foreach (var threat in threats.Values)
                if (!query.Blocked(point + Vector3.up, threat, Enemy, false, out _)) return false;
            return true;
        }
    }
}
