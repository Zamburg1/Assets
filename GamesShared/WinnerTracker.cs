using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.IO;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Tracks winners and gems awarded across all mini-games
/// </summary>
public class WinnerTracker : MonoBehaviour
{
    // Constants for storage and display
    private const string RECENT_WINNERS_KEY = "RecentWinners";
    private const string DAILY_WINNERS_KEY = "DailyWinners";
    private const int MAX_HISTORY_SIZE = 100; // Limit total history size

    [Header("Display Settings")]
    [SerializeField, Tooltip("Maximum number of winners to display in each list")]
    private int maxDisplayedWinners = 5;
    
    [Header("Winner Display UI")]
    [SerializeField, Tooltip("Text components to display recent winners")]
    public UnityEngine.UI.Text[] recentWinnerTextFields = new UnityEngine.UI.Text[5];

    [SerializeField, Tooltip("Text components to display daily top winners")]
    public UnityEngine.UI.Text[] dailyWinnerTextFields = new UnityEngine.UI.Text[5];
    
    [Header("Debug Options")]
    [SerializeField, Tooltip("Show debug messages in console")]
    private bool showDebugMessages = true;
    
    // Structure to track winner data
    [System.Serializable]
    public class WinnerEntry
    {
        public string username;
        public int gems;
        public string gameType;
        public string color = "#FFFFFF"; // Default white
        public DateTime timestamp;
        
        public WinnerEntry(string username, int gems, string gameType, string color = "#FFFFFF")
        {
            this.username = username;
            this.gems = gems;
            this.gameType = gameType;
            this.color = color;
            this.timestamp = DateTime.Now;
        }
    }
    
    // Internal storage for all winners (limited size)
    private List<WinnerEntry> allWinners = new List<WinnerEntry>();
    
    // Cached lists to avoid recalculating frequently
    private List<WinnerEntry> recentWinners = new List<WinnerEntry>();
    private List<WinnerEntry> dailyWinners = new List<WinnerEntry>();
    private bool needsRefresh = true;
    
    // Current date for daily tracking
    private DateTime currentDate;
    
    // Cached TwitchConnection reference
    private TwitchConnection twitchConnectionCache = null;
    
    [System.Serializable]
    private class WinnerList
    {
        public List<WinnerEntry> Winners;
    }
    
    [System.Serializable]
    private class DailyWinnerEntry
    {
        public string Username;
        public int TotalGems;
    }
    
    [System.Serializable]
    private class DailyWinnerList
    {
        public List<DailyWinnerEntry> Winners;
        public string Date;
    }
    
    // Public data structures for external access
    [System.Serializable]
    public class WinEntry
    {
        public string username;
        public string currency;
        public int amount;
        public System.DateTime timestamp;
    }
    
    [System.Serializable]
    public class UserAmountPair
    {
        public string username;
        public int amount;
    }
    
    private void Awake()
    {
        // Initialize the currentDate to today
        currentDate = DateTime.UtcNow.Date;
        LoadWinnerData();
        RefreshWinnerLists();
        
        // Initialize UI displays
        UpdateWinnerDisplays();
    }
    
    /// <summary>
    /// Record a winner with the gems they won
    /// </summary>
    public void RecordWinner(string username, int gems, string gameType = "Default")
    {
        // Get a color for this user based on their Twitch role
        string color = GetUserColor(username);
        
        // Add to the winners list
        allWinners.Add(new WinnerEntry(username, gems, gameType, color));
        
        // Trim history if it exceeds max size
        if (allWinners.Count > MAX_HISTORY_SIZE)
        {
            // Keep only the most recent entries
            allWinners = allWinners.OrderByDescending(w => w.timestamp)
                                   .Take(MAX_HISTORY_SIZE)
                                   .ToList();
        }
        
        needsRefresh = true;
        
        if (showDebugMessages)
        {
            Debug.Log($"[WinnerTracker] Recorded winner: {username}, {gems} gems, in {gameType}");
        }
        
        // Refresh immediately after recording a winner
        RefreshWinnerLists();
        
        // Update UI displays
        UpdateWinnerDisplays();
    }
    
    /// <summary>
    /// Record multiple winners who each won the same amount
    /// </summary>
    public void RecordMultipleWinners(List<string> usernames, int gemsEach, string gameType = "Default")
    {
        if (usernames == null || usernames.Count == 0) return;
        
        foreach (string username in usernames)
        {
            RecordWinner(username, gemsEach, gameType);
        }
        
        if (showDebugMessages)
        {
            Debug.Log($"[WinnerTracker] Recorded {usernames.Count} winners, {gemsEach} gems each, in {gameType}");
        }
    }
    
    /// <summary>
    /// Get the color for a user based on their Twitch role, using any available cached information
    /// </summary>
    private string GetUserColor(string username)
    {
        // Default color (white)
        string defaultColor = "#FFFFFF";
        
        // Check if we have existing color data for this user
        WinnerEntry existingEntry = allWinners.FindLast(w => w.username.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (existingEntry != null && !string.IsNullOrEmpty(existingEntry.color) && existingEntry.color != defaultColor)
        {
            return existingEntry.color;
        }
        
        // Try to find this user in any TwitchConnection instances to get their role color
        // Only look up TwitchConnection if we haven't already cached it
        if (twitchConnectionCache == null)
        {
            twitchConnectionCache = FindAnyObjectByType<TwitchConnection>();
        }
        
        if (twitchConnectionCache != null)
        {
            // If we have a TwitchConnection, try to get color from its recent messages
            // This is a placeholder - we'll use cached colors from recent interactions
            // The actual color will be set when the user interacts with chat
        }
        
        return defaultColor;
    }
    
    /// <summary>
    /// Refresh the cached winner lists (daily and recent)
    /// </summary>
    private void RefreshWinnerLists()
    {
        // Check if we need to reset the daily winners due to date change
        DateTime today = DateTime.UtcNow.Date;
        if (today != currentDate)
        {
            currentDate = today;
            needsRefresh = true;
        }
        
        if (!needsRefresh)
        {
            return; // No need to refresh if nothing has changed
        }
        
        // Update recent winners (most recent first)
        // Avoid ToList() allocations by directly filling the list
        recentWinners.Clear();
        foreach (var winner in allWinners.OrderByDescending(w => w.timestamp).Take(maxDisplayedWinners))
        {
            recentWinners.Add(winner);
        }
        
        // Update daily winners (aggregate gems won today)
        Dictionary<string, WinnerEntry> dailyWinnerDict = new Dictionary<string, WinnerEntry>();
        
        foreach (WinnerEntry winner in allWinners)
        {
            // Only include winners from today
            if (winner.timestamp.Date == today)
            {
                if (dailyWinnerDict.ContainsKey(winner.username))
                {
                    // Update existing entry
                    dailyWinnerDict[winner.username].gems += winner.gems;
                }
                else
                {
                    // Create new entry
                    dailyWinnerDict[winner.username] = new WinnerEntry(
                        winner.username, 
                        winner.gems, 
                        winner.gameType, 
                        winner.color);
                }
            }
        }
        
        // Use clear and add to avoid creating a new list
        dailyWinners.Clear();
        foreach (var winner in dailyWinnerDict.Values)
        {
            dailyWinners.Add(winner);
        }
        
        needsRefresh = false;
        SaveWinnerData();
        
        if (showDebugMessages)
        {
            Debug.Log($"[WinnerTracker] Refreshed winner lists: {recentWinners.Count} recent, {dailyWinners.Count} daily");
        }
    }
    
    /// <summary>
    /// Force a refresh of the winner lists
    /// </summary>
    public void ForceRefresh()
    {
        needsRefresh = true;
        RefreshWinnerLists();
    }
    
    /// <summary>
    /// Updates the UI text fields to display recent and daily winners
    /// </summary>
    public void UpdateWinnerDisplays()
    {
        // Get latest data
        List<Winner> recent = GetRecentWinners();
        List<Winner> daily = GetTopDailyWinners();
        
        // Update recent winner text fields
        for (int i = 0; i < recentWinnerTextFields.Length; i++)
        {
            if (recentWinnerTextFields[i] != null)
            {
                if (i < recent.Count)
                {
                    // Format: Username (Gems)
                    recentWinnerTextFields[i].text = $"{recent[i].username} ({recent[i].gems})";
                    
                    // Try to set color if supported
                    if (!string.IsNullOrEmpty(recent[i].color) && ColorUtility.TryParseHtmlString(recent[i].color, out Color userColor))
                    {
                        recentWinnerTextFields[i].color = userColor;
                    }
                    else
                    {
                        recentWinnerTextFields[i].color = Color.white;
                    }
                }
                else
                {
                    // No winner for this slot
                    recentWinnerTextFields[i].text = "-";
                    recentWinnerTextFields[i].color = Color.white;
                }
            }
        }
        
        // Update daily winner text fields
        for (int i = 0; i < dailyWinnerTextFields.Length; i++)
        {
            if (dailyWinnerTextFields[i] != null)
            {
                if (i < daily.Count)
                {
                    // Format: Username (Gems)
                    dailyWinnerTextFields[i].text = $"{daily[i].username} ({daily[i].gems})";
                    
                    // Try to set color if supported
                    if (!string.IsNullOrEmpty(daily[i].color) && ColorUtility.TryParseHtmlString(daily[i].color, out Color userColor))
                    {
                        dailyWinnerTextFields[i].color = userColor;
                    }
                    else
                    {
                        dailyWinnerTextFields[i].color = Color.white;
                    }
                }
                else
                {
                    // No winner for this slot
                    dailyWinnerTextFields[i].text = "-";
                    dailyWinnerTextFields[i].color = Color.white;
                }
            }
        }
        
        if (showDebugMessages)
        {
            Debug.Log($"[WinnerTracker] Updated UI displays with {recent.Count} recent winners and {daily.Count} daily winners");
        }
    }
    
    /// <summary>
    /// Get a list of recent winners
    /// </summary>
    public List<Winner> GetRecentWinners()
    {
        if (needsRefresh)
        {
            RefreshWinnerLists();
        }
        
        // Pre-allocate the correct size to avoid resizing
        var result = new List<Winner>(recentWinners.Count);
        foreach (var winner in recentWinners)
        {
            result.Add(new Winner(winner.username, winner.gems, winner.color));
        }
        
        return result;
    }
    
    /// <summary>
    /// Get the top winners for today
    /// </summary>
    public List<Winner> GetTopDailyWinners()
    {
        if (needsRefresh)
        {
            RefreshWinnerLists();
        }
        
        // Pre-allocate the correct size to avoid resizing
        int count = Mathf.Min(maxDisplayedWinners, dailyWinners.Count);
        var result = new List<Winner>(count);
        
        // Add winners ordered by gems won (descending)
        foreach (var winner in dailyWinners.OrderByDescending(w => w.gems).Take(maxDisplayedWinners))
        {
            result.Add(new Winner(winner.username, winner.gems, winner.color));
        }
        
        return result;
    }
    
    /// <summary>
    /// Get the total gems awarded today across all games
    /// </summary>
    public int GetTotalGemsAwardedToday()
    {
        return dailyWinners.Sum(w => w.gems);
    }
    
    /// <summary>
    /// Get the number of unique winners today
    /// </summary>
    public int GetUniqueWinnersToday()
    {
        return dailyWinners.Count;
    }
    
    /// <summary>
    /// Reset daily winners without losing history
    /// </summary>
    public void ResetDailyWinners()
    {
        // Keep all winners data but force daily winners to reset
        currentDate = DateTime.UtcNow.Date;
        needsRefresh = true;
        RefreshWinnerLists();
        SaveWinnerData();
    }
    
    /// <summary>
    /// Simplified winner class for external use
    /// </summary>
    public class Winner
    {
        public string username;
        public int gems;
        public string color;
        
        public Winner(string username, int gems, string color)
        {
            this.username = username;
            this.gems = gems;
            this.color = color;
        }
    }
    
    /// <summary>
    /// Saves winner data to disk asynchronously using JobSystemHelper
    /// </summary>
    private void SaveWinnerData()
    {
        // Serialize winner data
        WinnerList winnerListWrapper = new WinnerList
        {
            Winners = allWinners
        };
        
        string json = JsonUtility.ToJson(winnerListWrapper);
        
        // Get file path
        string filePath = GetWinnerFilePath();
        
        // Save asynchronously to avoid blocking the main thread
        Shared.Core.JobSystemHelper.RunAsync(() => 
        {
            try
            {
                // Ensure directory exists
                string directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // Write the file
                File.WriteAllText(filePath, json);
                
                // Log on main thread when complete
                Shared.Core.JobSystemHelper.RunOnMainThread(() => 
                {
                    if (showDebugMessages) 
                    {
                        Debug.Log($"[WinnerTracker] Saved {allWinners.Count} winners to disk");
                    }
                });
            }
            catch (Exception e)
            {
                // Log error on main thread
                Shared.Core.JobSystemHelper.RunOnMainThread(() => 
                {
                    Debug.LogError($"[WinnerTracker] Error saving winner data: {e.Message}");
                });
            }
        });
    }
    
    /// <summary>
    /// Loads winner data from disk asynchronously
    /// </summary>
    private void LoadWinnerData()
    {
        string filePath = GetWinnerFilePath();
        
        // Check if file exists before attempting to load
        if (!File.Exists(filePath))
        {
            if (showDebugMessages) Debug.Log("[WinnerTracker] No saved winner data found");
            return;
        }
        
        // Load data asynchronously
        Shared.Core.JobSystemHelper.RunAsync(() => 
        {
            try
            {
                string json = File.ReadAllText(filePath);
                
                // Parse and apply data on main thread
                Shared.Core.JobSystemHelper.RunOnMainThread(() => 
                {
                    try
                    {
                        WinnerList winnerList = JsonUtility.FromJson<WinnerList>(json);
                        if (winnerList != null && winnerList.Winners != null)
                        {
                            allWinners = winnerList.Winners;
                            needsRefresh = true;
                            
                            if (showDebugMessages) 
                            {
                                Debug.Log($"[WinnerTracker] Loaded {allWinners.Count} winners from disk");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[WinnerTracker] Error parsing winner data: {e.Message}");
                    }
                });
            }
            catch (Exception e)
            {
                // Log error on main thread
                Shared.Core.JobSystemHelper.RunOnMainThread(() => 
                {
                    Debug.LogError($"[WinnerTracker] Error loading winner data: {e.Message}");
                });
            }
        });
    }
    
    /// <summary>
    /// Gets the file path for winner data
    /// </summary>
    private string GetWinnerFilePath()
    {
        // Store in persistent data path
        return Path.Combine(Application.persistentDataPath, "winners.json");
    }

    /// <summary>
    /// Process a batch of winners by removing duplicates and consolidating them by currency
    /// </summary>
    public void ProcessWinners(List<WinEntry> entries)
    {
        if (entries == null || entries.Count == 0) return;
        
        // Use a string builder for log messages to reduce garbage
        StringBuilder logBuilder = null;
        if (showDebugLogs)
        {
            logBuilder = new StringBuilder();
            logBuilder.Append("Processing winners batch: ");
            logBuilder.Append(entries.Count);
            logBuilder.AppendLine(" entries");
        }
        
        // Group winners and amounts by user and currency
        Dictionary<string, Dictionary<string, int>> winnersByUserAndCurrency = new Dictionary<string, Dictionary<string, int>>();
        
        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.username) || string.IsNullOrEmpty(entry.currency))
            {
                if (showDebugLogs) 
                {
                    logBuilder.AppendLine($"- Skipping invalid entry: {entry.username}/{entry.currency}");
                }
                continue;
            }
            
            // Add or update the user's winnings for this currency
            if (!winnersByUserAndCurrency.TryGetValue(entry.username, out var currencyDict))
            {
                currencyDict = new Dictionary<string, int>();
                winnersByUserAndCurrency[entry.username] = currencyDict;
            }
            
            // Add to the currency amount if it exists, otherwise create
            if (!currencyDict.TryGetValue(entry.currency, out int currentAmount))
            {
                currencyDict[entry.currency] = entry.amount;
            }
            else
            {
                currencyDict[entry.currency] = currentAmount + entry.amount;
            }
        }
        
        // Now create consolidated win entries and add them to the queue
        foreach (var userEntry in winnersByUserAndCurrency)
        {
            string username = userEntry.Key;
            var currencyDict = userEntry.Value;
            
            // Directly enumerate the dictionary values without creating a new collection
            foreach (var currencyEntry in currencyDict)
            {
                string currency = currencyEntry.Key;
                int totalAmount = currencyEntry.Value;
                
                // Create the consolidated win entry
                WinEntry consolidatedEntry = new WinEntry
                {
                    username = username,
                    currency = currency,
                    amount = totalAmount,
                    timestamp = DateTime.Now
                };
                
                // Add to the processing queue
                pendingWinEntries.Enqueue(consolidatedEntry);
                
                // Log if enabled
                if (showDebugLogs)
                {
                    logBuilder.AppendLine($"- Added: {username} wins {totalAmount} {currency}");
                }
            }
        }
        
        // Output the combined log message to reduce Debug.Log calls
        if (showDebugLogs && logBuilder.Length > 0)
        {
            Debug.Log(logBuilder.ToString());
        }
    }

    /// <summary>
    /// Get winners for a specific currency, optionally filtering by minimum amount
    /// </summary>
    public List<UserAmountPair> GetWinnersForCurrency(string currency, int minAmount = 0)
    {
        List<UserAmountPair> result = new List<UserAmountPair>();
        
        // Early out if data not loaded
        if (!IsDataLoaded || string.IsNullOrEmpty(currency))
            return result;
        
        // Find the currency dictionary
        if (!winnerData.TryGetValue(currency, out var usersDict))
            return result;
        
        result.Capacity = usersDict.Count; // Pre-allocate capacity
        
        // Directly enumerate the dictionary without creating intermediate collections
        foreach (var userAmount in usersDict)
        {
            if (userAmount.Value >= minAmount)
            {
                result.Add(new UserAmountPair { 
                    username = userAmount.Key, 
                    amount = userAmount.Value 
                });
            }
        }
        
        return result;
    }

    /// <summary>
    /// Export winners to a CSV file with one user per line
    /// </summary>
    public async Task ExportWinnersToCSV(string filePath, string currency = null)
    {
        // Use StringBuilder for efficient string construction
        StringBuilder csvBuilder = new StringBuilder();
        csvBuilder.AppendLine("Username,Currency,Amount");
        
        if (currency != null)
        {
            // Export only the specified currency
            if (winnerData.TryGetValue(currency, out var usersDict))
            {
                foreach (var userAmount in usersDict)
                {
                    csvBuilder.Append(userAmount.Key);
                    csvBuilder.Append(',');
                    csvBuilder.Append(currency);
                    csvBuilder.Append(',');
                    csvBuilder.Append(userAmount.Value);
                    csvBuilder.AppendLine();
                }
            }
        }
        else
        {
            // Export all currencies
            foreach (var currencyDict in winnerData)
            {
                string currencyName = currencyDict.Key;
                var usersDict = currencyDict.Value;
                
                foreach (var userAmount in usersDict)
                {
                    csvBuilder.Append(userAmount.Key);
                    csvBuilder.Append(',');
                    csvBuilder.Append(currencyName);
                    csvBuilder.Append(',');
                    csvBuilder.Append(userAmount.Value);
                    csvBuilder.AppendLine();
                }
            }
        }
        
        // Write to file
        try
        {
            await System.IO.File.WriteAllTextAsync(filePath, csvBuilder.ToString());
            Debug.Log($"Exported {(currency != null ? currency + " " : "")}winners to {filePath}");
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to export winners: {ex.Message}");
            return false;
        }
    }
}