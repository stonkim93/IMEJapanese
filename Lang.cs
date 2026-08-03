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
    internal static class VirtualKeyCodes
    {
        public const int Shift = 0x10;
        public const int Ctrl = 0x11;
        public const int Right = 0x27;
        public const int Escape = 0x1B;
        public const int Backspace = 0x08;

        public const int LWin = 0x5B;
        public const int RWin = 0x5C;

        public const int vk_B = 0x42;
        public const int vk_C = 0x43;
        public const int vk_H = 0x48;
        public const int vk_J = 0x4A;
        public const int vk_K = 0x4B;
        public const int vk_L = 0x4C;
        public const int vk_M = 0x4D;
        public const int vk_N = 0x4E;
        public const int vk_P = 0x50;
        public const int vk_Y = 0x59;

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
                bool winHeld = (NativeMethods.GetKeyState(VirtualKeyCodes.LWin) & 0x8000) != 0 || 
                               (NativeMethods.GetKeyState(VirtualKeyCodes.RWin) & 0x8000) != 0;
                if (winHeld) return false;
            }
            return isShift;
        }

        public static string? GetChar(int vkCode, bool isShift)
        {
            byte[] keyState = new byte[256];
            NativeMethods.GetKeyboardState(keyState);

            if (isShift) 
            {
                keyState[VirtualKeyCodes.Shift] = 0x80;
                keyState[0xA0] = 0x80; // VK_LSHIFT
                keyState[0xA1] = 0x80; // VK_RSHIFT
            }
            else
            {
                keyState[VirtualKeyCodes.Shift] = 0;
                keyState[0xA0] = 0;
                keyState[0xA1] = 0;
            }

            IntPtr hWnd = NativeMethods.GetForegroundWindow();
            uint threadId = NativeMethods.GetWindowThreadProcessId(hWnd, out _);
            IntPtr hkl = NativeMethods.GetKeyboardLayout(threadId);

            uint scanCode = NativeMethods.MapVirtualKeyEx((uint)vkCode, 0, hkl);
            StringBuilder sb = new StringBuilder(5);
            
            int result = NativeMethods.ToUnicodeEx((uint)vkCode, scanCode, keyState, sb, sb.Capacity, 0, hkl);
            
            if (result > 0)
            {
                string ch = sb.ToString();
                if (isShift && ch.Length == 1 && IsSymbolOrNumber(vkCode))
                {
                    string? shiftedFallback = GetStandardShiftedSymbol(vkCode);
                    if (shiftedFallback != null && char.IsDigit(ch[0])) return shiftedFallback;
                }
                return ch;
            }

            if (isShift && IsSymbolOrNumber(vkCode)) return GetStandardShiftedSymbol(vkCode);
            return null;
        }

        private static string? GetStandardShiftedSymbol(int vkCode)
        {
            return vkCode switch
            {
                0x31 => "!", 0x32 => "@", 0x33 => "#", 0x34 => "$", 0x35 => "%",
                0x36 => "^", 0x37 => "&", 0x38 => "*", 0x39 => "(", 0x30 => ")",
                0xC0 => "~", 0xBD => "_", 0xBB => "+", 0xDB => "{", 0xDD => "}",
                0xDC => "|", 0xBA => ":", 0xDE => "\"", 0xBC => "<", 0xBE => ">", 0xBF => "?",
                _ => null
            };
        }

        public static bool IsSymbolOrNumber(int vkCode)
        {
            return (vkCode >= 0x30 && vkCode <= 0x39) || (vkCode >= 0xBA && vkCode <= 0xC0) || (vkCode >= 0xDB && vkCode <= 0xDE);   
        }

        // [최적화] 사용하지 않는 HandleGlobalKey2Mode 및 관련 IsSymbolOrNumberOrLetter 제거 완료
    }
    #endregion

    #region [ 1. 인터페이스 및 팩토리 (Interfaces & Factories) ]
    internal interface IKeyProcessor
    {
        bool IsVirtualShift { get; }
        int CurrentLayer { get; }
        
        bool ProcessKeyDown(int vkCode, bool isShift, bool capsOn, IntPtr hFore, bool isHangulMode);
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
            if ((NativeMethods.GetKeyState(VirtualKeyCodes.LWin) & 0x8000) != 0) inputs.Add(MakeKeyUp(VirtualKeyCodes.LWin));
            if ((NativeMethods.GetKeyState(VirtualKeyCodes.RWin) & 0x8000) != 0) inputs.Add(MakeKeyUp(VirtualKeyCodes.RWin));
            if ((NativeMethods.GetKeyState(VirtualKeyCodes.Shift) & 0x8000) != 0) inputs.Add(MakeKeyUp(VirtualKeyCodes.Shift));
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

                bool shiftHeld = (NativeMethods.GetKeyState(VirtualKeyCodes.Shift) & 0x8000) != 0;
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
            try { bool shiftHeld = (NativeMethods.GetKeyState(VirtualKeyCodes.Shift) & 0x8000) != 0; SendRight(shiftHeld); Thread.Sleep(ClipboardConfig.SelectionCancelDelayMs); }
            catch { }
        }

        private static void SendRight(bool shiftHeld)
        {
            var inputs = new List<NativeMethods.INPUT>();
            if (shiftHeld) inputs.Add(MakeKeyUp(VirtualKeyCodes.Shift));
            inputs.Add(MakeKeyDown(VirtualKeyCodes.Right)); inputs.Add(MakeKeyUp(VirtualKeyCodes.Right));
            if (shiftHeld) inputs.Add(MakeKeyDown(VirtualKeyCodes.Shift));
            SendInputsSafe(inputs);
        }

        private static void SendCtrlC(bool shiftHeld)
        {
            var inputs = new List<NativeMethods.INPUT>();
            if (shiftHeld) inputs.Add(MakeKeyUp(VirtualKeyCodes.Shift));
            inputs.Add(MakeKeyDown(VirtualKeyCodes.Ctrl)); inputs.Add(MakeKeyDown(VirtualKeyCodes.vk_C));
            inputs.Add(MakeKeyUp(VirtualKeyCodes.vk_C)); inputs.Add(MakeKeyUp(VirtualKeyCodes.Ctrl));
            if (shiftHeld) inputs.Add(MakeKeyDown(VirtualKeyCodes.Shift));
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

    #region [ 3. 공용 데이터: 일본어 변환 체인 (Japanese Shared) ]
    internal static class JapaneseShared
    {
        public static readonly Dictionary<string, string> HiraToKata = new()
        {
            {"あ","ア"},{"い","イ"},{"う","ウ"},{"え","エ"},{"お","オ"},
            {"か","カ"},{"き","キ"},{"く","ク"},{"け","ケ"},{"こ","コ"},
            {"さ","サ"},{"し","シ"},{"す","ス"},{"せ","セ"},{"そ","ソ"},
            {"た","タ"},{"ち","チ"},{"つ","ツ"},{"て","テ"},{"と","ト"},
            {"な","ナ"},{"に","ニ"},{"ぬ","ヌ"},{"ね","ネ"},{"の","ノ"},
            {"は","ハ"},{"ひ","ヒ"},{"ふ","フ"},{"へ","ヘ"},{"ほ","ホ"},
            {"ま","マ"},{"み","ミ"},{"む","ム"},{"め","メ"},{"も","モ"},
            {"ら","ラ"},{"り","リ"},{"る","ル"},{"れ","レ"},{"ろ","ロ"},
            {"や","ヤ"},{"ゆ","ユ"},{"よ","ヨ"},{"わ","ワ"},{"を","ヲ"},
            {"が","ガ"},{"ぎ","ギ"},{"ぐ","グ"},{"げ","ゲ"},{"ご","ゴ"},
            {"ざ","ザ"},{"じ","ジ"},{"ず","ズ"},{"ぜ","ゼ"},{"ぞ","ゾ"},
            {"だ","ダ"},{"ぢ","ヂ"},{"づ","ヅ"},{"で","デ"},{"ど","ド"},
            {"ば","バ"},{"び","ビ"},{"ぶ","ブ"},{"べ","ベ"},{"ぼ","ボ"},
            {"ぱ","パ"},{"ぴ","ピ"},{"ぷ","プ"},{"ぺ","ペ"},{"ぽ","ポ"},
            {"ぁ","ァ"},{"ぃ","ィ"},{"ぅ","ゥ"},{"ぇ","ェ"},{"ぉ","ォ"},
            {"ゃ","ャ"},{"ゅ","ュ"},{"ょ","ョ"},{"ゎ","ヮ"},{"っ","ッ"},
            {"ん","ン"},{"ゔ","ヴ"}
        };

        public static readonly Dictionary<string, string> KataToHira = HiraToKata.ToDictionary(kv => kv.Value, kv => kv.Key);

        public static readonly Dictionary<string, string> TransformMap = new()
        {
            {"あ","ぁ"},{"ぁ","あ"},{"い","ぃ"},{"ぃ","い"},{"え","ぇ"},{"ぇ","え"},{"お","ぉ"},{"ぉ","お"},{"う","ゔ"},{"ゔ","ぅ"},{"ぅ","う"},
            {"や","ゃ"},{"ゃ","や"},{"ゆ","ゅ"},{"ゅ","ゆ"},{"よ","ょ"},{"ょ","よ"},{"わ","ゎ"},{"ゎ","わ"},
            {"か","が"},{"が","か"},{"き","ぎ"},{"ぎ","き"},{"く","ぐ"},{"ぐ","く"},{"け","げ"},{"げ","け"},{"こ","ご"},{"ご","こ"},
            {"さ","ざ"},{"ざ","さ"},{"し","じ"},{"じ","し"},{"す","ず"},{"ず","す"},{"せ","ぜ"},{"ぜ","せ"},{"そ","ぞ"},{"ぞ","そ"},
            {"た","だ"},{"だ","た"},{"ち","ぢ"},{"ぢ","ち"},{"て","で"},{"で","て"},{"つ","づ"},{"づ","っ"},{"っ","つ"},{"と","ど"},{"ど","と"},
            {"は","ば"},{"ば","ぱ"},{"ぱ","は"},{"ひ","び"},{"び","ぴ"},{"ぴ","ひ"},{"ふ","ぶ"},{"ぶ","ぷ"},{"ぷ","ふ"},{"へ","べ"},{"べ","ぺ"},{"ぺ","へ"},{"ほ","ぼ"},{"ぼ","ぽ"},{"ぽ","ほ"},
            {"ア","ァ"},{"ァ","ア"},{"イ","ィ"},{"ィ","イ"},{"エ","ェ"},{"ェ","エ"},{"オ","ォ"},{"ォ","オ"},{"ウ","ヴ"},{"ヴ","ゥ"},{"ゥ","ウ"},
            {"ヤ","ャ"},{"ャ","ヤ"},{"ユ","ュ"},{"ュ","ユ"},{"ヨ","ョ"},{"ョ","ヨ"},{"ワ","ヮ"},{"ヮ","ワ"},
            {"カ","ガ"},{"ガ","カ"},{"キ","ギ"},{"ギ","キ"},{"ク","グ"},{"グ","ク"},{"ケ","ゲ"},{"ゲ","ケ"},{"コ","ゴ"},{"ゴ","コ"},
            {"サ","ザ"},{"ザ","サ"},{"シ","ジ"},{"ジ","シ"},{"ス","ズ"},{"ズ","ス"},{"セ","ゼ"},{"ゼ","セ"},{"ソ","ゾ"},{"ゾ","ソ"},
            {"タ","ダ"},{"ダ","タ"},{"チ","ヂ"},{"ヂ","チ"},{"テ","デ"},{"デ","テ"},{"ツ","ヅ"},{"ヅ","ッ"},{"ッ","ツ"},{"ト","ド"},{"ド","ト"},
            {"ハ","バ"},{"バ","パ"},{"パ","ハ"},{"ヒ","ビ"},{"ビ","ピ"},{"ピ","ヒ"},{"フ","ブ"},{"ブ","プ"},{"プ","フ"},{"ヘ","ベ"},{"ベ","ペ"},{"ペ","ヘ"},{"ホ","ボ"},{"ボ","ポ"},{"ポ","ホ"},
        };

        private static readonly Dictionary<string, string?[]> YoonHiraganaChains = new()
        {
            {"あ", new string?[]{"あ",null,null,"ぁ"}}, {"い", new string?[]{"い",null,null,"ぃ"}}, {"う", new string?[]{"う","ゔ",null,"ぅ"}}, {"え", new string?[]{"え",null,null,"ぇ"}}, {"お", new string?[]{"お",null,null,"ぉ"}},
            {"や", new string?[]{"や",null,null,"ゃ"}}, {"ゆ", new string?[]{"ゆ",null,null,"ゅ"}}, {"よ", new string?[]{"よ",null,null,"ょ"}}, {"わ", new string?[]{"わ",null,null,"ゎ"}},
            {"か", new string?[]{"か","が",null,null}}, {"き", new string?[]{"き","ぎ",null,null}}, {"く", new string?[]{"く","ぐ",null,null}}, {"け", new string?[]{"け","げ",null,null}}, {"こ", new string?[]{"こ","ご",null,null}},
            {"さ", new string?[]{"さ","ざ",null,null}}, {"し", new string?[]{"し","じ",null,null}}, {"す", new string?[]{"す","ず",null,null}}, {"せ", new string?[]{"せ","ぜ",null,null}}, {"そ", new string?[]{"そ","ぞ",null,null}},
            {"た", new string?[]{"た","だ",null,null}}, {"ち", new string?[]{"ち","ぢ",null,null}}, {"て", new string?[]{"て","で",null,null}}, {"つ", new string?[]{"つ","づ",null,"っ"}}, {"と", new string?[]{"と","ど",null,null}},
            {"は", new string?[]{"は","ば","ぱ",null}}, {"ひ", new string?[]{"ひ","び","ぴ",null}}, {"ふ", new string?[]{"ふ","ぶ","ぷ",null}}, {"へ", new string?[]{"へ","べ","ぺ",null}}, {"ほ", new string?[]{"ほ","ぼ","ぽ",null}},
            {"が", new string?[]{"か","が",null,null}}, {"ぎ", new string?[]{"き","ぎ",null,null}}, {"ぐ", new string?[]{"く","ぐ",null,null}}, {"げ", new string?[]{"け","げ",null,null}}, {"ご", new string?[]{"こ","ご",null,null}},
            {"ざ", new string?[]{"さ","ざ",null,null}}, {"じ", new string?[]{"し","じ",null,null}}, {"ず", new string?[]{"す","ず",null,null}}, {"ぜ", new string?[]{"せ","ぜ",null,null}}, {"ぞ", new string?[]{"そ","ぞ",null,null}},
            {"だ", new string?[]{"た","だ",null,null}}, {"ぢ", new string?[]{"ち","ぢ",null,null}}, {"づ", new string?[]{"つ","づ",null,"っ"}}, {"で", new string?[]{"て","で",null,null}}, {"ど", new string?[]{"と","ど",null,null}},
            {"ば", new string?[]{"は","ば","ぱ",null}}, {"び", new string?[]{"ひ","び","ぴ",null}}, {"ぶ", new string?[]{"ふ","ぶ","ぷ",null}}, {"べ", new string?[]{"へ","べ","ぺ",null}}, {"ぼ", new string?[]{"ほ","ぼ","ぽ",null}},
            {"ぱ", new string?[]{"は","ば","ぱ",null}}, {"ぴ", new string?[]{"ひ","び","ぴ",null}}, {"ぷ", new string?[]{"ふ","ぶ","ぷ",null}}, {"ぺ", new string?[]{"へ","べ","ぺ",null}}, {"ぽ", new string?[]{"ほ","ぼ","ぽ",null}},
            {"ゔ", new string?[]{"う","ゔ",null,"ぅ"}},
            {"ぁ", new string?[]{"あ",null,null,"ぁ"}}, {"ぃ", new string?[]{"い",null,null,"ぃ"}}, {"ぅ", new string?[]{"う","ゔ",null,"ぅ"}}, {"ぇ", new string?[]{"え",null,null,"ぇ"}}, {"ぉ", new string?[]{"お",null,null,"ぉ"}},
            {"ゃ", new string?[]{"や",null,null,"ゃ"}}, {"ゅ", new string?[]{"ゆ",null,null,"ゅ"}}, {"ょ", new string?[]{"よ",null,null,"ょ"}}, {"ゎ", new string?[]{"わ",null,null,"ゎ"}}, 
            {"っ", new string?[]{"つ","づ",null,"っ"}},
        };

        private static readonly Dictionary<string, string?[]> YoonKatakanaChains = new();

        static JapaneseShared()
        {
            foreach (var kv in YoonHiraganaChains)
            {
                if (!HiraToKata.TryGetValue(kv.Key, out string? kataKey)) continue;
                string?[] hiraChain = kv.Value;
                string?[] kataChain = new string?[4];
                for (int i = 0; i < 4; i++)
                    kataChain[i] = hiraChain[i] == null ? null : (HiraToKata.TryGetValue(hiraChain[i]!, out string? k) ? k : hiraChain[i]);
                YoonKatakanaChains[kataKey] = kataChain;
            }
        }

        private static int GetYoonCategory(string ch)
        {
            if (string.IsNullOrEmpty(ch)) return -1;
            if (YoonHiraganaChains.TryGetValue(ch, out string?[]? hChain))
                for (int i = 0; i < 4; i++) if (hChain[i] == ch) return i;
            if (YoonKatakanaChains.TryGetValue(ch, out string?[]? kChain))
                for (int i = 0; i < 4; i++) if (kChain[i] == ch) return i;
            return -1;
        }

        private static string GetNextYoonChar(string ch)
        {
            if (string.IsNullOrEmpty(ch)) return ch;
            string?[]? chain = YoonHiraganaChains.ContainsKey(ch) ? YoonHiraganaChains[ch] : (YoonKatakanaChains.ContainsKey(ch) ? YoonKatakanaChains[ch] : null);
            if (chain == null) return ch;

            int curCat = Array.IndexOf(chain, ch);
            if (curCat < 0) return ch;

            for (int step = 1; step <= 4; step++)
            {
                int nextCat = (curCat + step) % 4;
                if (chain[nextCat] != null) return chain[nextCat]!;
            }
            return ch;
        }

        private static string ConvertYoonToCategory(string ch, int toCat)
        {
            if (string.IsNullOrEmpty(ch)) return ch;
            string?[]? chain = YoonHiraganaChains.ContainsKey(ch) ? YoonHiraganaChains[ch] : (YoonKatakanaChains.ContainsKey(ch) ? YoonKatakanaChains[ch] : null);
            if (chain != null && toCat >= 0 && toCat < 4 && chain[toCat] != null) return chain[toCat]!;
            return ch;
        }

        public static string ApplyHiraganaKatakanaTransformation(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.Length == 1) return HiraToKata.ContainsKey(text) ? HiraToKata[text] : (KataToHira.ContainsKey(text) ? KataToHira[text] : text);

            bool isTargetKata = HiraToKata.ContainsKey(text[0].ToString());
            bool isTargetHira = KataToHira.ContainsKey(text[0].ToString());
            if (!isTargetKata && !isTargetHira) return text;

            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                string cs = c.ToString();
                sb.Append(isTargetKata ? (HiraToKata.ContainsKey(cs) ? HiraToKata[cs] : cs) : (KataToHira.ContainsKey(cs) ? KataToHira[cs] : cs));
            }
            return sb.ToString();
        }

        public static string ApplyYoonTransformation(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.Length == 1)
            {
                if ("んをンヲ".Contains(text)) return text;
                return GetNextYoonChar(text);
            }

            string first = text[0].ToString();
            if ("んをンヲ".Contains(first)) return text;

            int fromCat = GetYoonCategory(first);
            if (fromCat < 0) return text;

            string firstConverted = GetNextYoonChar(first);
            int toCat = GetYoonCategory(firstConverted);
            if (firstConverted == first) return text;

            StringBuilder sb = new StringBuilder(text.Length);
            sb.Append(firstConverted);
            
            for (int i = 1; i < text.Length; i++)
            {
                string c = text[i].ToString();
                if ("んをンヲ".Contains(c)) { sb.Append(c); continue; }

                int cCat = GetYoonCategory(c);
                if (cCat < 0) { sb.Append(c); continue; }
                sb.Append(ConvertYoonToCategory(c, toCat));
            }
            return sb.ToString();
        }
    }
    #endregion

    #region [ 4. 언어 프로세서: 일본어1, 일본어2, 일본어3 (Japanese1, Japanese2, Japanese3) ]
    
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

        public bool ProcessKeyDown(int vkCode, bool isShift, bool capsOn, IntPtr hFore, bool isHangulMode)
        {
            Japanese1Map.SetLayer(1);
            return Japanese1Map.ProcessKeyDownShared(vkCode, isShift, capsOn, hFore, isHangulMode);
        }

        public void OnMouseClick() 
        {
            Japanese1Map.SetLayer(1);
            if (Japanese1Map.IsWaitingVowel) Japanese1Map.Reset();
            Japanese1Map.SetLastOutputChar("");
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

        public bool ProcessKeyDown(int vkCode, bool isShift, bool capsOn, IntPtr hFore, bool isHangulMode)
        {
            Japanese1Map.SetLayer(2);
            return Japanese1Map.ProcessKeyDownShared(vkCode, isShift, capsOn, hFore, isHangulMode);
        }

        public void OnMouseClick() 
        {
            Japanese1Map.SetLayer(2);
            if (Japanese1Map.IsWaitingVowel) Japanese1Map.Reset();
            Japanese1Map.SetLastOutputChar("");
        }    
    }

    internal static class Japanese1Map
    {
        private const int VK_Q = 0x51, VK_W = 0x57, VK_E = 0x45, VK_R = 0x52, VK_A = 0x41, VK_S = 0x53, VK_D = 0x44, VK_F = 0x46;
        private const int VK_Z = 0x5A, VK_X = 0x58, VK_C = 0x43, VK_V = 0x56, VK_H = 0x48, VK_J = 0x4A, VK_K = 0x4B, VK_L = 0x4C;
        private const int VK_Y = 0x59, VK_T = 0x54, VK_U = 0x55, VK_I = 0x49, VK_O = 0x4F, VK_G = 0x47, VK_N = 0x4E, VK_M = 0x4D, VK_B = 0x42, VK_P = 0x50;

        private static readonly HashSet<int> _consonantKeys = new() { VK_Q, VK_W, VK_E, VK_R, VK_A, VK_S, VK_D, VK_F, VK_Z, VK_X, VK_C, VK_V };
        private static readonly HashSet<int> _vowelKeys = new() { VK_H, VK_J, VK_K, VK_L, VK_M };

        private static readonly Dictionary<(int Con, int Vow), (string Hira, string Kata)> _combineMap = new()
        {
            { (VK_Q, VK_H), ("ば","バ") }, { (VK_Q, VK_J), ("び","ビ") }, { (VK_Q, VK_K), ("ぶ","ブ") }, { (VK_Q, VK_M), ("べ","ベ") }, { (VK_Q, VK_L), ("ぼ","ボ") },
            { (VK_W, VK_H), ("ざ","ザ") }, { (VK_W, VK_J), ("じ","ジ") }, { (VK_W, VK_K), ("ず","ズ") }, { (VK_W, VK_M), ("ぜ","ゼ") }, { (VK_W, VK_L), ("ぞ","ゾ") },
            { (VK_E, VK_H), ("が","ガ") }, { (VK_E, VK_J), ("ぎ","ギ") }, { (VK_E, VK_K), ("ぐ","グ") }, { (VK_E, VK_M), ("げ","ゲ") }, { (VK_E, VK_L), ("ご","ゴ") },
            { (VK_R, VK_H), ("だ","ダ") }, { (VK_R, VK_J), ("ぢ","ヂ") }, { (VK_R, VK_K), ("づ","ヅ") }, { (VK_R, VK_M), ("で","デ") }, { (VK_R, VK_L), ("ど","ド") },
            { (VK_A, VK_H), ("は","ハ") }, { (VK_A, VK_J), ("ひ","ヒ") }, { (VK_A, VK_K), ("ふ","フ") }, { (VK_A, VK_M), ("へ","ヘ") }, { (VK_A, VK_L), ("ほ","ホ") },
            { (VK_S, VK_H), ("さ","サ") }, { (VK_S, VK_J), ("し","シ") }, { (VK_S, VK_K), ("す","ス") }, { (VK_S, VK_M), ("せ","セ") }, { (VK_S, VK_L), ("そ","ソ") },
            { (VK_D, VK_H), ("か","カ") }, { (VK_D, VK_J), ("き","キ") }, { (VK_D, VK_K), ("く","ク") }, { (VK_D, VK_M), ("け","ケ") }, { (VK_D, VK_L), ("こ","コ") },
            { (VK_F, VK_H), ("た","タ") }, { (VK_F, VK_J), ("ち","チ") }, { (VK_F, VK_K), ("つ","ツ") }, { (VK_F, VK_M), ("て","テ") }, { (VK_F, VK_L), ("と","ト") },
            { (VK_Z, VK_H), ("ぱ","パ") }, { (VK_Z, VK_J), ("ぴ","ピ") }, { (VK_Z, VK_K), ("ぷ","プ") }, { (VK_Z, VK_M), ("ぺ","ペ") }, { (VK_Z, VK_L), ("ぽ","ポ") },
            { (VK_X, VK_H), ("ま","マ") }, { (VK_X, VK_J), ("み","ミ") }, { (VK_X, VK_K), ("む","ム") }, { (VK_X, VK_M), ("め","メ") }, { (VK_X, VK_L), ("も","モ") },
            { (VK_C, VK_H), ("ら","ラ") }, { (VK_C, VK_J), ("り","リ") }, { (VK_C, VK_K), ("る","ル") }, { (VK_C, VK_M), ("れ","レ") }, { (VK_C, VK_L), ("ろ","ロ") },
            { (VK_V, VK_H), ("な","ナ") }, { (VK_V, VK_J), ("に","ニ") }, { (VK_V, VK_K), ("ぬ","ヌ") }, { (VK_V, VK_M), ("ね","ネ") }, { (VK_V, VK_L), ("の","ノ") },
        };

        private static readonly Dictionary<int, (string Hira, string Kata)> _soloMap = new()
        {
            { VK_T, ("っ","ッ") }, { VK_G, ("ん","ン") },
            { VK_Y, ("わ","ワ") }, { VK_U, ("を","ヲ") }, { VK_I, ("よ","ヨ") }, { VK_O, ("ゆ","ユ") }, { VK_P, ("や","ヤ") },
            { VK_H, ("あ","ア") }, { VK_J, ("い","イ") }, { VK_K, ("う","ウ") }, { VK_M, ("え","エ") }, { VK_L, ("お","オ") }
        };

        private static readonly Dictionary<int, (string Hira, string Kata)> _previewMapL1 = new()
        {
            { VK_Q, ("ば","バ") }, { VK_W, ("ざ","ザ") }, { VK_E, ("が","ガ") }, { VK_R, ("だ","ダ") }, 
            { VK_A, ("は","ハ") }, { VK_S, ("さ","サ") }, { VK_D, ("か","カ") }, { VK_F, ("た","タ") }, 
            { VK_Z, ("ぱ","パ") }, { VK_X, ("ま","マ") }, { VK_C, ("ら","ラ") }, { VK_V, ("な","ナ") },
        };

        private static readonly Dictionary<int, (string Hira, string Kata)> _previewMapL2 = new()
        {
            { VK_Q, ("ば","バ") }, { VK_W, ("じ","ジ") }, { VK_E, ("が","ガ") }, { VK_R, ("で","デ") },
            { VK_A, ("は","ハ") }, { VK_S, ("し","シ") }, { VK_D, ("か","カ") }, { VK_F, ("て","テ") }, 
            { VK_Z, ("ぱ","パ") }, { VK_X, ("も","モ") }, { VK_C, ("る","ル") }, { VK_V, ("の","ノ") },
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
            for (int i = 0; i < _ynToggleCount; i++) if (JapaneseShared.TransformMap.TryGetValue(preview, out string? toggled)) preview = toggled;
            _pendingChar = preview; 
                
            MainForm.Instance?.ShowOverlay(_pendingChar, 0);
        }
    
        public static void TogglePendingYn()
        {
            if (!_waitingVowel) return; _ynToggleCount++;
            if (JapaneseShared.TransformMap.TryGetValue(_pendingChar, out string? toggled)) _pendingChar = toggled;
            
            MainForm.Instance?.ShowOverlay(_pendingChar, 0);
        }
    
        public static void HandleHiraganaKatakanaTransformation()
        {
            TextSelectionUtils.TransformAndReplaceText(
                _lastOutputChar,
                JapaneseShared.ApplyHiraganaKatakanaTransformation,
                SetLastOutputChar,
                () => {
                    _isKatakana = !_isKatakana; 
                    _lastOutputChar = ""; 
                    MainForm.Instance?.ShowOverlay(_isKatakana ? "Katakana" : "Hiragana");
                }
            );
        }
    
        public static void HandleYoonTransformation()
        {
            TextSelectionUtils.TransformAndReplaceText(
                _lastOutputChar,
                JapaneseShared.ApplyYoonTransformation,
                SetLastOutputChar
            );
        }

        public static bool ProcessKeyDownShared(int vkCode, bool isShift, bool capsOn, IntPtr hFore, bool isHangulMode)
        {
            isShift = KeyboardLayoutAnalyzer.CheckCopilotShift(isShift);
            bool isVowelKey = vkCode is VirtualKeyCodes.vk_H or VirtualKeyCodes.vk_J or VirtualKeyCodes.vk_K or VirtualKeyCodes.vk_L or VirtualKeyCodes.vk_M;
            if (Japanese1Map.IsWaitingVowel && !isVowelKey)
            {
                if (vkCode == VirtualKeyCodes.vk_B) { Japanese1Map.TogglePendingHiraKata(); return true; }
                if (vkCode == VirtualKeyCodes.vk_N) { Japanese1Map.TogglePendingYn(); return true; }

                string pending = Japanese1Map.PendingChar;
                Japanese1Map.Reset();
                
                if (vkCode == VirtualKeyCodes.Escape || vkCode == VirtualKeyCodes.Backspace) return true;

                if (pending.Length > 0)
                {
                    GlobalInputHook.IsSending = true; NativeMethods.SendUnicodeString(pending); GlobalInputHook.IsSending = false; 
                }

                if (capsOn && isHangulMode)
                {
                    string? result = Japanese1Map.ProcessKey(vkCode, isShift);
                    if (result != null)
                    {
                        if (result.Length > 0) { 
                            GlobalInputHook.IsSending = true; NativeMethods.SendUnicodeString(result); GlobalInputHook.IsSending = false; 
                        }                        
                        return true;
                    }
                }
                return false;
            }

            if (vkCode is >= 0x21 and <= 0x28) { if (!isShift) Japanese1Map.SetLastOutputChar(""); return false; }
            if (vkCode == VirtualKeyCodes.vk_B && capsOn && isHangulMode) { Japanese1Map.HandleHiraganaKatakanaTransformation(); return true; }
            if (vkCode == VirtualKeyCodes.vk_N && capsOn && isHangulMode) { Japanese1Map.HandleYoonTransformation(); return true; }
            if (!capsOn || !isHangulMode) return false;
            if (TextSelectionUtils.IsConverting) return true;

            string? keyResult = Japanese1Map.ProcessKey(vkCode, isShift);
            if (keyResult == null) { Japanese1Map.SetLastOutputChar(""); return false; }

            if (keyResult.Length > 0)
            {
                GlobalInputHook.IsSending = true; 
                NativeMethods.SendUnicodeString(keyResult); 
                GlobalInputHook.IsSending = false; 
            }
            return true;
        }
    
        public static string? ProcessKey(int vkCode, bool isShift)
        {
            bool useKatakana = isShift ^ _isKatakana;

            switch (vkCode)
            {
                case VirtualKeyCodes.OemYen: { string ch = useKatakana ? "|" : "¥"; MainForm.Instance?.ShowOverlay(ch); _lastOutputChar = ch; return ch; }
                case VirtualKeyCodes.OemColon: { string ch = useKatakana ? ":" : "・"; MainForm.Instance?.ShowOverlay(ch); _lastOutputChar = ch; return ch; }
                case VirtualKeyCodes.OemComma: { string ch = useKatakana ? "、" : ","; MainForm.Instance?.ShowOverlay(ch); _lastOutputChar = ch; return ch; }
                case VirtualKeyCodes.OemPeriod: { string ch = useKatakana ? "。" : "."; MainForm.Instance?.ShowOverlay(ch); _lastOutputChar = ch; return ch; }
                case VirtualKeyCodes.OemSlash: { string ch = useKatakana ? "ー" : "/"; MainForm.Instance?.ShowOverlay(ch); _lastOutputChar = ch; return ch; }
            }

            if (KeyboardLayoutAnalyzer.IsSymbolOrNumber(vkCode))
            {
                string? ch = KeyboardLayoutAnalyzer.GetChar(vkCode, useKatakana);
                if (!string.IsNullOrEmpty(ch))
                {
                    MainForm.Instance?.ShowOverlay(ch);
                    _lastOutputChar = ch;
                    return ch;
                }
            }

            if (vkCode == VK_B || vkCode == VK_N) return null;

            if (_waitingVowel)
            {
                if (_vowelKeys.Contains(vkCode))
                {
                    var key = (_pendingConsonant, vkCode);
                    if (_combineMap.TryGetValue(key, out var combined))
                    {
                        string result = _isKatakana ? combined.Kata : combined.Hira;
                        for (int i = 0; i < _ynToggleCount; i++) if (JapaneseShared.TransformMap.TryGetValue(result, out string? toggled)) result = toggled;

                        string currentPending = _pendingChar;
                        string previewVow = vkCode switch { VK_H => _isKatakana ? "ア" : "あ", VK_J => _isKatakana ? "イ" : "い", VK_K => _isKatakana ? "ウ" : "う", VK_M => _isKatakana ? "エ" : "え", VK_L => _isKatakana ? "オ" : "お", _ => "?" };
                        
                        MainForm.Instance?.ShowOverlay($"{currentPending}+{previewVow}={result}");

                        _waitingVowel = false; _pendingConsonant = 0; _pendingChar = ""; _ynToggleCount = 0; _lastOutputChar = result; return result;
                    }
                }
            }
    
            if (_consonantKeys.Contains(vkCode))
            {
                _waitingVowel = true; _pendingConsonant = vkCode; _isKatakana = useKatakana; _ynToggleCount = 0; _pendingChar = GetPreview(vkCode);
                
                MainForm.Instance?.ShowOverlay(_pendingChar, 0);
                return "";
            }
    
            if (_soloMap.TryGetValue(vkCode, out var solo))
            {
                string ch = useKatakana ? solo.Kata : solo.Hira;
                MainForm.Instance?.ShowOverlay(ch); 
                _lastOutputChar = ch; return ch;
            }
    
            _lastOutputChar = ""; return null;
        }
    
        private static string GetPreview(int vkCode)
        {
            var map = CurrentLayer == 1 ? _previewMapL1 : _previewMapL2;
            if (map.TryGetValue(vkCode, out var p)) return _isKatakana ? p.Kata : p.Hira;
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

        public static void HandleHiraganaKatakanaTransformation()
        {
            TextSelectionUtils.TransformAndReplaceText(
                _lastOutputChar,
                JapaneseShared.ApplyHiraganaKatakanaTransformation,
                SetLastOutputChar,
                () => {
                    _isKatakana = !_isKatakana; 
                    _lastOutputChar = ""; 
                    MainForm.Instance?.ShowOverlay(_isKatakana ? "Katakana" : "Hiragana");
                }
            );
        }

        public static void HandleYoonTransformation()
        {
            TextSelectionUtils.TransformAndReplaceText(
                _lastOutputChar,
                JapaneseShared.ApplyYoonTransformation,
                SetLastOutputChar
            );
        }

        public static string? ProcessKey(int vkCode, bool isShift)
        {
            bool useKatakana = isShift ^ _isKatakana;

            switch (vkCode)
            {
                case VirtualKeyCodes.OemYen: { string ch_jpy = useKatakana ? "|" : "¥"; MainForm.Instance?.ShowOverlay(ch_jpy); _lastOutputChar = ch_jpy; return ch_jpy; }
                case VirtualKeyCodes.OemColon: { string ch_jpy = useKatakana ? ":" : "・"; MainForm.Instance?.ShowOverlay(ch_jpy); _lastOutputChar = ch_jpy; return ch_jpy; }
                case VirtualKeyCodes.OemComma: { string ch_jpy = useKatakana ? "、" : ","; MainForm.Instance?.ShowOverlay(ch_jpy); _lastOutputChar = ch_jpy; return ch_jpy; }
                case VirtualKeyCodes.OemPeriod: { string ch_jpy = useKatakana ? "。" : "."; MainForm.Instance?.ShowOverlay(ch_jpy); _lastOutputChar = ch_jpy; return ch_jpy; }
                case VirtualKeyCodes.OemSlash: { string ch_jpy = useKatakana ? "ー" : "/"; MainForm.Instance?.ShowOverlay(ch_jpy); _lastOutputChar = ch_jpy; return ch_jpy; }
            }

            if (KeyboardLayoutAnalyzer.IsSymbolOrNumber(vkCode))
            {
                string? ch_jpy = KeyboardLayoutAnalyzer.GetChar(vkCode, useKatakana);
                if (!string.IsNullOrEmpty(ch_jpy))
                {
                    MainForm.Instance?.ShowOverlay(ch_jpy);
                    _lastOutputChar = ch_jpy;
                    return ch_jpy;
                }
            }

            string? ch = null;

            if (CurrentLayer == 1)
            {
                ch = vkCode switch
                {
                    0x51 => useKatakana ? "レ" : "れ", 0x57 => useKatakana ? "ロ" : "ろ", 0x45 => useKatakana ? "ル" : "る", 0x52 => useKatakana ? "リ" : "り", 0x54 => useKatakana ? "ラ" : "ら", 
                    0x59 => useKatakana ? "ハ" : "は", 0x55 => useKatakana ? "ヒ" : "ひ", 0x49 => useKatakana ? "フ" : "ふ", 0x4F => useKatakana ? "ホ" : "ほ", 0x50 => useKatakana ? "ヘ" : "へ", 
                    0x41 => useKatakana ? "ネ" : "ね", 0x53 => useKatakana ? "ノ" : "の", 0x44 => useKatakana ? "ヌ" : "ぬ", 0x46 => useKatakana ? "ニ" : "に", 0x47 => useKatakana ? "ナ" : "な",
                    0x48 => useKatakana ? "ア" : "あ", 0x4A => useKatakana ? "イ" : "い", 0x4B => useKatakana ? "ウ" : "う", 0x4C => useKatakana ? "オ" : "お", 
                    0x5A => useKatakana ? "メ" : "め", 0x58 => useKatakana ? "モ" : "も", 0x43 => useKatakana ? "ム" : "む", 0x56 => useKatakana ? "ミ" : "み", 0x42 => useKatakana ? "マ" : "ま",
                    0x4E => useKatakana ? "ン" : "ん", 0x4D => useKatakana ? "エ" : "え", 
                    _ => null
                };
            }
            else if (CurrentLayer == 2)
            {
                ch = vkCode switch
                {
                    0x51 => useKatakana ? "ケ" : "け", 0x57 => useKatakana ? "コ" : "こ", 0x45 => useKatakana ? "ク" : "く", 0x52 => useKatakana ? "キ" : "き", 0x54 => useKatakana ? "カ" : "か",
                    0x59 => useKatakana ? "パ" : "ぱ", 0x55 => useKatakana ? "ピ" : "ぴ", 0x49 => useKatakana ? "プ" : "ぷ", 0x4F => useKatakana ? "ポ" : "ぽ", 0x50 => useKatakana ? "ペ" : "ぺ", 
                    0x41 => useKatakana ? "テ" : "て", 0x53 => useKatakana ? "ト" : "と", 0x44 => useKatakana ? "ツ" : "つ", 0x46 => useKatakana ? "チ" : "ち", 0x47 => useKatakana ? "タ" : "た",
                    0x48 => useKatakana ? "ッ" : "っ", 0x4A => useKatakana ? "ヨ" : "よ", 0x4B => useKatakana ? "ユ" : "ゆ", 0x4C => useKatakana ? "ヤ" : "や", 
                    0x5A => useKatakana ? "セ" : "せ", 0x58 => useKatakana ? "ソ" : "そ", 0x43 => useKatakana ? "ス" : "す", 0x56 => useKatakana ? "シ" : "し", 0x42 => useKatakana ? "サ" : "さ",
                    0x4E => useKatakana ? "ヲ" : "を", 0x4D => useKatakana ? "ワ" : "わ", 
                    _ => null
                };
            }
            else if (CurrentLayer == 3)
            {
                ch = vkCode switch
                {
                    0x51 => useKatakana ? "ゲ" : "げ", 0x57 => useKatakana ? "ゴ" : "ご", 0x45 => useKatakana ? "グ" : "ぐ", 0x52 => useKatakana ? "ギ" : "ぎ", 0x54 => useKatakana ? "ガ" : "が", 
                    0x59 => useKatakana ? "バ" : "ば", 0x55 => useKatakana ? "ビ" : "び", 0x49 => useKatakana ? "ブ" : "ぶ", 0x4F => useKatakana ? "ボ" : "ぼ", 0x50 => useKatakana ? "ベ" : "べ", 
                    0x41 => useKatakana ? "デ" : "で", 0x53 => useKatakana ? "ド" : "ど", 0x44 => useKatakana ? "ヅ" : "づ", 0x46 => useKatakana ? "ヂ" : "ぢ", 0x47 => useKatakana ? "ダ" : "だ",
                    0x48 => useKatakana ? "ヴ" : "ゔ", 0x4A => useKatakana ? "ョ" : "ょ", 0x4B => useKatakana ? "ュ" : "ゅ", 0x4C => useKatakana ? "ャ" : "ゃ", 
                    0x5A => useKatakana ? "ゼ" : "ぜ", 0x58 => useKatakana ? "ゾ" : "ぞ", 0x43 => useKatakana ? "ズ" : "ず", 0x56 => useKatakana ? "ジ" : "じ", 0x42 => useKatakana ? "ザ" : "ざ", 
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
            return false;
        }

        public bool ProcessKeyDown(int vkCode, bool isShift, bool capsOn, IntPtr hFore, bool isHangulMode)
        {
            isShift = KeyboardLayoutAnalyzer.CheckCopilotShift(isShift);

            if (vkCode is >= 0x21 and <= 0x28) { if (!isShift) Japanese3Map.SetLastOutputChar(""); return false; }

            if (Japanese3Map.CurrentLayer == 3)
            {
                if (vkCode == VirtualKeyCodes.vk_N) { if (!capsOn || !isHangulMode) return false; Japanese3Map.HandleHiraganaKatakanaTransformation(); return true; }
                if (vkCode == VirtualKeyCodes.vk_M) { if (!capsOn || !isHangulMode) return false; Japanese3Map.HandleYoonTransformation(); return true; }
            }

            if (!capsOn || !isHangulMode) return false;
            if (TextSelectionUtils.IsConverting) return true;

            string? keyResult = Japanese3Map.ProcessKey(vkCode, isShift);
            if (keyResult == null) { Japanese3Map.SetLastOutputChar(""); return false; }
            
            if (keyResult.Length > 0)
            {
                GlobalInputHook.IsSending = true; 
                NativeMethods.SendUnicodeString(keyResult); 
                GlobalInputHook.IsSending = false; 
            }
            return true;
        }

        public void OnMouseClick() => Japanese3Map.SetLastOutputChar("");
    }
    #endregion
}