// Lang.cs - IMEJapanese
// 일본어1(조합형) / 일본어2(조합형) / 일본어3(3Layer) 자판 매핑 및 처리.
#nullable enable
using System;
using System.Collections.Generic;
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

        public const int OemYen = 0xDC;      // (\ |) → (¥ |)
        public const int OemColon = 0xBA;    // (; :) → (・ :)
        public const int OemComma = 0xBC;    // (, <) → (, 、)
        public const int OemPeriod = 0xBE;   // (. >) → (. 。)
        public const int OemSlash = 0xBF;    // (/ ?) → (/ ー)
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
                    MainForm.Instance?.ShowOverlay($"{lastOutputChar[0]}→{toggled[0]}");
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
                            MainForm.Instance?.ShowOverlay($"{selected[0]}→{toggled[0]}");
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

        public static string? ProcessPunctuation(int vKey, bool useKatakana, Action<string> setLastOutputChar)
        {
            string? ch = vKey switch
            {
                VCode.OemYen => useKatakana ? "|" : "¥",
                VCode.OemColon => useKatakana ? ":" : "・",
                VCode.OemComma => useKatakana ? "、" : ",",
                VCode.OemPeriod => useKatakana ? "。" : ".",
                VCode.OemSlash => useKatakana ? "ー" : "/",
                _ => null
            };

            if (ch != null)
            {
                MainForm.Instance?.ShowOverlay(ch);
                setLastOutputChar(ch);
            }
            return ch;
        }

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
                MainForm.Instance?.ShowOverlay("영어 소문자 모드"); 
                return true; 
            } 
            if (!isHangulMode || !capsOn) {
                Japanese1Map.SetLayer(1);
                ImeState.SetHangulState(hFore, true);
                if (!capsOn) NativeMethods.SimulateCapsLock();
                MainForm.Instance?.ShowOverlay("일본어1_조합형");
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
                MainForm.Instance?.ShowOverlay("영어 소문자 모드"); 
                return true; 
            } 
            if (!isHangulMode || !capsOn) {
                Japanese1Map.SetLayer(2);
                ImeState.SetHangulState(hFore, true);
                if (!capsOn) NativeMethods.SimulateCapsLock();
                MainForm.Instance?.ShowOverlay("일본어2_조합형");
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

            { VCode.vk_Q, 401 }, { VCode.vk_W, 201 }, { VCode.vk_E, 101 }, { VCode.vk_R, 301 },
            { VCode.vk_A, 400 }, { VCode.vk_S, 200 }, { VCode.vk_D, 100 }, { VCode.vk_F, 300 },
            { VCode.vk_Z, 402 }, { VCode.vk_X, 600 }, { VCode.vk_C, 700 }, { VCode.vk_V, 500 }
        };

        private static readonly Dictionary<int, ushort> _vowelOffset = new()
        {
            // { vk_T, ("っ" : "ッ") }, { vk_G, ("ん" : "ン") },
            // { vk_Y, ("わ" : "ワ") }, { vk_U, ("を" : "ヲ") }, { vk_I, ("や" : "ヤ") }, { vk_O, ("よ" : "ヨ") }, { vk_P, ("ゆ" : "ユ") },
            // { vk_H, ("あ" : "ア") }, { vk_J, ("い" : "イ") }, { vk_K, ("う" : "ウ") }, { vk_L, ("お" : "オ") }, { vk_M, ("え" : "エ") }

            { VCode.vk_H, 00 }, { VCode.vk_J, 10 }, { VCode.vk_K, 20 }, { VCode.vk_M, 30 }, { VCode.vk_L, 40 }
        };

        private static readonly Dictionary<int, ushort> _soloMap = new()
        {
            // { vk_Q, ("ば" : "バ") }, { vk_W, ("ざ" : "ザ") }, { vk_E, ("が" : "ガ") }, { vk_R, ("だ" : "ダ") }, 
            // { vk_A, ("は" : "ハ") }, { vk_S, ("さ" : "サ") }, { vk_D, ("か" : "カ") }, { vk_F, ("た" : "タ") }, 
            // { vk_Z, ("ぱ" : "パ") }, { vk_X, ("ま" : "マ") }, { vk_C, ("ら" : "ラ") }, { vk_V, ("な" : "ナ") }

            { VCode.vk_T, 323 }, { VCode.vk_G, 920 },
            { VCode.vk_Y, 900 }, { VCode.vk_U, 940 }, { VCode.vk_I, 800 }, { VCode.vk_O, 840 }, { VCode.vk_P, 820 },
            { VCode.vk_H, 000 }, { VCode.vk_J, 010 }, { VCode.vk_K, 020 }, { VCode.vk_L, 040 }, { VCode.vk_M, 030 }
        };

        // Layer2 모음 고정 매핑
        private static readonly Dictionary<ushort, ushort> _previewMapL2 = new()
        {
            // { vk_Q, ("ば" : "バ") }, { vk_W, ("じ" : "ジ") }, { vk_E, ("が" : "ガ") }, { vk_R, ("で" : "デ") },
            // { vk_A, ("は" : "ハ") }, { vk_S, ("し" : "シ") }, { vk_D, ("か" : "カ") }, { vk_F, ("て" : "テ") }, 
            // { vk_Z, ("ぱ" : "パ") }, { vk_X, ("も" : "モ") }, { vk_C, ("る" : "ル") }, { vk_V, ("の" : "ノ") }

            { 401, 401 }, { 201, 211 }, { 101, 101 }, { 301, 331 },
            { 400, 400 }, { 200, 210 }, { 100, 100 }, { 300, 330 },
            { 402, 402 }, { 600, 640 }, { 700, 720 }, { 500, 540 }
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
                
            MainForm.Instance?.ShowOverlay(_pendingChar, 0);
        }
    
        public static void TogglePendingYn()
        {
            if (!_waitingVowel) return; _ynToggleCount++;
            _pendingChar = JapaneseCharacterProcessor.ProcessYN(_pendingChar);
            
            MainForm.Instance?.ShowOverlay(_pendingChar, 0);
        }
    
        public static void HandleHiraganaKatakanaTransformation() =>
            JapaneseTransformationHelper.HandleHiraganaKatakana(_lastOutputChar, SetLastOutputChar, () => {
                _isKatakana = !_isKatakana; 
                _lastOutputChar = ""; 
                MainForm.Instance?.ShowOverlay(_isKatakana ? "Katakana" : "Hiragana");
            });
    
        public static void HandleYoonTransformation() =>
            JapaneseTransformationHelper.HandleYoon(_lastOutputChar, SetLastOutputChar);

        public static bool ProcessKeyDownShared(int vKey, bool isShift, bool capsOn, IntPtr hFore, bool isHangulMode)
        {
            if (vKey is >= 0x21 and <= 0x28) { if (!isShift) SetLastOutputChar(""); return false; }

            // 리팩토링된 B키(HK) / N키(YN) 처리 부분
            if (vKey == VCode.vk_B && capsOn && isHangulMode) 
            { 
                if (_waitingVowel) ApplyPendingTransformation(JapaneseCharacterProcessor.ProcessHK); else HandleHiraganaKatakanaTransformation(); 
                return true; 
            }
            if (vKey == VCode.vk_N && capsOn && isHangulMode) 
            { 
                if (_waitingVowel) ApplyPendingTransformation(JapaneseCharacterProcessor.ProcessYN); else HandleYoonTransformation(); 
                return true; 
            }

            if (!capsOn || !isHangulMode) return false;
            if (TextSelectionUtils.IsConverting) return true;

            string? punct = JapaneseTransformationHelper.ProcessPunctuation(vKey, IsKatakana, SetLastOutputChar);
            if (punct != null)
            {
                GlobalInputHook.IsSending = true; NativeMethods.SendUnicodeString(punct); GlobalInputHook.IsSending = false;
                GlobalInputHook.AppendComposition(punct);
                return true;
            }

            string? sym = JapaneseTransformationHelper.ProcessSymbolOrNumber(vKey, IsKatakana, SetLastOutputChar);
            if (sym != null)
            {
                GlobalInputHook.IsSending = true; NativeMethods.SendUnicodeString(sym); GlobalInputHook.IsSending = false;
                GlobalInputHook.AppendComposition(sym);
                return true;
            }

            string? keyResult = ProcessKey(vKey, isShift);
            if (keyResult == null)
            {
                SetLastOutputChar("");
                if (vKey >= 0x41 && vKey <= 0x5A) return true; // 한글 변환 방지
                return false;
            }

            if (keyResult.Length > 0)
            {
                GlobalInputHook.IsSending = true; NativeMethods.SendUnicodeString(keyResult); GlobalInputHook.IsSending = false;
                GlobalInputHook.AppendComposition(keyResult);
            }
            return true;
        }
    
        public static string? ProcessKey(int vKey, bool isShift)
        {
            bool useKatakana = isShift ^ _isKatakana;
            string flushChar = ""; // 1) 미확정 문자 보존을 위한 임시 변수

            // 1) & 2) 미확정 문자 보존 및 상태 초기화
            if (_waitingVowel && !_vowelOffset.ContainsKey(vKey))
            {
                flushChar = _pendingChar;
                
                // 대기 상태 완전히 초기화하여 다음 입력을 새 입력으로 처리할 수 있도록 정리
                _waitingVowel = false;
                _pendingConsonant = 0;
                _pendingChar = "";
                _ynToggleCount = 0;
            }

            string? newResult = null; // 2번째 키에 대한 새로운 결괏값

            // 기존 기호 및 숫자 처리 로직
            string? punct = JapaneseTransformationHelper.ProcessPunctuation(vKey, useKatakana, SetLastOutputChar);
            if (punct != null)
            {
                newResult = punct;
            }
            else
            {
                string? sym = JapaneseTransformationHelper.ProcessSymbolOrNumber(vKey, useKatakana, SetLastOutputChar);
                if (sym != null)
                {
                    newResult = sym;
                }
                else if (vKey == VCode.vk_B || vKey == VCode.vk_N)
                {
                    newResult = null;
                }
                else if (_waitingVowel) // 위에서 모음이 아닌 경우는 걸러졌으므로, 여기는 반드시 모음 입력임
                {
                    if (_vowelOffset.TryGetValue(vKey, out ushort vOffset))
                    {
                        ushort code = (ushort)(_pendingConsonant + vOffset);
                        var jpChar = JapaneseCharacter.FromCode(code);
                        string result = _isKatakana ? jpChar.Katakana.ToString() : jpChar.Hiragana.ToString();
                        for (int i = 0; i < _ynToggleCount; i++) result = JapaneseCharacterProcessor.ProcessYN(result);

                        string currentPending = _pendingChar;
                        var vowChar = JapaneseCharacter.FromCode(vOffset);
                        string previewVow = _isKatakana ? vowChar.Katakana.ToString() : vowChar.Hiragana.ToString();
                        
                        MainForm.Instance?.ShowOverlay($"{currentPending}+{previewVow}={result}");

                        _waitingVowel = false; _pendingConsonant = 0; _pendingChar = ""; _ynToggleCount = 0; _lastOutputChar = result; 
                        newResult = result;
                    }
                }
                else if (_consonantBase.TryGetValue(vKey, out ushort cBase))
                {
                    _waitingVowel = true; _pendingConsonant = cBase; _isKatakana = useKatakana; _ynToggleCount = 0; _pendingChar = GetPreview(cBase);
                    
                    MainForm.Instance?.ShowOverlay(_pendingChar, 0);
                    newResult = "";
                }
                else if (_soloMap.TryGetValue(vKey, out ushort soloCode))
                {
                    var jpChar = JapaneseCharacter.FromCode(soloCode);
                    string ch = useKatakana ? jpChar.Katakana.ToString() : jpChar.Hiragana.ToString();
                    MainForm.Instance?.ShowOverlay(ch); 
                    _lastOutputChar = ch; 
                    newResult = ch;
                }
                else
                {
                    _lastOutputChar = "";
                }
            }

            // 3) 결합 반환
            // 보존된 문자(flushChar)가 있거나, 새롭게 처리된 결과(newResult)가 있다면 결합하여 반환
            if (!string.IsNullOrEmpty(flushChar) || newResult != null)
            {
                return flushChar + (newResult ?? "");
            }

            return null;
        }

        private static void ApplyPendingTransformation(Func<string, string> transformFunc)
        {
            string preview = transformFunc(_pendingChar);
            MainForm.Instance?.ShowOverlay($"{_pendingChar[0]}→{preview[0]}");
            
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

            { VCode.vk_Q, 730 }, { VCode.vk_W, 740 }, { VCode.vk_E, 720 }, { VCode.vk_R, 710 }, { VCode.vk_T, 700 }, 
            { VCode.vk_A, 530 }, { VCode.vk_S, 540 }, { VCode.vk_D, 520 }, { VCode.vk_F, 510 }, { VCode.vk_G, 500 },
            { VCode.vk_Z, 630 }, { VCode.vk_X, 640 }, { VCode.vk_C, 620 }, { VCode.vk_V, 610 }, { VCode.vk_B, 600 },
            { VCode.vk_Y, 400 }, { VCode.vk_U, 410 }, { VCode.vk_I, 420 }, { VCode.vk_O, 440 }, { VCode.vk_P, 430 }, 
            { VCode.vk_H, 920 }, { VCode.vk_J, 000 }, { VCode.vk_K, 010 }, { VCode.vk_L, 020 }, 
            { VCode.vk_N, 040 }, { VCode.vk_M, 030 }
        };

        private static readonly Dictionary<int, ushort> _layer2Map = new()
        {
            // { vk_Q, ( "ケ" : "け") }, { vk_W, ( "コ" : "こ") }, { vk_E, ( "ク" : "く") }, { vk_R, ( "キ" : "き") }, { vk_T, ( "カ" : "か") },
            // { vk_A, ( "テ" : "て") }, { vk_S, ( "ト" : "と") }, { vk_D, ( "ツ" : "つ") }, { vk_F, ( "チ" : "ち") }, { vk_G, ( "タ" : "た") },
            // { vk_Z, ( "セ" : "せ") }, { vk_X, ( "ソ" : "そ") }, { vk_C, ( "스" : "す") }, { vk_V, ( "シ" : "し") }, { vk_B, ( "サ" : "さ") },
            // { vk_Y, ( "パ" : "ぱ") }, { vk_U, ( "ピ" : "ぴ") }, { vk_I, ( "プ" : "ぷ") }, { vk_O, ( "PO" : "ぽ") }, { vk_P, ( "ペ" : "ぺ") }, 
            // { vk_H, ( "ッ" : "っ") }, { vk_J, ( "ヤ" : "や") }, { vk_K, ( "ヨ" : "よ") }, { vk_L, ( "ユ" : "ゆ") }, 
            // { vk_N, ( "ヲ" : "を") }, { vk_M, ( "ワ" : "わ") }

            { VCode.vk_Q, 130 }, { VCode.vk_W, 140 }, { VCode.vk_E, 120 }, { VCode.vk_R, 110 }, { VCode.vk_T, 100 },
            { VCode.vk_A, 330 }, { VCode.vk_S, 340 }, { VCode.vk_D, 320 }, { VCode.vk_F, 310 }, { VCode.vk_G, 300 },
            { VCode.vk_Z, 230 }, { VCode.vk_X, 240 }, { VCode.vk_C, 220 }, { VCode.vk_V, 210 }, { VCode.vk_B, 200 },
            { VCode.vk_Y, 402 }, { VCode.vk_U, 412 }, { VCode.vk_I, 422 }, { VCode.vk_O, 442 }, { VCode.vk_P, 432 }, 
            { VCode.vk_H, 323 }, { VCode.vk_J, 800 }, { VCode.vk_K, 840 }, { VCode.vk_L, 820 }, 
            { VCode.vk_N, 940 }, { VCode.vk_M, 900 }
        };

        private static readonly Dictionary<int, ushort> _layer3Map = new()
        {
            // { vk_Q, ( "ゲ" : "げ") }, { vk_W, ( "ゴ" : "ご") }, { vk_E, ( "グ" : "ぐ") }, { vk_R, ( "ギ" : "ぎ") }, { vk_T, ( "ガ" : "が") }, 
            // { vk_A, ( "デ" : "で") }, { vk_S, ( "ド" : "ど") }, { vk_D, ( "ヅ" : "づ") }, { vk_F, ( "ヂ" : "ぢ") }, { vk_G, ( "ダ" : "だ") },
            // { vk_Z, ( "ゼ" : "ぜ") }, { vk_X, ( "ゾ" : "ぞ") }, { vk_C, ( "ズ" : "ず") }, { vk_V, ( "ジ" : "じ") }, { vk_B, ( "ザ" : "ざ") }, 
            // { vk_Y, ( "バ" : "ば") }, { vk_U, ( "ビ" : "び") }, { vk_I, ( "ブ" : "ぶ") }, { vk_O, ( "ボ" : "ぼ") }, { vk_P, ( "ベ" : "べ") }, 
            // { vk_H, ( "ィ" : "ヴ") }, { vk_J, ( "ャ" : "ゃ") }, { vk_K, ( "ョ" : "ょ") }, { vk_L, ( "ュ" : "ゅ") }

            { VCode.vk_Q, 131 }, { VCode.vk_W, 141 }, { VCode.vk_E, 121 }, { VCode.vk_R, 111 }, { VCode.vk_T, 101 }, 
            { VCode.vk_A, 331 }, { VCode.vk_S, 341 }, { VCode.vk_D, 321 }, { VCode.vk_F, 311 }, { VCode.vk_G, 301 },
            { VCode.vk_Z, 231 }, { VCode.vk_X, 241 }, { VCode.vk_C, 221 }, { VCode.vk_V, 211 }, { VCode.vk_B, 201 },
            { VCode.vk_Y, 401 }, { VCode.vk_U, 411 }, { VCode.vk_I, 421 }, { VCode.vk_O, 441 }, { VCode.vk_P, 431 },
            { VCode.vk_J, 803 }, { VCode.vk_K, 843 }, { VCode.vk_L, 823 } // {VCode.vk_H, ( 013 , 021 )}
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
                MainForm.Instance?.ShowOverlay(_isVirtualShift ? "Katakana" : "Hiragana");
            });

        public static void HandleYoonTransformation() =>
            JapaneseTransformationHelper.HandleYoon(_lastOutputChar, SetLastOutputChar);

        public static bool ProcessKeyDownShared(int vKey, bool isShift, bool capsOn, IntPtr hFore, bool isHangulMode)
        {
            if (vKey is >= 0x21 and <= 0x28) { if (!isShift) SetLastOutputChar(""); return false; }
            if (vKey == VCode.vk_B && capsOn && isHangulMode) { HandleHiraganaKatakanaTransformation(); return true; }
            if (vKey == VCode.vk_N && capsOn && isHangulMode) { HandleYoonTransformation(); return true; }
            if (!capsOn || !isHangulMode) return false;
            if (TextSelectionUtils.IsConverting) return true;

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

            if (vKey == VCode.vk_B || vKey == VCode.vk_N) return true;

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
                    MainForm.Instance?.ShowOverlay("영어 소문자 모드");
                }
                else
                {
                    Japanese3Map.SetLayer(newLayer);
                    MainForm.Instance?.ShowOverlay($"일본어3_Layer{newLayer}");
                }
                return true;
            }
            if (!isHangulMode || !capsOn)
            {
                Japanese3Map.SetLayer(1);
                ImeState.SetHangulState(hFore, true);
                if (!capsOn) NativeMethods.SimulateCapsLock();
                MainForm.Instance?.ShowOverlay("일본어3_Layer1");
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