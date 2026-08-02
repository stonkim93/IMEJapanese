// ImeNativeCore.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

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
                bool isHangul = IsHangulModeSystemWide(foregroundHwnd);
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

        public static bool IsHangulModeSystemWide(IntPtr foregroundHwnd)
        {
            return CheckHangulPublic(foregroundHwnd);
        }

        public static bool CheckHangulPublic(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            if (_hangulStateCache.Count > MaxCacheSize)
            {
                _hangulStateCache.Clear();
            }

            IntPtr hImeWnd = GetTargetImeWindow(hWnd);
            if (hImeWnd != IntPtr.Zero)
            {
                IntPtr res = NativeMethods.SendMessageTimeout(hImeWnd, NativeMethods.WM_IME_CONTROL, (IntPtr)NativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 30, out IntPtr result);
                if (res != IntPtr.Zero)
                {
                    bool isHangul = ((uint)result.ToInt64() & NativeMethods.IME_CMODE_NATIVE) != 0;
                    _hangulStateCache[hWnd] = isHangul;
                    return isHangul;
                }
            }

            IntPtr hIMC = NativeMethods.ImmGetContext(hWnd);
            if (hIMC != IntPtr.Zero)
            {
                bool success = NativeMethods.ImmGetConversionStatus(hIMC, out uint conv, out _);
                NativeMethods.ImmReleaseContext(hWnd, hIMC);
                if (success)
                {
                    bool isHangul = (conv & NativeMethods.IME_CMODE_NATIVE) != 0;
                    _hangulStateCache[hWnd] = isHangul;
                    return isHangul;
                }
            }
            
            return _hangulStateCache.TryGetValue(hWnd, out bool cachedState) ? cachedState : false;
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
                }
            }
        }
    }

    // =======================================================================================
    // [최적화] 전역 시스템 훅 모듈 통합 및 누락되었던 키 제어 로직 복원 (GlobalInputHook)
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

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try { if (nCode >= 0 && wParam.ToInt32() == NativeMethods.WM_LBUTTONDOWN) ActiveProcessor?.OnMouseClick(); }
            catch { }
            return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        // --- 복원된 핵심 입력 훅 로직 ---
        private static bool IsInterestedKeyboardMessage(int msg) => msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;

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

        private static IntPtr HandleHanjaKey(int nCode, IntPtr wParam, IntPtr lParam, IntPtr hFore, bool capsOn, bool isHangulMode)
        {
            if (isHangulMode & !capsOn) return BypassKeyboardHook(nCode, wParam, lParam); 

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

            return BypassKeyboardHook(nCode, wParam, lParam);
        }

        private static IntPtr HandleLanguageProcessorKey(int nCode, IntPtr wParam, IntPtr lParam, int vkCode, IntPtr hFore, bool capsOn, bool isHangulMode)
        {
            IKeyProcessor? keyProcessor = ActiveProcessor;
            if (keyProcessor == null || ContextLangId != 0x0412) return BypassKeyboardHook(nCode, wParam, lParam);

            bool isShift = (NativeMethods.GetKeyState(0x10) & 0x8000) != 0;
            if (keyProcessor.ProcessKeyDown(vkCode, isShift, capsOn, hFore, isHangulMode)) return (IntPtr)1; 

            return BypassKeyboardHook(nCode, wParam, lParam);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
        private static IntPtr KbdHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (ShouldBypassHook(nCode, wParam)) return BypassKeyboardHook(nCode, wParam, lParam);

            try
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (!TryResolveKeyboardContext(vkCode, out IntPtr hFore, out bool capsOn, out bool isHangulMode, out bool isHanjaOrRCtrl))
                    return BypassKeyboardHook(nCode, wParam, lParam);

                if (isHanjaOrRCtrl) return HandleHanjaKey(nCode, wParam, lParam, hFore, capsOn, isHangulMode);

                return HandleLanguageProcessorKey(nCode, wParam, lParam, vkCode, hFore, capsOn, isHangulMode);
            }
            catch { }

            return BypassKeyboardHook(nCode, wParam, lParam);
        }
    }
}