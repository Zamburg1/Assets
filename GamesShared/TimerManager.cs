using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Alphasquad.GamesShared
{
    /// <summary>
    /// A timer reference object that tracks a specific timer instance
    /// </summary>
    public class TimerReference
    {
        public string Id { get; private set; }
        public float Duration { get; private set; }
        public float RemainingTime { get; internal set; }
        public bool IsActive { get; internal set; }
        public Action OnComplete { get; private set; }
        public Action<float> OnProgress { get; private set; }

        public TimerReference(string id, float duration, Action onComplete, Action<float> onProgress = null)
        {
            Id = id;
            Duration = duration;
            RemainingTime = duration;
            IsActive = true;
            OnComplete = onComplete;
            OnProgress = onProgress;
        }

        /// <summary>
        /// Cancels the timer and prevents the completion callback from firing
        /// </summary>
        public void Cancel()
        {
            IsActive = false;
        }
    }

    /// <summary>
    /// Centralized manager for all game timers
    /// </summary>
    public class TimerManager : MonoBehaviour
    {
        private static TimerManager _instance;
        public static TimerManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<TimerManager>();
                    if (_instance == null)
                    {
                        GameObject timerManagerObject = new GameObject("TimerManager");
                        _instance = timerManagerObject.AddComponent<TimerManager>();
                        DontDestroyOnLoad(timerManagerObject);
                    }
                }
                return _instance;
            }
        }

        private Dictionary<string, TimerReference> activeTimers = new Dictionary<string, TimerReference>();
        private Dictionary<string, Coroutine> timerCoroutines = new Dictionary<string, Coroutine>();

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Starts a new timer with the specified duration and callbacks
        /// </summary>
        /// <param name="duration">Duration of the timer in seconds</param>
        /// <param name="onComplete">Callback invoked when timer completes</param>
        /// <param name="onProgress">Optional callback invoked with progress information</param>
        /// <returns>Timer identifier string</returns>
        public string StartTimer(float duration, Action onComplete, Action<float> onProgress = null)
        {
            string timerId = Guid.NewGuid().ToString();
            return StartTimerWithId(timerId, duration, onComplete, onProgress);
        }

        /// <summary>
        /// Starts a timer with a specific ID, or restarts it if it already exists
        /// </summary>
        /// <param name="timerId">ID for the timer</param>
        /// <param name="duration">Duration of the timer in seconds</param>
        /// <param name="onComplete">Callback invoked when timer completes</param>
        /// <param name="onProgress">Optional callback invoked with progress information</param>
        /// <returns>Timer identifier string</returns>
        public string StartTimerWithId(string timerId, float duration, Action onComplete, Action<float> onProgress = null)
        {
            CancelTimer(timerId);

            TimerReference timerRef = new TimerReference(timerId, duration, onComplete, onProgress);
            activeTimers[timerId] = timerRef;
            
            Coroutine timerCoroutine = StartCoroutine(TimerCoroutine(timerRef));
            timerCoroutines[timerId] = timerCoroutine;
            
            DebugLogger.LogTimer(this, $"Started timer: {timerId}, duration: {duration}s", "start", duration);
            return timerId;
        }

        /// <summary>
        /// Cancels a timer identified by its ID
        /// </summary>
        /// <param name="timerId">ID of the timer to cancel</param>
        /// <returns>True if a timer was found and cancelled</returns>
        public bool CancelTimer(string timerId)
        {
            if (string.IsNullOrEmpty(timerId))
                return false;

            if (activeTimers.TryGetValue(timerId, out TimerReference timerRef))
            {
                timerRef.Cancel();

                if (timerCoroutines.TryGetValue(timerId, out Coroutine coroutine))
                {
                    StopCoroutine(coroutine);
                    timerCoroutines.Remove(timerId);
                }

                activeTimers.Remove(timerId);
                DebugLogger.LogTimer(this, $"Cancelled timer: {timerId}", "cancel", 0);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the remaining time for a timer
        /// </summary>
        /// <param name="timerId">ID of the timer</param>
        /// <returns>Remaining time in seconds, or -1 if timer not found</returns>
        public float GetRemainingTime(string timerId)
        {
            if (activeTimers.TryGetValue(timerId, out TimerReference timerRef))
            {
                return timerRef.RemainingTime;
            }
            return -1f;
        }

        /// <summary>
        /// Checks if a timer with the specified ID is active
        /// </summary>
        /// <param name="timerId">ID of the timer to check</param>
        /// <returns>True if the timer exists and is active</returns>
        public bool IsTimerActive(string timerId)
        {
            return activeTimers.TryGetValue(timerId, out TimerReference timerRef) && timerRef.IsActive;
        }

        /// <summary>
        /// Gets all active timer IDs
        /// </summary>
        /// <returns>Array of active timer IDs</returns>
        public string[] GetActiveTimerIds()
        {
            string[] timerIds = new string[activeTimers.Count];
            activeTimers.Keys.CopyTo(timerIds, 0);
            return timerIds;
        }

        /// <summary>
        /// Coroutine that runs the timer and invokes callbacks
        /// </summary>
        private IEnumerator TimerCoroutine(TimerReference timer)
        {
            float elapsedTime = 0f;
            
            while (elapsedTime < timer.Duration && timer.IsActive)
            {
                timer.RemainingTime = timer.Duration - elapsedTime;
                
                // Invoke progress callback if provided
                timer.OnProgress?.Invoke(timer.RemainingTime);
                
                yield return null;
                elapsedTime += Time.deltaTime;
            }

            // If timer is still active (wasn't cancelled), invoke completion callback
            if (timer.IsActive)
            {
                timer.RemainingTime = 0f;
                timer.IsActive = false;
                activeTimers.Remove(timer.Id);
                timerCoroutines.Remove(timer.Id);
                
                DebugLogger.LogTimer(this, $"Timer completed: {timer.Id}", "complete", 0);
                timer.OnComplete?.Invoke();
            }
        }

        /// <summary>
        /// Cancels all active timers
        /// </summary>
        public void CancelAllTimers()
        {
            string[] timerIds = GetActiveTimerIds();
            foreach (string timerId in timerIds)
            {
                CancelTimer(timerId);
            }
            DebugLogger.LogTimer(this, $"Cancelled all timers ({timerIds.Length} total)", "cancel", 0);
        }

        private void OnDestroy()
        {
            CancelAllTimers();
        }
    }
} 