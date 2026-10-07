# 일반 몹 Behavior 그래프

`Assets/AI/EnemyBehavior.asset`을 Unity Behavior에서 연다. 행동 선택은 이 그래프의 Try In Order에서, 행동 순서는 `Behaviors/`의 서브그래프에서 수정한다. `EnemyDecision`, `ChooseState()`, `TickAction()`에 의존하지 않는다.

## 수치 조정

일반 몹 전체의 기본값은 **메인 그래프 Blackboard**에서 바꾼다. 특정 프리팹/개체만 다르게 만들려면 **Behavior Graph Agent의 노출 변수 오버라이드**를 사용한다. EnemyAgent Inspector에 같은 수치를 중복 저장하지 않는다.

| 변수 | 기본값 | 의미 |
|---|---:|---|
| PatrolSpeed | 2 | 순찰 속도(m/s) |
| ChaseSpeed | 4 | 추격·조사 속도(m/s) |
| SightRange | 12 | 시야 거리(m) |
| FiringRange | 10 | 사격 거리(m) |
| FieldOfView | 100 | 수평 시야각(도) |
| SenseInterval | 0.1 | 감지 간격(초), 최소 0.02 |
| AlertDuration | 1.5 | 경고 시간 및 최초 교전 사격 유예(초) |
| SignalProgress | 0.8 | 경고 시작부터 증원 요청까지의 비율(0~1) |
| FireInterval | 1.5 | 최소 사격 간격(초), 최소 0.02 |
| WaypointWait | 1 | 순찰 지점 대기(초) |
| InvestigationWait | 2 | 조사 위치 도착 후 대기(초) |
| EyeHeight | 1.6 | 감지 시작점 높이(m) |
| TurnSpeed | 360 | 조준 회전 속도(도/초) |
| PathRefreshInterval | 0.2 | 같은 목적지 경로 갱신 간격(초) |
| DestinationTolerance | 0.5 | 목적지가 이 거리 이상 바뀌면 즉시 경로 갱신(m) |
| NavMeshSampleRadius | 1.5 | 목적지 주변 NavMesh 탐색 반경(m) |
| ArrivalTolerance | 0.15 | NavMeshAgent 정지 거리에 더하는 도착 여유(m) |
| CanRequestReinforcements | true | 증원 요청 허용. 증원으로 생성된 개체는 서버가 false로 설정 |

거리·시간·속도는 유한한 0 이상 값으로 설정한다. 시야각은 1~360, SignalProgress는 0~1로 설정한다. 0 속도는 이동을 멈추므로 도착을 기다리는 행동이 계속 실행될 수 있다.

서브그래프의 노출 변수는 메인 그래프의 같은 변수에 연결되어 있다. 따라서 메인 그래프를 통해 실행할 때는 **메인 값 또는 개체 오버라이드가 적용**된다. 서브그래프의 기본값만 바꾸면 메인 그래프의 설정은 바뀌지 않는다. 실행 중 속도/시야 변경은 다음 처리에 반영된다. 대기 노드는 시작 시 시간을 읽으므로 이미 진행 중인 대기는 다시 시작해야 변경된다. 사격 유예·쿨다운도 시작 시각 기준으로 예약된다.

순찰 경로·차폐 레이어·음원은 EnemyAgent에, 몸체 크기·정지 거리는 NavMeshAgent에 유지한다. 발소리 반경은 기존 소리 정의 에셋에, 증원 수·구역별 상한·호출 거리는 ReinforcementZone에 유지한다. 이 값들은 일반 몹 개인의 행동 수치가 아닌 공유 시스템 설정이다.

## 흐름

- **메인:** 최초 발견 + 미교전 + 증원 허용 → 경고, 시야 있음 → 전투, 단서 있음 → 조사, 나머지 → 순찰. Conditional Guard의 Lower Priority 관찰로 낮은 우선순위 행동을 중단한다.
- **경고:** 시작 → AlertDuration × SignalProgress 시점까지 대기 → 증원 한 번 요청 → AlertDuration 시점까지 대기 → 완료. 시야를 잃어도 끝까지 실행한다. 디스폰/비활성화 시 취소된다.
- **전투:** 교전 시작 → 시야를 잃으면 Abort → 반복 선택. 사거리 안이고 초기 유예가 끝나면 사격 상태로 조준·사격을 시도한다. 쿨다운 중에도 사격 상태를 유지하며 실제 발사는 C#에서 재검증한다. 사거리 안에서 초기 유예 중이면 정지·조준, 사거리 밖이면 추적한다.
- **조사:** 단서 위치·번호 저장 → 조사 상태 → 이동 → 대기 → 그 단서 해제. 새 소리가 오면 Restart로 새 단서를 저장하고 다시 조사한다. 이동 실패 시 대기를 건너뛰고 해당 단서를 해제한다. 플레이어 발견 시 상위 그래프가 중단한다.
- **순찰:** 순찰 상태 → 위치 선택 → 이동 → 대기 → 다음 지점. 없는/도달 불가능한 지점은 다음 지점으로 넘어간다.

Sequence의 실행 순서는 자식 배치 순서에 영향을 받으므로 노드 위치를 바꿀 때 순서를 확인한다. 이동 노드는 도착 시 Success, 경로 실패 시 Failure, 이동 중 Running을 반환한다. Succeeder는 예상 가능한 이동 실패·사격 쿨다운으로 상위 흐름이 잘못 종료되지 않도록 사용한다.

## Blackboard 상태값

`HasVisual`, `HasClue`, `Engaged`, `AlertInProgress`, `InFiringRange`, `AttackAllowed`, `FireReady`, `Target`, `LastKnownPosition`, `FireAllowedAt`, `PatrolIndex`, `ClueRevision`는 런타임 코드가 갱신한다. `PatrolDestination`, `InvestigationDestination`, `InvestigatedRevision`는 그래프가 저장한다. 이 값들은 설정값이 아니므로 기본값 변경으로 AI를 조정하지 않는다.

변수의 **Shared는 사용하지 않는다.** 같은 그래프를 사용하는 여러 적도 각자 독립적인 값과 타깃을 갖는다. 서브그래프 연결은 같은 적 내부에서만 값을 공유한다.

## 코드와 네트워크

EnemyAgent는 감지, NavMesh 이동, 사격 검증, 증원 시스템 호출, NGO 생명주기를 제공한다. 작은 Action 노드는 이 기능을 호출하며 행동 선택은 하지 않는다. 서버에서만 그래프와 NavMesh를 실행하고 기존 NetworkTransform/서버 쓰기 NetworkVariable로 결과를 전달한다. Blackboard는 자동 네트워크 복제 대상이 아니다.

`Tools → OSKAR MIKE → AI → 일반 몹 그래프 구성`은 이미 구성된 그래프의 편집 내용을 유지하며 컴파일한다. 테스트 씬 생성 메뉴는 씬/프리팹을 재구성하므로 그래프 편집만 할 때는 사용하지 않는다. `EnemyBehaviorGraphBuilder.Migrate()`는 초기 구성을 다시 작성하는 개발용 함수이므로 수동 편집 후 호출하지 않는다.

## 검증

AI 자동 검증은 기존 네트워크·감지·증원 검사와 함께 Blackboard 개체별 격리, 프리팹 기본값 보존, 실제 감지 거리 변경, 서브그래프 속도/경고 시간/조사 대기 연결, 새 단서로 조사 재시작, 비활성화 취소를 검사한다. 최신 결과는 `Logs/ai-validation.txt`에 저장된다.
