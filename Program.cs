// Program.cs - IMEJapanese
#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]

namespace IMEJapanese
{
    #region [ 사용자 설정 영역 (AppConfig) ]
    internal static class AppConfig
    {
        public const int PollingInterval = 100;
        public const int OverlayDefaultDurationMs = 1500;
        public const float OverlayDefaultFontSize = 29f;   
        public const int OverlayDefaultHeight = 52;
        public const int OverlayDefaultCharWidth = 30;
        public const int OverlayDefaultPaddingWidth = 24;
        public const int OverlayDefaultYOffset = 40;
        public const int TrayIconSize = 32;
        public const float TrayLowercaseFontSize = 31F;
        public const float TrayUppercaseFontSize = 32F;

        // [최적화] 불필요한 ShowCapsHangul 변수 제거
#if ENABLE_CAPS_Japanese1
        public static bool ShowCapsJapanese1 = true;            
#else
        public static bool ShowCapsJapanese1 = false;
#endif

#if ENABLE_CAPS_Japanese2
        public static bool ShowCapsJapanese2 = true;            
#else
        public static bool ShowCapsJapanese2 = false;
#endif

#if ENABLE_CAPS_Japanese3
        public static bool ShowCapsJapanese3 = true;            
#else
        public static bool ShowCapsJapanese3 = false;
#endif

#if ENABLE_KEYBOARD_LAYOUT
        public static bool ShowKeyboardlayoutMenu = true;           
#else
        public static bool ShowKeyboardlayoutMenu = false;
#endif

        public static bool ShowTextOverlayMenu = true;          
        public static bool ShowCopilotMapMenu = true;  

        public static int DefaultCapsMode = 1; // 1 = Japanese1
        public static bool DefaultShowKeyboardLayout = true;    
        public static bool DefaultShowTextOverlay = true;       
        public static bool DefaultEnableCopilotMap = false;     
        public static bool EnableCopilotMap = DefaultEnableCopilotMap;       

        public struct Theme
        {
            public Color TrayBgColor;    
            public Color TrayTextColor;  
            public string TrayText;      
            public string Description;   
        }

        public static readonly Dictionary<ImeState.State, Theme> Themes = new()
        {
            [ImeState.State.EnglishLower] = new Theme { TrayBgColor = Color.Black, TrayTextColor = Color.White, TrayText = "e", Description = "영어 소문자 [e]" },
            [ImeState.State.EnglishUpper] = new Theme { TrayBgColor = Color.Black, TrayTextColor = Color.DeepSkyBlue, TrayText = "E", Description = "영어 대문자 [E]" },
            [ImeState.State.Hangul] = new Theme { TrayBgColor = Color.Red, TrayTextColor = Color.White, TrayText = "K", Description = "한글 (Caps Off) [K]" },
            [ImeState.State.JapaneseIME] = new Theme { TrayBgColor = Color.Black, TrayTextColor = Color.Lime, TrayText = "j", Description = "Japanese IME [j]" },
            [ImeState.State.JapaneseHangul1] = new Theme { TrayBgColor = Color.Black, TrayTextColor = Color.Lime, TrayText = "J", Description = "일본어1_조합형 [J]" },
            [ImeState.State.JapaneseHangul2] = new Theme { TrayBgColor = Color.Black, TrayTextColor = Color.Lime, TrayText = "J", Description = "일본어2_조합형 [J]" },
            [ImeState.State.JapaneseHangul3] = new Theme { TrayBgColor = Color.Black, TrayTextColor = Color.Lime, TrayText = "J", Description = "일본어3_3Layer [J]" }
        };
    }
    #endregion

    #region [ 문자열 리소스 (UiText) ]
    internal static class UiText
    {
        public const string AppName = "IMEJapanese";
        public const string AlreadyRunningMessage = "이미 실행 중입니다.";
        public const string FatalErrorPrefix = "치명적 오류:\n";
        public const string StatusChecking = "현재 상태: 확인 중...";
        public static string HangulCapsMode => MainForm.Instance?.GetCapsModeOverlayText() ?? "일본어 입력모드";        
        public const string ExitMenu = "종료(Exit)";
        public const string GithubUrl = "https://github.com/stonkim93/IMEJapanese";

        public static string TrayTooltip(string description) => $"{AppName}: {description}";
        public static string StatusLabel(string description) => $"현재 상태: {description}";
    }
    #endregion

    #region [ 진입점 (Main) ]
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            using Mutex mutex = new Mutex(true, "IMEJapanese_SingleInstance", out bool first);
            if (!first)
            {
                MessageBox.Show(UiText.AlreadyRunningMessage, UiText.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{UiText.FatalErrorPrefix}{ex.Message}", UiText.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    #endregion

    #region [ 자판 배열창 폼 ]
    public class KeyboardLayoutForm : Form
    {
        private readonly PictureBox _pbLayoutImage;
        public event EventHandler? OnLayoutDoubleClicked;
        public event EventHandler? OnClosedByUser;
        private string _currentImageName = "";
        private Size _currentImageSize = new Size(600, 200);

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x00020000;   
                cp.Style |= 0x00080000;   
                cp.ExStyle |= 0x00040000; 
                cp.ExStyle |= 0x08000000; 
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        public KeyboardLayoutForm()
        {
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.ShowInTaskbar = true;
            this.TopMost = true; 
            this.Text = "IMEJapanese 자판 배열창";
            
            int screenWidth = Screen.PrimaryScreen?.WorkingArea.Width ?? 800;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(Math.Max(0, (screenWidth - this.Width) / 2), 50);

            try 
            { 
                var assembly = typeof(Program).Assembly;
                using Stream? stream = assembly.GetManifestResourceStream("IMEJapanese.images.IMEJapanese.ico");
                if (stream != null) this.Icon = new Icon(stream);
            } 
            catch { }

            _pbLayoutImage = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White
            };

            _pbLayoutImage.DoubleClick += (s, e) => OnLayoutDoubleClicked?.Invoke(this, EventArgs.Empty);
            this.Controls.Add(_pbLayoutImage);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.WindowState == FormWindowState.Normal)
            {
                if (this.ClientSize != _currentImageSize && _currentImageSize.Width > 0 && _currentImageSize.Height > 0)
                {
                    this.ClientSize = _currentImageSize;
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                OnClosedByUser?.Invoke(this, EventArgs.Empty);
            }
            base.OnFormClosing(e);
        }

        public void UpdateImage(string imageName)
        {
            if (_currentImageName == imageName) return;
            _currentImageName = imageName;
            this.Text = imageName;

            try
            {
                var assembly = typeof(Program).Assembly;
                string resourceName = $"IMEJapanese.images.{imageName}";
                using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                
                Image? oldImg = _pbLayoutImage.Image;
                if (stream != null)
                {
                    Image newImg = Image.FromStream(stream);
                    _pbLayoutImage.Image = newImg;
                    _currentImageSize = newImg.Size;
                    if (this.WindowState == FormWindowState.Normal) this.ClientSize = _currentImageSize;
                }
                else _pbLayoutImage.Image = null;
                
                oldImg?.Dispose();
            }
            catch
            {
                Image? oldImg = _pbLayoutImage.Image;
                _pbLayoutImage.Image = null;
                oldImg?.Dispose();
            }
        }
    }
    #endregion

    #region [ 오버레이 표시 폼 (TextOverlayForm) ]
    public class TextOverlayForm : Form
    {
        private readonly System.Windows.Forms.Timer _hideTimer;
        private string _displayText = "";
        private float _displayFontSize = 22f;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; 
                cp.ExStyle |= 0x00000080; 
                cp.ExStyle |= 0x00000008; 
                return cp;
            }
        }
        protected override bool ShowWithoutActivation => true;

        public TextOverlayForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.Black;
            this.ForeColor = Color.White;
            this.TopMost = true;
            this.ShowInTaskbar = false;

            _hideTimer = new System.Windows.Forms.Timer { Interval = AppConfig.OverlayDefaultDurationMs };
            _hideTimer.Tick += (s, e) => this.Hide();
            
            this.Paint += RenderOverlayText;
        }

        public void ShowOverlay(string text, bool useTimer, float fontSize, int width, int height, int x, int y)
        {
            _displayText = text;
            _displayFontSize = fontSize;
            
            this.Size = new Size(width, height);
            this.Location = new Point(x, y);
            
            if (useTimer) { _hideTimer.Stop(); _hideTimer.Start(); }
            else _hideTimer.Stop();
            
            if (!this.Visible) this.Show(); 
            this.Invalidate();
        }

        private void RenderOverlayText(object? sender, PaintEventArgs e)
        {
            using Font f = new Font("Malgun Gothic", _displayFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(e.Graphics, _displayText, f, this.ClientRectangle, Color.White, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        
        public void Clear()
        {
            _hideTimer.Stop();
            this.Hide();
        }
    }
    #endregion

    #region [ 레지스트리 키맵핑 도구 (RegistryManager) ]
    internal static class RegistryManager
    {
        private const string RegPath = @"SYSTEM\CurrentControlSet\Control\Keyboard Layout";
        private const string RegValue = "Scancode Map";
        private static readonly byte[] MappingBytes = { 0x71, 0xE0, 0x6E, 0x00 };

        public static bool IsAdmin()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        public static bool IsMappingApplied()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(RegPath, false);
                if (key?.GetValue(RegValue) is byte[] data && data.Length >= 20)
                {
                    int count = BitConverter.ToInt32(data, 8);
                    for (int i = 0; i < count - 1; i++)
                    {
                        int offset = 12 + (i * 4);
                        if (offset + 4 <= data.Length)
                        {
                            if (data[offset] == MappingBytes[0] && data[offset + 1] == MappingBytes[1] &&
                                data[offset + 2] == MappingBytes[2] && data[offset + 3] == MappingBytes[3])
                                return true;
                        }
                    }
                }
                return false;
            }
            catch { return false; }
        }

        public static bool ToggleMapping(bool apply)
        {
            if (!IsAdmin())
            {
                MessageBox.Show("레지스트리 수정을 위해 앱을 '관리자 권한'으로 실행해주세요.", "권한 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(RegPath, true);
                if (key == null) return false;
                byte[]? currentData = key.GetValue(RegValue) as byte[];

                if (apply)
                {
                    if (IsMappingApplied()) return true;

                    byte[] newData;
                    if (currentData == null || currentData.Length < 20)
                    {
                        newData = new byte[20];
                        Array.Clear(newData, 0, 8);
                        BitConverter.GetBytes(2).CopyTo(newData, 8);
                        MappingBytes.CopyTo(newData, 12);
                    }
                    else
                    {
                        int oldCount = BitConverter.ToInt32(currentData, 8);
                        newData = new byte[currentData.Length + 4];
                        Array.Copy(currentData, 0, newData, 0, 8);
                        BitConverter.GetBytes(oldCount + 1).CopyTo(newData, 8);
                        Array.Copy(currentData, 12, newData, 12, currentData.Length - 16);
                        MappingBytes.CopyTo(newData, currentData.Length - 4);
                    }
                    key.SetValue(RegValue, newData, RegistryValueKind.Binary);
                }
                else
                {
                    if (!IsMappingApplied() || currentData == null) return true;

                    int oldCount = BitConverter.ToInt32(currentData, 8);
                    if (oldCount <= 2) key.DeleteValue(RegValue, false);
                    else
                    {
                        byte[] newData = new byte[currentData.Length - 4];
                        Array.Copy(currentData, 0, newData, 0, 8);
                        BitConverter.GetBytes(oldCount - 1).CopyTo(newData, 8);

                        int destOffset = 12;
                        for (int i = 0; i < oldCount - 1; i++)
                        {
                            int srcOffset = 12 + (i * 4);
                            if (!(currentData[srcOffset] == MappingBytes[0] && currentData[srcOffset + 1] == MappingBytes[1] &&
                                  currentData[srcOffset + 2] == MappingBytes[2] && currentData[srcOffset + 3] == MappingBytes[3]))
                            {
                                Array.Copy(currentData, srcOffset, newData, destOffset, 4);
                                destOffset += 4;
                            }
                        }
                        key.SetValue(RegValue, newData, RegistryValueKind.Binary);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"레지스트리 수정 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }
    #endregion

    #region [ 메인 폼 (MainForm) 및 트레이 제어 ]
    internal class MainForm : Form
    {
        public static MainForm? Instance { get; private set; }
        public static IntPtr LastValidHwnd { get; private set; } = IntPtr.Zero;
        public static IntPtr LastValidFocusHwnd { get; private set; } = IntPtr.Zero;

        private const int HiddenFormSize = 16;
        private const int HiddenFormLocation = -100;
        private const int WindowPosChangedMessage = 0x001A;
        private const int TrayContextMenuForegroundDelayRetryMs = 60;
        private const int RebuildRetryAfterWindowPosChangedMs = 800;
        private const int RebuildRetryAfterScaleChangeMs = 1500;
        private const int DisplaySettingsChangedDelayMs = 400;
        private const int UserPreferenceChangedDelayMs = 600;

        private static readonly RectangleF TrayIconTextRectLower = new RectangleF(-2.0f, -5.0f, 36f, 36f);
        private static readonly RectangleF TrayIconTextRectUpper = new RectangleF(-2.0f, -3.5f, 36f, 36f);

        private readonly Dictionary<ImeState.State, StateAssets> _assetCache = new();
        private readonly System.Windows.Forms.Timer _stateCheckTimer;
        private readonly NotifyIcon _sysTrayIcon;
        private readonly ContextMenuStrip _trayContextMenu;
        private readonly ToolStripMenuItem _menuItemStatus;
        private bool _isTextOverlayEnabled = AppConfig.DefaultShowTextOverlay; 

        // [최적화] 불필요한 기본 한글 모드 제거
        internal enum CapsMode { Japanese1 = 1, Japanese2 = 2, Japanese3 = 3 }

        private CapsMode _activeCapsMode = (CapsMode)AppConfig.DefaultCapsMode;
        private bool _isKeyboardLayoutOverlayEnabled = AppConfig.DefaultShowKeyboardLayout;

        private ToolStripMenuItem _menuItemCapsJapanese1 = null!;
        private ToolStripMenuItem _menuItemCapsJapanese2 = null!;
        private ToolStripMenuItem _menuItemCapsJapanese3 = null!;
        private ToolStripMenuItem _menuItemToggleKeyboardLayout = null!;
        private ToolStripMenuItem _menuItemToggleTextOverlay = null!; 
        private ToolStripMenuItem _menuItemToggleCopilotMap = null!;

        private bool _isShiftVisualInverted = false; 
        private bool _lastHangulSyncState = false;
        private KeyboardLayoutForm? _frmKeyboardLayout;
        private TextOverlayForm? _frmTextOverlay; 
        private Point _lastKeyboardLayoutLocation = Point.Empty;

        private ImeState.State _previousImeState = (ImeState.State)(-1);
        private IntPtr _lastForegroundHwnd = IntPtr.Zero;
        private IntPtr _currentContextHwnd = IntPtr.Zero;
        private IntPtr _lastPolledHwnd = IntPtr.Zero; 

        private float _currentDpiScale = 1.0f;
        private static readonly uint s_currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        private readonly struct ActiveInputModeContext
        {
            public readonly bool IsJapanese1ModeActive;
            public readonly bool IsJapanese2ModeActive;
            public readonly bool IsJapanese3ModeActive;
            public readonly IKeyProcessor? ActiveProcessor;

            public ActiveInputModeContext(bool j1, bool j2, bool j3, IKeyProcessor? proc)
            {
                IsJapanese1ModeActive = j1; IsJapanese2ModeActive = j2; IsJapanese3ModeActive = j3; ActiveProcessor = proc;
            }
        }

        private readonly struct CapsModeStateMapping
        {
            public readonly CapsMode Mode;
            public readonly ImeState.State ActiveState;
            public readonly IKeyProcessor Processor;

            public CapsModeStateMapping(CapsMode m, ImeState.State s, IKeyProcessor p)
            {
                Mode = m; ActiveState = s; Processor = p;
            }
        }

        private class StateAssets : IDisposable
        {
            public Icon? TrayIcon;
            public string Description = "";

            public void Dispose() => TrayIcon?.Dispose();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 | 0x00000020 | 0x00080000 | 0x08000000 | 0x00000008;
                return cp;
            }
        }

        public MainForm()
        {
            Instance = this;
            this.Size = new Size(HiddenFormSize, HiddenFormSize);
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(HiddenFormLocation, HiddenFormLocation);

            _trayContextMenu = new ContextMenuStrip();
            _menuItemStatus = new ToolStripMenuItem(UiText.StatusChecking) { Enabled = false };

            BuildTrayMenu();

            _sysTrayIcon = new NotifyIcon { Text = UiText.AppName, ContextMenuStrip = _trayContextMenu, Visible = true };
            _sysTrayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) { NativeMethods.SetForegroundWindow(this.Handle); _trayContextMenu.Show(Cursor.Position); }
            };

            GlobalInputHook.Install();

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            RebuildStateAssets();

            _stateCheckTimer = new System.Windows.Forms.Timer { Interval = AppConfig.PollingInterval };
            _stateCheckTimer.Tick += ProcessStateCheck;
        }

        private void BuildTrayMenu()
        {
            var titleMenuItem = new ToolStripMenuItem(UiText.AppName, null, (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = UiText.GithubUrl, UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show($"웹페이지를 열 수 없습니다.\n{ex.Message}", UiText.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            });
            titleMenuItem.Font = new Font(titleMenuItem.Font, FontStyle.Bold); 
            _trayContextMenu.Items.Add(titleMenuItem);
            _trayContextMenu.Items.Add(_menuItemStatus);
            _trayContextMenu.Items.Add(new ToolStripSeparator());

            // [최적화] 순수 일본어 전용 입력을 위한 간결한 메뉴 구성
            _menuItemCapsJapanese1 = AddMenuToggle("일본어1_조합형_대표자음", AppConfig.ShowCapsJapanese1, (s, e) => UpdateCapsMode(CapsMode.Japanese1));
            _menuItemCapsJapanese2 = AddMenuToggle("일본어2_조합형_최빈자음", AppConfig.ShowCapsJapanese2, (s, e) => UpdateCapsMode(CapsMode.Japanese2));
            _menuItemCapsJapanese3 = AddMenuToggle("일본어3_3Layer", AppConfig.ShowCapsJapanese3, (s, e) => UpdateCapsMode(CapsMode.Japanese3));
            AddMenuSeparatorIf(AppConfig.ShowCapsJapanese1 || AppConfig.ShowCapsJapanese2 || AppConfig.ShowCapsJapanese3);

            _menuItemToggleKeyboardLayout = AddMenuToggle("일본어 키보드 배열창", AppConfig.ShowKeyboardlayoutMenu, (s, e) =>
            {
                _isKeyboardLayoutOverlayEnabled = _menuItemToggleKeyboardLayout.Checked;
                if (!_isKeyboardLayoutOverlayEnabled) CloseAllLayoutForms();
                else RefreshKeyboardLayoutOverlay();
            });
            _menuItemToggleKeyboardLayout.CheckOnClick = true;
            _menuItemToggleKeyboardLayout.Checked = _isKeyboardLayoutOverlayEnabled;

            _menuItemToggleTextOverlay = AddMenuToggle("일본어 입력문자 표시창", AppConfig.ShowTextOverlayMenu, (s, e) =>
            {
                _isTextOverlayEnabled = _menuItemToggleTextOverlay.Checked;
                if (!_isTextOverlayEnabled) _frmTextOverlay?.Clear();
            });
            _menuItemToggleTextOverlay.CheckOnClick = true;
            _menuItemToggleTextOverlay.Checked = _isTextOverlayEnabled;

            _menuItemToggleCopilotMap = AddMenuToggle("한자키 적용/복원 키맵핑", AppConfig.ShowCopilotMapMenu, (s, e) =>
            {
                bool isApplied = RegistryManager.IsMappingApplied();
                bool apply = !isApplied;
                string actionName = apply ? "적용" : "복원";

                if (MessageBox.Show($"Copilot 키를 한자키로 {actionName}하시겠습니까?\n(관리자 권한 및 재부팅 필요)", "키맵핑 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    if (RegistryManager.ToggleMapping(apply))
                    {
                        _menuItemToggleCopilotMap.Checked = apply;
                        AppConfig.EnableCopilotMap = apply;
                        MessageBox.Show($"키맵핑 {actionName} 완료.\n재부팅(Reboot)해 주시기 바랍니다.", "재부팅 필요", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else _menuItemToggleCopilotMap.Checked = isApplied;
                }
                else _menuItemToggleCopilotMap.Checked = isApplied;
            });
            _menuItemToggleCopilotMap.CheckOnClick = false; 
            _menuItemToggleCopilotMap.Checked = RegistryManager.IsMappingApplied(); 
            AppConfig.EnableCopilotMap = RegistryManager.IsMappingApplied(); 

            AddMenuSeparatorIf(AppConfig.ShowKeyboardlayoutMenu || AppConfig.ShowTextOverlayMenu || AppConfig.ShowCopilotMapMenu);
            _trayContextMenu.Items.Add(new ToolStripMenuItem(UiText.ExitMenu, null, (s, e) => this.Close()));

            SyncCapsMenuChecks();
        }

        private ToolStripMenuItem AddMenuToggle(string text, bool show, EventHandler onClick)
        {
            var item = new ToolStripMenuItem(text, null, onClick);
            if (show) _trayContextMenu.Items.Add(item);
            return item;
        }

        private void AddMenuSeparatorIf(bool condition) { if (condition) _trayContextMenu.Items.Add(new ToolStripSeparator()); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WindowPosChangedMessage) Task.Delay(200).ContinueWith(_ => this.BeginInvoke(new Action(() => RebuildAssetsWithRetry(RebuildRetryAfterWindowPosChangedMs))));
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _currentContextHwnd = NativeMethods.GetForegroundWindow();
            _lastPolledHwnd = _currentContextHwnd; 
            _lastForegroundHwnd = _currentContextHwnd;
            _lastHangulSyncState = ImeState.IsHangulModeSystemWide(_currentContextHwnd);
            
            _frmTextOverlay = new TextOverlayForm();
            
            if (_currentContextHwnd != IntPtr.Zero && !IsTaskbarWindow(_currentContextHwnd) && !IsAppOrTrayWindow(_currentContextHwnd))
            {
                LastValidHwnd = _currentContextHwnd;
                LastValidFocusHwnd = SearchFocusedInputHwnd(_currentContextHwnd);
            }

            ApplyVisualState(ImeState.Detect(_currentContextHwnd, _activeCapsMode == CapsMode.Japanese1, _activeCapsMode == CapsMode.Japanese2, _activeCapsMode == CapsMode.Japanese3));
            _stateCheckTimer.Start();
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(() => OnDisplaySettingsChanged(sender, e))); return; }
            Task.Delay(DisplaySettingsChangedDelayMs).ContinueWith(_ => this.BeginInvoke(new Action(() => RebuildAssetsWithRetry(RebuildRetryAfterScaleChangeMs))));
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.Accessibility || e.Category == UserPreferenceCategory.Mouse)
            {
                if (this.InvokeRequired) { this.BeginInvoke(new Action(() => OnUserPreferenceChanged(sender, e))); return; }
                Task.Delay(UserPreferenceChangedDelayMs).ContinueWith(_ => this.BeginInvoke(new Action(() => RebuildAssetsWithRetry(RebuildRetryAfterScaleChangeMs))));
            }
        }

        public void RequestLayoutRefresh() => this.BeginInvoke(new Action(RefreshKeyboardLayoutOverlay));

        private void UpdateCapsMode(CapsMode mode)
        {
            _activeCapsMode = mode;
            SyncCapsMenuChecks();
            _previousImeState = (ImeState.State)(-1);
            RefreshKeyboardLayoutOverlay();

            IntPtr activeHwnd = NativeMethods.GetForegroundWindow();
            if (activeHwnd != IntPtr.Zero && (IsTaskbarWindow(activeHwnd) || IsAppOrTrayWindow(activeHwnd)))
                EnforceCapsModeToTarget(activeHwnd, 1);

            IntPtr targetHwnd = LastValidFocusHwnd != IntPtr.Zero ? LastValidFocusHwnd : (LastValidHwnd != IntPtr.Zero ? LastValidHwnd : activeHwnd);
            if (targetHwnd != IntPtr.Zero)
            {
                if (!IsTaskbarWindow(targetHwnd) && !IsAppOrTrayWindow(targetHwnd)) NativeMethods.SetForegroundWindow(targetHwnd);
                EnforceCapsModeToTarget(targetHwnd);
            }
        }

        public string GetCapsModeOverlayText()
        {
            return _activeCapsMode switch
            {
                CapsMode.Japanese1 => "일본어1_조합형",
                CapsMode.Japanese2 => "일본어2_조합형",
                CapsMode.Japanese3 => "일본어3_3Layer",
                _ => "일본어 모드"
            };
        }

        private void SyncCapsMenuChecks()
        {
            if (_menuItemCapsJapanese1 != null) _menuItemCapsJapanese1.Checked = (_activeCapsMode == CapsMode.Japanese1);
            if (_menuItemCapsJapanese2 != null) _menuItemCapsJapanese2.Checked = (_activeCapsMode == CapsMode.Japanese2);
            if (_menuItemCapsJapanese3 != null) _menuItemCapsJapanese3.Checked = (_activeCapsMode == CapsMode.Japanese3);
        }

        private void ApplyCapsModeBase(IntPtr targetHwnd)
        {
            if (targetHwnd == IntPtr.Zero) return;
            ImeState.SetHangulState(targetHwnd, true);
            bool capsOn = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 0x0001) != 0;
            if (!capsOn) NativeMethods.SimulateCapsLock();
        }

        private void EnforceCapsModeToTarget(IntPtr targetHwnd, int retryCount = 2)
        {
            if (targetHwnd == IntPtr.Zero) return;
            ApplyCapsModeBase(targetHwnd);
            if (ImeState.IsHangulModeSystemWide(targetHwnd) || retryCount <= 0) return;

            IntPtr rootHwnd = LastValidHwnd != IntPtr.Zero ? LastValidHwnd : targetHwnd;

            Task.Delay(TrayContextMenuForegroundDelayRetryMs).ContinueWith(_ =>
                this.BeginInvoke(new Action(() =>
                {
                    IntPtr retryTarget = SearchFocusedInputHwnd(rootHwnd);
                    if (retryTarget == IntPtr.Zero) retryTarget = rootHwnd;

                    if (retryTarget != IntPtr.Zero && !IsTaskbarWindow(retryTarget) && !IsAppOrTrayWindow(retryTarget)) NativeMethods.SetForegroundWindow(retryTarget);
                    EnforceCapsModeToTarget(retryTarget, retryCount - 1);
                })));
        }

        private void ProcessStateCheck(object? sender, EventArgs e)
        {
            IntPtr actualHFore = NativeMethods.GetForegroundWindow();
            bool isFocusChanged = (actualHFore != _lastPolledHwnd);
            
            bool isTaskbar = IsTaskbarWindow(actualHFore);
            bool isTrayOrApp = IsAppOrTrayWindow(actualHFore);
            bool isLayoutForm = IsLayoutFormForeground(actualHFore);

            CacheLastValidWindows(actualHFore, isTaskbar, isTrayOrApp, isLayoutForm);
            SyncSystemHangulState(actualHFore, isTaskbar, isTrayOrApp, isLayoutForm, isFocusChanged);

            _lastPolledHwnd = actualHFore;

            IntPtr contextHwnd = ResolveContextHwnd(actualHFore);
            bool cachedIsHangulMode = ImeState.IsHangulModeSystemWide(contextHwnd);
            ushort contextLangId = ResolveLanguageId(contextHwnd);

            TrackCurrentWindow(contextHwnd, isTaskbar, isTrayOrApp, isLayoutForm);

            ImeState.State currentState = ImeState.Detect(contextHwnd, _activeCapsMode == CapsMode.Japanese1, _activeCapsMode == CapsMode.Japanese2, _activeCapsMode == CapsMode.Japanese3);

            ActiveInputModeContext activeInputMode = ResolveInputModeContext(currentState);

            GlobalInputHook.UpdateContext(new GlobalInputHook.HookContextSnapshot(
                contextHwnd, contextLangId, cachedIsHangulMode, activeInputMode.ActiveProcessor,
                activeInputMode.IsJapanese1ModeActive, activeInputMode.IsJapanese2ModeActive, activeInputMode.IsJapanese3ModeActive));

            if (currentState != _previousImeState)
            {
                _previousImeState = currentState;
                ApplyVisualState(currentState);
            }

            RefreshKeyboardLayoutOverlay();
        }

        private bool IsLayoutFormForeground(IntPtr actualHFore) => _frmKeyboardLayout != null && actualHFore == _frmKeyboardLayout.Handle;

        private void CacheLastValidWindows(IntPtr actualHFore, bool isTaskbar, bool isTrayOrApp, bool isLayoutForm)
        {
            if (!isTaskbar && !isTrayOrApp && !isLayoutForm && actualHFore != IntPtr.Zero && actualHFore != this.Handle)
            {
                LastValidHwnd = actualHFore;
                LastValidFocusHwnd = SearchFocusedInputHwnd(actualHFore);
            }
        }

        private void SyncSystemHangulState(IntPtr actualHFore, bool isTaskbar, bool isTrayOrApp, bool isLayoutForm, bool isFocusChanged)
        {
            bool isCurrentHangul = ImeState.IsHangulModeSystemWide(actualHFore);

            if (isFocusChanged)
            {
                if (LastValidHwnd != IntPtr.Zero)
                {
                    bool isValidHangul = ImeState.IsHangulModeSystemWide(LastValidHwnd);
                    if ((isTaskbar || isTrayOrApp || isLayoutForm) && isValidHangul != isCurrentHangul)
                    {
                        ImeState.SetHangulState(actualHFore, isValidHangul);
                        isCurrentHangul = ImeState.IsHangulModeSystemWide(actualHFore);
                    }
                }
                _lastHangulSyncState = isCurrentHangul;
            }
            else if (isCurrentHangul != _lastHangulSyncState)
            {
                _lastHangulSyncState = isCurrentHangul;

                Action<IntPtr> SetState = (hwnd) => { if (hwnd != IntPtr.Zero && hwnd != actualHFore) ImeState.SetHangulState(hwnd, isCurrentHangul); };
                SetState(LastValidHwnd);
                SetState(_frmKeyboardLayout?.Handle ?? IntPtr.Zero);
                SetState(this.Handle);
            }
        }

        private IntPtr ResolveContextHwnd(IntPtr actualHFore) => (LastValidHwnd != IntPtr.Zero) ? LastValidHwnd : actualHFore;

        private static ushort ResolveLanguageId(IntPtr contextHwnd)
        {
            if (contextHwnd == IntPtr.Zero) return 0;
            uint tid = NativeMethods.GetWindowThreadProcessId(contextHwnd, out _);
            return (ushort)(NativeMethods.GetKeyboardLayout(tid).ToInt64() & 0xFFFF);
        }

        private void TrackCurrentWindow(IntPtr contextHwnd, bool isTaskbar, bool isTrayOrApp, bool isLayoutForm)
        {
            if (contextHwnd != _currentContextHwnd)
            {
                if (!isTaskbar && !isTrayOrApp && !isLayoutForm) _lastForegroundHwnd = contextHwnd;
                _currentContextHwnd = contextHwnd;
            }
        }

        private ActiveInputModeContext ResolveInputModeContext(ImeState.State state)
        {
            CapsModeStateMapping[] maps = {
                new(CapsMode.Japanese1, ImeState.State.JapaneseHangul1, KeyProcessorFactory.Japanese1),
                new(CapsMode.Japanese2, ImeState.State.JapaneseHangul2, KeyProcessorFactory.Japanese2),
                new(CapsMode.Japanese3, ImeState.State.JapaneseHangul3, KeyProcessorFactory.Japanese3)
            };
            foreach (var map in maps)
                if (_activeCapsMode == map.Mode && state == map.ActiveState) return new ActiveInputModeContext(map.Mode == CapsMode.Japanese1, map.Mode == CapsMode.Japanese2, map.Mode == CapsMode.Japanese3, map.Processor);
            return new ActiveInputModeContext(false, false, false, null);
        }

        public void ShowOverlay(string text, int durationMs = AppConfig.OverlayDefaultDurationMs)
        {
            if (!_isTextOverlayEnabled) return;

            float scaledFontSize = AppConfig.OverlayDefaultFontSize * _currentDpiScale;
            int scaledHeight = (int)Math.Round(AppConfig.OverlayDefaultHeight * _currentDpiScale);
            int scaledCharWidth = (int)Math.Round(AppConfig.OverlayDefaultCharWidth * _currentDpiScale);
            int scaledPadWidth = (int)Math.Round(AppConfig.OverlayDefaultPaddingWidth * _currentDpiScale);
            int scaledYOffset = (int)Math.Round(AppConfig.OverlayDefaultYOffset * _currentDpiScale);

            if (this.InvokeRequired) this.BeginInvoke(new Action(() => ExecuteShowOverlay(text, durationMs > 0, scaledFontSize, scaledHeight, scaledCharWidth, scaledPadWidth, scaledYOffset)));
            else ExecuteShowOverlay(text, durationMs > 0, scaledFontSize, scaledHeight, scaledCharWidth, scaledPadWidth, scaledYOffset);
        }

        public void ClearOverlay() => _frmTextOverlay?.Clear();

        private void ExecuteShowOverlay(string ch, bool useTimer, float fontSize, int formH, int charW, int padW, int yOffset)
        {
            int length = 0; foreach (char c in ch) length += (c >= 0x1100 && c <= 0xD7AF) ? 2 : 1; 
            int minWidth = (int)Math.Round(40 * _currentDpiScale);
            Size sz = new Size(Math.Max(length * (charW / 2) + padW, minWidth), formH);

            Point pt = ResolveCaretPosition();
            _frmTextOverlay?.ShowOverlay(ch, useTimer, fontSize, sz.Width, sz.Height, pt.X, pt.Y + yOffset);
        }

        private static Point ResolveCaretPosition()
        {
            IntPtr hFore = NativeMethods.GetForegroundWindow();
            uint tid = NativeMethods.GetWindowThreadProcessId(hFore, out _);
            NativeMethods.GUITHREADINFO gti = new() { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            if (NativeMethods.GetGUIThreadInfo(tid, ref gti) && gti.hwndCaret != IntPtr.Zero)
            {
                NativeMethods.POINT pt = new() { X = gti.rectLeft, Y = gti.rectBottom };
                NativeMethods.ClientToScreen(gti.hwndCaret, ref pt);
                return new Point(pt.X, pt.Y);
            }
            if (NativeMethods.GetCursorPos(out NativeMethods.POINT mPt)) return new Point(mPt.X, mPt.Y);
            return Point.Empty;
        }

        private void RebuildAssetsWithRetry(int retryDelayMs)
        {
            _stateCheckTimer.Stop(); RebuildStateAssets(); _stateCheckTimer.Start();
            if (retryDelayMs > 0)
            {
                Task.Delay(retryDelayMs).ContinueWith(_ => this.BeginInvoke(new Action(() => { _stateCheckTimer.Stop(); RebuildStateAssets(); _stateCheckTimer.Start(); })));
            }
        }

        private void RebuildStateAssets()
        {
            bool trayWasVisible = false;
            try { trayWasVisible = _sysTrayIcon?.Visible ?? false; } catch { }

            foreach (var asset in _assetCache.Values) try { asset.Dispose(); } catch { }
            _assetCache.Clear();

            float dpi = 96f;
            IntPtr hFore = NativeMethods.GetForegroundWindow();
            if (hFore != IntPtr.Zero)
            {
                IntPtr hMonitor = NativeMethods.MonitorFromWindow(hFore, NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (hMonitor != IntPtr.Zero && NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0) dpi = dpiX;
            }
            else { uint sysDpi = NativeMethods.GetDpiForSystem(); if (sysDpi > 0) dpi = sysDpi; }

            _currentDpiScale = dpi / 96f;

            foreach (ImeState.State state in Enum.GetValues(typeof(ImeState.State)))
            {
                if (!AppConfig.Themes.TryGetValue(state, out AppConfig.Theme t)) continue;
                try
                {
                    _assetCache[state] = new StateAssets { Description = t.Description, TrayIcon = BuildTrayIcon(t.TrayText, t.TrayBgColor, t.TrayTextColor) };
                }
                catch { }
            }

            try
            {
                if (trayWasVisible && _sysTrayIcon != null)
                {
                    _sysTrayIcon.Visible = true;
                    ImeState.State st = _previousImeState == (ImeState.State)(-1) ? ImeState.State.EnglishLower : _previousImeState;
                    if (_assetCache.TryGetValue(st, out var ast) && ast.TrayIcon != null) _sysTrayIcon.Icon = ast.TrayIcon;
                }
            } catch { }
        }

        private static Icon BuildTrayIcon(string text, Color bg, Color fg)
        {
            using Bitmap bmp = new(AppConfig.TrayIconSize, AppConfig.TrayIconSize);
            using Graphics g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using SolidBrush bgBrush = new(bg); g.FillRectangle(bgBrush, 0, 0, AppConfig.TrayIconSize, AppConfig.TrayIconSize);
            
            bool lower = !string.IsNullOrEmpty(text) && char.IsLower(text[0]);
            using Font font = new(lower ? "Segoe Print" : "Segoe UI Black", lower ? AppConfig.TrayLowercaseFontSize : AppConfig.TrayUppercaseFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using SolidBrush fgBrush = new(fg);
            using StringFormat sf = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };

            RectangleF rect = lower ? TrayIconTextRectLower : TrayIconTextRectUpper;
            if (lower)
            {
                g.DrawString(text, font, fgBrush, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), sf);
                g.DrawString(text, font, fgBrush, new RectangleF(rect.X + 1f, rect.Y, rect.Width, rect.Height), sf);
                g.DrawString(text, font, fgBrush, new RectangleF(rect.X, rect.Y + 1f, rect.Width, rect.Height), sf);
                g.DrawString(text, font, fgBrush, new RectangleF(rect.X + 1f, rect.Y + 1f, rect.Width, rect.Height), sf);
                g.DrawString(text, font, fgBrush, new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width, rect.Height), sf);
            }
            else g.DrawString(text, font, fgBrush, rect, sf);

            IntPtr hIcon = bmp.GetHicon(); Icon icon = (Icon)Icon.FromHandle(hIcon).Clone(); NativeMethods.DestroyIcon(hIcon); return icon;
        }

        private void ApplyVisualState(ImeState.State state)
        {
            if (!_assetCache.TryGetValue(state, out StateAssets? assets)) return;
            try { if (assets.TrayIcon != null && (_sysTrayIcon.Icon == null || _sysTrayIcon.Icon.Handle != assets.TrayIcon.Handle)) _sysTrayIcon.Icon = assets.TrayIcon; }
            catch { _sysTrayIcon.Icon = assets.TrayIcon; }
            _sysTrayIcon.Text = UiText.TrayTooltip(assets.Description);
            _menuItemStatus.Text = UiText.StatusLabel(assets.Description);
        }

        private void RefreshKeyboardLayoutOverlay()
        {
            if (!_isKeyboardLayoutOverlayEnabled) { CloseAllLayoutForms(); return; }

            var processor = GlobalInputHook.ActiveProcessor;
            bool isPhyShift = (NativeMethods.GetKeyState(0x10) & 0x8000) != 0;
            if (AppConfig.EnableCopilotMap && ((NativeMethods.GetKeyState(0x5B) & 0x8000) != 0 || (NativeMethods.GetKeyState(0x5C) & 0x8000) != 0)) isPhyShift = false;
            
            bool isVirtShift = processor != null ? processor.IsVirtualShift : _isShiftVisualInverted;
            string suffix = (isPhyShift ^ isVirtShift) ? "2" : "1";
            string? name = null;

            if (_previousImeState == ImeState.State.EnglishLower || _previousImeState == ImeState.State.EnglishUpper || _previousImeState == ImeState.State.JapaneseIME) name = $"EnglishKey{suffix}.png";
            else if (_previousImeState == ImeState.State.Hangul) name = $"KoreanKey{suffix}.png";
            else if (_activeCapsMode == CapsMode.Japanese1) name = $"Japan1Layer1Key{suffix}.png";
            else if (_activeCapsMode == CapsMode.Japanese2) name = $"Japan1Layer2Key{suffix}.png";
            else if (_activeCapsMode == CapsMode.Japanese3) name = $"Japan2Layer{(processor?.CurrentLayer ?? 1)}Key{suffix}.png";
            else name = $"KoreanKey{suffix}.png";

            if (name == null) return;

            if (_frmKeyboardLayout == null || _frmKeyboardLayout.IsDisposed)
            {
                _frmKeyboardLayout = new KeyboardLayoutForm();
                if (_lastKeyboardLayoutLocation != Point.Empty) _frmKeyboardLayout.Location = _lastKeyboardLayoutLocation;
                _frmKeyboardLayout.OnLayoutDoubleClicked += (s, e) => { if (GlobalInputHook.ActiveProcessor != null) GlobalInputHook.ActiveProcessor.ToggleVirtualShift(); else _isShiftVisualInverted = !_isShiftVisualInverted; RefreshKeyboardLayoutOverlay(); };
                _frmKeyboardLayout.OnClosedByUser += (s, e) => { _isKeyboardLayoutOverlayEnabled = false; _menuItemToggleKeyboardLayout.Checked = false; CloseAllLayoutForms(); };
            }

            _frmKeyboardLayout.UpdateImage(name);
            if (!_frmKeyboardLayout.Visible) { _frmKeyboardLayout.Show(); if (_frmKeyboardLayout.WindowState == FormWindowState.Minimized) _frmKeyboardLayout.WindowState = FormWindowState.Normal; }
        }

        private void CloseAllLayoutForms()
        {
            if (_frmKeyboardLayout != null) { _lastKeyboardLayoutLocation = _frmKeyboardLayout.Location; _frmKeyboardLayout.Close(); _frmKeyboardLayout = null; }
        }

        private static IntPtr SearchFocusedInputHwnd(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return IntPtr.Zero;
            NativeMethods.GUITHREADINFO gti = new() { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            if (NativeMethods.GetGUIThreadInfo(NativeMethods.GetWindowThreadProcessId(hWnd, out _), ref gti))
            {
                if (gti.hwndFocus != IntPtr.Zero) return gti.hwndFocus;
                if (gti.hwndActive != IntPtr.Zero) return gti.hwndActive;
            }
            return hWnd;
        }

        private unsafe bool IsTaskbarWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            Span<char> nm = stackalloc char[256];
            fixed (char* p = nm)
            {
                int len = NativeMethods.GetClassName(hWnd, p, 256);
                if (len > 0) { var s = nm.Slice(0, len); return s.IndexOf("Shell_TrayWnd") >= 0 || s.IndexOf("NotifyIconOverflowWindow") >= 0; }
                return false;
            }
        }

        private unsafe bool IsAppOrTrayWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || hWnd == this.Handle) return true;
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid); if (pid == s_currentProcessId) return true;
            Span<char> nm = stackalloc char[256];
            fixed (char* p = nm)
            {
                int len = NativeMethods.GetClassName(hWnd, p, 256);
                if (len > 0) { var s = nm.Slice(0, len); return s.IndexOf("Progman") >= 0 || s.IndexOf("WorkerW") >= 0 || s.IndexOf("#32768") >= 0; }
                return false;
            }
        }
    }
    #endregion
}