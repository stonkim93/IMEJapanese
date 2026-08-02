// NativeMethods.cs
#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace IMEJapanese
{
    // =======================================================================================
    // [수정: 전역에서 사용되는 Win32 P/Invoke API 선언부 통합 관리]
    // IMEPointer 앱에서 넘어온 불필요한 마우스 포인터 및 커서 변경 관련 API를 모두 제거하고 최적화했습니다.
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
        #endregion

        #region Structs
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO { public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public int rectLeft, rectTop, rectRight, rectBottom; }
        [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFO { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
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
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial IntPtr GetForegroundWindow();
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial IntPtr GetKeyboardLayout(uint idThread);
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial short GetKeyState(int keyCode);
        [LibraryImport("user32.dll")][SuppressGCTransition][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetCursorPos(out POINT lpPoint);
        [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] public static partial IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
        [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)] public static partial int GetClassName(IntPtr hWnd, char* lpClassName, int nMaxCount);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetForegroundWindow(IntPtr hWnd);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
        [LibraryImport("user32.dll")] public static partial int GetSystemMetrics(int nIndex);
        [LibraryImport("user32.dll", EntryPoint = "GetDC")] public static partial IntPtr GetDC(IntPtr hWnd);
        [LibraryImport("user32.dll", EntryPoint = "ReleaseDC")] public static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);
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

        #region Gdi32
        [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")] public static partial IntPtr CreateCompatibleDC(IntPtr hdc);
        [LibraryImport("gdi32.dll", EntryPoint = "DeleteDC")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteDC(IntPtr hdc);
        [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection")] public static partial IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);
        [LibraryImport("gdi32.dll", EntryPoint = "SelectObject")] public static partial IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteObject(IntPtr hObject);
        #endregion

        #region Imm32
        [LibraryImport("imm32.dll")][SuppressGCTransition] public static partial IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
        [LibraryImport("imm32.dll")][SuppressGCTransition] public static partial IntPtr ImmGetContext(IntPtr hWnd);
        [LibraryImport("imm32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool ImmGetConversionStatus(IntPtr hIMC, out uint lpfdwConversion, out uint lpfdwSentence);
        [LibraryImport("imm32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        #endregion
    }
}