using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Shared.Core;

public class EntryManager : EntryManagerBase
{
    [Header("References")]
    public GiveawayController controller;
    public MultiplierStateManager multiplierStateManager;
    private TicketManager ticketManager;
    
    // Current multiplier state
    private string currentMultiplierState = "";
    
    // Save and load related fields
    private const string ENTRIES_SAVE_KEY = "GiveawayEntries";
    private const string MULTIPLIER_STATE_KEY = "CurrentMultiplierState";
    
    protected override void OnDisable()
    {
        base.OnDisable();
        
        // Additional cleanup code if any...
    }
    
    void OnDestroy() 
    {
        // Final save attempt on destruction
        SaveEntries();
    }
    
    public void Initialize(GiveawayController controller)
    {
        this.controller = controller;
        
        // Get reference to the MultiplierStateManager
        multiplierStateManager = controller.multiplierStateManager;
        
        // Get reference to the TicketManager
        ticketManager = FindAnyObjectByType<TicketManager>();
        if (ticketManager == null)
        {
            Debug.LogWarning("TicketManager not found, tickets will not be enforced");
        }
        
        LoadSavedEntries();
        
        // Ensure we have a valid multiplier state
        if (string.IsNullOrEmpty(currentMultiplierState))
        {
            SelectMultiplierState();
        }
    }
    
    /// <summary>
    /// Adds an entry for the specified username
    /// </summary>
    public override int AddEntry(string username)
    {
        // Use the base class SanitizeUsername
        username = SanitizeUsername(username);
        
        // Use the shared BetaUserTracker for consistent tracking
        BetaUserTracker.Instance.RecordUserInteraction(username, "GiveawayGame");
        
        // Check if we have a ticket manager and if the user has enough tickets
        if (ticketManager != null && !ticketManager.UseTicket(username))
        {
            Debug.Log($"Entry from {username} rejected - not enough tickets");
            return 0;
        }
        
        // Get the current entry count for this user (default to 0 if they don't exist yet)
        int currentEntries = 0;
        if (entries.ContainsKey(username))
        {
            currentEntries = entries[username];
        }
        
        // Calculate multiplier based on the current state
        float multiplier = multiplierStateManager.GetMultiplierForState(currentMultiplierState);
        
        // Add one new entry with the current multiplier applied
        int newEntries = Mathf.CeilToInt(1 * multiplier);
        
        // Update the entries dictionary
        entries[username] = currentEntries + newEntries;
        
        // Save the entries
        SaveEntries();
        
        Debug.Log($"Added {newEntries} entries for {username} (multiplier: {multiplier}x, total: {entries[username]})");
        
        // Return the total entries for this user
        return entries[username];
    }
    
    /// <summary>
    /// Gets the number of entries for a specific user
    /// </summary>
    public int GetPersonalEntryCount(string username)
    {
        // Use the base class SanitizeUsername for consistency
        username = SanitizeUsername(username);
        
        // Use TryGetValue for more efficient dictionary access
        if (entries.TryGetValue(username, out int count))
        {
            return count;
        }
        return 0;
    }
    
    /// <summary>
    /// Gets the total number of entries across all users
    /// </summary>
    public override int GetTotalEntryCount()
    {
        return base.GetTotalEntryCount();
    }
    
    /// <summary>
    /// Resets all entries
    /// </summary>
    public override void ResetEntries()
    {
        entries.Clear();
        SelectMultiplierState();
        SaveEntries();
    }
    
    /// <summary>
    /// Selects a random winner. Returns tuple of (username, gems awarded)
    /// </summary>
    public (string, int) SelectWinner()
    {
        // If no entries, return empty string
        if (entries.Count == 0)
        {
            return ("", 0);
        }
        
        // Calculate total entries
        int totalEntries = GetTotalEntryCount();
        
        // Pick a random number between 1 and total entries
        int winningNumber = Random.Range(1, totalEntries + 1);
        
        // Loop through to find the corresponding entry
        int runningTotal = 0;
        string winner = "";
        
        foreach (var entry in entries)
        {
            runningTotal += entry.Value;
            if (runningTotal >= winningNumber)
            {
                winner = entry.Key;
                break;
            }
        }
        
        // Calculate gems to award
        int gems = CalculateGems();
        
        // Record winner in WinnerTracker
        var winnerTracker = FindAnyObjectByType<WinnerTracker>();
        if (winnerTracker != null)
        {
            winnerTracker.RecordWinner(winner, gems);
        }
        
        return (winner, gems);
    }
    
    /// <summary>
    /// Gets the potential gems that would be awarded to a winner
    /// </summary>
    public int GetPotentialGems()
    {
        return CalculateGems();
    }
    
    /// <summary>
    /// Calculate gems for the current state
    /// </summary>
    private int CalculateGems()
    {
        // Count of unique participants
        int uniqueParticipants = entries.Count;
        
        // 1 gem per unique participant
        int gems = uniqueParticipants;
        
        // Apply current multiplier state
        float multiplier = multiplierStateManager.GetMultiplierForState(currentMultiplierState);
        gems = Mathf.CeilToInt(gems * multiplier);
        
        return gems;
    }
    
    /// <summary>
    /// Selects a random multiplier state
    /// </summary>
    public void SelectMultiplierState()
    {
        if (multiplierStateManager == null) return;
        
        currentMultiplierState = multiplierStateManager.SelectRandomMultiplierState();
        Debug.Log($"Selected multiplier state: {currentMultiplierState} ({multiplierStateManager.GetMultiplierForState(currentMultiplierState)}x)");
    }
    
    /// <summary>
    /// Gets the current multiplier state
    /// </summary>
    public MultiplierState GetCurrentMultiplierState()
    {
        if (multiplierStateManager == null || string.IsNullOrEmpty(currentMultiplierState))
        {
            // Return a default state if no valid state exists
            return new MultiplierState("default", "Normal", 1.0f, 1.0f, Color.white);
        }
        
        return multiplierStateManager.GetMultiplierState(currentMultiplierState);
    }
    
    #region Data Persistence
    
    private void SaveEntries()
    {
        // Use SerializationHelper to save the entries dictionary
        SerializationHelper.SaveDictionaryToPrefs(entries, ENTRIES_SAVE_KEY);
        
        // Save the current multiplier state separately
        PlayerPrefs.SetString(MULTIPLIER_STATE_KEY, currentMultiplierState);
        PlayerPrefs.Save();
        
        Debug.Log($"Saved {entries.Count} giveaway entries using SerializationHelper");
    }
    
    private void LoadSavedEntries()
    {
        // Clear existing entries
        entries.Clear();
        
        // Use SerializationHelper to load the entries dictionary
        var loadedEntries = SerializationHelper.LoadDictionaryFromPrefs<string, int>(ENTRIES_SAVE_KEY);
        
        // Copy the loaded entries to our dictionary
        if (loadedEntries != null)
        {
            foreach (var entry in loadedEntries)
            {
                entries[entry.Key] = entry.Value;
            }
            Debug.Log($"Loaded {entries.Count} giveaway entries using SerializationHelper");
        }
        
        // Load the current multiplier state
        if (PlayerPrefs.HasKey(MULTIPLIER_STATE_KEY))
        {
            currentMultiplierState = PlayerPrefs.GetString(MULTIPLIER_STATE_KEY);
        }
    }
    
    #endregion
}