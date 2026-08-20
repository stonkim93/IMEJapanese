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

        private string? _koreanPronunciation;
        public string KoreanPronunciation => _koreanPronunciation ??= CharacterDatabase.GetCharacterData(Code).KoreanPronunciation;

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

        public override string ToString() => $"{Code:000}: {Hiragana}/{Katakana} ({KoreanPronunciation})";
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
        public string EnglishCategory { get; init; } = string.Empty;
        public string Romaji { get; init; } = string.Empty;
        public string KoreanPronunciation { get; init; } = string.Empty;
    }

    public static class CharacterDatabase
    {
        private static readonly Dictionary<ushort, CharData> CodeToCharData = new()
        {
            // 기본 청음 (0) : 모음 ~ n, ん은 1000번이지만, わ행의 비어있는 920번에 배치하고, 영어 "ng"로 표시함.
            { 000, new CharData { Hiragana = 'あ', Katakana = 'ア', EnglishCategory = "aa", Romaji = "a", KoreanPronunciation = "아" } },
            { 010, new CharData { Hiragana = 'い', Katakana = 'イ', EnglishCategory = "ai", Romaji = "i", KoreanPronunciation = "이" } },
            { 020, new CharData { Hiragana = 'う', Katakana = 'ウ', EnglishCategory = "au", Romaji = "u", KoreanPronunciation = "우" } },
            { 030, new CharData { Hiragana = 'え', Katakana = 'エ', EnglishCategory = "ae", Romaji = "e", KoreanPronunciation = "에" } },
            { 040, new CharData { Hiragana = 'お', Katakana = 'オ', EnglishCategory = "ao", Romaji = "o", KoreanPronunciation = "오" } },
            { 100, new CharData { Hiragana = 'か', Katakana = 'カ', EnglishCategory = "ka", Romaji = "ka", KoreanPronunciation = "카" } },
            { 110, new CharData { Hiragana = 'き', Katakana = 'キ', EnglishCategory = "ki", Romaji = "ki", KoreanPronunciation = "키" } },
            { 120, new CharData { Hiragana = 'く', Katakana = 'ク', EnglishCategory = "ku", Romaji = "ku", KoreanPronunciation = "쿠" } },
            { 130, new CharData { Hiragana = 'け', Katakana = 'ケ', EnglishCategory = "ke", Romaji = "ke", KoreanPronunciation = "케" } },
            { 140, new CharData { Hiragana = 'こ', Katakana = 'コ', EnglishCategory = "ko", Romaji = "ko", KoreanPronunciation = "코" } },
            { 200, new CharData { Hiragana = 'さ', Katakana = 'サ', EnglishCategory = "sa", Romaji = "sa", KoreanPronunciation = "사" } },
            { 210, new CharData { Hiragana = 'し', Katakana = 'シ', EnglishCategory = "si", Romaji = "shi", KoreanPronunciation = "시" } },
            { 220, new CharData { Hiragana = 'す', Katakana = 'ス', EnglishCategory = "su", Romaji = "su", KoreanPronunciation = "스" } },
            { 230, new CharData { Hiragana = 'せ', Katakana = 'セ', EnglishCategory = "se", Romaji = "se", KoreanPronunciation = "세" } },
            { 240, new CharData { Hiragana = 'そ', Katakana = 'ソ', EnglishCategory = "so", Romaji = "so", KoreanPronunciation = "소" } },
            { 300, new CharData { Hiragana = 'た', Katakana = 'タ', EnglishCategory = "ta", Romaji = "ta", KoreanPronunciation = "타" } },
            { 310, new CharData { Hiragana = 'ち', Katakana = 'チ', EnglishCategory = "ti", Romaji = "chi", KoreanPronunciation = "치" } },
            { 320, new CharData { Hiragana = 'つ', Katakana = 'ツ', EnglishCategory = "tu", Romaji = "tsu", KoreanPronunciation = "츠" } },
            { 330, new CharData { Hiragana = 'て', Katakana = 'テ', EnglishCategory = "te", Romaji = "te", KoreanPronunciation = "테" } },
            { 340, new CharData { Hiragana = 'と', Katakana = 'ト', EnglishCategory = "to", Romaji = "to", KoreanPronunciation = "토" } },
            { 400, new CharData { Hiragana = 'は', Katakana = 'ハ', EnglishCategory = "ha", Romaji = "ha", KoreanPronunciation = "하" } },
            { 410, new CharData { Hiragana = 'ひ', Katakana = 'ヒ', EnglishCategory = "hi", Romaji = "hi", KoreanPronunciation = "히" } },
            { 420, new CharData { Hiragana = 'ふ', Katakana = 'フ', EnglishCategory = "hu", Romaji = "fu", KoreanPronunciation = "후" } },
            { 430, new CharData { Hiragana = 'へ', Katakana = 'ヘ', EnglishCategory = "he", Romaji = "he", KoreanPronunciation = "헤" } },
            { 440, new CharData { Hiragana = 'ほ', Katakana = 'ホ', EnglishCategory = "ho", Romaji = "ho", KoreanPronunciation = "호" } },
            { 500, new CharData { Hiragana = 'な', Katakana = 'ナ', EnglishCategory = "na", Romaji = "na", KoreanPronunciation = "나" } },
            { 510, new CharData { Hiragana = 'に', Katakana = 'ニ', EnglishCategory = "ni", Romaji = "ni", KoreanPronunciation = "니" } },
            { 520, new CharData { Hiragana = 'ぬ', Katakana = 'ヌ', EnglishCategory = "nu", Romaji = "nu", KoreanPronunciation = "누" } },
            { 530, new CharData { Hiragana = 'ね', Katakana = 'ネ', EnglishCategory = "ne", Romaji = "ne", KoreanPronunciation = "네" } },
            { 540, new CharData { Hiragana = 'の', Katakana = 'ノ', EnglishCategory = "no", Romaji = "no", KoreanPronunciation = "노" } },
            { 600, new CharData { Hiragana = 'ま', Katakana = 'マ', EnglishCategory = "ma", Romaji = "ma", KoreanPronunciation = "마" } },
            { 610, new CharData { Hiragana = 'み', Katakana = 'ミ', EnglishCategory = "mi", Romaji = "mi", KoreanPronunciation = "미" } },
            { 620, new CharData { Hiragana = 'む', Katakana = 'ム', EnglishCategory = "mu", Romaji = "mu", KoreanPronunciation = "무" } },
            { 630, new CharData { Hiragana = 'め', Katakana = 'メ', EnglishCategory = "me", Romaji = "me", KoreanPronunciation = "메" } },
            { 640, new CharData { Hiragana = 'も', Katakana = 'モ', EnglishCategory = "mo", Romaji = "mo", KoreanPronunciation = "모" } },
            { 700, new CharData { Hiragana = 'ら', Katakana = 'ラ', EnglishCategory = "ra", Romaji = "ra", KoreanPronunciation = "라" } },
            { 710, new CharData { Hiragana = 'り', Katakana = 'リ', EnglishCategory = "ri", Romaji = "ri", KoreanPronunciation = "리" } },
            { 720, new CharData { Hiragana = 'る', Katakana = 'ル', EnglishCategory = "ru", Romaji = "ru", KoreanPronunciation = "루" } },
            { 730, new CharData { Hiragana = 'れ', Katakana = 'レ', EnglishCategory = "re", Romaji = "re", KoreanPronunciation = "레" } },
            { 740, new CharData { Hiragana = 'ろ', Katakana = 'ロ', EnglishCategory = "ro", Romaji = "ro", KoreanPronunciation = "로" } },
            { 800, new CharData { Hiragana = 'や', Katakana = 'ヤ', EnglishCategory = "ya", Romaji = "ya", KoreanPronunciation = "야" } },
            { 820, new CharData { Hiragana = 'ゆ', Katakana = 'ユ', EnglishCategory = "yu", Romaji = "yu", KoreanPronunciation = "유" } },
            { 840, new CharData { Hiragana = 'よ', Katakana = 'ヨ', EnglishCategory = "yo", Romaji = "yo", KoreanPronunciation = "요" } },
            { 900, new CharData { Hiragana = 'わ', Katakana = 'ワ', EnglishCategory = "wa", Romaji = "wa", KoreanPronunciation = "와" } },
            // { 910, new CharData { Hiragana = 'ゐ', Katakana = 'ヰ', EnglishCategory = "wi", Romaji = "wi", KoreanPronunciation = "위" } },  //910->902
            { 920, new CharData { Hiragana = 'ん', Katakana = 'ン', EnglishCategory = "ng", Romaji = "n", KoreanPronunciation = "응" } },   //1000->920
            // { 930, new CharData { Hiragana = 'ゑ', Katakana = 'ヱ', EnglishCategory = "we", Romaji = "we", KoreanPronunciation = "웨" } },  //930->942
            { 940, new CharData { Hiragana = 'を', Katakana = 'ヲ', EnglishCategory = "wo", Romaji = "wo", KoreanPronunciation = "오" } },

            // 탁음 (1)
            { 021, new CharData { Hiragana = 'ゔ', Katakana = 'ヴ', EnglishCategory = "vu", Romaji = "vu", KoreanPronunciation = "브" } },
            { 101, new CharData { Hiragana = 'が', Katakana = 'ガ', EnglishCategory = "ga", Romaji = "ga", KoreanPronunciation = "가" } },
            { 111, new CharData { Hiragana = 'ぎ', Katakana = 'ギ', EnglishCategory = "gi", Romaji = "gi", KoreanPronunciation = "기" } },
            { 121, new CharData { Hiragana = 'ぐ', Katakana = 'グ', EnglishCategory = "gu", Romaji = "gu", KoreanPronunciation = "구" } },
            { 131, new CharData { Hiragana = 'げ', Katakana = 'ゲ', EnglishCategory = "ge", Romaji = "ge", KoreanPronunciation = "게" } },
            { 141, new CharData { Hiragana = 'ご', Katakana = 'ゴ', EnglishCategory = "go", Romaji = "go", KoreanPronunciation = "고" } },
            { 201, new CharData { Hiragana = 'ざ', Katakana = 'ザ', EnglishCategory = "za", Romaji = "za", KoreanPronunciation = "자" } },
            { 211, new CharData { Hiragana = 'じ', Katakana = 'ジ', EnglishCategory = "zi", Romaji = "ji", KoreanPronunciation = "지" } },
            { 221, new CharData { Hiragana = 'ず', Katakana = 'ズ', EnglishCategory = "zu", Romaji = "zu", KoreanPronunciation = "즈" } },
            { 231, new CharData { Hiragana = 'ぜ', Katakana = 'ゼ', EnglishCategory = "ze", Romaji = "ze", KoreanPronunciation = "제" } },
            { 241, new CharData { Hiragana = 'ぞ', Katakana = 'ゾ', EnglishCategory = "zo", Romaji = "zo", KoreanPronunciation = "조" } },
            { 301, new CharData { Hiragana = 'だ', Katakana = 'ダ', EnglishCategory = "da", Romaji = "da", KoreanPronunciation = "다" } },
            { 311, new CharData { Hiragana = 'ぢ', Katakana = 'ヂ', EnglishCategory = "di", Romaji = "ji", KoreanPronunciation = "지" } },
            { 321, new CharData { Hiragana = 'づ', Katakana = 'ヅ', EnglishCategory = "du", Romaji = "zu", KoreanPronunciation = "즈" } },
            { 331, new CharData { Hiragana = 'で', Katakana = 'デ', EnglishCategory = "de", Romaji = "de", KoreanPronunciation = "데" } },
            { 341, new CharData { Hiragana = 'ど', Katakana = 'ド', EnglishCategory = "do", Romaji = "do", KoreanPronunciation = "도" } },
            { 401, new CharData { Hiragana = 'ば', Katakana = 'バ', EnglishCategory = "ba", Romaji = "ba", KoreanPronunciation = "바" } },
            { 411, new CharData { Hiragana = 'び', Katakana = 'ビ', EnglishCategory = "bi", Romaji = "bi", KoreanPronunciation = "비" } },
            { 421, new CharData { Hiragana = 'ぶ', Katakana = 'ブ', EnglishCategory = "bu", Romaji = "bu", KoreanPronunciation = "부" } },
            { 431, new CharData { Hiragana = 'べ', Katakana = 'ベ', EnglishCategory = "be", Romaji = "be", KoreanPronunciation = "베" } },
            { 441, new CharData { Hiragana = 'ぼ', Katakana = 'ボ', EnglishCategory = "bo", Romaji = "bo", KoreanPronunciation = "보" } },

            // 반탁음 (2) : わ행 청음인 ゐ와 ゑ의 키보드 입력을 위하여 わ와 を의 반탁음으로 추가함. YN전환키 기능으로 입력 가능해짐.
            { 402, new CharData { Hiragana = 'ぱ', Katakana = 'パ', EnglishCategory = "pa", Romaji = "pa", KoreanPronunciation = "파" } },
            { 412, new CharData { Hiragana = 'ぴ', Katakana = 'ピ', EnglishCategory = "pi", Romaji = "pi", KoreanPronunciation = "피" } },
            { 422, new CharData { Hiragana = 'ぷ', Katakana = 'プ', EnglishCategory = "pu", Romaji = "pu", KoreanPronunciation = "푸" } },
            { 432, new CharData { Hiragana = 'ぺ', Katakana = 'ペ', EnglishCategory = "pe", Romaji = "pe", KoreanPronunciation = "페" } },
            { 442, new CharData { Hiragana = 'ぽ', Katakana = 'ポ', EnglishCategory = "po", Romaji = "po", KoreanPronunciation = "포" } },
            { 902, new CharData { Hiragana = 'ゐ', Katakana = 'ヰ', EnglishCategory = "wi", Romaji = "wi", KoreanPronunciation = "위" } },  //910->902로 변경함
            { 942, new CharData { Hiragana = 'ゑ', Katakana = 'ヱ', EnglishCategory = "we", Romaji = "we", KoreanPronunciation = "웨" } },  //930->942로 변경함

            // 스테가나 (3) : 요음, 촉음 등 작은글씨, 문자앞에 x를 추가함
            { 003, new CharData { Hiragana = 'ぁ', Katakana = 'ァ', EnglishCategory = "xaa", Romaji = "xa", KoreanPronunciation = "아" } },
            { 013, new CharData { Hiragana = 'ぃ', Katakana = 'ィ', EnglishCategory = "xai", Romaji = "xi", KoreanPronunciation = "이" } },
            { 023, new CharData { Hiragana = 'ぅ', Katakana = 'ゥ', EnglishCategory = "xau", Romaji = "xu", KoreanPronunciation = "우" } },
            { 033, new CharData { Hiragana = 'ぇ', Katakana = 'ェ', EnglishCategory = "xae", Romaji = "xe", KoreanPronunciation = "에" } },
            { 043, new CharData { Hiragana = 'ぉ', Katakana = 'ォ', EnglishCategory = "xao", Romaji = "xo", KoreanPronunciation = "오" } },
            { 103, new CharData { Hiragana = 'ゕ', Katakana = 'ヵ', EnglishCategory = "xka", Romaji = "xka", KoreanPronunciation = "카" } },
            { 133, new CharData { Hiragana = 'ゖ', Katakana = 'ヶ', EnglishCategory = "xke", Romaji = "xke", KoreanPronunciation = "케" } },
            { 323, new CharData { Hiragana = 'っ', Katakana = 'ッ', EnglishCategory = "xtu", Romaji = "xtsu", KoreanPronunciation = "ㅅ" } }, //촉음은 한국어 받침 소리
            { 803, new CharData { Hiragana = 'ゃ', Katakana = 'ャ', EnglishCategory = "xya", Romaji = "xya", KoreanPronunciation = "야" } },
            { 823, new CharData { Hiragana = 'ゅ', Katakana = 'ュ', EnglishCategory = "xyu", Romaji = "xyu", KoreanPronunciation = "유" } },
            { 843, new CharData { Hiragana = 'ょ', Katakana = 'ョ', EnglishCategory = "xyo", Romaji = "xyo", KoreanPronunciation = "요" } },
            { 903, new CharData { Hiragana = 'ゎ', Katakana = 'ヮ', EnglishCategory = "xwa", Romaji = "xwa", KoreanPronunciation = "와" } },
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