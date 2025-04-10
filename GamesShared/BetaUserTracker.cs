using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Text;

/// <summary>
/// Tracks all users who interact with the games during beta period
/// Saves data to an external file for easy access
/// </summary>
public class BetaUserTracker : MonoBehaviour
{
    // Struct to store user data with test flag
    private struct UserData
    {
        public DateTime JoinDate;
        public bool IsTestUser;
        
        public UserData(DateTime joinDate, bool isTestUser)
        {
            JoinDate = joinDate;
            IsTestUser = isTestUser;
        }
    }
    
    // Singleton instance
    private static BetaUserTracker _instance;
    public static BetaUserTracker Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("BetaUserTracker");
                _instance = go.AddComponent<BetaUserTracker>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
    
    [Header("Beta User Tracking")]
    [Tooltip("Enable tracking of users during beta period")]
    public bool enableTracking = true;
    
    [Tooltip("Directory where beta user data will be saved (relative to My Documents folder)")]
    public string saveDirectory = "AlphaSquad/BetaData";
    
    [Tooltip("Filename for the beta user data")]
    public string fileName = "beta_users.csv";
    
    [Tooltip("Set to 0 to save immediately when a new user is added, or higher value to batch saves")]
    public float autoSaveInterval = 0f; // Save immediately by default
    
    // Reference to the MockTwitchChat to identify test users
    private MockTwitchChat mockTwitchChat;
    
    // Dictionary to track users and their data
    private Dictionary<string, UserData> betaUsers = new Dictionary<string, UserData>();
    
    // Flag to track if data has been modified and needs saving
    private bool isDirty = false;
    
    // Timer for auto-saving
    private float nextSaveTime = 0f;
    
    // Path to the CSV file (calculated at runtime)
    private string fullSavePath;
    
    void Awake()
    {
        // Singleton pattern
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        _instance = this;
        DontDestroyOnLoad(gameObject);
        
        // Set up save path - use a location outside of Unity's Application.persistentDataPath
        // This is intentionally outside Unity's managed directories so you can access it easily
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string fullDir = Path.Combine(baseDir, saveDirectory);
        
        // Create directory if it doesn't exist
        if (!Directory.Exists(fullDir))
        {
            Directory.CreateDirectory(fullDir);
        }
        
        fullSavePath = Path.Combine(fullDir, fileName);
        Debug.Log($"Beta user data will be saved to: {fullSavePath}");
        
        // Find MockTwitchChat to identify test users
        mockTwitchChat = FindAnyObjectByType<MockTwitchChat>();
        
        // Load existing data if available
        LoadBetaUsers();
    }
    
    void Update()
    {
        // Auto-save based on interval
        if (isDirty && Time.time > nextSaveTime)
        {
            SaveBetaUsers();
            nextSaveTime = Time.time + autoSaveInterval;
        }
    }
    
    void OnApplicationQuit()
    {
        // Save data when application exits
        if (isDirty)
        {
            SaveBetaUsers();
        }
    }
    
    /// <summary>
    /// Check if a username is a test user
    /// </summary>
    private bool IsTestUser(string username)
    {
        // First check if we have a PlayerDataManager reference to use its implementation
        PlayerDataManager playerDataManager = FindAnyObjectByType<PlayerDataManager>();
        if (playerDataManager != null)
        {
            return playerDataManager.IsTestUser(username);
        }
        
        // If no PlayerDataManager, implement our own check
        if (string.IsNullOrEmpty(username)) return false;
        
        string cleanUsername = username.Trim().ToLowerInvariant();
        if (cleanUsername.StartsWith("@"))
        {
            cleanUsername = cleanUsername.Substring(1);
        }
        
        // Check for the special test prefix 
        if (cleanUsername.StartsWith("test_alphasquad_"))
        {
            return true;
        }
        
        // If we have MockTwitchChat, check its test users
        if (mockTwitchChat != null)
        {
            // Access the test usernames from MockTwitchChat using reflection
            var usernamesList = mockTwitchChat.GetType().GetField("mockUsernamesList", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(mockTwitchChat) as List<MockTwitchChat.TestUsername>;
                
            if (usernamesList != null)
            {
                foreach (var testUser in usernamesList)
                {
                    // Use reflection to get the username field
                    var testUsername = testUser.GetType().GetField("username")?.GetValue(testUser) as string;
                    if (!string.IsNullOrEmpty(testUsername) && testUsername.Equals(cleanUsername, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Record a user interaction with any game
    /// </summary>
    /// <param name="username">Username to record</param>
    /// <param name="gameType">Which game they interacted with</param>
    public void RecordUserInteraction(string username, string gameType)
    {
        if (!enableTracking) return;
        
        // Clean up username
        username = username.Trim().ToLowerInvariant();
        if (username.StartsWith("@"))
        {
            username = username.Substring(1);
        }
        
        // Check if the user is a test user
        bool isTestUser = IsTestUser(username);
        
        // Only record first interaction
        if (!betaUsers.ContainsKey(username))
        {
            betaUsers[username] = new UserData(DateTime.UtcNow, isTestUser);
            isDirty = true;
            
            Debug.Log($"Recorded new beta user: {username} in {gameType} at {betaUsers[username].JoinDate} (Test: {isTestUser})");
            
            // Save immediately if autoSaveInterval is 0
            if (autoSaveInterval <= 0)
            {
                SaveBetaUsers();
            }
        }
    }
    
    /// <summary>
    /// Export beta users to CSV (called automatically on interval and on quit)
    /// </summary>
    public void SaveBetaUsers()
    {
        if (!isDirty) return;
        
        try
        {
            StringBuilder sb = new StringBuilder();
            
            // CSV header
            sb.AppendLine("Username,FirstJoinDate,DaysSinceBetaStart,IsTestUser");
            
            // Calculate days since beta start (using first user as approximate beta start)
            DateTime betaStartDate = DateTime.MaxValue;
            foreach (var userData in betaUsers.Values)
            {
                if (userData.JoinDate < betaStartDate)
                {
                    betaStartDate = userData.JoinDate;
                }
            }
            
            // Sort users by join date
            var sortedUsers = new List<KeyValuePair<string, UserData>>(betaUsers);
            sortedUsers.Sort((x, y) => x.Value.JoinDate.CompareTo(y.Value.JoinDate));
            
            // Add each user to CSV
            foreach (var pair in sortedUsers)
            {
                string username = pair.Key;
                UserData userData = pair.Value;
                DateTime joinDate = userData.JoinDate;
                double daysSinceBetaStart = (joinDate - betaStartDate).TotalDays;
                
                sb.AppendLine($"{username},{joinDate:yyyy-MM-dd HH:mm:ss},{daysSinceBetaStart:F2},{userData.IsTestUser.ToString().ToLower()}");
            }
            
            // Write to file
            File.WriteAllText(fullSavePath, sb.ToString());
            
            Debug.Log($"Saved {betaUsers.Count} beta users to {fullSavePath}");
            isDirty = false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error saving beta users: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Load existing beta user data from file
    /// </summary>
    private void LoadBetaUsers()
    {
        if (!File.Exists(fullSavePath))
        {
            Debug.Log("No existing beta user data found. Starting fresh.");
            return;
        }
        
        try
        {
            string[] lines = File.ReadAllLines(fullSavePath);
            betaUsers.Clear();
            
            if (lines.Length <= 1)
            {
                Debug.Log("Beta user file exists but contains no user data.");
                return;
            }
            
            // Check if the header has our expected format
            bool fileHasTestUserFlag = lines[0].Contains("IsTestUser");
            
            // Process each line (skipping header)
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(',');
                
                if (parts.Length >= 2)
                {
                    string username = parts[0];
                    
                    // Only process if username is valid
                    if (!string.IsNullOrEmpty(username))
                    {
                        DateTime joinDate;
                        if (!DateTime.TryParse(parts[1], out joinDate))
                        {
                            joinDate = DateTime.UtcNow; // Fallback if parse fails
                        }
                        
                        // Check if the file contains the IsTestUser flag
                        bool isTestUser = false;
                        if (fileHasTestUserFlag && parts.Length >= 4)
                        {
                            bool.TryParse(parts[3], out isTestUser);
                        }
                        else
                        {
                            // File doesn't have the flag, so determine it now
                            isTestUser = IsTestUser(username);
                        }
                        
                        betaUsers[username] = new UserData(joinDate, isTestUser);
                    }
                }
            }
            
            Debug.Log($"Loaded {betaUsers.Count} beta users from file");
            
            // If the file doesn't have the IsTestUser flag, mark as dirty so we'll save with the updated format
            if (!fileHasTestUserFlag && betaUsers.Count > 0)
            {
                isDirty = true;
                Debug.Log("Beta user file format updated to include test user flag. Will save on next interval.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading beta users: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get total count of beta users
    /// </summary>
    public int GetBetaUserCount()
    {
        return betaUsers.Count;
    }
    
    /// <summary>
    /// Force an immediate save of the beta user data
    /// </summary>
    public void ForceSave()
    {
        SaveBetaUsers();
    }
} 