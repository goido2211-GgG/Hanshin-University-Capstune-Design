# VoiceAI 리팩터링 계획

기준일: 2026-11-09 (Asia/Seoul). **모든 코드 변경 항목은 미착수**이다. 이번 완료 범위는 개발 문서 작성뿐이다.

현재 근거와 호출 관계는 [ARCHITECTURE.md](ARCHITECTURE.md), 확정 게임 규칙은 [ROBOT_DESIGN.md](ROBOT_DESIGN.md), 사례별 시험은 [TEST_PLAN.md](TEST_PLAN.md)를 따른다. 게임 규칙이 이전 제안과 다르면 최신 ROBOT_DESIGN의 확정 규칙을 우선한다.

## 1. 범위와 최소 구성

1차 범위는 명령 실행·취소·유효성·의도 보고·1초 대기와 기존 이벤트 호환성이다. 신규 실행 클래스는 `AgentActionRunner`, `AgentCommandRules` 두 개로 제한한다. DTO는 기존 `AgentModels.cs`에 추가한다. 범용 EventBus나 대형 서비스 계층은 도입하지 않는다.

2차 범위는 탐색 타이머·후보 선택과 시험 퍼즐이다. 신규 `AgentExplorationController` 및 퍼즐 컴포넌트는 1차 Runner 계약이 검증된 뒤 추가한다. Kyle 이동 교체는 실제 설치본을 확인한 뒤 별도 진행한다.

## 2. 기존 파일별 변경 제안

경로 접두사: `Assets/VoiceAI/`. 아래는 계획이며 실제 수정 이력이 아니다.

| 파일 / 클래스 | 변경 내용 |
|---|---|
| `Runtime/Core/AgentModels.cs` | `AgentCommandHandle(SessionId, CommandId, Source)`, `AgentCommandSource`, `AgentExecutionResult`, 실행 상태·종료 사유 DTO 추가. `AgentTurnResult`에 작업 식별 추가 |
| `Runtime/Core/AgentResponseParser.cs` / `AgentResponseParser` | 구조 검사와 단일 행동 검사 분리. 필수 item·drop 소지·asking/refused·null·미지원 동사 방어. 최신 상태를 받는 실행 검증 경로 |
| `Runtime/Unity/SceneStageContext.cs` / `SceneStageContext` | 대상 ID 조회·중복 ID 오류·스테이지 격리. 가상 데이터와 실제 실행 대상을 구분 |
| `Runtime/Unity/AgentInteractable.cs` / `AgentInteractable` | 필요 시 활성 등록 중복 방어·상태 조회 계약 보강. 퍼즐 로직은 전용 컴포넌트로 위임 |
| `Runtime/Unity/AgentBodyController.cs` / `AgentBodyController` | TurnCompleted 구독·RunActions·작업 교체 책임 제거. 단일 행동 실행·명시적 취소·성공/실패 반환. 이동 중 유효성·시간 초과·접근 후 검증 |
| `Runtime/Core/AgentBrain.cs` / `AgentBrain` | `PrepareTurnAsync`와 `CommitTurn` 제안. 요청 스냅샷과 최신 결과만의 신뢰도·기억 확정. 중단된 요청의 공유 상태 변경 방지 |
| `Runtime/Unity/VoiceAgent.cs` / `VoiceAgent` | 요청별 연결 토큰·Runner 연결·RequestStop·최신 ID 검사. 기존 입력 상태와 실행 상태 분리. Reset/Disable/Destroy 정리 |
| `Runtime/Unity/OpenAIClient.cs` / `OpenAIClient` | 전송 전·응답 후·재시도 경계 취소 검사. 오래된 오류·응답이 새 작업에 반영되지 않는지 점검 |
| `Runtime/Unity/MicrophoneRecorder.cs` / `MicrophoneRecorder` | 취소 반복 호출 안전성 점검. VoiceAgent에서 Stop 예외를 잡도록 연동. 장치 권한 개선은 별도 범위 |
| `Runtime/Core/FrustrationDirector.cs` / `FrustrationDirector`, `TurnDirective` | 무작위 강제 오해를 새 경로에서 비활성화. 타입 보존·대체 여부는 참조 이전 후 결정 |
| `Runtime/Core/AgentRequestBuilder.cs` / `AgentRequestBuilder` | 명확성·규칙 판정 전달. 계획 아이템 ID 범위와 실행 시 소지 검사 분리. 복합 행동 지원 범위 명시 |
| `Resources/VoiceAI/Prompts/agent_system.txt` | 말투 유지. 정확한 지시 고의 오해 금지·규칙 기반 질문·안전 우선으로 정비 |
| `Runtime/Unity/VoiceAgentDebugUI.cs` / `VoiceAgentDebugUI` | 항상 동작하는 중지 버튼·키, 작업 ID·의도·실행 상태·결과 표시. 이벤트 해제·로그 제한 |
| `README.md` | 실제 지원 버전·구현 동사·가상 대상 한계·이벤트와 취소 의미 갱신 |

`WavEncoder`, `MultipartFormData`, `AgentApiException`은 기본 재사용 대상이다. 키 로더·인증 배포 변경은 이번 실행 리팩터링의 필수 범위가 아니다. 비밀 파일 수정·복사는 하지 않는다.

## 3. 신규 파일 제안

| 제안 파일 | 책임 / 도입 단계 |
|---|---|
| `Runtime/Unity/AgentActionRunner.cs` | MonoBehaviour. 작업 ID·우선순위·단일 실행 소유권·의도·대기·검증·종료. 1차 |
| `Runtime/Core/AgentCommandRules.cs` | 순수 규칙: 로컬 중지·명확성·대상 우선순위·조건부 오해. 1차 |
| `Runtime/Unity/AgentExplorationController.cs` | 시작/일반 명령 완료 3초 타이머·후보 선택·중지 억제. Runner로 이동·조사만 제출. 2차 |
| `Runtime/Unity/Puzzles/` 아래 퍼즐 클래스 | 시험 상태 저장 및 버튼·서랍·열쇠·문 조작 검증. 클래스 수·이름은 미확정. 2차 |
| `Tests/` 아래 테스트와 테스트 어셈블리 | 지연·역순 가짜 응답, 취소, 단계별 상태 변화·우선순위 검증. 향후 테스트 도구 도입 시 작성 |

실제 어셈블리 구성과 신규 .meta는 구현 단계에서 결정한다. 이번 문서 작업에서는 추가하지 않았다. RobotMovementAdapter·인식·지식·힌트 클래스를 지금 모두 만들지 않고 책임이 실제로 필요해지는 단계에 도입한다.

## 4. 핵심 계약

### 작업과 중지

- Runner가 작업 핸들을 발급하고 현재 작업을 소유한다. VoiceAgent는 해당 핸들에 연결된 요청 토큰을 소유한다.
- 입력 접수 시 기존 작업·탐색 타이머를 무효화하고 Body를 멈춘다. 음성은 녹음 시작 시 선점한다.
- 중지는 초기화 실패·녹음·STT·LLM·대기·실행 상태 모두에서 가능하다. 로컬 전체 문장 일치와 버튼·키로 LLM을 우회한다.
- Stop은 현재 계획 전체를 끝낸다. `[stop, move_to]`의 뒤 이동은 실행하지 않는다.
- 실제 성공한 집기·버튼 조작을 취소로 되돌리지 않는다. 실행 종료 통지는 정확히 한 번이다.
- 이전 작업이 종료 이벤트나 오류를 늦게 보내도 새 작업·탐색 타이머를 변경하지 못한다.

### 응답 확정과 실행

`PrepareTurnAsync`는 외부 호출과 파싱만 하고 기록·신뢰도·오해 상태를 확정하지 않는다. 최신 세션·명령·토큰 검사 후 메인 스레드에서 확정과 Runner 제출을 연속 처리한다. commit 시 현재 기록에 추가하여 준비 이후 발생한 유효 실행 사건을 덮어쓰지 않는다. 응답 결과는 Runner의 전용 제출 경로로 넘기며 일반 UI 이벤트가 실행 권한을 갖지 않는다.

Runner는 구조 검사→현재 행동 검증→의도→1초 대기→재검증→Body 실행→결과 기록 순으로 처리한다. 실제 상태 변경 직전에도 재검증한다. 처음부터 지원하지 않는 동사는 거부한다. 실행 실패는 나머지 행동을 중단한다.

### 호환성

기존 `TurnCompleted` 시그니처와 해석 완료 의미는 유지한다. 취소·폐기 응답은 발행하지 않는다. Body 구독 제거와 새 경로 활성화는 한 단위로 전환해 이중 실행을 막는다. 새 `IntentAnnounced`, `ExecutionStateChanged`, `ActionFinished`, `CommandFinished` 이벤트를 제안한다.

`SendChat`의 bool은 새 명령 접수 여부가 된다. 처리 중에도 새 명령으로 대체하면 true이다. 외부 Actions 직접 실행 구독자는 Runner로 이전해야 한다. UI 구독자의 예외가 API 실패나 이중 종료를 일으키지 않도록 알림 경계를 분리한다.

## 5. 구현 순서와 단계별 통과 조건

| 단계 | 선행 조건 / 작업 | 테스트 통과 조건 |
|---|---|---|
| P0 기준 보존 | 기존 사용자 변경·미추적 파일 목록 확인. 복원본·기준 확보 | 기존 변경 소실 없음, 비밀 미포함, 현재 동작과 미검증 범위 기록 |
| P1 모델·검증 | P0 → DTO, 구조 검사, 단일 행동 검사·대상 조회 | V01~V04: null/잘못된 동사·item·중복 ID 거부, 순차 인벤토리 갱신 검사 가능 |
| P2 실행 분리 | P1 → Runner와 Body 단일 실행 전환 | E01~E05: 의도 후 1초, 실행 한 번, 대기/이동 중지, 실패 후 연쇄 중단 |
| P3 요청 수명 | P2 → VoiceAgent/Brain/Client 취소·확정 분리 | R01~R05, L01: 역순 응답·오래된 오류·STT 무시, 기억/신뢰도 불변, 비활성화 정리 |
| P4 명령 규칙·UI | P3 → 로컬 중지·확률 오해 제거·프롬프트·이벤트 UI | C01~C03, M01~M05: 채팅/음성 입력, 명확한 명령 보존, 모호성·복합 제한, 중지 오인 없음 |
| P5 반자율 탐색 | P4 → 탐색 타이머·후보·자율 출처 제한 | A01~A06: 시작 3초, 정상 완료 후 3초, 플레이어 선점, 중지 후 재개 없음, 퍼즐 조작 0회 |
| P6 시험 퍼즐 | P5 및 퍼즐 세부 조건 합의 → 순차 상태 전이 | PZ01~PZ05: 버튼→서랍→열쇠→문, 우회 거부·중간 취소·최신 조건 반영 |
| P7 통합·문서 | P6 → 실제 API·대상 컨트롤러 연결 검증 | 회귀 사례 모두 실제 결과 기록. 미실행·실패를 완료로 바꾸지 않음 |

ID별 상세 절차는 [TEST_PLAN.md](TEST_PLAN.md)에 있다. 단계별 컴파일과 관련 시험을 먼저 통과시킨다. Kyle 통합 시 Body 이동부만 교체하고 같은 취소·검증 시험을 반복한다. 처음에는 현재 직선 이동을 보존하되 안전한 경로 보장은 완료로 선언하지 않는다.

## 6. Git 보존·복원 절차 제안

**이번 작업에서 Git add/commit/reset/clean/push는 실행하지 않았다.** 다음은 이후 코드 구현 시 별도 수행할 절차다.

1. 현재 수정·미추적 파일을 포함한 비밀 제외 복원 기준을 먼저 확보한다. 현재 HEAD만으로 미추적 VoiceAI 코드를 복원할 수 없다.
2. 전용 브랜치에서 작업하고 기준 커밋에 넣을 경로를 명시적으로 검토한다. blanket add나 secrets 강제 추가를 피한다. 기존 스테이징 파일도 확인한다.
3. 단계별 컴파일 가능한 커밋으로 분리한다. Body·Runner 실행 전환처럼 상호 의존하는 변경은 함께 기록한다.
4. 실패 시 추가 변경을 먼저 보존한 뒤 별도 worktree의 기준 버전으로 비교한다. 비밀 파일은 복사하지 않는다.
5. 현재 브랜치 복원이 필요하면 리팩터링 커밋만 역순 revert하는 방식을 우선한다. 충돌에서 기존 사용자 변경을 임의로 덮어쓰지 않는다.
6. hard reset, clean, 전체 restore를 복원 기본 절차로 사용하지 않는다.

## 7. 미확정·후속 범위

이동 방식·Kyle 실제 API, 대상 속성 모델, 질문 임계값, 복합 명령 상한과 초과 시 UX, 시간 허용 오차, 실패/잡담 후 자동 탐색, 퍼즐 연출·열쇠 소비 여부는 추가 합의가 필요하다. 일반 명령 **정상 완료 후 3초 재개**와 **명시적 중지 후 재개 금지**는 이미 확정되어 있다.
