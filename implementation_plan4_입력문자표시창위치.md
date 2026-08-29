# 입력문자 표시창 위치 개선 계획

## 배경 및 목표

현재 `ShowOverlay()`는 `ResolveCaretPosition()`을 사용해 마우스 포인터 위치를 기준으로 팝업을 표시합니다.
`KanjiCandidateOverlay.ShowAtLocation()`은 이미 `ResolveCaretRectangle()`을 사용해 캐럿의 정확한 직사각형을 기준으로 위치를 계산합니다.

이 계획은 텍스트 오버레이의 위치 계산을 용도별로 다르게 적용하도록 개선합니다.

## 표시창 위치 규칙 (요구사항 정리)

| 표시 유형 | 예시 | 기준 | 정렬 |
|---|---|---|---|
| **입력 글자** | `あ`, `ア`, `が` 등 | 입력 글자 바로 아래줄 | 오른쪽 끝 정렬 |
| **HK/YN 단일 전환** | `ひ→び`, `ア→ャ` (lastOutputChar 기반) | 글자 바로 아래줄 | 오른쪽 끝 정렬 |
| **선택 문자열 HK/YN 전환** | `あ→び` (selected 기반) | 선택 문자열 바로 아래줄 | **왼쪽** 끝 정렬 |
| **입력 모드 전환** | `한글CAPS모드`, `영어 소문자 모드`, `일본어1_조합형` 등 | 화면 정중앙 (좌우), 위에서 3/4 |
| **Layer 전환** | `일본어3_Layer2`, `Katakana`, `Hiragana` 등 | 화면 정중앙 (좌우), 위에서 3/4 |

## 구현 방법

### 1. `OverlayPositionMode` enum 추가 (`Program.cs`)

```csharp
public enum OverlayPositionMode
{
    CharInput,       // 입력 글자 - 캐럿 바로 아래, 오른쪽 끝 정렬
    CharToggle,      // 단일 글자 HK/YN 전환 - 캐럿 바로 아래, 오른쪽 끝 정렬  
    SelectionToggle, // 선택 문자열 전환 - 선택 영역 바로 아래, 왼쪽 끝 정렬
    ModeSwitch,      // 입력 모드/Layer 전환 - 화면 중앙 3/4
}
```

### 2. `ShowOverlay()` 시그니처 변경 (`Program.cs`)

```csharp
public void ShowOverlay(string text, int durationMs = AppConfig.OverlayDefaultDurationMs, 
                         OverlayPositionMode mode = OverlayPositionMode.CharInput)
```

### 3. `ExecuteShowOverlay()` 위치 계산 로직 변경 (`Program.cs`)

기존 `ResolveCaretPosition()` 대신 `KanjiCandidateOverlay.ShowAtLocation()`과 동일한 로직인 `ResolveCaretRectangle()`을 활용한 새 헬퍼 메서드 `CalculateOverlayLocation()` 구현:

- **CharInput / CharToggle**: `ResolveCaretRectangle()`의 Bottom 아래, overlay.Right를 caret.Right에 맞춤
- **SelectionToggle**: `ResolveCaretRectangle()`의 Bottom 아래, overlay.Left를 caret.Left에 맞춤  
- **ModeSwitch**: 화면 WorkingArea 기준 좌우 중앙, 세로 3/4 위치
- 화면 벗어남 방지 + 입력 글자/선택 영역 가리지 않도록 처리

### 4. 호출 측 변경 (`Keymaps.cs`, `ImeNativeCore.cs`)

#### CharInput (글자 오른쪽 끝 정렬):
- `Japanese1Map/2Map/3Map`: `ShowOverlay(ch)` → `ShowOverlay(ch, mode: OverlayPositionMode.CharInput)`

#### CharToggle (단일 글자 HK/YN - `lastOutputChar` 기반):
- `TransformAndReplaceText()`의 `lastOutputChar` 분기: `ShowOverlay("ひ→び", mode: CharToggle)`
- `ApplyPendingTransformation()`: `ShowOverlay("ひ→び", mode: CharToggle)`
- `TogglePendingHiraKata()`, `TogglePendingYn()`: `ShowOverlay(_pendingChar, 0, CharToggle)` (durationMs=0, 유지 표시)

#### SelectionToggle (선택 문자열 HK/YN - `selected` 기반):
- `TransformAndReplaceText()`의 `selected` 분기: `ShowOverlay("あ→び", mode: SelectionToggle)`

#### ModeSwitch (모드/Layer 전환):
- `ImeNativeCore.cs` L460: `ShowOverlay(UiText.HangulCapsMode, mode: ModeSwitch)`
- `Keymaps.cs` L512, L519, L553, L560, L688, L949, L1038, L1043, L1052`

> [!NOTE]
> 현재 `ShowOverlay`의 두 번째 매개변수인 `durationMs`는 int이고, `OverlayPositionMode`는 새 세 번째 매개변수입니다.
> 기존 `ShowOverlay(text, 0)` 호출은 `ShowOverlay(text, 0, CharToggle)` 형태로 변경이 필요합니다.

## 변경 파일 목록

### [MODIFY] [Program.cs](file:///d:/VSCODE/IMEJapanese/Program.cs)
- `OverlayPositionMode` enum 추가
- `ShowOverlay()` 시그니처에 `mode` 매개변수 추가  
- `ExecuteShowOverlay()` 위치 계산 로직을 mode별로 분기
- `ResolveCaretPosition()` 제거 (또는 내부 전용으로 유지)
- `CalculateOverlayLocation()` 신규 헬퍼 메서드 추가

### [MODIFY] [ImeNativeCore.cs](file:///d:/VSCODE/IMEJapanese/ImeNativeCore.cs)
- L460: `ShowOverlay(UiText.HangulCapsMode)` → ModeSwitch 모드

### [MODIFY] [Keymaps.cs](file:///d:/VSCODE/IMEJapanese/Keymaps.cs)
- 각 `ShowOverlay()` 호출에 적절한 `OverlayPositionMode` 추가

## 검증 계획

- 빌드 오류 없이 컴파일되는지 확인
- 각 모드별 시각적 동작 확인 (사용자 직접 테스트)

## 오픈 질문

없음 — 요구사항이 명확합니다.
