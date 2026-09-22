# IMEJapanese 오류 수정 및 개선 계획

## 1. 한글CAPS 모드 멈춤 현상(입력 불가) 해결
**원인 분석**: 
일본어를 입력하던 도중(자음 대기 상태 등) 사용자가 한/영 키를 누르거나 마우스 클릭 등으로 포커스가 변경되면, 내부 조합 상태(`_waitingVowel`, `_pendingConsonant` 등)가 꼬인 채로 남아있게 됩니다. 이 상태에서 다시 한글CAPS 모드(일본어 모드)로 돌아오면 이전의 꼬인 상태 때문에 일본어 입력이 무시되거나 비정상 작동하여 한글이 튀어나오게 됩니다.

**해결 방안**:
- `GlobalInputHook`에서 키보드 입력을 감지할 때마다 이전의 `capsOn` 및 `isHangulMode` 상태를 추적합니다.
- 사용자가 한/영 키를 눌러 `isHangulMode`가 바뀌거나, CapsLock 상태가 변경되어 **입력 모드가 전환된 것을 감지하면 즉시 내부 조합 상태(자음 대기 상태 등)를 강제 초기화(Reset)** 합니다.
- 이를 통해 모드 전환 후 다시 일본어 모드로 돌아왔을 때 항상 깔끔한 상태에서 일본어 입력이 시작되도록 보장합니다.

## 2. UI 구성요소 간 입력 모드 동기화 개선
**원인 분석**: 
`MainForm.cs`의 `SyncSystemHangulState` 메서드에서 포커스 변경 시 입력 모드(Hangul 상태)를 동기화하고 있으나, 동기화 대상이 `LastValidHwnd`, `_frmKeyboardLayout`, `MainForm`으로 한정되어 있습니다. 한자 변환창(`KanjiCandidateOverlay`)이나 트레이 메뉴 등으로 포커스가 이동할 때 상태가 완벽하게 전파되지 않는 문제가 있습니다.

**해결 방안**:
- `KanjiCandidateOverlay` 등 앱 내부의 모든 UI 윈도우 핸들에 대해서도 Hangul 상태 동기화가 적용되도록 `SyncSystemHangulState`를 확장합니다.
- 사용자가 트레이 메뉴나 배열창 등 앱 소속 창에서 한/영 키를 눌러 상태를 변경하면, 그 변경사항이 현재 작업 중이던 원본 창(`LastValidHwnd`)으로 즉각 반영(SetHangulState)되도록 양방향 동기화 로직을 강화합니다.

## 3. 치명적 오류 로깅 (IMEJapanese.log)
**해결 방안**:
- 실행 파일이 위치한 폴더에 `IMEJapanese.log` 파일을 생성합니다.
- `GlobalInputHook` 등 앱의 핵심 로직에서 예기치 않은 예외(Exception)가 발생할 경우, 디버깅을 위해 상세한 오류 스택 트레이스와 에러 코드를 해당 로그 파일에 타임스탬프와 함께 기록하도록 `Trace` 리스너를 강화합니다.

---

> [!IMPORTANT]
> **User Review Required**
> 위 계획에 따라 모드 전환 시 조합 상태를 강제로 초기화하고 동기화 범위를 넓히겠습니다. 계획에 동의하시면 승인(Proceed)을 눌러주세요. 즉시 코드 수정을 시작하겠습니다!
