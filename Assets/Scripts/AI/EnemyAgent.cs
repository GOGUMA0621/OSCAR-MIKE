using System;
using Unity.Behavior;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Action = System.Action;

namespace OskarMike.AI
{
    [BlackboardEnum]
    public enum EnemyState : byte { Patrol, Investigate, Alert, Chase, Shoot }

    [DefaultExecutionOrder(-40)]
    [RequireComponent(typeof(NetworkObject), typeof(NavMeshAgent), typeof(BehaviorGraphAgent))]
    public sealed class EnemyAgent : NetworkBehaviour
    {
        public EnemyPatrolRoute route;
        // Tuning lives in the agent's instanced Blackboard, never in duplicate Inspector fields.
        // These accessors also preserve the existing spawner and validation integration.
        public bool canRequestReinforcements { get => Read("CanRequestReinforcements", true); set => Write("CanRequestReinforcements", value); }
        public float patrolSpeed { get => Number("PatrolSpeed", 2); set => Write("PatrolSpeed", value); }
        public float sightRange => Number("SightRange", 12);
        public float firingRange => Number("FiringRange", 10);
        public float fieldOfView => Mathf.Clamp(Number("FieldOfView", 100), 1, 360);
        public float senseInterval => Mathf.Max(.02f, Number("SenseInterval", .1f));
        public float alertDuration => Number("AlertDuration", 1.5f);
        public float fireInterval => Mathf.Max(.02f, Number("FireInterval", 1.5f));
        public LayerMask obstructionMask = ~0;
        public AudioClip alertClip;
        public bool diagnostics = true;

        public event Action AlertStarted;
        public event Action<Vector3> ReinforcementRequested;
        public event Action<NetworkObject> FireRequested;
        public event Action AlertPresented;
        public event Action ShotPresented;

        private readonly NetworkVariable<EnemyState> state = new(EnemyState.Patrol,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<uint> alertSequence = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<uint> shotSequence = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public EnemyState State => state.Value;
        public uint AlertSequence => alertSequence.Value;
        public uint ShotSequence => shotSequence.Value;
        public uint PresentedAlertCount { get; private set; }
        public bool ServerActive => IsSpawned && IsServer && isActiveAndEnabled;
        public NetworkObject Target { get; private set; }
        public Vector3 LastKnownPosition { get; private set; }
        public bool HasVisual { get; private set; }
        public bool Engaged { get; private set; }
        public int PatrolIndex { get; private set; }
        public double FireAllowedAt { get; private set; }
        public int ClueRevision { get; private set; }
        public bool HasClue => hasClue;
        public double AlertStartedAt => alertStartedAt;

        private NavMeshAgent mover;
        private BehaviorGraphAgent graph;
        private AudioSource audioSource;
        private ReinforcementZone originZone;
        private bool hasClue, alertInProgress, signalled, pendingNoise, budgetReleased;
        private Vector3 noisePosition;
        private float noiseScore = float.PositiveInfinity;
        private double nextSense, alertStartedAt, nextFire, nextPathRetry;
        private int navigationFrame = -1;
        private NavMeshPath path;

        public T Read<T>(string name, T fallback = default)
        {
            if (graph == null) graph = GetComponent<BehaviorGraphAgent>();
            return graph != null && graph.GetVariable(name, out BlackboardVariable<T> variable) ? variable.Value : fallback;
        }
        public void Write<T>(string name, T value)
        {
            if (graph == null) graph = GetComponent<BehaviorGraphAgent>();
            if (graph != null) graph.SetVariableValue(name, value);
        }
        private float Number(string name, float fallback)
        {
            float value = Read(name, fallback);
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(0, value);
        }
        private void Update() { if (ServerActive && graph.enabled) Sense(); }

        private void Awake()
        {
            path = new NavMeshPath();
            mover = GetComponent<NavMeshAgent>();
            graph = GetComponent<BehaviorGraphAgent>();
            audioSource = GetComponent<AudioSource>();
            graph.enabled = false;
            mover.enabled = false;
        }

        public void Initialize(EnemyPatrolRoute patrolRoute, ReinforcementZone zone = null, Vector3? clue = null)
        {
            route = patrolRoute;
            originZone = zone;
            if (zone != null)
            {
                canRequestReinforcements = false;
                Engaged = true;
                FireAllowedAt = Time.timeAsDouble + alertDuration;
            }
            if (clue.HasValue) { LastKnownPosition = clue.Value; hasClue = true; ClueRevision++; }
            PublishBlackboard();
        }

        public override void OnNetworkSpawn()
        {
            alertSequence.OnValueChanged += OnAlertSequence;
            shotSequence.OnValueChanged += OnShotSequence;
            if (!IsServer) return;
            mover.enabled = true;
            if (!mover.isOnNavMesh || graph.Graph == null)
            {
                Debug.LogWarning("[적 AI] NavMesh 또는 Behavior 그래프가 없어 대기합니다.", this);
                mover.enabled = false;
                return;
            }
            ServerNoiseBus.Emitted += Hear;
            PublishBlackboard();
            graph.enabled = true;
        }

        public override void OnNetworkDespawn()
        {
            StopSimulation();
            alertSequence.OnValueChanged -= OnAlertSequence;
            shotSequence.OnValueChanged -= OnShotSequence;
            ReleasePopulation();
        }

        private void OnDisable() => StopSimulation();
        private void OnEnable()
        {
            if (!ServerActive || mover == null || graph == null || graph.Graph == null) return;
            mover.enabled = true;
            if (!mover.isOnNavMesh) { mover.enabled = false; return; }
            ServerNoiseBus.Emitted -= Hear;
            ServerNoiseBus.Emitted += Hear;
            graph.enabled = true;
            graph.Restart();
        }
        private void StopSimulation()
        {
            ServerNoiseBus.Emitted -= Hear;
            alertInProgress = false;
            signalled = true;
            if (Application.isPlaying && graph != null && graph.Graph != null) Write("AlertInProgress", false);
            if (graph != null)
            {
                if (Application.isPlaying && IsServer && graph.Graph != null) graph.End();
                graph.enabled = false;
            }
            StopMoving();
            if (mover != null) mover.enabled = false;
        }

        // Future death handlers call this once; dead enemies must then disable/despawn their AI.
        public void ReleasePopulation()
        {
            if (!IsServer || budgetReleased) return;
            budgetReleased = true;
            if (originZone != null) originZone.Release(this);
        }

        private void OnAlertSequence(uint previous, uint current)
        {
            if (current == previous) return;
            PresentedAlertCount++;
            if (audioSource != null && alertClip != null) audioSource.PlayOneShot(alertClip);
            else if (diagnostics) Debug.Log("[적 AI] 경고 시작", this);
            AlertPresented?.Invoke();
        }
        private void OnShotSequence(uint previous, uint current)
        {
            if (current == previous) return;
            if (diagnostics) Debug.Log("[적 AI] 사격 신호", this);
            ShotPresented?.Invoke();
        }

        private void Hear(AINoise noise)
        {
            if (!ServerActive || HasVisual || noise.Source == null || noise.Source.NetworkManager != NetworkManager) return;
            if (!AISpatialQuery.TryHear(Eye, noise, obstructionMask, out float score) || score >= noiseScore) return;
            noiseScore = score;
            noisePosition = noise.Position;
            pendingNoise = true;
        }

        private Vector3 Eye => transform.position + Vector3.up * Number("EyeHeight", 1.6f);
        public bool CanSee(NetworkObject target)
        {
            if (target == null || !target.IsSpawned || !target.gameObject.activeInHierarchy) return false;
            var cc = target.GetComponent<CharacterController>();
            Vector3 aim = cc != null ? cc.bounds.center : target.transform.position + Vector3.up;
            Vector3 delta = aim - Eye;
            if (delta.sqrMagnitude > sightRange * sightRange) return false;
            Vector3 horizontal = Vector3.ProjectOnPlane(delta, Vector3.up);
            return Vector3.Angle(transform.forward, horizontal) <= fieldOfView * 0.5f &&
                !AISpatialQuery.IsBlocked(Eye, aim, obstructionMask);
        }

        public void Sense()
        {
            if (!ServerActive || Time.timeAsDouble < nextSense) return;
            nextSense = Time.timeAsDouble + senseInterval;
            NetworkObject visible = CanSee(Target) ? Target : null;
            if (visible == null)
            {
                float closest = float.PositiveInfinity;
                foreach (var client in NetworkManager.ConnectedClientsList)
                {
                    NetworkObject candidate = client.PlayerObject;
                    if (!CanSee(candidate)) continue;
                    float distance = (candidate.transform.position - transform.position).sqrMagnitude;
                    if (distance < closest) { closest = distance; visible = candidate; }
                }
            }
            HasVisual = visible != null;
            if (HasVisual)
            {
                Target = visible;
                LastKnownPosition = visible.transform.position;
                hasClue = true;
            }
            else
            {
                Target = null;
                if (pendingNoise && !alertInProgress)
                {
                    LastKnownPosition = noisePosition;
                    hasClue = true;
                    ClueRevision++;
                }
            }
            pendingNoise = false;
            noiseScore = float.PositiveInfinity;
            PublishBlackboard();
        }

        public void SetState(EnemyState value)
        {
            if (!ServerActive) return;
            state.Value = value;
            if (value == EnemyState.Patrol) { Engaged = false; Target = null; }
            PublishBlackboard();
        }

        public void BeginEngagement()
        {
            if (!ServerActive || Engaged) return;
            Engaged = true;
            FireAllowedAt = Time.timeAsDouble + alertDuration;
            PublishBlackboard();
        }

        public void BeginAlert()
        {
            if (!ServerActive || alertInProgress) return;
            Engaged = true;
            alertInProgress = true;
            signalled = false;
            alertStartedAt = Time.timeAsDouble;
            FireAllowedAt = alertStartedAt + alertDuration;
            SetState(EnemyState.Alert);
            StopMoving();
            alertSequence.Value++;
            AlertStarted?.Invoke();
        }
        public void CompleteAlert()
        {
            if (!ServerActive) return;
            alertInProgress = false;
            PublishBlackboard();
        }
        public void RequestReinforcement()
        {
            if (!ServerActive || !alertInProgress || signalled) return;
            signalled = true;
            if (!canRequestReinforcements) return;
            ReinforcementRequested?.Invoke(LastKnownPosition);
            ReinforcementZone.Request(this, LastKnownPosition);
        }
        public bool TryFire()
        {
            if (!ServerActive || alertInProgress || Time.timeAsDouble < nextFire ||
                Time.timeAsDouble < FireAllowedAt || !CanSee(Target) ||
                Vector3.Distance(transform.position, Target.transform.position) > firingRange) return false;
            nextFire = Time.timeAsDouble + fireInterval;
            shotSequence.Value++;
            FireRequested?.Invoke(Target);
            return true;
        }
        public bool TryGetPatrolPoint(out Vector3 position)
        {
            position = transform.position;
            if (route == null || route.points.Length == 0) return false;
            PatrolIndex %= route.points.Length;
            Transform point = route.points[PatrolIndex];
            if (point == null) return false;
            position = point.position;
            return true;
        }
        public void AdvancePatrol()
        {
            if (route != null && route.points.Length > 0) PatrolIndex = (PatrolIndex + 1) % route.points.Length;
            PublishBlackboard();
        }
        public void ForgetClue(int investigatedRevision)
        {
            if (!ServerActive || HasVisual || investigatedRevision != ClueRevision) return;
            hasClue = false; Engaged = false; Target = null;
            StopMoving(); PublishBlackboard();
        }
        public bool Navigate(Vector3 destination, float speed)
        {
            if (mover == null || !mover.enabled || !mover.isOnNavMesh) return false;
            mover.speed = speed;
            if (navigationFrame == Time.frameCount) return true;
            navigationFrame = Time.frameCount;
            if (mover.hasPath && Time.timeAsDouble < nextPathRetry &&
                (mover.destination - destination).sqrMagnitude < Mathf.Pow(Number("DestinationTolerance", .5f), 2))
            { mover.isStopped = false; return true; }
            nextPathRetry = Time.timeAsDouble + Number("PathRefreshInterval", .2f);
            if (!NavMesh.SamplePosition(destination, out var hit, Number("NavMeshSampleRadius", 1.5f), mover.areaMask) ||
                !mover.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            mover.isStopped = false;
            mover.SetPath(path);
            return true;
        }

        public bool Arrived() => !mover.pathPending && mover.remainingDistance <= mover.stoppingDistance + Number("ArrivalTolerance", .15f);
        public void StopMoving()
        {
            if (mover != null && mover.enabled && mover.isOnNavMesh) mover.isStopped = true;
        }
        public void Face(Vector3 position)
        {
            Vector3 direction = Vector3.ProjectOnPlane(position - transform.position, Vector3.up);
            if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), Number("TurnSpeed", 360) * Time.deltaTime);
        }
        private void PublishBlackboard()
        {
            if (graph == null || graph.Graph == null) return;
            graph.SetVariableValue("Target", Target != null ? Target.gameObject : null);
            graph.SetVariableValue("LastKnownPosition", LastKnownPosition);
            graph.SetVariableValue("PatrolIndex", PatrolIndex);
            graph.SetVariableValue("Engaged", Engaged);
            graph.SetVariableValue("HasVisual", HasVisual);
            graph.SetVariableValue("HasClue", hasClue);
            graph.SetVariableValue("AlertInProgress", alertInProgress);
            graph.SetVariableValue("ClueRevision", ClueRevision);
            graph.SetVariableValue("InFiringRange", HasVisual && Vector3.Distance(transform.position, LastKnownPosition) <= firingRange);
            graph.SetVariableValue("FireReady", Time.timeAsDouble >= FireAllowedAt && Time.timeAsDouble >= nextFire);
            graph.SetVariableValue("AttackAllowed", Time.timeAsDouble >= FireAllowedAt);
            graph.SetVariableValue("FireAllowedAt", (float)FireAllowedAt);
        }
    }
}

