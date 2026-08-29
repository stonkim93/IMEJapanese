#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IMEJapanese
{
    /// <summary>
    /// 일본어 문자를 3자리 숫자 코드로 효율적으로 표현하는 읽기 전용(Readonly) 구조체
    /// 메모리 복사 오버헤드를 최소화하기 위해 내부 캐싱 필드를 제거하고 단 2바이트만 차지하도록 최적화됨.
    /// 
    /// 코드 구조: XXX (3자리)
    /// - 100의 자리: 형태 마크 (0=청음, 1=탁음, 2=반탁음, 3=스테가나, 4=숫자/기호)
    /// - 10의 자리: 자음 그룹 (0=a, 1=k, 2=s, 3=t, 4=h, 5=n, 6=m, 7=r, 8=y, 9=w)
    /// - 1의 자리: 모음 인덱스 (0~4=히라가나 a,i,u,e,o / 5~9=가타카나 a,i,u,e,o)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct JapaneseCharacter : IEquatable<JapaneseCharacter>
    {
        public ushort Code { get; }

        public JapaneseCharacter(ushort code)
        {
            Code = code;
        }

        public byte Voicing => (byte)(Code / 100);               // 100의 자리 : 형태 마크
        public byte ConsonantGroup => (byte)((Code / 10) % 10);  // 10의 자리 : 자음 그룹
        public byte VowelIndex => (byte)(Code % 10);             // 1의 자리 : 모음 인덱스

        // O(1) 배열 접근을 통해 즉시 데이터를 가져오므로 내부 캐싱이 불필요함
        public char Hiragana => CharacterDatabase.GetCharacterData(Code).Hiragana;
        public char Katakana => CharacterDatabase.GetCharacterData(Code).Katakana;
        public string Romaji => CharacterDatabase.GetCharacterData(Code).Romaji;
        public string KoreanPron => CharacterDatabase.GetCharacterData(Code).KoreanPron;

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
            return new JapaneseCharacter(code);
        }

        public static JapaneseCharacter FromHiragana(char hiragana) => FromCode(CharacterDatabase.GetCodeFromHiragana(hiragana));
        public static JapaneseCharacter FromKatakana(char katakana) => FromCode(CharacterDatabase.GetCodeFromKatakana(katakana));

        public JapaneseCharacter ToSeion()
        {
            if (Code >= 400) return this; 
            if (IsSeion) return this;
            return FromCode((ushort)(Code % 100)); 
        }

        public JapaneseCharacter NextVoicing()
        {
            if (Code >= 400) return this; 

            // 특수 YN 변환 규칙 (わ ↔ ゐ, を ↔ ゑ)
            if (Code == 090) return FromCode(091);
            if (Code == 091) return FromCode(090);
            if (Code == 094) return FromCode(093);
            if (Code == 093) return FromCode(094);

            byte currentVoicing = Voicing;
            
            for (int i = 1; i <= 4; i++)
            {
                byte nextVoicing = (byte)((currentVoicing + i) % 4);
                ushort nextCode = (ushort)(nextVoicing * 100 + (Code % 100));
                
                if (CharacterDatabase.ContainsCode(nextCode))
                {
                    return FromCode(nextCode);
                }
            }

            return this; 
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
        // [최적화] Dictionary 대신 배열을 사용하여 O(1) 상수 시간 접근 (가장 빠른 속도 보장)
        // 최대 코드가 440대이므로 크기를 500으로 설정
        private static readonly CharData?[] DataArray = new CharData?[500];
        private static readonly Dictionary<char, ushort> HiraganaToCode = new();
        private static readonly Dictionary<char, ushort> KatakanaToCode = new();

        static CharacterDatabase()
        {
            // 초기 데이터 정의
            var baseData = new Dictionary<ushort, CharData>
            {
                { 000, new CharData { Hiragana = 'あ', Katakana = 'ア', EngCategory = "aa", Romaji = "a", KoreanPron = "아" } },
                { 001, new CharData { Hiragana = 'い', Katakana = 'イ', EngCategory = "ai", Romaji = "i", KoreanPron = "이" } },
                { 002, new CharData { Hiragana = 'う', Katakana = 'ウ', EngCategory = "au", Romaji = "u", KoreanPron = "우" } },
                { 003, new CharData { Hiragana = 'え', Katakana = 'エ', EngCategory = "ae", Romaji = "e", KoreanPron = "에" } },
                { 004, new CharData { Hiragana = 'お', Katakana = 'オ', EngCategory = "ao", Romaji = "o", KoreanPron = "오" } },
                { 010, new CharData { Hiragana = 'か', Katakana = 'カ', EngCategory = "ka", Romaji = "ka", KoreanPron = "카" } },
                { 011, new CharData { Hiragana = 'き', Katakana = 'キ', EngCategory = "ki", Romaji = "ki", KoreanPron = "키" } },
                { 012, new CharData { Hiragana = 'く', Katakana = 'ク', EngCategory = "ku", Romaji = "ku", KoreanPron = "쿠" } },
                { 013, new CharData { Hiragana = 'け', Katakana = 'ケ', EngCategory = "ke", Romaji = "ke", KoreanPron = "케" } },
                { 014, new CharData { Hiragana = 'こ', Katakana = 'コ', EngCategory = "ko", Romaji = "ko", KoreanPron = "코" } },
                { 020, new CharData { Hiragana = 'さ', Katakana = 'サ', EngCategory = "sa", Romaji = "sa", KoreanPron = "사" } },
                { 021, new CharData { Hiragana = 'し', Katakana = 'シ', EngCategory = "si", Romaji = "shi", KoreanPron = "시" } },
                { 022, new CharData { Hiragana = 'す', Katakana = 'ス', EngCategory = "su", Romaji = "su", KoreanPron = "스" } },
                { 023, new CharData { Hiragana = 'せ', Katakana = 'セ', EngCategory = "se", Romaji = "se", KoreanPron = "세" } },
                { 024, new CharData { Hiragana = 'そ', Katakana = 'ソ', EngCategory = "so", Romaji = "so", KoreanPron = "소" } },
                { 030, new CharData { Hiragana = 'た', Katakana = 'タ', EngCategory = "ta", Romaji = "ta", KoreanPron = "타" } },
                { 031, new CharData { Hiragana = 'ち', Katakana = 'チ', EngCategory = "ti", Romaji = "chi", KoreanPron = "치" } },
                { 032, new CharData { Hiragana = 'つ', Katakana = 'ツ', EngCategory = "tu", Romaji = "tsu", KoreanPron = "츠" } },
                { 033, new CharData { Hiragana = 'て', Katakana = 'テ', EngCategory = "te", Romaji = "te", KoreanPron = "테" } },
                { 034, new CharData { Hiragana = 'と', Katakana = 'ト', EngCategory = "to", Romaji = "to", KoreanPron = "토" } },
                { 040, new CharData { Hiragana = 'は', Katakana = 'ハ', EngCategory = "ha", Romaji = "ha", KoreanPron = "하" } },
                { 041, new CharData { Hiragana = 'ひ', Katakana = 'ヒ', EngCategory = "hi", Romaji = "hi", KoreanPron = "히" } },
                { 042, new CharData { Hiragana = 'ふ', Katakana = 'フ', EngCategory = "hu", Romaji = "fu", KoreanPron = "후" } },
                { 043, new CharData { Hiragana = 'へ', Katakana = 'ヘ', EngCategory = "he", Romaji = "he", KoreanPron = "헤" } },
                { 044, new CharData { Hiragana = 'ほ', Katakana = 'ホ', EngCategory = "ho", Romaji = "ho", KoreanPron = "호" } },
                { 050, new CharData { Hiragana = 'な', Katakana = 'ナ', EngCategory = "na", Romaji = "na", KoreanPron = "나" } },
                { 051, new CharData { Hiragana = 'に', Katakana = 'ニ', EngCategory = "ni", Romaji = "ni", KoreanPron = "니" } },
                { 052, new CharData { Hiragana = 'ぬ', Katakana = 'ヌ', EngCategory = "nu", Romaji = "nu", KoreanPron = "누" } },
                { 053, new CharData { Hiragana = 'ね', Katakana = 'ネ', EngCategory = "ne", Romaji = "ne", KoreanPron = "네" } },
                { 054, new CharData { Hiragana = 'の', Katakana = 'ノ', EngCategory = "no", Romaji = "no", KoreanPron = "노" } },
                { 060, new CharData { Hiragana = 'ま', Katakana = 'マ', EngCategory = "ma", Romaji = "ma", KoreanPron = "마" } },
                { 061, new CharData { Hiragana = 'み', Katakana = 'ミ', EngCategory = "mi", Romaji = "mi", KoreanPron = "미" } },
                { 062, new CharData { Hiragana = 'む', Katakana = 'ム', EngCategory = "mu", Romaji = "mu", KoreanPron = "무" } },
                { 063, new CharData { Hiragana = 'め', Katakana = 'メ', EngCategory = "me", Romaji = "me", KoreanPron = "메" } },
                { 064, new CharData { Hiragana = 'も', Katakana = 'モ', EngCategory = "mo", Romaji = "mo", KoreanPron = "모" } },
                { 070, new CharData { Hiragana = 'ら', Katakana = 'ラ', EngCategory = "ra", Romaji = "ra", KoreanPron = "라" } },
                { 071, new CharData { Hiragana = 'り', Katakana = 'リ', EngCategory = "ri", Romaji = "ri", KoreanPron = "리" } },
                { 072, new CharData { Hiragana = 'る', Katakana = 'ル', EngCategory = "ru", Romaji = "ru", KoreanPron = "루" } },
                { 073, new CharData { Hiragana = 'れ', Katakana = 'レ', EngCategory = "re", Romaji = "re", KoreanPron = "레" } },
                { 074, new CharData { Hiragana = 'ろ', Katakana = 'ロ', EngCategory = "ro", Romaji = "ro", KoreanPron = "로" } },
                { 080, new CharData { Hiragana = 'や', Katakana = 'ヤ', EngCategory = "ya", Romaji = "ya", KoreanPron = "야" } },
                { 082, new CharData { Hiragana = 'ゆ', Katakana = 'ユ', EngCategory = "yu", Romaji = "yu", KoreanPron = "유" } },
                { 084, new CharData { Hiragana = 'よ', Katakana = 'ヨ', EngCategory = "yo", Romaji = "yo", KoreanPron = "요" } },
                { 090, new CharData { Hiragana = 'わ', Katakana = 'ワ', EngCategory = "wa", Romaji = "wa", KoreanPron = "와" } },
                { 091, new CharData { Hiragana = 'ゐ', Katakana = 'ヰ', EngCategory = "wi", Romaji = "wi", KoreanPron = "위" } },
                { 092, new CharData { Hiragana = 'ん', Katakana = 'ン', EngCategory = "wu", Romaji = "ng", KoreanPron = "응" } },
                { 093, new CharData { Hiragana = 'ゑ', Katakana = 'ヱ', EngCategory = "we", Romaji = "we", KoreanPron = "웨" } },
                { 094, new CharData { Hiragana = 'を', Katakana = 'ヲ', EngCategory = "wo", Romaji = "wo", KoreanPron = "오" } },

                // 탁음 (1)
                { 102, new CharData { Hiragana = 'ゔ', Katakana = 'ヴ', EngCategory = "vu", Romaji = "vu", KoreanPron = "브" } },
                { 110, new CharData { Hiragana = 'が', Katakana = 'ガ', EngCategory = "ga", Romaji = "ga", KoreanPron = "가" } },
                { 111, new CharData { Hiragana = 'ぎ', Katakana = 'ギ', EngCategory = "gi", Romaji = "gi", KoreanPron = "기" } },
                { 112, new CharData { Hiragana = 'ぐ', Katakana = 'グ', EngCategory = "gu", Romaji = "gu", KoreanPron = "구" } },
                { 113, new CharData { Hiragana = 'げ', Katakana = 'ゲ', EngCategory = "ge", Romaji = "ge", KoreanPron = "게" } },
                { 114, new CharData { Hiragana = 'ご', Katakana = 'ゴ', EngCategory = "go", Romaji = "go", KoreanPron = "고" } },
                { 120, new CharData { Hiragana = 'ざ', Katakana = 'ザ', EngCategory = "za", Romaji = "za", KoreanPron = "자" } },
                { 121, new CharData { Hiragana = 'じ', Katakana = 'ジ', EngCategory = "zi", Romaji = "ji", KoreanPron = "지" } },
                { 122, new CharData { Hiragana = 'ず', Katakana = 'ズ', EngCategory = "zu", Romaji = "zu", KoreanPron = "즈" } },
                { 123, new CharData { Hiragana = 'ぜ', Katakana = 'ゼ', EngCategory = "ze", Romaji = "ze", KoreanPron = "제" } },
                { 124, new CharData { Hiragana = 'ぞ', Katakana = 'ゾ', EngCategory = "zo", Romaji = "zo", KoreanPron = "조" } },
                { 130, new CharData { Hiragana = 'だ', Katakana = 'ダ', EngCategory = "da", Romaji = "da", KoreanPron = "다" } },
                { 131, new CharData { Hiragana = 'ぢ', Katakana = 'ヂ', EngCategory = "di", Romaji = "ji", KoreanPron = "디" } },
                { 132, new CharData { Hiragana = 'づ', Katakana = 'ヅ', EngCategory = "du", Romaji = "zu", KoreanPron = "드" } },
                { 133, new CharData { Hiragana = 'で', Katakana = 'デ', EngCategory = "de", Romaji = "de", KoreanPron = "데" } },
                { 134, new CharData { Hiragana = 'ど', Katakana = 'ド', EngCategory = "do", Romaji = "do", KoreanPron = "도" } },
                { 140, new CharData { Hiragana = 'ば', Katakana = 'バ', EngCategory = "ba", Romaji = "ba", KoreanPron = "바" } },
                { 141, new CharData { Hiragana = 'び', Katakana = 'ビ', EngCategory = "bi", Romaji = "bi", KoreanPron = "비" } },
                { 142, new CharData { Hiragana = 'ぶ', Katakana = 'ブ', EngCategory = "bu", Romaji = "bu", KoreanPron = "부" } },
                { 143, new CharData { Hiragana = 'べ', Katakana = 'ベ', EngCategory = "be", Romaji = "be", KoreanPron = "베" } },
                { 144, new CharData { Hiragana = 'ぼ', Katakana = 'ボ', EngCategory = "bo", Romaji = "bo", KoreanPron = "보" } },

                // 반탁음 (2)
                { 240, new CharData { Hiragana = 'ぱ', Katakana = 'パ', EngCategory = "pa", Romaji = "pa", KoreanPron = "파" } },
                { 241, new CharData { Hiragana = 'ぴ', Katakana = 'ピ', EngCategory = "pi", Romaji = "pi", KoreanPron = "피" } },
                { 242, new CharData { Hiragana = 'ぷ', Katakana = 'プ', EngCategory = "pu", Romaji = "pu", KoreanPron = "푸" } },
                { 243, new CharData { Hiragana = 'ぺ', Katakana = 'ペ', EngCategory = "pe", Romaji = "pe", KoreanPron = "페" } },
                { 244, new CharData { Hiragana = 'ぽ', Katakana = 'ポ', EngCategory = "po", Romaji = "po", KoreanPron = "포" } },

                // 스테가나 (3)
                { 300, new CharData { Hiragana = 'ぁ', Katakana = 'ァ', EngCategory = "xaa", Romaji = "xa", KoreanPron = "아" } },
                { 301, new CharData { Hiragana = 'ぃ', Katakana = 'ィ', EngCategory = "xai", Romaji = "xi", KoreanPron = "이" } },
                { 302, new CharData { Hiragana = 'ぅ', Katakana = 'ゥ', EngCategory = "xau", Romaji = "xu", KoreanPron = "우" } },
                { 303, new CharData { Hiragana = 'ぇ', Katakana = 'ェ', EngCategory = "xae", Romaji = "xe", KoreanPron = "에" } },
                { 304, new CharData { Hiragana = 'ぉ', Katakana = 'ォ', EngCategory = "xao", Romaji = "xo", KoreanPron = "오" } },
                { 310, new CharData { Hiragana = 'ゕ', Katakana = 'ヵ', EngCategory = "xka", Romaji = "xka", KoreanPron = "카" } },
                { 313, new CharData { Hiragana = 'ゖ', Katakana = 'ヶ', EngCategory = "xke", Romaji = "xke", KoreanPron = "케" } },
                { 332, new CharData { Hiragana = 'っ', Katakana = 'ッ', EngCategory = "xtu", Romaji = "xtsu", KoreanPron = "ㅅ" } },
                { 380, new CharData { Hiragana = 'ゃ', Katakana = 'ャ', EngCategory = "xya", Romaji = "xya", KoreanPron = "야" } },
                { 382, new CharData { Hiragana = 'ゅ', Katakana = 'ュ', EngCategory = "xyu", Romaji = "xyu", KoreanPron = "유" } },
                { 384, new CharData { Hiragana = 'ょ', Katakana = 'ョ', EngCategory = "xyo", Romaji = "xyo", KoreanPron = "요" } },
                { 390, new CharData { Hiragana = 'ゎ', Katakana = 'ヮ', EngCategory = "xwa", Romaji = "xwa", KoreanPron = "와" } },

                // 숫자 및 특수기호 (4)
                { 400, new CharData { Hiragana = '0' , Katakana = ')' , EngCategory = "0", Romaji = ")", KoreanPron = "number" } },
                { 401, new CharData { Hiragana = '1' , Katakana = '!' , EngCategory = "1", Romaji = "!", KoreanPron = "number" } },
                { 402, new CharData { Hiragana = '2' , Katakana = '@' , EngCategory = "2", Romaji = "@", KoreanPron = "number" } },
                { 403, new CharData { Hiragana = '3' , Katakana = '#' , EngCategory = "3", Romaji = "#", KoreanPron = "number" } },
                { 404, new CharData { Hiragana = '4' , Katakana = '$' , EngCategory = "4", Romaji = "$", KoreanPron = "number" } },

                { 410, new CharData { Hiragana = '5' , Katakana = '%' , EngCategory = "5", Romaji = "%", KoreanPron = "number" } },
                { 411, new CharData { Hiragana = '6' , Katakana = '^' , EngCategory = "6", Romaji = "^", KoreanPron = "number" } },
                { 412, new CharData { Hiragana = '7' , Katakana = '&' , EngCategory = "7", Romaji = "&", KoreanPron = "number" } },
                { 413, new CharData { Hiragana = '8' , Katakana = '*' , EngCategory = "8", Romaji = "*", KoreanPron = "number" } },
                { 414, new CharData { Hiragana = '9' , Katakana = '(' , EngCategory = "9", Romaji = "(", KoreanPron = "number" } },

                { 420, new CharData { Hiragana = ',' , Katakana = 'ー', EngCategory = ",", Romaji = "<", KoreanPron = "symbol" } },
                { 421, new CharData { Hiragana = '.' , Katakana = '・', EngCategory = ".", Romaji = ">", KoreanPron = "symbol" } },
                { 422, new CharData { Hiragana = '々', Katakana = '～', EngCategory = "`", Romaji = "~", KoreanPron = "symbol" } },
                { 423, new CharData { Hiragana = '。', Katakana = '?' , EngCategory = "/", Romaji = "?", KoreanPron = "symbol" } },
                { 424, new CharData { Hiragana = '、', Katakana = ':' , EngCategory = ";", Romaji = ":", KoreanPron = "symbol" } },

                { 430, new CharData { Hiragana = '「', Katakana = '『', EngCategory = "[",  Romaji = "{", KoreanPron = "symbol" } },
                { 431, new CharData { Hiragana = '」', Katakana = '』', EngCategory = "]",  Romaji = "}", KoreanPron = "symbol" } },
                { 432, new CharData { Hiragana = '円' , Katakana = '¥', EngCategory = "\\", Romaji = "|", KoreanPron = "symbol" } },
                { 433, new CharData { Hiragana = '-' , Katakana = '_' , EngCategory = "-",  Romaji = "_", KoreanPron = "symbol" } },
                { 434, new CharData { Hiragana = '=' , Katakana = '+' , EngCategory = "=",  Romaji = "+", KoreanPron = "symbol" } },
                { 440, new CharData { Hiragana = '\'', Katakana = '\"', EngCategory = "\'", Romaji = "\"",KoreanPron = "symbol" } }

                /* 영어
                { 441, new CharData { Hiragana = ' ' , Katakana = '〆', EngCategory = " ", Romaji = "々", KoreanPron = "symbol" } },
                { 442, new CharData { Hiragana = '…' , Katakana = '※', EngCategory = "…", Romaji = "※", KoreanPron = "symbol" } },
                { 443, new CharData { Hiragana = '【', Katakana = '】', EngCategory = "【", Romaji = "】", KoreanPron = "symbol" } },

                { 444, new CharData { Hiragana = 'a' , Katakana = 'A' , EngCategory = "a", Romaji = "A", KoreanPron = "english" } },
                { 450, new CharData { Hiragana = 'b' , Katakana = 'B' , EngCategory = "b", Romaji = "B", KoreanPron = "english" } },
                { 451, new CharData { Hiragana = 'c' , Katakana = 'C' , EngCategory = "c", Romaji = "C", KoreanPron = "english" } },
                { 452, new CharData { Hiragana = 'd' , Katakana = 'D' , EngCategory = "d", Romaji = "D", KoreanPron = "english" } },
                { 453, new CharData { Hiragana = 'e' , Katakana = 'E' , EngCategory = "e", Romaji = "E", KoreanPron = "english" } },
                { 454, new CharData { Hiragana = 'f' , Katakana = 'F' , EngCategory = "f", Romaji = "F", KoreanPron = "english" } },

                { 460, new CharData { Hiragana = 'g' , Katakana = 'G' , EngCategory = "g", Romaji = "G", KoreanPron = "english" } },
                { 461, new CharData { Hiragana = 'h' , Katakana = 'H' , EngCategory = "h", Romaji = "H", KoreanPron = "english" } },
                { 462, new CharData { Hiragana = 'i' , Katakana = 'I' , EngCategory = "i", Romaji = "I", KoreanPron = "english" } },
                { 463, new CharData { Hiragana = 'j' , Katakana = 'J' , EngCategory = "j", Romaji = "J", KoreanPron = "english" } },
                { 464, new CharData { Hiragana = 'k' , Katakana = 'K' , EngCategory = "k", Romaji = "K", KoreanPron = "english" } },

                { 470, new CharData { Hiragana = 'l' , Katakana = 'L' , EngCategory = "l", Romaji = "L", KoreanPron = "english" } },
                { 471, new CharData { Hiragana = 'm' , Katakana = 'M' , EngCategory = "m", Romaji = "M", KoreanPron = "english" } },
                { 472, new CharData { Hiragana = 'n' , Katakana = 'N' , EngCategory = "n", Romaji = "N", KoreanPron = "english" } },
                { 473, new CharData { Hiragana = 'o' , Katakana = 'O' , EngCategory = "o", Romaji = "O", KoreanPron = "english" } },
                { 474, new CharData { Hiragana = 'p' , Katakana = 'P' , EngCategory = "p", Romaji = "P", KoreanPron = "english" } },

                { 480, new CharData { Hiragana = 'q' , Katakana = 'Q' , EngCategory = "q", Romaji = "Q", KoreanPron = "english" } },
                { 481, new CharData { Hiragana = 'r' , Katakana = 'R' , EngCategory = "r", Romaji = "R", KoreanPron = "english" } },
                { 482, new CharData { Hiragana = 's' , Katakana = 'S' , EngCategory = "s", Romaji = "S", KoreanPron = "english" } },
                { 483, new CharData { Hiragana = 't' , Katakana = 'T' , EngCategory = "t", Romaji = "T", KoreanPron = "english" } },
                { 484, new CharData { Hiragana = 'u' , Katakana = 'U' , EngCategory = "u", Romaji = "U", KoreanPron = "english" } },

                { 490, new CharData { Hiragana = 'v' , Katakana = 'V' , EngCategory = "v", Romaji = "V", KoreanPron = "english" } },
                { 491, new CharData { Hiragana = 'w' , Katakana = 'W' , EngCategory = "w", Romaji = "W", KoreanPron = "english" } },
                { 492, new CharData { Hiragana = 'x' , Katakana = 'X' , EngCategory = "x", Romaji = "X", KoreanPron = "english" } },
                { 493, new CharData { Hiragana = 'y' , Katakana = 'Y' , EngCategory = "y", Romaji = "Y", KoreanPron = "english" } },
                { 494, new CharData { Hiragana = 'z' , Katakana = 'Z' , EngCategory = "z", Romaji = "Z", KoreanPron = "english" } },
                */
            };

            foreach (var kvp in baseData)
            {
                ushort hCode = kvp.Key;
                // 가타카나는 1의 자리에 +5
                ushort kCode = (ushort)(hCode + 5);

                // 배열에 직접 바인딩하여 O(1) 검색 지원
                DataArray[hCode] = kvp.Value;
                DataArray[kCode] = kvp.Value;

                // 역방향 검색 딕셔너리 구성
                HiraganaToCode[kvp.Value.Hiragana] = hCode;
                KatakanaToCode[kvp.Value.Katakana] = kCode;
            }
        }

        public static bool ContainsCode(ushort code) => code < 500 && DataArray[code] != null;
        
        public static CharData GetCharacterData(ushort code)
        {
            if (code < 500 && DataArray[code] != null) return DataArray[code]!;
            throw new ArgumentException($"알 수 없는 문자 코드: {code:000}");
        }

        public static ushort GetCodeFromChar(char ch)
        {
            if (HiraganaToCode.TryGetValue(ch, out var code)) return code;
            if (KatakanaToCode.TryGetValue(ch, out code)) return code;
            throw new ArgumentException($"알 수 없는 문자: {ch}");
        }

        public static ushort GetCodeFromHiragana(char hiragana)
        {
            if (HiraganaToCode.TryGetValue(hiragana, out var code)) return code;
            throw new ArgumentException($"알 수 없는 히라가나/기본문자: {hiragana}");
        }

        public static ushort GetCodeFromKatakana(char katakana)
        {
            if (KatakanaToCode.TryGetValue(katakana, out var code)) return code;
            throw new ArgumentException($"알 수 없는 카타카나/확장문자: {katakana}");
        }

        public static bool IsValidCharacter(char ch) => HiraganaToCode.ContainsKey(ch) || KatakanaToCode.ContainsKey(ch);
        public static bool IsHiraganaChar(char ch) => HiraganaToCode.ContainsKey(ch);
        public static bool IsKatakanaChar(char ch) => KatakanaToCode.ContainsKey(ch);
    }

    public static class JapaneseCharacterProcessor
    {
        public static string ProcessHK(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var result = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (CharacterDatabase.IsValidCharacter(ch))
                {
                    ushort code = CharacterDatabase.GetCodeFromChar(ch);
                    
                    if (code < 400)
                    {
                        bool isHiragana = CharacterDatabase.IsHiraganaChar(ch);
                        var jpChar = isHiragana ? JapaneseCharacter.FromHiragana(ch) : JapaneseCharacter.FromKatakana(ch);
                        result.Append(isHiragana ? jpChar.Katakana : jpChar.Hiragana);
                        continue;
                    }
                }
                result.Append(ch);
            }
            return result.ToString();
        }

        public static string ProcessYN(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var result = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (CharacterDatabase.IsValidCharacter(ch))
                {
                    ushort code = CharacterDatabase.GetCodeFromChar(ch);
                    
                    if (code < 400)
                    {
                        bool isHiragana = CharacterDatabase.IsHiraganaChar(ch);
                        var jpChar = isHiragana ? JapaneseCharacter.FromHiragana(ch) : JapaneseCharacter.FromKatakana(ch);
                        
                        var nextChar = jpChar.NextVoicing();
                        result.Append(isHiragana ? nextChar.Hiragana : nextChar.Katakana);
                        continue;
                    }
                }
                result.Append(ch);
            }
            return result.ToString();
        }

        public static string ToHiragana(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var result = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (CharacterDatabase.IsValidCharacter(ch))
                {
                    ushort code = CharacterDatabase.GetCodeFromChar(ch);
                    
                    if (code < 400)
                    {
                        bool isHiragana = CharacterDatabase.IsHiraganaChar(ch);
                        var jpChar = isHiragana ? JapaneseCharacter.FromHiragana(ch) : JapaneseCharacter.FromKatakana(ch);
                        result.Append(jpChar.Hiragana);
                        continue;
                    }
                }
                result.Append(ch);
            }
            return result.ToString();
        }
    }
}