using UnityEngine;
using System.Collections.Generic;
using Shared.Core;

/// <summary>
/// Main game manager that coordinates communication between different mini-games
/// All games run simultaneously
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("Connected Games")]
    [Tooltip("All mini-games that are connected to the system")]
    public List<MiniGameController> connectedGames = new List<MiniGameController>();
    
    [Header("Settings")]
    [Tooltip("Twitch connection for chat commands")]
    public TwitchConnection twitchConnection;
    
    void Start()
    {
        // Make sure that we have at least one game
        if (connectedGames.Count == 0)
        {
            Debug.LogWarning("No mini-games have been connected to the GameManager.");
        }
        
        // Setup the Twitch connection if available
        if (twitchConnection != null)
        {
            // Subscribe to chat message events
            twitchConnection.Initialize(null);
            twitchConnection.OnMessageReceived += ProcessChatMessage;
        }
        
        // All games should already be active
        foreach (var game in connectedGames)
        {
            Debug.Log($"Connected to game: {game.GetType().Name}");
        }
    }
    
    void OnDestroy()
    {
        // Clean up event subscriptions
        if (twitchConnection != null)
        {
            twitchConnection.OnMessageReceived -= ProcessChatMessage;
        }
    }
    
    /// <summary>
    /// Process chat messages and route them to all games
    /// Each game will handle only the commands relevant to it
    /// </summary>
    private void ProcessChatMessage(string username, string message)
    {
        // Pass the message to all games
        foreach (var game in connectedGames)
        {
            if (game != null && game.gameObject.activeInHierarchy)
            {
                game.ProcessChatMessage(username, message);
            }
        }
    }
    
    /// <summary>
    /// Gets a specific game by type
    /// </summary>
    public T GetGame<T>() where T : MiniGameController
    {
        foreach (var game in connectedGames)
        {
            if (game is T typedGame)
            {
                return typedGame;
            }
        }
        return null;
    }
} 