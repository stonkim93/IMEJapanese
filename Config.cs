// Config.cs - IMEJapanese 설정 통합 파일
#nullable enable
using System.Collections.Generic;
using System.Drawing;

namespace IMEJapanese
{
    #region [ 사용자 설정 영역 (AppConfig) ]
    internal static class AppConfig
    {
        // --------------------------------------------------------
        // 디버깅 및 시스템 성능 제어
        // --------------------------------------------------------
        /// <summary>0: 기록 중지, 1: 중요사항(오류) 기록, 2: 모든 기록 지속(디버그용)</summary>
        public static int LogLevel = 1; 
        
        /// <summary>현재 입력 상태 등을 감지하는 주기(ms). 너무 짧으면 CPU 점유율이 상승할 수 있습니다.</summary>
        public static int PollingInterval = 100;

        // --------------------------------------------------------
        // 화면 오버레이(알림창) UI 시각적 설정
        // --------------------------------------------------------
        /// <summary>텍스트 오버레이가 화면에 표시되는 기본 지속 시간(ms)</summary>
        public const int OverlayDefaultDurationMs = 1500;
        
        /// <summary>오버레이 알림 텍스트의 폰트 크기</summary>
        public const float OverlayDefaultFontSize = 29f;
        
        /// <summary>오버레이 창의 전체 높이 픽셀</summary>
        public const int OverlayDefaultHeight = 52;
        
        /// <summary>각 글자가 차지하는 예상 너비 픽셀 (문자열 길이에 따른 전체 너비 계산용)</summary>
        public const int OverlayDefaultCharWidth = 30;
        
        /// <summary>오버레이 창 텍스트의 좌우 여백(Padding)</summary>
        public const int OverlayDefaultPaddingWidth = 24;
        
        /// <summary>오버레이 창이 화면에 나타날 때 마우스 커서 또는 텍스트 입력 커서로부터 Y축(아래쪽)으로 띄울 간격</summary>
        public const int OverlayDefaultYOffset = 40;

        // --------------------------------------------------------
        // 작업 표시줄(시스템 트레이) 아이콘 시각적 설정
        // --------------------------------------------------------
        /// <summary>트레이 아이콘의 렌더링 크기</summary>
        public const int TrayIconSize = 32;
        /// <summary>트레이 아이콘에 표시될 영문 소문자의 폰트 크기</summary>
        public const float TrayLowercaseFontSize = 31F;
        /// <summary>트레이 아이콘에 표시될 영문 대문자의 폰트 크기</summary>
        public const float TrayUppercaseFontSize = 32F;

        // --------------------------------------------------------
        // 한자 변환 알고리즘 및 텍스트 제한 설정
        // --------------------------------------------------------
        /// <summary>한 번에 한자 변환(Space 키 등)을 시도할 최대 문자열 길이 제한</summary>
        public static int MaxKanjiConversionLength = 20;

        // --------------------------------------------------------
        // 컨텍스트 메뉴(트레이 우클릭 메뉴) 활성화 옵션
        // --------------------------------------------------------
#if ENABLE_CAPS_Japanese1
        public static bool ShowCapsJapanese1 = true;
#else
        public static bool ShowCapsJapanese1 = false;
#endif

#if ENABLE_CAPS_Japanese2
        public static bool ShowCapsJapanese2 = true;
#else
        public static bool ShowCapsJapanese2 = false;
#endif

#if ENABLE_CAPS_Japanese3
        public static bool ShowCapsJapanese3 = true;
#else
        public static bool ShowCapsJapanese3 = false;
#endif

#if ENABLE_KEYBOARD_LAYOUT
        public static bool ShowKeyboardlayoutMenu = true;
#else
        public static bool ShowKeyboardlayoutMenu = false;
#endif

        /// <summary>트레이 메뉴에서 '입력모드 알림 표시' 항목 노출 여부</summary>
        public static bool ShowTextOverlayMenu = true;
        
        /// <summary>트레이 메뉴에서 'Copilot 맵핑' 항목 노출 여부</summary>
        public static bool ShowCopilotMapMenu = true;

        // --------------------------------------------------------
        // 프로그램 최초 실행 시 기본 작동 상태 지정
        // --------------------------------------------------------
        /// <summary>앱 시작 시 기본으로 선택되어 있을 Caps Lock 일본어 모드 (1, 2, 3 중 선택)</summary>
        public static int DefaultCapsMode = 1;
        
        /// <summary>앱 시작 시 자판 배열창 팝업을 기본으로 띄울지 여부</summary>
        public static bool DefaultShowKeyboardLayout = true;
        
        /// <summary>앱 시작 시 입력모드 전환 오버레이(화면 알림) 기능을 기본으로 활성화할지 여부</summary>
        public static bool DefaultShowTextOverlay = true;
        
        /// <summary>앱 시작 시 Copilot 맵핑을 기본으로 활성화할지 여부</summary>
        public static bool DefaultEnableCopilotMap = false;
        
        /// <summary>현재 실행 중인 Copilot 맵핑 활성화 상태 값</summary>
        public static bool EnableCopilotMap = DefaultEnableCopilotMap;

        // --------------------------------------------------------
        // 오프라인/온라인 한자 변환 설정
        // --------------------------------------------------------
        /// <summary>로컬 SQLite DB(Mozc)를 이용한 한자 변환 사용 여부</summary>
        public static bool EnableLocalConversion = true;
        
        /// <summary>로컬 변환에 실패했을 때 Google 온라인 한자 변환 API를 사용할지 여부</summary>
        public static bool UseGoogleApi { get; set; } = true;

        // --------------------------------------------------------
        // Viterbi 한자 변환 알고리즘 파라미터 (고급)
        // --------------------------------------------------------
        /// <summary>동적 계획법 탐색 시 유지할 상위 노드의 개수(Beam Width). 높을수록 정확하나 연산 증가.</summary>
        public static int ViterbiBeamWidth = 6;
        /// <summary>Viterbi 알고리즘 종료 시 묶어서 반환할 최종 최적 경로(후보)의 개수</summary>
        public static int ViterbiMaxCandidates = 9;
        /// <summary>단어 분할 과정에서 각 서브스트링에 대해 추출할 후보 단어 제한</summary>
        public static int MaxCandidatesPerSubstring = 5;

        public struct Theme
        {
            public Color TrayBgColor;
            public Color TrayTextColor;
            public string TrayText;
            public string Description;
        }

        public static readonly Dictionary<ImeState.State, Theme> Themes = new()
        {
            [ImeState.State.EnglishLower] = new Theme { TrayBgColor = Color.Black, TrayTextColor = Color.White, TrayText = "e", Description = "영어 소문자 [e]" },
            [ImeState.State.EnglishUpper] = new Theme { TrayBgColor = Color.Black, TrayTextColor = Color.DeepSkyBlue, TrayText = "E", Description = "영어 대문자 [E]" },
            [ImeState.State.Hangul] = new Theme { TrayBgColor = Color.Red, TrayTextColor = Color.White, TrayText = "K", Description = "한글 (Caps Off) [K]" },
            [ImeState.State.JapaneseIME] = new Theme { TrayBgColor = Color.Black, TrayTextColor = Color.Lime, TrayText = "j", Description = "Japanese IME [j]" },
            [ImeState.State.JapaneseHangul1] = new Theme { TrayBgColor = Color.Black, TrayTextColor = Color.Lime, TrayText = "J", Description = "일본어1_조합형 [J]" },
            [ImeState.State.JapaneseHangul2] = new Theme { TrayBgColor = Color.Black, TrayTextColor = Color.Lime, TrayText = "J", Description = "일본어2_조합형 [J]" },
            [ImeState.State.JapaneseHangul3] = new Theme { TrayBgColor = Color.Black, TrayTextColor = Color.Lime, TrayText = "J", Description = "일본어3_3Layer [J]" }
        };
    }
    #endregion

    #region [ 문자열 리소스 (UiText) ]
    internal static class UiText
    {
        public const string AppName = "IMEJapanese";
        public const string AlreadyRunningMessage = "이미 실행 중입니다.";
        public const string FatalErrorPrefix = "치명적 오류:\n";
        public const string StatusChecking = "현재 상태: 확인 중...";
        public static string HangulCapsMode => MainForm.Instance?.GetCapsModeOverlayText() ?? "일본어 입력모드";
        public const string ExitMenu = "종료(Exit)";
        public const string GithubUrl = "https://github.com/stonkim93/IMEJapanese";

        public static string TrayTooltip(string description) => $"{AppName}: {description}";
        public static string StatusLabel(string description) => $"현재 상태: {description}";
    }
    #endregion

    #region [ Mozc 최적화 설정 (MozcConfig) ]
    /// <summary>
    /// Mozc 오프라인/온라인 한자 변환의 성능 및 정확도를 조율하는 중앙 설정 클래스입니다.
    /// 시스템 사양이나 사용자 경험(UX)에 맞춰 아래의 수치들을 조정하여 최적화할 수 있습니다.
    /// </summary>
    public static class MozcConfig
    {
        // 1. 형태소 분석 설정 : 문맥파악 정확도 vs 변환속도 (15 ~ 30, 기본값: 20)
        public const int MaxPrefixMatchLength = 20;

        // 2. 문자열 매칭 설정 : Connection Cost 연산량 vs 자연스런 문장 생성 (3 ~ 10, 기본값: 5)
        public const int MaxCandidatesPerSubstring = 10;

        // 3. UI 및 후보 표시 설정 : 렌더링 지연 vs 사용자 선택폭 증가 (5 ~ 9, 기본값: 7) 
        public const int MaxDisplayCandidates = 7;

        // 4. 데이터베이스(SQLite) 쿼리 최적화 설정 : 쿼리 시간 vs DB 부하 (50 ~ 200, 기본값: 100)
        public const int DbQueryBatchSize = 200;

        // 5. 온라인 API 폴백(Fallback) 설정 : API 호출 지연 vs 검색 정확도 향상 (2 ~ 5, 기본값: 3) 
        public const int ApiTimeoutSeconds = 3;
    }
    #endregion
}
