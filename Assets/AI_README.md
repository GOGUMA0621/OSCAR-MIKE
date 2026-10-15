# 적 AI 프로토타입

## 실행

Unity 6000.3.9f1에서 `Assets/Scenes/EnemyAITest.unity`를 열고 Play 후 **호스트 시작**을 누른다.
다른 실행 파일에서는 주소를 입력하고 **클라이언트 접속**을 누른다. 로컬 테스트는 `127.0.0.1:7777`을 사용하며 최대 4명이다.
ESC로 입력/마우스 잠금을 전환한다. 이 테스트 세션은 UTP 직접 접속이며 제품의 UGS 세션 흐름을 대체하지 않는다.

- `Assets/AI/EnemyBehavior.asset`: Start(반복) → Try In Order → 경고 / 전투 / 조사 / 순찰. 조건과 우선순위는 그래프에서 편집한다.
- `Assets/AI/Behaviors/`: Patrol / Investigate / Alert / Combat 서브그래프. Sequence, Conditional Guard, Abort, Restart, Repeat로 실제 흐름을 구성한다.
- 수치 변경과 그래프 편집 방법: [일반 몹 그래프 안내](AI/GENERAL_ENEMY_GRAPH.md).
- `Assets/AI/Prefabs/PatrolEnemy.prefab`: 순찰과 증원에 함께 사용하는 적. 증원 시 서버가 재호출을 금지한다.
- `Assets/AI/Definitions/`: Walk / Sprint / Crouch / Prone 소리 정의. 반경과 간격을 개별 조절한다.
- 씬의 **증원 구역**: 프리팹, 스폰 지점, 복귀 경로, 호출 거리, 쿨다운과 생존 상한 설정.

그래프·프리팹·테스트 씬을 다시 만들려면 **Tools → OSKAR MIKE → AI → 테스트 씬 생성**을 사용한다.
이 명령은 생성된 테스트 씬과 프리팹을 재구성하므로 해당 에셋에 수동 편집을 했다면 먼저 보존한다. 기존 소리 정의와 그래프는 재사용한다.

## 동작

순찰 적만 처음 생성된다. 시각 발견 후 1.5초 경고하며 1.2초에 근처의 가용 증원 구역 하나로 요청한다.
시야를 잃어도 경고와 요청은 완료하지만, 사격은 시야·거리·쿨다운을 매번 통과해야 한다.
경고 도중 디스폰/비활성화하면 아직 보내지 않은 요청은 취소된다.

소리는 오디오 볼륨이 아닌 서버 이벤트다. 걷기 5m, 달리기 12m, 웅크리기 2m, 엎드리기 1m가 기본이다.
시야 차폐 콜라이더가 있으면 반경을 50%로 줄인다. 캐릭터 콜라이더와 트리거는 차폐에서 제외한다.
소리로는 당시 위치만 조사하며 직접 플레이어를 보기 전에는 경고·사격하지 않는다.
서버의 입력 이동 결과를 발소리 컴포넌트에 전달하기 때문에 이동 발판 자체 이동에는 발소리가 없다.

증원 구역은 회당 2마리, 쿨다운 30초, 구역별 생존 4마리가 기본이다.
막혔거나 NavMesh가 없는 지점은 건너뛰며 요청을 저장하지 않는다. 스폰된 증원은 마지막 목격 위치로 이동한다.
직접 시야를 확보하고 스폰 후 1.5초가 지난 뒤에만 사격할 수 있으며, 추가 증원을 부르지 않는다.
조사가 끝나면 구역의 순찰 경로로 돌아가고 생존 슬롯을 계속 차지한다.

## 확장 연결점

- `EnemyAgent.AlertStarted`: 서버 경고 시작 이벤트.
- `EnemyAgent.ReinforcementRequested(Vector3)`: 서버 경고 후반에 보내는 마지막 목격 위치. 기본 구역 선택·스폰은 이미 연결되어 있다.
- `EnemyAgent.FireRequested(NetworkObject)`: 서버 사격 신호. 피해·총알은 아직 구현하지 않았다.
- `AlertPresented`, `ShotPresented`: 클라이언트 표시 이벤트. 시퀀스 NetworkVariable은 최신 상태를 전달하므로 높은 발사 빈도의 모든 개별 효과를 보장하는 전송 큐는 아니다.
- `ReleasePopulation()`: 향후 사망 로직의 슬롯 반환 지점. 중복 호출은 안전하며, 사망 시 AI를 중지하거나 디스폰해야 한다.
- `ServerNoiseBus.Emit()`: 새로운 소리 정의를 서버에서 발행하는 진입점. NGO 스폰 객체만 출처로 허용한다.

모든 행동 판단·NavMesh 이동·발소리 감지·증원 생성은 서버 권한이다. 새 RPC는 없다.
제품용 Player에는 발소리 컴포넌트만 추가된다. 입력 예외 컴포넌트는 TestPlayer에만 붙는다.
경고 음원은 프리팹의 `Alert Clip`에 연결한다. 미지정 시 한국어 로그로 표시한다.

## 검증

2026-10-02 Unity 6000.3.9f1 에디터 및 Windows 개발 빌드에서 그래프 전환 후 통합 검증 41항목을 통과했다.
별도 프로세스의 호스트 + 클라이언트 3개로 4인 스폰, 적 3마리 동기화, 경고·사격 시퀀스 수신,
클라이언트 AI/이동 비활성화 및 호스트 종료를 확인했다. 늦게 접속한 클라이언트는 이전 경고음을 재생하지 않았다.
이번 변경의 독립 실행 검증 결과는 `Logs/GraphValidation/Logs/`에 있다.
빌드는 오류 0개로 성공했다. 기존 PlayerNetworkController 미사용 필드, Sentis 셰이더 및 MCP 연결 경고가 포함되어 경고 없는 빌드는 아니다.
기존 UGS MainMenu/Lobby 흐름과 실제 입력에 따른 발소리 발생 회귀는 아래 수동 항목으로 남는다.

**Tools → OSKAR MIKE → AI → 자동 검증**은 테스트 씬을 플레이 모드에서 실행하고 결과를 `Logs/ai-validation.txt`에 기록한다.
배치 실행 시 `-executeMethod OskarMike.AI.Editor.EnemyAIValidation.Run`을 사용하고 `-quit`은 붙이지 않는다.
에디터 배치 플레이 전환이 시간 초과되는 환경에서는 `EnemyAITestBuilder.BuildExecutable`로 빌드한 뒤
`Builds/AI/EnemyAITest.exe -batchmode -nographics -aiValidate`로 동일한 검증을 실행한다.
네트워크 검증은 실행 파일의 `-aiNetworkHost`, `-aiNetworkClient -aiId client1` 옵션을 사용한다.
호스트의 `Logs/ai-network-ready.txt` 생성 후 마지막 클라이언트를 `-aiNetworkClient -aiId late -aiLate`로 접속한다.

수동 회귀 항목:

1. 호스트 + 클라이언트 3명 접속과 개별 스폰, 적 위치·경고·사격 상태 동기화.
2. 경고/사격 후 늦게 접속했을 때 과거 소리가 재생되지 않는지 확인.
3. 움직임 중 스태미나 소진, 벽에 막힘, 점프, 발판 이동의 발소리 분류.
4. 경고 도중 타깃 연결 해제와 호스트 종료, 재시작 후 초기 상태.
5. MainMenu → Lobby → GameMap, 준비/시작, 참가 코드 표시, 4인 스폰과 호스트 종료.

범위 밖: 실제 발소리 음원 제작, 탄환/명중/체력/사격 효과, 절차적 맵 연결.
