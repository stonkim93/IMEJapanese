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
using System.Net.Http;
using System.Text.Json;
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

            // ─────────────────────────────────────────────────────────────
            // [버그 픽스] SendReplacement 후 대상 앱의 포커스가 돌아오면서 
            // 과도기적 IME 상태 혼란으로 인해 일시적으로 '영어 대문자' 상태가 되는 현상 차단.
            // 문자열 치환 직후 대상 창의 한글(Japanese1/2/3) 상태를 강제로 복원합니다.
            // ─────────────────────────────────────────────────────────────
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
                    // ─────────────────────────────────────────────────────
                    // [한자변환 수정] 한자 후보 활성 시 외부 클릭 감지 로직 완성
                    // 마우스 훅에서 직접 외부 클릭을 감지하고 후보창을 닫습니다.
                    // 이 경우 버퍼 초기화는 우회하여 타이핑 손실을 막습니다.
                    // ─────────────────────────────────────────────────────
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
            isHangulMode = CachedIsHangulMode;
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
                // 한자키 KEYUP을 처리된 경우 소비하여 OS 전달 방지
                return (IntPtr)1;
            }

            return BypassKeyboardHook(nCode, wParam, lParam);
        }

        private static IntPtr HandleLanguageProcessorKey(int nCode, IntPtr wParam, IntPtr lParam, int msg, int vkCode, IntPtr hFore, bool capsOn, bool isHangulMode)
        {
            IKeyProcessor? keyProcessor = ActiveProcessor;
            if (keyProcessor == null || ContextLangId != 0x0412) return BypassKeyboardHook(nCode, wParam, lParam);

            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                bool isShift = (NativeMethods.GetKeyState(0x10) & 0x8000) != 0;
                if (keyProcessor.ProcessKeyDown(vkCode, isShift, capsOn, hFore, isHangulMode)) return (IntPtr)1;
            }
            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
            {
                // A-Z 알파벳 입력 KEYUP 이벤트를 소비하여 OS 누수로 인한 중복입력 방지
                if (capsOn && isHangulMode && vkCode >= 0x41 && vkCode <= 0x5A)
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

                // ─────────────────────────────────────────────────────
                // [한자변환 수정] 한자 후보 오버레이 활성 시 키 가로채기
                // 오버레이가 포커스를 받지 않으므로, 키보드 훅에서 직접
                // 방향키/Enter/Esc/숫자키 등을 가로채서 오버레이에 전달합니다.
                // ─────────────────────────────────────────────────────
                if (KanjiCandidateOverlay.IsActive)
                {
                    if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                    {
                        // KEYDOWN: 오버레이에 키를 전달하고 소비
                        if (KanjiCandidateOverlay.HandleKeyFromHook(vkCode))
                        {
                            return (IntPtr)1; // 키 소비 → 대상 앱에 전달 안 함
                        }
                    }
                    else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
                    {
                        // KEYUP: 한자 후보 활성 중에는 KEYUP도 소비하여
                        // 대상 앱에 의도치 않은 키 이벤트가 전달되지 않도록 방지합니다.
                        return (IntPtr)1;
                    }
                }

                // [수정 핵심 로직] 스페이스바 누수(Leak) 완벽 차단
                // 한글 CAPS (일본어) 모드에서는 스페이스바의 DOWN, UP 이벤트를 무조건 먼저 가로채서 소비시킴
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
                                            return (IntPtr)1; // 한자 변환 성공시 무조건 DOWN 소비
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"KbdHookCallback: HandleKanjiConversion threw: {ex}");
                                    }
                                }
                                else
                                {
                                    // 컴포지션 버퍼가 비어있을 때 선택영역 체크 후 변환
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
                                    return (IntPtr)1; // DOWN 완전 소비
                                }
                            }
                            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
                            {
                                return (IntPtr)1; // 일본어 모드에서는 스페이스바 KEYUP 또한 무조건 소비하여 OS 누수 방지
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

                // msg 파라미터 추가하여 이벤트 분기 처리 (중복 입력 방지)
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
                string? selectedText = !string.IsNullOrEmpty(inputComp) ? inputComp : GetCompositionText();
                Trace.WriteLine($"HandleKanjiConversion: selectedText='{selectedText}'");

                if (string.IsNullOrEmpty(selectedText)) return false;
                if (!MozcDictionary.IsJapaneseText(selectedText)) return false;

                // 사전 로딩 등 Blocking 코드를 Task 내부로 완전히 이동 (스페이스바 타임아웃 방지)
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
                            var googleCandidates = await GoogleJapaneseInputApi.GetCandidatesAsync(selectedText);
                            Trace.WriteLine($"HandleKanjiConversion: Google API candidates count={googleCandidates?.Count}");
                            if (googleCandidates != null && googleCandidates.Count > 0)
                            {
                                MainForm.Instance?.BeginInvoke(new Action(() => MainForm.Instance.ShowKanjiCandidateAsync(googleCandidates, selectedText, isReplacingSelection)));
                                foundCandidates = true;
                                return;
                            }
                        }

                        if (AppConfig.EnableLocalConversion)
                        {
                            var vits = BuildViterbiCandidates(selectedText, maxCandidates: 9);
                            Trace.WriteLine($"HandleKanjiConversion: Viterbi candidates count={vits?.Count}");
                            if (vits != null && vits.Count > 0)
                            {
                                Trace.WriteLine("HandleKanjiConversion: showing Viterbi candidates");
                                MainForm.Instance?.BeginInvoke(new Action(() => MainForm.Instance.ShowKanjiCandidateAsync(vits, selectedText, isReplacingSelection)));
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
                        var replacements = BuildReplacementCandidates(selectedText);
                        Trace.WriteLine($"HandleKanjiConversion: replacement candidates count={replacements?.Count}");
                        if (replacements != null && replacements.Count > 0)
                        {
                            var objs = replacements.Select(r => r.ReplacementText).ToList();
                            Trace.WriteLine("HandleKanjiConversion: showing replacement candidates");
                            MainForm.Instance?.BeginInvoke(new Action(() => MainForm.Instance.ShowKanjiCandidateAsync(objs, selectedText, isReplacingSelection)));
                            foundCandidates = true;
                        }
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

        internal sealed class ReplacementEntry
        {
            public string ReplacementText { get; init; } = string.Empty;
            public string MatchedReading { get; init; } = string.Empty;
            public MozcDictionary.KanjiEntry? SourceEntry { get; init; }
            public override string ToString() => ReplacementText;
        }

        private static List<ReplacementEntry> BuildReplacementCandidates(string selectedText)
        {
            const int MaxCandidatesPerSubstring = 5;
            const int MaxTotalCandidates = 12;

            var results = new List<ReplacementEntry>();
            string normalized = MozcDictionary.NormalizeToHiragana(selectedText);
            int len = normalized.Length;

            for (int start = 0; start < len; start++)
            {
                var matches = MozcDictionary.GetEntriesForReadingAt(normalized, start, MaxCandidatesPerSubstring);
                if (matches == null || matches.Count == 0) continue;

                foreach (var m in matches)
                {
                    int end = start + m.Length;
                    string prefix = selectedText.Substring(0, start);
                    string suffix = selectedText.Substring(Math.Min(end, selectedText.Length));
                    var c = m.Entry;
                    var rep = new ReplacementEntry
                    {
                        ReplacementText = prefix + c.Kanji + suffix,
                        MatchedReading = normalized.Substring(start, m.Length),
                        SourceEntry = c
                    };
                    results.Add(rep);
                    if (results.Count >= MaxTotalCandidates) break;
                }

                if (results.Count >= MaxTotalCandidates) break;
            }

            var dedup = results.GroupBy(x => x.ReplacementText).Select(g => g.First()).ToList();
            return dedup;
        }

        private static List<string> BuildViterbiCandidates(string selectedText, int maxCandidates = 5, int beamWidthPerPos = 6)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(selectedText)) return results;

            string normalized = MozcDictionary.NormalizeToHiragana(selectedText);
            int len = normalized.Length;

            int beam = Math.Max(1, beamWidthPerPos);
            var dp = new List<(int cost, List<MozcDictionary.KanjiEntry> chain)>[len + 1];
            for (int i = 0; i <= len; i++) dp[i] = new List<(int, List<MozcDictionary.KanjiEntry>)>();

            dp[0].Add((0, new List<MozcDictionary.KanjiEntry>()));

            for (int pos = 0; pos < len; pos++)
            {
                if (dp[pos].Count == 0) continue;
                var matches = MozcDictionary.GetEntriesForReadingAt(normalized, pos, maxPerSubstring: Math.Max(1, AppConfig.MaxCandidatesPerSubstring));
                if (matches == null || matches.Count == 0) continue;

                foreach (var partial in dp[pos])
                {
                    int prevRightId = partial.chain.Count > 0 ? partial.chain[^1].RightId : 0;

                    foreach (var m in matches)
                    {
                        int next = pos + m.Length;

                        int transitionCost = MozcDictionary.GetTransitionCost(prevRightId, m.Entry.LeftId);

                        int newCost = partial.cost + m.Entry.Cost + transitionCost;
                        var newChain = new List<MozcDictionary.KanjiEntry>(partial.chain) { m.Entry };
                        dp[next].Add((newCost, newChain));
                    }
                }

                for (int p = pos + 1; p <= Math.Min(len, pos + 32); p++)
                {
                    if (dp[p].Count > beam)
                    {
                        dp[p] = dp[p].OrderBy(t => t.cost).Take(beam).ToList();
                    }
                }
            }

            if (dp[len].Count == 0) return results;

            var ordered = dp[len].OrderBy(t => t.cost).Take(Math.Max(1, Math.Min(maxCandidates, AppConfig.ViterbiMaxCandidates))).ToList();
            foreach (var item in ordered)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var e in item.chain)
                {
                    sb.Append(e.Kanji);
                }
                string rep = sb.ToString();
                if (!string.IsNullOrEmpty(rep)) results.Add(rep);
            }

            return results.Distinct().ToList();
        }
    }
}