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
        private static readonly HashSet<int> _consonantKeys = new() { VCode.vk_Q, VCode.vk_W, VCode.vk_E, VCode.vk_R, VCode.vk_A, VCode.vk_S, VCode.vk_D, VCode.vk_F, VCode.vk_Z, VCode.vk_X, VCode.vk_C, VCode.vk_V };
        private static readonly HashSet<int> _vowelKeys = new() { VCode.vk_H, VCode.vk_J, VCode.vk_K, VCode.vk_L, VCode.vk_M };

        private static readonly Dictionary<(int Con, int Vow), (string Hira, string Kata)> _combineMap = new()
        {
            { (VCode.vk_Q, VCode.vk_H), ("ば","バ") }, { (VCode.vk_Q, VCode.vk_J), ("び","ビ") }, { (VCode.vk_Q, VCode.vk_K), ("ぶ","ブ") }, { (VCode.vk_Q, VCode.vk_M), ("べ","ベ") }, { (VCode.vk_Q, VCode.vk_L), ("ぼ","ボ") },
            { (VCode.vk_W, VCode.vk_H), ("ざ","ザ") }, { (VCode.vk_W, VCode.vk_J), ("じ","ジ") }, { (VCode.vk_W, VCode.vk_K), ("ず","ズ") }, { (VCode.vk_W, VCode.vk_M), ("ぜ","ゼ") }, { (VCode.vk_W, VCode.vk_L), ("ぞ","ゾ") },
            { (VCode.vk_E, VCode.vk_H), ("が","ガ") }, { (VCode.vk_E, VCode.vk_J), ("ぎ","ギ") }, { (VCode.vk_E, VCode.vk_K), ("ぐ","グ") }, { (VCode.vk_E, VCode.vk_M), ("げ","ゲ") }, { (VCode.vk_E, VCode.vk_L), ("ご","ゴ") },
            { (VCode.vk_R, VCode.vk_H), ("だ","ダ") }, { (VCode.vk_R, VCode.vk_J), ("ぢ","ヂ") }, { (VCode.vk_R, VCode.vk_K), ("づ","ヅ") }, { (VCode.vk_R, VCode.vk_M), ("で","デ") }, { (VCode.vk_R, VCode.vk_L), ("ど","ド") },
            { (VCode.vk_A, VCode.vk_H), ("は","ハ") }, { (VCode.vk_A, VCode.vk_J), ("ひ","ヒ") }, { (VCode.vk_A, VCode.vk_K), ("ふ","フ") }, { (VCode.vk_A, VCode.vk_M), ("へ","ヘ") }, { (VCode.vk_A, VCode.vk_L), ("ほ","ホ") },
            { (VCode.vk_S, VCode.vk_H), ("さ","サ") }, { (VCode.vk_S, VCode.vk_J), ("し","シ") }, { (VCode.vk_S, VCode.vk_K), ("す","ス") }, { (VCode.vk_S, VCode.vk_M), ("せ","セ") }, { (VCode.vk_S, VCode.vk_L), ("そ","ソ") },
            { (VCode.vk_D, VCode.vk_H), ("か","カ") }, { (VCode.vk_D, VCode.vk_J), ("き","キ") }, { (VCode.vk_D, VCode.vk_K), ("く","ク") }, { (VCode.vk_D, VCode.vk_M), ("け","ケ") }, { (VCode.vk_D, VCode.vk_L), ("こ","コ") },
            { (VCode.vk_F, VCode.vk_H), ("た","タ") }, { (VCode.vk_F, VCode.vk_J), ("ち","チ") }, { (VCode.vk_F, VCode.vk_K), ("つ","ツ") }, { (VCode.vk_F, VCode.vk_M), ("て","テ") }, { (VCode.vk_F, VCode.vk_L), ("と","ト") },
            { (VCode.vk_Z, VCode.vk_H), ("ぱ","パ") }, { (VCode.vk_Z, VCode.vk_J), ("ぴ","ピ") }, { (VCode.vk_Z, VCode.vk_K), ("ぷ","プ") }, { (VCode.vk_Z, VCode.vk_M), ("ぺ","ペ") }, { (VCode.vk_Z, VCode.vk_L), ("ぽ","ポ") },
            { (VCode.vk_X, VCode.vk_H), ("ま","マ") }, { (VCode.vk_X, VCode.vk_J), ("み","ミ") }, { (VCode.vk_X, VCode.vk_K), ("む","ム") }, { (VCode.vk_X, VCode.vk_M), ("め","メ") }, { (VCode.vk_X, VCode.vk_L), ("も","モ") },
            { (VCode.vk_C, VCode.vk_H), ("ら","ラ") }, { (VCode.vk_C, VCode.vk_J), ("り","リ") }, { (VCode.vk_C, VCode.vk_K), ("る","ル") }, { (VCode.vk_C, VCode.vk_M), ("れ","レ") }, { (VCode.vk_C, VCode.vk_L), ("ろ","ロ") },
            { (VCode.vk_V, VCode.vk_H), ("な","ナ") }, { (VCode.vk_V, VCode.vk_J), ("に","ニ") }, { (VCode.vk_V, VCode.vk_K), ("ぬ","ヌ") }, { (VCode.vk_V, VCode.vk_M), ("ね","ネ") }, { (VCode.vk_V, VCode.vk_L), ("の","ノ") },
        };

        private static readonly Dictionary<int, (string Hira, string Kata)> _soloMap = new()
        {
            { VCode.vk_T, ("っ","ッ") }, { VCode.vk_G, ("ん","ン") },
            { VCode.vk_Y, ("わ","ワ") }, { VCode.vk_U, ("を","ヲ") }, { VCode.vk_I, ("や","ヤ") }, { VCode.vk_O, ("よ","ヨ") }, { VCode.vk_P, ("ゆ","ユ") },
            { VCode.vk_H, ("あ","ア") }, { VCode.vk_J, ("い","イ") }, { VCode.vk_K, ("う","ウ") }, { VCode.vk_L, ("お","オ") }, { VCode.vk_M, ("え","エ") }
        };

        private static readonly Dictionary<int, (string Hira, string Kata)> _previewMapL1 = new()
        {
            { VCode.vk_Q, ("ば","バ") }, { VCode.vk_W, ("ざ","ザ") }, { VCode.vk_E, ("が","ガ") }, { VCode.vk_R, ("だ","ダ") }, 
            { VCode.vk_A, ("は","ハ") }, { VCode.vk_S, ("さ","サ") }, { VCode.vk_D, ("か","カ") }, { VCode.vk_F, ("た","タ") }, 
            { VCode.vk_Z, ("ぱ","パ") }, { VCode.vk_X, ("ま","マ") }, { VCode.vk_C, ("ら","ラ") }, { VCode.vk_V, ("な","ナ") },
        };

        private static readonly Dictionary<int, (string Hira, string Kata)> _previewMapL2 = new()
        {
            { VCode.vk_Q, ("ば","バ") }, { VCode.vk_W, ("じ","ジ") }, { VCode.vk_E, ("が","ガ") }, { VCode.vk_R, ("で","デ") },
            { VCode.vk_A, ("は","ハ") }, { VCode.vk_S, ("し","シ") }, { VCode.vk_D, ("か","カ") }, { VCode.vk_F, ("て","テ") }, 
            { VCode.vk_Z, ("ぱ","パ") }, { VCode.vk_X, ("も","モ") }, { VCode.vk_C, ("る","ル") }, { VCode.vk_V, ("の","ノ") },
        };

        private static bool _isKatakana = false;
        private static bool _waitingVowel = false;
        private static int _pendingConsonant = 0;
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
            if (vKey == VCode.vk_B && capsOn && isHangulMode) { HandleHiraganaKatakanaTransformation(); return true; }
            if (vKey == VCode.vk_N && capsOn && isHangulMode) { HandleYoonTransformation(); return true; }
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
                // 한글CAPS 모드에서 알파벳 키 유출(한글 입력) 방지를 위해 차단
                if (vKey >= 0x41 && vKey <= 0x5A) return true;
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

            string? punct = JapaneseTransformationHelper.ProcessPunctuation(vKey, useKatakana, SetLastOutputChar);
            if (punct != null) return punct;

            string? sym = JapaneseTransformationHelper.ProcessSymbolOrNumber(vKey, useKatakana, SetLastOutputChar);
            if (sym != null) return sym;

            if (vKey == VCode.vk_B || vKey == VCode.vk_N) return null;

            if (_waitingVowel)
            {
                if (_vowelKeys.Contains(vKey))
                {
                    var key = (_pendingConsonant, vKey);
                    if (_combineMap.TryGetValue(key, out var combined))
                    {
                        string result = _isKatakana ? combined.Kata : combined.Hira;
                        for (int i = 0; i < _ynToggleCount; i++) result = JapaneseCharacterProcessor.ProcessYN(result);

                        string currentPending = _pendingChar;
                        string previewVow = vKey switch { VCode.vk_H => _isKatakana ? "ア" : "あ", VCode.vk_J => _isKatakana ? "イ" : "い", VCode.vk_K => _isKatakana ? "ウ" : "う", VCode.vk_M => _isKatakana ? "エ" : "え", VCode.vk_L => _isKatakana ? "オ" : "お", _ => "?" };                        
                        MainForm.Instance?.ShowOverlay($"{currentPending}+{previewVow}={result}");

                        _waitingVowel = false; _pendingConsonant = 0; _pendingChar = ""; _ynToggleCount = 0; _lastOutputChar = result; return result;
                    }
                }
            }
    
            if (_consonantKeys.Contains(vKey))
            {
                _waitingVowel = true; _pendingConsonant = vKey; _isKatakana = useKatakana; _ynToggleCount = 0; _pendingChar = GetPreview(vKey);
                
                MainForm.Instance?.ShowOverlay(_pendingChar, 0);
                return "";
            }
    
            if (_soloMap.TryGetValue(vKey, out var solo))
            {
                string ch = useKatakana ? solo.Kata : solo.Hira;
                MainForm.Instance?.ShowOverlay(ch); 
                _lastOutputChar = ch; return ch;
            }
    
            _lastOutputChar = ""; return null;
        }
    
        private static string GetPreview(int vKey)
        {
            var map = CurrentLayer == 1 ? _previewMapL1 : _previewMapL2;
            if (map.TryGetValue(vKey, out var p)) return _isKatakana ? p.Kata : p.Hira;
            return "?";
        }
    }

    internal static class Japanese3Map
    {
        private static bool _isKatakana = false;
        private static string _lastOutputChar = "";
        public static int CurrentLayer { get; private set; } = 1;

        public static bool IsKatakana => _isKatakana;

        public static void SetLastOutputChar(string ch) => _lastOutputChar = ch;

        public static void CycleLayerOrSwitchToEnglish(IntPtr hFore) 
        { 
            if (CurrentLayer == 1)
            {
                CurrentLayer = 2;
                MainForm.Instance?.ShowOverlay("Layer2");
            }
            else if (CurrentLayer == 2)
            {
                CurrentLayer = 3;
                MainForm.Instance?.ShowOverlay("Layer3");
            }
            else
            {
                CurrentLayer = 1;
                ImeState.SetHangulState(hFore, false);
                NativeMethods.SimulateCapsLock();
                MainForm.Instance?.ShowOverlay("영어 소문자 모드");
            }
        }

        public static void TogglePendingHiraKataModeOnly() => _isKatakana = !_isKatakana;

        public static void HandleHiraganaKatakanaTransformation() =>
            JapaneseTransformationHelper.HandleHiraganaKatakana(_lastOutputChar, SetLastOutputChar, () => {
                _isKatakana = !_isKatakana; 
                _lastOutputChar = ""; 
                MainForm.Instance?.ShowOverlay(_isKatakana ? "Katakana" : "Hiragana");
            });

        public static void HandleYoonTransformation() =>
            JapaneseTransformationHelper.HandleYoon(_lastOutputChar, SetLastOutputChar);

        public static string? ProcessKey(int vKey, bool isShift)
        {
            bool useKatakana = isShift ^ _isKatakana;

            string? punct = JapaneseTransformationHelper.ProcessPunctuation(vKey, useKatakana, SetLastOutputChar);
            if (punct != null) return punct;

            string? sym = JapaneseTransformationHelper.ProcessSymbolOrNumber(vKey, useKatakana, SetLastOutputChar);
            if (sym != null) return sym;

            string? ch = null;

            if (CurrentLayer == 1)
            {
                ch = vKey switch
                {
                    VCode.vk_Q => useKatakana ? "レ" : "れ", VCode.vk_W => useKatakana ? "ロ" : "ろ", VCode.vk_E => useKatakana ? "ル" : "る", VCode.vk_R => useKatakana ? "リ" : "り", VCode.vk_T => useKatakana ? "ラ" : "ら", 
                    VCode.vk_Y => useKatakana ? "ハ" : "は", VCode.vk_U => useKatakana ? "ヒ" : "ひ", VCode.vk_I => useKatakana ? "フ" : "ふ", VCode.vk_O => useKatakana ? "ホ" : "ほ", VCode.vk_P => useKatakana ? "ヘ" : "へ", 
                    VCode.vk_A => useKatakana ? "ネ" : "ね", VCode.vk_S => useKatakana ? "ノ" : "の", VCode.vk_D => useKatakana ? "ヌ" : "ぬ", VCode.vk_F => useKatakana ? "ニ" : "に", VCode.vk_G => useKatakana ? "ナ" : "な",
                    VCode.vk_H => useKatakana ? "ン" : "ん", VCode.vk_J => useKatakana ? "ア" : "あ", VCode.vk_K => useKatakana ? "イ" : "い", VCode.vk_L => useKatakana ? "ウ" : "う", 
                    VCode.vk_Z => useKatakana ? "メ" : "め", VCode.vk_X => useKatakana ? "モ" : "も", VCode.vk_C => useKatakana ? "ム" : "む", VCode.vk_V => useKatakana ? "ミ" : "み", VCode.vk_B => useKatakana ? "マ" : "ま",
                    VCode.vk_N => useKatakana ? "オ" : "お", VCode.vk_M => useKatakana ? "エ" : "え",
                    _ => null
                };
            }
            else if (CurrentLayer == 2)
            {
                ch = vKey switch
                {
                    VCode.vk_Q => useKatakana ? "ケ" : "け", VCode.vk_W => useKatakana ? "コ" : "こ", VCode.vk_E => useKatakana ? "ク" : "く", VCode.vk_R => useKatakana ? "キ" : "き", VCode.vk_T => useKatakana ? "カ" : "か",
                    VCode.vk_Y => useKatakana ? "パ" : "ぱ", VCode.vk_U => useKatakana ? "ピ" : "ぴ", VCode.vk_I => useKatakana ? "プ" : "ぷ", VCode.vk_O => useKatakana ? "ポ" : "ぽ", VCode.vk_P => useKatakana ? "ペ" : "ぺ", 
                    VCode.vk_A => useKatakana ? "テ" : "て", VCode.vk_S => useKatakana ? "ト" : "と", VCode.vk_D => useKatakana ? "ツ" : "つ", VCode.vk_F => useKatakana ? "チ" : "ち", VCode.vk_G => useKatakana ? "タ" : "た",
                    VCode.vk_H => useKatakana ? "ッ" : "っ", VCode.vk_J => useKatakana ? "ヤ" : "や", VCode.vk_K => useKatakana ? "ヨ" : "よ", VCode.vk_L => useKatakana ? "ユ" : "ゆ", 
                    VCode.vk_Z => useKatakana ? "セ" : "せ", VCode.vk_X => useKatakana ? "ソ" : "そ", VCode.vk_C => useKatakana ? "스" : "す", VCode.vk_V => useKatakana ? "シ" : "し", VCode.vk_B => useKatakana ? "サ" : "さ",
                    VCode.vk_N => useKatakana ? "ヲ" : "を", VCode.vk_M => useKatakana ? "ワ" : "わ", 
                    _ => null
                };
            }
            else if (CurrentLayer == 3)
            {
                ch = vKey switch
                {
                    VCode.vk_Q => useKatakana ? "ゲ" : "げ", VCode.vk_W => useKatakana ? "ゴ" : "ご", VCode.vk_E => useKatakana ? "グ" : "ぐ", VCode.vk_R => useKatakana ? "ギ" : "ぎ", VCode.vk_T => useKatakana ? "ガ" : "が", 
                    VCode.vk_Y => useKatakana ? "バ" : "ば", VCode.vk_U => useKatakana ? "ビ" : "び", VCode.vk_I => useKatakana ? "ブ" : "ぶ", VCode.vk_O => useKatakana ? "ボ" : "ぼ", VCode.vk_P => useKatakana ? "ベ" : "べ", 
                    VCode.vk_A => useKatakana ? "デ" : "で", VCode.vk_S => useKatakana ? "ド" : "ど", VCode.vk_D => useKatakana ? "ヅ" : "づ", VCode.vk_F => useKatakana ? "ヂ" : "ぢ", VCode.vk_G => useKatakana ? "ダ" : "だ",
                    VCode.vk_H => useKatakana ? "ィ" : "ヴ", VCode.vk_J => useKatakana ? "ャ" : "ゃ", VCode.vk_K => useKatakana ? "ョ" : "ょ", VCode.vk_L => useKatakana ? "ュ" : "ゅ", 
                    VCode.vk_Z => useKatakana ? "ゼ" : "ぜ", VCode.vk_X => useKatakana ? "ゾ" : "ぞ", VCode.vk_C => useKatakana ? "ズ" : "ず", VCode.vk_V => useKatakana ? "ジ" : "じ", VCode.vk_B => useKatakana ? "ザ" : "ざ", 
                    _ => null
                };
            }

            if (ch != null) 
            { 
                MainForm.Instance?.ShowOverlay(ch); 
                _lastOutputChar = ch; return ch; 
            }
            return null;
        }
    }

    internal class Japanese3Processor : IKeyProcessor
    {
        public bool IsVirtualShift => Japanese3Map.IsKatakana;
        public int CurrentLayer => Japanese3Map.CurrentLayer;

        public void ToggleVirtualShift() => Japanese3Map.TogglePendingHiraKataModeOnly();

        public bool ProcessHanjaKey(IntPtr hFore, bool capsOn, bool isHangulMode)
        {
            if (isHangulMode && capsOn) 
            { 
                Japanese3Map.CycleLayerOrSwitchToEnglish(hFore);
                return true;
            }
            if (!isHangulMode || !capsOn) 
            {
                ImeState.SetHangulState(hFore, true);
                if (!capsOn) NativeMethods.SimulateCapsLock();
                MainForm.Instance?.ShowOverlay("Layer" + Japanese3Map.CurrentLayer);
                return true;
            }
            return false;
        }

        public bool ProcessKeyDown(int vKey, bool isShift, bool capsOn, IntPtr hFore, bool isHangulMode)
        {
            isShift = KeyboardLayoutAnalyzer.CheckCopilotShift(isShift);

            if (vKey is >= 0x21 and <= 0x28) { if (!isShift) Japanese3Map.SetLastOutputChar(""); return false; }

            if (Japanese3Map.CurrentLayer == 3)
            {
                if (vKey == VCode.vk_N && capsOn && isHangulMode ) { Japanese3Map.HandleHiraganaKatakanaTransformation(); return true; }
                if (vKey == VCode.vk_M && capsOn && isHangulMode ) { Japanese3Map.HandleYoonTransformation(); return true; }
            }
            if (!capsOn || !isHangulMode) return false;
            if (TextSelectionUtils.IsConverting) return true;

            string? keyResult = Japanese3Map.ProcessKey(vKey, isShift);
            if (keyResult == null) { Japanese3Map.SetLastOutputChar(""); return false; }

            if (keyResult.Length > 0)
            {
                GlobalInputHook.IsSending = true; 
                NativeMethods.SendUnicodeString(keyResult); 
                GlobalInputHook.IsSending = false; 
                GlobalInputHook.AppendComposition(keyResult);
            }
            return true;
        }

        public void OnMouseClick() 
        {
            Japanese3Map.SetLastOutputChar("");
            GlobalInputHook.ClearCompositionBuffer();
        }
    }
    #endregion
}