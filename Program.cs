// Program.cs - IMEJapanese
#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041.0")]

namespace IMEJapanese
{
    public enum OverlayPositionMode
    {
        CharInput,
        CharToggle,
        SelectionToggle,
        ModeSwitch
    }

    #region [ 진입점 (Main) ]
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            using Mutex mutexPointer = new Mutex(true, @"Global\IMEPointer_SingleInstance", out bool isPointerFirst);
            using Mutex mutexPali = new Mutex(true, @"Global\IMEPali_SingleInstance", out bool isPaliFirst);
            using Mutex mutexJapanese = new Mutex(true, @"Global\IMEJapanese_SingleInstance", out bool isJapaneseFirst);

            if (!isPointerFirst || !isPaliFirst || !isJapaneseFirst)
            {
                MessageBox.Show("IMEJapanese 앱이 이미 실행 중입니다.", "IMEJapanese", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            // [수정 #14] 앱 시작 시 레지스트리에서 이전 설정값 로드
            RegistryManager.LoadSettings();

            // [최적화 1] 불필요하게 중첩된 try-catch 블록 제거 및 정리
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IMEJapanese.log");
                var listener = new TextWriterTraceListener(logPath);
                Trace.Listeners.Add(listener);
                Trace.AutoFlush = true;
            }
            catch { }

            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                try
                {
                    Task.Run(() =>
                    {
                        MozcDictionary.LoadDictionary();
                        MozcDictionary.PrintStatistics();
                    });
                }
                catch (Exception ex)
                {
                    if (AppConfig.LogLevel >= 1) System.Diagnostics.Debug.WriteLine($"Failed to start MozcDictionary loader task: {ex.Message}");
                }

                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                if (AppConfig.LogLevel >= 1) Trace.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Fatal Error: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"{UiText.FatalErrorPrefix}{ex.Message}", UiText.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        internal enum CapsMode { Japanese1 = 1, Japanese2 = 2, Japanese3 = 3 }

        private CapsMode _activeCapsMode = (CapsMode)AppConfig.DefaultCapsMode;
        private bool _isKeyboardLayoutOverlayEnabled = AppConfig.DefaultShowKeyboardLayout;

        private ToolStripMenuItem _menuItemCapsJapanese1 = null!;
        private ToolStripMenuItem _menuItemCapsJapanese2 = null!;
        private ToolStripMenuItem _menuItemCapsJapanese3 = null!;

        private ToolStripMenuItem _menuItemUseMozc = null!;
        private ToolStripMenuItem _menuItemUseGoogleApi = null!;

        private ToolStripMenuItem _menuItemToggleKeyboardLayout = null!;
        private ToolStripMenuItem _menuItemToggleTextOverlay = null!;
        private ToolStripMenuItem _menuItemToggleCopilotMap = null!;

        private bool _isShiftVisualInverted = false;
        private bool _lastHangulSyncState = false;
        private bool _lastOurWindowHangulState = false;
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

        private static readonly CapsModeStateMapping[] _capsModeMaps = {
            new(CapsMode.Japanese1, ImeState.State.JapaneseHangul1, KeyProcessorFactory.Japanese1),
            new(CapsMode.Japanese2, ImeState.State.JapaneseHangul2, KeyProcessorFactory.Japanese2),
            new(CapsMode.Japanese3, ImeState.State.JapaneseHangul3, KeyProcessorFactory.Japanese3)
        };
        
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
                if (e.Button == MouseButtons.Left)
                {
                    _trayContextMenu.Show(Cursor.Position);
                }
            };

            GlobalInputHook.Install();

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            RebuildStateAssets();

            _stateCheckTimer = new System.Windows.Forms.Timer { Interval = AppConfig.PollingInterval };
            _stateCheckTimer.Tick += ProcessStateCheck;

            MozcDictionary.DictionaryLoaded += OnMozcDictionaryLoaded;
            if (MozcDictionary.IsLoaded)
            {
                UpdateDictionaryStatusUi();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            GlobalInputHook.Uninstall();
            MozcDictionary.Dispose();
            base.OnFormClosing(e);
        }

        private void OnMozcDictionaryLoaded()
        {
            UpdateDictionaryStatusUi();
        }

        // [수정 #2] 사전 로드 상태 UI 피드백 강화 (로드 중 / 완료 동시 표시)
        // [uc5d0최 2] UI 스레드 접근 패턴을 InvokeRequired를 사용해 일관성 있고 깔끔하게 수정
        private void UpdateDictionaryStatusUi()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(UpdateDictionaryStatusUi));
                return;
            }

            try
            {
                if (MozcDictionary.IsLoaded)
                {
                    _menuItemStatus.Text = "사전 로드 완료";
                    _sysTrayIcon.Text = UiText.TrayTooltip("사전 로드 완료");
                }
                else
                {
                    // [수정 #2] 사전 로드 중임을 사용자에게 로드 중 메시지로 안내
                    _menuItemStatus.Text = "사전 로드 중...";
                    _sysTrayIcon.Text = UiText.TrayTooltip("사전 로드 중");
                }
            }
            catch { }
        }

        private static Rectangle ResolveCaretRectangle()
        {
            IntPtr hFore = NativeMethods.GetForegroundWindow();
            uint tid = NativeMethods.GetWindowThreadProcessId(hFore, out _);
            NativeMethods.GUITHREADINFO gti = new() { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };

            if (NativeMethods.GetGUIThreadInfo(tid, ref gti) && gti.hwndCaret != IntPtr.Zero)
            {
                NativeMethods.POINT ptTopLeft = new() { X = gti.rectLeft, Y = gti.rectTop };
                NativeMethods.ClientToScreen(gti.hwndCaret, ref ptTopLeft);
                NativeMethods.POINT ptBottomRight = new() { X = gti.rectRight, Y = gti.rectBottom };
                NativeMethods.ClientToScreen(gti.hwndCaret, ref ptBottomRight);

                int width = Math.Max(1, ptBottomRight.X - ptTopLeft.X);
                int height = Math.Max(1, ptBottomRight.Y - ptTopLeft.Y);
                if (height < 5) height = 24;

                return new Rectangle(ptTopLeft.X, ptTopLeft.Y, width, height);
            }

            try
            {
                var focusedElement = System.Windows.Automation.AutomationElement.FocusedElement;
                if (focusedElement != null && focusedElement.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out object patternObj))
                {
                    var textPattern = (System.Windows.Automation.TextPattern)patternObj;
                    var selections = textPattern.GetSelection();
                    
                    if (selections.Length > 0)
                    {
                        var rects = selections[0].GetBoundingRectangles();
                        if (rects.Length > 0)
                        {
                            return new Rectangle((int)rects[0].X, (int)rects[0].Y, (int)rects[0].Width, (int)rects[0].Height);
                        }
                    }
                }
            }
            catch { }

            if (NativeMethods.GetCursorPos(out NativeMethods.POINT mPt)) return new Rectangle(mPt.X, mPt.Y, 1, 24);
            return new Rectangle(0, 0, 1, 24);
        }

        // [최적화 3] 중복 코드를 제거하기 위한 제네릭 코어 헬퍼 메소드 구현
        private void ShowKanjiCandidateCore<T>(List<T> candidates, string originalText, bool isReplacingSelection, Action<Rectangle, List<T>, Action<T?>> showOverlay, Func<T, string?> getResultString)
        {
            if (candidates == null || candidates.Count == 0) return;

            try
            {
                this.BeginInvoke(new Action(() =>
                {
                    IntPtr targetHwnd = LastValidFocusHwnd != IntPtr.Zero ? LastValidFocusHwnd : (LastValidHwnd != IntPtr.Zero ? LastValidHwnd : NativeMethods.GetForegroundWindow());
                    bool wasHangul = ImeState.CheckHangulPublic(targetHwnd);
                    Rectangle targetRect = ResolveCaretRectangle();

                    showOverlay(targetRect, candidates, (selected) =>
                    {
                        string? resultStr = selected != null ? getResultString(selected) : null;

                        if (string.IsNullOrEmpty(resultStr)) 
                        {
                            if (wasHangul && targetHwnd != IntPtr.Zero)
                            {
                                ImeState.SetHangulState(targetHwnd, true);
                            }
                            return;
                        }

                        System.Threading.Tasks.Task.Run(() =>
                        {
                            GlobalInputHook.CommitKanjiConversion(originalText, resultStr, isReplacingSelection);

                            if (wasHangul && targetHwnd != IntPtr.Zero)
                            {
                                ImeState.SetHangulState(targetHwnd, true);
                            }
                        });
                    });
                }));
            }
            catch { }
        }

        internal void ShowKanjiCandidateAsync(List<MozcDictionary.KanjiEntry> candidates, string originalText, bool isReplacingSelection = false)
        {
            ShowKanjiCandidateCore(candidates, originalText, isReplacingSelection, KanjiCandidateOverlay.ShowOverlay, item => item?.Kanji);
        }

        internal void ShowKanjiCandidateAsync(List<string> replacements, string originalText, bool isReplacingSelection = false)
        {
            ShowKanjiCandidateCore(replacements, originalText, isReplacingSelection, KanjiCandidateOverlay.ShowOverlay, item => item);
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

            _menuItemCapsJapanese1 = AddMenuToggle("일본어1_조합형_대표자음", AppConfig.ShowCapsJapanese1, (s, e) => UpdateCapsMode(CapsMode.Japanese1));
            _menuItemCapsJapanese2 = AddMenuToggle("일본어2_조합형_최빈자음", AppConfig.ShowCapsJapanese2, (s, e) => UpdateCapsMode(CapsMode.Japanese2));
            _menuItemCapsJapanese3 = AddMenuToggle("일본어3_완성형_3Layer", AppConfig.ShowCapsJapanese3, (s, e) => UpdateCapsMode(CapsMode.Japanese3));
            AddMenuSeparatorIf(AppConfig.ShowCapsJapanese1 || AppConfig.ShowCapsJapanese2 || AppConfig.ShowCapsJapanese3);

            _menuItemUseMozc = new ToolStripMenuItem("Mozc 오프라인 한자변환", null, async (s, e) =>
            {
                string dbPath = MozcDictionary.GetDictionaryPath();

#if STORE_BUILD
                // MS Store 빌드 (IsStoreBuild=true): Embedded Resource에서 LocalAppData 폴더로 자동 해제
                if (!File.Exists(dbPath))
                {
                    _menuItemStatus.Text = "현재 상태: 사전 해제 중...";
                    bool extracted = MozcDictionary.EnsureStoreDictionaryExtracted(dbPath);
                    if (extracted)
                    {
                        _menuItemStatus.Text = "현재 상태: 사전 해제 완료";
                        if (!MozcDictionary.IsLoaded)
                        {
                            _ = Task.Run(() => { MozcDictionary.LoadDictionary(); });
                        }
                    }
                    else
                    {
                        _menuItemStatus.Text = "현재 상태: 사전 해제 실패";
                        MessageBox.Show("사전 리소스를 해제하지 못했습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
#else
                // 일반 빌드 (IsStoreBuild=false): 온라인에서 실행 파일 경로로 다운로드 및 압축 해제
                if (!File.Exists(dbPath))
                {
                    DialogResult result = MessageBox.Show(
                        "오프라인 사전을 사용하려면 'mozc_dict_connect.db' 파일이 필요합니다.\n온라인으로 사전DB 파일을 다운로드 하시겠습니까?",
                        "사전 다운로드",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result == DialogResult.Yes)
                    {
                        try
                        {
                            _menuItemStatus.Text = "현재 상태: 사전 다운로드 중...";
                            string zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mozc_dict_connect.zip");

                            using (var client = new System.Net.Http.HttpClient())
                            {
                                var response = await client.GetAsync("https://github.com/stonkim93/IMEJapanese/releases/download/IMEJapanese/mozc_dict_connect.zip");
                                response.EnsureSuccessStatusCode();
                                using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                                {
                                    await response.Content.CopyToAsync(fs);
                                }
                            }

                            _menuItemStatus.Text = "현재 상태: 사전 압축 해제 중...";

                            ZipFile.ExtractToDirectory(zipPath, AppDomain.CurrentDomain.BaseDirectory, true);

                            if (File.Exists(zipPath))
                            {
                                File.Delete(zipPath);
                            }

                            _menuItemStatus.Text = "현재 상태: 사전 다운로드 완료";
                            MessageBox.Show("사전 파일이 성공적으로 복사되었습니다.", "다운로드 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);

                            if (!MozcDictionary.IsLoaded)
                            {
                                _ = Task.Run(() => { MozcDictionary.LoadDictionary(); });
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"다운로드 또는 압축해제 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            _menuItemStatus.Text = "현재 상태: 사전 다운로드 실패";
                        }
                    }
                }
#endif

                if (File.Exists(dbPath))
                {
                    AppConfig.UseGoogleApi = false;
                }
                else
                {
                    AppConfig.UseGoogleApi = true;
                }
                SyncDictionaryApiMenuChecks();
            });

            _menuItemUseGoogleApi = new ToolStripMenuItem("Google 온라인 한자변환", null, (s, e) =>
            {
                AppConfig.UseGoogleApi = true;
                SyncDictionaryApiMenuChecks();
            });

            _trayContextMenu.Items.Add(_menuItemUseMozc);
            _trayContextMenu.Items.Add(_menuItemUseGoogleApi);
            _trayContextMenu.Items.Add(new ToolStripSeparator());

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
            SyncDictionaryApiMenuChecks();
        }

        private void SyncDictionaryApiMenuChecks()
        {
            if (_menuItemUseMozc != null) _menuItemUseMozc.Checked = !AppConfig.UseGoogleApi;
            if (_menuItemUseGoogleApi != null) _menuItemUseGoogleApi.Checked = AppConfig.UseGoogleApi;
            // [수정 #14] API 선택 변경 시 절리스트리에 즉시 저장
            RegistryManager.SaveSettings();
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
            _lastHangulSyncState = ImeState.CheckHangulPublic(_currentContextHwnd);

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
            AppConfig.DefaultCapsMode = (int)mode; // [수정 #14] 현재 모드를 DefaultCapsMode에 동기화
            SyncCapsMenuChecks();
            _previousImeState = (ImeState.State)(-1);
            RefreshKeyboardLayoutOverlay();
            // [수정 #14] CapsMode 변경 시 레지스트리에 저장
            RegistryManager.SaveSettings();

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
            if (ImeState.CheckHangulPublic(targetHwnd) || retryCount <= 0) return;

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
            bool cachedIsHangulMode = (isTaskbar || isTrayOrApp || isLayoutForm) ? _lastHangulSyncState : ImeState.CheckHangulPublic(contextHwnd);
            ushort contextLangId = ResolveLanguageId(contextHwnd);

            TrackCurrentWindow(contextHwnd, isTaskbar, isTrayOrApp, isLayoutForm);

            ImeState.State currentState = ImeState.Detect(contextHwnd, _activeCapsMode == CapsMode.Japanese1, _activeCapsMode == CapsMode.Japanese2, _activeCapsMode == CapsMode.Japanese3, cachedIsHangulMode);

            ActiveInputModeContext activeInputMode = ResolveInputModeContext(currentState);

            GlobalInputHook.UpdateContext(new GlobalInputHook.HookContextSnapshot(
                contextHwnd, contextLangId, cachedIsHangulMode, activeInputMode.ActiveProcessor,
                activeInputMode.IsJapanese1ModeActive, activeInputMode.IsJapanese2ModeActive, activeInputMode.IsJapanese3ModeActive));

            if (KanjiCandidateOverlay.IsActive)
            {
                RefreshKeyboardLayoutOverlay();
                return;
            }

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
            bool isOurWindow = (isTrayOrApp || isTaskbar || isLayoutForm);
            bool isCurrentHangul = ImeState.CheckHangulPublic(actualHFore);

            if (isFocusChanged)
            {
                if (!isOurWindow)
                {
                    _lastHangulSyncState = isCurrentHangul;
                }
                else
                {
                    if (isCurrentHangul != _lastHangulSyncState)
                    {
                        ImeState.SetHangulState(actualHFore, _lastHangulSyncState);
                        // 새로 설정 후 실제 적용된 상태를 읽어 기준점으로 삼음 (실패 시 플립 방지)
                        isCurrentHangul = ImeState.CheckHangulPublic(actualHFore);
                    }
                    _lastOurWindowHangulState = isCurrentHangul;
                }
            }
            else
            {
                bool stateChanged = false;
                if (isOurWindow)
                {
                    // 우리가 관리하는 창에서 상태가 변경되었다면 (사용자가 한영키를 누름)
                    if (isCurrentHangul != _lastOurWindowHangulState)
                    {
                        _lastOurWindowHangulState = isCurrentHangul;
                        stateChanged = true;
                    }
                }
                else if (isCurrentHangul != _lastHangulSyncState)
                {
                    stateChanged = true;
                }

                if (stateChanged)
                {
                    _lastHangulSyncState = isCurrentHangul;

                    Action<IntPtr> SetState = (hwnd) => { if (hwnd != IntPtr.Zero && hwnd != actualHFore) ImeState.SetHangulState(hwnd, isCurrentHangul); };
                    SetState(LastValidHwnd);
                    SetState(_frmKeyboardLayout?.Handle ?? IntPtr.Zero);
                    SetState(this.Handle);

                    IntPtr kanjiOverlayHandle = KanjiCandidateOverlay.ActiveHandle;
                    if (kanjiOverlayHandle != IntPtr.Zero)
                    {
                        SetState(kanjiOverlayHandle);
                    }
                }
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
            foreach (var map in _capsModeMaps)
            {
                if (_activeCapsMode == map.Mode && state == map.ActiveState)
                {
                    return new ActiveInputModeContext(
                        map.Mode == CapsMode.Japanese1, 
                        map.Mode == CapsMode.Japanese2, 
                        map.Mode == CapsMode.Japanese3, 
                        map.Processor
                    );
                }
            }
            return new ActiveInputModeContext(false, false, false, null);
        }

        public void ShowOverlay(string text, int durationMs = AppConfig.OverlayDefaultDurationMs, OverlayPositionMode mode = OverlayPositionMode.CharInput)
        {
            if (!_isTextOverlayEnabled) return;

            float scaledFontSize = AppConfig.OverlayDefaultFontSize * _currentDpiScale;
            int scaledHeight = (int)Math.Round(AppConfig.OverlayDefaultHeight * _currentDpiScale);
            int scaledCharWidth = (int)Math.Round(AppConfig.OverlayDefaultCharWidth * _currentDpiScale);
            int scaledPadWidth = (int)Math.Round(AppConfig.OverlayDefaultPaddingWidth * _currentDpiScale);
            int scaledYOffset = (int)Math.Round(AppConfig.OverlayDefaultYOffset * _currentDpiScale);

            if (this.InvokeRequired) this.BeginInvoke(new Action(() => ExecuteShowOverlay(text, durationMs > 0, scaledFontSize, scaledHeight, scaledCharWidth, scaledPadWidth, scaledYOffset, mode)));
            else ExecuteShowOverlay(text, durationMs > 0, scaledFontSize, scaledHeight, scaledCharWidth, scaledPadWidth, scaledYOffset, mode);
        }

        public void ClearOverlay() => _frmTextOverlay?.Clear();

        private void ExecuteShowOverlay(string ch, bool useTimer, float fontSize, int formH, int charW, int padW, int yOffset, OverlayPositionMode mode)
        {
            int length = 0; foreach (char c in ch) length += (c >= 0x1100 && c <= 0xD7AF) ? 2 : 1;
            int minWidth = (int)Math.Round(40 * _currentDpiScale);
            Size sz = new Size(Math.Max(length * (charW / 2) + padW, minWidth), formH);

            Point pt = CalculateOverlayLocation(sz, yOffset, mode);
            _frmTextOverlay?.ShowOverlay(ch, useTimer, fontSize, sz.Width, sz.Height, pt.X, pt.Y);
        }

        private static Point CalculateOverlayLocation(Size overlaySize, int yOffset, OverlayPositionMode mode)
        {
            Rectangle caretRect = ResolveCaretRectangle();
            var screen = Screen.FromPoint(caretRect.Location);
            int x = caretRect.Left;
            int y = caretRect.Bottom + yOffset;

            if (mode == OverlayPositionMode.ModeSwitch)
            {
                x = screen.WorkingArea.Left + (screen.WorkingArea.Width - overlaySize.Width) / 2;
                y = screen.WorkingArea.Top + (screen.WorkingArea.Height * 3) / 4 - overlaySize.Height / 2;
            }
            else if (mode == OverlayPositionMode.CharInput || mode == OverlayPositionMode.CharToggle)
            {
                x = caretRect.Right - overlaySize.Width;
            }
            else if (mode == OverlayPositionMode.SelectionToggle)
            {
                x = caretRect.Left;
            }

            if (x + overlaySize.Width > screen.WorkingArea.Right)
                x = screen.WorkingArea.Right - overlaySize.Width;
            if (x < screen.WorkingArea.Left)
                x = screen.WorkingArea.Left;

            if (y + overlaySize.Height > screen.WorkingArea.Bottom)
                y = caretRect.Top - overlaySize.Height - 5;
            if (y < screen.WorkingArea.Top)
                y = screen.WorkingArea.Top;

            return new Point(x, y);
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
            }
            catch { }
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
            else if (_activeCapsMode == CapsMode.Japanese1) name = $"Japan1Key{suffix}.png";
            else if (_activeCapsMode == CapsMode.Japanese2) name = $"Japan2Key{suffix}.png";
            else if (_activeCapsMode == CapsMode.Japanese3) name = $"Japan3Layer{(processor?.CurrentLayer ?? 1)}Key{suffix}.png";
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