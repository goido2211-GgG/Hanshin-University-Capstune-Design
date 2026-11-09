# VoiceAI: 음성/채팅 → AI 요원 명령

## 1차 실행·취소 리팩터링 (2026-11-09)

- `VoiceAgent`는 STT/LLM 해석, `AgentActionRunner`는 작업 ID·의도·기본 1초 대기·취소·최신 검증, `AgentBodyController`는 단일 행동 실행을 담당합니다.
- 기존 단일 로봇 씬은 런타임 Runner 자동 연결을 사용합니다. Body의 VoiceAgent가 모호하면 Inspector에서 직접 지정합니다.
- 중지 버튼, Esc, 채팅 `중지`/`멈춰`/`취소`/`stop`은 API 없이 처리합니다. 새 채팅·녹음 시작은 이전 작업을 대체합니다.
- `TurnCompleted`는 해석 완료입니다. 실제 실행은 Runner의 `ActionFinished`, `CommandFinished`로 확인합니다. 외부 코드가 Actions를 다시 실행하면 안 됩니다.
- 실행 지원: 이동·조사·집기·내려놓기·대기·중지. 퍼즐 동사와 follow_drone은 거부합니다. 무작위 강제 오해는 새 Brain 경로에서 사용하지 않습니다.
- 설치된 Unity DLL 참조 컴파일과 대체 환경 자동 시험을 수행했습니다. Unity Play Mode·실제 STT/LLM은 아직 검증하지 않았습니다.

현재 아키텍처·Inspector 절차·시험 한계는 `Docs/VoiceAI/ARCHITECTURE.md`, `TEST_PLAN.md`를 따릅니다. 아래 API와 팀 연동 예시 중 행동을 별도 실행하는 부분은 Runner 연동으로 이전해야 합니다.

플레이어의 말을 받아 요원의 대사, 감정, 행동으로 바꾸는 모듈입니다.

```
마이크 → gpt-transcribe(음성→텍스트) → gpt-6-luna(요원 역할 + JSON 응답) → 코드 검증 → 게임 실행
```

## 설치

1. `Assets/VoiceAI` 폴더와 `secrets/README.txt`를 프로젝트에 복사합니다. `.gitignore`가 이미 있으면 내용을 합칩니다.
2. Package Manager → Add package by name → `com.unity.nuget.newtonsoft-json`
3. Edit → Project Settings → Editor에서 Version Control은 Visible Meta Files, Asset Serialization은 Force Text로 둡니다.
4. Unity가 만든 `.meta` 파일도 함께 커밋합니다.

현재 대상은 Unity 6.3 LTS (프로젝트 기록 6000.3.25f1)입니다.

## API 키

프로젝트 폴더(Assets 옆)에 `secrets/openai_api_key.txt`를 만들고 키만 한 줄로 넣습니다.
이 파일은 깃에 올라가지 않으므로 팀원마다 각자 만들어야 합니다.

커밋 전 확인: `git check-ignore secrets/openai_api_key.txt` 명령이 경로를 출력하면 정상입니다.

## 5분 시험

1. 빈 씬에 빈 게임오브젝트를 만들고 `VoiceAgentDebugUI`를 붙입니다. 필요한 컴포넌트는 자동으로 붙습니다.
2. Play를 누르고 V키를 누른 채 말하거나, 채팅창에 입력합니다.
3. 씬에 오브젝트가 없으면 시험용 문, 책상, 카드키, 빨간 버튼으로 동작합니다.

Mac에서 처음 실행하면 마이크 권한을 물어봅니다. 소리가 안 잡히면 시스템 설정 → 개인정보 보호 → 마이크에서 Unity를 켜 주세요.
빌드할 때는 Player Settings의 Microphone Usage Description도 채워야 합니다.

## 팀원별 연동

### 요원, 드론, UI

```csharp
using AgentEscape.VoiceAI;

voiceAgent.TurnCompleted += turn =>
{
    subtitle.text = turn.Response.Speech;
    animator.SetTrigger(turn.Response.Emotion);

    foreach (AgentAction action in turn.Actions)
    {
        // action.Verb에 따라 실행 큐에 추가
    }
};

voiceAgent.StartVoiceInput();  // 말하기 키를 눌렀을 때
voiceAgent.StopVoiceInput();   // 말하기 키를 뗐을 때
voiceAgent.SendChat("문 열어"); // 채팅 입력
```

처리 중 새 입력은 이전 작업을 취소하고 대체합니다. `StateChanged`는 입력 처리 상태이며, 실제 실행 상태는 `voiceAgent.Runner.State`입니다.

| verb | 필드 | 의미 |
|---|---|---|
| `move_to` | target | 대상으로 이동 |
| `move_direction` | direction, steps | forward/back/left/right로 이동 |
| `follow_drone`, `stop`, `wait` | 없음 | 드론 따라가기, 멈춤, 대기 |
| `inspect` | target | 살펴보기 |
| `pick_up`, `drop`, `open`, `close`, `push`, `pull`, `press` | target | 조작 |
| `use` | target, item | 가진 물건을 대상에 사용 |

`emotion` 값: neutral, curious, confused, proud, happy, scared, hurt, annoyed

### 맵, 기믹, 미니게임

요원이 다룰 오브젝트에 `AgentInteractable`을 붙입니다.

| 필드 | 예시 | 비고 |
|---|---|---|
| id | `door_exit` | 씬 안에서 겹치면 안 됩니다 |
| displayName | 출구 문 | 음성 인식 힌트로도 쓰입니다 |
| description | 철문. 옆에 카드 인식기가 있다. | 퍼즐 정답을 쓰지 않습니다 |
| state | 잠김 | 기믹이 바뀔 때 코드에서 갱신합니다 |
| allowedVerbs | open, use | move_to, inspect는 항상 허용됩니다 |
| minTrust | 70 | 설득이 필요한 오브젝트에만 지정합니다 |

요원에게 안 보이는 오브젝트는 컴포넌트를 꺼 두면 AI도 모릅니다.

행동의 결과는 요원에게 알려 주세요. 그래야 다음 대사가 상황과 맞습니다.

```csharp
voiceAgent.ReportEvent("문을 열려고 했지만 잠겨 있었다.");
voiceAgent.ResetConversation(keepTrust: true); // 스테이지 전환 시
```

스테이지 설명과 난이도는 `SceneStageContext`의 `brief`, `confusionLevel`(0~3)에서 정합니다.
요원 위치와 소지품(`agentLocation`, `inventory`)도 게임 코드에서 갱신해야 합니다.

## 프롬프트와 난이도 조절

| 바꿀 것 | 위치 |
|---|---|
| 요원 성격, 말투, 오해 유형 | `Resources/VoiceAI/Prompts/agent_system.txt` |
| 오해 확률, 연속 실패 한도 | `Runtime/Core/FrustrationDirector.cs` |
| 모델, 응답 속도 | `VoiceAgent` 인스펙터 |

- **무작위 강제 오해는 현재 요청 경로에서 사용하지 않습니다.** 자세한 규칙 기반 오해·대상 선택은 후속 단계입니다.
- **퍼즐 판정은 코드가 합니다.** 플레이어가 "규칙을 무시해"라고 해도 허용되지 않은 행동은 실행되지 않습니다.
- **스토리 반전은 프롬프트에 넣지 않습니다.** 주인공이 통 속의 뇌라는 사실을 AI가 모르면 새어 나갈 수도 없습니다. 퍼즐 정답도 마찬가지입니다.

## 주의

- `gpt-transcribe`, `gpt-6-luna`는 OpenAI 문서 기준(2026년 11월) 모델입니다. 모델명은 `VoiceAgent` 인스펙터에서 바꿀 수 있습니다.
- 키가 든 빌드를 외부에 배포하지 마세요. 시연 PC에서만 쓰고, 공개 배포가 필요하면 중간 서버를 둬야 합니다.
