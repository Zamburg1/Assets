using UnityEngine;

namespace Shared.Core
{
    /// <summary>
    /// Standardized game states for all mini-games.
    /// Provides a consistent state machine model across different game types.
    /// </summary>
    public enum GameState
    {
        /// <summary>
        /// Game is initialized but not yet started
        /// </summary>
        WaitingToStart,
        
        /// <summary>
        /// Game is actively running, accepting user input
        /// </summary>
        Active,
        
        /// <summary>
        /// Game round has finished, showing results
        /// </summary>
        Results,
        
        /// <summary>
        /// Game has ended completely
        /// </summary>
        Ended,
        
        /// <summary>
        /// Game is paused (timer stopped)
        /// </summary>
        Paused
    }
    
    /// <summary>
    /// Extension methods for GameState enum
    /// </summary>
    public static class GameStateExtensions
    {
        /// <summary>
        /// Check if the game state allows user interaction
        /// </summary>
        public static bool AllowsUserInteraction(this GameState state)
        {
            return state == GameState.Active;
        }
        
        /// <summary>
        /// Check if the game state is an active state (game in progress)
        /// </summary>
        public static bool IsActiveState(this GameState state)
        {
            return state == GameState.Active || state == GameState.Paused;
        }
        
        /// <summary>
        /// Check if the game state is an end state (results or ended)
        /// </summary>
        public static bool IsEndState(this GameState state)
        {
            return state == GameState.Results || state == GameState.Ended;
        }
    }
} 