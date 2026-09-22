// ImeNativeCore.cs
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Forms;
using System.Windows.Automation;
using System.Linq;

namespace IMEJapanese
{
    // =======================================================================================
    // 5. 감지 및 입력 훅 모듈 (ImeState)
    // =======================================================================================
    internal static class ImeState
    {
        public enum State
        {
            EnglishLower, EnglishUpper, Hangul, JapaneseIME, JapaneseHangul1, JapaneseHangul2, JapaneseHangul3
        }

        private const int MaxCacheSize = 100;
        // [수정 #4] 키보드 훅(비UI 스레드)과 UI 스레드가 동시 접근하므로 ConcurrentDictionary 사용
        private static readonly ConcurrentDictionary<IntPtr, bool> _hangulStateCache = new ConcurrentDictionary<IntPtr, bool>();
        private static IntPtr _lastCheckedHwnd = IntPtr.Zero;
        private static DateTime _lastCheckedTime = DateTime.MinValue;

        public static bool IsHangul(State state) =>
            state == State.Hangul || state == State.JapaneseHangul1 || state == State.JapaneseHangul2 || state == State.JapaneseHangul3;

        public static State Detect(IntPtr foregroundHwnd,
            bool enableJapanese1 = false, bool enableJapanese2 = false, bool enableJapanese3 = false, bool? knownHangulState = null)
        {
            bool capsOn = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 0x0001) != 0;
            if (foregroundHwnd == IntPtr.Zero) return capsOn ? State.EnglishUpper : State.EnglishLower;

            uint threadId = NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _);
            long hklValue = NativeMethods.GetKeyboardLayout(threadId).ToInt64();
            ushort langId = (ushort)(hklValue & 0xFFFF);

            if (langId == 0x0411) return State.JapaneseIME;

            if (langId == 0x0412) // 한국어 레이아웃
            {
                bool isHangul = knownHangulState ?? CheckHangulPublic(foregroundHwnd);
                if (isHangul)
                {
                    if (capsOn)
                    {
                        if (enableJapanese1) return State.JapaneseHangul1;
                        if (enableJapanese2) return State.JapaneseHangul2;
                        if (enableJapanese3) return State.JapaneseHangul3;
                    }
                    return State.Hangul;
                }
                return capsOn ? State.EnglishUpper : State.EnglishLower;
            }

            return capsOn ? State.EnglishUpper : State.EnglishLower;
        }

        private static IntPtr GetTargetImeWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return IntPtr.Zero;
            uint threadId = NativeMethods.GetWindowThreadProcessId(hWnd, out _);
            IntPtr focusWnd = hWnd;

            NativeMethods.GUITHREADINFO gti = new() { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            if (NativeMethods.GetGUIThreadInfo(threadId, ref gti))
            {
                if (gti.hwndFocus != IntPtr.Zero) focusWnd = gti.hwndFocus;
                else if (gti.hwndActive != IntPtr.Zero) focusWnd = gti.hwndActive;
            }

            IntPtr hIme = NativeMethods.ImmGetDefaultIMEWnd(focusWnd);
            return hIme != IntPtr.Zero ? hIme : NativeMethods.ImmGetDefaultIMEWnd(hWnd);
        }

        public static bool CheckHangulPublic(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            if (hWnd == _lastCheckedHwnd && (DateTime.Now - _lastCheckedTime).TotalMilliseconds < 50)
            {
                return _hangulStateCache.TryGetValue(hWnd, out bool recentState) && recentState;
            }

            if (_hangulStateCache.Count > MaxCacheSize)
            {
                // ConcurrentDictionary.Clear()은 스레드 안전함
                _hangulStateCache.Clear();
            }

            bool isHangul = false;
            bool success = false;

            IntPtr hImeWnd = GetTargetImeWindow(hWnd);
            if (hImeWnd != IntPtr.Zero)
            {
                IntPtr res = NativeMethods.SendMessageTimeout(hImeWnd, NativeMethods.WM_IME_CONTROL, (IntPtr)NativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 30, out IntPtr result);
                if (res != IntPtr.Zero)
                {
                    isHangul = ((uint)result.ToInt64() & NativeMethods.IME_CMODE_NATIVE) != 0;
                    success = true;
                }
            }

            if (!success)
            {
                IntPtr hIMC = NativeMethods.ImmGetContext(hWnd);
                if (hIMC != IntPtr.Zero)
                {
                    if (NativeMethods.ImmGetConversionStatus(hIMC, out uint conv, out _))
                    {
                        isHangul = (conv & NativeMethods.IME_CMODE_NATIVE) != 0;
                        success = true;
                    }
                    NativeMethods.ImmReleaseContext(hWnd, hIMC);
                }
            }

            if (success)
            {
                _hangulStateCache[hWnd] = isHangul;
                _lastCheckedHwnd = hWnd;
                _lastCheckedTime = DateTime.Now;
                return isHangul;
            }

            return _hangulStateCache.TryGetValue(hWnd, out bool cachedState) && cachedState;
        }

        public static void SetHangulState(IntPtr hWnd, bool setHangul)
        {
            IntPtr hImeWnd = GetTargetImeWindow(hWnd);
            if (hImeWnd != IntPtr.Zero)
            {
                NativeMethods.SendMessageTimeout(hImeWnd, NativeMethods.WM_IME_CONTROL, (IntPtr)NativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 20, out IntPtr result);
                uint mode = (uint)result.ToInt64();
                bool isHangul = (mode & NativeMethods.IME_CMODE_NATIVE) != 0;

                if (isHangul != setHangul)
                {
                    if (setHangul) mode |= NativeMethods.IME_CMODE_NATIVE;
                    else mode &= ~NativeMethods.IME_CMODE_NATIVE;
                    NativeMethods.SendMessageTimeout(hImeWnd, NativeMethods.WM_IME_CONTROL, (IntPtr)NativeMethods.IMC_SETCONVERSIONMODE, (IntPtr)mode, NativeMethods.SMTO_ABORTIFHUNG, 20, out _);

                    _hangulStateCache[hWnd] = setHangul;
                    _lastCheckedHwnd = hWnd;
                    _lastCheckedTime = DateTime.Now;
                }
            }
        }
    }

    // =======================================================================================
    // 전역 시스템 훅 모듈 통합 (GlobalInputHook)
    // =======================================================================================
    internal static class GlobalInputHook
    {
        // Virtual Key Constants
        private const int VK_SPACE = 0x20;
        private const int VK_BACK = 0x08;
        private const int VK_RETURN = 0x0D;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_TAB = 0x09;
        private const int VK_HANJA = 0x19;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_LSHIFT = 0x10;
        private const int VK_LCONTROL = 0x11;
        private const int VK_LMENU = 0x12; // Alt
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        internal readonly struct HookContextSnapshot
        {
            public readonly IntPtr ContextHwnd;
            public readonly ushort ContextLangId;
            public readonly bool IsHangulMode;
            public readonly IKeyProcessor? ActiveProcessor;
            public readonly bool IsJapanese1ModeActive;
            public readonly bool IsJapanese2ModeActive;
            public readonly bool IsJapanese3ModeActive;

            public HookContextSnapshot(
                IntPtr contextHwnd,
                ushort contextLangId,
                bool isHangulMode,
                IKeyProcessor? activeProcessor,
                bool isJapanese1ModeActive,
                bool isJapanese2ModeActive,
                bool isJapanese3ModeActive)
            {
                ContextHwnd = contextHwnd;
                ContextLangId = contextLangId;
                IsHangulMode = isHangulMode;
                ActiveProcessor = activeProcessor;
                IsJapanese1ModeActive = isJapanese1ModeActive;
                IsJapanese2ModeActive = isJapanese2ModeActive;
                IsJapanese3ModeActive = isJapanese3ModeActive;
            }
        }

        public static bool IsEnabled { get; set; } = true;

        private static HookContextSnapshot _contextSnapshot = new(
            IntPtr.Zero, 0, false, null, false, false, false);

        public static bool IsJapanese1ModeActive => _contextSnapshot.IsJapanese1ModeActive;
        public static bool IsJapanese2ModeActive => _contextSnapshot.IsJapanese2ModeActive;
        public static bool IsJapanese3ModeActive => _contextSnapshot.IsJapanese3ModeActive;
        public static IKeyProcessor? ActiveProcessor => _contextSnapshot.ActiveProcessor;
        public static IntPtr ContextHwnd => _contextSnapshot.ContextHwnd;
        public static ushort ContextLangId => _contextSnapshot.ContextLangId;
        public static bool CachedIsHangulMode => _contextSnapshot.IsHangulMode;

        public static volatile bool IsSending = false;
        public static volatile bool IsReplacingSelection = false;
        private static IntPtr _kbdHookId = IntPtr.Zero;
        private static IntPtr _mouseHookId = IntPtr.Zero;
        private static IntPtr _lastResolvedContextHwnd = IntPtr.Zero;

        public static unsafe void Install()
        {
            if (_kbdHookId != IntPtr.Zero && _mouseHookId != IntPtr.Zero) return;
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var module = process.MainModule ?? throw new InvalidOperationException("MainModule을 가져올 수 없습니다.");
            IntPtr hMod = NativeMethods.GetModuleHandle(module.ModuleName);

            if (_kbdHookId == IntPtr.Zero)
            {
                delegate* unmanaged[Stdcall]<int, IntPtr, IntPtr, IntPtr> kbdCb = &KbdHookCallback;
                _kbdHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, kbdCb, hMod, 0);
            }
            if (_mouseHookId == IntPtr.Zero)
            {
                delegate* unmanaged[Stdcall]<int, IntPtr, IntPtr, IntPtr> mouseCb = &MouseHookCallback;
                _mouseHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, mouseCb, hMod, 0);
            }
        }

        public static void Uninstall()
        {
            if (_kbdHookId != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_kbdHookId); _kbdHookId = IntPtr.Zero; }
            if (_mouseHookId != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_mouseHookId); _mouseHookId = IntPtr.Zero; }
        }

        public static void UpdateContext(HookContextSnapshot snapshot)
        {
            _contextSnapshot = snapshot;
        }

        private static readonly System.Text.StringBuilder _compositionBuffer = new System.Text.StringBuilder();
        private static readonly object _compositionLock = new object();

        public static void AppendComposition(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (_compositionLock)
            {
                IsReplacingSelection = false;
                _compositionBuffer.Append(text);
                if (AppConfig.LogLevel >= 2) Debug.WriteLine($"[CompositionBuffer] Appended '{text}' -> current: '{_compositionBuffer}'");
            }
        }

        public static void RemoveLastCompositionChar()
        {
            lock (_compositionLock)
            {
                if (_compositionBuffer.Length > 0)
                {
                    if (_compositionBuffer.Length >= 2 && char.IsSurrogatePair(_compositionBuffer[_compositionBuffer.Length - 2], _compositionBuffer[_compositionBuffer.Length - 1]))
                    {
                        _compositionBuffer.Remove(_compositionBuffer.Length - 2, 2);
                    }
                    else
                    {
                        _compositionBuffer.Remove(_compositionBuffer.Length - 1, 1);
                    }
                    if (AppConfig.LogLevel >= 2) Debug.WriteLine($"[CompositionBuffer] Removed last char -> current: '{_compositionBuffer}'");
                }
            }
        }

        public static void ClearCompositionBuffer()
        {
            lock (_compositionLock)
            {
                IsReplacingSelection = false;
                if (_compositionBuffer.Length > 0)
                {
                    _compositionBuffer.Clear();
                    if (AppConfig.LogLevel >= 2) Debug.WriteLine("[CompositionBuffer] Cleared");
                }
            }
        }

        public static string GetCompositionText()
        {
            lock (_compositionLock)
            {
                return _compositionBuffer.ToString();
            }
        }

        public static void CommitKanjiConversion(string originalText, string selectedText, bool isReplacingSelection = false)
        {
            if (string.IsNullOrEmpty(selectedText))
            {
                IsReplacingSelection = false;
                return;
            }

            int backCount = 0;
            if (isReplacingSelection || IsReplacingSelection)
            {
                backCount = 0;
            }
            else
            {
                string textToMeasure = string.IsNullOrEmpty(originalText) ? GetCompositionText() : originalText;
                backCount = new System.Globalization.StringInfo(textToMeasure).LengthInTextElements;
            }

            IsReplacingSelection = false;

            if (AppConfig.LogLevel >= 2) Debug.WriteLine($"CommitKanjiConversion: backCount={backCount}, selectedText='{selectedText}'");
            SendReplacement(backCount, selectedText);
            ClearCompositionBuffer();

            IntPtr hFore = ResolveContextHwnd();
            if (hFore != IntPtr.Zero)
            {
                bool isHangul = ImeState.CheckHangulPublic(hFore);
                if (!isHangul)
                {
                    ImeState.SetHangulState(hFore, true);
                }
            }
        }

        public static void SendReplacement(int backCount, string text)
        {
            IsSending = true;

            if (AppConfig.EnableCopilotMap)
            {
                Thread.Sleep(50);
                bool isShift = (NativeMethods.GetKeyState(VK_LSHIFT) & 0x8000) != 0;
                bool isLWin = (NativeMethods.GetKeyState(VK_LWIN) & 0x8000) != 0;
                bool isRWin = (NativeMethods.GetKeyState(VK_RWIN) & 0x8000) != 0;

                if (isShift) NativeMethods.keybd_event(VK_LSHIFT, 0, 0x0002, UIntPtr.Zero);
                if (isLWin) NativeMethods.keybd_event(VK_LWIN, 0, 0x0002, UIntPtr.Zero);
                if (isRWin) NativeMethods.keybd_event(VK_RWIN, 0, 0x0002, UIntPtr.Zero);
            }

            for (int i = 0; i < backCount; i++) NativeMethods.SendBackspace();

            // [수정 #1] Backspace 연속 전송 후 대상 앱이 처리할 수 있도록 짧은 딜레이 삽입
            // 딜레이 없이 바로 유니코드 문자를 전송하면 느린 앱(구형 Office 등)에서 글자가 겹치는 현상 발생
            if (backCount > 0) Thread.Sleep(15);

            if (!string.IsNullOrEmpty(text)) NativeMethods.SendUnicodeString(text);
            IsSending = false;
        }

        private static void SendSpaceKey()
        {
            IsSending = true;
            NativeMethods.INPUT[] inputs = new NativeMethods.INPUT[2];
            inputs[0].type = NativeMethods.INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = VK_SPACE; 
            inputs[1].type = NativeMethods.INPUT_KEYBOARD;
            inputs[1].U.ki.wVk = VK_SPACE;
            inputs[1].U.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;
            NativeMethods.SendInput(2, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
            IsSending = false;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && (wParam.ToInt32() == NativeMethods.WM_LBUTTONDOWN || wParam.ToInt32() == NativeMethods.WM_RBUTTONDOWN))
                {
                    if (KanjiCandidateOverlay.IsActive)
                    {
                        int mouseX = Marshal.ReadInt32(lParam, 0);
                        int mouseY = Marshal.ReadInt32(lParam, 4);
                        var clickPoint = new System.Drawing.Point(mouseX, mouseY);
                        KanjiCandidateOverlay.HandleMouseClickFromHook(clickPoint);
                    }
                    else
                    {
                        ActiveProcessor?.OnMouseClick();
                        ClearCompositionBuffer();
                    }
                }
            }
            catch { }
            return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private static bool IsInterestedKeyboardMessage(int msg) =>
            msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN ||
            msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;

        private static bool IsHanjaOrRightCtrl(int vkCode) => vkCode == VK_HANJA || vkCode == VK_RCONTROL;

        private static bool HasBlockedModifierChord(bool allowCtrlForCurrentKey)
        {
            bool isCtrl = (NativeMethods.GetKeyState(VK_LCONTROL) & 0x8000) != 0;
            if (isCtrl && !allowCtrlForCurrentKey) return true;
            if ((NativeMethods.GetKeyState(VK_LMENU) & 0x8000) != 0) return true;

            bool isWin = (NativeMethods.GetKeyState(VK_LWIN) & 0x8000) != 0 || (NativeMethods.GetKeyState(VK_RWIN) & 0x8000) != 0;
            if (AppConfig.EnableCopilotMap && isWin) isWin = false;
            return isWin;
        }

        private static IntPtr ResolveContextHwnd()
        {
            IntPtr hwnd = ContextHwnd;
            if (hwnd != IntPtr.Zero) { _lastResolvedContextHwnd = hwnd; return hwnd; }
            if (_lastResolvedContextHwnd != IntPtr.Zero) return _lastResolvedContextHwnd;
            hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd != IntPtr.Zero) _lastResolvedContextHwnd = hwnd;
            return hwnd;
        }

        private static IntPtr BypassKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam) => NativeMethods.CallNextHookEx(_kbdHookId, nCode, wParam, lParam);

        private static bool ShouldBypassHook(int nCode, IntPtr wParam)
        {
            if (nCode < 0 || IsSending || !IsEnabled) return true;
            return !IsInterestedKeyboardMessage(wParam.ToInt32());
        }

        private static bool TryResolveKeyboardContext(int vkCode, out IntPtr hFore, out bool capsOn, out bool isHangulMode, out bool isHanjaOrRCtrl)
        {
            isHanjaOrRCtrl = IsHanjaOrRightCtrl(vkCode);
            if (!isHanjaOrRCtrl && HasBlockedModifierChord(false)) { hFore = IntPtr.Zero; capsOn = false; isHangulMode = false; return false; }
            hFore = ResolveContextHwnd();
            if (hFore == IntPtr.Zero) { capsOn = false; isHangulMode = false; return false; }

            capsOn = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 0x0001) != 0;
            isHangulMode = ImeState.CheckHangulPublic(hFore);
            return true;
        }

        private static IntPtr HandleHanjaKey(int nCode, IntPtr wParam, IntPtr lParam, int msg, IntPtr hFore, bool capsOn, bool isHangulMode)
        {
            if (isHangulMode & !capsOn) return BypassKeyboardHook(nCode, wParam, lParam);

            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                if (!isHangulMode)
                {
                    ImeState.SetHangulState(hFore, true);
                    if (!capsOn) NativeMethods.SimulateCapsLock();
                    MainForm.Instance?.ShowOverlay(UiText.HangulCapsMode, mode: OverlayPositionMode.ModeSwitch);
                    return (IntPtr)1;
                }

                IKeyProcessor? hanjaProcessor = ActiveProcessor;
                if (hanjaProcessor != null && hanjaProcessor.ProcessHanjaKey(hFore, capsOn, isHangulMode))
                {
                    MainForm.Instance?.RequestLayoutRefresh();
                    return (IntPtr)1;
                }
            }
            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
            {
                return (IntPtr)1;
            }

            return BypassKeyboardHook(nCode, wParam, lParam);
        }

        private static IntPtr HandleLanguageProcessorKey(int nCode, IntPtr wParam, IntPtr lParam, int msg, int vkCode, IntPtr hFore, bool capsOn, bool isHangulMode)
        {
            IKeyProcessor? keyProcessor = ActiveProcessor;
            if (keyProcessor == null) return BypassKeyboardHook(nCode, wParam, lParam);

            uint threadId = NativeMethods.GetWindowThreadProcessId(hFore, out _);
            ushort langId = (ushort)(NativeMethods.GetKeyboardLayout(threadId).ToInt64() & 0xFFFF);
            if (langId != 0x0412) return BypassKeyboardHook(nCode, wParam, lParam);

            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                bool isShift = (NativeMethods.GetKeyState(VK_LSHIFT) & 0x8000) != 0;
                if (keyProcessor.ProcessKeyDown(vkCode, isShift, capsOn, hFore, isHangulMode)) return (IntPtr)1;
            }
            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
            {
                if (capsOn && isHangulMode && ((vkCode >= 0x41 && vkCode <= 0x5A) || KeyboardLayoutAnalyzer.IsSymbolOrNumber(vkCode) || vkCode == VK_SPACE))
                {
                    return (IntPtr)1;
                }
            }

            return BypassKeyboardHook(nCode, wParam, lParam);
        }

        private static bool _lastCapsOn = false;
        private static bool _lastIsHangulMode = false;
        private static IntPtr _lastContextHwndForReset = IntPtr.Zero;

        private static void UpdateStateTracking(IntPtr hFore, bool capsOn, bool isHangulMode)
        {
            if (_lastContextHwndForReset != hFore || capsOn != _lastCapsOn)
            {
                ActiveProcessor?.OnMouseClick();
            }
            _lastCapsOn = capsOn;
            _lastIsHangulMode = isHangulMode;
            _lastContextHwndForReset = hFore;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
        private static IntPtr KbdHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (ShouldBypassHook(nCode, wParam)) return BypassKeyboardHook(nCode, wParam, lParam);

            try
            {
                int vkCode = Marshal.ReadInt32(lParam);
                int msg = wParam.ToInt32();
                if (AppConfig.LogLevel >= 2) Debug.WriteLine($"KbdHookCallback: vkCode={vkCode} wParam={wParam}");

                if (KanjiCandidateOverlay.IsActive)
                {
                    if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                    {
                        if (KanjiCandidateOverlay.HandleKeyFromHook(vkCode))
                        {
                            return (IntPtr)1;
                        }
                    }
                    else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
                    {
                        return (IntPtr)1;
                    }
                }

                if (vkCode == VK_SPACE) 
                {
                    if (TryResolveKeyboardContext(vkCode, out IntPtr hForeSpc, out bool capsOnSpc, out bool isHangulModeSpc, out _))
                    {
                        UpdateStateTracking(hForeSpc, capsOnSpc, isHangulModeSpc);

                        if (capsOnSpc && isHangulModeSpc)
                        {
                            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                            {
                                bool isShiftSpc = (NativeMethods.GetKeyState(VK_LSHIFT) & 0x8000) != 0;
                                ActiveProcessor?.ProcessKeyDown(vkCode, isShiftSpc, capsOnSpc, hForeSpc, isHangulModeSpc);
                                
                                string compText = GetCompositionText();
                                if (AppConfig.LogLevel >= 2) Debug.WriteLine($"KbdHookCallback: Space key detected, compositionBuffer='{compText}'");

                                if (!string.IsNullOrEmpty(compText))
                                {
                                    try
                                    {
                                        if (HandleKanjiConversion(hForeSpc, compText, false))
                                        {
                                            return (IntPtr)1;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        if (AppConfig.LogLevel >= 1) Debug.WriteLine($"KbdHookCallback: HandleKanjiConversion threw: {ex}");
                                    }
                                }
                                else
                                {
                                    Task.Run(() =>
                                    {
                                        try
                                        {
                                            string? selectedText = TextSelectionUtils.ReadSelectedText();
                                            if (AppConfig.LogLevel >= 2) Trace.WriteLine($"[Space] selectedText='{selectedText}'");

                                            if (!string.IsNullOrEmpty(selectedText) && MozcDictionary.IsJapaneseText(selectedText))
                                            {
                                                IsReplacingSelection = true;
                                                bool converted = HandleKanjiConversion(hForeSpc, selectedText, true);
                                                if (!converted)
                                                {
                                                    IsReplacingSelection = false;
                                                    TextSelectionUtils.CancelSelection();
                                                    SendSpaceKey();
                                                }
                                            }
                                            else
                                            {
                                                SendSpaceKey();
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            if (AppConfig.LogLevel >= 1) Trace.WriteLine($"[Space] selectedText conversion error: {ex.Message}");
                                            SendSpaceKey();
                                        }
                                    });
                                    return (IntPtr)1;
                                }
                            }
                            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
                            {
                                return (IntPtr)1;
                            }
                        }
                    }
                }

                if (msg == NativeMethods.WM_KEYDOWN)
                {
                    if (vkCode == VK_BACK)
                    {
                        RemoveLastCompositionChar();
                    }
                    else if (vkCode is VK_RETURN or VK_ESCAPE or VK_TAB or (>= 0x21 and <= 0x28))
                    {
                        ClearCompositionBuffer();
                    }
                }

                if (!TryResolveKeyboardContext(vkCode, out IntPtr hFore, out bool capsOn, out bool isHangulMode, out bool isHanjaOrRCtrl))
                    return BypassKeyboardHook(nCode, wParam, lParam);

                UpdateStateTracking(hFore, capsOn, isHangulMode);

                if (isHanjaOrRCtrl) return HandleHanjaKey(nCode, wParam, lParam, msg, hFore, capsOn, isHangulMode);

                return HandleLanguageProcessorKey(nCode, wParam, lParam, msg, vkCode, hFore, capsOn, isHangulMode);
            }
            catch (Exception ex)
            {
                if (AppConfig.LogLevel >= 1) Trace.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Error in KbdHookCallback: {ex.Message}\n{ex.StackTrace}");
            }

            return BypassKeyboardHook(nCode, wParam, lParam);
        }

        private static bool HandleKanjiConversion(IntPtr hFore, string? inputComp = null, bool isReplacingSelection = false)
        {
            try
            {
                if (AppConfig.LogLevel >= 2) Trace.WriteLine($"HandleKanjiConversion: hFore={hFore}");
                string? fullText = !string.IsNullOrEmpty(inputComp) ? inputComp : GetCompositionText();
                if (AppConfig.LogLevel >= 2) Trace.WriteLine($"HandleKanjiConversion: fullText='{fullText}'");

                if (string.IsNullOrEmpty(fullText)) return false;

                string targetToConvert = fullText;
                string preservedPrefix = string.Empty;
                string preservedSuffix = string.Empty;

                if (fullText.Length > AppConfig.MaxKanjiConversionLength)
                {
                    int maxLen = AppConfig.MaxKanjiConversionLength;
                    if (!isReplacingSelection)
                    {
                        targetToConvert = fullText.Substring(fullText.Length - maxLen);
                        preservedPrefix = fullText.Substring(0, fullText.Length - maxLen);
                    }
                    else
                    {
                        targetToConvert = fullText.Substring(0, maxLen);
                        preservedSuffix = fullText.Substring(maxLen);
                    }
                }

                if (!MozcDictionary.IsJapaneseText(targetToConvert)) return false;

                Task.Run(async () =>
                {
                    if (!MozcDictionary.IsLoaded)
                    {
                        try
                        {
                            MozcDictionary.LoadDictionary();
                            int waited = 0;
                            while (!MozcDictionary.IsLoaded && waited < 2000)
                            {
                                Thread.Sleep(120);
                                waited += 120;
                            }
                            MozcDictionary.PrintStatistics();
                        }
                        catch (Exception ex) { if (AppConfig.LogLevel >= 1) Trace.WriteLine($"HandleKanjiConversion: LoadDictionary failed: {ex}"); }
                    }

                    bool foundCandidates = false;
                    try
                    {
                        if (AppConfig.UseGoogleApi)
                        {
                            var googleCandidates = await GoogleJapaneseInputApi.GetCandidatesAsync(targetToConvert);
                            if (AppConfig.LogLevel >= 2) Trace.WriteLine($"HandleKanjiConversion: Google API candidates count={googleCandidates?.Count}");
                            if (googleCandidates != null && googleCandidates.Count > 0)
                            {
                                var finalCandidates = googleCandidates
                                    .Select(c => preservedPrefix + c + preservedSuffix)
                                    .ToList();

                                MainForm.Instance?.BeginInvoke(new Action(() => 
                                    MainForm.Instance.ShowKanjiCandidateAsync(finalCandidates, fullText, isReplacingSelection)));
                                foundCandidates = true;
                                return;
                            }
                        }

                        if (AppConfig.EnableLocalConversion)
                        {
                            var entries = KanjiConverter.GetKanjiCandidatesOptimized(targetToConvert);

                            if (AppConfig.LogLevel >= 2) Trace.WriteLine($"HandleKanjiConversion: Local candidates count={entries.Count}");
                            if (entries.Count > 0)
                            {
                                var finalLocalCandidates = entries
                                    .Select(e => preservedPrefix + e.Kanji + preservedSuffix)
                                    .ToList();

                                if (AppConfig.LogLevel >= 2) Trace.WriteLine("HandleKanjiConversion: showing local candidates");
                                MainForm.Instance?.BeginInvoke(new Action(() => 
                                    MainForm.Instance.ShowKanjiCandidateAsync(finalLocalCandidates, fullText, isReplacingSelection)));
                                foundCandidates = true;
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (AppConfig.LogLevel >= 1) Debug.WriteLine($"HandleKanjiConversion: lookup failed: {ex}");
                    }

                    if (!foundCandidates)
                    {
                        ClearCompositionBuffer();
                        SendSpaceKey();
                    }
                });

                return true;
            }
            catch { return false; }
        }
    }
}