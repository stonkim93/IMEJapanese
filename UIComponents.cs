// UIComponents.cs - IMEJapanese
#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;

namespace IMEJapanese
{
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

    #region [ 한자 변환 오버레이 표시 폼 (KanjiCandidateOverlay) ]
    internal class KanjiCandidateOverlay : Form
    {
        private static KanjiCandidateOverlay? _activeOverlay;

        public static bool IsActive => _activeOverlay != null && _activeOverlay.Visible;
        public static IntPtr ActiveHandle => _activeOverlay != null && _activeOverlay.IsHandleCreated ? _activeOverlay.Handle : IntPtr.Zero;

        private readonly List<string> _displayTexts;
        private readonly Action<int> _onSelectedIndex;
        private int _selectedIndex = 0;
        private readonly List<Label> _labels = new();
        private bool _isClosing = false;

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
                return cp;
            }
        }

        private KanjiCandidateOverlay(List<string> displayTexts, Action<int> onSelectedIndex)
        {
            _displayTexts = displayTexts ?? new List<string>();
            _onSelectedIndex = onSelectedIndex ?? (_ => { });

            InitializeForm();
            BuildUI();
        }

        private void InitializeForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(250, 250, 250);
            Padding = new Padding(6);
        }

        private void BuildUI()
        {
            var font = new Font("Meiryo UI", 14f, FontStyle.Regular, GraphicsUnit.Point);
            int spacing = 6;
            int width = 0;

            for (int i = 0; i < _displayTexts.Count; i++)
            {
                int itemIndex = i;
                var lbl = new Label()
                {
                    AutoSize = true,
                    Font = font,
                    ForeColor = Color.Black,
                    BackColor = Color.Transparent,
                    Text = _displayTexts[i],
                    Cursor = Cursors.Hand
                };

                lbl.MouseEnter += (s, e) =>
                {
                    _selectedIndex = itemIndex;
                    UpdateSelectionVisual();
                };

                lbl.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        _selectedIndex = itemIndex;
                        SelectAndClose();
                    }
                };

                _labels.Add(lbl);
                Controls.Add(lbl);

                using (var g = CreateGraphics())
                {
                    var sz = g.MeasureString(lbl.Text, lbl.Font);
                    width = Math.Max(width, (int)sz.Width + Padding.Left + Padding.Right + 24);
                }
            }

            int y = Padding.Top;
            foreach (var lbl in _labels)
            {
                lbl.Location = new Point(Padding.Left + 6, y);
                y += lbl.Height + spacing;
            }

            int totalHeight = _labels.Count > 0 
                ? (y - spacing + Padding.Bottom) 
                : Padding.Top + Padding.Bottom;

            Size = new Size(Math.Max(200, width), Math.Max(40, totalHeight));
            UpdateSelectionVisual();

            this.Cursor = Cursors.Hand;
            this.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    SelectAndClose();
                }
            };
        }

        private void UpdateSelectionVisual()
        {
            for (int i = 0; i < _labels.Count; i++)
            {
                if (i == _selectedIndex)
                {
                    _labels[i].BackColor = Color.SkyBlue;
                    _labels[i].ForeColor = Color.White;
                }
                else
                {
                    _labels[i].BackColor = Color.Transparent;
                    _labels[i].ForeColor = Color.Black;
                }
            }
            Invalidate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (AppConfig.LogLevel >= 2) Debug.WriteLine("[KanjiOverlay] OnShown - 오버레이 표시됨");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (_activeOverlay == this)
            {
                _activeOverlay = null;
                if (AppConfig.LogLevel >= 2) Debug.WriteLine("[KanjiOverlay] OnFormClosed - 참조 해제");
            }
        }

        private void SelectAndClose()
        {
            if (_isClosing) return;
            _isClosing = true;

            if (_selectedIndex >= 0 && _selectedIndex < _displayTexts.Count)
                _onSelectedIndex(_selectedIndex);
            else
                _onSelectedIndex(-1);
            Close();
        }

        public static bool HandleKeyFromHook(int vkCode)
        {
            var overlay = _activeOverlay;
            if (overlay == null || !overlay.Visible) return false;

            switch (vkCode)
            {
                case 0x1B:
                    overlay.BeginInvoke(new Action(() =>
                    {
                        overlay._onSelectedIndex(-1);
                        overlay.Close();
                    }));
                    return true;

                case 0x26:
                    overlay.BeginInvoke(new Action(() =>
                    {
                        overlay._selectedIndex = Math.Max(0, overlay._selectedIndex - 1);
                        overlay.UpdateSelectionVisual();
                    }));
                    return true;

                case 0x28:
                case 0x20:
                    overlay.BeginInvoke(new Action(() =>
                    {
                        overlay._selectedIndex = (overlay._selectedIndex + 1) % overlay._labels.Count;
                        overlay.UpdateSelectionVisual();
                    }));
                    return true;

                case 0x0D:
                    overlay.BeginInvoke(new Action(() =>
                    {
                        overlay.SelectAndClose();
                    }));
                    return true;

                case >= 0x31 and <= 0x39:
                    int n1 = vkCode - 0x31;
                    if (n1 < overlay._displayTexts.Count)
                    {
                        overlay.BeginInvoke(new Action(() =>
                        {
                            overlay._selectedIndex = n1;
                            overlay.SelectAndClose();
                        }));
                    }
                    return true;

                case >= 0x61 and <= 0x69:
                    int n2 = vkCode - 0x61;
                    if (n2 < overlay._displayTexts.Count)
                    {
                        overlay.BeginInvoke(new Action(() =>
                        {
                            overlay._selectedIndex = n2;
                            overlay.SelectAndClose();
                        }));
                    }
                    return true;

                default:
                    return true;
            }
        }

        public static void HandleMouseClickFromHook(Point clickPoint)
        {
            var overlay = _activeOverlay;
            if (overlay == null || !overlay.Visible) return;

            if (overlay.Bounds.Contains(clickPoint)) return;

            if (AppConfig.LogLevel >= 2) Debug.WriteLine("[KanjiOverlay] 외부 클릭 - 닫기");
            overlay.BeginInvoke(new Action(() =>
            {
                overlay._onSelectedIndex(-1);
                overlay.Close();
            }));
        }

        public static void DismissActiveOverlay()
        {
            var overlay = _activeOverlay;
            if (overlay == null || !overlay.Visible) return;

            overlay.BeginInvoke(new Action(() =>
            {
                overlay._onSelectedIndex(-1);
                overlay.Close();
            }));
        }

        // [최적화] 중복 로직 통합을 위한 내부 제네릭 메서드
        private static void ShowOverlayInternal<T>(Rectangle targetRect, List<T> items, Func<T, int, string> textSelector, Action<T?> onSelected) where T : class
        {
            if (items == null || items.Count == 0)
            {
                onSelected?.Invoke(null);
                return;
            }

            DismissActiveOverlay();

            var displayItems = items.Take(MozcConfig.MaxDisplayCandidates).ToList();
            var displayTexts = displayItems.Select((item, index) => textSelector(item, index)).ToList();

            var overlay = new KanjiCandidateOverlay(displayTexts, selectedIndex =>
            {
                if (selectedIndex >= 0 && selectedIndex < displayItems.Count)
                    onSelected?.Invoke(displayItems[selectedIndex]);
                else
                    onSelected?.Invoke(null);
            });

            _activeOverlay = overlay;
            ShowAtLocation(overlay, targetRect);
        }

        internal static void ShowOverlay(Rectangle targetRect, List<MozcDictionary.KanjiEntry> candidates, Action<MozcDictionary.KanjiEntry?> onSelected)
        {
            ShowOverlayInternal(targetRect, candidates, (c, i) => $"{i + 1}. {c.Kanji}  ({c.Reading})", onSelected);
        }

        internal static void ShowOverlay(Rectangle targetRect, List<string> items, Action<string?> onSelected)
        {
            ShowOverlayInternal(targetRect, items, (s, i) => s, onSelected);
        }

        private static void ShowAtLocation(Form overlay, Rectangle targetRect)
        {
            var screen = Screen.FromPoint(targetRect.Location);

            int x = targetRect.Left;
            int y = targetRect.Bottom + 5; 

            if (x + overlay.Width > screen.WorkingArea.Right)
            {
                x = screen.WorkingArea.Right - overlay.Width;
            }
            if (y + overlay.Height > screen.WorkingArea.Bottom)
            {
                y = targetRect.Top - overlay.Height - 5;
            }

            if (x < screen.WorkingArea.Left) x = screen.WorkingArea.Left;
            if (y < screen.WorkingArea.Top) y = screen.WorkingArea.Top;

            overlay.Location = new Point(x, y);
            overlay.Show();
        }
    }
    #endregion
}
