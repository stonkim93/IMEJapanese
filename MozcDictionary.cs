// MozcDictionary.cs
#nullable enable
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
// KanjiCandidateOverlay.cs
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;
// GoogleJapaneseInputApi.cs
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;

namespace IMEJapanese
{
    public static class MozcDictionary
    {
        public static event Action? DictionaryLoaded;

        public class KanjiEntry
        {
            public string Reading { get; set; } = string.Empty;
            public string Kanji { get; set; } = string.Empty;
            public ushort LeftId { get; set; }
            public ushort RightId { get; set; }
            public short Cost { get; set; }

            public KanjiEntry() { }

            public KanjiEntry(string reading, string kanji, ushort leftId = 0, ushort rightId = 0, short cost = 0)
            {
                Reading = reading;
                Kanji = kanji;
                LeftId = leftId;
                RightId = rightId;
                Cost = cost;
            }
        }

        public class ReadingMatch
        {
            public int Length { get; set; }
            public KanjiEntry Entry { get; set; } = new();
        }

        public static bool IsLoaded { get; private set; } = false;

        private static short[]? _transitionMatrix;
        private static int _matrixSize;
        private static SqliteConnection? _connection;

        public static void LoadDictionary()
        {
            if (IsLoaded) return;

            try
            {
                string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mozc_dict_connect.db");
                if (!File.Exists(dbPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[MozcDictionary] DB 파일을 찾을 수 없습니다: {dbPath}");
                    return;
                }

                string connectionString = $"Data Source={dbPath}";
                _connection = new SqliteConnection(connectionString);
                _connection.Open();

                LoadConnectionMatrix(_connection);

                IsLoaded = true;
                DictionaryLoaded?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MozcDictionary] 사전 로드 중 오류 발생: {ex}");
            }
        }

        private static void LoadConnectionMatrix(SqliteConnection connection)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT matrix_size, data FROM matrix_metadata WHERE id = 1 LIMIT 1;";

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    _matrixSize = reader.GetInt32(0);

                    using var blobStream = reader.GetStream(1);
                    using var ms = new MemoryStream();
                    blobStream.CopyTo(ms);
                    byte[] raw = ms.ToArray();

                    // Byte 배열을 short(Int16) 배열로 변환
                    _transitionMatrix = new short[raw.Length / 2];
                    Buffer.BlockCopy(raw, 0, _transitionMatrix, 0, raw.Length);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MozcDictionary] Connection Matrix 로드 실패: {ex}");
            }
        }

        public static int GetTransitionCost(int rightId, int leftId)
        {
            if (_transitionMatrix == null || _matrixSize == 0) return 0;

            long index = ((long)rightId * _matrixSize) + leftId;
            if (index >= 0 && index < _transitionMatrix.Length)
            {
                return _transitionMatrix[index];
            }
            return 0;
        }

        public static void PrintStatistics()
        {
            System.Diagnostics.Debug.WriteLine($"[MozcDictionary] Matrix Size: {_matrixSize}");
            System.Diagnostics.Debug.WriteLine($"[MozcDictionary] Loaded Matrix length: {_transitionMatrix?.Length ?? 0}");
        }

        public static bool IsJapaneseText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                if ((c >= 0x3040 && c <= 0x309F) ||
                    (c >= 0x30A0 && c <= 0x30FF) ||
                    (c >= 0x4E00 && c <= 0x9FAF))
                {
                    return true;
                }
            }
            return false;
        }

        // 입력된 문자열(가타카나 포함)을 히라가나로 정규화
        public static string NormalizeToHiragana(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                // 가타카나 유니코드 범위인 경우 히라가나로 변환 (- 0x0060)
                if (c >= 0x30A1 && c <= 0x30F6)
                {
                    sb.Append((char)(c - 0x0060));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        // SQLite DB에서 주어진 텍스트의 접두어(Prefix)에 일치하는 사전 항목을 조회
        public static List<ReadingMatch> GetEntriesForReadingAt(string text, int startIndex, int maxPerSubstring = 5)
        {
            var results = new List<ReadingMatch>();
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open) return results;

            // 너무 긴 문자열 탐색 방지를 위해 최대길이 20자로 제한
            int maxLen = Math.Min(text.Length - startIndex, 20);
            var prefixes = new List<string>();
            for (int i = 1; i <= maxLen; i++)
            {
                prefixes.Add(text.Substring(startIndex, i));
            }

            if (prefixes.Count == 0) return results;

            try
            {
                using var cmd = _connection.CreateCommand();
                var parameters = new List<string>();

                // IN 절에 사용할 파라미터 생성
                for (int i = 0; i < prefixes.Count; i++)
                {
                    string pName = $"@p{i}";
                    parameters.Add(pName);
                    cmd.Parameters.AddWithValue(pName, prefixes[i]);
                }

                // SQLite IN 질의를 통해 일치하는 읽기를 가진 단어를 비용(cost) 오름차순으로 조회
                // python build_mozc_db.py에서 생성하는 일반적인 Mozc SQLite 스키마 명칭인 dictionary를 사용
                cmd.CommandText = $@"
                    SELECT reading, kanji, left_id, right_id, cost 
                    FROM dictionary 
                    WHERE reading IN ({string.Join(", ", parameters)}) 
                    ORDER BY cost ASC";

                using var reader = cmd.ExecuteReader();
                var matchesByReading = new Dictionary<string, List<KanjiEntry>>();

                while (reader.Read())
                {
                    string reading = reader.GetString(0);
                    string kanji = reader.GetString(1);
                    ushort leftId = (ushort)reader.GetInt32(2);
                    ushort rightId = (ushort)reader.GetInt32(3);
                    short cost = (short)reader.GetInt32(4);

                    if (!matchesByReading.ContainsKey(reading))
                    {
                        matchesByReading[reading] = new List<KanjiEntry>();
                    }

                    // 각 Substring(접두어) 마다 요구된 maxPerSubstring 개수만큼만 가져옵니다.
                    if (matchesByReading[reading].Count < maxPerSubstring)
                    {
                        matchesByReading[reading].Add(new KanjiEntry(reading, kanji, leftId, rightId, cost));
                    }
                }

                // 조회된 결과를 ReadingMatch 객체 리스트로 병합
                foreach (var kvp in matchesByReading)
                {
                    foreach (var entry in kvp.Value)
                    {
                        results.Add(new ReadingMatch { Length = kvp.Key.Length, Entry = entry });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MozcDictionary] DB 조회 오류: {ex.Message}");
            }

            return results;
        }
        public static void Dispose()
        {
            if (_connection != null)
            {
                if (_connection.State == System.Data.ConnectionState.Open)
                {
                    _connection.Close();
                }
                _connection.Dispose();
                _connection = null;
            }
            IsLoaded = false;
        }
    }

    // KanjiCandidateOverlay.cs
    // ============================================================================================
    // 한자 후보 선택 오버레이 창
    // [핵심 설계] ShowWithoutActivation + WS_EX_NOACTIVATE 을 사용하여
    // 오버레이가 표시되어도 원래 앱이 포그라운드를 유지합니다.
    // 키 입력은 GlobalInputHook의 KbdHookCallback에서 HandleKeyFromHook()을 통해 전달받습니다.
    // ============================================================================================
    internal class KanjiCandidateOverlay : Form
    {
        // ─────────────────────────────────────────────────────────────
        // 정적 필드: 현재 활성화된 오버레이 인스턴스 추적
        // GlobalInputHook에서 키 입력을 전달할 때 이 참조를 사용합니다.
        // ─────────────────────────────────────────────────────────────
        private static KanjiCandidateOverlay? _activeOverlay;

        /// <summary>
        /// 현재 한자 후보 오버레이가 활성(표시 중)인지 여부.
        /// GlobalInputHook의 KbdHookCallback에서 키 가로채기 여부를 결정할 때 사용합니다.
        /// </summary>
        public static bool IsActive => _activeOverlay != null && _activeOverlay.Visible;

        // ─────────────────────────────────────────────────────────────
        // 인스턴스 필드
        // ─────────────────────────────────────────────────────────────
        private readonly List<string> _displayTexts;
        private readonly Action<int> _onSelectedIndex;
        private int _selectedIndex = 0;
        private readonly List<Label> _labels = new();

        // ─────────────────────────────────────────────────────────────
        // [핵심 변경 1] ShowWithoutActivation 오버라이드
        // 이 속성이 true이면 Show() 호출 시 창이 포커스를 빼앗지 않습니다.
        // 원래 앱(예: 메모장, VS Code 등)이 포그라운드를 유지합니다.
        // ─────────────────────────────────────────────────────────────
        protected override bool ShowWithoutActivation => true;

        // ─────────────────────────────────────────────────────────────
        // [핵심 변경 2] CreateParams에 WS_EX_NOACTIVATE 스타일 추가
        // WS_EX_NOACTIVATE: 사용자가 클릭해도 이 창이 활성화되지 않음
        // WS_EX_TOOLWINDOW: 작업 표시줄에 나타나지 않음
        // WS_EX_TOPMOST: 항상 최상위 표시
        // ─────────────────────────────────────────────────────────────
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE - 활성화 방지
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW - 작업 표시줄 비표시
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST    - 최상위
                return cp;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 생성자
        // ─────────────────────────────────────────────────────────────
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
            // [변경] KeyPreview와 KeyDown은 더 이상 사용하지 않음
            // 키 입력은 GlobalInputHook → HandleKeyFromHook()을 통해 처리됩니다.
            // [변경] Deactivate 이벤트 핸들러 제거
            // 포커스를 받지 않으므로 Deactivate가 발생하지 않음
            // 대신 MouseHookCallback에서 외부 클릭 시 DismissActiveOverlay()를 호출합니다.
        }

        private void BuildUI()
        {
            var font = new Font("Meiryo UI", 14f, FontStyle.Regular, GraphicsUnit.Point);
            int spacing = 6;
            int width = 0;
            int height = Padding.Top + Padding.Bottom;

            for (int i = 0; i < _displayTexts.Count; i++)
            {
                var lbl = new Label()
                {
                    AutoSize = true,
                    Font = font,
                    ForeColor = Color.Black,
                    BackColor = Color.Transparent,
                    Text = _displayTexts[i],
                };
                _labels.Add(lbl);
                Controls.Add(lbl);

                using (var g = CreateGraphics())
                {
                    var sz = g.MeasureString(lbl.Text, lbl.Font);
                    width = Math.Max(width, (int)sz.Width + Padding.Left + Padding.Right + 24);
                    height += (int)sz.Height + spacing;
                }
            }

            int y = Padding.Top;
            foreach (var lbl in _labels)
            {
                lbl.Location = new Point(Padding.Left + 6, y);
                y += lbl.Height + spacing;
            }

            Size = new Size(Math.Max(200, width), Math.Max(40, height + 8));
            UpdateSelectionVisual();
        }

        // ─────────────────────────────────────────────────────────────
        // UI 업데이트: 선택된 항목 하이라이트
        // ─────────────────────────────────────────────────────────────
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

        // ─────────────────────────────────────────────────────────────
        // [핵심 변경 3] OnShown에서 Focus() 호출 제거
        // 포커스를 빼앗지 않으므로 Focus() 호출이 불필요합니다.
        // ─────────────────────────────────────────────────────────────
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Focus() 및 _labels[0].Focus() 호출을 제거함
            // 키 입력은 GlobalInputHook에서 처리됩니다.
            Debug.WriteLine("[KanjiOverlay] OnShown - 오버레이 표시됨 (포커스 이동 없음)");
        }

        // ─────────────────────────────────────────────────────────────
        // 폼 닫힘 시 정적 참조 정리
        // ─────────────────────────────────────────────────────────────
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            // 현재 인스턴스가 활성 오버레이인 경우에만 정적 참조를 null로 설정
            if (_activeOverlay == this)
            {
                _activeOverlay = null;
                Debug.WriteLine("[KanjiOverlay] OnFormClosed - 활성 오버레이 참조 해제됨");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 선택 확정 후 닫기
        // ─────────────────────────────────────────────────────────────
        private void SelectAndClose()
        {
            if (_selectedIndex >= 0 && _selectedIndex < _displayTexts.Count)
                _onSelectedIndex(_selectedIndex);
            else
                _onSelectedIndex(-1);
            Close();
        }

        // =============================================================
        // 정적 API: GlobalInputHook에서 호출하는 메서드들
        // =============================================================

        /// <summary>
        /// GlobalInputHook의 KbdHookCallback에서 호출됩니다.
        /// 한자 후보 오버레이가 활성 상태일 때, 키 입력을 처리합니다.
        /// 
        /// 반환값: true = 키가 처리됨 (소비), false = 키가 처리되지 않음
        /// </summary>
        /// <param name="vkCode">가상 키 코드 (예: 0x26=Up, 0x28=Down 등)</param>
        /// <returns>키를 소비했으면 true</returns>
        public static bool HandleKeyFromHook(int vkCode)
        {
            var overlay = _activeOverlay;
            if (overlay == null || !overlay.Visible) return false;

            // UI 스레드에서 실행해야 하므로 Invoke 사용
            // 하지만 훅 콜백에서는 빠르게 반환해야 하므로, 
            // 키 처리 가능 여부만 빠르게 판단하고 실제 처리는 BeginInvoke로 비동기 위임
            switch (vkCode)
            {
                case 0x1B: // Escape - 취소
                    overlay.BeginInvoke(new Action(() =>
                    {
                        overlay._onSelectedIndex(-1);
                        overlay.Close();
                    }));
                    return true;

                case 0x26: // Up - 위로 이동
                    overlay.BeginInvoke(new Action(() =>
                    {
                        overlay._selectedIndex = Math.Max(0, overlay._selectedIndex - 1);
                        overlay.UpdateSelectionVisual();
                    }));
                    return true;

                case 0x28: // Down - 아래로 이동
                    overlay.BeginInvoke(new Action(() =>
                    {
                        overlay._selectedIndex = Math.Min(overlay._labels.Count - 1, overlay._selectedIndex + 1);
                        overlay.UpdateSelectionVisual();
                    }));
                    return true;

                case 0x0D: // Enter - 선택 확정
                case 0x20: // Space - 선택 확정
                    overlay.BeginInvoke(new Action(() =>
                    {
                        overlay.SelectAndClose();
                    }));
                    return true;

                // 숫자키 1-9 (메인 키보드)
                case >= 0x31 and <= 0x39:
                    int n1 = vkCode - 0x31; // 0-based index
                    if (n1 < overlay._displayTexts.Count)
                    {
                        overlay.BeginInvoke(new Action(() =>
                        {
                            overlay._selectedIndex = n1;
                            overlay.SelectAndClose();
                        }));
                        return true;
                    }
                    return true; // 범위 밖이어도 키는 소비 (대상 앱에 전달 안 함)

                // 숫자키 1-9 (넘패드)
                case >= 0x61 and <= 0x69:
                    int n2 = vkCode - 0x61; // 0-based index
                    if (n2 < overlay._displayTexts.Count)
                    {
                        overlay.BeginInvoke(new Action(() =>
                        {
                            overlay._selectedIndex = n2;
                            overlay.SelectAndClose();
                        }));
                        return true;
                    }
                    return true; // 범위 밖이어도 키는 소비

                default:
                    // 한자 후보 활성 중에는 나머지 키도 소비하여 
                    // 대상 앱에 의도치 않은 입력이 가지 않도록 합니다.
                    return true;
            }
        }

        /// <summary>
        /// GlobalInputHook의 MouseHookCallback에서 호출됩니다.
        /// 오버레이 영역 밖을 클릭하면 오버레이를 닫습니다.
        /// 이전의 Deactivate 이벤트를 대체합니다.
        /// </summary>
        /// <param name="clickPoint">마우스 클릭 좌표 (스크린 좌표)</param>
        public static void HandleMouseClickFromHook(Point clickPoint)
        {
            var overlay = _activeOverlay;
            if (overlay == null || !overlay.Visible) return;

            // 오버레이 영역 안의 클릭은 무시 (오버레이 내부 상호작용)
            if (overlay.Bounds.Contains(clickPoint)) return;

            // 오버레이 영역 밖 클릭 → 취소하고 닫기
            Debug.WriteLine("[KanjiOverlay] 외부 클릭 감지 - 오버레이 닫기");
            overlay.BeginInvoke(new Action(() =>
            {
                overlay._onSelectedIndex(-1);
                overlay.Close();
            }));
        }

        /// <summary>
        /// 현재 활성화된 오버레이를 강제로 닫습니다.
        /// 예: 사용자가 다른 조작을 시작할 때 호출됩니다.
        /// </summary>
        public static void DismissActiveOverlay()
        {
            var overlay = _activeOverlay;
            if (overlay == null || !overlay.Visible) return;

            Debug.WriteLine("[KanjiOverlay] DismissActiveOverlay 호출 - 오버레이 강제 닫기");
            overlay.BeginInvoke(new Action(() =>
            {
                overlay._onSelectedIndex(-1);
                overlay.Close();
            }));
        }

        // =============================================================
        // 정적 팩토리: 오버레이 표시 (KanjiEntry 버전)
        // =============================================================
        internal static void ShowOverlay(Point location, List<MozcDictionary.KanjiEntry> candidates, Action<MozcDictionary.KanjiEntry?> onSelected)
        {
            if (candidates == null || candidates.Count == 0)
            {
                onSelected?.Invoke(null);
                return;
            }

            // 이전 오버레이가 열려있으면 먼저 닫기
            DismissActiveOverlay();

            var items = candidates.Take(9).ToList();
            var displayTexts = items.Select((c, i) => $"{i + 1}. {c.Kanji}  ({c.Reading})").ToList();

            var overlay = new KanjiCandidateOverlay(displayTexts, selectedIndex =>
            {
                if (selectedIndex >= 0 && selectedIndex < items.Count)
                    onSelected?.Invoke(items[selectedIndex]);
                else
                    onSelected?.Invoke(null);
            });

            // 정적 참조에 등록 (GlobalInputHook에서 접근하기 위함)
            _activeOverlay = overlay;
            ShowAtLocation(overlay, location);
        }

        // =============================================================
        // 정적 팩토리: 오버레이 표시 (String 버전, Replacement용)
        // =============================================================
        internal static void ShowOverlay(Point location, List<string> items, Action<string?> onSelected)
        {
            if (items == null || items.Count == 0)
            {
                onSelected?.Invoke(null);
                return;
            }

            // 이전 오버레이가 열려있으면 먼저 닫기
            DismissActiveOverlay();

            var displayItems = items.Take(9).ToList();
            var overlay = new KanjiCandidateOverlay(displayItems, selectedIndex =>
            {
                if (selectedIndex >= 0 && selectedIndex < displayItems.Count)
                    onSelected?.Invoke(displayItems[selectedIndex]);
                else
                    onSelected?.Invoke(null);
            });

            // 정적 참조에 등록
            _activeOverlay = overlay;
            ShowAtLocation(overlay, location);
        }

        // =============================================================
        // [핵심 변경 4] ShowAtLocation에서 Activate() 호출 제거
        // Show()만 호출하여 창을 표시하되, 포커스는 빼앗지 않습니다.
        // =============================================================
        private static void ShowAtLocation(Form overlay, Point location)
        {
            overlay.Location = location;
            var screen = Screen.FromPoint(location);

            // 화면 경계 밖으로 나가지 않도록 위치 조정
            if (overlay.Right > screen.WorkingArea.Right)
                overlay.Left = Math.Max(screen.WorkingArea.Left, screen.WorkingArea.Right - overlay.Width - 8);

            if (overlay.Bottom > screen.WorkingArea.Bottom)
                overlay.Top = Math.Max(screen.WorkingArea.Top, screen.WorkingArea.Bottom - overlay.Height - 8);

            overlay.Show();
            // [변경] overlay.Activate() 호출 제거
            // ShowWithoutActivation + WS_EX_NOACTIVATE에 의해 포커스 이동 없이 표시됩니다.
            Debug.WriteLine("[KanjiOverlay] ShowAtLocation 완료 - Activate() 호출 없음");
        }
    }

    public static class GoogleJapaneseInputApi
    {
        // 타임아웃 유지 및 소켓 고갈 방지를 위한 단일 인스턴스
        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        public static async Task<List<string>> GetCandidatesAsync(string text)
        {
            var candidates = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return candidates;

            try
            {
                string encodedText = Uri.EscapeDataString(text);
                string url = $"http://www.google.com/transliterate?langpair=ja-Hira|ja&text={encodedText}";

                // HTTP 요청
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    var firstSegment = root[0];
                    if (firstSegment.ValueKind == JsonValueKind.Array && firstSegment.GetArrayLength() >= 2)
                    {
                        var firstSegmentCandidates = firstSegment[1].EnumerateArray()
                            .Select(x => x.GetString())
                            .Where(x => !string.IsNullOrEmpty(x))
                            .ToList();

                        // 성능 최적화: StringBuilder 사용
                        var remainingTextBuilder = new StringBuilder();
                        for (int i = 1; i < root.GetArrayLength(); i++)
                        {
                            var seg = root[i];
                            if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() >= 2)
                            {
                                var segCands = seg[1].EnumerateArray();
                                if (segCands.Any())
                                {
                                    remainingTextBuilder.Append(segCands.First().GetString());
                                }
                            }
                        }

                        string remainingText = remainingTextBuilder.ToString();

                        foreach (var cand in firstSegmentCandidates)
                        {
                            if (cand != null)
                            {
                                candidates.Add(cand + remainingText);
                            }
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("[Google API] Timeout or cancelled.");
            }
            catch (HttpRequestException httpEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Google API] Network Error: {httpEx.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Google API] Parsing Error: {ex.Message}");
            }

            return candidates.Distinct().ToList();
        }
    }

}