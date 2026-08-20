// ImeNativeCore.cs
#nullable enable
using System;
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
        private static readonly Dictionary<IntPtr, bool> _hangulStateCache = new Dictionary<IntPtr, bool>();
        private static IntPtr _lastCheckedHwnd = IntPtr.Zero;
        private static DateTime _lastCheckedTime = DateTime.MinValue;

        public static bool IsHangul(State state) =>
            state == State.Hangul || state == State.JapaneseHangul1 || state == State.JapaneseHangul2 || state == State.JapaneseHangul3;

        public static State Detect(IntPtr foregroundHwnd,
            bool enableJapanese1 = false, bool enableJapanese2 = false, bool enableJapanese3 = false)
        {
            bool capsOn = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 0x0001) != 0;
            if (foregroundHwnd == IntPtr.Zero) return capsOn ? State.EnglishUpper : State.EnglishLower;

            uint threadId = NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _);
            long hklValue = NativeMethods.GetKeyboardLayout(threadId).ToInt64();
            ushort langId = (ushort)(hklValue & 0xFFFF);

            if (langId == 0x0411) return State.JapaneseIME;

            if (langId == 0x0412) // 한국어 레이아웃
            {
                bool isHangul = CheckHangulPublic(foregroundHwnd);
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
                Debug.WriteLine($"[CompositionBuffer] Appended '{text}' -> current: '{_compositionBuffer}'");
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
                    Debug.WriteLine($"[CompositionBuffer] Removed last char -> current: '{_compositionBuffer}'");
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
                    Debug.WriteLine("[CompositionBuffer] Cleared");
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

            Debug.WriteLine($"CommitKanjiConversion: backCount={backCount}, selectedText='{selectedText}'");
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
                bool isShift = (NativeMethods.GetKeyState(0x10) & 0x8000) != 0;
                bool isLWin = (NativeMethods.GetKeyState(0x5B) & 0x8000) != 0;
                bool isRWin = (NativeMethods.GetKeyState(0x5C) & 0x8000) != 0;

                if (isShift) NativeMethods.keybd_event(0x10, 0, 0x0002, UIntPtr.Zero);
                if (isLWin) NativeMethods.keybd_event(0x5B, 0, 0x0002, UIntPtr.Zero);
                if (isRWin) NativeMethods.keybd_event(0x5C, 0, 0x0002, UIntPtr.Zero);
            }

            for (int i = 0; i < backCount; i++) NativeMethods.SendBackspace();
            if (!string.IsNullOrEmpty(text)) NativeMethods.SendUnicodeString(text);
            IsSending = false;
        }

        private static void SendSpaceKey()
        {
            IsSending = true;
            NativeMethods.keybd_event(0x20, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event(0x20, 0, 0x0002, UIntPtr.Zero);
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

        private static bool IsHanjaOrRightCtrl(int vkCode) => vkCode == 0x19 || vkCode == 0xA3;

        private static bool HasBlockedModifierChord(bool allowCtrlForCurrentKey)
        {
            bool isCtrl = (NativeMethods.GetKeyState(0x11) & 0x8000) != 0;
            if (isCtrl && !allowCtrlForCurrentKey) return true;
            if ((NativeMethods.GetKeyState(0x12) & 0x8000) != 0) return true;

            bool isWin = (NativeMethods.GetKeyState(0x5B) & 0x8000) != 0 || (NativeMethods.GetKeyState(0x5C) & 0x8000) != 0;
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
            if (HasBlockedModifierChord(isHanjaOrRCtrl)) { hFore = IntPtr.Zero; capsOn = false; isHangulMode = false; return false; }
            hFore = ResolveContextHwnd();
            if (hFore == IntPtr.Zero) { capsOn = false; isHangulMode = false; return false; }

            capsOn = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 0x0001) != 0;
            // 실시간 IME 한글 입력 모드 동적 확인
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
                    MainForm.Instance?.ShowOverlay(UiText.HangulCapsMode);
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

            // 활성 윈도우 스레드의 언어 레이아웃 동적 검증 (한국어 레이아웃 0x0412 확인)
            uint threadId = NativeMethods.GetWindowThreadProcessId(hFore, out _);
            ushort langId = (ushort)(NativeMethods.GetKeyboardLayout(threadId).ToInt64() & 0xFFFF);
            if (langId != 0x0412) return BypassKeyboardHook(nCode, wParam, lParam);

            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                bool isShift = (NativeMethods.GetKeyState(0x10) & 0x8000) != 0;
                if (keyProcessor.ProcessKeyDown(vkCode, isShift, capsOn, hFore, isHangulMode)) return (IntPtr)1;
            }
            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
            {
                // 한글CAPS 모드일 때 처리되는 키(알파벳, 기호, 스페이스)의 KEYUP 이벤트를 확실히 차단
                if (capsOn && isHangulMode && ((vkCode >= 0x41 && vkCode <= 0x5A) || KeyboardLayoutAnalyzer.IsSymbolOrNumber(vkCode) || vkCode == 0x20))
                {
                    return (IntPtr)1;
                }
            }

            return BypassKeyboardHook(nCode, wParam, lParam);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
        private static IntPtr KbdHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (ShouldBypassHook(nCode, wParam)) return BypassKeyboardHook(nCode, wParam, lParam);

            try
            {
                int vkCode = Marshal.ReadInt32(lParam);
                int msg = wParam.ToInt32();
                Debug.WriteLine($"KbdHookCallback: vkCode={vkCode} wParam={wParam}");

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

                if (vkCode == 0x20)
                {
                    if (TryResolveKeyboardContext(vkCode, out IntPtr hForeSpc, out bool capsOnSpc, out bool isHangulModeSpc, out _))
                    {
                        if (capsOnSpc && isHangulModeSpc)
                        {
                            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                            {
                                string compText = GetCompositionText();
                                Debug.WriteLine($"KbdHookCallback: Space key detected, compositionBuffer='{compText}'");

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
                                        Debug.WriteLine($"KbdHookCallback: HandleKanjiConversion threw: {ex}");
                                    }
                                }
                                else
                                {
                                    Task.Run(() =>
                                    {
                                        string? selectedText = TextSelectionUtils.ReadSelectedText();
                                        if (!string.IsNullOrEmpty(selectedText) && MozcDictionary.IsJapaneseText(selectedText))
                                        {
                                            IsReplacingSelection = true;
                                            if (!HandleKanjiConversion(hForeSpc, selectedText, true))
                                            {
                                                IsReplacingSelection = false;
                                                SendSpaceKey();
                                            }
                                        }
                                        else
                                        {
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
                    if (vkCode == 0x08)
                    {
                        RemoveLastCompositionChar();
                    }
                    else if (vkCode is 0x0D or 0x1B or 0x09 or (>= 0x21 and <= 0x28))
                    {
                        ClearCompositionBuffer();
                    }
                }

                if (!TryResolveKeyboardContext(vkCode, out IntPtr hFore, out bool capsOn, out bool isHangulMode, out bool isHanjaOrRCtrl))
                    return BypassKeyboardHook(nCode, wParam, lParam);

                if (isHanjaOrRCtrl) return HandleHanjaKey(nCode, wParam, lParam, msg, hFore, capsOn, isHangulMode);

                return HandleLanguageProcessorKey(nCode, wParam, lParam, msg, vkCode, hFore, capsOn, isHangulMode);
            }
            catch { }

            return BypassKeyboardHook(nCode, wParam, lParam);
        }

        private static bool HandleKanjiConversion(IntPtr hFore, string? inputComp = null, bool isReplacingSelection = false)
        {
            try
            {
                Trace.WriteLine($"HandleKanjiConversion: hFore={hFore}");
                // 전체 원본 문자열 확보
                string? fullText = !string.IsNullOrEmpty(inputComp) ? inputComp : GetCompositionText();
                Trace.WriteLine($"HandleKanjiConversion: fullText='{fullText}'");

                if (string.IsNullOrEmpty(fullText)) return false;

                string targetToConvert = fullText;
                string preservedPrefix = string.Empty;
                string preservedSuffix = string.Empty;

                // [수정됨] 최대 글자수 초과 시 보존 영역과 변환 영역 분리
                if (fullText.Length > AppConfig.MaxKanjiConversionLength)
                {
                    int maxLen = AppConfig.MaxKanjiConversionLength;
                    if (!isReplacingSelection)
                    {
                        // 키보드 입력: 뒤쪽(최신 입력)을 변환 대상으로, 앞쪽을 보존
                        targetToConvert = fullText.Substring(fullText.Length - maxLen);
                        preservedPrefix = fullText.Substring(0, fullText.Length - maxLen);
                    }
                    else
                    {
                        // 마우스 선택: 앞쪽을 변환 대상으로, 뒤쪽을 보존
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
                        catch (Exception ex) { Trace.WriteLine($"HandleKanjiConversion: LoadDictionary failed: {ex}"); }
                    }

                    bool foundCandidates = false;
                    try
                    {
                        if (AppConfig.UseGoogleApi)
                        {
                            var googleCandidates = await GoogleJapaneseInputApi.GetCandidatesAsync(targetToConvert);
                            Trace.WriteLine($"HandleKanjiConversion: Google API candidates count={googleCandidates?.Count}");
                            if (googleCandidates != null && googleCandidates.Count > 0)
                            {
                                // [수정됨] 변환된 후보들에 보존된 Prefix와 Suffix를 결합
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
                            var entries = KanjiConverter.GetKanjiCandidates(targetToConvert);
                            Trace.WriteLine($"HandleKanjiConversion: Local candidates count={entries.Count}");
                            if (entries.Count > 0)
                            {
                                // [수정됨] 로컬 사전 변환 후보들에 보존된 Prefix와 Suffix를 결합
                                var finalLocalCandidates = entries
                                    .Select(e => preservedPrefix + e.Kanji + preservedSuffix)
                                    .ToList();

                                Trace.WriteLine("HandleKanjiConversion: showing local candidates");
                                MainForm.Instance?.BeginInvoke(new Action(() => 
                                    MainForm.Instance.ShowKanjiCandidateAsync(finalLocalCandidates, fullText, isReplacingSelection)));
                                foundCandidates = true;
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"HandleKanjiConversion: lookup failed: {ex}");
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