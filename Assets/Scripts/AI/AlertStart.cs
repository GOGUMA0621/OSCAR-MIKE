using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

namespace OskarMike.AI
{
    [Serializable, GeneratePropertyBag]
    [Condition(name: "블랙보드 불리언 비교", story: "[Value] 값이 [Expected]", category: "OSKAR MIKE/조건", id: "061a5bba47714daeb606d9df11c50201")]
    public partial class EnemyFlagCondition : Condition
    {
        [SerializeReference] public BlackboardVariable<bool> Value;
        [SerializeReference] public BlackboardVariable<bool> Expected = new(true);
        public override bool IsTrue() => Value != null && Value.Value == Expected.Value;
    }

    [Serializable, GeneratePropertyBag]
    [Condition(name: "새 조사 단서", story: "[Current] 단서와 [Captured] 단서가 다름", category: "OSKAR MIKE/조건", id: "061a5bba47714daeb606d9df11c50202")]
    public partial class EnemyClueChangedCondition : Condition
    {
        [SerializeReference] public BlackboardVariable<int> Current;
        [SerializeReference] public BlackboardVariable<int> Captured;
        public override bool IsTrue() => Current.Value != Captured.Value;
    }

    [Serializable]
    public abstract class EnemyGraphAction : Action
    {
        protected EnemyAgent Enemy;
        protected sealed override Status OnStart()
        {
            Enemy = GameObject.GetComponent<EnemyAgent>();
            return Enemy != null && Enemy.ServerActive ? Begin() : Status.Failure;
        }
        protected virtual Status Begin() => Status.Success;
    }

    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Alert Start", story: "경고 시작", category: "Action", id: "061a5bba47714daeb606d9df11c50301")]
    public partial class EnemySetState : EnemyGraphAction
    {
        [SerializeReference] public BlackboardVariable<EnemyState> State;
        protected override Status Begin() { Enemy.SetState(State.Value); return Status.Success; }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "교전 시작", category: "OSKAR MIKE/전투", id: "061a5bba47714daeb606d9df11c50302")]
    public partial class EnemyBeginEngagement : EnemyGraphAction
    {
        protected override Status Begin() { Enemy.BeginEngagement(); return Status.Success; }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "경고 시작", category: "OSKAR MIKE/경고", id: "061a5bba47714daeb606d9df11c50303")]
    public partial class EnemyBeginAlert : EnemyGraphAction
    {
        protected override Status Begin() { Enemy.BeginAlert(); return Status.Success; }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "경고 시점 대기", story: "경고 [Duration] 초의 [Progress] 비율까지 대기", category: "OSKAR MIKE/경고", id: "061a5bba47714daeb606d9df11c50304")]
    public partial class EnemyWaitAlert : EnemyGraphAction
    {
        [SerializeReference] public BlackboardVariable<float> Duration = new(1.5f);
        [SerializeReference] public BlackboardVariable<float> Progress = new(1);
        protected override Status Begin() => OnUpdate();
        protected override Status OnUpdate()
        {
            if (!Enemy.ServerActive) return Status.Failure;
            Enemy.StopMoving(); Enemy.Face(Enemy.LastKnownPosition);
            return Time.timeAsDouble >= Enemy.AlertStartedAt + Mathf.Max(0, Duration.Value) * Mathf.Clamp01(Progress.Value)
                ? Status.Success : Status.Running;
        }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "증원 요청", category: "OSKAR MIKE/경고", id: "061a5bba47714daeb606d9df11c50305")]
    public partial class EnemyRequestReinforcement : EnemyGraphAction
    {
        protected override Status Begin() { Enemy.RequestReinforcement(); return Status.Success; }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "경고 완료", category: "OSKAR MIKE/경고", id: "061a5bba47714daeb606d9df11c50306")]
    public partial class EnemyCompleteAlert : EnemyGraphAction
    {
        protected override Status Begin() { Enemy.CompleteAlert(); return Status.Success; }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "순찰 위치 선택", story: "다음 순찰 위치를 [Destination] 에 저장", category: "OSKAR MIKE/이동", id: "061a5bba47714daeb606d9df11c50307")]
    public partial class EnemyPickPatrolPoint : EnemyGraphAction
    {
        [SerializeReference] public BlackboardVariable<Vector3> Destination;
        protected override Status Begin()
        {
            if (!Enemy.TryGetPatrolPoint(out var point)) return Status.Failure;
            Destination.Value = point; return Status.Success;
        }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "다음 순찰 지점", category: "OSKAR MIKE/이동", id: "061a5bba47714daeb606d9df11c50308")]
    public partial class EnemyAdvancePatrol : EnemyGraphAction
    {
        protected override Status Begin() { Enemy.AdvancePatrol(); return Status.Success; }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "위치로 이동", story: "[Destination] 까지 속도 [Speed] 로 이동", category: "OSKAR MIKE/이동", id: "061a5bba47714daeb606d9df11c50309")]
    public partial class EnemyMoveTo : EnemyGraphAction
    {
        [SerializeReference] public BlackboardVariable<Vector3> Destination;
        [SerializeReference] public BlackboardVariable<float> Speed;
        protected override Status Begin() => OnUpdate();
        protected override Status OnUpdate()
        {
            if (!Enemy.ServerActive || !Enemy.Navigate(Destination.Value, Mathf.Max(0, Speed.Value))) return Status.Failure;
            return Enemy.Arrived() ? Status.Success : Status.Running;
        }
        protected override void OnEnd() { if (Enemy != null) Enemy.StopMoving(); }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "조사 단서 저장", story: "단서를 [Destination] 과 [Revision] 에 저장", category: "OSKAR MIKE/조사", id: "061a5bba47714daeb606d9df11c50310")]
    public partial class EnemyCaptureClue : EnemyGraphAction
    {
        [SerializeReference] public BlackboardVariable<Vector3> Destination;
        [SerializeReference] public BlackboardVariable<int> Revision;
        protected override Status Begin()
        {
            Destination.Value = Enemy.LastKnownPosition; Revision.Value = Enemy.ClueRevision; return Status.Success;
        }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "조사 단서 해제", story: "조사한 [Revision] 단서 해제", category: "OSKAR MIKE/조사", id: "061a5bba47714daeb606d9df11c50311")]
    public partial class EnemyClearClue : EnemyGraphAction
    {
        [SerializeReference] public BlackboardVariable<int> Revision;
        protected override Status Begin() { Enemy.ForgetClue(Revision.Value); return Status.Success; }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "대상 추적 이동", story: "대상을 속도 [Speed] 로 추적", category: "OSKAR MIKE/이동", id: "061a5bba47714daeb606d9df11c50312")]
    public partial class EnemyFollowTarget : EnemyGraphAction
    {
        [SerializeReference] public BlackboardVariable<float> Speed;
        protected override Status Begin() => OnUpdate();
        protected override Status OnUpdate()
        {
            if (!Enemy.ServerActive || !Enemy.HasVisual) return Status.Failure;
            if (!Enemy.Navigate(Enemy.LastKnownPosition, Mathf.Max(0, Speed.Value))) return Status.Failure;
            return Status.Running;
        }
        protected override void OnEnd() { if (Enemy != null) Enemy.StopMoving(); }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "정지 후 조준", category: "OSKAR MIKE/전투", id: "061a5bba47714daeb606d9df11c50313")]
    public partial class EnemyAim : EnemyGraphAction
    {
        protected override Status Begin() { Enemy.StopMoving(); Enemy.Face(Enemy.LastKnownPosition); return Status.Success; }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "한 발 사격", category: "OSKAR MIKE/전투", id: "061a5bba47714daeb606d9df11c50314")]
    public partial class EnemyFire : EnemyGraphAction
    {
        protected override Status Begin() => Enemy.TryFire() ? Status.Success : Status.Failure;
    }
}
