# VoiceAI 변경 이력

## 2026-11-11 — 중단된 통합 검증 재개 및 종료

- 기존 통합 구현은 재작성하지 않았다. 사용자 직접 성공 보고(새 씬, 사람 이동, 드론 전환·비행, 자율 탐색, SF 맵 렌더링)를 TEST_PLAN에 별도 출처로 기록했다.
- 실제 Unity 6000.3.25f1 Play Mode에서 가상 입력·가짜 Brain 응답으로 미완료 검증 27개 assertion 통과. 채팅 중 조작·Tab 차단, 단일 카메라/AudioListener, 상자 3개 조회·조사, 집기/내려놓기, 탐색 정지/재개·선점, 대기/이동 취소, 늦은 응답 폐기를 확인했다. Play 오류·경고 각각 0, 실제 API·마이크 사용 없음.
- C# 컴파일 성공. Editor 시작의 Cinemachine asmref/partial-class 진단은 별도 잔여 사항이며 수정하지 않았다. 첫 실행 인자 오류는 검증 도구 실행을 수정하여 해결했으며 게임 기능 코드는 수정하지 않았다.
- TEST_PLAN/CHANGELOG만 영구 갱신. 이번 임시 Editor 검증 스크립트/meta는 정리하고 원문·결과·로그는 Library/VoiceAIIntegrationAudit-20261111에 보존했다. 전날 Builder 및 기존 파일은 삭제하지 않았다.
- 검증 전 외부 백업 12개 SHA-256 일치. 검증 직후 원본 12개도 일치. Git 병합 인덱스·HEAD·MERGE_HEAD 유지 확인, 기존 VoiceAI 원본 프로젝트 수정 없음. add/commit/push/reset/clean/restore 없음.
- 남은 검증은 실제 입력·한글 IME·커서, 별도 승인하의 실제 API/STT, 추후 Kyle 연결이다. 기존 회귀 51개는 다시 실행하지 않았다. 퍼즐 등 새 기능은 구현하지 않았다.

## 2026-11-11 — 독립된 사람·드론·AI 통합 프로토타입

### 실제 구현

- 새 VoiceAI_Integration_Test 씬: 기존 SF 맵 복제, HumanPlayer / Drone / AIRobot 분리, 카메라 2개, 실제 상자 3개와 동일 씬 VoiceAISystem 연결.
- 기존 PlayerController / DroneController / AgentBrain / AgentActionRunner / AgentBodyController / Exploration / AnimatorBridge를 재사용했다.
- ControlModeSwitcher: 입력 차단, 외부 키 처리 옵션, 선택적 드론 진입 정책, 현재 카메라·모드 공개. 기존 serialized 참조와 기본 동작을 유지했다.
- VoiceAgentDebugUI 및 새 VoiceAIIntegrationInput: Enter 채팅, V 녹음, Tab 전환, Esc AI 취소, F 시험 정보 조회, 채팅 중 수동 조작·전환 차단, IME 조합 guard.
- VoiceAgent: 기존 기본 초기화 동작을 유지하는 initializeApiOnAwake 옵션과 첫 실제 요청 지연 초기화. 새 씬 자율 탐색·로컬 중지는 키 없이 동작한다.
- 신규 URP 재질 4개: 임시 로봇과 빨강·파랑·노랑 시험 상자. 실제 Kyle 원본은 복사하지 않았다.
- 개발 문서와 통합 시험 가이드를 갱신했다.

### 검증과 보존

실제 Unity 6000.3.25f1 씬 생성 및 C# 컴파일을 통과했다(오류 0 / 경고 0). 자동 Play 결과는 TEST_PLAN에 별도로 기록한다. 실제 네트워크·음성·IME·렌더링·Kyle 검증과 구분한다.

변경 전 코드·문서·기존 씬·검증 도구 18개를 Library/VoiceAIIntegrationBuild-20261111/Backup에 SHA-256 검증 백업했다. 기존 Git 병합 인덱스와 원본 프로젝트는 변경하지 않는다. 새 씬만 저장하며 기존 Test_Scene / kkws_Scene / ssm 씬, 패키지, 기존 재질은 이 단계에서 수정하지 않는다. 자동 commit/push/add/reset/clean은 하지 않는다.

### 후속

실제 사용자 회귀와 Kyle 연결을 확인한 뒤 동일 행동 검증을 사용하는 버튼→서랍→열쇠→문 퍼즐, 좌석 진입 정책, 장애물 경로 탐색을 진행한다.

날짜 기준: Asia/Seoul. 문서 작성과 실제 코드 변경을 별도로 기록한다. 계획·예상 결과·미실행 시험을 구현 완료 이력으로 기록하지 않는다.

## 2026-11-10 — Kyle Animation Event 무음 수신기 추가

### 확인한 현상·에셋

사용자 보고: 실제 Unity에서 반자율 탐색이 동작하지만 `Walk_N`의 `OnFootstep has no receiver` 오류가 발생한다. 이전 Starter Assets 이동 스크립트 제거로 수신기가 없어졌다는 설명은 가능한 원인 해석이며, 이번에는 에셋 이벤트와 필요한 콜백만 확인했다. 탐색 전체 세부 시험을 새로 모두 통과했다고 기록하지 않는다.

- `Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/Locomotion--Walk_N.anim.fbx.meta`: Walk_N에 OnFootstep 2개(time 저장값 0.31872225, 0.7970205).
- 같은 폴더 Run_N·Run_S에도 각각 OnFootstep 2개.
- Walk_N_Land·Run_N_Land 및 Jump--Jump 파일의 JumpLand에는 OnLand가 각각 1개. Idle·JumpStart·InAir 및 Kyle 모델 메타에서 별도 이벤트를 찾지 못했다.
- 해당 이벤트들의 data는 빈 문자열, objectReferenceParameter는 instanceID 0, floatParameter/intParameter는 0, messageOptions는 0이다. 콜백은 UnityEngine.AnimationEvent 객체 인자로 수신한다. time 저장값의 단위를 실제 재생 초로 임의 해석하지 않는다.

### 실제 최소 변경

- 신규 `Assets/VoiceAI/Runtime/Unity/RobotAnimationEventReceiver.cs` 및 `.meta`.
- public `void OnFootstep(AnimationEvent animationEvent)`, `void OnLand(AnimationEvent animationEvent)`를 무음·무상태 수신으로 구현했다. 인자 참조·AudioSource·로그·이동·Animator 파라미터·퍼즐 상태 접근이 없다.
- TEST_PLAN에 정확한 Inspector 부착 위치와 미실행 실제 시험을 추가했다.
- AgentBodyController·RobotAnimatorBridge·RobotExplorationController와 다른 기존 코드는 변경하지 않았다. 씬·원본 에셋·클립·Controller·공용 설정·비밀 파일도 변경하지 않았다.

### 검증과 남은 사용자 작업

- 신규 수신기를 포함한 전체 Runtime의 Unity 6.3 DLL 참조 C# 빌드: 오류 0·경고 0.
- 기존 대체 환경 자동 회귀 52개 통과. 이 시험은 새 수신기의 native Animation Event 전달을 시험하지 않는다. 테스트 하네스·기존 시험 코드는 이번에 변경하지 않았다.
- **수신기 부착 후 오류 소멸·실제 Play Mode 이벤트 전달은 미검증**이다. 사용자가 클립을 재생하는 Animator와 같은 GameObject에 컴포넌트를 추가해야 한다. 씬은 자동 수정하지 않았다.
- 자동 Git add/commit/push/reset/clean/restore 없음. 기존 미커밋 변경 보존, API 키 접근 없음.

## 2026-11-10 — 반자율 탐색 시스템 v0.1 구현

### 변경 전 복원 백업

- 사용자 Play Mode 종료·ssm.unity 저장 확인과 백업 실행 승인 후, 검토된 스크립트를 -Execute로 실행했다.
- 경로: `C:\Dev\Capstune_design\_local_backups\VoiceAI-pre-exploration-v01-20261110`. 기존 백업을 덮어쓰지 않았다.
- 지정 원본 111개, 13,242,336바이트. SHA-256 전체 일치. 별도 BACKUP_MANIFEST.json 1개를 포함해 실제 총 112개다. 비밀·캐시 경로 0개, 복사 직후 원본 해시·Git 상태 유지 확인.
- PowerShell의 영구 실행 정책은 변경하지 않고 승인된 실행 프로세스에 한정해 스크립트를 실행했다. 완료 메시지의 개수 표시 누락은 매니페스트 별도 재검사로 확인했으며 파일 검증은 성공했다.

### 실제 코드 변경

| 파일 | 이유·실제 결과 |
|---|---|
| 신규 Assets/VoiceAI/Runtime/Unity/RobotExplorationController.cs 및 .meta | 실제 대상 선택·3초 시작/재개·실패 기억·최신 작업별 종료 처리. 새 API 호출 없음 |
| Runtime/Core/AgentModels.cs | Player/Autonomous 출처와 실행 결과 Source 추가 |
| Runtime/Core/AgentCommandRules.cs | 전체 문장 로컬 탐색 재개/계속 판정 추가 |
| Runtime/Unity/AgentActionRunner.cs | 출처·자율 진입 제한·플레이어 우선·중지 억제·move_to/inspect 화이트리스트·대상/범위 재검증. 오래된 종료가 새 작업 상태를 덮어쓰지 않도록 정리 |
| Runtime/Unity/AgentInteractable.cs | Inspector opt-in(기본 false)과 runtime HasBeenInspected 추가. 퍼즐 state와 분리 |
| Runtime/Unity/AgentBodyController.cs | 실제 inspect 성공 때 조사 기억 갱신. 기존 이동/회전/집기/정지·Bridge 알림 유지 |
| Runtime/Unity/VoiceAgent.cs | 명시 중지 억제, API 없는 재개 텍스트/메서드, 음성 재개 시 STT 이후 LLM 생략 |
| Runtime/Unity/VoiceAgentDebugUI.cs | 작업 출처·자동 중지 플래그·로컬 재개 버튼 표시 |
| Docs/VoiceAI/Validation/BehaviorTests.csproj, UnityShim.cs, BehaviorTests.cs, README.md | Controller 포함·메뉴 속성 대체·탐색 17개 자동 시험·검증 설명 |
| ARCHITECTURE.md, ROBOT_DESIGN.md, TEST_PLAN.md, CHANGELOG.md | 구현/미구현 구분, Inspector·기본 정책·백업·사용자 시험 출처 기록 |

기존 Runner의 작업 ID·의도·1초·최신 검증을 재사용한다. 자율 탐색은 미조사 실제 대상만 거리→ordinal ID로 선택하며, 조사 완료·실패 대상의 무한 반복을 막는다. 명시적 중지는 일반 명령 완료로 해제되지 않는다. 플레이어 실패/거부/취소 후 자동 재개하지 않는 보수적 정책과 기본 10단위 반경을 사용한다. 임의 장애물 경로·안전 보장은 추가하지 않았다.

### 검증 및 사용자 보고

- 사용자 직접 추가 Play Mode 보고: Kyle 외형·자연스러운 걷기·이동 완료 Idle 성공. TEST_PLAN U07/U08로 기록. **애니메이션 연결 이후 Esc 취소 회귀 성공으로 기록하지 않았다.**
- Codex 자동 검사: Unity 6.3 DLL 참조 런타임 빌드 오류 0·경고 0, 기존 35개+탐색 17개=52개 통과. 가짜 네트워크/Unity 환경이며 실제 Play Mode·마이크/API 실행은 아님.
- 새 탐색의 실제 Unity 시험, 연결 이후 Esc·채팅/음성/집기/걷기 회귀는 사용자 확인 대기. 과거 성공 결과를 새 코드 성공으로 확대하지 않는다.
- 씬·Starter Assets·Packages·ProjectSettings·다른 팀 파일·비밀 파일은 변경하지 않는다. 기존 미커밋 상태를 유지하며 Git add/commit/push/reset/clean/restore를 실행하지 않았다.
- 후속: 사용자 Inspector opt-in 및 X01~X16 시험 → 취소/선점 회귀 확인 → 장애물 경로·퍼즐·힌트·영구 조사 기억 검토. 자율 퍼즐 조작·새 LLM 탐색 호출은 이번에 도입하지 않았다.

## 2026-11-10 — Robot Kyle 걷기 애니메이션 연결 코드

### 실제 조사·변경

- 실제 `StarterAssetsThirdPerson.controller`의 Float Speed/MotionSpeed, Bool Grounded/Jump/FreeFall과 Idle/Walk/Run 임계값 0/2/6을 확인했다. MotionSpeed가 locomotion 상태 재생 배수임을 확인했다. 파라미터 이름을 추측하지 않았다.
- Kyle 모델과 클립의 Humanoid 메타·loopTime·Avatar GUID를 조사했다. native Avatar 유효성과 시각적 리타기팅은 미검증이다.
- 신규 `Assets/VoiceAI/Runtime/Unity/RobotAnimatorBridge.cs` 및 `.meta`: 실제 Body 변위로 속도 측정, Idle/Walk 가중치·걷기 재생 배수·지상 Bool 전달, 정지 즉시 초기화, null/다른 파라미터 계약 방어, 런타임 Root Motion 비활성화. Transform은 쓰지 않는다.
- 수정 `Assets/VoiceAI/Runtime/Unity/AgentBodyController.cs`: 실제 이동 dt와 정지 신호 이벤트 추가. 기존 직선 이동·집기·내려놓기를 유지하며 방향 이동도 진행 방향으로 회전한다. Runner/VoiceAgent/API 코드는 이번에 변경하지 않았다.
- 수정 `Docs/VoiceAI/Validation/BehaviorTests.csproj`, `UnityShim.cs`, `BehaviorTests.cs`, `README.md`: Bridge 포함, Animator 파라미터 대체 객체·방향 시험·에셋 계약 검사와 10개 자동 사례 추가.
- ARCHITECTURE/TEST_PLAN/CHANGELOG와 이전 Kyle 계획 문서의 현재 상태를 갱신했다.

### 실제 검증 결과와 한계

- Unity 6000.3.25f1 DLL 참조 전체 런타임 빌드: 오류 0·경고 0.
- 대체 환경 자동 검사: 기존 25개와 신규 10개, 총 35개 통과. Idle·Walk 파라미터·재생 배수·완료·취소·대기·참조 없음·Root Motion 설정·방향 회전·채팅 집기/내려놓기 포함.
- 이는 Animator 상태 머신·뼈대·발 움직임을 native 엔진에서 재생한 시험이 아니다. 이번 걷기 변경의 실제 Unity Play Mode·실제 마이크/API 회귀는 미실행이다. 이전 사용자 U01~U06 성공 보고와 구분한다.
- 씬·Starter Assets 원본·Packages·ProjectSettings·다른 작업물은 수정하지 않았다. Git add/commit/push/reset/clean/restore를 수행하지 않았고 비밀 파일도 읽지 않았다.

### 남은 사용자 설정·문제

- player에 Bridge를 추가하고 Controller가 연결된 KyleRobot Animator를 지정해야 한다. 씬에 Animator가 두 개여서 자동 선택하지 않는다. 중복 Animator·외부 이동 스크립트는 Inspector 점검이 필요하다.
- 발맞춤 보정값 2m/s·걷기 진입 0.12초는 기본값이다. Idle 포즈 전환의 자연스러움과 리타기팅은 실제 화면에서 조정·확인한다.
- CharacterController·충돌·중력·Run/Jump·집기 전용 애니메이션·손 소켓·자율 탐색·퍼즐은 이번 범위 밖이다.

## 2026-11-09 — 사용자 Unity 실제 시험 결과 반영 및 Kyle 통합 계획

### 출처와 완료 범위

수행자: **사용자**. 근거: 대화로 전달된 직접 Unity 시험 결과. 보고 기록일은 2026-11-09이며 실제 시험 일시·원본 로그·영상·플랫폼 세부는 미제공이다. Codex 직접 실행·자동화 시험으로 기록하지 않는다.

사용자 성공 보고:

1. Unity Play Mode 정상 실행, Console 오류 0개.
2. 채팅 명령으로 빨간 상자 집기.
3. AI 해석 후 실제 큐브 이동 및 상자 집기.
4. 이동 중 Esc로 이동 중지 및 Cancelled 처리.
5. 행동 예고 후 기본 1초 대기 중 Esc 취소.
6. 실제 마이크 STT→AI 해석→행동 실행.

이는 기존 1차 구현의 실행 경로·의도 대기·취소 경로 일부가 실제 Unity에서 확인되었다는 결과다. 새 기능을 구현한 기록은 아니다. 이전 Codex 참조 빌드·대체 환경 25개 통과와 별도 출처로 유지한다.

### 이번 실제 파일 변경 — 문서만 수정

| 파일 | 이유·결과 |
|---|---|
| Docs/VoiceAI/TEST_PLAN.md | U01~U06 사용자 성공 보고 추가. 복합 사례는 확인한 부분만 갱신하고 미확인 조건 분리 |
| Docs/VoiceAI/CHANGELOG.md | 사용자 수행 사실·남은 검증·향후 계획 기록. 아래 과거 미실행 기록은 당시 결과로 보존 |
| Docs/VoiceAI/ARCHITECTURE.md | 현 C# 책임·호출과 비교. 실행 구조 유지, 실제 사용자 확인 범위와 후속 Kyle 계획 링크 추가 |
| 신규 Docs/VoiceAI/ROBOT_KYLE_INTEGRATION_PLAN.md | Import 조사 근거, 외형 우선 연결·이동부 교체 단계·충돌·사용자 Inspector 설정 계획 작성 |

### 남아 있는 문제·향후 순서

- 이번 사용자 보고에 포함되지 않은 LLM/STT 처리 중 취소, 로컬 텍스트·버튼 중지, 늦은 응답·기억 보호, 새 명령 선점, 내려놓기·상태 변경·비활성화 경로는 실제 Unity 추가 시험이 필요하다. 실패했다는 뜻은 아니다.
- 현재 Transform 직선 이동은 충돌·중력·경로 안전성을 보장하지 않는다. 집기는 부모 변경·머리 위 배치이며 손 소켓·애니메이션·물리 보정이 없다.
- 현재 Assets/Packages·관련 패키지 캐시 검색에서 Kyle 모델·프리팹·컨트롤러 Import 증거를 찾지 못했다. 계정 보유·다른 프로젝트 설치 여부는 미확인이다.
- 다음 순서 제안: 남은 취소 회귀 시험 → 정확한 Kyle 패키지/프리팹/리그/클립 확인 → 큐브 이동 소유권을 유지한 외형 연결 → Animator·이동 어댑터 → 같은 취소 회귀 재시험.
- 자율 탐색·퍼즐·힌트·TTS·정교한 안전·전체 규칙 오해는 여전히 미구현이다. 패키지·공용 설정의 기존 미커밋 변경도 유지한다.

이번에는 C#·씬·Packages·ProjectSettings를 변경하지 않았고, 키·비밀 파일을 읽거나 출력하지 않았다. Git commit/push/reset/clean을 실행하지 않았으며 기존 미커밋 작업을 유지했다. Codex는 Unity·자동 시험을 새로 실행하지 않았다.

## 2026-11-09 — 실행·취소 시스템 1차 C# 구현

기준 커밋: `8eaae6a14e5ad7e4cba14a55cd340f0cddde5745` / `checkpoint/voiceai-before-runner`.
작업 브랜치: `feature/voiceai-prototype`. 이번 구현은 자동 스테이징·커밋·태그·push 없이 작업 트리에 남긴다.

### 실제 코드·에셋 변경

경로 접두사: `Assets/VoiceAI/`.

| 파일·클래스 | 이유 | 실제 구현 결과 |
|---|---|---|
| 신규 Runtime/Unity/AgentActionRunner.cs 및 .meta | 실행 책임·취소 분리 | 작업 ID·상태·의도·1초 대기·프레임 검증·종료 결과. 런타임 자동 연결 |
| 신규 Runtime/Core/AgentCommandRules.cs 및 .meta | API 없는 중지 | 전체 문장 중지 판정과 행동 복사. 전체 대상 선택·오해 규칙은 미구현 |
| Runtime/Core/AgentModels.cs | 식별·결과 계약 | ID·실행 상태·결과·준비 턴 DTO 추가 |
| Runtime/Core/AgentBrain.cs | 취소된 응답의 상태 오염 방지 | Prepare/Commit 분리·revision·중복/취소 검사. RunTurnAsync 호환 유지. 무작위 오해 호출 중단 |
| Runtime/Core/AgentResponseParser.cs | 실행 가능한 행동만 판정 | null·질문/거부·미지원 동사 방어, 실행 시 소지 검사·걸음 수 제한 |
| Runtime/Unity/VoiceAgent.cs | 입력 선점·늦은 응답 방어 | 요청별 토큰·ID gate·로컬 중지·상태 확정·Runner 연결·수명 정리 |
| Runtime/Unity/AgentBodyController.cs | 단일 실행 경로 | 턴 구독/코루틴 큐 제거, 개별 Begin/Tick/Cancel·결과·시간 제한. 직선 이동 유지 |
| Runtime/Unity/SceneStageContext.cs | 실제 대상 일치 | 같은 씬 필터·유일한 활성 ID 조회 |
| Runtime/Unity/AgentInteractable.cs | 등록 중복 방지 | 활성 목록 중복 추가 차단 |
| Runtime/Unity/OpenAIClient.cs | 전송 전 취소 | 시도 시작 전 토큰 확인. 기존 STT/Responses 형식 유지 |
| Runtime/Unity/VoiceAgentDebugUI.cs | 관찰·중지 | 실행 상태·의도·결과, 중지 버튼/Esc, 이벤트 해제·로그 제한·큐브 명령 |
| README.md | 사용법 불일치 정정 | 현재 Unity 버전·취소·이벤트 의미·실행 범위 안내 |

### 문서·검증 도구 변경

- ARCHITECTURE.md: 기준 구조와 실제 1차 구조 분리, ID·취소·검증·이벤트·후속 범위 갱신.
- TEST_PLAN.md: Inspector 준비·실제 수동 시험 절차, 자동 검사와 Unity 결과 분리.
- CHANGELOG.md: 이 실제 코드 변경 이력 추가. 아래 초기 문서 작성 이력은 당시 사실로 보존.
- 신규 Validation/: UnityCompile.csproj, BehaviorTests.csproj, UnityShim.cs, BehaviorTests.cs, NuGet.Config, README.md, .gitignore. 로컬 참조 컴파일과 가짜 전송·대체 Unity 환경 동작 검사. 생성 산출물은 폴더별 ignore 처리.

### 실제 검증 결과

- 전체 런타임 Unity DLL 참조 C# 빌드: 오류 0·경고 0.
- 대체 환경 자동 동작 시험: 25개 통과, 최종 실패 없음.
- 변경 범위 diff 공백 검사 통과. 새 런타임 스크립트에 .meta 추가.
- **구현 당시 Codex의 Unity Editor 컴파일·Play Mode·실제 마이크·실제 API 시험은 미실행**. 이후 사용자 시험 결과는 위 별도 이력과 TEST_PLAN U01~U06에 기록.
- 씬·공용 패키지·프로젝트 설정·키·기존 팀 작업물은 수정하지 않음. 실제 API 키 로더를 실행하지 않음.
- 자율 탐색·퍼즐·Kyle·힌트·TTS·정교한 안전 판단·전체 규칙 기반 오해는 미구현.

## 2026-11-09 — 문서 작성 완료

| 파일 | 수정 이유 | 실제 수행 결과 |
|---|---|---|
| `Docs/VoiceAI/ARCHITECTURE.md` | 기존 시험 코드와 목표 구조의 혼동 방지 | 현재 클래스·호출 관계·문제·계획된 작업/취소/검증 계약 정리 |
| `Docs/VoiceAI/ROBOT_DESIGN.md` | 반자율 로봇의 확정 규칙 명시 | 말투·권한·대상 우선순위·규칙 오해·탐색 타이밍·미확정 항목 구분 |
| `Docs/VoiceAI/REFACTOR_PLAN.md` | 구현 순서와 책임 명확화 | 기존 파일별 변경·최소 신규 클래스·의존 단계·시험 통과 조건 정리 |
| `Docs/VoiceAI/CHANGELOG.md` | 문서와 코드 이력 구분 | 이번 문서 작성 사실만 기록 |
| `Docs/VoiceAI/TEST_PLAN.md` | 예상 결과를 실제 실행 결과와 분리 | 명령·취소·탐색·시험 퍼즐 사례와 미실행 결과 기록 양식 작성 |

이번 문서에서는 최신 요구에 따라 일반 명령 정상 완료 후 약 3초 자동 탐색 재개를 명시했다. 앞선 계획의 보수적인 명시적 재개 기본안은 채택하지 않았다. 명시적 중지 후 자동 재개 금지는 유지한다. 이는 설계 문서 반영이며 런타임 구현 변경은 아니다.

### 실제 코드·에셋 변경

**이번 작업에서 수행한 코드·씬·패키지·프로젝트 설정 변경 없음.** AgentActionRunner·AgentCommandRules·탐색·퍼즐은 미구현이다. 기존 C#·프롬프트·README도 수정하지 않았다. 기존 작업 트리 변경은 이번 작업의 구현 결과로 간주하지 않는다.

### 검증 범위

- 저장소 코드·설정을 읽고 문서 근거로 사용했다.
- 문서 5개 생성을 확인하고 내용과 상대 링크를 검토했다. 깨진 상대 링크는 0개였다. 작업 전후 Git 상태에서 기존 변경 목록은 유지되고 `Docs/`가 새로 추가된 것을 확인했다. 실제 쓰기 작업은 요청한 문서 5개에만 수행했다. 이 확인 결과는 런타임 시험과 구분한다.
- Unity 컴파일·Play Mode·실제 STT/LLM·퍼즐 시험은 **미실행**이다.
- API 키·비밀 파일은 읽거나 출력하지 않았다.
- Git add, commit, reset, clean, push를 실행하지 않았다.

## 향후 실제 변경 기록 양식

아래 양식은 작성 예시가 아닌 빈 기록 템플릿이다. 실제 변경이 발생한 뒤에만 새 이력으로 추가한다.

| 날짜 | 구분(문서/코드/씬/설정) | 파일·클래스 | 이유 | 실제 구현 결과 | 검증 ID·결과 | 남은 문제 |
|---|---|---|---|---|---|---|
| 미기록 | 미기록 | 미기록 | 미기록 | 미기록 | 미실행 | 미기록 |
