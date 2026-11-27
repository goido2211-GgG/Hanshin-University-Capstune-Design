# VoiceAI 통합 시험 씬

## FirstScene 팀 테스트

`Assets/Scenes/FirstScene.unity`는 Unity Hub에서 **6000.3.25f1**로 연다. `Packages/manifest.json`과 `Packages/packages-lock.json`의 의존성을 Unity가 복원하도록 기다린다.

### Robot Kyle 로컬 복원

1. 팀원이 사용할 권한이 있는 Unity 계정으로 Asset Store의 **First Person + Third Person | Character Controllers**를 확보한다. Unity의 **Window → Package Manager → My Assets**에서 패키지를 다운로드하고 Import한다. 기존에 팀에서 확인한 설치본이 있다면 같은 패키지 버전을 사용한다. [Unity Asset Store](https://assetstore.unity.com/packages/3d/characters/first-person-third-person-character-controllers-196526) · [Unity 6000.3 Import 안내](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-ui-import.html)
2. 패키지의 `Assets/Starter Assets/Runtime/SpaceRobotKyle/`와 `Runtime/ThirdPersonController/Character/`의 모델·재질·아바타·애니메이션 의존성을 원본 경로와 `.meta`로 설치한다. 가져오기 목록에 기존 Input System·Cinemachine 설정이나 샘플 씬이 있다면 프로젝트 파일을 덮어쓰지 않도록 확인한다. `Assets/Starter Assets/`는 로컬에서 관리한다.
3. 아래 두 `.meta`의 `guid`를 확인한다. 설치 버전의 GUID가 다르면 팀에서 확인한 패키지 판본과 Import 항목을 확인한다. FirstScene의 참조나 설치된 에셋의 GUID를 직접 바꾸면 기존 씬의 연결도 깨질 수 있다.

| 로컬 파일 | 원본 GUID |
|---|---|
| `Assets/Starter Assets/Runtime/SpaceRobotKyle/Models/KyleRobot.fbx` | `b1787a7070753594996dac074aee4532` |
| `Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller` | `40db3173a05ae3242b1c182a09b0a183` |

4. `FirstScene`을 열고 `AIRobot - Robot Kyle` 아래의 모델과 Animator Controller가 연결되는지 Inspector에서 확인한다. 각 팀원이 에셋을 로컬로 설치하므로 GitHub 브랜치에는 패키지 원본과 `.meta`를 포함하지 않는다.

### 저녁 통합 테스트 순서

1. Game 뷰에서 드론을 기동 발판 위에 놓고 왼쪽 Ctrl로 하강한다. 캡슐이 상승한 뒤 Kyle이 활성화되고 자율탐색이 기본 OFF인지 확인한다.
2. Enter로 한글 채팅을 열고 `문 열어`를 입력한다. Kyle이 첫 번째 벽면 버튼을 눌러 문을 열면 `초록색 버튼 눌러`를 입력한다. Kyle이 다음 구역으로 이동해 상자를 떨어뜨린다.
3. 상자가 멈추면 `저 상자 집어` → `상자를 발판 위로 옮겨` → `발판에 내려놔`를 실행한다. 두 번째 문은 지정한 상자가 발판에 안착할 때 열린다.
4. 씬을 다시 시작하고 상자를 떨어뜨린 뒤 `저 상자 가져다가 문 여는 발판 위에 올려놔`를 한 번 입력한다. 한국어 IME에서 조합 완료와 명령 전송이 구분되는지도 확인한다.
5. 실제 음성은 마이크 권한과 로컬 API 설정을 준비한 팀원이 V를 누른 채 명령을 말하고 떼어 확인한다. 키는 `OPENAI_API_KEY` 환경 변수 또는 로컬 `secrets/openai_api_key.txt`로 설정한다. 채팅의 로컬 퍼즐 명령은 키 없이 시험할 수 있다.

아래 내용은 기존 `VoiceAI_Integration_Test` 씬에 대한 기록이다.

기준일: 2026-11-11, Unity 6000.3.25f1. 이 문서는 통합 worktree용이다. 원본 VoiceAI 프로젝트와 기존 Test_Scene / kkws_Scene / ssm 씬은 보존한다.

## 확정된 역할

- HumanPlayer: 방에 갇힌 사람. PlayerController로 직접 이동한다.
- Drone: DroneController로 직접 조종하는 독립 개체다.
- AIRobot: Kyle 역할의 독립 AI 로봇이다. 현재는 캡슐 임시 외형이며 AgentBodyController만 이동을 실행한다.
- 플레이어 구조를 위한 퍼즐은 후속이다. 현재는 실제 시험 상자 3개에 대한 이동·조사·집기·내려놓기를 연결한다.

## 씬과 연결

씬 경로는 Assets/Scenes/VoiceAI_Integration_Test.unity다. Test_Scene을 복제하여 동일한 게임 씬에 맵, 사람, 드론, AIRobot, VoiceAISystem, 시험 상자를 배치한다. 런타임 Additive 로딩은 사용하지 않는다.

VoiceAISystem의 SceneStageContext는 useDemoObjectsWhenEmpty=false이며 red_box / blue_box / yellow_box만 실제 AgentInteractable 대상으로 사용한다. VoiceAgent와 AgentActionRunner는 AIRobot의 Body 하나에만 연결한다. 플레이어나 드론에 AgentBodyController를 추가하지 않는다.

복제 씬의 기존 Main Camera와 불완전한 CM_vcam01은 새 씬에서만 제거한다. 사람·드론의 직접 Camera와 AudioListener를 ControlModeSwitcher가 전환한다. 원본 Test_Scene 카메라는 수정하지 않는다.

시험 상자는 Rigidbody 없이 발바닥 기준 root와 Visual 자식을 사용한다. 기존 집기·내려놓기 로직을 그대로 재사용하기 위한 배치다. 배경 가구는 자동 상호작용 대상으로 등록하지 않는다.

## 입력

| 입력 | 동작 |
|---|---|
| WASD / 마우스 | 현재 사람 또는 드론 이동·시점 |
| Space | 사람 점프 / 드론 상승 |
| 왼쪽 Ctrl | 드론 하강 |
| Tab | 사람·드론 전환 |
| Enter | 채팅 열기 / 전송. 빈 채팅은 닫기 |
| V 누르기·떼기 | 음성 녹음 시작·종료. 채팅 중에는 시작하지 않음 |
| Esc | AI 작업 즉시 중지 및 채팅 닫기. 게임 일시정지는 구현하지 않음 |
| F | 현재 카메라 앞 실제 시험 대상의 정보 표시. 퍼즐 상태는 바꾸지 않음 |

채팅 중에는 사람·드론 컨트롤러와 Tab 전환을 차단하고 커서를 표시한다. 현재 모드의 카메라·AudioListener는 유지한다. 이 시험 단계에서는 컨트롤러 비활성화로 사람의 중력 처리도 잠시 멈춘다. 게임 전체 시간이나 AI 탐색은 일시정지하지 않는다.

ControlModeSwitcher의 선택적 IDroneAccessPolicy를 이용하면 후속 단계에서 앉은 상태 진입 조건을 추가할 수 있다. 지금은 해당 제한이 없으며 기존 laptop 거리 조건은 계속 지원한다.

## API와 비밀 파일

새 씬은 VoiceAgent.initializeApiOnAwake=false다. 자율 탐색·로컬 중지·탐색 재개는 API를 초기화하지 않는다. 첫 실제 일반 채팅 또는 음성 요청에서 기존 OpenAI 연결을 초기화한다. 기존 씬은 필드 기본값 true로 이전 초기화 동작을 유지한다.

통합 worktree에 secrets나 API 키를 복사하지 않는다. 실제 STT·LLM 왕복은 사용자가 별도로 적법한 로컬 설정을 준비한 뒤 검증한다. 자동 Play 검증은 API 키 로더나 마이크를 사용하지 않고 로컬 명령과 가짜 응답을 사용한다. 가짜 LLM 시험은 실제 네트워크 성공을 의미하지 않는다.

## 수동 시험

1. Unity Hub에서 통합 worktree 경로를 확인하고 Unity 6000.3.25f1로 연다.
2. 새 씬을 연 뒤 Play, Game 뷰를 클릭한다.
3. 사람 WASD·점프·마우스, Tab 전환, 드론 이동·상하 이동, Tab 복귀를 확인한다.
4. Enter로 채팅을 열고 WASD / Tab / V를 눌러 수동 개체가 움직이거나 전환되지 않는지 확인한다.
5. 채팅에 멈춰를 입력하고 Enter: 채팅이 닫히며 AI가 즉시 중지해야 한다.
6. 탐색 재개를 입력하면 약 3초 뒤 실제 상자를 이동·조사한다. Esc 후에는 자동 재개하지 않는다.
7. API 설정 후 빨간 상자를 집어 / 빨간 상자를 내려놓아 / 파란 상자를 살펴봐를 시험한다.
8. 사람 모드와 드론 모드에서 모두 음성·채팅 명령이 AIRobot에게만 전달되는지 확인한다.
9. 한국어 IME 조합·Enter 전송, 실제 V 마이크 입력, 재질·조명·시야는 수동 확인한다.

## 실제 Kyle 연결

1. 라이선스와 팀의 로컬 에셋 취급 방침을 먼저 확인한다.
2. AIRobot root의 Body / Exploration / Collider와 VoiceAISystem 참조를 유지한다.
3. AIRobot/VisualRoot의 임시 외형을 실제 Kyle 시각 모델로 교체한다. 사람·드론 컨트롤러는 붙이지 않는다.
4. RobotAnimatorBridge.targetAnimator에 실제 Animator를 연결한다.
5. Avatar와 Controller, Speed/MotionSpeed(float), Grounded/Jump/FreeFall(bool)를 확인하고 Root Motion은 끈다.
6. 실제 Animator 오브젝트에 RobotAnimationEventReceiver를 붙인다.
7. 외형 높이·바닥 피벗·집기 위치를 조정하고 걷기·Idle·Esc 회귀를 다시 시험한다.

## 후속 퍼즐 순서

실제 상호작용 계약과 안전 검증을 정한 뒤 버튼 → 서랍 → 열쇠 → 문 상태 머신을 연결한다. F 수동 상호작용과 AI 행동은 동일한 Unity 검증을 통과해야 한다. 문 열기로 플레이어 구조 완료를 판정한다. 앉은 상태 드론 진입, 장애물 경로 탐색, 힌트·말투·전체 규칙 기반 오해는 별도 후속 단계다.

## 검증 기록

구현 및 자동 검증의 실제 결과는 CHANGELOG.md / TEST_PLAN.md와 Library/VoiceAIIntegrationBuild-20261111의 BuildResult.json / PlayResult.json을 따른다. 실제 STT·LLM, 실제 Kyle, 화면 기반 검증은 자동 성공으로 기록하지 않는다.
