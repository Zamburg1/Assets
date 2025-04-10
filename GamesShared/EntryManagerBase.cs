using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Base class for entry managers across different games
/// </summary>
public abstract class EntryManagerBase : MonoBehaviour
{
    /// <summary>
    /// Dictionary to store entries by username
    /// </summary>
    protected Dictionary<string, int> entries = new Dictionary<string, int>();
    
    /// <summary>
    /// Cache for sanitized usernames to avoid repeated string operations
    /// </summary>
    private Dictionary<string, string> sanitizedUsernameCache = new Dictionary<string, string>(100);
    
    /// <summary>
    /// Flag indicating if data is dirty and cache needs updating
    /// </summary>
    protected bool isDirty = false;
    
    /// <summary>
    /// Timestamp of last update for throttling
    /// </summary>
    protected float lastUpdateTime = 0f;
    
    /// <summary>
    /// Default update interval in seconds
    /// </summary>
    [SerializeField] protected float updateInterval = 0.5f;
    
    /// <summary>
    /// Flag to force immediate updates
    /// </summary>
    protected bool forceUpdate = false;
    
    /// <summary>
    /// Flag to enable debug logging
    /// </summary>
    [Header("Debug Settings")]
    [SerializeField] protected bool debugMode = false;
    
    /// <summary>
    /// Event fired when a user contributes gems
    /// </summary>
    public event Action<string, int> OnGemContribution;
    
    /// <summary>
    /// Add an entry for the specified username. Returns the total entries after adding.
    /// </summary>
    public abstract int AddEntry(string username);
    
    /// <summary>
    /// Reset all entries
    /// </summary>
    public abstract void ResetEntries();
    
    /// <summary>
    /// Gets the total number of entries across all users
    /// </summary>
    public virtual int GetTotalEntryCount()
    {
        int total = 0;
        foreach (var entry in entries)
        {
            total += entry.Value;
        }
        return total;
    }
    
    /// <summary>
    /// Gets all usernames who have entered
    /// </summary>
    public virtual List<string> GetAllParticipants()
    {
        return new List<string>(entries.Keys);
    }
    
    /// <summary>
    /// Gets the number of unique participants
    /// </summary>
    public virtual int GetUniqueParticipants()
    {
        return entries.Count;
    }
    
    /// <summary>
    /// Initialize cache management and timestamp tracking
    /// </summary>
    protected virtual void InitializeCacheManagement()
    {
        lastUpdateTime = Time.time;
        isDirty = false;
        forceUpdate = false;
        sanitizedUsernameCache.Clear();
    }
    
    /// <summary>
    /// Force an immediate update of cached data
    /// </summary>
    public virtual void ForceUpdate()
    {
        forceUpdate = true;
        UpdateCache();
    }
    
    /// <summary>
    /// Mark data as changed, requiring cache update
    /// </summary>
    protected virtual void MarkDataChanged()
    {
        isDirty = true;
        lastUpdateTime = Time.time;
    }
    
    /// <summary>
    /// Update cache if enough time has passed or forced
    /// </summary>
    protected virtual void UpdateCache()
    {
        float currentTime = Time.time;
        
        // Check if enough time has passed since last update
        if (currentTime - lastUpdateTime < updateInterval && !forceUpdate)
        {
            return;
        }
        
        // If data has changed and cache is dirty, update it
        if (isDirty)
        {
            RefreshCache();
            isDirty = false;
        }
        
        // Record time of this update
        lastUpdateTime = currentTime;
        forceUpdate = false;
    }
    
    /// <summary>
    /// Game-specific implementation of cache refresh
    /// Override this in derived classes to update specific caches
    /// </summary>
    protected virtual void RefreshCache()
    {
        // Base implementation does nothing
        // Override in derived classes to update specific data caches
    }
    
    /// <summary>
    /// Throttled update method to be called from Update
    /// </summary>
    protected virtual void UpdateUserInteractionData()
    {
        UpdateCache();
    }
    
    /// <summary>
    /// Sanitize username by removing special characters and normalizing case
    /// Cached to reduce allocations
    /// </summary>
    /// <param name="username">Raw username</param>
    /// <returns>Sanitized username</returns>
    protected virtual string SanitizeUsername(string username)
    {
        if (string.IsNullOrEmpty(username))
            return string.Empty;
                
        // Check cache first
        if (sanitizedUsernameCache.TryGetValue(username, out string sanitized))
            return sanitized;
        
        // Only replace non-alphanumeric characters and convert to lowercase
        sanitized = Regex.Replace(username.ToLowerInvariant(), @"[^a-z0-9_]", "");
        
        // Cache the result for future use
        if (sanitizedUsernameCache.Count > 1000) // Prevent unlimited growth
        {
            // Clear half the cache when it gets too large
            var oldestEntries = sanitizedUsernameCache.Keys.Take(500).ToList();
            foreach (var key in oldestEntries)
            {
                sanitizedUsernameCache.Remove(key);
            }
        }
        
        sanitizedUsernameCache[username] = sanitized;
        return sanitized;
    }
    
    /// <summary>
    /// Try to add an entry, returning true if successful
    /// </summary>
    /// <param name="username">Username to add</param>
    /// <returns>True if entry was added successfully</returns>
    public virtual bool TryAddEntry(string username)
    {
        username = SanitizeUsername(username);
        
        if (string.IsNullOrEmpty(username))
            return false;
            
        AddEntry(username);
        MarkDataChanged();
        
        if (debugMode)
            Debug.Log($"[{GetType().Name}] Added entry for user: {username}");
            
        return true;
    }
    
    /// <summary>
    /// Validate gem amount, ensuring it's within acceptable range
    /// </summary>
    /// <param name="gems">Gem amount to validate</param>
    /// <returns>Valid gem amount</returns>
    protected virtual int ValidateGemAmount(int gems)
    {
        if (gems <= 0)
            return 10; // Default gem amount
                
        return Mathf.Clamp(gems, 1, 1000); // Clamp between 1 and 1000
    }
    
    /// <summary>
    /// Track gem contribution for a user
    /// </summary>
    /// <param name="username">Username</param>
    /// <param name="gems">Gem amount</param>
    protected virtual void TrackGemContribution(string username, int gems)
    {
        if (gems <= 0)
            return; // Ignore if gems are not positive
        
        string sanitizedName = SanitizeUsername(username);
        
        if (entries.ContainsKey(sanitizedName))
            entries[sanitizedName] += gems;
        else
            entries[sanitizedName] = gems;
        
        // Notify listeners
        if (OnGemContribution != null)
            OnGemContribution(sanitizedName, gems);
        
        if (debugMode)
            Debug.Log($"Gem contribution: {sanitizedName} contributed {gems} gems. Total: {entries[sanitizedName]}");
            
        MarkDataChanged();
    }
    
    /// <summary>
    /// Get total gems contributed by all users
    /// </summary>
    /// <returns>Total gem count</returns>
    public virtual int GetTotalGems()
    {
        return entries.Values.Sum();
    }
    
    /// <summary>
    /// Get total gems contributed by a specific user
    /// </summary>
    /// <param name="username">Username</param>
    /// <returns>Gem count for user</returns>
    public virtual int GetUserGems(string username)
    {
        string sanitizedName = SanitizeUsername(username);
        
        if (entries.TryGetValue(sanitizedName, out int gems))
            return gems;
        
        return 0;
    }
    
    /// <summary>
    /// Reset gem contributions
    /// </summary>
    public virtual void ResetGemContributions()
    {
        entries.Clear();
        MarkDataChanged();
    }
    
    /// <summary>
    /// Get participants who have submitted entries
    /// </summary>
    /// <returns>List of participant usernames</returns>
    public virtual List<string> GetParticipants()
    {
        return new List<string>(entries.Keys);
    }
    
    /// <summary>
    /// Check if a user is a participant
    /// </summary>
    /// <param name="username">Username to check</param>
    /// <returns>True if user is a participant</returns>
    public virtual bool IsParticipant(string username)
    {
        string sanitizedName = SanitizeUsername(username);
        return entries.ContainsKey(sanitizedName);
    }
    
    /// <summary>
    /// Get top gem contributors
    /// </summary>
    /// <param name="count">Number of contributors to return</param>
    /// <returns>Dictionary of usernames and gem counts</returns>
    public virtual Dictionary<string, int> GetTopContributors(int count = 5)
    {
        return entries
            .OrderByDescending(kvp => kvp.Value)
            .Take(count)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
    
    /// <summary>
    /// Add a participant to the list
    /// </summary>
    /// <param name="username">Username to add</param>
    protected virtual void AddParticipant(string username)
    {
        username = SanitizeUsername(username);
        
        if (!entries.ContainsKey(username))
            entries[username] = 1;
        else
            entries[username]++;
            
        MarkDataChanged();
    }
    
    /// <summary>
    /// Calculate weights for all entries
    /// </summary>
    /// <returns>Dictionary of usernames and their weight</returns>
    protected virtual Dictionary<string, float> CalculateWeights()
    {
        int total = GetTotalEntryCount();
        Dictionary<string, float> weights = new Dictionary<string, float>();
        
        if (total > 0)
        {
            foreach (var entry in entries)
            {
                weights[entry.Key] = (float)entry.Value / total;
            }
        }
        
        return weights;
    }
    
    protected virtual void OnDisable()
    {
        // Clean up events
        if (OnGemContribution != null)
            OnGemContribution = null;
    }
} 