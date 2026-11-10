# 기본 체력

기준: 캐릭터 머지 `912c8ef2` + 4431 worktree의 기존 AI 전투 확장.
2026-10-21 사용자 요청으로 4431에 캐릭터 머지본과 체력 변경을 반영했다. 기존 변경은 Logs/HealthTransfer-20261021-164729에 백업했고 복구 씬은 보존했다.

- 제품 Player와 AI TestPlayer에 `PlayerHealth`, 한국어 `PlayerHealthHud` 연결.
- 플레이어 기본/최대 체력 100. 자동 회복·부활은 없음. 새 네트워크 스폰은 초기화하며 씬 이동 자체는 초기화하지 않음.
- 서버만 `ApplyDamage(amount, attacker)`를 호출한다. 이 API는 이미 계산한 최종 피해를 받는다.
- 부위 피해는 플레이어/적 모두 `ApplyHit(baseDamage, HitRegion.Head/Body, attacker)`를 사용한다. 머리 2배, 나머지 1배. 배율을 미리 곱해서 전달하지 않는다.
- 0/음수/NaN/무한 피해, 유효하지 않은 부위, 클라이언트 호출, 사망 후 피해를 거부한다.
- 체력은 서버 쓰기 NetworkVariable. 플레이어 사망 이벤트는 서버에서 한 번만 발생한다.
- 사망 시 서버의 특수 이동·이동 속도·기울이기를 정리하며 이후 이동 RPC를 거부한다. 로컬 입력 재활성화도 거부한다.
- 사망한 플레이어의 NetworkObject는 세션/카메라 유지를 위해 남긴다. AI 시야·전투 타깃 유효성에서는 제외한다.
- 적의 기존 회복·예약 해제·증원 인원 반환 동작을 유지한다.
- 추가 RPC 없음. 실제 총기·레이캐스트 부위 선택·머리 콜라이더·사망 애니메이션은 이번 체력 기반에 포함되지 않는다.

## 연결 및 검증

`Tools > OSKAR MIKE > Combat > 기본 체력 프리팹 연결`은 제품 Player에 컴포넌트를 연결하고,
해당 Player에서 생성형 TestPlayer를 다시 저장한다. 테스트 프리팹 수동 편집이 있다면 먼저 보존한다.
AI 씬·적 프리팹·Behavior Graph를 재생성하지 않는다.

`Tools > OSKAR MIKE > AI > 서버 체력 검증`은 실제 NGO 호스트에서 적 회복/사망과
양측 몸통/머리 피해, 플레이어 사망·입력 재활성화 차단·AI 감지 제외를 검증한다.
배치 진입점: `OskarMike.AI.Editor.EnemyCombatHealthValidation.Run` (`-quit` 제외).

수동 확인: `EnemyAITest`에서 호스트 시작 → 스폰된 Player의 `PlayerHealth` 컴포넌트 메뉴에서
`체력 테스트/몸 피해 10` 또는 `머리 피해 20 (기본 10)` 선택.
이 메뉴는 에디터에만 존재하며, 클라이언트에서는 서버 피해 API가 요청을 거부한다.

실제 4인 체력 동기화, 늦은 접속, UGS MainMenu→Lobby→GameMap, 호스트 종료,
슬라이드/다이브/넘기 도중 사망과 HUD 시각 확인은 별도 수동 검증 대상이다.
