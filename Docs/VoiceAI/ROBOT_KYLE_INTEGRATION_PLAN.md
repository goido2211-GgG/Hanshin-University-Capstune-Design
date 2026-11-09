# Robot Kyle 통합 준비 계획

> 2026-11-10 상태 갱신: 아래는 11월 9일 Import 전 조사·계획의 보존 기록이다. 현재는 `Assets/Starter Assets/Runtime/SpaceRobotKyle/`와 ThirdPersonController 애니메이션을 실제 확인했으며, VoiceAI의 `RobotAnimatorBridge`로 걷기 표현을 구현했다. 따라서 아래의 “Import 증거 없음”, “애니메이션 브리지 미구현”은 당시 상태다. 새 계약·씬에 존재하는 Animator 두 개·Inspector 연결·시험은 [ARCHITECTURE.md](ARCHITECTURE.md), [TEST_PLAN.md](TEST_PLAN.md)의 2026-11-10 절을 따른다. CharacterController 등 전용 이동 백엔드 통합은 여전히 미구현이다.

조사·기록일: 2026-11-09 (Asia/Seoul). **코드 분석과 계획 문서이며 실제 통합·Import·씬 설정을 수행하지 않았다.** C#·씬·Packages·ProjectSettings와 기존 미커밋 파일은 유지한다.

## 1. 코드에서 확인한 현재 동작

| 근거 파일·클래스 | 확인 사실 |
|---|---|
| [AgentBodyController.cs](../../Assets/VoiceAI/Runtime/Unity/AgentBodyController.cs) / AgentBodyController.BeginAction | 대상·방향으로 직선 목적지를 계산. Y 높이를 유지하고 대상 앞 stopDistance에 정지 |
| 같은 파일 / TickAction | Vector3.MoveTowards로 Body Transform 위치 변경. 대상 이동은 RotateTowards로 회전하지만 move_direction은 현재 로봇 축으로 이동하고 회전시키지 않음 |
| 같은 파일 / CancelCurrentAction | 행동·대상·이동 플래그 제거. Runner가 더 Tick하지 않으므로 현재 큐브 이동 중지 |
| 같은 파일 / PickUp 분기 | 대상 부모를 Body Transform으로 바꾸고 localPosition=Vector3.up*carryHeight. 손 소켓·집기 클립·Rigidbody 처리 없음 |
| [AgentActionRunner.cs](../../Assets/VoiceAI/Runtime/Unity/AgentActionRunner.cs) / AgentActionRunner | 1개의 Body만 등록, 의도·1초·매 프레임 검증 뒤 Begin/Tick 호출. 별도 컨트롤러 Update가 계속 이동하면 현재 취소 계약만으로는 정지 보장 불가 |
| [VoiceAgent.cs](../../Assets/VoiceAI/Runtime/Unity/VoiceAgent.cs) | Runner를 VoiceAgent GameObject에 자동 준비. Body는 명시 참조 또는 같은 씬 유일 후보로 등록 |

Animator, CharacterController.Move, NavMeshAgent, Rigidbody 기반 이동 호출은 현재 Body에 없다. README의 animator.SetTrigger 예시는 런타임 구현이 아니다.

## 2. 프로젝트 Import 조사 결과

현재 Assets 최상위에는 Scenes, Settings, TutorialInfo, VoiceAI만 확인된다. Assets/Packages 파일명·텍스트에서 Kyle/Robot/StarterAssets/ThirdPersonController와 모델·프리팹·Animator Controller를 검색했으나 해당 에셋을 확인하지 못했다. Library/PackageCache에서도 Kyle/StarterAssets 이름의 파일을 찾지 못했다. manifest에 Input System 1.20.0·Cinemachine 3.1.7·URP 17.3.0 등이 있는 것은 Kyle Import의 증거가 아니다.

판정: **현재 프로젝트에 Import되었다는 증거 없음.** 사용자 계정의 다운로드/보유 상태, 다른 프로젝트 설치, 다른 이름으로 제공된 모델까지 없다고 단정하지 않는다. 정확한 에셋 이름·버전·실제 프리팹·스크립트·리그·클립이 확인되기 전 Animator 파라미터·외부 컨트롤러 클래스명을 확정하지 않는다. 이 문서는 Kyle과 Starter Assets가 반드시 동일한 패키지라는 전제를 두지 않는다.

## 3. 권장 단계 A — 외형 우선 통합

**후속 작업 제안, 미구현.** 현재 성공한 큐브의 루트/Body/VoiceAgent 연결과 실행·취소를 유지하고 Kyle 모델만 자식으로 붙인다. 첫 목표는 외형이 따라 움직이고 중지 시 같이 멈추는 것이다. 원본 프리팹 대신 사용자 시험용 인스턴스·Variant 사용을 검토한다.

```text
VoiceAgent 호스트
 ├─ VoiceAgent / SceneStageContext / DebugUI
 └─ AgentActionRunner (기존 위치 유지)

player 루트 (이동 소유자 하나)
 ├─ AgentBodyController → 위 VoiceAgent 명시 참조
 ├─ 기존 충돌 구성 (별도 검토 전 유지)
 └─ KyleVisual
    └─ 실제 모델 / Animator / 실제 Avatar·Controller
```

외형만 붙이면 Body의 기존 루트 이동은 따라가지만 걷기 애니메이션·손 집기가 자동 연결되지는 않는다. 기존 큐브 MeshRenderer는 필요할 때 사용자 시험 씬에서만 숨기고, Body가 있는 루트 GameObject 자체를 비활성화하지 않는다. 비활성화하면 Body 등록이 해제되고 작업이 취소된다.

Animator/Avatar는 실제 모델의 Rig(Generic/Humanoid 등)과 클립을 먼저 확인한다. 근거 없이 Humanoid로 강제 변경하지 않는다. 클립이 없으면 Idle/Walk가 있다고 가정하지 않는다. 초기 외형 단계의 위치 결정은 Body에만 두고 루트 모션에 의한 추가 이동을 막는다. Animator의 Apply Root Motion과 OnAnimatorMove 사용 여부를 함께 점검한다.

## 4. Animator 연결 계획

**미구현 제안:** 최소 애니메이션 브리지 하나를 추가할 수 있다. 예시 이름 `RobotAnimationBridge`는 아직 존재하지 않는다.

- 이동 애니메이션은 LLM speech나 TurnCompleted가 아니라 루트의 실제 프레임 변위/속도를 기준으로 구동한다. Runner Executing에는 wait·집기 등도 포함되므로 Executing만으로 Walk를 켜지 않는다.
- 실제 Animator 파라미터 목록을 확인해 속도·이동 여부를 매핑한다. Speed, MotionSpeed 등은 예시일 뿐 설치본에 존재한다고 가정하지 않는다.
- Waiting/취소/실패/종료와 실제 정지 상태에서는 이동 파라미터를 0으로 복구한다. 부드러운 전이와 위치 중지는 분리해 검증한다.
- 집기·손 소켓은 별도 단계다. 실제 rig/손 본을 확인한 뒤 carryAnchor를 두는 방안을 검토한다. 현재 머리 위 집기 방식을 손 집기 완료로 기록하지 않는다.
- 감정 이름에 동일한 Animator Trigger가 있다고 가정하지 않는다. 트리거를 쓰려면 실제 Controller에 일치하는 파라미터를 먼저 설계해야 한다.

## 5. 단계 B — 실제 이동 컨트롤러 통합

실제 설치 코드 확인 후 선택한다. **지금은 최종 이동 백엔드·클래스명을 결정하지 않는다.**

| 설치본 확인 결과 | 연결 방향 |
|---|---|
| 모델·Animator만 있음 | 단계 A 유지. Body 이동을 재사용하고 애니메이션 브리지만 추가 |
| CharacterController 기반 컨트롤러 있음 | 입력·카메라·속도·중력·루트 소유권 확인 후 AI 방향/정지 어댑터 검토 |
| 플레이어 입력을 직접 읽는 컨트롤러 있음 | PlayerInput/WASD 경로와 AI 경로를 동시에 활성화하지 않음. 실제 코드의 외부 입력 API를 확인 |
| NavMesh/Rigidbody/루트 모션 기반 | Body Transform 이동과 동시 실행 금지. 도달·실패·정지 계약을 백엔드에 맞춰 별도 설계 |

어댑터를 도입하더라도 Runner의 ID·1초 대기·재검증·종료 이벤트는 유지한다. Body 내부의 이동 호출만 추상화하는 최소 변경을 우선한다. 예시 `RobotMovementAdapter`는 현재 미구현이다.

취소 시 목표·입력·수평 이동 속도 등 백엔드 상태를 명시적으로 지워야 한다. 중력이나 물리 관성은 별도 계약이 필요하므로 모든 velocity를 무조건 0으로 만드는 것으로 결정하지 않는다. 도달 판정은 실제 위치를 사용하고 장애물로 막히면 현재 기본 행동 제한 30초 안에서 실패 처리해야 한다.

## 6. 충돌 가능성

- Body와 기존 컨트롤러/루트 모션이 동시에 위치를 쓰면 이중 이동·회전 경쟁이 발생할 수 있다.
- 기존 컨트롤러 Update가 입력을 유지하면 Runner 취소 후에도 로봇이 움직일 수 있다.
- 카메라 기준 WASD 방향과 Body의 forward/back/left/right 기준이 다를 수 있다.
- Kyle 모델 전방·루트 회전·스케일이 맞지 않으면 옆걸음·발 미끄러짐·잘못된 손 위치가 생길 수 있다.
- 자식 Rigidbody/Collider·루트 CharacterController가 중복되면 충돌과 집기 방식에 영향을 줄 수 있다.
- Animator가 스케일·루트·부모를 갱신하면 Body의 이동·부모 변경과 충돌할 수 있다.
- 같은 VoiceAgent에 Body를 두 개 붙이면 Runner가 오류를 낸다. 큐브 Body와 Kyle Body를 동시에 연결하지 않는다.
- Starter Assets 설치본이 현재 Cinemachine/Input System 버전과 맞는지는 미확인이다. Import 요구로 공용 패키지 변경이 생기면 별도 검토한다.

## 7. 사용자가 Unity에서 수행할 준비·설정

**이번에 수행된 설정이 아니라, 에셋 확인 후 별도 승인된 시험 단계의 절차**다.

1. Project 창에서 Kyle 모델·프리팹·Animator Controller·스크립트의 실제 경로를 확인한다. 없으면 정확한 에셋과 버전을 먼저 결정한다. 아직 공용 Packages/ProjectSettings를 임의 변경하지 않는다.
2. Inspector에서 Rig, Avatar, Animator Controller, 클립 목록, Apply Root Motion, OnAnimatorMove 스크립트, 이동/입력/카메라 의존 컴포넌트를 기록한다.
3. 현재 큐브 시험 씬을 유지하고 별도 시험 씬/프리팹 Variant에서 연결한다. 기존 테스트 장면을 덮어쓰지 않는다.
4. 단계 A에서는 player 루트를 그대로 두고 Kyle 외형을 자식으로 배치한다. localPosition·localRotation·스케일과 모델 전방·바닥 높이를 맞춘다. 유효한 Avatar/Controller는 실제 에셋 기준으로 지정한다.
5. 위치를 움직이는 주체를 Body 하나로 제한한다. 외부 컨트롤러는 의존성을 확인한 뒤 시험 인스턴스에서 비활성화하거나 분리한다. Animator가 필요로 하는 컴포넌트까지 일괄 삭제하지 않는다.
6. Body.voiceAgent는 기존 호스트에 명시 연결한다. Runner는 기존 호스트에 하나만 유지하고 Intent Delay Seconds=1로 둔다.
7. 외형 이동·정지부터 검증한다. 브리지 코드가 추가되기 전에는 Inspector 참조만으로 걷기/집기를 완성할 수 없다는 점을 확인한다.
8. 애니메이션 브리지 또는 이동 어댑터 구현 뒤 실제 파라미터·컨트롤러 참조를 지정하고 아래 회귀 시험을 수행한다.

## 8. 통합 통과 조건 — 아직 모두 미실행

- 채팅과 실제 음성으로 Kyle 외형/루트가 같은 대상에 이동·집기하며 두 번 실행하지 않는다.
- 예고·1초 대기 중 루트 이동 없음, Esc 취소 뒤 실행 없음.
- 이동 중 Esc 이후 추가 수평 이동 없음. Idle/이동 파라미터가 실제 정지와 맞는다.
- 새 명령 선점·늦은 응답 무시·현재 작업 보존이 유지된다.
- 루트 모션·입력 컨트롤러로 인한 이중 이동·회전·발 미끄러짐을 확인한다.
- 집기 위치·높이·충돌이 모델에 맞으며 손 집기는 구현한 범위만 판정한다.
- 중력·벽·경사·도달 실패는 해당 백엔드 구현 후 별도 검증한다. 단순 외형 연결 성공으로 경로 안전성을 완료 처리하지 않는다.

현재 사용자 성공 보고는 큐브 기반 [U01~U06](TEST_PLAN.md)이며 Kyle 통합 성공 보고가 아니다.

## 9. 기술 근거

현재 코드 분석 외의 엔진 동작 근거는 Unity 6.3 공식 문서를 확인했다.

- 루트 모션 적용 여부와 OnAnimatorMove 처리 관계: [Animator.applyRootMotion](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/animator/applyrootmotion).
- CharacterController.Move는 충돌에 따라 이동이 제한되며 중력을 자동 적용하지 않는다. 컨트롤러 교체 시 중력 구현도 점검해야 한다: [CharacterController.Move](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/charactercontroller/move).

이 문서는 에셋 배포 페이지나 설치본을 확인한 것으로 대체하지 않는다. 실제 Kyle 스크립트·파라미터·라이선스·패키지 요구는 Import 조사 후 확정한다.
