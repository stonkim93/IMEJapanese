// NativeMethods.cs
#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace IMEJapanese
{
    // =======================================================================================
    // [최적화] 불필요해진 포인터 및 렌더링 관련 Win32 P/Invoke API 및 구조체 완전 제거
    // =======================================================================================
    internal static unsafe partial class NativeMethods
    {
        #region Constants
        public const int VK_CAPITAL = 0x14;                 // Caps Lock 키의 가상 키 코드
        public const int WM_IME_CONTROL = 0x0283;           // IME 제어 메시지
        public const int IMC_GETCONVERSIONMODE = 0x0001;    // IME 변환 모드 가져오기
        public const int IMC_SETCONVERSIONMODE = 0x0002;    // IME 변환 모드 설정
        public const uint IME_CMODE_NATIVE = 0x0001;        // IME 변환 모드: 한글 모드
        public const uint SMTO_ABORTIFHUNG = 0x0002;        // SendMessageTimeout 플래그: 응답이 없으면 중단
        public const int WH_KEYBOARD_LL = 13;               // Low-level keyboard hook
        public const int WH_MOUSE_LL = 14;                  // Low-level mouse hook (클릭 시 입력 상태 초기화용)
        public const int WM_KEYDOWN = 0x0100;               // Key down 메시지
        public const int WM_SYSKEYDOWN = 0x0104;            // System Key down 메시지
        public const int WM_LBUTTONDOWN = 0x0201;           // Left mouse button down 메시지
        public const uint INPUT_KEYBOARD = 1;               // 키보드 입력 유형
        public const uint KEYEVENTF_UNICODE = 0x0004;       // Unicode 키 이벤트 플래그
        public const uint KEYEVENTF_KEYUP = 0x0002;         // 키 업 이벤트 플래그
        public const int MDT_EFFECTIVE_DPI = 0;             // 모니터 DPI 가져오기: 실제 DPI
        public const uint MONITOR_DEFAULTTONEAREST = 2;     // 기본 모니터: 가장 가까운 모니터

        #endregion

        #region Structs
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        //[StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
        //[StructLayout(LayoutKind.Sequential)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO { public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public int rectLeft, rectTop, rectRight, rectBottom; }
        //[StructLayout(LayoutKind.Sequential)] public struct BITMAPINFO { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion U; }
        [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public HARDWAREINPUT hi; }
        [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy, mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }
        #endregion

        #region User32 (General & Keyboard/Mouse)
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] public static partial IntPtr SetWindowsHookEx(int idHook, delegate* unmanaged[Stdcall]<int, IntPtr, IntPtr, IntPtr> lpfn, IntPtr hMod, uint dwThreadId);
        [LibraryImport("user32.dll", EntryPoint = "UnhookWindowsHookEx", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool UnhookWindowsHookEx(IntPtr hhk);
        [LibraryImport("user32.dll", EntryPoint = "CallNextHookEx")] public static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [LibraryImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)] public static partial uint SendInput(uint nInputs, ReadOnlySpan<INPUT> pInputs, int cbSize);
        [LibraryImport("user32.dll", EntryPoint = "GetDpiForSystem")] public static partial uint GetDpiForSystem();
        [LibraryImport("user32.dll", EntryPoint = "MonitorFromWindow")] public static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial IntPtr GetForegroundWindow();
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial IntPtr GetKeyboardLayout(uint idThread);
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial short GetKeyState(int keyCode);
        [LibraryImport("user32.dll")][SuppressGCTransition][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetCursorPos(out POINT lpPoint);
        [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] public static partial IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
        [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)] public static partial int GetClassName(IntPtr hWnd, char* lpClassName, int nMaxCount);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetForegroundWindow(IntPtr hWnd);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyIcon(IntPtr hIcon);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        // Keyboard/IME APIs
        [DllImport("user32.dll", SetLastError = true)] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out, MarshalAs(UnmanagedType.LPWStr, SizeConst = 64)] StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);
        [LibraryImport("user32.dll")] public static partial uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetKeyboardState(byte[] lpKeyState);
        
        // Clipboard APIs
        [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] public static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr GetClipboardData(uint uFormat);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool IsClipboardFormatAvailable(uint format);
        #endregion

        #region Imm32
        [LibraryImport("imm32.dll")][SuppressGCTransition] public static partial IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
        [LibraryImport("imm32.dll")][SuppressGCTransition] public static partial IntPtr ImmGetContext(IntPtr hWnd);
        [LibraryImport("imm32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool ImmGetConversionStatus(IntPtr hIMC, out uint lpfdwConversion, out uint lpfdwSentence);
        [LibraryImport("imm32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        #endregion

        #region Kernel32 & Shcore
        [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)] public static partial IntPtr GetModuleHandle(string lpModuleName);
        [LibraryImport("shcore.dll")] public static partial int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        [LibraryImport("kernel32.dll")] public static partial IntPtr GlobalLock(IntPtr hMem);
        [LibraryImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GlobalUnlock(IntPtr hMem);
        #endregion

        #region Helper Methods
        public static void SimulateCapsLock()                   // Caps Lock 키 입력을 시뮬레이션하는 메서드
        {
            INPUT[] inputs = new INPUT[2];                      // Caps Lock 키 입력을 시뮬레이션하기 위해 2개의 INPUT 구조체 배열 생성
            inputs[0].type = INPUT_KEYBOARD;                    // 첫 번째 INPUT 구조체는 키다운 이벤트를 나타냄
            inputs[0].U.ki.wVk = VK_CAPITAL;                    // 가상 키 코드 VK_CAPITAL은 Caps Lock 키를 나타냄
            inputs[1].type = INPUT_KEYBOARD;                    // 두 번째 INPUT 구조체는 키업 이벤트를 나타냄
            inputs[1].U.ki.wVk = VK_CAPITAL;                    // 가상 키 코드 VK_CAPITAL은 Caps Lock 키를 나타냄
            inputs[1].U.ki.dwFlags = KEYEVENTF_KEYUP;           // 키업 이벤트를 나타내는 플래그 설정
            SendInput(2, inputs, Marshal.SizeOf<INPUT>());      // 두 개의 INPUT 구조체를 사용하여 키 입력 이벤트를 전송
        }

        public static void SendBackspace()                      // 백스페이스 키 입력을 시뮬레이션하는 메서드
        {
            INPUT[] inputs = new INPUT[2];                      // 백스페이스 키 입력을 시뮬레이션하기 위해 2개의 INPUT 구조체 배열 생성
            inputs[0].type = INPUT_KEYBOARD;                    // 첫 번째 INPUT 구조체는 키다운 이벤트를 나타냄
            inputs[0].U.ki.wVk = 0x08;                          // 가상 키 코드 0x08은 백스페이스 키를 나타냄
            inputs[1].type = INPUT_KEYBOARD;                    // 두 번째 INPUT 구조체는 키업 이벤트를 나타냄
            inputs[1].U.ki.wVk = 0x08;                          // 가상 키 코드 0x08은 백스페이스 키를 나타냄
            inputs[1].U.ki.dwFlags = KEYEVENTF_KEYUP;           // 키업 이벤트를 나타내는 플래그 설정
            SendInput(2, inputs, Marshal.SizeOf<INPUT>());      // 두 개의 INPUT 구조체를 사용하여 키 입력 이벤트를 전송
        }

        public static void SendUnicodeString(string text)       // 유니코드 문자열을 입력으로 보내는 메서드
        {
            if (string.IsNullOrEmpty(text)) return;             // 문자열이 비어있으면 아무 작업도 수행하지 않음
            INPUT[] inputs = new INPUT[text.Length * 2];        // 각 문자마다 키다운과 키업 이벤트를 생성하기 위해 2배 크기의 배열 생성
            for (int i = 0; i < text.Length; i++)
            {
                inputs[i * 2].type = INPUT_KEYBOARD;            // 키다운 이벤트
                inputs[i * 2].U.ki.wScan = text[i];             // 유니코드 문자 스캔 코드 설정
                inputs[i * 2].U.ki.dwFlags = KEYEVENTF_UNICODE; // 유니코드 키다운 이벤트 플래그 설정
                inputs[i * 2 + 1].type = INPUT_KEYBOARD;        // 키업 이벤트
                inputs[i * 2 + 1].U.ki.wScan = text[i];         // 유니코드 문자 스캔 코드 설정
                inputs[i * 2 + 1].U.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;   // 유니코드 키업 이벤트 플래그 설정
            }
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());            // 모든 입력 이벤트를 한 번에 전송
        }
        #endregion
    }
}