using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;
#if UNITY_2018_4_OR_NEWER
using Newtonsoft.Json;
#endif

/// <summary>
/// Simple data recorder for player information
/// Records player stats directly to CSV files in the same folder as BetaData
/// </summary>
namespace Shared.Player
{
    public class PlayerDataManager : MonoBehaviour
    {
        private static PlayerDataManager _instance;
        private static readonly object _lock = new object();
        private static bool _isQuitting = false;
        
        [Header("Data Storage Settings")]
        [Tooltip("Directory where player data will be saved (relative to My Documents folder)")]
        public string saveDirectory = "AlphaSquad/BetaData";
        
        [Tooltip("Filename for the player data CSV")]
        public string fileName = "player_data.csv";
        
        [Tooltip("Filename for the win history CSV")]
        public string winHistoryFileName = "win_history.csv";
        
        [Tooltip("How frequently to flush pending writes to disk (in seconds)")]
        public float writeBatchInterval = 30f;
        
        // File paths (calculated at runtime)
        private string fullSavePath;
        private string winHistoryPath;
        
        // Reference to the MockTwitchChat to identify test users
        private MockTwitchChat mockTwitchChat;
        
        // Cache of test usernames for faster lookup
        private HashSet<string> testUsernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Queues for batched writes - thread-safe collections
        private ConcurrentQueue<string> pendingPlayerDataWrites = new ConcurrentQueue<string>();
        private ConcurrentQueue<string> pendingWinHistoryWrites = new ConcurrentQueue<string>();
        
        // Flag to track if write operation is in progress
        private bool isWriting = false;
        
        // In-memory cache of player data to reduce file reads - protected by lock for thread safety
        private Dictionary<string, PlayerData> _playerDataCache = new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, PlayerStats> _playerStatsCache = new Dictionary<string, PlayerStats>();
        private readonly object _cacheLock = new object();
        
        private bool _isDataDirty = false;
        private float _lastSaveTime = 0f;
        private const float AUTO_SAVE_INTERVAL = 60f; // Auto-save every minute if changes occurred
        
        // Events
        public event Action<string, PlayerData> OnPlayerDataUpdated;
        public event Action<string, PlayerStats> OnPlayerStatsUpdated;
        
        // Reusable StringBuilder for string operations
        private readonly StringBuilder _reusableStringBuilder = new StringBuilder(1024);
        
        // Cancellation token source for async operations
        private CancellationTokenSource _cancellationTokenSource;
        
        // Class to represent player data in memory
        public class PlayerData
        {
            public string Username { get; set; }
            public int TotalGemsWon { get; set; }
            public int GiveawayWins { get; set; }
            public int TriviaWins { get; set; }
            public DateTime LastActiveTime { get; set; }
            public DateTime FirstActivityTime { get; set; }
            public bool IsTestUser { get; set; }
            
            public string ToCsvLine()
            {
                return $"{Username},{TotalGemsWon},{GiveawayWins},{TriviaWins},{LastActiveTime:yyyy-MM-dd HH:mm:ss},{FirstActivityTime:yyyy-MM-dd HH:mm:ss},{IsTestUser}";
            }
        }
        
        // Class to represent extended player statistics
        public class PlayerStats
        {
            public string Username { get; set; }
            public int TotalVotes { get; set; }
            public int TotalMessages { get; set; }
            public int CommandsUsed { get; set; }
            public DateTime LastActive { get; set; }
            public Dictionary<string, int> GameParticipation { get; set; } = new Dictionary<string, int>();
            
            public PlayerStats()
            {
                LastActive = DateTime.Now;
            }
        }
        
        // Properties
        public static PlayerDataManager Instance
        {
            get
            {
                if (_isQuitting)
                    return null;
                    
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = FindObjectOfType<PlayerDataManager>();
                        
                        if (_instance == null)
                        {
                            GameObject go = new GameObject("PlayerDataManager");
                            _instance = go.AddComponent<PlayerDataManager>();
                            DontDestroyOnLoad(go);
                        }
                    }
                    return _instance;
                }
            }
        }
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Initialize cancellation token source
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Set up save path - use a location outside of Unity's Application.persistentDataPath
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fullDir = Path.Combine(baseDir, saveDirectory);
            
            // Create directory if it doesn't exist
            if (!Directory.Exists(fullDir))
            {
                Directory.CreateDirectory(fullDir);
            }
            
            fullSavePath = Path.Combine(fullDir, fileName);
            winHistoryPath = Path.Combine(fullDir, winHistoryFileName);
            Debug.Log($"Player data will be saved to: {fullSavePath}");
            Debug.Log($"Win history will be saved to: {winHistoryPath}");
            
            // Create files with headers if they don't exist
            EnsureFilesExist();
            
            // Find MockTwitchChat in the scene
            mockTwitchChat = FindObjectOfType<MockTwitchChat>();
            
            // Initialize test usernames cache
            UpdateTestUsernameCache();
            
            // Start batch writing coroutine
            InvokeRepeating("ProcessPendingWrites", writeBatchInterval, writeBatchInterval);
            
            // Load player data cache on startup
            LoadAllPlayerData();
        }
        
        private void OnDestroy()
        {
            // Cancel any pending async operations
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
            
            // Make sure to flush any pending writes when destroyed
            ProcessPendingWrites();
            
            // Save pending changes
            if (_isDataDirty)
            {
                SaveAllPlayerData();
            }
            
            // Clear event subscribers to prevent memory leaks
            OnPlayerDataUpdated = null;
            OnPlayerStatsUpdated = null;
        }
        
        private void OnApplicationQuit()
        {
            _isQuitting = true;
            
            // Cancel any pending operations
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
            }
            
            // Ensure all pending writes are processed before quitting
            ProcessPendingWrites();
            
            // Save any pending changes
            if (_isDataDirty)
            {
                SaveAllPlayerData();
            }
        }
        
        private void Update()
        {
            // Auto-save if data is dirty and enough time has passed
            if (_isDataDirty && Time.realtimeSinceStartup - _lastSaveTime > AUTO_SAVE_INTERVAL)
            {
                SaveAllPlayerData();
                _lastSaveTime = Time.realtimeSinceStartup;
                _isDataDirty = false;
            }
        }
        
        /// <summary>
        /// Load player data into memory cache to reduce file I/O
        /// </summary>
        private async void LoadAllPlayerData()
        {
            lock (_cacheLock)
            {
                _playerDataCache.Clear();
                _playerStatsCache.Clear();
            }
            
            try
            {
                if (File.Exists(fullSavePath))
                {
                    // Use cancellation token for the async operation
                    string[] lines = await File.ReadAllLinesAsync(fullSavePath, _cancellationTokenSource.Token);
                    
                    // Pre-allocate capacity for better performance with large files
                    lock (_cacheLock)
                    {
                        if (lines.Length > 1)
                        {
                            _playerDataCache.EnsureCapacity(lines.Length - 1);
                        }
                    }
                    
                    // Skip header row
                    for (int i = 1; i < lines.Length; i++)
                    {
                        if (_cancellationTokenSource.IsCancellationRequested) return;
                        
                        string line = lines[i];
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        // Use memory-efficient CSV parsing
                        string[] parts = ParseCsvRowEfficient(line);
                        if (parts.Length < 6) continue; // Need at least 6 fields
                        
                        string username = parts[0];
                        
                        // Parse values with fallbacks
                        int.TryParse(parts[1], out int totalGems);
                        int.TryParse(parts[2], out int giveawayWins);
                        int.TryParse(parts[3], out int triviaWins);
                        
                        DateTime lastActive = DateTime.Now;
                        DateTime firstActive = DateTime.Now;
                        
                        DateTime.TryParse(parts[4], out lastActive);
                        DateTime.TryParse(parts[5], out firstActive);
                        
                        bool isTestUser = parts.Length > 6 && bool.TryParse(parts[6], out bool testUser) && testUser;
                        
                        PlayerData newData = new PlayerData
                        {
                            Username = username,
                            TotalGemsWon = totalGems,
                            GiveawayWins = giveawayWins,
                            TriviaWins = triviaWins,
                            LastActiveTime = lastActive,
                            FirstActivityTime = firstActive,
                            IsTestUser = isTestUser
                        };
                        
                        lock (_cacheLock)
                        {
                            _playerDataCache[username] = newData;
                        }
                    }
                    
                    Debug.Log($"Loaded {_playerDataCache.Count} player records into memory cache");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Player data loading was canceled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading player data cache: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Process all pending writes in a batch
        /// </summary>
        private async void ProcessPendingWrites()
        {
            if (isWriting) return; // Don't start multiple write operations
            
            try
            {
                isWriting = true;
                
                // Process player data writes
                if (pendingPlayerDataWrites.Count > 0)
                {
                    List<string> playerWrites = new List<string>(pendingPlayerDataWrites.Count);
                    while (pendingPlayerDataWrites.TryDequeue(out string line))
                    {
                        playerWrites.Add(line);
                    }
                    
                    if (playerWrites.Count > 0)
                    {
                        Debug.Log($"Writing {playerWrites.Count} player data records to disk");
                        await File.AppendAllLinesAsync(fullSavePath, playerWrites, _cancellationTokenSource.Token);
                    }
                }
                
                // Process win history writes
                if (pendingWinHistoryWrites.Count > 0)
                {
                    List<string> winWrites = new List<string>(pendingWinHistoryWrites.Count);
                    while (pendingWinHistoryWrites.TryDequeue(out string line))
                    {
                        winWrites.Add(line);
                    }
                    
                    if (winWrites.Count > 0)
                    {
                        Debug.Log($"Writing {winWrites.Count} win history records to disk");
                        await File.AppendAllLinesAsync(winHistoryPath, winWrites, _cancellationTokenSource.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Write operation was canceled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing pending writes: {ex.Message}");
            }
            finally
            {
                isWriting = false;
            }
        }
        
        /// <summary>
        /// Update the cache of test usernames from MockTwitchChat
        /// </summary>
        private void UpdateTestUsernameCache()
        {
            testUsernames.Clear();
            
            if (mockTwitchChat != null)
            {
                // Access the test usernames from MockTwitchChat using reflection (since the list is private)
                var usernamesList = mockTwitchChat.GetType().GetField("mockUsernamesList", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(mockTwitchChat) as List<MockTwitchChat.TestUsername>;
                    
                if (usernamesList != null)
                {
                    foreach (var testUser in usernamesList)
                    {
                        // Use reflection to get the username field
                        var username = testUser.GetType().GetField("username")?.GetValue(testUser) as string;
                        if (!string.IsNullOrEmpty(username))
                        {
                            // Add to our cache
                            testUsernames.Add(username.ToLowerInvariant());
                        }
                    }
                    
                    Debug.Log($"Cached {testUsernames.Count} test usernames from MockTwitchChat");
                }
            }
        }
        
        /// <summary>
        /// Check if a username is a test user
        /// </summary>
        /// <param name="username">Username to check</param>
        /// <returns>True if the user is a test user</returns>
        public bool IsTestUser(string username)
        {
            if (string.IsNullOrEmpty(username)) return false;
            
            // Clean up username for comparison
            string cleanUsername = username.Trim().ToLowerInvariant();
            if (cleanUsername.StartsWith("@"))
            {
                cleanUsername = cleanUsername.Substring(1);
            }
            
            // Method 1: Check if in our cached test user list from MockTwitchChat
            if (testUsernames.Contains(cleanUsername))
            {
                return true;
            }
            
            // Method 2: Check for the special test prefix we use 
            // The prefix "test_alphasquad_" is distinctive enough not to appear in real usernames
            if (cleanUsername.StartsWith("test_alphasquad_"))
            {
                return true;
            }
            
            // No more general heuristics - only explicit test users are detected
            return false;
        }
        
        /// <summary>
        /// Create files with headers if they don't exist
        /// </summary>
        private void EnsureFilesExist()
        {
            try
            {
                // Player data file
                if (!File.Exists(fullSavePath))
                {
                    using (StreamWriter writer = new StreamWriter(fullSavePath, false, Encoding.UTF8))
                    {
                        writer.WriteLine("Username,TotalGemsWon,GiveawayWins,TriviaWins,LastActiveTime,FirstActivityTime,IsTestUser");
                    }
                    Debug.Log("Created new player data file with headers");
                }
                else
                {
                    // Check if we need to update the header to include IsTestUser
                    UpdateCsvHeaderIfNeeded(fullSavePath, "IsTestUser");
                }
                
                // Win history file
                if (!File.Exists(winHistoryPath))
                {
                    using (StreamWriter writer = new StreamWriter(winHistoryPath, false, Encoding.UTF8))
                    {
                        writer.WriteLine("Username,GameType,GemAmount,Timestamp,IsTestUser");
                    }
                    Debug.Log("Created new win history file with headers");
                }
                else
                {
                    // Check if we need to update the header to include IsTestUser
                    UpdateCsvHeaderIfNeeded(winHistoryPath, "IsTestUser");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to create files: {e.Message}");
            }
        }
        
        /// <summary>
        /// Update a CSV file to add a new column if it's missing
        /// </summary>
        private void UpdateCsvHeaderIfNeeded(string filePath, string newColumnName)
        {
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                if (lines.Length > 0)
                {
                    string header = lines[0];
                    if (!header.Contains(newColumnName))
                    {
                        // Header needs updating
                        header += "," + newColumnName;
                        lines[0] = header;
                        
                        // Write back the updated file
                        File.WriteAllLines(filePath, lines);
                        Debug.Log($"Updated CSV header in {Path.GetFileName(filePath)} to include {newColumnName}");
                        
                        // If we're adding IsTestUser, also populate values for existing records
                        if (newColumnName == "IsTestUser")
                        {
                            PopulateTestUserColumn(filePath, lines);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to update CSV header: {e.Message}");
            }
        }
        
        /// <summary>
        /// Populate the IsTestUser column for existing records
        /// </summary>
        private void PopulateTestUserColumn(string filePath, string[] lines)
        {
            try
            {
                List<string> updatedLines = new List<string>();
                updatedLines.Add(lines[0]); // Add the header
                
                // Process each data row
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string[] fields = ParseCsvRow(line);
                    
                    if (fields.Length > 0)
                    {
                        string username = fields[0];
                        bool isTest = IsTestUser(username);
                        
                        // Append the test user status to the line
                        line += "," + isTest.ToString().ToLower();
                        updatedLines.Add(line);
                    }
                    else
                    {
                        // Keep the line as is if it can't be parsed
                        updatedLines.Add(line);
                    }
                }
                
                // Write back the updated file
                File.WriteAllLines(filePath, updatedLines);
                Debug.Log($"Populated IsTestUser column for {updatedLines.Count - 1} records in {Path.GetFileName(filePath)}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to populate test user column: {e.Message}");
            }
        }
        
        /// <summary>
        /// Record a win for a player
        /// </summary>
        /// <param name="username">Player username</param>
        /// <param name="gemAmount">Amount of gems won</param>
        /// <param name="gameType">Type of game (e.g., "Giveaway", "Trivia")</param>
        public void RecordWin(string username, int gemAmount, string gameType)
        {
            if (string.IsNullOrEmpty(username) || gemAmount <= 0)
            {
                return;
            }
            
            try
            {
                // Determine if this is a test user
                bool isTestUser = IsTestUser(username);
                
                // Update win counts in memory cache first
                lock (_cacheLock)
                {
                    if (!_playerDataCache.TryGetValue(username, out PlayerData playerData))
                    {
                        // New player
                        playerData = new PlayerData
                        {
                            Username = username,
                            TotalGemsWon = gemAmount,
                            GiveawayWins = 0,
                            TriviaWins = 0,
                            LastActiveTime = DateTime.Now,
                            FirstActivityTime = DateTime.Now,
                            IsTestUser = isTestUser
                        };
                        
                        _playerDataCache[username] = playerData;
                    }
                    else
                    {
                        // Existing player
                        playerData.TotalGemsWon += gemAmount;
                        playerData.LastActiveTime = DateTime.Now;
                    }
                    
                    // Increment specific game win counter
                    if (gameType?.ToLower() == "giveaway")
                    {
                        playerData.GiveawayWins++;
                    }
                    else if (gameType?.ToLower() == "trivia")
                    {
                        playerData.TriviaWins++;
                    }
                    
                    // Queue player data update
                    pendingPlayerDataWrites.Enqueue(playerData.ToCsvLine());
                }
                
                // Queue win history record
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                pendingWinHistoryWrites.Enqueue($"{username},{gameType},{gemAmount},{timestamp},{isTestUser}");
                
                // Mark data as dirty for auto-save
                _isDataDirty = true;
                
                // If we have too many pending writes, process them now to avoid memory issues
                if (pendingPlayerDataWrites.Count > 100 || pendingWinHistoryWrites.Count > 100)
                {
                    ProcessPendingWrites();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error recording win: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Escape a field for CSV format
        /// </summary>
        private string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            
            bool needsQuotes = field.Contains(",") || field.Contains("\"") || field.Contains("\n");
            if (!needsQuotes) return field;
            
            // Escape quotes by doubling them and wrap in quotes
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
        
        /// <summary>
        /// Parse a CSV row with minimal memory allocations
        /// </summary>
        private string[] ParseCsvRowEfficient(string row)
        {
            List<string> fields = new List<string>();
            bool inQuotes = false;
            
            // Reuse the StringBuilder to avoid allocations
            _reusableStringBuilder.Clear();
            
            for (int i = 0; i < row.Length; i++)
            {
                char c = row[i];
                
                if (c == '"')
                {
                    if (inQuotes && i < row.Length - 1 && row[i + 1] == '"')
                    {
                        // Escaped quote
                        _reusableStringBuilder.Append('"');
                        i++; // Skip the next quote
                    }
                    else
                    {
                        // Toggle quote mode
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    // End of field
                    fields.Add(_reusableStringBuilder.ToString());
                    _reusableStringBuilder.Clear();
                }
                else
                {
                    _reusableStringBuilder.Append(c);
                }
            }
            
            // Add the last field
            fields.Add(_reusableStringBuilder.ToString());
            
            return fields.ToArray();
        }
        
        /// <summary>
        /// Saves all player data to disk
        /// </summary>
        public async void SaveAllPlayerData()
        {
            try
            {
                // Reuse StringBuilder to avoid allocations
                _reusableStringBuilder.Clear();
                _reusableStringBuilder.AppendLine("Username,TotalGemsWon,GiveawayWins,TriviaWins,LastActiveTime,FirstActivityTime,IsTestUser");
                
                // Get a thread-safe copy of the cache
                Dictionary<string, PlayerData> cacheCopy;
                lock (_cacheLock)
                {
                    cacheCopy = new Dictionary<string, PlayerData>(_playerDataCache);
                }
                
                foreach (var entry in cacheCopy)
                {
                    _reusableStringBuilder.AppendLine(entry.Value.ToCsvLine());
                }
                
                // Write to temp file first, then move to avoid partial writes
                string tempPath = fullSavePath + ".temp";
                await File.WriteAllTextAsync(tempPath, _reusableStringBuilder.ToString(), _cancellationTokenSource.Token);
                File.Move(tempPath, fullSavePath, true);
                
                Debug.Log($"Saved {cacheCopy.Count} player records to disk");
                _isDataDirty = false;
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Save operation was canceled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error saving player data: {ex.Message}");
            }
        }
    }
}