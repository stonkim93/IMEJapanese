// Lang.cs - IMEJapanese
// 일본어1(조합형) / 일본어2(조합형) / 일본어3(3Layer) 자판 매핑 및 처리.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace IMEJapanese
{
    using VCode = IMEJapanese.VirtualKeyCodes;
    internal static class VirtualKeyCodes
    {
        public const int Shift = 0x10;
        public const int Ctrl = 0x11;
        public const int Right = 0x27;
        public const int Escape = 0x1B;
        public const int Backspace = 0x08;

        public const int LWin = 0x5B;
        public const int RWin = 0x5C;

        // 알파벳 (A-Z)
        public const int vk_A = 0x41;
        public const int vk_B = 0x42;
        public const int vk_C = 0x43;
        public const int vk_D = 0x44;
        public const int vk_E = 0x45;
        public const int vk_F = 0x46;
        public const int vk_G = 0x47;
        public const int vk_H = 0x48;
        public const int vk_I = 0x49;
        public const int vk_J = 0x4A;
        public const int vk_K = 0x4B;
        public const int vk_L = 0x4C;
        public const int vk_M = 0x4D;
        public const int vk_N = 0x4E;
        public const int vk_O = 0x4F;
        public const int vk_P = 0x50;
        public const int vk_Q = 0x51;
        public const int vk_R = 0x52;
        public const int vk_S = 0x53;
        public const int vk_T = 0x54;
        public const int vk_U = 0x55;
        public const int vk_V = 0x56;
        public const int vk_W = 0x57;
        public const int vk_X = 0x58;
        public const int vk_Y = 0x59;
        public const int vk_Z = 0x5A;

        // 특수기호 및 숫자 가상키 코드 (Windows VK 기준)
        public const int OemLeftBraket = 0xDB;  // ([ {) → (「 『) - 430
        public const int OemRightBraket = 0xDD; // (] }) → (」 』) - 431
        public const int OemWave = 0xC0;        // (` ~) → (々  ～)- 422
        public const int OemDash = 0xBC;        // (, <) → (, ー)  - 420
        public const int OemDot = 0xBE;         // (. >) → (. ・)  - 421
        public const int OemPeriod = 0xBF;      // (/ ?) → (。?)   - 423
        public const int OemComma = 0xBA;       // (; :) → (、:)   - 424
        public const int OemYen = 0xDC;         // (\ |) → (円 ¥)  - 432
        public const int OemMinus = 0xBD;       // (- _) → (- _)   - 433
        public const int OemPlus = 0xBB;        // (= +) → (= +)   - 434
        public const int OemQuote = 0xDE;       // (' ") → (' ")   - 440
    }

    #region [ 0. 유틸리티: 키보드 레이아웃 분석 (KeyboardLayoutAnalyzer) ]
    internal static class KeyboardLayoutAnalyzer
    {
        public static bool CheckCopilotShift(bool isShift)
        {
            if (AppConfig.EnableCopilotMap && isShift)
            {
                bool winHeld = (NativeMethods.GetKeyState(VCode.LWin) & 0x8000) != 0 || 
                               (NativeMethods.GetKeyState(VCode.RWin) & 0x8000) != 0;
                if (winHeld) return false;
            }
            return isShift;
        }

        public static string? GetChar(int vKey, bool isShift)
        {
            byte[] keyState = new byte[256];
            NativeMethods.GetKeyboardState(keyState);

            if (isShift) 
            {
                keyState[VCode.Shift] = 0x80;
                keyState[0xA0] = 0x80; // vk_LSHIFT
                keyState[0xA1] = 0x80; // vk_RSHIFT
            }
            else
            {
                keyState[VCode.Shift] = 0;
                keyState[0xA0] = 0;
                keyState[0xA1] = 0;
            }

            IntPtr hWnd = NativeMethods.GetForegroundWindow();
            uint threadId = NativeMethods.GetWindowThreadProcessId(hWnd, out _);
            IntPtr hkl = NativeMethods.GetKeyboardLayout(threadId);

            uint scanCode = NativeMethods.MapVirtualKeyEx((uint)vKey, 0, hkl);
            StringBuilder sb = new StringBuilder(5);
            
            int result = NativeMethods.ToUnicodeEx((uint)vKey, scanCode, keyState, sb, sb.Capacity, 0, hkl);
            
            if (result > 0)
            {
                string ch = sb.ToString();
                if (isShift && ch.Length == 1 && IsSymbolOrNumber(vKey))
                {
                    string? shiftedFallback = GetStandardShiftedSymbol(vKey);
                    if (shiftedFallback != null && char.IsDigit(ch[0])) return shiftedFallback;
                }
                return ch;
            }

            if (isShift && IsSymbolOrNumber(vKey)) return GetStandardShiftedSymbol(vKey);
            return null;
        }

        private static string? GetStandardShiftedSymbol(int vKey)
        {
            return vKey switch
            {
                0x31 => "!", 0x32 => "@", 0x33 => "#", 0x34 => "$", 0x35 => "%",
                0x36 => "^", 0x37 => "&", 0x38 => "*", 0x39 => "(", 0x30 => ")",
                0xC0 => "~", 0xBD => "_", 0xBB => "+", 0xDB => "{", 0xDD => "}",
                0xDC => "|", 0xBA => ":", 0xDE => "\"", 0xBC => "<", 0xBE => ">", 0xBF => "?",
                _ => null
            };
        }

        public static bool IsSymbolOrNumber(int vKey)
        {
            return (vKey >= 0x30 && vKey <= 0x39) || (vKey >= 0xBA && vKey <= 0xC0) || (vKey >= 0xDB && vKey <= 0xDE);   
        }
    }
    #endregion

    #region [ 1. 인터페이스 및 팩토리 (Interfaces & Factories) ]
    internal interface IKeyProcessor
    {
        bool IsVirtualShift { get; }
        int CurrentLayer { get; }
        
        bool ProcessKeyDown(int vKey, bool isShift, bool capsOn, IntPtr hFore, bool isHangulMode);
        bool ProcessHanjaKey(IntPtr hFore, bool capsOn, bool isHangulMode);
        void OnMouseClick();
        void ToggleVirtualShift();
    }

    internal static class KeyProcessorFactory
    {
        public static readonly IKeyProcessor Japanese1 = new Japanese1Processor();
        public static readonly IKeyProcessor Japanese2 = new Japanese2Processor();
        public static readonly IKeyProcessor Japanese3 = new Japanese3Processor();
    }
    #endregion

    #region [ 2. 유틸리티: 텍스트 선택 및 클립보드 제어 (UI Automation & Clipboard) ]
    
    internal static class OverlayHelper
    {
        public static void ClearOverlay() { try { MainForm.Instance?.ClearOverlay(); } catch { } }
    }

    internal static class TextSelectionUtils
    {
        internal struct ClipboardConfig
        {
            public const uint UnicodeTextFormat = 13;
            public const int OpenRetryCount = 3;
            public const int OpenRetryDelayMs = 10;
            public const int CopyPollingRetryCount = 20;
            public const int CopyPollingDelayMs = 20;
            public const int RestoreDelayMs = 400;
            public const int SelectionCancelDelayMs = 20;
        }

        public static volatile bool IsConverting = false;

        public static void ForceReleaseCopilotModifiers()
        {
            if (!AppConfig.EnableCopilotMap) return;
            var inputs = new List<NativeMethods.INPUT>();
            if ((NativeMethods.GetKeyState(VCode.LWin) & 0x8000) != 0) inputs.Add(MakeKeyUp(VCode.LWin));
            if ((NativeMethods.GetKeyState(VCode.RWin) & 0x8000) != 0) inputs.Add(MakeKeyUp(VCode.RWin));
            if ((NativeMethods.GetKeyState(VCode.Shift) & 0x8000) != 0) inputs.Add(MakeKeyUp(VCode.Shift));
            if (inputs.Count > 0) SendInputsSafe(inputs);
        }

        public static void ExecuteOnStaThread(Action action)
        {
            Thread thread = new Thread(() => { try { action(); } catch { } }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public static void TransformAndReplaceText(
            string lastOutputChar,
            Func<string, string> transformFunc,
            Action<string> setLastOutputChar,
            Action? modeSwitchAction = null)
        {
            if (AppConfig.EnableCopilotMap) Thread.Sleep(50);

            if (!string.IsNullOrEmpty(lastOutputChar))
            {
                string toggled = transformFunc(lastOutputChar);
                if (toggled != lastOutputChar)
                {
                    MainForm.Instance?.ShowOverlay($"{lastOutputChar[0]}→{toggled[0]}", mode: OverlayPositionMode.CharToggle);
                    setLastOutputChar(toggled);
                    if (AppConfig.EnableCopilotMap) ForceReleaseCopilotModifiers();
                    GlobalInputHook.SendReplacement(1, toggled);
                    return;
                }
                modeSwitchAction?.Invoke();
                return;
            }

            if (IsConverting) return;
            IsConverting = true;
            ExecuteOnStaThread(() =>
            {
                try
                {
                    string? selected = ReadSelectedText();
                    if (!string.IsNullOrEmpty(selected))
                    {
                        string toggled = transformFunc(selected);
                        if (toggled != selected)
                        {
                            MainForm.Instance?.ShowOverlay($"{selected[0]}→{toggled[0]}", mode: OverlayPositionMode.SelectionToggle);
                            setLastOutputChar("");
                            if (AppConfig.EnableCopilotMap) ForceReleaseCopilotModifiers();
                            GlobalInputHook.SendReplacement(0, toggled);
                            return;
                        }
                        else if (modeSwitchAction == null)
                        {
                            CancelSelection();
                        }
                    }
                    modeSwitchAction?.Invoke();
                }
                catch { }
                finally { IsConverting = false; }
            });
        }

        public static string? ReadSelectedText()
        {
            try
            {
                IsConverting = true;
                try
                {
                    var focusedElement = AutomationElement.FocusedElement;
                    if (focusedElement != null && focusedElement.TryGetCurrentPattern(TextPattern.Pattern, out object patternObj))
                    {
                        var selections = ((TextPattern)patternObj).GetSelection();
                        if (selections != null && selections.Length > 0)
                        {
                            string text = selections[0].GetText(-1).Trim('\r', '\n', '\t', ' ', '\0');
                            if (text.Length > 0) return text;
                        }
                    }
                }
                catch { }

                bool shiftHeld = (NativeMethods.GetKeyState(VCode.Shift) & 0x8000) != 0;
                string? saved = GetTextWin32();
                try
                {
                    ClearWin32();
                    Thread.Sleep(ClipboardConfig.CopyPollingDelayMs);
                    if (AppConfig.EnableCopilotMap) ForceReleaseCopilotModifiers();
                    
                    SendCtrlC(shiftHeld);

                    string? copied = null;
                    for (int i = 0; i < ClipboardConfig.CopyPollingRetryCount; i++)
                    {
                        Thread.Sleep(ClipboardConfig.CopyPollingDelayMs);
                        copied = GetTextWin32();
                        if (!string.IsNullOrEmpty(copied)) break;
                    }

                    RestoreClipboardAsync(saved);

                    if (!string.IsNullOrEmpty(copied))
                    {
                        string cleanCopied = copied.Trim('\r', '\n', '\t', ' ', '\0');
                        if (cleanCopied.Length > 0) return cleanCopied;
                    }
                    return null;
                }
                catch { return null; } 
            }
            finally { IsConverting = false; }
        }

        private static void RestoreClipboardAsync(string? savedText)
        {
            Task.Run(() =>
            {
                Thread.Sleep(ClipboardConfig.RestoreDelayMs);
                ExecuteOnStaThread(() => {
                    try {
                        if (!string.IsNullOrEmpty(savedText)) Clipboard.SetText(savedText);
                        else Clipboard.Clear();
                    } catch { } 
                });
            });
        }

        public static void CancelSelection()
        {
            try { bool shiftHeld = (NativeMethods.GetKeyState(VCode.Shift) & 0x8000) != 0; SendRight(shiftHeld); Thread.Sleep(ClipboardConfig.SelectionCancelDelayMs); }
            catch { }
        }

        private static void SendRight(bool shiftHeld)
        {
            var inputs = new List<NativeMethods.INPUT>();
            if (shiftHeld) inputs.Add(MakeKeyUp(VCode.Shift));
            inputs.Add(MakeKeyDown(VCode.Right)); inputs.Add(MakeKeyUp(VCode.Right));
            if (shiftHeld) inputs.Add(MakeKeyDown(VCode.Shift));
            SendInputsSafe(inputs);
        }

        private static void SendCtrlC(bool shiftHeld)
        {
            var inputs = new List<NativeMethods.INPUT>();
            if (shiftHeld) inputs.Add(MakeKeyUp(VCode.Shift));
            inputs.Add(MakeKeyDown(VCode.Ctrl)); inputs.Add(MakeKeyDown(VCode.vk_C));
            inputs.Add(MakeKeyUp(VCode.vk_C)); inputs.Add(MakeKeyUp(VCode.Ctrl));
            if (shiftHeld) inputs.Add(MakeKeyDown(VCode.Shift));
            SendInputsSafe(inputs);
        }

        private static void SendInputsSafe(List<NativeMethods.INPUT> inputs)
        {
            GlobalInputHook.IsSending = true; 
            NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<NativeMethods.INPUT>());
            GlobalInputHook.IsSending = false; 
        }

        private static NativeMethods.INPUT MakeKeyDown(ushort vk) => new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, U = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = vk } } };
        private static NativeMethods.INPUT MakeKeyUp(ushort vk) => new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, U = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = vk, dwFlags = NativeMethods.KEYEVENTF_KEYUP } } };

        public static string? GetTextWin32()
        {
            try
            {
                if (!NativeMethods.IsClipboardFormatAvailable(ClipboardConfig.UnicodeTextFormat)) return null;
                bool opened = false;
                for (int i = 0; i < ClipboardConfig.OpenRetryCount; i++) { Thread.Sleep(ClipboardConfig.OpenRetryDelayMs); if (NativeMethods.OpenClipboard(IntPtr.Zero)) { opened = true; break; } }
                if (!opened) return null;
                
                string? result = null;
                IntPtr hGlobal = NativeMethods.GetClipboardData(ClipboardConfig.UnicodeTextFormat);
                if (hGlobal != IntPtr.Zero)
                {
                    IntPtr ptr = NativeMethods.GlobalLock(hGlobal);
                    if (ptr != IntPtr.Zero)
                    {
                        result = Marshal.PtrToStringUni(ptr);
                        NativeMethods.GlobalUnlock(hGlobal);
                    }
                }
                NativeMethods.CloseClipboard();
                return result;
            }
            catch { return null; }
        }

        public static bool ClearWin32()
        {
            for (int i = 0; i < ClipboardConfig.OpenRetryCount; i++)
            {
                try { if (NativeMethods.OpenClipboard(IntPtr.Zero)) { NativeMethods.EmptyClipboard(); NativeMethods.CloseClipboard(); return true; } } catch { }
                Thread.Sleep(ClipboardConfig.OpenRetryDelayMs);
            }
            return false;
        }
    }
    #endregion

    #region [ 3. 언어 프로세서: 일본어1, 일본어2, 일본어3 (Japanese1, Japanese2, Japanese3) ]

    // 중복 제거를 위한 변환 및 특수문자 공통 처리 헬퍼
    internal static class JapaneseTransformationHelper
    {
        public static void HandleHiraganaKatakana(string lastOutputChar, Action<string> setLastOutputChar, Action onModeToggle)
        {
            TextSelectionUtils.TransformAndReplaceText(
                lastOutputChar,
                JapaneseCharacterProcessor.ProcessHK,
                setLastOutputChar,
                onModeToggle
            );
        }

        public static void HandleYoon(string lastOutputChar, Action<string> setLastOutputChar)
        {
            TextSelectionUtils.TransformAndReplaceText(
                lastOutputChar,
                JapaneseCharacterProcessor.ProcessYN,
                setLastOutputChar
            );
        }

        /// <summary>
        /// 가상 키 코드(vKey)를 JapaneseCharacter.cs의 400~440번 3자리 baseCode로 매핑
        /// </summary>
        public static ushort? GetCodeFromVKey(int vKey)
        {
            return vKey switch
            {
                // 숫자키 0 ~ 9
                0x30 => 400, // 0 / )
                0x31 => 401, // 1 / !
                0x32 => 402, // 2 / @
                0x33 => 403, // 3 / #
                0x34 => 404, // 4 / $
                0x35 => 410, // 5 / %
                0x36 => 411, // 6 / ^
                0x37 => 412, // 7 / &
                0x38 => 413, // 8 / *
                0x39 => 414, // 9 / (

                // 일본어 특수기호 및 기호 키
                VCode.OemDash => 420,        // , <  -> Hiragana: ',',  Katakana: 'ー'
                VCode.OemDot => 421,         // . >  -> Hiragana: '.',  Katakana: '・'
                VCode.OemWave => 422,        // ` ~  -> Hiragana: '々', Katakana: '～'
                VCode.OemPeriod => 423,      // / ?  -> Hiragana: '。', Katakana: '?'
                VCode.OemComma => 424,       // ; :  -> Hiragana: '、', Katakana: ':'

                VCode.OemLeftBraket => 430,  // [ {  -> Hiragana: '「', Katakana: '『'
                VCode.OemRightBraket => 431, // ] }  -> Hiragana: '」', Katakana: '』'
                VCode.OemYen => 432,         // \ |  -> Hiragana: '円', Katakana: '¥'
                VCode.OemMinus => 433,       // - _  -> Hiragana: '-',  Katakana: '_'
                VCode.OemPlus => 434,        // = +  -> Hiragana: '=',  Katakana: '+'
                VCode.OemQuote => 440,       // ' "  -> Hiragana: '\'', Katakana: '\"'

                _ => null
            };
        }

        /// <summary>
        /// 400~440(가타카나/Shift 기준 405~445)번 3자리 코드를 사용하여 기호 및 숫자를 처리
        /// </summary>
        public static string? ProcessPunctuation(int vKey, bool useKatakana, Action<string> setLastOutputChar)
        {
            ushort? code = GetCodeFromVKey(vKey);
            if (code.HasValue)
            {
                // [수정됨] 수동 오프셋 (+ 5) 계산을 생략하고 500 사이즈 O(1) Dictionary 캐싱을 호출
                // 구조체의 Katakana / Hiragana char 속성에서 직접 값을 가져옵니다.
                if (CharacterDatabase.ContainsCode(code.Value))
                {
                    var jpChar = JapaneseCharacter.FromCode(code.Value);
                    string ch = useKatakana ? jpChar.Katakana.ToString() : jpChar.Hiragana.ToString();

                    MainForm.Instance?.ShowOverlay(ch);
                    setLastOutputChar(ch);
                    return ch;
                }
            }
            return null;
        }
        /// <summary>
        /// 400~440번 3자리 수 코드로 직접 정의되지 않은 기타 키보드 기호 및 숫자 처리 (Fallback)
        /// </summary>
        public static string? ProcessSymbolOrNumber(int vKey, bool useKatakana, Action<string> setLastOutputChar)
        {
            if (KeyboardLayoutAnalyzer.IsSymbolOrNumber(vKey))
            {
                string? ch = KeyboardLayoutAnalyzer.GetChar(vKey, useKatakana);
                if (!string.IsNullOrEmpty(ch))
                {
                    MainForm.Instance?.ShowOverlay(ch);
                    setLastOutputChar(ch);
                    return ch;
                }
            }
            return null;
        }
    }
    
    internal class Japanese1Processor : IKeyProcessor
    {
        public bool IsVirtualShift => Japanese1Map.IsKatakana;
        public int CurrentLayer => 1;
        public void ToggleVirtualShift() => Japanese1Map.TogglePendingHiraKataModeOnly();

        public bool ProcessHanjaKey(IntPtr hFore, bool capsOn, bool isHangulMode)
        {
            if (isHangulMode && capsOn) 
            { 
                Japanese1Map.SetLayer(1);
                ImeState.SetHangulState(hFore, false); 
                NativeMethods.SimulateCapsLock(); 
                MainForm.Instance?.ShowOverlay("영어 소문자 모드", mode: OverlayPositionMode.ModeSwitch); 
                return true; 
            } 
            if (!isHangulMode || !capsOn) {
                Japanese1Map.SetLayer(1);
                ImeState.SetHangulState(hFore, true);
                if (!capsOn) NativeMethods.SimulateCapsLock();
                MainForm.Instance?.ShowOverlay("일본어1_조합형", mode: OverlayPositionMode.ModeSwitch);
                return true;
            }
            return false;
        }

        public bool ProcessKeyDown(int vKey, bool isShift, bool capsOn, IntPtr hFore, bool isHangulMode)
        {
            Japanese1Map.SetLayer(1);
            return Japanese1Map.ProcessKeyDownShared(vKey, isShift, capsOn, hFore, isHangulMode);
        }

        public void OnMouseClick() 
        {
            Japanese1Map.SetLayer(1);
            if (Japanese1Map.IsWaitingVowel) Japanese1Map.Reset();
            Japanese1Map.SetLastOutputChar("");
            GlobalInputHook.ClearCompositionBuffer();
        }    
    }

    internal class Japanese2Processor : IKeyProcessor
    {
        public bool IsVirtualShift => Japanese1Map.IsKatakana;
        public int CurrentLayer => 2;
        public void ToggleVirtualShift() => Japanese1Map.TogglePendingHiraKataModeOnly();

        public bool ProcessHanjaKey(IntPtr hFore, bool capsOn, bool isHangulMode)
        {
            if (isHangulMode && capsOn) 
            { 
                Japanese1Map.SetLayer(2);
                ImeState.SetHangulState(hFore, false); 
                NativeMethods.SimulateCapsLock(); 
                MainForm.Instance?.ShowOverlay("영어 소문자 모드", mode: OverlayPositionMode.ModeSwitch); 
                return true; 
            } 
            if (!isHangulMode || !capsOn) {
                Japanese1Map.SetLayer(2);
                ImeState.SetHangulState(hFore, true);
                if (!capsOn) NativeMethods.SimulateCapsLock();
                MainForm.Instance?.ShowOverlay("일본어2_조합형", mode: OverlayPositionMode.ModeSwitch);
                return true;
            }
            return false;
        }

        public bool ProcessKeyDown(int vKey, bool isShift, bool capsOn, IntPtr hFore, bool isHangulMode)
        {
            Japanese1Map.SetLayer(2);
            return Japanese1Map.ProcessKeyDownShared(vKey, isShift, capsOn, hFore, isHangulMode);
        }

        public void OnMouseClick() 
        {
            Japanese1Map.SetLayer(2);
            if (Japanese1Map.IsWaitingVowel) Japanese1Map.Reset();
            Japanese1Map.SetLastOutputChar("");
            GlobalInputHook.ClearCompositionBuffer();
        }    
    }

    internal static class Japanese1Map
    {
        // 3자리 수 코드 기반 데이터 모델 적용
        // Base(자음+탁음정보) + Offset(모음) 결합으로 최적화 처리
        private static readonly Dictionary<int, ushort> _consonantBase = new()
        {
            // { (vk_Q, vk_H), ("ば" : "バ") }, { (vk_Q, vk_J), ("び" : "ビ") }, { (vk_Q, vk_K), ("ぶ" : "ブ") }, { (vk_Q, vk_M), ("べ" : "ベ") }, { (vk_Q, vk_L), ("ぼ" : "ボ") },
            // { (vk_W, vk_H), ("ざ" : "ザ") }, { (vk_W, vk_J), ("じ" : "ジ") }, { (vk_W, vk_K), ("ず" : "ズ") }, { (vk_W, vk_M), ("ぜ" : "ゼ") }, { (vk_W, vk_L), ("ぞ" : "ゾ") },
            // { (vk_E, vk_H), ("が" : "ガ") }, { (vk_E, vk_J), ("ぎ" : "ギ") }, { (vk_E, vk_K), ("ぐ" : "グ") }, { (vk_E, vk_M), ("げ" : "ゲ") }, { (vk_E, vk_L), ("ご" : "ゴ") },
            // { (vk_R, vk_H), ("だ" : "ダ") }, { (vk_R, vk_J), ("ぢ" : "ヂ") }, { (vk_R, vk_K), ("づ" : "ヅ") }, { (vk_R, vk_M), ("で" : "デ") }, { (vk_R, vk_L), ("ど" : "ド") },
            // { (vk_A, vk_H), ("は" : "ハ") }, { (vk_A, vk_J), ("ひ" : "ヒ") }, { (vk_A, vk_K), ("ふ" : "フ") }, { (vk_A, vk_M), ("へ" : "ヘ") }, { (vk_A, vk_L), ("ほ" : "ホ") },
            // { (vk_S, vk_H), ("さ" : "サ") }, { (vk_S, vk_J), ("し" : "シ") }, { (vk_S, vk_K), ("す" : "ス") }, { (vk_S, vk_M), ("せ" : "セ") }, { (vk_S, vk_L), ("そ" : "ソ") },
            // { (vk_D, vk_H), ("か" : "カ") }, { (vk_D, vk_J), ("き" : "キ") }, { (vk_D, vk_K), ("く" : "ク") }, { (vk_D, vk_M), ("け" : "ケ") }, { (vk_D, vk_L), ("こ" : "コ") },
            // { (vk_F, vk_H), ("た" : "タ") }, { (vk_F, vk_J), ("ち" : "チ") }, { (vk_F, vk_K), ("つ" : "ツ") }, { (vk_F, vk_M), ("て" : "テ") }, { (vk_F, vk_L), ("と" : "ト") },
            // { (vk_Z, vk_H), ("ぱ" : "パ") }, { (vk_Z, vk_J), ("ぴ" : "ピ") }, { (vk_Z, vk_K), ("ぷ" : "プ") }, { (vk_Z, vk_M), ("ぺ" : "ペ") }, { (vk_Z, vk_L), ("ぽ" : "ポ") },
            // { (vk_X, vk_H), ("ま" : "マ") }, { (vk_X, vk_J), ("み" : "ミ") }, { (vk_X, vk_K), ("む" : "ム") }, { (vk_X, vk_M), ("め" : "メ") }, { (vk_X, vk_L), ("も" : "モ") },
            // { (vk_C, vk_H), ("ら" : "ラ") }, { (vk_C, vk_J), ("り" : "リ") }, { (vk_C, vk_K), ("る" : "ル") }, { (vk_C, vk_M), ("れ" : "レ") }, { (vk_C, vk_L), ("ろ" : "ロ") },
            // { (vk_V, vk_H), ("な" : "ナ") }, { (vk_V, vk_J), ("に" : "ニ") }, { (vk_V, vk_K), ("ぬ" : "ヌ") }, { (vk_V, vk_M), ("ね" : "ネ") }, { (vk_V, vk_L), ("の" : "ノ") }

            { VCode.vk_Q, 140 }, { VCode.vk_W, 120 }, { VCode.vk_E, 110 }, { VCode.vk_R, 130 },
            { VCode.vk_A, 040 }, { VCode.vk_S, 020 }, { VCode.vk_D, 010 }, { VCode.vk_F, 030 },
            { VCode.vk_Z, 240 }, { VCode.vk_X, 060 }, { VCode.vk_C, 070 }, { VCode.vk_V, 050 }
        };

        private static readonly Dictionary<int, ushort> _vowelOffset = new()
        {
            // { vk_T, ("っ" : "ッ") }, { vk_G, ("ん" : "ン") },
            // { vk_Y, ("わ" : "ワ") }, { vk_U, ("を" : "ヲ") }, { vk_I, ("や" : "ヤ") }, { vk_O, ("よ" : "ヨ") }, { vk_P, ("ゆ" : "ユ") },
            // { vk_H, ("あ" : "ア") }, { vk_J, ("い" : "イ") }, { vk_K, ("う" : "ウ") }, { vk_L, ("お" : "オ") }, { vk_M, ("え" : "エ") }

            { VCode.vk_H, 0 }, { VCode.vk_J, 1 }, { VCode.vk_K, 2 }, { VCode.vk_M, 3 }, { VCode.vk_L, 4 }
        };

        private static readonly Dictionary<int, ushort> _soloMap = new()
        {
            // { vk_Q, ("ば" : "バ") }, { vk_W, ("ざ" : "ザ") }, { vk_E, ("が" : "ガ") }, { vk_R, ("だ" : "ダ") }, 
            // { vk_A, ("は" : "ハ") }, { vk_S, ("さ" : "サ") }, { vk_D, ("か" : "カ") }, { vk_F, ("た" : "タ") }, 
            // { vk_Z, ("ぱ" : "パ") }, { vk_X, ("ま" : "マ") }, { vk_C, ("ら" : "ラ") }, { vk_V, ("な" : "ナ") }

            { VCode.vk_T, 332 }, { VCode.vk_G, 092 },
            { VCode.vk_Y, 090 }, { VCode.vk_U, 094 }, { VCode.vk_I, 080 }, { VCode.vk_O, 084 }, { VCode.vk_P, 082 },
            { VCode.vk_H, 000 }, { VCode.vk_J, 001 }, { VCode.vk_K, 002 }, { VCode.vk_L, 004 }, { VCode.vk_M, 003 }
        };

        // Layer2 모음 고정 매핑
        private static readonly Dictionary<ushort, ushort> _previewMapL2 = new()
        {
            // { vk_Q, ("ば" : "バ") }, { vk_W, ("じ" : "ジ") }, { vk_E, ("が" : "ガ") }, { vk_R, ("で" : "デ") },
            // { vk_A, ("は" : "ハ") }, { vk_S, ("し" : "シ") }, { vk_D, ("か" : "カ") }, { vk_F, ("て" : "テ") }, 
            // { vk_Z, ("ぱ" : "パ") }, { vk_X, ("も" : "モ") }, { vk_C, ("る" : "ル") }, { vk_V, ("の" : "ノ") }

            { 140, 140 }, { 120, 121 }, { 110, 110 }, { 130, 133 },
            { 040, 040 }, { 020, 021 }, { 010, 010 }, { 030, 033 },
            { 240, 240 }, { 060, 064 }, { 070, 072 }, { 050, 054 }
        };

        private static bool _isKatakana = false;
        private static bool _waitingVowel = false;
        private static ushort _pendingConsonant = 0;
        private static string _pendingChar = "";
        private static string _lastOutputChar = "";
        private static int _ynToggleCount = 0;

        public static int CurrentLayer { get; private set; } = 1;
        public static void SetLayer(int layer) => CurrentLayer = layer;
        public static bool IsWaitingVowel => _waitingVowel;
        public static string PendingChar => _pendingChar;
        public static bool IsKatakana => _isKatakana;

        public static void Reset() 
        { 
            _waitingVowel = false; 
            _pendingConsonant = 0; 
            _pendingChar = ""; 
            _lastOutputChar = ""; 
            _ynToggleCount = 0; 
            
            OverlayHelper.ClearOverlay();
        }
             
        public static void SetLastOutputChar(string ch) => _lastOutputChar = ch;
    
        public static void TogglePendingHiraKataModeOnly() => _isKatakana = !_isKatakana;

        public static void TogglePendingHiraKata()
        {
            if (!_waitingVowel) return;
            _isKatakana = !_isKatakana;
            string preview = GetPreview(_pendingConsonant);
            for (int i = 0; i < _ynToggleCount; i++) preview = JapaneseCharacterProcessor.ProcessYN(preview);
            _pendingChar = preview; 
                
            MainForm.Instance?.ShowOverlay(_pendingChar, 0, OverlayPositionMode.CharToggle);
        }
    
        public static void TogglePendingYn()
        {
            if (!_waitingVowel) return; _ynToggleCount++;
            _pendingChar = JapaneseCharacterProcessor.ProcessYN(_pendingChar);
            
            MainForm.Instance?.ShowOverlay(_pendingChar, 0, OverlayPositionMode.CharToggle);
        }
    
        public static void HandleHiraganaKatakanaTransformation() =>
            JapaneseTransformationHelper.HandleHiraganaKatakana(_lastOutputChar, SetLastOutputChar, () => {
                _isKatakana = !_isKatakana; 
                _lastOutputChar = ""; 
                MainForm.Instance?.ShowOverlay(_isKatakana ? "Katakana" : "Hiragana", mode: OverlayPositionMode.ModeSwitch);
            });
    
        public static void HandleYoonTransformation() =>
            JapaneseTransformationHelper.HandleYoon(_lastOutputChar, SetLastOutputChar);

        public static bool ProcessKeyDownShared(int vKey, bool isShift, bool capsOn, IntPtr hFore, bool isHangulMode)
        {
            // 방향키 등 일부 제어키는 직접 처리 안 함
            if (vKey is >= 0x21 and <= 0x28) { if (!isShift) SetLastOutputChar(""); return false; }

            // B키(HK 히라가나↔가타카나) / N키(YN 요음 변환) 처리
            if (vKey == VCode.vk_B && capsOn && isHangulMode)
            {
                if (_waitingVowel) ApplyPendingTransformation(JapaneseCharacterProcessor.ProcessHK);
                else HandleHiraganaKatakanaTransformation();
                return true;
            }
            if (vKey == VCode.vk_N && capsOn && isHangulMode)
            {
                if (_waitingVowel) ApplyPendingTransformation(JapaneseCharacterProcessor.ProcessYN);
                else HandleYoonTransformation();
                return true;
            }

            if (!capsOn || !isHangulMode) return false;
            if (TextSelectionUtils.IsConverting) return true;

            // ESC / Delete / Backspace : 조합모드면 대기중인 자음을 취소, 아니면 그냥 흘려보냄
            if (vKey is VCode.Escape or 0x2E or VCode.Backspace)
            {
                if (_waitingVowel)
                {
                    // 조합 대기 취소 → 대기 중인 자음 글자 제거 없이 상태만 초기화
                    if (AppConfig.LogLevel >= 2) Trace.WriteLine($"[Japanese1Map] 조합 취소: pendingChar='{_pendingChar}'");
                    _waitingVowel = false;
                    _pendingConsonant = 0;
                    _pendingChar = "";
                    _ynToggleCount = 0;
                    OverlayHelper.ClearOverlay();
                    // 실제 키 입력(백스페이스 등)은 OS로 흘려보냄
                    return false;
                }
                // 조합 대기 중이 아닐 때는 그냥 통과
                return false;
            }

            string? keyResult = ProcessKey(vKey, isShift);
            if (AppConfig.LogLevel >= 2) Trace.WriteLine($"[Japanese1Map] vKey={vKey} isShift={isShift} waitingVowel={_waitingVowel} → keyResult='{keyResult}'");

            if (keyResult == null)
            {
                SetLastOutputChar("");
                if (vKey >= 0x41 && vKey <= 0x5A) return true; // 한글 변환 방지
                return false;
            }

            if (keyResult.Length > 0)
            {
                GlobalInputHook.IsSending = true;
                NativeMethods.SendUnicodeString(keyResult);
                GlobalInputHook.IsSending = false;
                GlobalInputHook.AppendComposition(keyResult);
            }
            return true;
        }

        /// <summary>
        /// 2단계 조합 처리:
        /// 1단계) 대표자음 키 → 조합 대기 상태로 진입, 오버레이 표시
        /// 2단계) 모음 → 확정 출력 / ESC·Del·BS → 자음 취소(호출 전 처리됨) / 그 외 → 대기 자음 확정 후 새 입력 처리
        /// </summary>
        public static string? ProcessKey(int vKey, bool isShift)
        {
            bool useKatakana = isShift ^ _isKatakana;

            // ─────────────────────────────────────────────────
            // [조합 대기 중] 2번째 키 입력 처리
            // ─────────────────────────────────────────────────
            if (_waitingVowel)
            {
                // 2-A) 모음 키 → 조합 확정
                if (_vowelOffset.TryGetValue(vKey, out ushort vOffset))
                {
                    ushort code = (ushort)(_pendingConsonant + vOffset);
                    string result;

                    if (CharacterDatabase.ContainsCode(code))
                    {
                        var jpChar = JapaneseCharacter.FromCode(code);
                        result = _isKatakana ? jpChar.Katakana.ToString() : jpChar.Hiragana.ToString();
                        for (int i = 0; i < _ynToggleCount; i++) result = JapaneseCharacterProcessor.ProcessYN(result);
                        MainForm.Instance?.ShowOverlay($"{_pendingChar}+{GetVowelPreview(vOffset)}={result}");
                    }
                    else
                    {
                        // 조합 불가 → 자음 그대로 + 단독 모음
                        string vowelStr = GetVowelPreview(vOffset);
                        result = _pendingChar + vowelStr;
                        if (AppConfig.LogLevel >= 2) Trace.WriteLine($"[Japanese1Map] 조합 불가: code={code}, result='{result}'");
                    }

                    _waitingVowel = false; _pendingConsonant = 0; _pendingChar = ""; _ynToggleCount = 0;
                    _lastOutputChar = result;
                    return result;
                }

                // 2-B) 그 외 키 → 대기 중인 자음을 확정 출력, 새 키를 재귀 처리
                string flush = _pendingChar;
                _waitingVowel = false; _pendingConsonant = 0; _pendingChar = ""; _ynToggleCount = 0;
                if (AppConfig.LogLevel >= 2) Trace.WriteLine($"[Japanese1Map] 자음 확정 후 새 입력 처리: flush='{flush}'");

                // 새 키 처리 (재귀, 이미 대기가 없으므로 1단계로 진입)
                string? next = ProcessKey(vKey, isShift);
                // flush + 새 결과를 합쳐서 반환
                if (!string.IsNullOrEmpty(flush) || next != null)
                    return flush + (next ?? "");
                return flush.Length > 0 ? flush : null;
            }

            // ─────────────────────────────────────────────────
            // [조합 대기 없음] 1번째 키 입력 처리
            // ─────────────────────────────────────────────────

            // 기호/구두점 처리
            string? punct = JapaneseTransformationHelper.ProcessPunctuation(vKey, useKatakana, SetLastOutputChar);
            if (punct != null) return punct;

            string? sym = JapaneseTransformationHelper.ProcessSymbolOrNumber(vKey, useKatakana, SetLastOutputChar);
            if (sym != null) return sym;

            // B, N 은 상위에서 처리됨
            if (vKey == VCode.vk_B || vKey == VCode.vk_N) return null;

            // 대표자음 키 → 조합 대기
            if (_consonantBase.TryGetValue(vKey, out ushort cBase))
            {
                _waitingVowel = true;
                _pendingConsonant = cBase;
                _isKatakana = useKatakana;
                _ynToggleCount = 0;
                _pendingChar = GetPreview(cBase);
                MainForm.Instance?.ShowOverlay(_pendingChar, 0);
                if (AppConfig.LogLevel >= 2) Trace.WriteLine($"[Japanese1Map] 자음 입력: vKey={vKey} cBase={cBase} pendingChar='{_pendingChar}'");
                return ""; // 아직 출력 없음 (대기)
            }

            // 단독 문자 키(あ행 등 자음 없는 문자)
            if (_soloMap.TryGetValue(vKey, out ushort soloCode))
            {
                var jpChar = JapaneseCharacter.FromCode(soloCode);
                string ch = useKatakana ? jpChar.Katakana.ToString() : jpChar.Hiragana.ToString();
                MainForm.Instance?.ShowOverlay(ch);
                _lastOutputChar = ch;
                return ch;
            }

            _lastOutputChar = "";
            return null;
        }

        private static string GetVowelPreview(ushort vOffset)
        {
            if (CharacterDatabase.ContainsCode(vOffset))
            {
                var vowChar = JapaneseCharacter.FromCode(vOffset);
                return _isKatakana ? vowChar.Katakana.ToString() : vowChar.Hiragana.ToString();
            }
            return "";
        }

        private static void ApplyPendingTransformation(Func<string, string> transformFunc)
        {
            string preview = transformFunc(_pendingChar);
            MainForm.Instance?.ShowOverlay($"{_pendingChar[0]}→{preview[0]}", mode: OverlayPositionMode.CharToggle);
            
            GlobalInputHook.IsSending = true; NativeMethods.SendUnicodeString(preview); GlobalInputHook.IsSending = false;
            GlobalInputHook.AppendComposition(preview);
            
            _waitingVowel = false; _pendingConsonant = 0; _pendingChar = ""; _ynToggleCount = 0; _lastOutputChar = preview; 
        }
    
        private static string GetPreview(ushort baseCode)
        {
            ushort code = baseCode;
            if (CurrentLayer == 2 && _previewMapL2.TryGetValue(baseCode, out ushort l2Code))
            {
                code = l2Code;
            }
            var jpChar = JapaneseCharacter.FromCode(code);
            return _isKatakana ? jpChar.Katakana.ToString() : jpChar.Hiragana.ToString();
        }
    }

    internal static class Japanese3Map
    {
        // 3자리 수 코드 기반 데이터 모델 적용
        private static readonly Dictionary<int, ushort> _layer1Map = new()
        {
            // { vk_Q, ("レ" : "れ") }, { vk_W, ("ロ" : "ろ") }, { vk_E, ("ル" : "る") }, { vk_R, ("リ" : "り") }, { vk_T, ("ラ" : "ら") },
            // { vk_A, ("ネ" : "ね") }, { vk_S, ("ノ" : "の") }, { vk_D, ("ヌ" : "ぬ") }, { vk_F, ("ニ" : "に") }, { vk_G, ("ナ" : "な") },
            // { vk_Z, ("メ" : "め") }, { vk_X, ("モ" : "も") }, { vk_C, ("ム" : "む") }, { vk_V, ("ミ" : "み") }, { vk_B, ("MA" : "ま") },
            // { vk_Y, ("ハ" : "は") }, { vk_U, ("ヒ" : "ひ") }, { vk_I, ("フ" : "ふ") }, { vk_O, ("HO" : "ほ") }, { vk_P, ("ヘ" : "へ") },
            // { vk_H, ("ン" : "ん") }, { vk_J, ("ア" : "あ") }, { vk_K, ("イ" : "い") }, { vk_L, ("ウ" : "う") },
            // { vk_N, ("オ" : "お") }, { vk_M, ("エ" : "え") }

            { VCode.vk_Q, 073 }, { VCode.vk_W, 074 }, { VCode.vk_E, 072 }, { VCode.vk_R, 071 }, { VCode.vk_T, 070 }, 
            { VCode.vk_A, 053 }, { VCode.vk_S, 054 }, { VCode.vk_D, 052 }, { VCode.vk_F, 051 }, { VCode.vk_G, 050 },
            { VCode.vk_Z, 063 }, { VCode.vk_X, 064 }, { VCode.vk_C, 062 }, { VCode.vk_V, 061 }, { VCode.vk_B, 060 },
            { VCode.vk_Y, 040 }, { VCode.vk_U, 041 }, { VCode.vk_I, 042 }, { VCode.vk_O, 044 }, { VCode.vk_P, 043 }, 
            { VCode.vk_H, 092 }, { VCode.vk_J, 000 }, { VCode.vk_K, 001 }, { VCode.vk_L, 002 }, 
            { VCode.vk_N, 004 }, { VCode.vk_M, 003 }
        };

        private static readonly Dictionary<int, ushort> _layer2Map = new()
        {
            // { vk_Q, ( "ケ" : "け") }, { vk_W, ( "コ" : "こ") }, { vk_E, ( "ク" : "く") }, { vk_R, ( "キ" : "き") }, { vk_T, ( "カ" : "か") },
            // { vk_A, ( "テ" : "て") }, { vk_S, ( "ト" : "と") }, { vk_D, ( "ツ" : "つ") }, { vk_F, ( "チ" : "ち") }, { vk_G, ( "タ" : "た") },
            // { vk_Z, ( "セ" : "せ") }, { vk_X, ( "ソ" : "そ") }, { vk_C, ( "스" : "す") }, { vk_V, ( "シ" : "し") }, { vk_B, ( "サ" : "さ") },
            // { vk_Y, ( "パ" : "ぱ") }, { vk_U, ( "ピ" : "ぴ") }, { vk_I, ( "プ" : "ぷ") }, { vk_O, ( "PO" : "ぽ") }, { vk_P, ( "ペ" : "ぺ") }, 
            // { vk_H, ( "ッ" : "っ") }, { vk_J, ( "ヤ" : "や") }, { vk_K, ( "ヨ" : "よ") }, { vk_L, ( "ユ" : "ゆ") }, 
            // { vk_N, ( "ヲ" : "を") }, { vk_M, ( "ワ" : "わ") }

            { VCode.vk_Q, 013 }, { VCode.vk_W, 014 }, { VCode.vk_E, 012 }, { VCode.vk_R, 011 }, { VCode.vk_T, 010 },
            { VCode.vk_A, 033 }, { VCode.vk_S, 034 }, { VCode.vk_D, 032 }, { VCode.vk_F, 031 }, { VCode.vk_G, 030 },
            { VCode.vk_Z, 023 }, { VCode.vk_X, 024 }, { VCode.vk_C, 022 }, { VCode.vk_V, 021 }, { VCode.vk_B, 020 },
            { VCode.vk_Y, 240 }, { VCode.vk_U, 241 }, { VCode.vk_I, 242 }, { VCode.vk_O, 244 }, { VCode.vk_P, 243 }, 
            { VCode.vk_H, 332 }, { VCode.vk_J, 080 }, { VCode.vk_K, 084 }, { VCode.vk_L, 082 }, 
            { VCode.vk_N, 094 }, { VCode.vk_M, 090 }
        };

        private static readonly Dictionary<int, ushort> _layer3Map = new()
        {
            // { vk_Q, ( "ゲ" : "げ") }, { vk_W, ( "ゴ" : "ご") }, { vk_E, ( "グ" : "ぐ") }, { vk_R, ( "ギ" : "ぎ") }, { vk_T, ( "ガ" : "が") }, 
            // { vk_A, ( "デ" : "で") }, { vk_S, ( "ド" : "ど") }, { vk_D, ( "ヅ" : "づ") }, { vk_F, ( "ヂ" : "ぢ") }, { vk_G, ( "ダ" : "だ") },
            // { vk_Z, ( "ゼ" : "ぜ") }, { vk_X, ( "ゾ" : "ぞ") }, { vk_C, ( "ズ" : "ず") }, { vk_V, ( "ジ" : "じ") }, { vk_B, ( "ザ" : "ざ") }, 
            // { vk_Y, ( "バ" : "ば") }, { vk_U, ( "ビ" : "び") }, { vk_I, ( "ブ" : "ぶ") }, { vk_O, ( "ボ" : "ぼ") }, { vk_P, ( "ベ" : "べ") }, 
            // { vk_H, ( "ィ" : "ヴ") }, { vk_J, ( "ャ" : "ゃ") }, { vk_K, ( "ョ" : "ょ") }, { vk_L, ( "ュ" : "ゅ") }

            { VCode.vk_Q, 113 }, { VCode.vk_W, 114 }, { VCode.vk_E, 112 }, { VCode.vk_R, 111 }, { VCode.vk_T, 110 }, 
            { VCode.vk_A, 133 }, { VCode.vk_S, 134 }, { VCode.vk_D, 132 }, { VCode.vk_F, 131 }, { VCode.vk_G, 130 },
            { VCode.vk_Z, 123 }, { VCode.vk_X, 124 }, { VCode.vk_C, 122 }, { VCode.vk_V, 121 }, { VCode.vk_B, 120 },
            { VCode.vk_Y, 140 }, { VCode.vk_U, 141 }, { VCode.vk_I, 142 }, { VCode.vk_O, 144 }, { VCode.vk_P, 143 },
            { VCode.vk_J, 380 }, { VCode.vk_K, 384 }, { VCode.vk_L, 382 } 
        };

        private static bool _isVirtualShift = false;
        private static string _lastOutputChar = "";

        public static bool IsVirtualShift => _isVirtualShift;
        public static int CurrentLayer { get; private set; } = 1;

        public static void SetLayer(int layer) => CurrentLayer = layer;
        public static void SetLastOutputChar(string ch) => _lastOutputChar = ch;
        
        public static void ToggleVirtualShiftOnly() => _isVirtualShift = !_isVirtualShift;

        public static void HandleHiraganaKatakanaTransformation() =>
            JapaneseTransformationHelper.HandleHiraganaKatakana(_lastOutputChar, SetLastOutputChar, () => {
                _isVirtualShift = !_isVirtualShift; 
                _lastOutputChar = ""; 
                MainForm.Instance?.ShowOverlay(_isVirtualShift ? "Katakana" : "Hiragana", mode: OverlayPositionMode.ModeSwitch);
            });

        public static void HandleYoonTransformation() =>
            JapaneseTransformationHelper.HandleYoon(_lastOutputChar, SetLastOutputChar);

        public static bool ProcessKeyDownShared(int vKey, bool isShift, bool capsOn, IntPtr hFore, bool isHangulMode)
        {
            if (vKey is >= 0x21 and <= 0x28) { if (!isShift) SetLastOutputChar(""); return false; }
            if (!capsOn || !isHangulMode) return false;
            if (TextSelectionUtils.IsConverting) return true;
            if (CurrentLayer == 3 && capsOn && isHangulMode) 
            {
                if (vKey == VCode.vk_N) { HandleHiraganaKatakanaTransformation(); return true; }
                if (vKey == VCode.vk_M) { HandleYoonTransformation(); return true; }
            }

            bool useKatakana = isShift ^ _isVirtualShift;

            string? punct = JapaneseTransformationHelper.ProcessPunctuation(vKey, useKatakana, SetLastOutputChar);
            if (punct != null)
            {
                GlobalInputHook.IsSending = true; NativeMethods.SendUnicodeString(punct); GlobalInputHook.IsSending = false;
                GlobalInputHook.AppendComposition(punct);
                return true;
            }

            string? sym = JapaneseTransformationHelper.ProcessSymbolOrNumber(vKey, useKatakana, SetLastOutputChar);
            if (sym != null)
            {
                GlobalInputHook.IsSending = true; NativeMethods.SendUnicodeString(sym); GlobalInputHook.IsSending = false;
                GlobalInputHook.AppendComposition(sym);
                return true;
            }

            //if (vKey == VCode.vk_M || vKey == VCode.vk_N) return true;

            string? ch = null;
            if (CurrentLayer == 3 && vKey == VCode.vk_H)
            {
                ch = useKatakana ? "ィ" : "ヴ";
            }
            else
            {
                ushort? code = CurrentLayer switch
                {
                    1 => _layer1Map.TryGetValue(vKey, out var c) ? c : (ushort?)null,
                    2 => _layer2Map.TryGetValue(vKey, out var c) ? c : (ushort?)null,
                    3 => _layer3Map.TryGetValue(vKey, out var c) ? c : (ushort?)null,
                    _ => null
                };

                if (code.HasValue)
                {
                    var jpChar = JapaneseCharacter.FromCode(code.Value);
                    ch = useKatakana ? jpChar.Katakana.ToString() : jpChar.Hiragana.ToString();
                }
            }

            if (ch != null)
            {
                GlobalInputHook.IsSending = true; NativeMethods.SendUnicodeString(ch); GlobalInputHook.IsSending = false;
                GlobalInputHook.AppendComposition(ch);
                MainForm.Instance?.ShowOverlay(ch);
                _lastOutputChar = ch; return true;
            }

            _lastOutputChar = "";
            if (vKey >= 0x41 && vKey <= 0x5A) return true;
            return false;
        }
    }

    internal class Japanese3Processor : IKeyProcessor
    {
        public bool IsVirtualShift => Japanese3Map.IsVirtualShift;
        public int CurrentLayer => Japanese3Map.CurrentLayer;
        public void ToggleVirtualShift() => Japanese3Map.ToggleVirtualShiftOnly();

        public bool ProcessHanjaKey(IntPtr hFore, bool capsOn, bool isHangulMode)
        {
            if (isHangulMode && capsOn)
            {
                int newLayer = Japanese3Map.CurrentLayer + 1;
                if (newLayer > 3)
                {
                    Japanese3Map.SetLayer(1);
                    ImeState.SetHangulState(hFore, false);
                    NativeMethods.SimulateCapsLock();
                    MainForm.Instance?.ShowOverlay("영어 소문자 모드", mode: OverlayPositionMode.ModeSwitch);
                }
                else
                {
                    Japanese3Map.SetLayer(newLayer);
                    MainForm.Instance?.ShowOverlay($"일본어3_Layer{newLayer}", mode: OverlayPositionMode.ModeSwitch);
                }
                return true;
            }
            if (!isHangulMode || !capsOn)
            {
                Japanese3Map.SetLayer(1);
                ImeState.SetHangulState(hFore, true);
                if (!capsOn) NativeMethods.SimulateCapsLock();
                MainForm.Instance?.ShowOverlay("일본어3_Layer1", mode: OverlayPositionMode.ModeSwitch);
                return true;
            }
            return false;
        }

        public bool ProcessKeyDown(int vKey, bool isShift, bool capsOn, IntPtr hFore, bool isHangulMode)
        {
            return Japanese3Map.ProcessKeyDownShared(vKey, isShift, capsOn, hFore, isHangulMode);
        }

        public void OnMouseClick()
        {
            Japanese3Map.SetLastOutputChar("");
            GlobalInputHook.ClearCompositionBuffer();
        }
    }
    #endregion
}