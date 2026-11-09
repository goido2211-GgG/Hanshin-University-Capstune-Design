# 1차 리팩터링 검증 도구

기준일: 2026-11-10. 새 패키지를 설치하지 않고 로컬 .NET 10 SDK, 기존 Unity 6000.3.25f1 DLL, 프로젝트 Library의 Newtonsoft.Json DLL을 사용한다. bin/obj/.cli는 이 폴더의 .gitignore로 제외한다. 루트 .gitignore와 Packages는 변경하지 않는다.

## 실행

프로젝트 루트 PowerShell에서:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) 'Docs/VoiceAI/Validation/.cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet build Docs/VoiceAI/Validation/UnityCompile.csproj --configfile Docs/VoiceAI/Validation/NuGet.Config --nologo
dotnet run --project Docs/VoiceAI/Validation/BehaviorTests.csproj --configfile Docs/VoiceAI/Validation/NuGet.Config
```

Unity 설치 경로가 다르면 빌드에 `-p:UnityEditorRoot="설치한 Editor 경로"`를 추가한다. 프로젝트의 로컬 Newtonsoft.Json 캐시가 없으면 이 도구는 참조를 찾지 못한다. 네트워크 패키지 원본은 NuGet.Config에서 비활성화했다. 이 도구가 프로젝트 패키지를 설치하거나 설정을 수정하지 않는다.

## 검증 범위

- UnityCompile: Assets/VoiceAI/Runtime의 모든 실제 C# 소스를 Unity 6.3 엔진 DLL로 참조 컴파일한다. .NET 10에서의 참조 빌드이므로 Unity Editor 자체의 스크립트 컴파일·빌드 타깃·IL2CPP 검증을 대신하지 않는다.
- BehaviorTests: 실제 Runner/Body/VoiceAgent/Brain/검증 코드를 컴파일해 시험한다. UnityShim으로 GameObject·Transform·라이프사이클을 최소 대체하고 키 로더·녹음·API 전송을 가짜로 바꾼다. 실제 비밀 파일·네트워크·마이크는 사용하지 않는다.
- Shim은 물리·회전·IMGUI·Unity SynchronizationContext·실제 프레임 순서·네이티브 객체 파괴를 완전히 재현하지 않는다. 해당 항목은 Unity에서 수동 시험해야 한다.

## 실제 수행 결과

- 전체 런타임 참조 빌드: 오류 0, 경고 0.
- 동작 시험: 기존 실행/취소 25개 + 걷기 연결 10개 + 반자율 탐색 17개 = 총 52개 통과. Controller 파라미터/임계값, Idle/Walk·정지·방향·집기 회귀와 탐색 3초·대상 필터·거리/ID·반복 방지·플레이어 선점·지속 중지·로컬 재개·실제 대상 재검증·출처 화이트리스트·이전 결과 무시를 검사한다.
- 에셋 계약 사례는 프로젝트 루트에서 실행하고 Import된 `Assets/Starter Assets/.../StarterAssetsThirdPerson.controller`를 읽는다. 해당 에셋이 없으면 실패하며 가짜 Controller로 조용히 대체하지 않는다.
- 이번 탐색 변경의 Unity Editor 컴파일 / Play Mode / 실제 API / 실제 마이크 / IMGUI: Codex 미실행. 이전 사용자 U01~U06 및 걷기·완료 Idle U07/U08은 TEST_PLAN의 별도 출처 기록이다. Bridge 연결 이후 Esc는 아직 사용자 성공 보고가 없다.
- 실패한 자동 시험: 최종 실행에서 없음.

구체적인 테스트 구현은 BehaviorTests.cs에 있으며, 사용자가 수행할 실제 Unity 절차는 상위 [TEST_PLAN.md](../TEST_PLAN.md)에 기록한다.
