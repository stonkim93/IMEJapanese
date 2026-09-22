#nullable enable
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text;
// KanjiCandidateOverlay.cs
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;
// GoogleJapaneseInputApi.cs
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Reflection;

namespace IMEJapanese
{
    public static class MozcDictionary
    {
        public static event Action? DictionaryLoaded;

        private static readonly ConcurrentDictionary<string, List<KanjiEntry>> _entryCache = new(StringComparer.Ordinal);
        private const int MaxCacheSize = 5000;

        public class KanjiEntry
        {
            public string Reading { get; set; } = string.Empty;
            public string Kanji { get; set; } = string.Empty;
            public ushort LeftId { get; set; }
            public ushort RightId { get; set; }
            public int Cost { get; set; }

            public KanjiEntry() { }

            public KanjiEntry(string reading, string kanji, ushort leftId = 0, ushort rightId = 0, int cost = 0)
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

        // [수정 #3] LoadDictionary 중복 실행 방지: 동시에 여러 Task.Run에서 호출될 때
        // SqliteConnection이 중복 생성되는 경주 조건 차단
        private static readonly SemaphoreSlim _loadSemaphore = new SemaphoreSlim(1, 1);

        private static short[]? _transitionMatrix;
        private static int _matrixSize;
        private static SqliteConnection? _connection;

/// <summary>
        /// Store 빌드 여부에 따라 쓰기 가능한 사전 DB 경로를 반환합니다.
        /// </summary>
        public static string GetDictionaryPath()
        {
#if STORE_BUILD
            string localAppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IMEJapanese");
            if (!Directory.Exists(localAppDataDir))
            {
                Directory.CreateDirectory(localAppDataDir);
            }
            return Path.Combine(localAppDataDir, "mozc_dict_connect.db");
#else
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mozc_dict_connect.db");
#endif
        }

#if STORE_BUILD
        /// <summary>
        /// Store 빌드 시 임베디드 리소스(mozc_dict_connect.db.gz)를 LocalAppData 경로로 자동 해제합니다.
        /// </summary>
        public static bool EnsureStoreDictionaryExtracted(string dbPath)
        {
            if (File.Exists(dbPath)) return true;

            try
            {
                string? dir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var assembly = Assembly.GetExecutingAssembly();
                string resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("mozc_dict_connect.db.gz", StringComparison.OrdinalIgnoreCase))
                    ?? "IMEJapanese.mozc_dict_connect.db.gz";

                using var resourceStream = assembly.GetManifestResourceStream(resourceName);
                if (resourceStream == null)
                {
                    if (AppConfig.LogLevel >= 1) Debug.WriteLine($"[MozcDictionary] Embedded Resource '{resourceName}'를 찾을 수 없습니다.");
                    return false;
                }

                using var gzipStream = new GZipStream(resourceStream, CompressionMode.Decompress);
                using var fileStream = File.Create(dbPath);
                gzipStream.CopyTo(fileStream);

                if (AppConfig.LogLevel >= 1) Debug.WriteLine($"[MozcDictionary] Store 사전 해제 완료: {dbPath}");
                return true;
            }
            catch (Exception ex)
            {
                if (AppConfig.LogLevel >= 1) Debug.WriteLine($"[MozcDictionary] Store 사전 해제 중 오류 발생: {ex}");
                return false;
            }
        }
#endif

        public static void LoadDictionary()
        {
            if (IsLoaded) return;

            // [수정 #3] 세마포어로 진입 시도 (로드 중이면 즉시 리턴)
            if (!_loadSemaphore.Wait(0)) return;
            try
            {
                if (IsLoaded) return; // double-check inside lock

                string dbPath = GetDictionaryPath();

#if STORE_BUILD
                if (!File.Exists(dbPath))
                {
                    EnsureStoreDictionaryExtracted(dbPath);
                }
#endif

                if (!File.Exists(dbPath))
                {
                    throw new FileNotFoundException($"[MozcDictionary] DB 파일을 찾을 수 없습니다. 경로: {dbPath}");
                }

                string connectionString = $"Data Source={dbPath}";
                _connection = new SqliteConnection(connectionString);
                _connection.Open();

                using (var pragmaCmd = _connection.CreateCommand())
                {
                    pragmaCmd.CommandText = @"
                        PRAGMA mmap_size = 268435456; 
                        PRAGMA cache_size = -10000; 
                        PRAGMA temp_store = MEMORY; 
                        PRAGMA synchronous = OFF;
                        PRAGMA journal_mode = OFF;";
                    pragmaCmd.ExecuteNonQuery();
                }

                LoadConnectionMatrix(_connection);

                IsLoaded = true;
                DictionaryLoaded?.Invoke();
            }
            catch (Exception ex)
            {
                if (AppConfig.LogLevel >= 1) Debug.WriteLine($"[MozcDictionary] 사전 로드 중 오류 발생: {ex}");
                MessageBox.Show($"DB 로드 실패:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _loadSemaphore.Release();
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

                    _transitionMatrix = new short[raw.Length / 2];
                    Buffer.BlockCopy(raw, 0, _transitionMatrix, 0, raw.Length);
                }
            }
            catch (Exception ex)
            {
                if (AppConfig.LogLevel >= 1) Debug.WriteLine($"[MozcDictionary] Connection Matrix 로드 실패: {ex}");
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
            if (AppConfig.LogLevel >= 2)
            {
                Debug.WriteLine($"[MozcDictionary] Matrix Size: {_matrixSize}");
                Debug.WriteLine($"[MozcDictionary] Loaded Matrix length: {_transitionMatrix?.Length ?? 0}");
                Debug.WriteLine($"[MozcDictionary] Entry Cache Count: {_entryCache.Count}");
            }
        }

        public static bool IsJapaneseText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                if (CharacterDatabase.IsValidCharacter(c) ||
                    (c >= 0x4E00 && c <= 0x9FAF))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddToCache(string key, List<KanjiEntry> entries)
        {
            if (_entryCache.Count >= MaxCacheSize)
            {
                _entryCache.Clear();
            }
            _entryCache[key] = entries;
        }

        public static List<ReadingMatch> GetEntriesForReadingAt(string text, int startIndex, int maxPerSubstring = 5)
        {
            var results = new List<ReadingMatch>();
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open) return results;

            int maxLen = Math.Min(text.Length - startIndex, MozcConfig.MaxPrefixMatchLength);
            
            var missingPrefixes = new List<string>(maxLen);
            var cachedMatches = new List<(int Length, string Prefix, List<KanjiEntry> Entries)>(maxLen);

            for (int i = 1; i <= maxLen; i++)
            {
                string prefix = text.Substring(startIndex, i);
                if (_entryCache.TryGetValue(prefix, out var cachedEntries))
                {
                    cachedMatches.Add((i, prefix, cachedEntries));
                }
                else
                {
                    missingPrefixes.Add(prefix);
                }
            }

            if (missingPrefixes.Count > 0)
            {
                try
                {
                    var fetched = GetEntriesForReadingsBatch(missingPrefixes);
                    
                    for (int i = 0; i < missingPrefixes.Count; i++)
                    {
                        string p = missingPrefixes[i];
                        if (fetched.TryGetValue(p, out var entries))
                        {
                            AddToCache(p, entries);
                            cachedMatches.Add((p.Length, p, entries));
                        }
                        else
                        {
                            AddToCache(p, new List<KanjiEntry>(0));
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (AppConfig.LogLevel >= 1) Debug.WriteLine($"[MozcDictionary] DB 조회 오류: {ex.Message}");
                }
            }

            foreach (var match in cachedMatches)
            {
                int count = Math.Min(match.Entries.Count, maxPerSubstring);
                for (int i = 0; i < count; i++)
                {
                    results.Add(new ReadingMatch { Length = match.Length, Entry = match.Entries[i] });
                }
            }

            results.Sort((a, b) =>
            {
                int lenCmp = b.Length.CompareTo(a.Length);
                if (lenCmp != 0) return lenCmp;
                return a.Entry.Cost.CompareTo(b.Entry.Cost);
            });

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

        public static List<KanjiEntry> GetKanjiCandidatesFromDb(string reading)
        {
            if (string.IsNullOrEmpty(reading)) return new List<KanjiEntry>(0);

            if (_entryCache.TryGetValue(reading, out var cached))
            {
                if (cached.Count <= MozcConfig.MaxDisplayCandidates) return new List<KanjiEntry>(cached);
                return cached.GetRange(0, MozcConfig.MaxDisplayCandidates);
            }

            var results = new List<KanjiEntry>();
            
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                return results;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = $@"
                    SELECT reading, kanji, left_id, right_id, cost
                    FROM dictionary
                    WHERE reading = @reading
                    ORDER BY cost ASC
                    LIMIT {MozcConfig.MaxDisplayCandidates};";
                
                var param = cmd.CreateParameter();
                param.ParameterName = "@reading";
                param.Value = EncodeReadingToBlob(reading);
                param.SqliteType = SqliteType.Blob;
                cmd.Parameters.Add(param);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string kanji = reader.GetString(1);
                    ushort leftId = (ushort)reader.GetInt32(2);
                    ushort rightId = (ushort)reader.GetInt32(3);
                    int cost = reader.GetInt32(4);

                    results.Add(new KanjiEntry(reading, kanji, leftId, rightId, cost));
                }

                AddToCache(reading, results);
            }
            catch (Exception ex)
            {
                if (AppConfig.LogLevel >= 1) Debug.WriteLine($"[MozcDictionary] DB 단건 조회 오류: {ex.Message}");
            }

            return results;
        }

        public static Dictionary<string, List<KanjiEntry>> GetEntriesForReadingsBatch(IEnumerable<string> readings)
        {
            var results = new Dictionary<string, List<KanjiEntry>>(StringComparer.Ordinal);
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open) 
                return results;

            var uniqueToFetch = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in readings)
            {
                if (string.IsNullOrWhiteSpace(r)) continue;

                if (_entryCache.TryGetValue(r, out var cached))
                {
                    results[r] = cached;
                }
                else
                {
                    uniqueToFetch.Add(r);
                }
            }

            if (uniqueToFetch.Count == 0) return results;
            var readingList = uniqueToFetch.ToList(); 

            try
            {
                int batchSize = MozcConfig.DbQueryBatchSize;
                for (int i = 0; i < readingList.Count; i += batchSize)
                {
                    int currentBatchSize = Math.Min(batchSize, readingList.Count - i);
                    using var cmd = _connection.CreateCommand();
                    
                    var inClauseBuilder = new StringBuilder((currentBatchSize * 5) - 2);
                    
                    for (int j = 0; j < currentBatchSize; j++)
                    {
                        string pName = $"@p{j}";
                        if (j > 0) inClauseBuilder.Append(", ");
                        inClauseBuilder.Append(pName);
                        
                        var param = cmd.CreateParameter();
                        param.ParameterName = pName;
                        param.Value = EncodeReadingToBlob(readingList[i + j]); 
                        param.SqliteType = SqliteType.Blob;
                        cmd.Parameters.Add(param);
                    }

                    cmd.CommandText = $@"
                        SELECT reading, kanji, left_id, right_id, cost 
                        FROM dictionary 
                        WHERE reading IN ({inClauseBuilder})";

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        byte[] readingBlob = reader.GetFieldValue<byte[]>(0);
                        string reading = DecodeBlobToReading(readingBlob);
                        
                        string kanji = reader.GetString(1);
                        ushort leftId = (ushort)reader.GetInt32(2);
                        ushort rightId = (ushort)reader.GetInt32(3);
                        int cost = reader.GetInt32(4);

                        if (!results.TryGetValue(reading, out var list))
                        {
                            list = new List<KanjiEntry>();
                            results[reading] = list;
                        }

                        list.Add(new KanjiEntry(reading, kanji, leftId, rightId, cost));
                    }
                }

                foreach (var kvp in results)
                {
                    AddToCache(kvp.Key, kvp.Value);
                }
            }
            catch (Exception ex)
            {
                if (AppConfig.LogLevel >= 1) Debug.WriteLine($"[MozcDictionary] DB Batch 조회 오류: {ex.Message}");
            }

            return results;
        }

        public static byte[] EncodeReadingToBlob(string reading)
        {
            if (string.IsNullOrEmpty(reading)) return Array.Empty<byte>();

            byte[] blob = new byte[reading.Length * 2];
            for (int i = 0; i < reading.Length; i++)
            {
                char c = reading[i];
                ushort code = (ushort)c;
                
                // CharacterDatabase의 O(1) Dictionary 캐싱을 활용해 코드 변환
                if (CharacterDatabase.IsValidCharacter(c))
                {
                    code = CharacterDatabase.GetCodeFromChar(c);
                }
                
                blob[i * 2] = (byte)(code & 0xFF);
                blob[i * 2 + 1] = (byte)((code >> 8) & 0xFF);
            }
            return blob;
        }

        public static string DecodeBlobToReading(byte[] blob)
        {
            if (blob == null || blob.Length == 0) return string.Empty;

            return string.Create(blob.Length / 2, blob, (span, state) =>
            {
                for (int i = 0; i < span.Length; i++)
                {
                    ushort code = (ushort)(state[i * 2] | (state[i * 2 + 1] << 8));
                    
                    // 기존의 baseCode 수동 계산 로직을 제거하고 
                    // 500 사이즈로 최적화된 CharacterDatabase.ContainsCode를 직접 호출
                    if (CharacterDatabase.ContainsCode(code))
                    {
                        // 경량화된 JapaneseCharacter 구조체를 활용하여 문자 렌더링
                        var jpChar = JapaneseCharacter.FromCode(code);
                        
                        bool isKatakana = (code % 10) >= 5;
                        span[i] = isKatakana ? jpChar.Katakana : jpChar.Hiragana;
                    }
                    else
                    {
                        span[i] = (char)code;
                    }
                }
            });
        }
    }
    

    public static class GoogleJapaneseInputApi
    {
        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(MozcConfig.ApiTimeoutSeconds)
        };

        public static async Task<List<string>> GetCandidatesAsync(string text)
        {
            // [최적화] List 대신 HashSet을 사용하여 중복 검사 오버헤드 완화 및 GC 효율 증가
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();

            try
            {
                string encodedText = Uri.EscapeDataString(text);
                // [수정 #16] http → https 보안 강화 (MITM 공격 방지)
                string url = $"https://www.google.com/transliterate?langpair=ja-Hira|ja&text={encodedText}";

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
                if (AppConfig.LogLevel >= 1) Debug.WriteLine("[Google API] Timeout or cancelled.");
            }
            catch (HttpRequestException httpEx)
            {
                if (AppConfig.LogLevel >= 1) Debug.WriteLine($"[Google API] Network Error: {httpEx.Message}");
            }
            catch (Exception ex)
            {
                if (AppConfig.LogLevel >= 1) Debug.WriteLine($"[Google API] Parsing Error: {ex.Message}");
            }

            return candidates.ToList();
        }
    }
}