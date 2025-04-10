using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Shared.Core;

public class LootEntryManager : EntryManagerBase
{
    // Track the current chest selections for this round
    private Dictionary<string, int> currentRoundSelections = new Dictionary<string, int>();
    private HashSet<string> currentRoundParticipants = new HashSet<string>();
    private int currentRoundId = 0;
    private TwitchConnection twitchConnection;
    
    // Maintain counters for each chest to avoid repeated LINQ queries
    private int[] chestSelectionCounts = new int[8];
    
    [Header("Gem Pool Settings")]
    [Tooltip("Minimum gems contributed per unique participant")]
    [Range(1, 20)]
    public int minGemsPerParticipant = 7;
    
    [Tooltip("Maximum gems contributed per unique participant")]
    [Range(1, 20)]
    public int maxGemsPerParticipant = 9;
    
    private int currentGemPool = 0;
    
    // Cache the TwitchConnection reference
    private TwitchConnection _cachedTwitchConnection;
    
    void Awake()
    {
        // Cache the reference in Awake instead of Start
        _cachedTwitchConnection = FindObjectOfType<TwitchConnection>();
        twitchConnection = _cachedTwitchConnection;
    }
    
    public void StartNewRound(int roundId)
    {
        currentRoundId = roundId;
        
        // Clear previous data
        currentRoundSelections.Clear();
        currentRoundParticipants.Clear();
        
        // Reset chest counters
        for (int i = 0; i < chestSelectionCounts.Length; i++)
        {
            chestSelectionCounts[i] = 0;
        }
        
        // Reset gem pool for new round
        currentGemPool = 0;
    }
    
    // Method to clear all selections
    public void ClearSelections()
    {
        currentRoundSelections.Clear();
        currentRoundParticipants.Clear();
        
        // Reset chest counters
        for (int i = 0; i < chestSelectionCounts.Length; i++)
        {
            chestSelectionCounts[i] = 0;
        }
        
        currentGemPool = 0;
        Debug.Log("Cleared all chest selections and reset gem pool");
    }
    
    // Method to add a chest selection for a user
    public bool AddChestSelection(string username, int chestIndex)
    {
        return TryAddSelection(username, chestIndex, currentRoundId);
    }
    
    // Override the abstract method from EntryManagerBase
    public override int AddEntry(string username)
    {
        // This is a delegate to the shared functionality
        // Users need to specify a chest, so this redirects to the zero chest by default
        // This ensures compatibility with any shared code that might call AddEntry
        if (TryAddSelection(username, 0, currentRoundId))
        {
            return entries[username];
        }
        return 0;
    }
    
    // Override the abstract method from EntryManagerBase
    public override void ResetEntries()
    {
        // Clear base class entries
        entries.Clear();
        // Also clear our game-specific selections
        ClearSelections();
    }
    
    public bool TryAddSelection(string username, int chestIndex, int roundId)
    {
        // Sanitize username using the base class method
        username = SanitizeUsername(username);
        
        if (debugLog) Debug.Log($"Attempting to add chest selection for {username} (Round {roundId}, Chest {chestIndex + 1})");
        
        // Verify this is for the current round
        if (roundId != currentRoundId)
        {
            if (debugLog) Debug.Log($"Rejected: Selection for wrong round ID (got {roundId}, current is {currentRoundId})");
            return false;
        }
        
        // Check if user already selected a chest for this round (single lookup)
        if (currentRoundSelections.TryGetValue(username, out int previousSelection))
        {
            if (debugLog) Debug.Log($"Rejected: {username} already selected Chest {previousSelection + 1} for this round");
            
            // Use cached reference
            if (twitchConnection != null)
            {
                twitchConnection.SendChatMessage($"@{username}, you've already selected Chest {previousSelection + 1} for this round!");
            }
            return false;
        }
        
        // Use the shared BetaUserTracker singleton for consistent tracking
        BetaUserTracker.Instance.RecordUserInteraction(username, "LootGame");
        
        // If the user had a previous selection, decrement that chest's counter
        if (currentRoundSelections.TryGetValue(username, out int oldChest) && oldChest >= 0 && oldChest < chestSelectionCounts.Length)
        {
            chestSelectionCounts[oldChest]--;
        }
        
        // Add the selection and track participation
        currentRoundSelections[username] = chestIndex;
        
        // Increment the counter for the selected chest
        if (chestIndex >= 0 && chestIndex < chestSelectionCounts.Length)
        {
            chestSelectionCounts[chestIndex]++;
        }
        
        // Use the base class entries dictionary for consistent tracking
        entries[username] = 1; // Each user gets 1 entry (chest selection) per round
        
        // Each user contributes to the gem pool exactly once per round
        // Use HashSet.Add which returns bool indicating if item was added
        bool isNewParticipant = currentRoundParticipants.Add(username);
        if (isNewParticipant)
        {
            // Add a random number of gems between min and max for this new participant
            int gemsToAdd = Random.Range(minGemsPerParticipant, maxGemsPerParticipant + 1);
            currentGemPool += gemsToAdd;
            
            if (debugLog) Debug.Log($"New participant {username} contributed {gemsToAdd} gems to the pool (now {currentGemPool})");
        }
        
        if (debugLog) Debug.Log($"Chest selection accepted for {username}");
        return true;
    }
    
    // Flag to control debug logging
    private bool debugLog = false;
    
    public List<string> GetWinners(int winningChestIndex)
    {
        // Pre-allocate the list with a reasonable capacity based on participants
        List<string> winners = new List<string>(currentRoundParticipants.Count / 4);
        
        // Manually iterate to avoid LINQ allocations
        foreach (var entry in currentRoundSelections)
        {
            if (entry.Value == winningChestIndex)
            {
                winners.Add(entry.Key);
            }
        }
        
        return winners;
    }
    
    public int GetWinnerCount(int winningChestIndex)
    {
        // Use our optimized counter instead of LINQ query
        if (winningChestIndex >= 0 && winningChestIndex < chestSelectionCounts.Length)
        {
            return chestSelectionCounts[winningChestIndex];
        }
        return 0;
    }
    
    public int GetPotentialGems()
    {
        // Return the total gem pool value
        return currentGemPool;
    }
    
    /// <summary>
    /// Get a list of all unique participants
    /// </summary>
    public override int GetUniqueParticipants()
    {
        return uniqueUsernames.Count;
    }
    
    public int GetTotalSelections()
    {
        return currentRoundSelections.Count;
    }
    
    // Get count of selections for a specific chest
    public int GetSelectionsForChest(int chestIndex)
    {
        // Use our optimized counter instead of LINQ query
        if (chestIndex >= 0 && chestIndex < chestSelectionCounts.Length)
        {
            return chestSelectionCounts[chestIndex];
        }
        return 0;
    }
    
    // Calculate gems per winner based on the winning chest
    public int CalculateGemsPerWinner(int winningChestIndex)
    {
        int winnerCount = GetWinnerCount(winningChestIndex);
        return (winnerCount > 0) ? (currentGemPool / winnerCount) : 0;
    }
    
    // Use GetAllParticipants from the base class where appropriate
    public new List<string> GetAllParticipants()
    {
        // Return all participants to ensure consistency with base class behavior
        return base.GetAllParticipants();
    }
} 