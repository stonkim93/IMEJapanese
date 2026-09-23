# 🔤 IMEJapanese 한자변환 기능 상세 설명서

초보 개발자가 Mozc 오프라인 한자변환과 Google 온라인 한자변환이 어떻게 구현되는지 이해할 수 있도록 작성한 설명서입니다.

---

## 📋 목차

1. [한자변환 기능 개요](#1-한자변환-기능-개요)
2. [Mozc 오프라인 한자변환 구현](#2-mozc-오프라인-한자변환-구현)
3. [Google 온라인 한자변환 구현](#3-google-온라인-한자변환-구현)
4. [mozc_dict_connect.db 파일 생성](#4-mozc_dict_connect-db-파일-생성)
5. [두 방식의 비교](#5-두-방식의-비교)
6. [초보자 학습 가이드](#6-초보자-학습-가이드)

---

## 1. 한자변환 기능 개요

### 1.1 기능의 목적

```
사용자가 입력한 일본어 문자열(히라가나/카타카나)을 한자(漢字)로 변환하여 표시하는 기능입니다.

예시:
  입력: "かきます" (히라가나)
  ↓ (한자변환)
  출력: "書きます" 또는 "画きます" 등 (한자)
  
  사용자는 여러 후보 중에서 원하는 한자를 선택
```

### 1.2 작동 흐름 (전체 파이프라인)

```
┌──────────────────────────────────────────────────────┐
│ Step 1: 사용자가 일본어 입력                        │
│ "かきます" → IMEJapanese에서 화면에 출력            │
└──────────────────┬───────────────────────────────────┘
                   │
┌──────────────────▼───────────────────────────────────┐
│ Step 2: 사용자가 Space 키 누르기                    │
│ (또는 입력 중인 텍스트 선택 후 Space)              │
└──────────────────┬───────────────────────────────────┘
                   │
┌──────────────────▼───────────────────────────────────┐
│ Step 3: ImeNativeCore.KbdHookCallback() 호출        │
│ - Space 키(0x20) 감지                               │
│ - 한글CAPS 모드 확인                                │
│ - 선택된 텍스트 추출                                │
└──────────────────┬───────────────────────────────────┘
                   │
┌──────────────────▼───────────────────────────────────┐
│ Step 4: 한자변환 방식 선택                         │
│ ├─ Mozc 오프라인 (UseGoogleApi == false)           │
│ │  └─ MozcDictionary.GetKanjiCandidates()          │
│ └─ Google 온라인 (UseGoogleApi == true)            │
│    └─ GoogleIMEAPI.GetKanjiCandidates()            │
└──────────────────┬───────────────────────────────────┘
                   │
┌──────────────────▼───────────────────────────────────┐
│ Step 5: 한자 후보 받기                             │
│ 결과: [書, 画, 掛, ...] (여러 후보)                │
└──────────────────┬───────────────────────────────────┘
                   │
┌──────────────────▼───────────────────────────────────┐
│ Step 6: KanjiCandidateOverlay UI 표시              │
│ 사용자가 후보 중 하나 선택                          │
└──────────────────┬───────────────────────────────────┘
                   │
┌──────────────────▼───────────────────────────────────┐
│ Step 7: 선택된 한자로 교체                         │
│ "かきます" → "書きます"                             │
│ (또는 사용자가 선택한 다른 한자)                    │
└──────────────────────────────────────────────────────┘
```

---

## 2. Mozc 오프라인 한자변환 구현

### 2.1 구조 개요

```
오프라인 한자변환이란?
- 인터넷 연결 불필요
- 미리 다운로드한 'mozc_dict_connect.db' 파일 사용
- 속도: 매우 빠름 (로컬 디스크 접근)
- 품질: 표준 (Google Mozc 공식 사전 기반)
```

### 2.2 필요한 파일

#### mozc_dict_connect.db 파일

```
파일 구조:
┌─────────────────────────────────────┐
│ mozc_dict_connect.db (SQLite3)      │
├─────────────────────────────────────┤
│ 1. dictionary 테이블                │
│    - reading (히라가나 읽음)        │
│    - kanji (한자 표기)              │
│    - left_id, right_id             │
│    - cost (비용/가능도)            │
│                                     │
│ 2. matrix_metadata 테이블           │
│    - matrix_size                    │
│    - data (BLOB: 연결 비용 행렬)   │
└─────────────────────────────────────┘

파일 크기: 약 50~100MB
저장 위치: IMEJapanese.exe와 같은 폴더
```

#### 파일 생성 방식

```
Step 1: Google Mozc 소스코드 다운로드
        ↓
        src/data/dictionary/
        ├─ dictionary00.txt
        ├─ dictionary01.txt
        ├─ ...
        └─ dictionary09.txt
        
        + connection_single_column.txt
        
Step 2: Python 스크립트로 변환
        ├─ build_mozc_db.py (단어 사전 생성)
        ├─ BLOB.py (연결 행렬 삽입)
        └─ 결과: mozc_dict_connect.db
        
Step 3: 압축하여 배포
        └─ mozc_dict_connect.zip (Release에 업로드)
```

### 2.3 코드 분석: Mozc Dictionary 로드

```csharp
// MozcDictionary.cs - 파일 로드 부분

public static void LoadDictionary()
{
    // 1. 데이터베이스 파일 경로 설정
    string dbPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, 
        "mozc_dict_connect.db"
    );
    
    // 2. 파일 존재 여부 확인
    if (!File.Exists(dbPath))
    {
        Debug.WriteLine("[MozcDictionary] DB 파일을 찾을 수 없습니다");
        // 사용자에게 다운로드 제안 (Program.cs에서 처리)
        return;
    }
    
    // 3. SQLite 데이터베이스 연결
    string connectionString = $"Data Source={dbPath};Mode=ReadOnly;";
    using (var conn = new SqliteConnection(connectionString))
    {
        conn.Open();
        
        // 4. dictionary 테이블에서 데이터 로드
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT reading, kanji FROM dictionary;";
        
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                string reading = reader.GetString(0);  // 히라가나
                string kanji = reader.GetString(1);    // 한자
                
                // 메모리 캐시에 저장
                if (!_dictionary.ContainsKey(reading))
                    _dictionary[reading] = new List<string>();
                    
                _dictionary[reading].Add(kanji);
            }
        }
        
        // 5. Connection Matrix 로드 (비용 행렬)
        cmd.CommandText = "SELECT matrix_size, data FROM matrix_metadata WHERE id = 1;";
        using (var reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                _matrixSize = reader.GetInt32(0);
                var matrixBlob = reader.GetFieldValue<byte[]>(1);
                
                // BLOB 바이너리 데이터를 short[] 배열로 변환
                _transitionMatrix = new short[matrixBlob.Length / 2];
                Buffer.BlockCopy(matrixBlob, 0, _transitionMatrix, 0, matrixBlob.Length);
            }
        }
    }
    
    Debug.WriteLine($"[MozcDictionary] 로드 완료: {_dictionary.Count}개 항목");
}
```

**핵심 개념:**
- `SqliteConnection`: SQLite 데이터베이스와 연결
- `ExecuteReader()`: 쿼리 결과를 한 행씩 읽기
- `BLOB`: Binary Large Object (바이너리 데이터 저장)
- `Buffer.BlockCopy()`: 바이너리 데이터를 배열로 변환

### 2.4 코드 분석: 한자 검색

```csharp
// MozcDictionary.cs - 한자 검색 부분

public static List<KanjiEntry> GetKanjiCandidates(
    string selectedText, 
    int maxCandidates = 9)
{
    List<KanjiEntry> candidates = new();
    
    try
    {
        // 1. 메모리 캐시에서 먼저 검색
        if (_dictionary.TryGetValue(selectedText, out var kanjiList))
        {
            // 2. 각 한자를 KanjiEntry 객체로 변환
            foreach (var kanji in kanjiList)
            {
                candidates.Add(new KanjiEntry
                {
                    Kanji = kanji,
                    Meaning = kanji,  // 간단 구현
                    Frequency = 50,   // 기본값
                    Examples = Array.Empty<string>()
                });
                
                if (candidates.Count >= maxCandidates)
                    break;  // 최대 후보 수 도달
            }
        }
        else
        {
            // 3. 데이터베이스에서 직접 검색 (캐시 미스 시)
            string dbPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "mozc_dict_connect.db"
            );
            
            string connectionString = $"Data Source={dbPath};Mode=ReadOnly;";
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                
                var cmd = conn.CreateCommand();
                // 4. SQL 쿼리: reading으로 검색하고 cost로 정렬
                cmd.CommandText = 
                    @"SELECT kanji, cost 
                      FROM dictionary 
                      WHERE reading = ?1 
                      ORDER BY cost ASC 
                      LIMIT ?2;";
                
                cmd.Parameters.AddWithValue("?1", selectedText);
                cmd.Parameters.AddWithValue("?2", maxCandidates);
                
                // 5. 결과 읽기
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string kanji = reader.GetString(0);
                        int cost = reader.GetInt32(1);
                        
                        candidates.Add(new KanjiEntry
                        {
                            Kanji = kanji,
                            Meaning = kanji,
                            Frequency = Math.Max(1, 100 - (cost / 10)),
                            Examples = Array.Empty<string>()
                        });
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[MozcDictionary] DB 조회 오류: {ex.Message}");
    }
    
    return candidates;
}
```

**핵심 개념:**
- `TryGetValue()`: 딕셔너리에서 안전하게 값 검색
- `ORDER BY cost ASC`: 비용이 낮은 것(자주 사용되는 것)부터 정렬
- `LIMIT`: 반환 결과 개수 제한
- `ExecuteReader()`: 여러 행의 결과 읽기

### 2.5 Connection Matrix의 역할

```
Connection Matrix란?
- 일본어 형태소 분석에서 두 인접한 요소 간의 연결 가능도를 나타내는 2D 배열
- 크기: 보통 2672 x 2672 (Mozc 기준)
- 값: 비용(cost) 정수 (낮을수록 자연스러운 조합)

예시:
       [동사 끝] [명사 시작]
       
         5    20   100   ...
     ┌────────────────────────┐
     │  5    10    50   ...   │  [명사 시작]
명사 │  20   25    60   ...   │  
시작 │ 100  105   150   ...   │  
     │ ...   ...   ...   ...  │
     └────────────────────────┘

cost = 5 → 매우 자연스러운 조합 (자주 나타남)
cost = 100 → 어색한 조합 (드물게 나타남)
```

**사용 방식:**
```csharp
// 형태소 분석 시 cost 계산
int cost = _transitionMatrix[leftId * _matrixSize + rightId];

// cost가 낮을수록 가능도가 높음
if (cost < 50)
    Console.WriteLine("자연스러운 조합");
else
    Console.WriteLine("어색한 조합");
```

---

## 3. Google 온라인 한자변환 구현

### 3.1 구조 개요

```
온라인 한자변환이란?
- Google의 온라인 API 사용
- 인터넷 연결 필요
- 속도: 느림 (네트워크 레이턴시 50~200ms)
- 품질: 매우 높음 (Google의 AI 기반 형태소 분석)
- 추가 설정: 없음 (파일 다운로드 불필요)
```

### 3.2 Google CGI API for Japanese Input

```
API 엔드포인트:
https://www.google.com/transliterate?

쿼리 파라미터:
- text: 변환할 히라가나 문자열
- uquery: 사용자 입력 쿼리
- langpair: 언어 쌍 (일본어)

응답 포맷:
JSON 형식으로 후보 반환
```

### 3.3 코드 분석: Google API 호출

```csharp
// Program.cs에서 한자변환 호출 시

if (AppConfig.UseGoogleApi)
{
    // Google 온라인 API 사용
    candidates = await GoogleIMEAPI.GetKanjiCandidatesAsync(selectedText);
}
else
{
    // Mozc 오프라인 사용
    candidates = MozcDictionary.GetKanjiCandidates(selectedText);
}
```

### 3.4 Google API 상세 구현 (개념)

```csharp
public static class GoogleIMEAPI
{
    private static readonly HttpClient _httpClient = new();
    
    public static async Task<List<KanjiEntry>> GetKanjiCandidatesAsync(
        string hiragana,
        int maxCandidates = 9)
    {
        List<KanjiEntry> candidates = new();
        
        try
        {
            // 1. API 요청 URL 구성
            string url = "https://www.google.com/transliterate?" +
                        $"text={Uri.EscapeDataString(hiragana)}&" +
                        "uquery=&langpair=ja-Hira|ja-Kanji";
            
            // 2. GET 요청 전송
            HttpResponseMessage response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            // 3. 응답 본문 읽기
            string responseBody = await response.Content.ReadAsStringAsync();
            
            // 4. JSON 파싱
            // Google API 응답 형식:
            // [["かきます", ["書きます", "画きます", "掛きます", ...]]]
            var jsonArray = JsonNode.Parse(responseBody);
            
            // 5. 한자 후보 추출
            if (jsonArray is JsonArray outerArray && outerArray.Count > 0)
            {
                var firstItem = outerArray[0];
                if (firstItem is JsonArray innerArray && innerArray.Count > 1)
                {
                    var kanjiCandidates = innerArray[1];
                    if (kanjiCandidates is JsonArray candidatesArray)
                    {
                        int count = 0;
                        foreach (var candidate in candidatesArray)
                        {
                            if (count >= maxCandidates)
                                break;
                            
                            string? kanjiText = candidate?.GetValue<string>();
                            if (!string.IsNullOrEmpty(kanjiText))
                            {
                                candidates.Add(new KanjiEntry
                                {
                                    Kanji = kanjiText,
                                    Meaning = kanjiText,
                                    Frequency = 100 - count * 10,  // 순서대로 빈도 감소
                                    Examples = Array.Empty<string>()
                                });
                                count++;
                            }
                        }
                    }
                }
            }
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[GoogleIMEAPI] 네트워크 오류: {ex.Message}");
            // 오류 발생 시 빈 리스트 반환 → UI에 "변환 불가" 표시
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleIMEAPI] 파싱 오류: {ex.Message}");
        }
        
        return candidates;
    }
}
```

**핵심 개념:**
- `HttpClient`: HTTP 요청 전송
- `async/await`: 비동기 작업 (UI 블로킹 방지)
- `JsonNode.Parse()`: JSON 파싱
- `Uri.EscapeDataString()`: URL 인코딩

### 3.5 Google API 응답 예시

```
요청:
GET https://www.google.com/transliterate?
    text=かきます&
    langpair=ja-Hira|ja-Kanji

응답:
[
  [
    "かきます",
    [
      "書きます",
      "画きます",
      "掛きます",
      "描きます",
      "欠きます",
      "柿ます"
    ]
  ]
]

파싱 결과:
candidates = [
  {Kanji: "書きます", Frequency: 90},
  {Kanji: "画きます", Frequency: 80},
  {Kanji: "掛きます", Frequency: 70},
  ...
]
```

---

## 4. mozc_dict_connect.db 파일 생성

### 4.1 전체 프로세스

```
Step 1: Google Mozc 소스 다운로드
            ↓
Step 2: dictionary*.txt 파일 추출
            ↓
Step 3: Python 스크립트로 처리
            ├─ build_mozc_db.py (단어 사전 생성)
            └─ BLOB.py (연결 행렬 삽입)
            ↓
Step 4: mozc_dict_connect.db 생성
            ↓
Step 5: 압축 및 배포
            └─ GitHub Release에 업로드
```

### 4.2 Step 1-2: Mozc 소스 다운로드

```bash
# 1. Mozc 저장소 클론
git clone https://github.com/google/mozc.git
cd mozc/src/data/dictionary

# 2. 필요한 파일 확인
ls -la dictionary*.txt
# dictionary00.txt
# dictionary01.txt
# ...
# dictionary09.txt

ls -la connection_single_column.txt
```

### 4.3 Step 3: build_mozc_db.py 상세 분석

#### 파일 구조

```
build_mozc_db.py는 두 가지 일을 순서대로 수행합니다:
1. Mozc 단어 사전 → SQLite로 변환
2. 연결 행렬 → 바이너리 파일로 변환
```

#### Part 1: 단어 사전 생성

```python
# [1/2] Mozc 단어 사전 SQLite 생성 시작

# 1. 기존 DB 파일 삭제 (깨끗한 시작)
if os.path.exists(db_path):
    os.remove(db_path)

# 2. 새 SQLite DB 연결
conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# 3. 성능 최적화 (대량 데이터 삽입)
cursor.execute("PRAGMA synchronous = OFF;")   # 동기화 무시 → 속도 향상
cursor.execute("PRAGMA journal_mode = MEMORY;")  # 저널을 메모리에만 유지

# 4. 테이블 생성
cursor.execute("""
CREATE TABLE dictionary (
    reading TEXT NOT NULL,      # 히라가나 읽음 (예: "かく")
    kanji TEXT NOT NULL,        # 한자 표기 (예: "書")
    left_id INTEGER NOT NULL,   # 형태소 분석용 ID
    right_id INTEGER NOT NULL,  # 형태소 분석용 ID
    cost INTEGER NOT NULL       # 비용 (낮을수록 자주 사용)
);
""")

# 5. dictionary*.txt 파일 처리
dict_files = sorted(glob.glob("dictionary*.txt"))
batch_data = []

for file_path in dict_files:
    print(f"처리 중: {file_path}")
    
    with open(file_path, "r", encoding="utf-8") as f:
        for line in f:
            # Mozc 파일 형식:
            # reading(0)  left_id(1)  right_id(2)  cost(3)  (예약)(4)  kanji(5) ...
            # 예: かく      6  6  9012  1  書
            
            parts = line.strip().split("\t")
            if len(parts) >= 5:
                # 필드 추출
                reading = parts[0]      # "かく"
                left_id = int(parts[1]) # 6
                right_id = int(parts[2]) # 6
                cost = int(parts[3])    # 9012
                kanji = parts[4]        # "書"
                
                # 배치에 데이터 추가
                batch_data.append((reading, kanji, left_id, right_id, cost))
            
            # 100,000개마다 데이터베이스에 커밋 (메모리 절약)
            if len(batch_data) >= 100000:
                cursor.executemany(
                    "INSERT INTO dictionary VALUES (?, ?, ?, ?, ?)",
                    batch_data
                )
                conn.commit()
                batch_data.clear()

# 6. 남은 데이터 커밋
if batch_data:
    cursor.executemany(
        "INSERT INTO dictionary VALUES (?, ?, ?, ?, ?)",
        batch_data
    )
    conn.commit()

# 7. 조회 속도 최적화를 위한 인덱스 생성
# 가장 자주 사용되는 검색 조건: reading + cost
cursor.execute("CREATE INDEX idx_reading_cost ON dictionary(reading, cost);")
conn.commit()

# 8. 데이터베이스 최적화 (파일 크기 감소)
cursor.execute("VACUUM;")
conn.close()
```

**핵심 이해:**

| 개념 | 설명 | 예시 |
|:---|:---|:---|
| **reading** | 히라가나 읽음 | "かく" |
| **kanji** | 한자 표기 | "書" |
| **left_id/right_id** | 형태소 연결 ID | 6, 6 |
| **cost** | 비용 (낮을수록 자주 사용) | 9012 |
| **인덱스** | 검색 속도 향상 | reading과 cost로 빠르게 검색 |
| **PRAGMA** | SQLite 설정 | 동기화 끄기 = 속도 향상 |

#### Part 2: 연결 행렬 생성

```python
# [2/2] 연결 행렬 바이너리(Short Array) 변환

# 1. 텍스트 파일 읽기
matrix_txt = "connection_single_column.txt"

with open(matrix_txt, "r", encoding="utf-8") as f_in, \
     open("connection.bin", "wb") as f_out:
    
    # 2. 첫 번째 줄: 행렬 크기 (보통 2672)
    matrix_size = int(f_in.readline().strip())
    # 이는 2672 × 2672 크기의 정방 행렬을 의미
    
    # 3. 헤더에 행렬 크기 저장 (4바이트 정수)
    f_out.write(struct.pack("<i", matrix_size))
    # "<i" = little-endian 부호있는 정수
    
    # 4. 나머지 2672×2672 = 7,139,584개의 비용 데이터 처리
    count = 0
    for line in f_in:
        cost = int(line.strip())
        
        # 5. 각 비용을 2바이트 signed short로 인코딩
        f_out.write(struct.pack("<h", cost))
        # "<h" = little-endian 부호있는 2바이트 정수
        
        count += 1
```

**바이너리 형식:**

```
바이너리 파일 구조:
┌─────────────────────────────────────────┐
│ 헤더: matrix_size (4바이트)             │
│       예: 2672 → 0x00 0x00 0x0A 0x70   │
├─────────────────────────────────────────┤
│ 데이터: cost[0] (2바이트 short)         │
│        cost[1] (2바이트 short)          │
│        cost[2] (2바이트 short)          │
│        ...                              │
│        cost[7139583] (2바이트 short)    │
└─────────────────────────────────────────┘

총 크기: 4 + (2672 × 2672 × 2) = 약 43MB
```

### 4.4 Step 4: BLOB.py로 연결 행렬 삽입

```python
# BLOB.py - connection.bin을 SQLite BLOB으로 삽입

# 1. 바이너리 파일 읽기
with open("connection.bin", "rb") as f:
    matrix_bytes = f.read()  # 모든 바이트를 메모리에 로드

# 2. 행렬 차원 계산 (역 계산)
# 파일에 저장된 헤더 데이터 사용 가능하지만,
# 여기서는 파일 크기로부터 계산
total_shorts = len(matrix_bytes) // 2  # 2바이트 단위로 개수
matrix_size = int(total_shorts ** 0.5)
# 예: 14,278,656 바이트 ÷ 2 = 7,139,328 shorts
#     √7,139,328 ≈ 2,672

# 3. SQLite 테이블 생성 (BLOB 저장용)
cursor.execute("""
CREATE TABLE IF NOT EXISTS matrix_metadata (
    id INTEGER PRIMARY KEY,
    matrix_size INTEGER NOT NULL,
    data BLOB NOT NULL       # BLOB: Binary Large Object
)
""")

# 4. 바이너리 데이터 삽입
cursor.execute("""
INSERT OR REPLACE INTO matrix_metadata (id, matrix_size, data)
VALUES (1, ?, ?)
""", (matrix_size, matrix_bytes))

conn.commit()
conn.close()

print(f"Matrix saved: {matrix_size}x{matrix_size}")
# 출력: Matrix saved: 2672x2672
```

### 4.5 Step 5: 파일 배포

```bash
# 1. 파일 압축
zip mozc_dict_connect.zip mozc_dict_connect.db

# 2. GitHub Release에 업로드
# https://github.com/stonkim93/IMEJapanese/releases

# 3. IMEJapanese 앱에서 자동 다운로드
# Program.cs에서:
// "Mozc 오프라인 한자변환" 메뉴 클릭
// → 파일 없음 확인
// → 사용자 동의
// → mozc_dict_connect.zip 자동 다운로드
// → 압축 해제
// → 사용 가능
```

---

## 5. 두 방식의 비교

### 5.1 성능 비교

| 항목 | Mozc 오프라인 | Google 온라인 |
|:---|:---|:---|
| **응답 시간** | 10~50ms (로컬) | 50~200ms (네트워크) |
| **첫 호출** | 빠름 | 빠름 |
| **연속 호출** | 캐시되어 더 빠름 | 매번 네트워크 요청 |
| **동시성** | 단일 스레드 안전 | 비동기 처리 필요 |

### 5.2 정확도 비교

| 항목 | Mozc 오프라인 | Google 온라인 |
|:---|:---|:---|
| **기본 단어** | 95% | 98% |
| **신조어** | 낮음 | 높음 (AI 기반) |
| **형태소 분석** | 단순 | 고도화 |
| **동음이의어** | 기본 | AI 추론 |

### 5.3 사용자 관점

| 항목 | Mozc 오프라인 | Google 온라인 |
|:---|:---|:---|
| **설정 난도** | 높음 (파일 다운로드) | 낮음 (자동) |
| **오프라인 사용** | 가능 | 불가능 |
| **개인정보** | 안전 | Google 서버 전송 |
| **비용** | 무료 | 무료 (하지만 Google 데이터 센터 비용) |

### 5.4 코드 복잡도

| 항목 | Mozc 오프라인 | Google 온라인 |
|:---|:---|:---|
| **코드 라인** | ~200줄 | ~100줄 |
| **외부 의존성** | sqlite3 | System.Net.Http, System.Text.Json |
| **에러 처리** | 파일 없음, DB 오류 | 네트워크 오류, JSON 파싱 |
| **디버깅** | 비교적 쉬움 | 네트워크 문제 복잡 |

---

## 6. 초보자 학습 가이드

### 6.1 코드 읽기 순서 (추천)

#### Level 1: 기본 개념 이해 (2-3시간)

1. **Program.cs 읽기**
   - `UseGoogleApi` 플래그 확인
   - 메뉴 항목 "Mozc 오프라인" vs "Google 온라인" 선택 로직

2. **ImeNativeCore.cs의 Space 키 처리**
   - Space 키 감지 로직
   - `ReadSelectedText()` 함수 호출
   - `ShowKanjiCandidateAsync()` 호출

#### Level 2: 각 방식 구현 이해 (4-5시간)

3. **Mozc 오프라인 (MozcDictionary.cs)**
   - `LoadDictionary()`: SQLite 데이터 로드
   - `GetKanjiCandidates()`: 검색 로직
   - Connection Matrix 로드

4. **Google 온라인 (개념 수준)**
   - HTTP 요청 구성
   - JSON 응답 파싱
   - 비동기 처리

#### Level 3: 완전 이해 (1-2주)

5. **Python 빌드 스크립트**
   - `build_mozc_db.py`: Mozc 파일 처리
   - `BLOB.py`: 바이너리 삽입

6. **SQLite 이해**
   - 인덱스의 역할
   - PRAGMA 최적화
   - BLOB 데이터 타입

### 6.2 주요 개념 정리

#### 1. 데이터베이스 스키마

```
dictionary 테이블:
┌──────────┬──────────┬─────────┬─────────┬──────────┐
│ reading  │ kanji    │ left_id │ right_id │ cost    │
├──────────┼──────────┼─────────┼─────────┼──────────┤
│ かく     │ 書       │ 6       │ 6       │ 9012    │
│ かく     │ 描       │ 6       │ 6       │ 10234   │
│ かく     │ 画       │ 6       │ 6       │ 11456   │
└──────────┴──────────┴─────────┴─────────┴──────────┘

인덱스: idx_reading_cost
용도: WHERE reading = 'かく' ORDER BY cost를 빠르게 처리
```

#### 2. SQL 쿼리 패턴

```sql
-- 기본 검색: reading으로 한자 찾기
SELECT kanji, cost 
FROM dictionary 
WHERE reading = 'かく' 
ORDER BY cost ASC;

-- 결과:
-- 書   9012
-- 描  10234
-- 画  11456
```

#### 3. 비용(Cost) 해석

```
cost = 9012  → 매우 자주 사용됨 → 높은 우선순위
cost = 10234 → 자주 사용됨     → 중간 우선순위
cost = 11456 → 드물게 사용됨   → 낮은 우선순위

비용이 낮을수록 더 적절한 한자 변환!
```

### 6.3 실습 과제

#### 과제 1: Mozc 데이터 탐색

```csharp
// 1. SQLite 쿼리 도구(DB Browser) 사용
// 2. mozc_dict_connect.db 열기
// 3. 다음 쿼리 실행해보기:

SELECT kanji, cost 
FROM dictionary 
WHERE reading = 'ある' 
LIMIT 10;

// 결과 관찰:
// - 한자가 여러 개인가?
// - cost 값이 어떻게 배열되어 있는가?
```

#### 과제 2: Connection Matrix 이해

```csharp
// 1. MozcDictionary.cs에서 _matrixSize 확인
// 2. 행렬 크기 계산:
//    2672 x 2672 = 7,139,584개 원소
//    각 원소 2바이트 = 14,279,168 바이트 ≈ 13.6MB

// 3. 메모리에 로드된 행렬 크기:
//    short[] _transitionMatrix.Length
```

#### 과제 3: Google API 테스트

```csharp
// 1. 간단한 콘솔 앱 작성
// 2. Google CGI API 호출:

string hiragana = "かきます";
string url = $"https://www.google.com/transliterate?" +
             $"text={Uri.EscapeDataString(hiragana)}&" +
             "langpair=ja-Hira|ja-Kanji";

using (HttpClient client = new())
{
    var response = await client.GetAsync(url);
    var json = await response.Content.ReadAsStringAsync();
    Console.WriteLine(json);
}

// 3. 응답 JSON 파싱해보기
```

### 6.4 트러블슈팅 가이드

#### 문제 1: "mozc_dict_connect.db를 찾을 수 없음"

```
원인: 파일이 없거나 잘못된 경로

해결:
1. 파일이 .exe와 같은 폴더에 있는가? 확인
2. 파일 이름 정확성 확인 (대소문자 구분)
3. Program.cs의 다운로드 로직 실행
```

#### 문제 2: "Dictionary 조회 오류"

```
원인: 데이터베이스 파일 손상 또는 락

해결:
1. 파일이 다른 프로세스에서 열려있는가? 확인
2. 읽기 전용 권한 확인
3. 파일 재다운로드
```

#### 문제 3: "Google API 응답 없음"

```
원인: 네트워크 오류 또는 API 변경

해결:
1. 인터넷 연결 확인
2. 방화벽/프록시 확인
3. 타임아웃 값 증가 (HttpClient.Timeout)
4. 오프라인 모드로 전환
```

---

## 요약

### Mozc 오프라인 한자변환
- ✅ **장점**: 빠름, 오프라인 사용 가능, 안전
- ❌ **단점**: 파일 다운로드 필요, 신조어 약함
- 💾 **데이터**: SQLite DB (단어사전 + 연결 행렬)
- ⚙️ **생성**: Python 스크립트로 빌드

### Google 온라인 한자변환
- ✅ **장점**: 설정 불필요, 고정확도, 신조어 강함
- ❌ **단점**: 느림, 네트워크 필수, 개인정보
- 🌐 **통신**: HTTP 요청/응답
- ⚡ **처리**: 비동기 (async/await)

### 추천 사용
```
초보자: Google 온라인으로 시작
       (설정 간단, 안정적)

숙련자: Mozc 오프라인으로 전환
       (성능 향상, 오프라인 독립)

최적: 두 방식 모두 지원
      (사용자가 선택 가능)
```

---

**이 문서로 IMEJapanese의 한자변환 기능을 완벽히 이해할 수 있습니다!** 🚀
