using UnityEngine;
using System;
using System.Threading;

namespace Shared.Core
{
    /// <summary>
    /// Helper utility for thread management
    /// </summary>
    public static class JobSystemHelper
    {
        // Thread synchronization object
        private static readonly object _lockObject = new object();
        
        // Cached reference to the main thread dispatcher
        private static UnityMainThreadDispatcher _cachedDispatcher;
        
        /// <summary>
        /// Run an action asynchronously on a background thread with proper error handling
        /// </summary>
        /// <param name="action">The action to execute</param>
        public static void RunAsync(Action action)
        {
            if (action == null) return;
            
            ThreadPool.QueueUserWorkItem(_ => 
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    // Use a lock to ensure thread-safe logging
                    lock (_lockObject)
                    {
                        Debug.LogError($"Error in background thread: {e.Message}\n{e.StackTrace}");
                    }
                }
            });
        }
        
        /// <summary>
        /// Run an action on the main thread via the MainThreadDispatcher
        /// </summary>
        /// <param name="action">The action to execute</param>
        public static void RunOnMainThread(Action action)
        {
            if (action == null) return;
            
            try 
            {
                // Use cached dispatcher if available, otherwise find and cache it
                if (_cachedDispatcher == null)
                {
                    _cachedDispatcher = UnityEngine.Object.FindAnyObjectByType<UnityMainThreadDispatcher>();
                    
                    // If still not found, log error
                    if (_cachedDispatcher == null)
                    {
                        lock (_lockObject)
                        {
                            Debug.LogError("Cannot run action on main thread: MainThreadDispatcher not found");
                        }
                        return;
                    }
                }
                
                // Queue action on the dispatcher
                _cachedDispatcher.Enqueue(action);
            }
            catch (Exception e)
            {
                lock (_lockObject)
                {
                    Debug.LogError($"Error scheduling main thread action: {e.Message}");
                    
                    // Reset cached reference if there was an error (might be destroyed)
                    _cachedDispatcher = null;
                }
            }
        }
        
        /// <summary>
        /// Reset the cached dispatcher reference
        /// Call this when changing scenes if needed
        /// </summary>
        public static void ResetDispatcherCache()
        {
            lock (_lockObject)
            {
                _cachedDispatcher = null;
            }
        }
    }
} 