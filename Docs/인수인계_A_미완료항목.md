# 《사건기록: 누락》 미완료 항목 — 후속 AI 전달용

- 작업일: 2026-11-09
- 검수 범위: Unity 프로젝트 `Case File_Omission`의 `Assets/Scripts`·`Assets/Tests/EditMode` 코드와 노션 기획 정리본(홈, 03 메인 사건, 04 의뢰 사건, 06 게임 시스템, 07 NPC 및 경찰, 08 모델링 관련) 대조. 대조 항목은 회의 카드 판단, NPC 행동, 증언·문서 자료, 최종 보고서, 기타 기획(성격 명칭·비밀번호·관리표·시간 칸), 기존 메서드 4개.
- 기획 원본: 노션 "《사건기록: 누락》 — 기획 정리본" https://app.notion.com/p/410cdcd13e998112962ae3600f60a7c9 (01~10 주제 페이지가 기준, 보관본은 원문 대조용)
- 사용자 결정이 필요한 항목은 별도 문서 "사용자 결정 필요 항목 — 후속 AI 전달용"에 있다. 이 문서에는 구현 또는 검증만 남은 항목만 적는다.

## 이번 작업에서 해결해 목록에서 뺀 항목

- `NpcActionLoop`의 "할 일이 없으면 무작위 인접 이동" → 07 확정 "할 일이 없으면 대기한다"와 충돌하므로 대기(`Stay`)로 수정함. 같은 NPC 쌍에 이미 전달한 정보는 다시 시도하지 않게 함.
- `RoomGraph.FindPath`, `RoomOccupancyTracker.Remove`, `RoomOccupancyTracker.GetOccupants`, `ClueRecord.MarkHeldByNpc` → 모두 이미 구현되어 있었음(중복 작성하지 않음). 직접 검증하는 테스트가 없어서 추가함.
- 거짓 알리바이·반박 자료와 회의 카드 판단의 연결 → `EvidenceMaterialGenerator.BuildContradictionIndex`가 거짓 자료마다 반박 자료 ID와 반박 자료의 정보 ID를 `CardContradictionIndex`에 '명확한 모순'으로 등록하고, `NpcCardJudge`·`PoliceCardJudge`가 판정자가 실제로 아는 정보로만 모순을 센다. 연결 구조는 이미 맞게 구현되어 있음을 확인함(테스트 실행 결과는 아래 1번 참고).
- 검증을 거친 수용의 의심도: 원문은 "연속 수용 감소량 + 검증 보너스 -8"이며, `MeetingSession.RecordAccepted(verified: true)`와 `MeetingSuspicionTable.VerifiedAcceptBonus = -8f`가 그대로 구현되어 있음을 확인함.

## 1. 테스트 실행 결과 (참고 — 미완료 항목 아님)

- 실행 방법: Unity 에디터를 닫은 상태에서 `Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults <파일>` (Unity 6000.6.0f1). 에디터를 연 상태에서는 이 세션의 MCP for Unity 서버가 에디터를 찾지 못했다(`No Unity Editor instances found`, Stdio·HTTP 모두). 원인은 `미확인`.
- 결과(2026-11-09, 이번 변경 포함): EditMode 전체 362개 중 통과 362, 실패 0, 건너뜀 0.
- 클래스별 결과(모두 통과): `NpcActionLoopTests` 6, `NpcBehaviorPlannerTests` 25, `MeetingCardJudgementTests` 17, `ContractTimelineTests` 13, `EvidenceMaterialTests` 11, `FinalReportMappingTests` 16, `MainCaseClueAcquisitionTests` 8, `WorldStateSaveServiceTests` 14, `RoomOccupancyTrackerTests` 8, `ClueRepositoryTests` 7
- 이번에 수정·추가한 테스트: `NpcActionLoopTests`(`DecideAndAct_NoOneToTalkTo_StaysInPlace`, `DecideAndAct_AlreadyTransferredInfo_IsNotRetried`, `DecideAndAct_IgnoresNpcsInDifferentRooms` 기대값 변경), `RoomOccupancyTrackerTests`(`GetOccupants_ReturnsOnlyThatRoom_AndRemoveClearsPosition`, `FindPath_AvoidsBlockedRoom_OrReturnsNull`), `ClueRepositoryTests`(`MarkHeldByNpc_ChangesWorldStateOnly_RecordStaysInReport`)
- 주의: 테스트가 모두 통과했더라도 아래 2~4번 동작은 구현되지 않았다.

## 2. NPC 행동 시스템의 실제 실행 경로 연결

- 현재 상태: `미완료`
- 관련 파일: `Assets/Scripts/NPC/NpcActionLoop.cs`, `Assets/Scripts/NPC/NpcBehaviorPlanner.cs`
- 현재 구현 상태: 두 클래스 모두 게임 코드(런타임·MonoBehaviour·다른 서비스)에서 호출하는 곳이 없고 테스트에서만 생성된다. 그래서 "실제 실행 경로"는 아직 없다. 둘을 비교하면 다음과 같다.
  - `NpcBehaviorPlanner`: 한 턴 1회 행동, 대화 상대의 턴 소모, 생존·위치·지식·접촉 검사, 일정 이동, 막힌 경로 우회·대기·포기, 단서 자동 획득, 방향성 관계 의심도 반영
  - `NpcActionLoop`: 정보 전달 아니면 대기. 관계 의심도는 항상 `SuspicionLevel.None`이고, 턴 중복 방지가 없다. 방에 있는 상대가 정보를 보내는 방향(상대 → 행동 NPC)이다.
- 미완료 원인: 게임 루프나 턴 진행을 담당하는 런타임 코드가 아직 없다.
- 이미 시도한 것: `NpcActionLoop`의 확정 규칙 위반(무작위 이동)만 수정했다. 둘을 하나로 합치는 작업은 호출부가 정해지지 않아 하지 않았다.
- 테스트 결과: 단위 테스트(`NpcActionLoopTests` 6, `NpcBehaviorPlannerTests` 25)는 통과. 게임 루프와 연결한 통합 동작은 테스트 없음.
- 다음 AI가 할 일: 시간 구간 진행(`GameStateController.TimeBlockAdvanced`) 또는 조사 턴 진행 지점에서 `NpcBehaviorPlanner.ExecuteTurn`을 호출하도록 연결한다. `NpcActionLoop`는 새 호출부에서 쓰지 않도록 `[Obsolete]` 처리를 검토한다. 중복 행동, 턴 이중 소모, 이동·접촉·단서 획득이 함께 일어날 때 충돌이 없는지 통합 테스트를 추가한다.
- 기획 근거: 07 "NPC 행동 결정 구조", 홈 "공통 이벤트 처리 순서".

## 3. 보류한 의문을 다음 회의로 넘기는 처리

- 현재 상태: `미완료`
- 관련 파일: `Assets/Scripts/Meeting/MeetingCardResolver.cs`, `Assets/Scripts/Meeting/MeetingSession.cs`
- 현재 구현 상태: `MeetingCardResolver.Defer`는 의문을 `Deferred` 상태로만 바꾼다. 리졸버가 회의(`MeetingSession`) 하나에 묶여 있어 다음 회의로 옮기는 경로가 없다. "다음 회의에서 가장 먼저 처리"를 강제하지 않고, "다음 회의에서도 넘김"을 감지하지 않는다.
- 미완료 원인: 회의 간 상태를 이어 주는 구조가 없다. 넘겼을 때의 의심도 상승량은 원문에 없다(사용자 결정 문서 B-3).
- 이미 시도한 것: 없음. 원문을 대조해 상승량 수치가 없음을 확인했다.
- 테스트 결과: 테스트 없음.
- 다음 AI가 할 일: 보류된 `CardQuestion`을 다음 회의의 리졸버로 넘기는 API를 만든다(예: 생성자 또는 `ImportDeferred`). 다음 회의가 시작되면 보류 의문을 먼저 처리하지 않고는 새 카드 제시를 막거나 경고한다. 두 번째 회의에서도 보류하면 그 의문에 '이월 2회' 표시를 남긴다. 상승량은 주입식 설정(기본값 없음)으로 두고, 결정이 나기 전에는 적용하지 않는다. 저장 데이터(`WorldSaveData`의 Meeting)에도 반영한다.
- 기획 근거: 06 §6 "보류한 의문은 바로 다음 회의에서 가장 먼저 처리해야 한다. 다음 회의에서도 처리하지 않고 넘기면 의심도가 상승한다", 06 §12 "조사 결과를 이용한 다음 회의의 재검증 — 확정".

## 4. 조사 시간 구간 명칭과 의미 불일치

- 현재 상태: `검증 필요`
- 관련 파일: `Assets/Scripts/Core/TimeBlock.cs`, `Assets/Scripts/Core/GameStateController.cs`, `Assets/Tests/EditMode/GameStateControllerTests.cs`, `Assets/Tests/EditMode/SaveGameServiceTests.cs`
- 현재 구현 상태: `TimeBlock`은 `Morning`/`Afternoon`이고 주석의 근거는 "통합 명세서 §2-2 확정: 하루는 오전/오후 두 조사 구간"이다. 의뢰는 항상 `Morning`으로 시작한다.
- 미완료 원인: 현재 기준 문서 06 §20-2는 "저녁에 시작하면 아침까지, 아침에 시작하면 저녁까지 조사한다"고 하므로, 두 구간은 낮(아침→저녁)과 밤(저녁→아침)이다. 코드 주석이 인용한 "통합 명세서 §2-2"는 현재 기준 페이지에서 찾지 못했다(`미확인`). 두 구간·하루 증가 구조는 같고 이름과 시작 구간만 다르다.
- 이미 시도한 것: 없음. 이름을 바꾸면 테스트·저장 데이터에 영향이 있어 근거를 확인한 뒤 진행하도록 남겼다.
- 테스트 결과: 현재 오전/오후 동작을 다룬 `GameStateControllerTests`·`SaveGameServiceTests`는 통과(전체 362개 통과에 포함). 06 기준의 낮/밤 구간·시작 구간 동작은 테스트 없음.
- 다음 AI가 할 일: 노션 보관본에서 "통합 명세서 §2-2"를 찾아 06 §20-2와 어느 쪽이 최신인지 확인한다. 06이 기준이면 `Afternoon`을 `Evening`(밤 구간)으로 바꾼다. `JsonUtility`는 enum을 정수로 저장하므로 값 순서를 유지하면 기존 저장과 호환된다. 시작 구간을 고르는 규칙이 없다면 사용자 결정 문서 B-16을 따른다.
- 기획 근거: 06 "20. 개별 의뢰 진행 흐름 — 2. 조사와 상호작용", "3. 조사 시간 종료와 회의".
