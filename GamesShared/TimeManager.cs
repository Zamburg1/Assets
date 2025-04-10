using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Shared.Core
{
    /// <summary>
    /// Manages time-based operations across games including daily resets and time formatting
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        // Singleton instance with thread safety
        private static TimeManager _instance;
        private static readonly object _lock = new object();
        private static bool _isQuitting = false;
        
        // Timers storage
        private readonly Dictionary<string, TimerInfo> _timers = new Dictionary<string, TimerInfo>();
        private readonly List<string> _timersToRemove = new List<string>();
        
        // Settings
        [SerializeField] private bool logTimerEvents = true;
        
        /// <summary>
        /// Timer information container
        /// </summary>
        private class TimerInfo
        {
            public string Id { get; set; }
            public float Duration { get; set; }
            public float RemainingTime { get; set; }
            public bool IsPaused { get; set; }
            public bool IsCompleted { get; set; }
            public Action OnComplete { get; set; }
            public Action<float> OnTick { get; set; }
            public Coroutine TimerCoroutine { get; set; }
        }
        
        /// <summary>
        /// Singleton instance of TimeManager
        /// </summary>
        public static TimeManager Instance
        {
            get
            {
                if (_isQuitting)
                    return null;
                    
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        // Try to find existing instance
                        _instance = FindObjectOfType<TimeManager>();
                        
                        // Create new instance if none exists
                        if (_instance == null)
                        {
                            GameObject go = new GameObject("TimeManager");
                            _instance = go.AddComponent<TimeManager>();
                            DontDestroyOnLoad(go);
                            Debug.Log("TimeManager created automatically");
                        }
                    }
                    return _instance;
                }
            }
        }
        
        [Header("Time Reset Configuration")]
        [SerializeField, Tooltip("Hour of the day (UTC) when daily resets occur")]
        private int dailyResetHour = 0; // Default: midnight UTC
        
        // Tracks the next reset time
        private DateTime nextDailyResetTime;
        
        // Event for daily reset
        public delegate void DailyResetHandler();
        public event DailyResetHandler OnDailyReset;
        
        // Dictionary to track time-based events
        private System.Collections.Generic.Dictionary<string, DateTime> scheduledEvents = 
            new System.Collections.Generic.Dictionary<string, DateTime>();
        
        // Add these fields to track progress callbacks
        private System.Collections.Generic.Dictionary<string, Action<float>> progressCallbacks = 
            new System.Collections.Generic.Dictionary<string, Action<float>>();
        private System.Collections.Generic.Dictionary<string, float> eventDurations = 
            new System.Collections.Generic.Dictionary<string, float>();
        private Coroutine progressUpdateCoroutine;
        
        private Coroutine dailyResetCoroutine;
        private bool isDestroyed = false;
        
        private void Awake()
        {
            // Ensure singleton behavior
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Calculate next reset time
            CalculateNextDailyReset();
            
            // Start daily reset checker
            if (dailyResetCoroutine != null)
            {
                StopCoroutine(dailyResetCoroutine);
            }
            dailyResetCoroutine = StartCoroutine(DailyResetChecker());
        }
        
        private void OnDestroy()
        {
            isDestroyed = true;
            if (dailyResetCoroutine != null)
            {
                StopCoroutine(dailyResetCoroutine);
                dailyResetCoroutine = null;
            }
            
            // Clean up events to prevent memory leaks
            OnDailyReset = null;
            
            // Clean up all timers on destroy
            StopAllTimers();
            
            // Ensure instance is cleared when the original is destroyed
            if (_instance == this)
            {
                _instance = null;
            }
        }
        
        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }
        
        /// <summary>
        /// Calculate the next daily reset time
        /// </summary>
        private void CalculateNextDailyReset()
        {
            DateTime now = DateTime.UtcNow;
            
            // Set reset time for today at the specified hour
            nextDailyResetTime = new DateTime(
                now.Year, now.Month, now.Day, 
                dailyResetHour, 0, 0, DateTimeKind.Utc);
                
            // If that time has already passed today, set for tomorrow
            if (now >= nextDailyResetTime)
            {
                nextDailyResetTime = nextDailyResetTime.AddDays(1);
            }
            
            Debug.Log($"Next daily reset scheduled for: {nextDailyResetTime} UTC");
        }
        
        /// <summary>
        /// Coroutine to check for daily reset
        /// </summary>
        private IEnumerator DailyResetChecker()
        {
            while (!isDestroyed)
            {
                DateTime now = DateTime.UtcNow;
                
                // Check if we've reached or passed the reset time
                if (now >= nextDailyResetTime)
                {
                    // Trigger reset event
                    Debug.Log("Daily reset triggered");
                    OnDailyReset?.Invoke();
                    
                    // Calculate the next reset time
                    CalculateNextDailyReset();
                }
                
                // Check every minute to reduce overhead
                yield return new WaitForSeconds(60);
            }
        }
        
        /// <summary>
        /// Get the time remaining until the next daily reset
        /// </summary>
        public TimeSpan GetTimeUntilDailyReset()
        {
            TimeSpan timeRemaining = nextDailyResetTime - DateTime.UtcNow;
            return timeRemaining > TimeSpan.Zero ? timeRemaining : TimeSpan.Zero;
        }
        
        /// <summary>
        /// Get a formatted string for the time until daily reset
        /// </summary>
        public string GetFormattedTimeUntilReset()
        {
            TimeSpan timeUntilReset = GetTimeUntilDailyReset();
            
            // Format time into a natural sentence
            if (timeUntilReset.Days > 0)
            {
                return $"Reset in {timeUntilReset.Days} day{(timeUntilReset.Days > 1 ? "s" : "")} and {timeUntilReset.Hours} hour{(timeUntilReset.Hours != 1 ? "s" : "")}.";
            }
            else if (timeUntilReset.Hours > 0 && timeUntilReset.Minutes > 0)
            {
                return $"Reset in {timeUntilReset.Hours} hour{(timeUntilReset.Hours != 1 ? "s" : "")} and {timeUntilReset.Minutes} minute{(timeUntilReset.Minutes != 1 ? "s" : "")}.";
            }
            else if (timeUntilReset.Hours > 0)
            {
                return $"Reset in {timeUntilReset.Hours} hour{(timeUntilReset.Hours != 1 ? "s" : "")}.";
            }
            else if (timeUntilReset.Minutes > 0)
            {
                return $"Reset in {timeUntilReset.Minutes} minute{(timeUntilReset.Minutes != 1 ? "s" : "")}.";
            }
            else
            {
                return "Reset very soon.";
            }
        }
        
        /// <summary>
        /// Schedule an event to occur after a specified duration in seconds
        /// </summary>
        /// <param name="durationInSeconds">Duration in seconds</param>
        /// <param name="onComplete">Action to call when the event is due</param>
        /// <returns>Unique ID for the scheduled event</returns>
        public string ScheduleEvent(float durationInSeconds, Action onComplete)
        {
            // Generate a unique ID
            string eventId = Guid.NewGuid().ToString();
            
            // Schedule the event
            DateTime triggerTime = DateTime.UtcNow.AddSeconds(durationInSeconds);
            scheduledEvents[eventId] = triggerTime;
            
            // Start a coroutine to invoke the completion callback when due
            StartCoroutine(WaitForEventCompletion(eventId, onComplete));
            
            return eventId;
        }
        
        /// <summary>
        /// Check if a scheduled event is due
        /// </summary>
        /// <param name="eventId">ID of the event to check</param>
        /// <param name="removeIfDue">Whether to remove the event if it's due</param>
        /// <returns>True if the event is due, false otherwise</returns>
        public bool IsEventDue(string eventId, bool removeIfDue = false)
        {
            if (!scheduledEvents.TryGetValue(eventId, out DateTime triggerTime))
                return false;
                
            bool isDue = DateTime.UtcNow >= triggerTime;
            
            if (isDue && removeIfDue)
            {
                scheduledEvents.Remove(eventId);
            }
            
            return isDue;
        }
        
        /// <summary>
        /// Get the time remaining until a scheduled event
        /// </summary>
        /// <param name="eventId">ID of the event</param>
        /// <returns>TimeSpan until the event or TimeSpan.Zero if not found or already due</returns>
        public TimeSpan GetTimeUntilEvent(string eventId)
        {
            if (!scheduledEvents.TryGetValue(eventId, out DateTime triggerTime))
                return TimeSpan.Zero;
                
            TimeSpan timeRemaining = triggerTime - DateTime.UtcNow;
            return timeRemaining > TimeSpan.Zero ? timeRemaining : TimeSpan.Zero;
        }
        
        /// <summary>
        /// Set the daily reset hour (UTC)
        /// </summary>
        /// <param name="hour">Hour (0-23)</param>
        public void SetDailyResetHour(int hour)
        {
            dailyResetHour = Mathf.Clamp(hour, 0, 23);
            CalculateNextDailyReset();
        }
        
        /// <summary>
        /// Format a TimeSpan as a human-readable duration
        /// </summary>
        public static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.Days > 0)
            {
                return $"{timeSpan.Days}d {timeSpan.Hours}h {timeSpan.Minutes}m";
            }
            else if (timeSpan.Hours > 0)
            {
                return $"{timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
            }
            else if (timeSpan.Minutes > 0)
            {
                return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
            }
            else
            {
                return $"{timeSpan.Seconds}s";
            }
        }
        
        /// <summary>
        /// Schedule an event to occur after a specified duration in seconds with progress tracking
        /// </summary>
        /// <param name="durationInSeconds">Duration in seconds</param>
        /// <param name="onComplete">Action to call when the event is due</param>
        /// <param name="onProgress">Optional callback receiving remaining time in seconds</param>
        /// <returns>Unique ID for the scheduled event</returns>
        public string ScheduleEventWithProgress(float durationInSeconds, Action onComplete, Action<float> onProgress = null)
        {
            // Generate a unique ID
            string eventId = Guid.NewGuid().ToString();
            
            // Schedule the event
            DateTime triggerTime = DateTime.UtcNow.AddSeconds(durationInSeconds);
            scheduledEvents[eventId] = triggerTime;
            
            // Store duration for percentage calculations
            eventDurations[eventId] = durationInSeconds;
            
            // Register the progress callback if provided
            if (onProgress != null)
            {
                progressCallbacks[eventId] = onProgress;
                
                // Initialize the callback with full time
                onProgress(durationInSeconds);
                
                // Start the progress tracker if it's not already running
                if (progressUpdateCoroutine == null)
                {
                    progressUpdateCoroutine = StartCoroutine(UpdateProgressCallbacks());
                }
            }
            
            // Start a coroutine to invoke the completion callback when due
            StartCoroutine(WaitForEventCompletion(eventId, onComplete));
            
            return eventId;
        }
        
        /// <summary>
        /// Remove a scheduled event and its progress callback
        /// </summary>
        /// <param name="eventId">ID of the event to remove</param>
        public void RemoveScheduledEvent(string eventId)
        {
            scheduledEvents.Remove(eventId);
            progressCallbacks.Remove(eventId);
            eventDurations.Remove(eventId);
        }
        
        /// <summary>
        /// Coroutine to wait for an event to complete and then invoke its callback
        /// </summary>
        private IEnumerator WaitForEventCompletion(string eventId, Action onComplete)
        {
            // Wait until the event is due
            while (!IsEventDue(eventId, false))
            {
                yield return new WaitForSeconds(0.1f);
                
                // Early exit if the event was removed
                if (!scheduledEvents.ContainsKey(eventId))
                {
                    yield break;
                }
            }
            
            // Remove the event
            RemoveScheduledEvent(eventId);
            
            // Invoke the callback
            onComplete?.Invoke();
        }
        
        /// <summary>
        /// Coroutine to update all progress callbacks periodically
        /// </summary>
        private IEnumerator UpdateProgressCallbacks()
        {
            // Reuse this list to avoid allocations
            List<string> keysToRemove = new List<string>(4);
            List<string> eventKeys = new List<string>(8);
            
            while (progressCallbacks.Count > 0 && !isDestroyed)
            {
                // Clear tracking lists (reusing memory)
                keysToRemove.Clear();
                eventKeys.Clear();
                
                // Collect keys once to avoid dictionary modification during iteration
                eventKeys.AddRange(progressCallbacks.Keys);
                
                // Update all registered callbacks
                foreach (string eventId in eventKeys)
                {
                    // Skip if the event has been removed
                    if (!scheduledEvents.ContainsKey(eventId))
                    {
                        keysToRemove.Add(eventId);
                        continue;
                    }
                    
                    // Get the callback - null check for safety
                    if (progressCallbacks.TryGetValue(eventId, out Action<float> callback) && callback != null)
                    {
                        // Calculate time remaining
                        float timeRemaining = (float)GetTimeUntilEvent(eventId).TotalSeconds;
                        
                        // Invoke the callback with the remaining time
                        callback(timeRemaining);
                    }
                }
                
                // Remove any completed/missing events
                foreach (string key in keysToRemove)
                {
                    progressCallbacks.Remove(key);
                }
                
                // Update several times per second for smooth progress
                yield return new WaitForSeconds(0.1f);
            }
            
            progressUpdateCoroutine = null;
        }
        
        /// <summary>
        /// Schedule a timed event with a unique ID
        /// </summary>
        /// <param name="eventId">Unique identifier for the event</param>
        /// <param name="triggerTime">Time when the event should trigger</param>
        public void ScheduleEvent(string eventId, DateTime triggerTime)
        {
            scheduledEvents[eventId] = triggerTime;
        }
        
        private void Update()
        {
            // Update all active timers
            _timersToRemove.Clear();
            
            foreach (var timer in _timers)
            {
                if (timer.Value.IsPaused || timer.Value.IsCompleted)
                    continue;
                    
                timer.Value.RemainingTime -= UnityEngine.Time.deltaTime;
                timer.Value.OnTick?.Invoke(timer.Value.RemainingTime);
                
                if (timer.Value.RemainingTime <= 0)
                {
                    timer.Value.RemainingTime = 0;
                    timer.Value.IsCompleted = true;
                    
                    if (timer.Value.TimerCoroutine != null)
                    {
                        StopCoroutine(timer.Value.TimerCoroutine);
                        timer.Value.TimerCoroutine = null;
                    }
                    
                    try
                    {
                        timer.Value.OnComplete?.Invoke();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"TimeManager: Error in timer completion callback: {e.Message}\n{e.StackTrace}");
                    }
                    
                    _timersToRemove.Add(timer.Key);
                }
            }
            
            // Remove completed timers
            foreach (string timerId in _timersToRemove)
            {
                _timers.Remove(timerId);
                
                if (logTimerEvents)
                {
                    Debug.Log($"TimeManager: Timer '{timerId}' completed and removed");
                }
            }
        }
        
        /// <summary>
        /// Create a new timer with specified duration
        /// </summary>
        /// <param name="id">Unique timer identifier</param>
        /// <param name="duration">Timer duration in seconds</param>
        /// <param name="onComplete">Action to execute when timer completes</param>
        /// <param name="onTick">Action to execute on each timer update</param>
        /// <returns>True if timer was created successfully</returns>
        public bool CreateTimer(string id, float duration, Action onComplete = null, Action<float> onTick = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError("TimeManager: Cannot create timer with empty id");
                return false;
            }
            
            if (duration <= 0)
            {
                Debug.LogError($"TimeManager: Cannot create timer '{id}' with duration <= 0");
                return false;
            }
            
            if (_timers.ContainsKey(id))
            {
                Debug.LogWarning($"TimeManager: Timer '{id}' already exists, stopping existing timer");
                StopTimer(id);
            }
            
            TimerInfo timer = new TimerInfo
            {
                Id = id,
                Duration = duration,
                RemainingTime = duration,
                IsPaused = false,
                IsCompleted = false,
                OnComplete = onComplete,
                OnTick = onTick
            };
            
            _timers[id] = timer;
            
            if (logTimerEvents)
            {
                Debug.Log($"TimeManager: Created timer '{id}' with duration {duration}s");
            }
            
            return true;
        }
        
        /// <summary>
        /// Create a timer that uses a coroutine for more precise timing
        /// </summary>
        /// <param name="id">Unique timer identifier</param>
        /// <param name="duration">Timer duration in seconds</param>
        /// <param name="onComplete">Action to execute when timer completes</param>
        /// <param name="onTick">Action to execute on each timer update</param>
        /// <param name="tickInterval">How often to call onTick in seconds</param>
        /// <returns>True if timer was created successfully</returns>
        public bool CreatePreciseTimer(string id, float duration, Action onComplete = null, 
                                   Action<float> onTick = null, float tickInterval = 0.1f)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError("TimeManager: Cannot create precise timer with empty id");
                return false;
            }
            
            if (duration <= 0)
            {
                Debug.LogError($"TimeManager: Cannot create precise timer '{id}' with duration <= 0");
                return false;
            }
            
            if (_timers.ContainsKey(id))
            {
                Debug.LogWarning($"TimeManager: Timer '{id}' already exists, stopping existing timer");
                StopTimer(id);
            }
            
            TimerInfo timer = new TimerInfo
            {
                Id = id,
                Duration = duration,
                RemainingTime = duration,
                IsPaused = false,
                IsCompleted = false,
                OnComplete = onComplete,
                OnTick = onTick
            };
            
            _timers[id] = timer;
            timer.TimerCoroutine = StartCoroutine(PreciseTimerCoroutine(timer, tickInterval));
            
            if (logTimerEvents)
            {
                Debug.Log($"TimeManager: Created precise timer '{id}' with duration {duration}s");
            }
            
            return true;
        }
        
        /// <summary>
        /// Coroutine for precise timer execution
        /// </summary>
        private IEnumerator PreciseTimerCoroutine(TimerInfo timer, float tickInterval)
        {
            float elapsed = 0;
            WaitForSeconds tickWait = new WaitForSeconds(tickInterval);
            
            while (elapsed < timer.Duration)
            {
                yield return tickWait;
                
                if (timer.IsPaused)
                    continue;
                    
                elapsed += tickInterval;
                timer.RemainingTime = Mathf.Max(0, timer.Duration - elapsed);
                
                try
                {
                    timer.OnTick?.Invoke(timer.RemainingTime);
                }
                catch (Exception e)
                {
                    Debug.LogError($"TimeManager: Error in timer tick callback: {e.Message}\n{e.StackTrace}");
                }
            }
            
            // Ensure we're completely at 0
            timer.RemainingTime = 0;
            timer.IsCompleted = true;
            
            try
            {
                timer.OnComplete?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"TimeManager: Error in timer completion callback: {e.Message}\n{e.StackTrace}");
            }
            
            if (_timers.ContainsKey(timer.Id))
            {
                _timers.Remove(timer.Id);
                
                if (logTimerEvents)
                {
                    Debug.Log($"TimeManager: Precise timer '{timer.Id}' completed and removed");
                }
            }
        }
        
        /// <summary>
        /// Pause a timer
        /// </summary>
        /// <param name="id">Timer identifier</param>
        /// <returns>True if timer was paused successfully</returns>
        public bool PauseTimer(string id)
        {
            if (!_timers.TryGetValue(id, out TimerInfo timer))
            {
                Debug.LogWarning($"TimeManager: Cannot pause timer '{id}', timer not found");
                return false;
            }
            
            if (timer.IsPaused)
            {
                Debug.LogWarning($"TimeManager: Timer '{id}' is already paused");
                return true;
            }
            
            timer.IsPaused = true;
            
            if (logTimerEvents)
            {
                Debug.Log($"TimeManager: Paused timer '{id}' with {timer.RemainingTime}s remaining");
            }
            
            return true;
        }
        
        /// <summary>
        /// Resume a paused timer
        /// </summary>
        /// <param name="id">Timer identifier</param>
        /// <returns>True if timer was resumed successfully</returns>
        public bool ResumeTimer(string id)
        {
            if (!_timers.TryGetValue(id, out TimerInfo timer))
            {
                Debug.LogWarning($"TimeManager: Cannot resume timer '{id}', timer not found");
                return false;
            }
            
            if (!timer.IsPaused)
            {
                Debug.LogWarning($"TimeManager: Timer '{id}' is already running");
                return true;
            }
            
            timer.IsPaused = false;
            
            if (logTimerEvents)
            {
                Debug.Log($"TimeManager: Resumed timer '{id}' with {timer.RemainingTime}s remaining");
            }
            
            return true;
        }
        
        /// <summary>
        /// Stop and remove a timer
        /// </summary>
        /// <param name="id">Timer identifier</param>
        /// <returns>True if timer was stopped successfully</returns>
        public bool StopTimer(string id)
        {
            if (!_timers.TryGetValue(id, out TimerInfo timer))
            {
                return false;
            }
            
            if (timer.TimerCoroutine != null && gameObject.activeInHierarchy)
            {
                StopCoroutine(timer.TimerCoroutine);
                timer.TimerCoroutine = null;
            }
            
            _timers.Remove(id);
            
            if (logTimerEvents)
            {
                Debug.Log($"TimeManager: Stopped timer '{id}'");
            }
            
            return true;
        }
        
        /// <summary>
        /// Stop all active timers
        /// </summary>
        public void StopAllTimers()
        {
            foreach (var timer in _timers.Values)
            {
                if (timer.TimerCoroutine != null && gameObject.activeInHierarchy)
                {
                    StopCoroutine(timer.TimerCoroutine);
                    timer.TimerCoroutine = null;
                }
            }
            
            _timers.Clear();
            
            if (logTimerEvents)
            {
                Debug.Log("TimeManager: Stopped all timers");
            }
        }
        
        /// <summary>
        /// Check if a timer exists
        /// </summary>
        /// <param name="id">Timer identifier</param>
        /// <returns>True if timer exists</returns>
        public bool HasTimer(string id)
        {
            return _timers.ContainsKey(id);
        }
        
        /// <summary>
        /// Get the remaining time for a timer
        /// </summary>
        /// <param name="id">Timer identifier</param>
        /// <returns>Remaining time in seconds, or -1 if timer not found</returns>
        public float GetRemainingTime(string id)
        {
            if (_timers.TryGetValue(id, out TimerInfo timer))
            {
                return timer.RemainingTime;
            }
            
            return -1f;
        }
        
        /// <summary>
        /// Get the progress of a timer (0.0 to 1.0)
        /// </summary>
        /// <param name="id">Timer identifier</param>
        /// <returns>Timer progress (0.0 to 1.0), or -1 if timer not found</returns>
        public float GetTimerProgress(string id)
        {
            if (_timers.TryGetValue(id, out TimerInfo timer))
            {
                return 1f - (timer.RemainingTime / timer.Duration);
            }
            
            return -1f;
        }
    }
} 