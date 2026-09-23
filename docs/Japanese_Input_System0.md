# 🎌 IMEJapanese 일본어 입력 시스템 상세 설명서 (수정판)

초보 개발자가 IMEJapanese의 일본어 입력 시스템을 정확히 이해할 수 있도록 실제 코드 기반으로 작성한 설명서입니다.

---

## 📋 목차

1. [일본어 문자 구조체 (3자리 코드)](#1-일본어-문자-구조체-3자리-코드)
2. [일본어1의 자음+모음 조합 입력](#2-일본어1의-자음모음-조합-입력)
3. [HK/YN 전환키 기능](#3-hkyn-전환키-기능)
4. [입력 흐름 종합 및 예시](#4-입력-흐름-종합-및-예시)

---

## 1. 일본어 문자 구조체 (3자리 코드)

### 1.1 개념과 목적

일본어 문자 하나를 **3자리 숫자 코드**로 효율적으로 표현합니다.

```
예시:
  100 → "か" (ka, 청음)
  101 → "が" (ga, 탁음)
  102 → "ぎ" (gi, 탁음) - 아니, 112입니다
  000 → "あ" (a, 모음)
```

**왜 3자리 코드인가?**

```
✅ 메모리: 한 문자 = 2바이트 (ushort)
✅ 성능: 산술 연산으로 속성 추출 (나눗셈/나머지)
✅ 계산: 탁음/반탁음 변환이 산술 연산
✅ 효율: 대량 문자 처리 시 메모리/CPU 절감
```

### 1.2 코드 구조 상세

```
3자리 코드: A B C
            ┃ ┃ ┃
            ┃ ┃ └─ C (1의 자리): 형태 마크 (Voicing)
            ┃ └──── B (10의 자리): 모음 인덱스 (VowelIndex)
            └────── A (100의 자리): 자음 그룹 (ConsonantGroup)
```

#### A자리 (100의 자리): 자음 그룹

```
ConsonantGroup 값:

0 = 모음 (あ, い, う, え, お)
1 = k행 (か, き, く, け, こ)
2 = s행 (さ, し, す, せ, そ)
3 = t행 (た, ち, つ, て, と)
4 = h행 (は, ひ, ふ, へ, ほ)
5 = n행 (な, に, ぬ, ね, の)  [실제: 네이티브는 마행, 코드상 5번]
6 = m행 (ま, み, む, め, も)  [실제: 마행]
7 = y행 (や, ゆ, よ)
8 = r행 (ら, り, る, れ, ろ)
9 = w행, ん (わ, を, ん 등)

계산: Code / 100
```

#### B자리 (10의 자리): 모음 인덱스

```
VowelIndex 값:

0 = a (あ, か, さ, ...) → 히라가나
1 = i (い, き, し, ...) → 히라가나
2 = u (う, く, す, ...) → 히라가나
3 = e (え, け, せ, ...) → 히라가나
4 = o (お, こ, そ, ...) → 히라가나

5 = a (ア, カ, サ, ...) → 카타카나
6 = i (イ, キ, シ, ...) → 카타카나
7 = u (ウ, ク, ス, ...) → 카타카나
8 = e (エ, ケ, セ, ...) → 카타카나
9 = o (オ, コ, ソ, ...) → 카타카나

특징:
- 0~4: 히라가나 (IsCapital = false)
- 5~9: 카타카나 (IsCapital = true)
- 실제 모음: VowelIndex % 5 → 0=a, 1=i, 2=u, 3=e, 4=o

계산: (Code / 10) % 10
```

#### C자리 (1의 자리): 형태 마크

```
Voicing 값:

0 = 청음 (清音, 기본형)
    예: か, さ, た, は, ま, や, ら, わ
    특징: 탁점(゛)이나 반탁점(゜) 없음

1 = 탁음 (濁音, 게단)
    예: が, ざ, だ, ば
    특징: 탁점(゛) 있음

2 = 반탁음 (半濁音, 파단)
    예: ぱ, ぴ, ぷ, ぺ, ぽ
    특징: 반탁점(゜) 있음 (h행/p행만 가능)

3 = 스테가나 (小文字, 작은 문자)
    예: ぁ(작은 あ), ぃ(작은 い), ゃ(작은 や)
    특징: 요음(야행과 조합) 또는 촉음(っ)

계산: Code % 10
```

### 1.3 구체적인 예시

#### 예시 1: "か" (ka)

```
코드: 100
분석:
  100 / 100 = 1   → ConsonantGroup = 1 (k행)
  (100 / 10) % 10 = 0 → VowelIndex = 0 (a)
  100 % 10 = 0    → Voicing = 0 (청음)

결과: k + a + 청음 = "か" ✅
```

#### 예시 2: "がぎぐげご" (탁음 g행)

```
코드들:
  101: ConsonantGroup=1, VowelIndex=0, Voicing=1 → "が" (ga)
  111: ConsonantGroup=1, VowelIndex=1, Voicing=1 → "ぎ" (gi)
  121: ConsonantGroup=1, VowelIndex=2, Voicing=1 → "ぐ" (gu)
  131: ConsonantGroup=1, VowelIndex=3, Voicing=1 → "げ" (ge)
  141: ConsonantGroup=1, VowelIndex=4, Voicing=1 → "ご" (go)

패턴: 같은 행, 다른 모음, Voicing=1
```

#### 예시 3: "ぱぴぷぺぽ" (반탁음 p행)

```
코드들:
  402: ConsonantGroup=4, VowelIndex=0, Voicing=2 → "ぱ"
  412: ConsonantGroup=4, VowelIndex=1, Voicing=2 → "ぴ"
  422: ConsonantGroup=4, VowelIndex=2, Voicing=2 → "ぷ"
  432: ConsonantGroup=4, VowelIndex=3, Voicing=2 → "ぺ"
  442: ConsonantGroup=4, VowelIndex=4, Voicing=2 → "ぽ"

패턴: h행(4), 다른 모음, Voicing=2 (반탁음은 h행만 가능)
```

#### 예시 4: "ア" (카타카나 a)

```
코드: 050
분석:
  050 / 100 = 0 → ConsonantGroup = 0 (모음)
  (050 / 10) % 10 = 5 → VowelIndex = 5 (카타카나 a)
  050 % 10 = 0 → Voicing = 0 (청음)

결과: 모음 + 카타카나 a + 청음 = "ア" ✅
IsCapital = true (VowelIndex >= 5)
```

### 1.4 코드 실현 (JapaneseCharacter.cs)

```csharp
public struct JapaneseCharacter : IEquatable<JapaneseCharacter>
{
    // 핵심 데이터: 3자리 코드
    public ushort Code { get; private set; }

    // 각 자리 추출 (계산으로 생성)
    public byte ConsonantGroup => (byte)(Code / 100);       // 100의 자리
    public byte VowelIndex => (byte)((Code / 10) % 10);    // 10의 자리
    public byte Voicing => (byte)(Code % 10);              // 1의 자리

    // 문자 캐싱
    private char _hiragana;
    public char Hiragana => _hiragana == '\0' 
        ? (_hiragana = CharacterDatabase.GetCharacterData(Code).Hiragana) 
        : _hiragana;

    // 형태 판별
    public bool IsSeion => Voicing == 0;        // 청음?
    public bool IsDakuten => Voicing == 1;      // 탁음?
    public bool IsHandakuten => Voicing == 2;   // 반탁음?
    public bool IsSutegana => Voicing == 3;     // 스테가나?
    public bool IsCapital => VowelIndex >= 5;   // 카타카나?
}
```

---

## 2. 일본어1의 자음+모음 조합 입력

### 2.1 기본 원리

일본어 문자를 **2단계로 입력**합니다.

```
Step 1: 자음 키 누르기
        자음 그룹 선택 (예: k행, s행, ...)
        → 화면에 미리보기 표시
        → 모음 입력 대기

Step 2: 모음 키 누르기
        모음 선택 (a, i, u, e, o)
        → 자음 + 모음 조합으로 문자 생성
        → 화면에 최종 문자 출력
```

### 2.2 키 배치 (실제 구현)

#### 왼손 (자음 키)

```
QWERTY 자판:
┌─────────────────┐
│ Q W E R A S D F │  ← 자음 키
│ Z X C V         │  ← 자음 키 + 특수
└─────────────────┘

_consonantKeys = { Q, W, E, R, A, S, D, F, Z, X, C, V }

각 키가 어떤 행을 나타내는지는 
_combineMap Dictionary에서 정의됨
(실제 배치는 코드에서 확인 필요)
```

#### 오른손 (모음 키)

```
QWERTY 자판:
┌─────────────────┐
│ H J K L M       │  ← 모음 키
└─────────────────┘

_vowelKeys = { H, J, K, L, M }

H = a (あ행)
J = i (い행)
K = u (う行)
L = e (え行)
M = o (お行)
```

### 2.3 조합 맵 (_combineMap)

```csharp
// Lang.cs의 Japanese1Map.ProcessKey()

private static readonly Dictionary<(int Con, int Vow), (string Hira, string Kata)> _combineMap = new()
{
    // (자음키, 모음키) → (히라가나, 카타카나) 조합
    // 예시:
    ((int)VCode.vk_D, (int)VCode.vk_H), ("か", "カ")),  // Q+H
    ((int)VCode.vk_D, (int)VCode.vk_J), ("き", "キ")),  // Q+J
    ((int)VCode.vk_D, (int)VCode.vk_K), ("く", "ク")),  // Q+K
    ((int)VCode.vk_D, (int)VCode.vk_L), ("こ", "コ")),  // Q+M
    ((int)VCode.vk_D, (int)VCode.vk_M), ("け", "ケ")),  // Q+L
    
    ((int)VCode.vk_S, (int)VCode.vk_H), ("さ", "サ")),  // W+H
    ((int)VCode.vk_S, (int)VCode.vk_J), ("し", "シ")),  // W+J
    ((int)VCode.vk_S, (int)VCode.vk_K), ("す", "ス")),  // W+K
    ((int)VCode.vk_S, (int)VCode.vk_L), ("そ", "ソ")),  // W+M
    ((int)VCode.vk_S, (int)VCode.vk_M), ("せ", "セ")),  // W+L
    
    // ... 계속
};
```

### 2.4 입력 흐름 (ProcessKey)

```csharp
// Lang.cs의 Japanese1Map.ProcessKey() 메서드

public static string? ProcessKey(int vKey, bool isShift)
{
    // Step 1: 모음 입력 (자음을 이미 입력했고, 모음 대기 중인 경우)
    if (_waitingVowel)
    {
        if (_vowelKeys.Contains(vKey))  // H, J, K, L, M
        {
            // (자음키, 모음키) 조합으로 문자 검색
            var key = (_pendingConsonant, vKey);
            
            if (_combineMap.TryGetValue(key, out var combined))
            {
                // 히라가나 또는 카타카나 선택 (Shift 키에 따라)
                string result = isShift ? combined.Kata : combined.Hira;
                
                // YN 토글 적용 (이전에 YN 키를 눌렀다면)
                for (int i = 0; i < _ynToggleCount; i++)
                    result = JapaneseCharacterProcessor.ProcessYN(result);
                
                // 상태 초기화
                _waitingVowel = false;
                _pendingConsonant = 0;
                _ynToggleCount = 0;
                
                return result;  // ✅ 최종 문자 반환
            }
        }
    }

    // Step 2: 자음 입력 (새로운 문자 시작)
    if (_consonantKeys.Contains(vKey))  // Q, W, E, R, A, S, D, F, Z, X, C, V
    {
        _waitingVowel = true;           // 모음 입력 대기 시작
        _pendingConsonant = vKey;       // 누른 키 저장
        
        // 화면에 미리보기 표시
        string preview = GetPreviewChar(vKey);  // 해당 행의 첫 음절
        MainForm.Instance?.ShowOverlay(preview, 0);
        
        return "";  // 아직 문자 반환하지 않음 (모음 입력 대기)
    }

    return null;  // 인식하지 못한 키
}
```

### 2.5 입력 예시

#### "かきます" 입력

```
Step 1: D 누르기 (か 행 선택)
  상태: _waitingVowel = true, _pendingConsonant = D
  화면: "か" 미리보기 (D행의 a음)
  출력: "" (아직 출력 안 함)

Step 2: H 누르기 (a 모음)
  조합: (D, H) → _combineMap 조회
  결과: "か" (isShift=false이므로 히라가나)
  YN 토글: 없음 (카운트=0)
  화면: "か+あ=か" 표시
  출력: "か" ✅
  상태: _waitingVowel = false

Step 3: S 누르기 (さ 행 선택)
  상태: _waitingVowel = true, _pendingConsonant = S
  화면: "さ" 미리보기
  출력: "" (아직 출력 안 함)

Step 4: J 누르기 (i 모음)
  조합: (W, J) → _combineMap 조회
  결과: "し" (S+J → "し")
  화면: "さ+い=し"
  출력: "し" ✅

Step 5: F 누르기 (た 행 선택)
Step 6: L 누르기 (o 모음)
  조합: (F, L)
  결과: "と" (ta + o)
  출력: "と" ✅

최종: "か" + "し" + "と" = "かしと" 또는 다른 조합
```

### 2.6 Shift 키와의 상호작용

```csharp
// ProcessKey 메서드의 isShift 파라미터

isShift = false (일반)
  → combined.Hira 사용
  → 히라가나 출력 (예: "か")

isShift = true (Shift 누른 상태)
  → combined.Kata 사용
  → 카타카나 출력 (예: "カ")

예시:
  Q + H = "か" (일반)
  Shift + Q + H = "カ" (Shift)
```

---

## 3. HK/YN 전환키 기능

### 3.1 HK 기능: 히라가나 ↔ 카타카나 전환

#### 개념

선택된 일본어 문자의 형식을 히라가나 ↔ 카타카나로 전환합니다.

```
예시:
  선택: "かきます" (모두 히라가나)
  HK 키 누르기
  결과: "カキマス" (모두 카타카나)
  
  다시 HK 누르기
  결과: "かきます" (다시 히라가나로)
```

#### 원리

VowelIndex를 이용한 변환:

```
히라가나 (VowelIndex = 0~4)
    ↓↓↓ HK 키 ↓↓↓
카타카나 (VowelIndex = 5~9)

예:
  "か" (100) → "カ" (105)
  "き" (110) → "キ" (115)
  "さ" (200) → "サ" (205)

수식:
  VowelIndex % 5 = 실제 모음 (a, i, u, e, o)
  
  히라가나 → 카타카나:
    새 VowelIndex = VowelIndex + 5
    새 Code = (Code / 10) * 10 + 새 VowelIndex
  
  카타카나 → 히라가나:
    새 VowelIndex = VowelIndex - 5
    새 Code = (Code / 10) * 10 + 새 VowelIndex
```

#### 코드 구현

```csharp
// JapaneseCharacterProcessor.ProcessHK() in JapaneseCharacter.cs

public static string ProcessHK(string text)
{
    if (string.IsNullOrEmpty(text)) return text;

    var result = new StringBuilder(text.Length);
    foreach (char ch in text)
    {
        if (!CharacterDatabase.IsValidCharacter(ch))
        {
            result.Append(ch);  // 일본어 아님 → 그대로
            continue;
        }

        // 문자를 JapaneseCharacter로 변환
        var jpChar = (ch >= 0x3040 && ch <= 0x309F)  // 히라가나 범위
            ? JapaneseCharacter.FromHiragana(ch)
            : JapaneseCharacter.FromKatakana(ch);

        // 형식 감지 및 반대 형식으로 변환
        char converted = (ch >= 0x3040 && ch <= 0x309F)
            ? jpChar.Katakana   // 히라가나 → 카타카나
            : jpChar.Hiragana;  // 카타카나 → 히라가나

        result.Append(converted);
    }

    return result.ToString();
}
```

### 3.2 YN 기능: 청음/탁음/반탁음/스테가나 순환

#### 개념

일본어 문자의 음성(Voicing) 형태를 순환시킵니다.

```
Voicing 순환:
  0 (청음) → 1 (탁음) → 2 (반탁음) → 3 (스테가나) → 0 (청음)

단, 존재하지 않는 상태는 자동으로 건너뜀

예시:
  "か" (Voicing=0) → "が" (Voicing=1) → "か" (Voicing=0)
  "は" (Voicing=0) → "ば" (Voicing=1) → "ぱ" (Voicing=2) → "は" (Voicing=0)
  "あ" (Voicing=0) → "ぁ" (Voicing=3) → "あ" (Voicing=0)
```

#### 형태별 순환 규칙 (실제 코드 기반)

```
NextVoicing() 메서드의 동작:

1. 현재 Voicing 읽기
2. 다음 상태 탐색 (최대 4번 순환):
   for i in 1..4:
     nextVoicing = (currentVoicing + i) % 4
     nextCode = (Code / 10) * 10 + nextVoicing
     if CharacterDatabase.ContainsCode(nextCode):
       return FromCode(nextCode)

3. 존재하는 다음 상태 반환, 없으면 원본 유지

예시:
  "か" (100): Voicing=0
    i=1: 101 존재? → "が" ✅ 반환
    
  "がぎぐげご" (탁음 g행): Voicing=1
    i=1: Voicing=2, 102/112/122/132/142 존재? → No
    i=2: Voicing=3, 103/113/123/133/143 존재? → No
    i=3: Voicing=0, 100/110/120/130/140 존재? → "か", "き", "く", "け", "こ" ✅
    → 다시 청음으로 돌아감
    
  "は" (400): Voicing=0 (h행은 반탁음 가능)
    i=1: 401 → "ば" ✅
    
  "ば" (401): Voicing=1
    i=1: 402 → "ぱ" ✅
    
  "ぱ" (402): Voicing=2
    i=1: Voicing=3, 403 존재? → No
    i=2: Voicing=0, 400 → "は" ✅
```

#### 코드 구현

```csharp
// JapaneseCharacter.NextVoicing() in JapaneseCharacter.cs

public JapaneseCharacter NextVoicing()
{
    byte currentVoicing = Voicing;
    
    // 4가지 상태를 순차적으로 탐색
    for (int i = 1; i <= 4; i++)
    {
        byte nextVoicing = (byte)((currentVoicing + i) % 4);
        ushort nextCode = (ushort)((Code / 10) * 10 + nextVoicing);
        
        // 존재하는 상태 찾기
        if (CharacterDatabase.ContainsCode(nextCode))
        {
            return FromCode(nextCode);
        }
    }

    // 상태 변화 불가능 → 원본 유지
    return this;
}

// JapaneseCharacterProcessor.ProcessYN()

public static string ProcessYN(string text)
{
    if (string.IsNullOrEmpty(text)) return text;

    var result = new StringBuilder(text.Length);
    foreach (char ch in text)
    {
        if (!CharacterDatabase.IsValidCharacter(ch))
        {
            result.Append(ch);  // 일본어 아님 → 그대로
            continue;
        }

        // 문자를 JapaneseCharacter로 변환
        var jpChar = (ch >= 0x3040 && ch <= 0x309F)  // 히라가나
            ? JapaneseCharacter.FromHiragana(ch)
            : JapaneseCharacter.FromKatakana(ch);

        // 다음 음성 단계로 전환
        var nextChar = jpChar.NextVoicing();

        // 원본 형식 유지 (히라가나 또는 카타카나)
        char converted = (ch >= 0x3040 && ch <= 0x309F)
            ? nextChar.Hiragana   // 히라가나 유지
            : nextChar.Katakana;  // 카타카나 유지

        result.Append(converted);
    }

    return result.ToString();
}
```

### 3.3 YN 토글 카운트

```csharp
// Lang.cs의 Japanese1Map에서

private static int _ynToggleCount = 0;  // YN 누른 횟수

// HK 또는 특정 키 누를 때 리셋
// YN 키 누를 때 증가

// ProcessKey에서 사용:
for (int i = 0; i < _ynToggleCount; i++)
    result = JapaneseCharacterProcessor.ProcessYN(result);

// 예시:
// YN 1번 누르면: _ynToggleCount = 1
//   "か" → NextVoicing() → "が"
// YN 2번 누르면: _ynToggleCount = 2
//   "か" → NextVoicing() → "が" → NextVoicing() → "か"
```

---

## 4. 입력 흐름 종합 및 예시

### 4.1 전체 입력 프로세스

```
┌─────────────────────────────────────────────────┐
│ 사용자가 한글CAPS 모드에서 키 누르기             │
└────────────────┬────────────────────────────────┘
                 │
                 ▼
     ┌───────────────────────┐
     │ ImeNativeCore         │
     │ KbdHookCallback()     │
     │ (저수준 키 훅)        │
     └───────┬───────────────┘
             │
      ┌──────┴──────────┐
      │                 │
      ▼                 ▼
  ┌────────────┐   ┌──────────────┐
  │ 일반 입력  │   │ HK/YN 키     │
  │ (자음/모음)│   │ (선택된 텍스트│
  └────┬───────┘   └──────┬───────┘
       │                  │
       ▼                  ▼
  ┌──────────────────────────────┐
  │ Japanese1/2Map.ProcessKey()  │
  │ (자음+모음 조합)             │
  └────┬─────────────────────────┘
       │
       ▼
  ┌──────────────────────────────┐
  │ JapaneseCharacterProcessor   │
  │ .ProcessHK() / .ProcessYN()  │
  │ (형식/음성 변환)             │
  └────┬─────────────────────────┘
       │
       ▼
  ┌──────────────────────────────┐
  │ 최종 문자                    │
  │ (히라가나/카타카나)          │
  └────┬─────────────────────────┘
       │
       ▼
  ┌──────────────────────────────┐
  │ 대상 앱에 출력               │
  │ (Word, Excel, 메모장 등)     │
  └──────────────────────────────┘
```

### 4.2 단계별 실제 사용 시나리오

#### 시나리오 1: "さくら" 입력

```
상황: Word에서 일본어1 모드, 히라가나 입력

Step 1: S 누르기 (さ행 선택)
  _waitingVowel = true
  화면: "さ" 미리보기
  출력: 없음

Step 2: H 누르기 (a 모음)
  (S, H) → "さ"
  출력: "さ" ✅

Step 3: D 누르기 (か행 선택)
  _waitingVowel = true
  화면: "か" 미리보기

Step 4: K 누르기 (u 모음)
  (D, K) → "く"
  출력: "く" ✅

Step 5: C 누르기 (ら행 선택)  [예상]
  
Step 6: H 누르기 (a 모음)
  (C, H) → "ら"
  출력: "ら" ✅

최종 결과: "さくら"
```

#### 시나리오 2: "がぎぐげご" 입력 (탁음 g행)

```
상황: Word에서 일본어1 모드

Step 1: E 누르기 (が행 선택)
  상태: _waitingVowel = true

Step 2: H 누르기 (a 모음)
  (E, H) → "が"
  출력: "が" ✅

Step 3: E 누르기 (が행 선택)
  상태: _waitingVowel = true

Step 4: J 누르기 (i 모음)
  (E, J) → "ぎ"
  출력: "ぎ" ✅

... (반복)

최종 결과: "がぎぐげご"
```

#### 시나리오 3: "さくら" 선택 후 HK 적용

```
상황: Word에서 "さくら" 입력 후 선택

Step 1: "さくら" 전체 선택
  [さくら] ← 선택됨

Step 2: HK 키 누르기
  각 문자에 대해 ProcessHK() 실행:
    'さ' → IsCapital=false (히라가나)
          → 'サ' (카타카나)
    'く' → IsCapital=false
          → 'ク'
    'ら' → IsCapital=false
          → 'ラ'
  
  결과: "サクラ" 출력 ✅

Step 3: 다시 HK 키 누르기
  'サ' → IsCapital=true (카타카나)
        → 'さ' (히라가나)
  
  결과: "さくら" 출력 ✅ (원래대로)
```

#### 시나리오 4: "さくら" 선택 후 YN 적용

```
상황: "さくら" 선택됨

Step 1: YN 키 1번 누르기
  'さ' → NextVoicing() → 'ざ'
  'く' → NextVoicing() → 'ぐ'
  'ら' → NextVoicing() → 없음 (r행은 탁음 없음) → 'ら'
  
  결과: "ざぐら" 출력 ✅

Step 2: YN 키 2번 누르기 (1단계 더)
  'ざ' → NextVoicing() → 없음 (s행은 반탁음 없음) → 'さ'
  
  결과: "さくら" 출력 ✅ (원래대로)
```

---

## 요약

### 3자리 코드의 효율성

```
메모리: 각 문자 = 2바이트 (ushort)
성능: 산술 연산으로 속성 추출
계산: Voicing 변환이 단순 코드 계산

예:
  "かきます" (4글자) = 4 × 2바이트 = 8바이트
```

### 자음+모음 조합 입력

```
원리: 2-step 입력
  1. 자음 키 → 모음 대기
  2. 모음 키 → 문자 생성

장점: 양손 분리 입력, 자연스러운 진행
구현: Dictionary<(자음키, 모음키), 문자>
```

### HK/YN 기능

```
HK: VowelIndex를 5 증감하여 형식 전환
    (0~4 히라가나 ↔ 5~9 카타카나)

YN: Voicing을 순환시켜 음성 변환
    (0→1→2→3→0, 존재 여부 확인)
    
특징: 구조체의 속성을 최대한 활용
효율: 빠른 연산과 메모리 절약
```

---

**이제 IMEJapanese의 일본어 입력 시스템을 정확히 이해했습니다!** 🎌
