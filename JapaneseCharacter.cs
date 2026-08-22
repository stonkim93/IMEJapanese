using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IMEJapanese
{
    /// <summary>
    /// 일본어 문자를 3자리 숫자 코드로 효율적으로 표현하는 구조체
    /// 
    /// 코드 구조: XXX (3자리)
    /// - 첫 자리: 자음 그룹 (0=모음, 1=k, 2=s, 3=t, 4=h, 5=m, 6=y, 7=r, 8=w, 9=n)
    /// - 둘째 자리: 모음 인덱스 (0=a, 1=i, 2=u, 3=e, 4=o, 5~9=대문자 등),  0~4 히라가나, 5~9 가타카나
    /// - 셋째 자리: 형태 마크 (0=청음, 1=탁음, 2=반탁음, 3=스테가나)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct JapaneseCharacter : IEquatable<JapaneseCharacter>
    {
        public ushort Code { get; private set; }

        public byte ConsonantGroup => (byte)(Code / 100);       // 100의 자리 : 자음 그룹
        public byte VowelIndex => (byte)((Code / 10) % 10);     // 10의 자리 : 모음 인덱스
        public byte Voicing => (byte)(Code % 10);               // 1의 자리 : 형태 마크

        // 지연 로딩을 통한 메모리 최적화
        private char _hiragana;
        public char Hiragana => _hiragana == '\0' ? (_hiragana = CharacterDatabase.GetCharacterData(Code).Hiragana) : _hiragana;

        private char _katakana;
        public char Katakana => _katakana == '\0' ? (_katakana = CharacterDatabase.GetCharacterData(Code).Katakana) : _katakana;

        private string? _romaji;
        public string Romaji => _romaji ??= CharacterDatabase.GetCharacterData(Code).Romaji;

        private string? _KoreanPron;
        public string KoreanPron => _KoreanPron ??= CharacterDatabase.GetCharacterData(Code).KoreanPron;

        public bool IsSeion => Voicing == 0;
        public bool IsDakuten => Voicing == 1;
        public bool IsHandakuten => Voicing == 2;
        public bool IsSutegana => Voicing == 3;
        public bool IsCapital => VowelIndex >= 5;
        public char Vowel => "aiueo"[VowelIndex % 5];

        public static JapaneseCharacter FromCode(ushort code)
        {
            if (!CharacterDatabase.ContainsCode(code))
                throw new ArgumentOutOfRangeException(nameof(code), $"등록되지 않은 문자 코드입니다: {code:000}");
            return new JapaneseCharacter { Code = code };
        }

        public static JapaneseCharacter FromHiragana(char hiragana) => FromCode(CharacterDatabase.GetCodeFromHiragana(hiragana));
        public static JapaneseCharacter FromKatakana(char katakana) => FromCode(CharacterDatabase.GetCodeFromKatakana(katakana));

        /// <summary>
        /// 청음 형태(원형) 반환
        /// </summary>
        public JapaneseCharacter ToSeion()
        {
            if (IsSeion) return this;
            return FromCode((ushort)((Code / 10) * 10)); // 일의 자리를 0으로 초기화
        }

        /// <summary>
        /// 청음(0) -> 탁음(1) -> 반탁음(2) -> 스테가나(3) -> 청음(0) 순환
        /// 문자에 존재하지 않는 상태는 자동으로 건너뜁니다. (예: 'あ'(0) -> 'ぁ'(3) -> 'あ'(0))
        /// </summary>
        public JapaneseCharacter NextVoicing()
        {
            byte currentVoicing = Voicing;
            
            // 4가지 상태를 순차적으로 탐색하여 존재하는 가장 가까운 다음 상태 반환
            for (int i = 1; i <= 4; i++)
            {
                byte nextVoicing = (byte)((currentVoicing + i) % 4);
                ushort nextCode = (ushort)((Code / 10) * 10 + nextVoicing);
                
                if (CharacterDatabase.ContainsCode(nextCode))
                {
                    return FromCode(nextCode);
                }
            }

            return this; // 상태 변화가 불가능한 문자인 경우 원본 유지
        }

        public override string ToString() => $"{Code:000}: {Hiragana}/{Katakana} ({KoreanPron})";
        public override bool Equals(object? obj) => obj is JapaneseCharacter other && Code == other.Code;
        public bool Equals(JapaneseCharacter other) => Code == other.Code;
        public override int GetHashCode() => Code.GetHashCode();
        public static bool operator ==(JapaneseCharacter left, JapaneseCharacter right) => left.Code == right.Code;
        public static bool operator !=(JapaneseCharacter left, JapaneseCharacter right) => left.Code != right.Code;
    }

    public class CharData
    {
        public char Hiragana { get; init; }
        public char Katakana { get; init; }
        public string EngCategory { get; init; } = string.Empty;
        public string Romaji { get; init; } = string.Empty;
        public string KoreanPron { get; init; } = string.Empty;
    }

    public static class CharacterDatabase
    {
        private static readonly Dictionary<ushort, CharData> CodeToCharData = new()
        {
            // 기본 청음 (0) : 모음 ~ n, ん은 1000번이지만, わ행의 비어있는 920번에 배치하고, 영어 "ng"로 표시함.
            { 000, new CharData { Hiragana = 'あ', Katakana = 'ア', EngCategory = "aa", Romaji = "a", KoreanPron = "아" } },
            { 010, new CharData { Hiragana = 'い', Katakana = 'イ', EngCategory = "ai", Romaji = "i", KoreanPron = "이" } },
            { 020, new CharData { Hiragana = 'う', Katakana = 'ウ', EngCategory = "au", Romaji = "u", KoreanPron = "우" } },
            { 030, new CharData { Hiragana = 'え', Katakana = 'エ', EngCategory = "ae", Romaji = "e", KoreanPron = "에" } },
            { 040, new CharData { Hiragana = 'お', Katakana = 'オ', EngCategory = "ao", Romaji = "o", KoreanPron = "오" } },
            { 100, new CharData { Hiragana = 'か', Katakana = 'カ', EngCategory = "ka", Romaji = "ka", KoreanPron = "카" } },
            { 110, new CharData { Hiragana = 'き', Katakana = 'キ', EngCategory = "ki", Romaji = "ki", KoreanPron = "키" } },
            { 120, new CharData { Hiragana = 'く', Katakana = 'ク', EngCategory = "ku", Romaji = "ku", KoreanPron = "쿠" } },
            { 130, new CharData { Hiragana = 'け', Katakana = 'ケ', EngCategory = "ke", Romaji = "ke", KoreanPron = "케" } },
            { 140, new CharData { Hiragana = 'こ', Katakana = 'コ', EngCategory = "ko", Romaji = "ko", KoreanPron = "코" } },
            { 200, new CharData { Hiragana = 'さ', Katakana = 'サ', EngCategory = "sa", Romaji = "sa", KoreanPron = "사" } },
            { 210, new CharData { Hiragana = 'し', Katakana = 'シ', EngCategory = "si", Romaji = "shi", KoreanPron = "시" } },
            { 220, new CharData { Hiragana = 'す', Katakana = 'ス', EngCategory = "su", Romaji = "su", KoreanPron = "스" } },
            { 230, new CharData { Hiragana = 'せ', Katakana = 'セ', EngCategory = "se", Romaji = "se", KoreanPron = "세" } },
            { 240, new CharData { Hiragana = 'そ', Katakana = 'ソ', EngCategory = "so", Romaji = "so", KoreanPron = "소" } },
            { 300, new CharData { Hiragana = 'た', Katakana = 'タ', EngCategory = "ta", Romaji = "ta", KoreanPron = "타" } },
            { 310, new CharData { Hiragana = 'ち', Katakana = 'チ', EngCategory = "ti", Romaji = "chi", KoreanPron = "치" } },
            { 320, new CharData { Hiragana = 'つ', Katakana = 'ツ', EngCategory = "tu", Romaji = "tsu", KoreanPron = "츠" } },
            { 330, new CharData { Hiragana = 'て', Katakana = 'テ', EngCategory = "te", Romaji = "te", KoreanPron = "테" } },
            { 340, new CharData { Hiragana = 'と', Katakana = 'ト', EngCategory = "to", Romaji = "to", KoreanPron = "토" } },
            { 400, new CharData { Hiragana = 'は', Katakana = 'ハ', EngCategory = "ha", Romaji = "ha", KoreanPron = "하" } },
            { 410, new CharData { Hiragana = 'ひ', Katakana = 'ヒ', EngCategory = "hi", Romaji = "hi", KoreanPron = "히" } },
            { 420, new CharData { Hiragana = 'ふ', Katakana = 'フ', EngCategory = "hu", Romaji = "fu", KoreanPron = "후" } },
            { 430, new CharData { Hiragana = 'へ', Katakana = 'ヘ', EngCategory = "he", Romaji = "he", KoreanPron = "헤" } },
            { 440, new CharData { Hiragana = 'ほ', Katakana = 'ホ', EngCategory = "ho", Romaji = "ho", KoreanPron = "호" } },
            { 500, new CharData { Hiragana = 'な', Katakana = 'ナ', EngCategory = "na", Romaji = "na", KoreanPron = "나" } },
            { 510, new CharData { Hiragana = 'に', Katakana = 'ニ', EngCategory = "ni", Romaji = "ni", KoreanPron = "니" } },
            { 520, new CharData { Hiragana = 'ぬ', Katakana = 'ヌ', EngCategory = "nu", Romaji = "nu", KoreanPron = "누" } },
            { 530, new CharData { Hiragana = 'ね', Katakana = 'ネ', EngCategory = "ne", Romaji = "ne", KoreanPron = "네" } },
            { 540, new CharData { Hiragana = 'の', Katakana = 'ノ', EngCategory = "no", Romaji = "no", KoreanPron = "노" } },
            { 600, new CharData { Hiragana = 'ま', Katakana = 'マ', EngCategory = "ma", Romaji = "ma", KoreanPron = "마" } },
            { 610, new CharData { Hiragana = 'み', Katakana = 'ミ', EngCategory = "mi", Romaji = "mi", KoreanPron = "미" } },
            { 620, new CharData { Hiragana = 'む', Katakana = 'ム', EngCategory = "mu", Romaji = "mu", KoreanPron = "무" } },
            { 630, new CharData { Hiragana = 'め', Katakana = 'メ', EngCategory = "me", Romaji = "me", KoreanPron = "메" } },
            { 640, new CharData { Hiragana = 'も', Katakana = 'モ', EngCategory = "mo", Romaji = "mo", KoreanPron = "모" } },
            { 700, new CharData { Hiragana = 'ら', Katakana = 'ラ', EngCategory = "ra", Romaji = "ra", KoreanPron = "라" } },
            { 710, new CharData { Hiragana = 'り', Katakana = 'リ', EngCategory = "ri", Romaji = "ri", KoreanPron = "리" } },
            { 720, new CharData { Hiragana = 'る', Katakana = 'ル', EngCategory = "ru", Romaji = "ru", KoreanPron = "루" } },
            { 730, new CharData { Hiragana = 'れ', Katakana = 'レ', EngCategory = "re", Romaji = "re", KoreanPron = "레" } },
            { 740, new CharData { Hiragana = 'ろ', Katakana = 'ロ', EngCategory = "ro", Romaji = "ro", KoreanPron = "로" } },
            { 800, new CharData { Hiragana = 'や', Katakana = 'ヤ', EngCategory = "ya", Romaji = "ya", KoreanPron = "야" } },
            { 820, new CharData { Hiragana = 'ゆ', Katakana = 'ユ', EngCategory = "yu", Romaji = "yu", KoreanPron = "유" } },
            { 840, new CharData { Hiragana = 'よ', Katakana = 'ヨ', EngCategory = "yo", Romaji = "yo", KoreanPron = "요" } },
            { 900, new CharData { Hiragana = 'わ', Katakana = 'ワ', EngCategory = "wa", Romaji = "wa", KoreanPron = "와" } },
            // { 910, new CharData { Hiragana = 'ゐ', Katakana = 'ヰ', EngCategory = "wi", Romaji = "wi", KoreanPron = "위" } },  //910->902
            { 920, new CharData { Hiragana = 'ん', Katakana = 'ン', EngCategory = "ng", Romaji = "n", KoreanPron = "응" } },   //1000->920
            // { 930, new CharData { Hiragana = 'ゑ', Katakana = 'ヱ', EngCategory = "we", Romaji = "we", KoreanPron = "웨" } },  //930->942
            { 940, new CharData { Hiragana = 'を', Katakana = 'ヲ', EngCategory = "wo", Romaji = "wo", KoreanPron = "오" } },

            // 탁음 (1)
            { 021, new CharData { Hiragana = 'ゔ', Katakana = 'ヴ', EngCategory = "vu", Romaji = "vu", KoreanPron = "브" } },
            { 101, new CharData { Hiragana = 'が', Katakana = 'ガ', EngCategory = "ga", Romaji = "ga", KoreanPron = "가" } },
            { 111, new CharData { Hiragana = 'ぎ', Katakana = 'ギ', EngCategory = "gi", Romaji = "gi", KoreanPron = "기" } },
            { 121, new CharData { Hiragana = 'ぐ', Katakana = 'グ', EngCategory = "gu", Romaji = "gu", KoreanPron = "구" } },
            { 131, new CharData { Hiragana = 'げ', Katakana = 'ゲ', EngCategory = "ge", Romaji = "ge", KoreanPron = "게" } },
            { 141, new CharData { Hiragana = 'ご', Katakana = 'ゴ', EngCategory = "go", Romaji = "go", KoreanPron = "고" } },
            { 201, new CharData { Hiragana = 'ざ', Katakana = 'ザ', EngCategory = "za", Romaji = "za", KoreanPron = "자" } },
            { 211, new CharData { Hiragana = 'じ', Katakana = 'ジ', EngCategory = "zi", Romaji = "ji", KoreanPron = "지" } },
            { 221, new CharData { Hiragana = 'ず', Katakana = 'ズ', EngCategory = "zu", Romaji = "zu", KoreanPron = "즈" } },
            { 231, new CharData { Hiragana = 'ぜ', Katakana = 'ゼ', EngCategory = "ze", Romaji = "ze", KoreanPron = "제" } },
            { 241, new CharData { Hiragana = 'ぞ', Katakana = 'ゾ', EngCategory = "zo", Romaji = "zo", KoreanPron = "조" } },
            { 301, new CharData { Hiragana = 'だ', Katakana = 'ダ', EngCategory = "da", Romaji = "da", KoreanPron = "다" } },
            { 311, new CharData { Hiragana = 'ぢ', Katakana = 'ヂ', EngCategory = "di", Romaji = "ji", KoreanPron = "지" } },
            { 321, new CharData { Hiragana = 'づ', Katakana = 'ヅ', EngCategory = "du", Romaji = "zu", KoreanPron = "즈" } },
            { 331, new CharData { Hiragana = 'で', Katakana = 'デ', EngCategory = "de", Romaji = "de", KoreanPron = "데" } },
            { 341, new CharData { Hiragana = 'ど', Katakana = 'ド', EngCategory = "do", Romaji = "do", KoreanPron = "도" } },
            { 401, new CharData { Hiragana = 'ば', Katakana = 'バ', EngCategory = "ba", Romaji = "ba", KoreanPron = "바" } },
            { 411, new CharData { Hiragana = 'び', Katakana = 'ビ', EngCategory = "bi", Romaji = "bi", KoreanPron = "비" } },
            { 421, new CharData { Hiragana = 'ぶ', Katakana = 'ブ', EngCategory = "bu", Romaji = "bu", KoreanPron = "부" } },
            { 431, new CharData { Hiragana = 'べ', Katakana = 'ベ', EngCategory = "be", Romaji = "be", KoreanPron = "베" } },
            { 441, new CharData { Hiragana = 'ぼ', Katakana = 'ボ', EngCategory = "bo", Romaji = "bo", KoreanPron = "보" } },

            // 반탁음 (2) : わ행 청음인 ゐ와 ゑ의 키보드 입력을 위하여 わ와 を의 반탁음으로 추가함. YN전환키 기능으로 입력 가능해짐.
            { 402, new CharData { Hiragana = 'ぱ', Katakana = 'パ', EngCategory = "pa", Romaji = "pa", KoreanPron = "파" } },
            { 412, new CharData { Hiragana = 'ぴ', Katakana = 'ピ', EngCategory = "pi", Romaji = "pi", KoreanPron = "피" } },
            { 422, new CharData { Hiragana = 'ぷ', Katakana = 'プ', EngCategory = "pu", Romaji = "pu", KoreanPron = "푸" } },
            { 432, new CharData { Hiragana = 'ぺ', Katakana = 'ペ', EngCategory = "pe", Romaji = "pe", KoreanPron = "페" } },
            { 442, new CharData { Hiragana = 'ぽ', Katakana = 'ポ', EngCategory = "po", Romaji = "po", KoreanPron = "포" } },
            { 902, new CharData { Hiragana = 'ゐ', Katakana = 'ヰ', EngCategory = "wi", Romaji = "wi", KoreanPron = "위" } },  //910->902로 변경함
            { 942, new CharData { Hiragana = 'ゑ', Katakana = 'ヱ', EngCategory = "we", Romaji = "we", KoreanPron = "웨" } },  //930->942로 변경함

            // 스테가나 (3) : 요음, 촉음 등 작은글씨, 문자앞에 x를 추가함
            { 003, new CharData { Hiragana = 'ぁ', Katakana = 'ァ', EngCategory = "xaa", Romaji = "xa", KoreanPron = "아" } },
            { 013, new CharData { Hiragana = 'ぃ', Katakana = 'ィ', EngCategory = "xai", Romaji = "xi", KoreanPron = "이" } },
            { 023, new CharData { Hiragana = 'ぅ', Katakana = 'ゥ', EngCategory = "xau", Romaji = "xu", KoreanPron = "우" } },
            { 033, new CharData { Hiragana = 'ぇ', Katakana = 'ェ', EngCategory = "xae", Romaji = "xe", KoreanPron = "에" } },
            { 043, new CharData { Hiragana = 'ぉ', Katakana = 'ォ', EngCategory = "xao", Romaji = "xo", KoreanPron = "오" } },
            { 103, new CharData { Hiragana = 'ゕ', Katakana = 'ヵ', EngCategory = "xka", Romaji = "xka", KoreanPron = "카" } },
            { 133, new CharData { Hiragana = 'ゖ', Katakana = 'ヶ', EngCategory = "xke", Romaji = "xke", KoreanPron = "케" } },
            { 323, new CharData { Hiragana = 'っ', Katakana = 'ッ', EngCategory = "xtu", Romaji = "xtsu", KoreanPron = "ㅅ" } }, //촉음은 한국어 받침 소리
            { 803, new CharData { Hiragana = 'ゃ', Katakana = 'ャ', EngCategory = "xya", Romaji = "xya", KoreanPron = "야" } },
            { 823, new CharData { Hiragana = 'ゅ', Katakana = 'ュ', EngCategory = "xyu", Romaji = "xyu", KoreanPron = "유" } },
            { 843, new CharData { Hiragana = 'ょ', Katakana = 'ョ', EngCategory = "xyo", Romaji = "xyo", KoreanPron = "요" } },
            { 903, new CharData { Hiragana = 'ゎ', Katakana = 'ヮ', EngCategory = "xwa", Romaji = "xwa", KoreanPron = "와" } },
        };

        private static readonly Dictionary<char, ushort> HiraganaToCode = CodeToCharData.ToDictionary(x => x.Value.Hiragana, x => x.Key);
        private static readonly Dictionary<char, ushort> KatakanaToCode = CodeToCharData.ToDictionary(x => x.Value.Katakana, x => x.Key);

        public static bool ContainsCode(ushort code) => CodeToCharData.ContainsKey(code);
        
        public static CharData GetCharacterData(ushort code)
        {
            if (CodeToCharData.TryGetValue(code, out var data)) return data;
            throw new ArgumentException($"알 수 없는 문자 코드: {code:000}");
        }

        public static ushort GetCodeFromHiragana(char hiragana)
        {
            if (HiraganaToCode.TryGetValue(hiragana, out var code)) return code;
            throw new ArgumentException($"알 수 없는 히라가나: {hiragana}");
        }

        public static ushort GetCodeFromKatakana(char katakana)
        {
            if (KatakanaToCode.TryGetValue(katakana, out var code)) return code;
            throw new ArgumentException($"알 수 없는 카타카나: {katakana}");
        }

        public static bool IsValidCharacter(char ch) => HiraganaToCode.ContainsKey(ch) || KatakanaToCode.ContainsKey(ch);
    }

    public static class JapaneseCharacterProcessor
    {
        public static string ProcessHK(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var result = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (!CharacterDatabase.IsValidCharacter(ch))
                {
                    result.Append(ch);
                    continue;
                }

                bool isHiragana = ch >= 0x3040 && ch <= 0x309F;
                var jpChar = isHiragana ? JapaneseCharacter.FromHiragana(ch) : JapaneseCharacter.FromKatakana(ch);
                result.Append(isHiragana ? jpChar.Katakana : jpChar.Hiragana);
            }
            return result.ToString();
        }

        public static string ProcessYN(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var result = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (!CharacterDatabase.IsValidCharacter(ch))
                {
                    result.Append(ch);
                    continue;
                }

                bool isHiragana = ch >= 0x3040 && ch <= 0x309F;
                var jpChar = isHiragana ? JapaneseCharacter.FromHiragana(ch) : JapaneseCharacter.FromKatakana(ch);
                
                var nextChar = jpChar.NextVoicing();
                result.Append(isHiragana ? nextChar.Hiragana : nextChar.Katakana);
            }
            return result.ToString();
        }

        public static string ToHiragana(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var result = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (!CharacterDatabase.IsValidCharacter(ch))
                {
                    result.Append(ch);
                    continue;
                }
                bool isHiragana = ch >= 0x3040 && ch <= 0x309F;
                var jpChar = isHiragana ? JapaneseCharacter.FromHiragana(ch) : JapaneseCharacter.FromKatakana(ch);
                result.Append(jpChar.Hiragana);
            }
            return result.ToString();
        }
    }

    public static class KanjiConverter
    {
        public static List<MozcDictionary.KanjiEntry> GetMorphologicalMatch(string hiragana)
        {
            if (string.IsNullOrEmpty(hiragana)) 
                return new List<MozcDictionary.KanjiEntry>();

            int n = hiragana.Length;
            var dp = new List<(int cost, string kanji, string reading, ushort rightId)>[n + 1];
            for (int i = 0; i <= n; i++) dp[i] = new List<(int cost, string kanji, string reading, ushort rightId)>();
            
            dp[0].Add((0, "", "", 0));

            for (int i = 0; i < n; i++)
            {
                var topPaths = dp[i].OrderBy(p => p.cost).Take(50).ToList();
                dp[i] = topPaths;

                if (topPaths.Count == 0) continue;

                var matches = MozcDictionary.GetEntriesForReadingAt(hiragana, i, 10);
                
                var fallbackEntry = new MozcDictionary.KanjiEntry(hiragana.Substring(i, 1), hiragana.Substring(i, 1), 0, 0, 8000);
                matches.Add(new MozcDictionary.ReadingMatch { Length = 1, Entry = fallbackEntry });

                foreach (var match in matches)
                {
                    int nextIdx = i + match.Length;
                    if (nextIdx > n) continue;

                    // [추가됨] 방안 2: 길이에 비례하는 보상(음수 비용 가중치) 부여
                    int lengthReward = match.Length * 3000;

                    foreach (var path in topPaths)
                    {
                        int transitionCost = path.rightId == 0 
                            ? MozcDictionary.GetTransitionCost(0, match.Entry.LeftId) 
                            : MozcDictionary.GetTransitionCost(path.rightId, match.Entry.LeftId);
                            
                        // 길이에 따른 보상을 합산하여 최종 비용 계산
                        int totalCost = path.cost + transitionCost + match.Entry.Cost - lengthReward;

                        dp[nextIdx].Add((totalCost, path.kanji + match.Entry.Kanji, path.reading + match.Entry.Reading, match.Entry.RightId));
                    }
                }
            }

            var finalPaths = dp[n].OrderBy(p => p.cost)
                                  .GroupBy(p => p.kanji)
                                  .Select(g => g.First())
                                  .ToList();

            // [추가됨] 방안 3: 1위 후보점수 기준 Threshold 동적 필터링 적용 (최대 6개)
            if (finalPaths.Count > 0)
            {
                int bestCost = finalPaths.First().cost;
                int threshold = 4000; // 허용할 최대 비용 편차
                finalPaths = finalPaths.Where(p => p.cost <= bestCost + threshold).Take(6).ToList();
            }

            var results = new List<MozcDictionary.KanjiEntry>();
            foreach (var path in finalPaths)
            {
                // 음수 코스트 오버플로우/언더플로우 방지를 위해 제한
                short clampedCost = (short)Math.Max(short.MinValue, Math.Min(path.cost, short.MaxValue));
                results.Add(new MozcDictionary.KanjiEntry(path.reading, path.kanji, 0, path.rightId, clampedCost));
            }

            return results;
        }

        public static List<MozcDictionary.KanjiEntry> GetKanjiCandidates(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) 
                return new List<MozcDictionary.KanjiEntry>();

            string normalized = JapaneseCharacterProcessor.ToHiragana(text);

            if (normalized.Length >= 7)
            {
                return GetSegmentedKanjiCandidates(normalized);
            }

            return GetMorphologicalMatch(normalized);
        }

        private static List<MozcDictionary.KanjiEntry> GetSegmentedKanjiCandidates(string text)
        {
            var segments = SplitTextByParticles(text);
            var combinedPaths = new List<(int cost, string kanji, string reading)> { (0, "", "") };

            foreach (var segment in segments)
            {
                var segmentCandidates = GetMorphologicalMatch(segment);
                var nextPaths = new List<(int cost, string kanji, string reading)>();

                var bestCands = segmentCandidates.Take(3).ToList();
                if (bestCands.Count == 0) bestCands.Add(new MozcDictionary.KanjiEntry(segment, segment, 0, 0, 8000));

                foreach (var path in combinedPaths)
                {
                    foreach (var cand in bestCands)
                    {
                        nextPaths.Add((path.cost + cand.Cost, path.kanji + cand.Kanji, path.reading + cand.Reading));
                    }
                }
                
                // 중간 단계 폭발적 증가 방지를 위한 적당 수치 유지
                combinedPaths = nextPaths.OrderBy(p => p.cost).Take(15).ToList();
            }

            var orderedPaths = combinedPaths.OrderBy(p => p.cost).ToList();

            // [추가됨] 방안 3: 최종 반환 시 1위 후보점수 기준 Threshold 동적 필터링 적용 (최대 6개)
            if (orderedPaths.Count > 0)
            {
                int bestCost = orderedPaths.First().cost;
                int threshold = 4000;
                orderedPaths = orderedPaths.Where(p => p.cost <= bestCost + threshold).Take(6).ToList();
            }

            var results = new List<MozcDictionary.KanjiEntry>();
            foreach (var path in orderedPaths)
            {
                short clampedCost = (short)Math.Max(short.MinValue, Math.Min(path.cost, short.MaxValue));
                results.Add(new MozcDictionary.KanjiEntry(path.reading, path.kanji, 0, 0, clampedCost));
            }
            return results;
        }

        private static List<string> SplitTextByParticles(string text)
        {
            var result = new List<string>();
            string[] particles = { "から", "まで", "には", "では", "は", "が", "を", "に", "で", "と", "へ", "も", "の", "、", "。"};
            
            int startIndex = 0;
            while (startIndex < text.Length)
            {
                if (text.Length - startIndex < 7)
                {
                    result.Add(text.Substring(startIndex));
                    break;
                }

                int bestSplitIdx = -1;
                int bestSplitLen = 0;

                for (int i = 2; i <= 6 && (startIndex + i) < text.Length; i++)
                {
                    foreach (var p in particles)
                    {
                        if (text.Substring(startIndex + i).StartsWith(p))
                        {
                            bestSplitIdx = startIndex + i;
                            bestSplitLen = p.Length;
                            break;
                        }
                    }
                    if (bestSplitIdx != -1) break;
                }

                if (bestSplitIdx != -1)
                {
                    int segmentEnd = bestSplitIdx + bestSplitLen;
                    result.Add(text.Substring(startIndex, segmentEnd - startIndex));
                    startIndex = segmentEnd;
                }
                else
                {
                    int chunkLen = Math.Min(6, text.Length - startIndex);
                    result.Add(text.Substring(startIndex, chunkLen));
                    startIndex += chunkLen;
                }
            }

            return result;
        }
    }
}