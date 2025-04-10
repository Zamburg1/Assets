using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Shared.Core;

public class MultiplierStateManager : MonoBehaviour
{
    [Header("Multiplier States")]
    [SerializeField]
    private List<MultiplierState> multiplierStates = new List<MultiplierState>();
    
    private EntryManager entryManager;
    
    // The currently active multiplier state ID
    private string currentStateId = "";
    
    /// <summary>
    /// Initialize with EntryManager
    /// </summary>
    public void Initialize(EntryManager entryManager)
    {
        this.entryManager = entryManager;
        
        // If no states defined, ensure at least one default state exists
        if (multiplierStates.Count == 0)
        {
            multiplierStates.Add(new MultiplierState("normal", "Normal", 1.0f, 100.0f, Color.white));
        }
        
        // Normalize weights to ensure they sum to 100%
        NormalizeWeights();
        
        // Load or select initial state
        if (PlayerPrefs.HasKey("CurrentMultiplierState"))
        {
            currentStateId = PlayerPrefs.GetString("CurrentMultiplierState");
        }
        else
        {
            SelectRandomMultiplierState();
        }
    }
    
    /// <summary>
    /// Normalize weights to ensure they sum to 100%
    /// </summary>
    private void NormalizeWeights()
    {
        float totalWeight = 0f;
        foreach (var state in multiplierStates)
        {
            totalWeight += state.weight;
        }
        
        if (totalWeight <= 0f)
        {
            Debug.LogError("Total weight of multiplier states is zero or negative!");
            return;
        }
        
        // Scale all weights so they sum to 100
        float scaleFactor = 100f / totalWeight;
        foreach (var state in multiplierStates)
        {
            state.weight *= scaleFactor;
        }
        
        Debug.Log($"Normalized multiplier state weights to sum to 100%: {string.Join(", ", multiplierStates.Select(s => $"{s.name}={s.weight:F1}%").ToArray())}");
    }
    
    /// <summary>
    /// Gets the multiplier for a specific state by ID
    /// </summary>
    public float GetMultiplierForState(string stateId)
    {
        var state = multiplierStates.Find(s => s.id == stateId);
        return state != null ? state.multiplier : 1.0f;
    }
    
    /// <summary>
    /// Gets the current active multiplier value
    /// </summary>
    public float GetCurrentMultiplier()
    {
        return GetMultiplierForState(currentStateId);
    }
    
    /// <summary>
    /// Gets the rarest state (lowest weight)
    /// </summary>
    public string GetRarestState()
    {
        if (multiplierStates.Count == 0)
            return "";
            
        var rarestState = multiplierStates.OrderBy(s => s.weight).FirstOrDefault();
        return rarestState?.id ?? "";
    }
    
    /// <summary>
    /// Selects a random multiplier state weighted by their probability
    /// </summary>
    public string SelectRandomMultiplierState()
    {
        if (multiplierStates.Count == 0)
            return "";
        
        // Using normalized weights (they should sum to 100)
        // Pick a random point between 0 and 100
        float randomPoint = Random.Range(0f, 100f);
        
        // Find which state that point lands on
        float currentWeight = 0f;
        foreach (var state in multiplierStates)
        {
            currentWeight += state.weight;
            if (randomPoint <= currentWeight)
            {
                currentStateId = state.id;
                SaveCurrentState();
                return currentStateId;
            }
        }
        
        // Should never reach here, but just in case
        currentStateId = multiplierStates[0].id;
        SaveCurrentState();
        return currentStateId;
    }
    
    /// <summary>
    /// Gets a MultiplierState by ID
    /// </summary>
    public MultiplierState GetMultiplierState(string stateId)
    {
        return multiplierStates.Find(s => s.id == stateId);
    }
    
    /// <summary>
    /// Gets the current MultiplierState
    /// </summary>
    public MultiplierState GetCurrentMultiplierState()
    {
        return GetMultiplierState(currentStateId);
    }
    
    /// <summary>
    /// Save the current state for persistence
    /// </summary>
    private void SaveCurrentState()
    {
        PlayerPrefs.SetString("CurrentMultiplierState", currentStateId);
        PlayerPrefs.Save();
    }
}