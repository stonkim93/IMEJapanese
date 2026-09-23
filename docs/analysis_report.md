# IMEJapanese 앱 상태 점검 보고서

> 분석 대상 버전: **v1.2.1.0** | 분석 일자: 2026-09-22

---

## 📊 요약

| 분류 | 발견 건수 | 우선순위 |
|---|---|---|
| 🔴 버그 / 잠재적 크래시 | 4건 | 높음 |
| 🟠 안정성 / 스레드 안전성 | 3건 | 높음 |
| 🟡 성능 개선 | 4건 | 중간 |
| 🔵 코드 품질 / 유지보수성 | 5건 | 낮음 |

---

## 🔴 버그 / 잠재적 크래시

### 1. `SendReplacement`에서 backspace 전송 시 타이밍 없음 → 입력 손실 위험
**파일:** [`ImeNativeCore.cs`](file:///d:/VSCODE/IMEJapanese/ImeNativeCore.cs#L344-L363)

```csharp
for (int i = 0; i < backCount; i++) NativeMethods.SendBackspace();
if (!string.IsNullOrEmpty(text)) NativeMethods.SendUnicodeString(text);
```

Backspace 연속 전송과 Unicode 문자열 전송 사이에 **딜레이 없이** 진행됩니다. 대상 앱이 Backspace를 완전히 처리하기 전에 새 문자가 전송되면, 특히 느린 앱(구형 Office, 메모장 등)에서 **이전 글자가 지워지지 않거나 글자가 겹치는 현상**이 발생할 수 있습니다.

**제안:** `SendBackspace()` 루프 후 짧은 `Thread.Sleep(10~20ms)` 또는 `SendInput` 배치 방식으로 교체.

---

### 2. `HandleKanjiConversion`에서 사전 미로드 시 블로킹 대기 로직 불안정
**파일:** [`ImeNativeCore.cs`](file:///d:/VSCODE/IMEJapanese/ImeNativeCore.cs#L679-L693)

```csharp
while (!MozcDictionary.IsLoaded && waited < 2000)
{
    Thread.Sleep(120);
    waited += 120;
}
```

이 코드는 `Task.Run()` 내부에서 `Thread.Sleep`으로 블로킹합니다. 최대 2초를 기다리지만, 실제로 사전 로드가 실패하거나 2초 내에 완료되지 않으면 변환을 **조용히 포기**하고 스페이스 키를 전송합니다. 사전 로드 중임을 사용자에게 알리는 UI 피드백이 없습니다.

**제안:** 사전 로드 중인 경우 오버레이에 "사전 로드 중..." 메시지 표시.

---

### 3. `MozcDictionary.LoadDictionary()`의 경쟁 조건 (Race Condition)
**파일:** [`MozcGoogleDictionary.cs`](file:///d:/VSCODE/IMEJapanese/MozcGoogleDictionary.cs#L121-L166)

```csharp
public static void LoadDictionary()
{
    if (IsLoaded) return;  // ← 잠금 없음!
    ...
    IsLoaded = true;
```

`IsLoaded` 체크와 실제 로드 사이에 **락(lock)이 없어**, 스페이스 키 처리 시(`HandleKanjiConversion`)에서 `Task.Run`으로 중복 호출될 경우 **동시에 두 번 실행**될 수 있습니다. `SqliteConnection`이 두 번 생성되거나, `_transitionMatrix`가 이중 초기화될 수 있습니다.

**제안:** 진입부에 `lock`이나 `Lazy<T>` 패턴, 또는 `SemaphoreSlim` 적용.

---

### 4. `hangulStateCache`가 스레드 안전하지 않음
**파일:** [`ImeNativeCore.cs`](file:///d:/VSCODE/IMEJapanese/ImeNativeCore.cs#L26-L132)

```csharp
private static readonly Dictionary<IntPtr, bool> _hangulStateCache = new Dictionary<IntPtr, bool>();
```

`_hangulStateCache`가 일반 `Dictionary`로 선언되어 있는데, `CheckHangulPublic`은 키보드 훅 콜백(비UI 스레드)과 UI 스레드 양쪽에서 동시에 호출됩니다. `Dictionary`는 스레드 안전하지 않으므로 **동시 읽기/쓰기 시 크래시** 위험이 있습니다.

**제안:** `ConcurrentDictionary<IntPtr, bool>`으로 교체.

---

## 🟠 안정성 / 스레드 안전성

### 5. `GlobalInputHook.IsSending` / `IsReplacingSelection` 가시성 문제
**파일:** [`ImeNativeCore.cs`](file:///d:/VSCODE/IMEJapanese/ImeNativeCore.cs#L218-L219)

```csharp
public static volatile bool IsSending = false;
public static volatile bool IsReplacingSelection = false;
```

`volatile`로 선언되어 있으나, 두 변수는 **두 필드를 조건 검사 후 함께 변경**하는 패턴으로 사용됩니다. `volatile`은 단일 필드의 원자성을 보장하지 않으므로, 복합 조건 로직에서 TOCTOU(Time-Of-Check-Time-Of-Use) 문제가 발생할 수 있습니다.

**제안:** `Interlocked` 혹은 `lock` 블록을 사용해 두 상태를 원자적으로 관리.

---

### 6. UI 스레드에서 호출되는 `Task.Delay().ContinueWith()` 패턴의 일관성 부재
**파일:** [`Program.cs`](file:///d:/VSCODE/IMEJapanese/Program.cs#L538)

```csharp
if (m.Msg == WindowPosChangedMessage) Task.Delay(200).ContinueWith(_ => this.BeginInvoke(...));
```

`ContinueWith`는 기본적으로 `TaskScheduler.Default`(스레드 풀)에서 실행됩니다. `BeginInvoke`로 다시 마샬링하긴 하지만, `OnDisplaySettingsChanged`, `OnUserPreferenceChanged`에서도 같은 패턴이 반복되어 코드 일관성이 떨어집니다. 

**제안:** `Task.Delay().ContinueWith()`를 `async/await`로 통일:
```csharp
async void RebuildAfterDelay(int ms) { await Task.Delay(ms); RebuildAssetsWithRetry(0); }
```

---

### 7. 트레이 메뉴의 `async void` 이벤트 핸들러에서 예외 처리 미흡
**파일:** [`Program.cs`](file:///d:/VSCODE/IMEJapanese/Program.cs#L377)

```csharp
_menuItemUseMozc = new ToolStripMenuItem("Mozc 오프라인 한자변환", null, async (s, e) =>
{
    ...
    using (var client = new System.Net.Http.HttpClient()) { ... }
```

`async void` 핸들러 내에서 `HttpClient`를 매번 `new`로 생성합니다. `HttpClient`는 재사용해야 하는 객체이며(소켓 고갈 위험), 또한 `async void`에서 발생하는 예외는 UI 스레드 예외 핸들러로 전파되어 앱이 크래시될 수 있습니다.

**제안:** `HttpClient`를 `static readonly`로 클래스 레벨에 선언, 혹은 `HttpClientFactory` 적용.

---

## 🟡 성능 개선

### 8. `KanjiConverter`의 DP 알고리즘에서 `ToList()` 과도한 사용
**파일:** [`KanjiConversion.cs`](file:///d:/VSCODE/IMEJapanese/KanjiConversion.cs#L134)

```csharp
foreach (var kvp in dp[i].ToList()) {
```

DP 내부 루프마다 `ToList()`로 복사를 생성합니다. 루프 도중 컬렉션 수정이 없다면 불필요한 GC 압력이 발생합니다. 키 집합만 순회한다면 `Keys.ToArray()`로 충분하거나, 루프 구조를 재설계하는 편이 낫습니다.

---

### 9. `TextOverlayForm.RenderOverlayText`에서 매 페인트마다 `Font` 생성
**파일:** [`UIComponents.cs`](file:///d:/VSCODE/IMEJapanese/UIComponents.cs#L178)

```csharp
private void RenderOverlayText(object? sender, PaintEventArgs e)
{
    using Font f = new Font("Malgun Gothic", _displayFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
```

`Paint` 이벤트가 발생할 때마다 `Font` 객체를 새로 생성합니다. 오버레이 표시 중 리페인트가 빈번하면 GC 부담이 늘어납니다.

**제안:** 폰트 크기가 바뀔 때만 캐시된 `Font`를 재생성하도록 개선.

---

### 10. `_hangulStateCache`의 캐시 무효화 전략이 조잡함
**파일:** [`ImeNativeCore.cs`](file:///d:/VSCODE/IMEJapanese/ImeNativeCore.cs#L90-L93)

```csharp
if (_hangulStateCache.Count > MaxCacheSize)
{
    _hangulStateCache.Clear();  // 전체 지우기
}
```

`MaxCacheSize`(100개) 초과 시 전체를 지웁니다. LRU(Least Recently Used) 방식이 아닌 이 방식은 자주 사용하는 창 핸들 캐시가 날아가 불필요한 IME 쿼리가 재발생합니다.

**제안:** 간단한 LRU 캐시 또는 `ConcurrentDictionary` + `Queue` 조합으로 교체.

---

### 11. `_stateCheckTimer` 폴링 중 매 틱마다 `ImeState.CheckHangulPublic()` 호출
**파일:** [`Program.cs`](file:///d:/VSCODE/IMEJapanese/Program.cs#L661)

```csharp
bool cachedIsHangulMode = (isTaskbar || isTrayOrApp || isLayoutForm) ? _lastHangulSyncState : ImeState.CheckHangulPublic(contextHwnd);
```

100ms마다 한글 IME 상태를 Win32 메시지(`SendMessageTimeout`)로 조회합니다. 포커스 변경이 없을 때도 항상 조회하며, 이는 매초 10회 Win32 IPC 호출로 이어집니다.

**제안:** 포커스 창이 바뀌지 않았을 때는 이전 캐시 값을 더 적극적으로 재사용.

---

## 🔵 코드 품질 / 유지보수성

### 12. 하드코딩된 매직 넘버 다수 존재
**파일:** [`Program.cs`](file:///d:/VSCODE/IMEJapanese/Program.cs), [`ImeNativeCore.cs`](file:///d:/VSCODE/IMEJapanese/ImeNativeCore.cs)

```csharp
cp.ExStyle |= 0x00000080 | 0x00000020 | 0x08000000 | 0x00000008;  // 의미 불명
height < 5 → height = 24;  // 매직 넘버
Thread.Sleep(50);           // 이유 불명
```

Win32 ExStyle 상수들이 숫자로만 기록되어 있어 의도 파악이 어렵습니다. `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE` 등 명명된 상수로 교체를 권장합니다.

---

### 13. `MozcGoogleDictionary.cs` 파일의 역할 과부하
**파일:** [`MozcGoogleDictionary.cs`](file:///d:/VSCODE/IMEJapanese/MozcGoogleDictionary.cs)

한 파일에 `MozcDictionary`, `GoogleJapaneseInputApi` 두 클래스가 공존하며, 파일 상단 주석을 보면 원래 별도 파일이었던 것(`// KanjiCandidateOverlay.cs`, `// GoogleJapaneseInputApi.cs`)이 합쳐진 흔적이 있습니다. 역할이 섞여 있어 유지보수 시 혼란을 줍니다.

**제안:** `GoogleJapaneseInputApi` 클래스를 별도 파일로 분리.

---

### 14. 설정값이 런타임에 변경되어도 영구 저장되지 않음
**파일:** [`Config.cs`](file:///d:/VSCODE/IMEJapanese/Config.cs)

`AppConfig.UseGoogleApi`, `AppConfig.EnableCopilotMap` 등이 트레이 메뉴에서 변경 가능하지만, 앱 재시작 시 초기값으로 리셋됩니다. 사용자가 선택한 설정이 유지되지 않는 것은 UX 저하 요인입니다.

**제안:** `Microsoft.Win32.Registry` 또는 `System.Configuration`을 이용해 사용자 설정을 영구 저장.

---

### 15. `implementation_plan*.md` 파일 4개가 프로젝트 루트에 방치
**위치:** 프로젝트 루트 폴더

```
implementation_plan.md
implementation_plan2.md
implementation_plan2_walkthrough.md
implementation_plan3.md
implementation_plan4_입력문자표시창위치.md
```

작업 중 생성된 계획 파일들이 정리되지 않았습니다. `.gitignore`에 추가하거나 `docs/` 폴더로 이동을 권장합니다.

---

### 16. Google API URL이 HTTP(비보안)
**파일:** [`MozcGoogleDictionary.cs`](file:///d:/VSCODE/IMEJapanese/MozcGoogleDictionary.cs#L528)

```csharp
string url = $"http://www.google.com/transliterate?langpair=ja-Hira|ja&text={encodedText}";
```

`http://`를 사용하고 있어 중간자 공격(MITM)에 취약합니다. `https://`로 변경해야 합니다.

---

## ✅ 개선 우선순위 요약

| 우선순위 | 항목 | 예상 작업 규모 |
|---|---|---|
| 🔴 즉시 | `_hangulStateCache` → `ConcurrentDictionary` 교체 (#4) | 소 |
| 🔴 즉시 | `LoadDictionary` 이중 실행 방지 lock 추가 (#3) | 소 |
| 🔴 즉시 | Google API URL `http` → `https` 변경 (#16) | 소 |
| 🟠 단기 | `SendReplacement` 타이밍 개선 (#1) | 소 |
| 🟠 단기 | 사전 로드 중 UI 피드백 개선 (#2) | 중 |
| 🟡 중기 | 사용자 설정 영구 저장 기능 추가 (#14) | 중 |
| 🟡 중기 | `TextOverlayForm` Font 캐싱 개선 (#9) | 소 |
| 🔵 장기 | `GoogleJapaneseInputApi` 파일 분리 (#13) | 소 |
| 🔵 장기 | 계획 파일 정리 (#15) | 소 |
