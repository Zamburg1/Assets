using System;
using System.Collections.Generic;
using UnityEngine;
using Shared.Core;

namespace Shared.Core
{
    /// <summary>
    /// Base class for managing tickets across different games
    /// </summary>
    public abstract class TicketManagerBase : MonoBehaviour
    {
        [Header("Ticket Configuration")]
        [SerializeField, Tooltip("Max tickets per user per day")]
        protected int maxTicketsPerUser = 1000;
        
        [Header("References")]
        [SerializeField] protected TwitchConnection twitchConnection;
        
        // Database for tickets used by username
        protected Dictionary<string, int> ticketsUsed = new Dictionary<string, int>();
        
        // Reference to time manager
        protected TimeManager timeManager;
        
        // Track the next reset time
        protected DateTime nextResetTime;
        
        protected bool isDestroyed = false;
        
        // Save key for tickets - should be overridden in derived classes
        protected virtual string TicketsSaveKey => "Tickets";
        
        protected virtual void Awake()
        {
            // Get reference to the TimeManager
            timeManager = TimeManager.Instance;
            
            // Load saved tickets
            LoadTickets();
            
            // Check if we need to reset tickets
            CheckResetTickets();
            
            // Calculate the next reset time (midnight UTC)
            nextResetTime = timeManager.GetNextDailyResetTime();
            
            // Register for the daily reset event
            timeManager.OnDailyReset += HandleDailyReset;
        }
        
        protected virtual void Start()
        {
            // Find TwitchConnection if available
            if (twitchConnection == null)
            {
                twitchConnection = FindAnyObjectByType<TwitchConnection>();
                if (twitchConnection == null)
                {
                    Debug.LogWarning("TwitchConnection not found, cannot send messages");
                }
            }
        }
        
        protected virtual void OnDisable()
        {
            if (!isDestroyed)
            {
                SaveTickets();
                
                // Unregister from the daily reset event
                if (timeManager != null)
                {
                    timeManager.OnDailyReset -= HandleDailyReset;
                }
            }
        }
        
        protected virtual void OnDestroy()
        {
            isDestroyed = true;
            SaveTickets();
            
            // Unregister from the daily reset event
            if (timeManager != null)
            {
                timeManager.OnDailyReset -= HandleDailyReset;
            }
        }
        
        protected virtual void OnApplicationPause(bool pauseStatus)
        {
            // Save tickets when application pauses
            if (pauseStatus)
            {
                SaveTickets();
            }
        }
        
        protected virtual void OnApplicationQuit()
        {
            SaveTickets();
        }
        
        /// <summary>
        /// Handler for daily reset event
        /// </summary>
        protected virtual void HandleDailyReset()
        {
            ResetAllTickets();
            nextResetTime = timeManager.GetNextDailyResetTime();
            Debug.Log($"Daily ticket reset performed via TimeManager. Next reset at: {nextResetTime}");
        }
        
        /// <summary>
        /// Get a formatted string for the time until reset
        /// </summary>
        protected virtual string GetTimeUntilReset()
        {
            // Use the TimeManager to format the time until reset
            return timeManager.GetFormattedTimeUntilReset();
        }
        
        /// <summary>
        /// Attempt to use a ticket for the given user. Returns if successful.
        /// </summary>
        public virtual bool UseTicket(string username)
        {
            // Initialize tickets for new users
            if (!ticketsUsed.ContainsKey(username))
            {
                ticketsUsed[username] = maxTicketsPerUser;
            }
            
            if (ticketsUsed[username] <= 0)
            {
                SendNoTicketsMessage(username);
                return false;
            }

            ticketsUsed[username]--;
            int remainingTickets = ticketsUsed[username];
            
            // Save ticket usage
            SaveTickets();
            
            return true;
        }
        
        /// <summary>
        /// Send a message to the user indicating they have no tickets
        /// </summary>
        protected virtual void SendNoTicketsMessage(string username)
        {
            if (twitchConnection != null)
            {
                twitchConnection.SendChatMessage($"@{username}, you don't have any tickets remaining. {GetTimeUntilReset()}.");
            }
        }
        
        /// <summary>
        /// Get remaining tickets for a user
        /// </summary>
        public virtual int GetRemainingTickets(string username)
        {
            if (!ticketsUsed.ContainsKey(username))
            {
                ticketsUsed[username] = maxTicketsPerUser;
                SaveTickets();
            }
            
            return ticketsUsed[username];
        }
        
        /// <summary>
        /// Process a command to check ticket count
        /// </summary>
        public virtual void ProcessTicketsCommand(string username)
        {
            if (twitchConnection == null) return;
            
            int tickets = GetRemainingTickets(username);
            
            twitchConnection.SendChatMessage($"@{username}, you have {tickets} tickets remaining. {GetTimeUntilReset()}.");
        }
        
        /// <summary>
        /// Check if we need to reset tickets
        /// </summary>
        protected virtual void CheckResetTickets()
        {
            DateTime now = DateTime.UtcNow;
            
            // If we've passed the reset time
            if (now >= nextResetTime)
            {
                ResetAllTickets();
                
                // Get the next reset time from TimeManager
                nextResetTime = timeManager.GetNextDailyResetTime();
                Debug.Log($"Daily ticket reset performed. Next reset at: {nextResetTime}");
            }
        }
        
        /// <summary>
        /// Reset tickets for all users
        /// </summary>
        protected virtual void ResetAllTickets()
        {
            // Instead of just resetting tickets, clear all user data at midnight
            // This is more memory-efficient but loses historical data
            ticketsUsed.Clear();
            
            Debug.Log("Complete user ticket data reset performed at midnight");
            SaveTickets();
        }
        
        /// <summary>
        /// Announce a winner in chat
        /// </summary>
        public virtual void AnnounceWinner(string username, int reward)
        {
            if (twitchConnection == null) return;
            
            twitchConnection.SendChatMessage($"@{username} has won {reward}, congratulations!");
            
            // If they won, let them know their ticket balance
            int remainingTickets = GetRemainingTickets(username);
            twitchConnection.SendChatMessage($"@{username}, you have {remainingTickets} tickets remaining. {GetTimeUntilReset()}.");
        }
        
        /// <summary>
        /// Save ticket data to PlayerPrefs
        /// </summary>
        protected virtual void SaveTickets()
        {
            // Use SerializationHelper to save tickets
            SerializationHelper.SaveDictionaryToPrefs(ticketsUsed, TicketsSaveKey);
            Debug.Log($"Saved ticket data for {ticketsUsed.Count} users using SerializationHelper");
        }
        
        /// <summary>
        /// Load ticket data from PlayerPrefs
        /// </summary>
        protected virtual void LoadTickets()
        {
            ticketsUsed.Clear();
            
            // Use SerializationHelper to load tickets
            var loadedTickets = SerializationHelper.LoadDictionaryFromPrefs<string, int>(TicketsSaveKey);
            
            if (loadedTickets != null && loadedTickets.Count > 0)
            {
                foreach (var ticket in loadedTickets)
                {
                    ticketsUsed[ticket.Key] = ticket.Value;
                }
                Debug.Log($"Loaded ticket data for {ticketsUsed.Count} users");
            }
            else
            {
                Debug.Log("No saved ticket data found, starting fresh");
            }
        }
    }
} 