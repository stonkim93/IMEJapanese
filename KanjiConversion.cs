// KanjiConversion.cs - IMEJapanese
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace IMEJapanese
{
    /// <summary>
    /// 히라가나 텍스트의 형태소를 분석하여 기본형(어간)과 활용 어미(접미사)를 분리하는 클래스입니다.
    /// 용언(동사, 형용사 등)의 활용을 인식하여 자연스러운 한자 변환을 돕습니다.
    /// </summary>
    public static class JapaneseMorphologyAnalyzer
    {
        // 주요 동사/형용사 활용 어미 목록 (추후 유지보수 시 추가 가능)
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

        /// <summary>
        /// 주어진 히라가나 문자열을 분석하여 가능한 어간(BaseForm)과 어미(Suffix) 조합들을 반환합니다.
        /// </summary>
        /// <param name="hiragana">분석할 히라가나 문자열</param>
        /// <returns>기본형(어간)과 접미사 튜플의 리스트</returns>
        public static List<(string BaseForm, string OriginalSuffix)> Analyze(string hiragana)
        {
            var results = new List<(string BaseForm, string OriginalSuffix)> { (hiragana, "") };
            if (string.IsNullOrEmpty(hiragana)) return results;

            foreach (string suffix in Suffixes)
            {
                if (hiragana.EndsWith(suffix) && hiragana.Length > suffix.Length)
                {
                    string stem = hiragana.Substring(0, hiragana.Length - suffix.Length);
                    if (stem.Length == 0) continue;
                    
                    char lastChar = stem[stem.Length - 1];
                    char s0 = suffix[0]; 

                    if (s0 == 'し' || s0 == 'す' || s0 == 'さ') 
                    {
                        results.Add((stem, suffix));
                    }

                    if (CharacterDatabase.IsValidCharacter(lastChar))
                    {
                        ushort code = CharacterDatabase.GetCodeFromChar(lastChar);
                        string baseStem = stem.Substring(0, stem.Length - 1);

                        if (s0 == 'た' || s0 == 'だ')
                        {
                            switch (code)
                            {
                                case 332: // っ
                                    results.Add((baseStem + "う", "っ" + suffix));
                                    results.Add((baseStem + "つ", "っ" + suffix));
                                    results.Add((baseStem + "る", "っ" + suffix));
                                    break;
                                case 092: // ん
                                    results.Add((baseStem + "む", "ん" + suffix));
                                    results.Add((baseStem + "ぬ", "ん" + suffix));
                                    results.Add((baseStem + "ぶ", "ん" + suffix));
                                    break;
                                case 001: // い
                                    results.Add((baseStem + "く", "い" + suffix));
                                    results.Add((baseStem + "ぐ", "い" + suffix));
                                    break;
                            }
                        }

                        if (code < 200 && (code % 10) == 1) // i-row
                        {
                            ushort uCode = (ushort)(code + 1); // u-row
                            if (CharacterDatabase.ContainsCode(uCode))
                            {
                                char uChar = CharacterDatabase.GetCharacterData(uCode).Hiragana;
                                results.Add((baseStem + uChar, lastChar + suffix));
                            }
                        }
                    }
                }
            }
            return results;
        }
    }

    /// <summary>
    /// 동적 계획법(DP) 및 비터비(Viterbi) 알고리즘을 활용하여 
    /// 입력된 히라가나 문자열을 최적의 한자 후보들로 변환하는 클래스입니다.
    /// </summary>
    public static class KanjiConverter
    {
        /// <summary>
        /// 히라가나 입력 문자열에 대해 가장 적합한 한자 변환 후보(KanjiEntry) 리스트를 추출합니다.
        /// 형태소 분석 및 형태소 간 연결 비용(Transition Cost)을 고려하여 최적 경로를 찾습니다.
        /// </summary>
        /// <param name="text">변환할 원본 문자열</param>
        /// <returns>우선순위(Cost)가 높은 순서대로 정렬된 한자 후보 리스트</returns>
        public static List<MozcDictionary.KanjiEntry> GetKanjiCandidatesOptimized(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) 
                return new List<MozcDictionary.KanjiEntry>();

            string normalized = JapaneseCharacterProcessor.ToHiragana(text);
            int n = normalized.Length;
            
            var allSubstrings = new HashSet<string>();
            for (int i = 0; i < n; i++) {
                for (int len = 1; len <= Math.Min(20, n - i); len++) {
                    allSubstrings.Add(normalized.Substring(i, len));
                }
            }
            var dictMatches = MozcDictionary.GetEntriesForReadingsBatch(allSubstrings);

            int beamWidth = 50; 
            var dp = new Dictionary<ushort, List<(int cost, string kanji)>>[n + 1];
            for (int i = 0; i <= n; i++) 
                dp[i] = new Dictionary<ushort, List<(int cost, string kanji)>>();
            
            dp[0][0] = new List<(int cost, string kanji)> { (0, "") };

            for (int i = 0; i < n; i++)
            {
                if (dp[i].Count == 0) continue;

                foreach (var kvp in dp[i].ToList()) {
                    dp[i][kvp.Key] = kvp.Value.OrderBy(x => x.cost).Take(beamWidth).ToList();
                }

                var matches = new List<MozcDictionary.ReadingMatch>();
                for (int len = 1; len <= Math.Min(20, n - i); len++)
                {
                    string subText = normalized.Substring(i, len);
                    
                    if (dictMatches.TryGetValue(subText, out var entries)) {
                        foreach (var e in entries) 
                            matches.Add(new MozcDictionary.ReadingMatch { Length = len, Entry = e });
                    }

                    if (len > 1) {
                        var morphs = JapaneseMorphologyAnalyzer.Analyze(subText);
                        foreach (var morph in morphs) {
                            if (string.IsNullOrEmpty(morph.OriginalSuffix)) continue;
                            if (dictMatches.TryGetValue(morph.BaseForm, out var baseEntries)) {
                                foreach (var baseEntry in baseEntries) {
                                    var combinedKanji = baseEntry.Kanji + morph.OriginalSuffix;
                                    matches.Add(new MozcDictionary.ReadingMatch { 
                                        Length = len, 
                                        Entry = new MozcDictionary.KanjiEntry(subText, combinedKanji, baseEntry.LeftId, baseEntry.RightId, baseEntry.Cost + 500) 
                                    });
                                }
                            }
                        }
                    }

                    if (len == 1)
                    {
                        ushort fallbackId = 1934;
                        int fallbackCost = CharacterDatabase.IsValidCharacter(subText[0]) ? 8000 : 10000;
                        
                        matches.Add(new MozcDictionary.ReadingMatch {
                            Length = 1,
                            Entry = new MozcDictionary.KanjiEntry(subText, subText, fallbackId, fallbackId, fallbackCost) 
                        });
                    }
                }

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
                            int totalCost = path.cost + transitionCost + cand.Cost;
                            if (!dp[nextIdx].ContainsKey(cand.RightId))
                                dp[nextIdx][cand.RightId] = new List<(int cost, string kanji)>();

                            dp[nextIdx][cand.RightId].Add((totalCost, path.kanji + cand.Kanji));
                        }
                    }
                }
            }

            var finalCandidates = new List<(int cost, string kanji)>();
            foreach (var kvp in dp[n])
            {
                int lastRightId = kvp.Key;
                int eosTransitionCost = MozcDictionary.GetTransitionCost(lastRightId, 0);

                foreach (var path in kvp.Value) {
                    finalCandidates.Add((path.cost + eosTransitionCost, path.kanji));
                }
            }

            var finalPaths = finalCandidates
                                .OrderBy(p => p.cost)
                                .GroupBy(p => p.kanji)
                                .Select(g => g.First())
                                .Take(9).ToList();

            var results = new List<MozcDictionary.KanjiEntry>();
            foreach (var path in finalPaths) {
                results.Add(new MozcDictionary.KanjiEntry(normalized, path.kanji, 0, 0, path.cost));
            }
            return results;
        }
    }
}
