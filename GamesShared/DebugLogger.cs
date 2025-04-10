using System;
using UnityEngine;

namespace Alphasquad.GamesShared
{
    /// <summary>
    /// Centralized logging system for all mini-games to ensure consistent debug output
    /// </summary>
    public static class DebugLogger
    {
        /// <summary>
        /// Global debug flag to enable/disable all debug logging
        /// </summary>
        public static bool GlobalDebugEnabled = true;

        /// <summary>
        /// Log a debug message with context
        /// </summary>
        /// <param name="context">The MonoBehaviour context (typically 'this')</param>
        /// <param name="message">Message to log</param>
        /// <param name="debugEnabled">Whether debugging is enabled for this specific context</param>
        public static void Log(MonoBehaviour context, string message, bool debugEnabled = true)
        {
            if (!GlobalDebugEnabled || !debugEnabled) return;
            
            Debug.Log($"[{context.GetType().Name}] {message}");
        }

        /// <summary>
        /// Log a warning message with context
        /// </summary>
        /// <param name="context">The MonoBehaviour context (typically 'this')</param>
        /// <param name="message">Warning message to log</param>
        /// <param name="debugEnabled">Whether debugging is enabled for this specific context</param>
        public static void LogWarning(MonoBehaviour context, string message, bool debugEnabled = true)
        {
            if (!GlobalDebugEnabled || !debugEnabled) return;
            
            Debug.LogWarning($"[{context.GetType().Name}] WARNING: {message}");
        }

        /// <summary>
        /// Log an error message with context
        /// </summary>
        /// <param name="context">The MonoBehaviour context (typically 'this')</param>
        /// <param name="message">Error message to log</param>
        /// <param name="debugEnabled">Whether debugging is enabled for this specific context</param>
        public static void LogError(MonoBehaviour context, string message, bool debugEnabled = true)
        {
            // Errors are always logged regardless of debug settings for safety
            Debug.LogError($"[{context.GetType().Name}] ERROR: {message}");
        }

        /// <summary>
        /// Log an exception with context
        /// </summary>
        /// <param name="context">The MonoBehaviour context (typically 'this')</param>
        /// <param name="exception">The exception to log</param>
        /// <param name="debugEnabled">Whether debugging is enabled for this specific context</param>
        public static void LogException(MonoBehaviour context, Exception exception, bool debugEnabled = true)
        {
            // Exceptions are always logged regardless of debug settings for safety
            Debug.LogError($"[{context.GetType().Name}] EXCEPTION: {exception.Message}\n{exception.StackTrace}");
        }

        /// <summary>
        /// Log initialization related messages
        /// </summary>
        /// <param name="context">The MonoBehaviour context (typically 'this')</param>
        /// <param name="message">Initialization message to log</param>
        /// <param name="debugEnabled">Whether debugging is enabled for this specific context</param>
        public static void LogInitialization(MonoBehaviour context, string message, bool debugEnabled = true)
        {
            if (!GlobalDebugEnabled || !debugEnabled) return;
            
            Debug.Log($"[{context.GetType().Name}] INIT: {message}");
        }

        /// <summary>
        /// Log game state changes
        /// </summary>
        /// <param name="context">The MonoBehaviour context (typically 'this')</param>
        /// <param name="oldState">Previous game state</param>
        /// <param name="newState">New game state</param>
        /// <param name="debugEnabled">Whether debugging is enabled for this specific context</param>
        public static void LogStateChange(MonoBehaviour context, Enum oldState, Enum newState, bool debugEnabled = true)
        {
            if (!GlobalDebugEnabled || !debugEnabled) return;
            
            Debug.Log($"[{context.GetType().Name}] STATE CHANGE: {oldState} → {newState}");
        }

        /// <summary>
        /// Log timer operations
        /// </summary>
        /// <param name="context">The MonoBehaviour context (typically 'this')</param>
        /// <param name="timerName">Name or identifier of the timer</param>
        /// <param name="operation">Operation being performed (start/stop/tick)</param>
        /// <param name="duration">Duration of the timer (if applicable)</param>
        /// <param name="debugEnabled">Whether debugging is enabled for this specific context</param>
        public static void LogTimer(MonoBehaviour context, string timerName, string operation, float duration = 0f, bool debugEnabled = true)
        {
            if (!GlobalDebugEnabled || !debugEnabled) return;
            
            string message = duration > 0 
                ? $"TIMER {operation}: {timerName} ({duration:F2}s)" 
                : $"TIMER {operation}: {timerName}";
                
            Debug.Log($"[{context.GetType().Name}] {message}");
        }

        /// <summary>
        /// Log command processing
        /// </summary>
        /// <param name="context">The MonoBehaviour context (typically 'this')</param>
        /// <param name="command">Command being processed</param>
        /// <param name="user">User who sent the command (if applicable)</param>
        /// <param name="debugEnabled">Whether debugging is enabled for this specific context</param>
        public static void LogCommand(MonoBehaviour context, string command, string user = null, bool debugEnabled = true)
        {
            if (!GlobalDebugEnabled || !debugEnabled) return;
            
            string message = string.IsNullOrEmpty(user) 
                ? $"COMMAND: {command}" 
                : $"COMMAND from {user}: {command}";
                
            Debug.Log($"[{context.GetType().Name}] {message}");
        }

        /// <summary>
        /// Log user interaction data
        /// </summary>
        /// <param name="context">The MonoBehaviour context (typically 'this')</param>
        /// <param name="userId">ID of the user</param>
        /// <param name="action">Action performed</param>
        /// <param name="data">Additional data about the action</param>
        /// <param name="debugEnabled">Whether debugging is enabled for this specific context</param>
        public static void LogUserInteraction(MonoBehaviour context, string userId, string action, string data = null, bool debugEnabled = true)
        {
            if (!GlobalDebugEnabled || !debugEnabled) return;
            
            string message = string.IsNullOrEmpty(data) 
                ? $"USER {userId}: {action}" 
                : $"USER {userId}: {action} - {data}";
                
            Debug.Log($"[{context.GetType().Name}] {message}");
        }
    }
} 