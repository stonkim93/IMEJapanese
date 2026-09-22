# IMEJapanese 성능 최적화 및 구조 개편 구현 계획

이 앱은 전체적으로 잘 작성되어 있으며 특히 `CharacterDatabase`의 O(1) 배열 구조나 `MozcDictionary`의 `ConcurrentDictionary` 캐싱, Win32 API 최적화 등 성능 면에서 훌륭한 최적화가 진행되어 있습니다. 
Github 배포 및 MS Store 등록을 앞두고 유지보수성과 코드 가독성을 극대화하기 위해 **"파일 분리"** 및 **"설정 통합"**, **"중복 코드 정리"**에 초점을 맞춘 리팩토링을 제안합니다.

## User Review Required

> [!IMPORTANT]
> 아래의 제안은 기존 로직을 수정하지 않고, **파일을 논리적으로 분리하고 설정을 통합**하여 향후 유지보수를 쉽게 만드는 작업입니다. 
> 제안된 구조 개편 방향에 동의하시는지 확인을 부탁드립니다. 승인해 주시면 2~3개의 파일 단위로 순차적으로 작업을 진행하겠습니다.

## Proposed Changes

### 1. 설정 및 리소스 통합 관리 (Config.cs)
현재 `Program.cs`에 있는 `AppConfig`, `UiText`와 `MozcDictionary.cs`에 있는 `MozcConfig` 등 설정값들을 하나의 파일로 통합합니다.

#### [NEW] [Config.cs](file:///d:/VSCODE/IMEJapanese/Config.cs)
- `AppConfig` 클래스 이동
- `UiText` 클래스 이동
- `MozcConfig` 클래스 이동 (이름을 통합 관리 구조에 맞게 유지)

### 2. UI 및 폼 컴포넌트 분리 (UIComponents.cs)
여러 파일에 흩어져 있는 폼 클래스들을 하나로 모으거나 분리하여 `Program.cs`와 `MozcDictionary.cs`의 복잡도를 낮춥니다.

#### [NEW] [UIComponents.cs](file:///d:/VSCODE/IMEJapanese/UIComponents.cs)
- `KeyboardLayoutForm` (현재 `Program.cs`)
- `TextOverlayForm` (현재 `Program.cs`)
- `KanjiCandidateOverlay` (현재 `MozcDictionary.cs`)

### 3. 형태소 분석 및 한자 변환 로직 분리 (KanjiConversion.cs)
`JapaneseCharacter.cs`에 포함되어 있어 파일이 비대해진 변환 로직을 분리합니다.

#### [NEW] [KanjiConversion.cs](file:///d:/VSCODE/IMEJapanese/KanjiConversion.cs)
- `JapaneseMorphologyAnalyzer` 이동
- `KanjiConverter` 이동

### 4. 레지스트리 관리 클래스 분리 (RegistryManager.cs)
#### [NEW] [RegistryManager.cs](file:///d:/VSCODE/IMEJapanese/RegistryManager.cs)
- `RegistryManager` 클래스 분리 (현재 `Program.cs`)

### 5. 기존 파일 다이어트 (Modify)
#### [MODIFY] [Program.cs](file:///d:/VSCODE/IMEJapanese/Program.cs)
- `AppConfig`, `UiText`, `KeyboardLayoutForm`, `TextOverlayForm`, `RegistryManager` 제거 (위의 새 파일로 이동).
- `MainForm` 및 `Program` 클래스만 유지하여 앱의 진입점으로서의 역할에 집중.

#### [MODIFY] [JapaneseCharacter.cs](file:///d:/VSCODE/IMEJapanese/JapaneseCharacter.cs)
- `JapaneseMorphologyAnalyzer`, `KanjiConverter` 제거.
- 일본어 문자 체계 구조체 및 DB 로직만 남겨 단일 책임 원칙(SRP) 준수.

#### [MODIFY] [MozcDictionary.cs](file:///d:/VSCODE/IMEJapanese/MozcDictionary.cs)
- `MozcConfig`, `KanjiCandidateOverlay` 제거.
- SQLite DB 접근 및 딕셔너리 관리 로직만 유지.

#### [MODIFY] [Lang.cs](file:///d:/VSCODE/IMEJapanese/Lang.cs)
- 중복된 주석 코드 블록(사용하지 않는 Dictionary 매핑 등) 삭제.
- 기타 미세한 가독성 정리.

## Verification Plan
1. 코드를 2~3개씩 분리하여 작성한 후, 컴파일 에러(Build Error)가 발생하는지 점검합니다.
2. 기존 기능(한자 변환, 오버레이 UI, 일본어 입력)이 정상 작동하는지 논리적 연결을 확인합니다.
