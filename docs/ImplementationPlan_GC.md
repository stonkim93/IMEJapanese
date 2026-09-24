# IMEPointer 메모리 할당 및 GC 부하 최적화 계획 (Implementation Plan)

`KanjiConversion.cs`의 Viterbi 알고리즘(`GetKanjiCandidatesOptimized`)과 `JapaneseMorphologyAnalyzer`는 한자 변환을 수행할 때 수많은 임시 객체(`string`, `List`, `Dictionary`, `Tuple`)를 생성하여 가비지 컬렉터(GC)에 심각한 부하를 줍니다. 

다음과 같은 방향으로 메모리 할당(GC 압박)을 최적화할 계획입니다.

## 1. DP(동적 계획법) 상태 자료구조 경량화
**문제점:**
Viterbi 알고리즘에서 상태 저장을 위해 매 글자 인덱스마다 `Dictionary<ushort, List<(int cost, string kanji)>>`를 힙(Heap)에 할당합니다. 리스트와 딕셔너리가 중첩되어 있어 객체 생성 오버헤드가 큽니다.

**개선 방안:**
- `Dictionary` 대신 값 타입(struct) 기반의 `List<DpNode>` 또는 고정 크기 배열을 활용하여 객체 할당을 최소화합니다.
- 불필요한 `Tuple` 생성 및 `ToList()` 호출을 제거하고, 내부에서 리스트 풀(List Pool)을 재사용하거나 정렬된 배열을 유지합니다.

## 2. 무분별한 Substring 생성 방지
**문제점:**
`GetKanjiCandidatesOptimized`의 도입부에서 모든 가능한 부분 문자열(최대 길이 20)을 무조건 `Substring`으로 생성하여 `HashSet`에 담은 뒤 DB 배치를 돌립니다. 이로 인해 유효하지 않은 형태의 쓰레기 문자열까지 수백 개가 생성됩니다.

**개선 방안:**
- `ReadOnlySpan<char>`을 활용하여 텍스트를 자르지 않고 비교 및 평가합니다.
- `HashSet`에 미리 모든 조합을 넣지 않고, 기존에 만들어둔 효율적인 `MozcDictionary.GetEntriesForReadingAt`을 재사용하거나, Trie/Prefix 캐싱 방식을 활용하여 유효한 문자열(DB/Cache에 존재할 가능성이 있는 것)만 `string`으로 인스턴스화합니다.

## 3. 형태소 분석기(JapaneseMorphologyAnalyzer) 할당 최소화
**문제점:**
`Analyze` 함수가 호출될 때마다 매번 새로운 `List<(string BaseForm, string OriginalSuffix)>`를 반환하며, 내부적으로도 문자열 덧셈(`+`) 연산을 통해 수많은 어미 문자열 파편을 생성합니다.

**개선 방안:**
- 반환 타입을 `ref struct`나 기존에 할당된 버퍼를 재사용하도록 구조를 변경합니다.
- 런타임에 빈번하게 문자열을 결합(`+`)하지 않도록, `Span`이나 사전 정의된 상태 머신 로직을 이용해 판별만 수행합니다.

위 계획대로 코드 최적화를 진행할까요? **[진행 (Proceed)]** 버튼을 눌러 승인해 주시면 `KanjiConversion.cs`의 구조 개선 작업을 시작하겠습니다.
