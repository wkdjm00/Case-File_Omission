# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 프로젝트 개요

Case File_Omission — Unity 6 (`6000.6.0f1`, URP) 게임. 현재는 게임 규칙(의뢰·단서·NPC·회의·경찰·보고서·엔딩·저장)을 순수 C# 로직으로 구현하는 단계이며, 씬(`Assets/Scenes/SampleScene.unity`)과 연결된 게임플레이 코드는 아직 없다.

코드 주석의 `§2-2`, `§15`, "06페이지 확정" 같은 표기는 저장소 밖 기획 문서의 절·페이지 번호다. 규칙을 바꿀 때는 해당 주석이 가리키는 확정 사항과 어긋나지 않는지 확인한다. 주석·로그 메시지는 한국어로 쓴다.

## 테스트

모든 테스트는 EditMode NUnit 테스트(`Assets/Tests/EditMode`, 어셈블리 `CaseFileOmission.Tests.EditMode`)이며 PlayMode 테스트는 없다.

- Unity 에디터: Window > General > Test Runner > EditMode.
- 커맨드라인(에디터가 이 프로젝트를 열고 있지 않을 때만):
  ```
  "<Unity 6000.6.0f1 설치 경로>\Editor\Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults TestResults.xml [-testFilter ClassName]
  ```

`*.csproj`, `Library/` 등은 Unity가 생성하며 git에서 무시된다. 별도 lint나 빌드 스크립트는 없다.

## 아키텍처

`Assets/Scripts/<모듈>/` 폴더마다 asmdef(`CaseFileOmission.<모듈>`)가 있고, 의존 방향은 asmdef `references`로 강제된다. 새 의존을 추가하면 순환이 생기지 않는지 asmdef에서 확인한다.

- **Core** (의존 없음): `GameStateController`(의뢰 단계 Idle→Investigation→Meeting→Resolved, 오전/오후 `TimeBlock`과 일차), `GameEventLog`, `GameLogger`(§15 구조화 오류 기록 — 플레이어 비노출), `ProbabilityCalculator`(정수 %p 보정 합산 후 하한·상한 적용; 모든 확률 시스템 공통), `ActionGuards`, 다른 모듈이 구현하는 인터페이스(`IAliveCheckable`, `IRoomConnectivityProvider`).
- **Room** → Core. 네임스페이스는 폴더명과 달리 `CaseFileOmission.Rooms`.
- **Contracts** → Core, Room: 의뢰 생성·검증(`ContractGenerator`, `ContractValidator`, 진실/공식결론/외부상태).
- **Evidence** → Core, Contracts: 단서 저장소, 증거 조작·결과 판정, 수용량.
- **NPC** → Core, Contracts, Room, Evidence: 일정, 행동 계획, 정보 전달, 회유, 관계.
- **Meeting** → Core, Evidence, NPC: 회의 세션, 카드 뽑기·판정, 거짓말 탐지.
- **Police** → Core, Meeting, Evidence: 의심도, 추적, 생존 상태, 회의 의심 조정.
- **Organization**, **Reports** → Core, Contracts, Evidence. **Combat** → Core.
- **ContractContent** → 의뢰 콘텐츠(타임라인·증거 자료) 생성, 여러 모듈 조합.
- **Endings** → Core, Police, Organization. **MainCase** → 메인 사건 진행·최종 보고서·보고 후 분기.
- **SaveLoad** → 거의 모든 모듈. `SaveGameService`(상위 상태) + `WorldStateSaveService`(`WorldStateTargets`에 묶인 세부 상태 Capture/Restore).

설계 규칙(여러 파일에 걸쳐 일관되게 지켜지는 것):

- 게임 로직은 MonoBehaviour가 아닌 순수 C# 클래스다. UnityEngine 사용은 설정용 ScriptableObject(`ContractGenerationConfig`, `ClueDefinition`, `FinalReportAnswerConfig`)와 SaveLoad의 직렬화/파일 저장 정도로 한정된다.
- 확률 판정 서비스는 생성자에서 `System.Random`을 선택적으로 받는다(`random ?? new Random()`). 테스트는 시드 고정 `Random`을 주입해 결정적으로 검증한다.
- 상태 전이는 잘못된 단계에서 호출되면 `InvalidOperationException`을 던진다. 저장 복원용 `RestoreState` 류 메서드만 전이 규칙을 우회한다.
- 복원은 확률 판정·이벤트·조작을 다시 실행하지 않고 결과 상태만 되살린다. 잘못된 참조는 건너뛰고 `GameLogger`에 남긴다. 런타임 상태를 추가하면 `WorldSaveData`/`WorldStateSaveService`와 `WorldStateSaveServiceTests`도 함께 갱신한다.
