using UnityEngine;
using System;

/// <summary>
/// Represents a multiplier state used by the giveaway game
/// </summary>
[Serializable]
public class MultiplierState
{
    /// <summary>
    /// Unique identifier for this state
    /// </summary>
    [HideInInspector]  // Hide this field in the Inspector
    public string id;
    
    /// <summary>
    /// Display name for this state
    /// </summary>
    public string name;
    
    /// <summary>
    /// Multiplier value (e.g., 1.0 = normal, 2.0 = double entries)
    /// </summary>
    public float multiplier = 1.0f;
    
    /// <summary>
    /// Relative weight for random selection (higher = more likely)
    /// </summary>
    public float weight = 1.0f;
    
    /// <summary>
    /// Color associated with this state for UI display
    /// </summary>
    public Color stateColor = Color.white;
    
    /// <summary>
    /// Creates a new multiplier state
    /// </summary>
    public MultiplierState() { }
    
    /// <summary>
    /// Creates a multiplier state with specific values
    /// </summary>
    public MultiplierState(string id, string name, float multiplier, float weight, Color color)
    {
        this.id = id;
        this.name = name;
        this.multiplier = multiplier;
        this.weight = weight;
        this.stateColor = color;
    }
} 