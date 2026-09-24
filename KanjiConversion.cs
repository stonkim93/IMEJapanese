// KanjiConversion.cs - IMEJapanese
#nullable enable
using System;
using System.Collections.Generic;

namespace IMEJapanese
{
    /// <summary>
    /// 히라가나 텍스트의 형태소를 분석하여 기본형(어간)과 활용 어미(접미사)를 분리하는 클래스입니다.
    /// 용언(동사, 형용사 등)의 활용을 인식하여 자연스러운 한자 변환을 돕습니다.
    /// </summary>
    public static class JapaneseMorphologyAnalyzer
    {
        private static readonly string[] Suffixes = 
        {
            "ます", "ました", "ましょう", "ません",
            "ない", "なかった", "なく", "なくて",
            "ている", "ていた", "てる",
            "た", "だ", "だろう", "だった",
            "せる", "させる", "れる", "られる",
            "込む", "込んで", "込んだ",
            "する", "した", "して",
            "かった", "くて"
        };

        [ThreadStatic]
        private static List<(string BaseForm, string OriginalSuffix)>? _buffer;

        public static List<(string BaseForm, string OriginalSuffix)> Analyze(string hiragana)
        {
            if (_buffer == null) _buffer = new List<(string, string)>(32);
            _buffer.Clear();
            _buffer.Add((hiragana, ""));

            if (string.IsNullOrEmpty(hiragana)) return _buffer;

            var span = hiragana.AsSpan();

            foreach (string suffix in Suffixes)
            {
                if (span.EndsWith(suffix.AsSpan()) && span.Length > suffix.Length)
                {
                    var stemSpan = span.Slice(0, span.Length - suffix.Length);
                    if (stemSpan.Length == 0) continue;
                    
                    char lastChar = stemSpan[stemSpan.Length - 1];
                    char s0 = suffix[0]; 

                    if (s0 == 'し' || s0 == 'す' || s0 == 'さ') 
                    {
                        _buffer.Add((stemSpan.ToString(), suffix));
                    }

                    if (CharacterDatabase.IsValidCharacter(lastChar))
                    {
                        ushort code = CharacterDatabase.GetCodeFromChar(lastChar);
                        var baseStemSpan = stemSpan.Slice(0, stemSpan.Length - 1);
                        string baseStem = baseStemSpan.ToString();

                        if (s0 == 'た' || s0 == 'だ')
                        {
                            switch (code)
                            {
                                case 332: // っ
                                    _buffer.Add((baseStem + "う", "っ" + suffix));
                                    _buffer.Add((baseStem + "つ", "っ" + suffix));
                                    _buffer.Add((baseStem + "る", "っ" + suffix));
                                    break;
                                case 092: // ん
                                    _buffer.Add((baseStem + "む", "ん" + suffix));
                                    _buffer.Add((baseStem + "ぬ", "ん" + suffix));
                                    _buffer.Add((baseStem + "ぶ", "ん" + suffix));
                                    break;
                                case 001: // い
                                    _buffer.Add((baseStem + "く", "い" + suffix));
                                    _buffer.Add((baseStem + "ぐ", "い" + suffix));
                                    break;
                            }
                        }

                        if (code < 200 && (code % 10) == 1) // i-row
                        {
                            ushort uCode = (ushort)(code + 1); // u-row
                            if (CharacterDatabase.ContainsCode(uCode))
                            {
                                char uChar = CharacterDatabase.GetCharacterData(uCode).Hiragana;
                                _buffer.Add((baseStem + uChar, lastChar + suffix));
                            }
                        }
                    }
                }
            }
            return _buffer;
        }
    }

    public struct DpNode : IComparable<DpNode>
    {
        public int Cost;
        public string Kanji;

        public DpNode(int cost, string kanji)
        {
            Cost = cost;
            Kanji = kanji;
        }

        public int CompareTo(DpNode other)
        {
            return Cost.CompareTo(other.Cost);
        }
    }

    public static class KanjiConverter
    {
        public static List<MozcDictionary.KanjiEntry> GetKanjiCandidatesOptimized(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) 
                return new List<MozcDictionary.KanjiEntry>();

            string normalized = JapaneseCharacterProcessor.ToHiragana(text);
            int n = normalized.Length;
            int beamWidth = 50; 
            
            // DP 배열: 각 위치마다 (RightId -> DpNode 리스트)
            var dp = new Dictionary<ushort, List<DpNode>>[n + 1];
            for (int i = 0; i <= n; i++) 
                dp[i] = new Dictionary<ushort, List<DpNode>>();
            
            dp[0][0] = new List<DpNode> { new DpNode(0, "") };

            for (int i = 0; i < n; i++)
            {
                if (dp[i].Count == 0) continue;

                // Beam Search 최적화 (불필요한 ToList() 제거 및 제자리 정렬)
                foreach (var kvp in dp[i]) {
                    if (kvp.Value.Count > beamWidth) {
                        kvp.Value.Sort();
                        kvp.Value.RemoveRange(beamWidth, kvp.Value.Count - beamWidth);
                    }
                }

                // Substring을 모두 미리 생성하지 않고 GetEntriesForReadingAt 활용
                var matches = MozcDictionary.GetEntriesForReadingAt(normalized, i, 20);

                // 길이가 1인 경우 (Fallback 포함)
                bool hasLength1 = false;
                foreach (var match in matches) {
                    if (match.Length == 1) {
                        hasLength1 = true;
                        break;
                    }
                }

                if (!hasLength1 && i < n)
                {
                    string singleChar = normalized.Substring(i, 1);
                    ushort fallbackId = 1934;
                    int fallbackCost = CharacterDatabase.IsValidCharacter(singleChar[0]) ? 8000 : 10000;
                    
                    matches.Add(new MozcDictionary.ReadingMatch {
                        Length = 1,
                        Entry = new MozcDictionary.KanjiEntry(singleChar, singleChar, fallbackId, fallbackId, fallbackCost) 
                    });
                }

                // 형태소 분석을 통한 추가 매치 생성 (길이 1 이상)
                int matchesCount = matches.Count;
                for (int m = 0; m < matchesCount; m++)
                {
                    var match = matches[m];
                    if (match.Length > 1) {
                        string subText = normalized.Substring(i, match.Length);
                        var morphs = JapaneseMorphologyAnalyzer.Analyze(subText);
                        
                        // _buffer 재사용을 위해 morphs 복사본을 만들어 순회하거나, 
                        // Analyze 내부가 아닌 외부에서 GetEntriesForReadingsBatch 대신 캐시를 이용할 수 있음
                        // 편의상 MozcGoogleDictionary의 GetEntriesForReadingAt을 통해 처리된 기본형을 매칭할 수 있습니다.
                        // (간단화를 위해 여기서는 _buffer 직접 순회)
                        for (int k = 0; k < morphs.Count; k++) {
                            var morph = morphs[k];
                            if (string.IsNullOrEmpty(morph.OriginalSuffix)) continue;
                            
                            // 기본형에 대한 캐시 조회
                            var baseEntries = MozcDictionary.GetEntriesForReadingAt(morph.BaseForm, 0, morph.BaseForm.Length);
                            foreach (var baseMatch in baseEntries) {
                                if (baseMatch.Length == morph.BaseForm.Length) {
                                    var combinedKanji = baseMatch.Entry.Kanji + morph.OriginalSuffix;
                                    matches.Add(new MozcDictionary.ReadingMatch { 
                                        Length = match.Length, 
                                        Entry = new MozcDictionary.KanjiEntry(subText, combinedKanji, baseMatch.Entry.LeftId, baseMatch.Entry.RightId, baseMatch.Entry.Cost + 500) 
                                    });
                                }
                            }
                        }
                    }
                }

                // 다음 상태로 전이
                foreach (var match in matches)
                {
                    int nextIdx = i + match.Length;
                    if (nextIdx > n) continue;

                    var cand = match.Entry;
                    foreach (var kvp in dp[i])
                    {
                        int prevRightId = kvp.Key;
                        int transitionCost = MozcDictionary.GetTransitionCost(prevRightId, cand.LeftId);

                        foreach (var path in kvp.Value)
                        {
                            int totalCost = path.Cost + transitionCost + cand.Cost;
                            
                            if (!dp[nextIdx].TryGetValue(cand.RightId, out var nextList))
                            {
                                nextList = new List<DpNode>();
                                dp[nextIdx][cand.RightId] = nextList;
                            }

                            nextList.Add(new DpNode(totalCost, path.Kanji + cand.Kanji));
                        }
                    }
                }
            }

            var finalCandidates = new List<DpNode>();
            foreach (var kvp in dp[n])
            {
                int lastRightId = kvp.Key;
                int eosTransitionCost = MozcDictionary.GetTransitionCost(lastRightId, 0);

                foreach (var path in kvp.Value) {
                    finalCandidates.Add(new DpNode(path.Cost + eosTransitionCost, path.Kanji));
                }
            }

            finalCandidates.Sort();
            
            var results = new List<MozcDictionary.KanjiEntry>();
            var seenKanji = new HashSet<string>();

            foreach (var path in finalCandidates) {
                if (seenKanji.Add(path.Kanji)) {
                    results.Add(new MozcDictionary.KanjiEntry(normalized, path.Kanji, 0, 0, path.Cost));
                    if (results.Count >= 9) break;
                }
            }

            return results;
        }
    }
}
