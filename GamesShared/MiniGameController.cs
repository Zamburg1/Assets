using UnityEngine;
using System;
using System.Collections.Generic;
using Shared.Chat;
using Alphasquad.GamesShared;

namespace Shared.Core
{
    /// <summary>
    /// Base class for all mini-game controllers with common functionality
    /// </summary>
    public abstract class MiniGameController : MonoBehaviour
    {
        [Header("Common Game Settings")]
        [SerializeField] protected float gameDuration = 300f;
        [SerializeField] protected float resultDisplayDuration = 5f;
        
        [Header("Common References")]
        [SerializeField] protected TwitchConnection twitchConnection;
        [SerializeField] protected WinnerTracker winnerTracker;

        [Header("Debug Settings")]
        [SerializeField] protected bool debugMode = false;
        
        // Core system references
        protected ChatCommandProcessor commandProcessor;
        protected UIManager uiManager;
        protected TimerManager timerManager;
        protected bool commandsRegistered = false;
        private List<string> registeredCommands = new List<string>();
        
        // Game state
        protected GameState currentGameState = GameState.WaitingToStart;
        public GameState CurrentGameState => currentGameState;
        
        // Event delegates
        public delegate void GameEventHandler(int eventId);
        public delegate void GameStateChangedHandler(GameState newState);
        
        // Events
        public event GameEventHandler OnGameStart;
        public event GameEventHandler OnGameEnd;
        public event Action<float> OnTimerUpdate;
        public event GameStateChangedHandler OnGameStateChanged;
        
        // Timer references
        private string gameTimerId;
        private string resultsTimerId;
        
        #region MonoBehaviour Lifecycle
        
        protected virtual void Awake()
        {
            commandProcessor = ChatCommandProcessor.Instance;
            timerManager = TimerManager.Instance;
            
            FindOrCreateManagers();
            
            if (commandProcessor == null)
                DebugLogger.LogWarning(this, "ChatCommandProcessor not found");
                
            if (timerManager == null)
                DebugLogger.LogWarning(this, "TimerManager not found");
            
            DebugLogger.LogInitialization(this, "MiniGameController initialized", debugMode);
        }
        
        protected virtual void Start()
        {
            SetGameState(GameState.WaitingToStart);
        }
        
        protected virtual void OnEnable()
        {
            if (!commandsRegistered)
                RegisterCommands();
        }
        
        protected virtual void OnDisable()
        {
            UnregisterCommands();
        }
        
        protected virtual void Update()
        {
            uiManager?.CheckForUIUpdate();
        }
        
        protected virtual void OnDestroy()
        {
            Cleanup();
        }
        
        #endregion
        
        #region Initialization
        
        protected virtual void FindOrCreateManagers()
        {
            uiManager = FindObjectOfType<UIManager>();
            if (uiManager == null)
            {
                GameObject uiManagerObject = new GameObject("UIManager");
                uiManager = uiManagerObject.AddComponent<UIManager>();
                DebugLogger.LogInitialization(this, "Created UIManager", debugMode);
            }
        }
        
        #endregion
        
        #region Command Processing
        
        protected virtual void RegisterCommands()
        {
            DebugLogger.LogInitialization(this, "Registering commands", debugMode);
            commandsRegistered = true;
        }
        
        protected virtual void UnregisterCommands()
        {
            if (!commandsRegistered || commandProcessor == null) return;
            
            int count = commandProcessor.UnregisterAllCommandsFrom(this.gameObject);
            DebugLogger.LogCommand(this, $"Unregistered {count} commands", debugMode);
            
            commandsRegistered = false;
            registeredCommands.Clear();
        }
        
        protected bool RegisterCommand(string command, ChatCommandProcessor.CommandHandler handler, 
                                       string description, string usage, bool requiresArgs = false)
        {
            if (commandProcessor == null) return false;
            
            commandProcessor.RegisterCommand(
                command,
                handler,
                description,
                usage,
                requiresArgs,
                this.gameObject
            );
            
            DebugLogger.LogCommand(this, $"Registered command: {command}", debugMode);
            registeredCommands.Add(command);
            return true;
        }
        
        #endregion
        
        #region Game State Management
        
        protected virtual void SetGameState(GameState newState)
        {
            if (newState == currentGameState) return;
                
            GameState oldState = currentGameState;
            currentGameState = newState;
            
            DebugLogger.LogStateChange(this, $"Game state changed from {oldState} to {currentGameState}", debugMode);
            
            OnGameStateChanged?.Invoke(currentGameState);
            OnGameStateChanged(newState);
            
            uiManager?.MarkUIForUpdate();
        }
        
        protected virtual void OnGameStateChanged(GameState newState)
        {
            // Base implementation does nothing - override in derived classes
        }
        
        public bool AllowsUserInteraction()
        {
            return currentGameState.AllowsUserInteraction();
        }
        
        public virtual void PauseGame()
        {
            if (currentGameState == GameState.Active)
                SetGameState(GameState.Paused);
        }
        
        public virtual void ResumeGame()
        {
            if (currentGameState == GameState.Paused)
                SetGameState(GameState.Active);
        }
        
        public virtual void StartNewGame()
        {
            DebugLogger.LogStateChange(this, "Starting new game", debugMode);
            SetGameState(GameState.Active);
            OnGameStart?.Invoke(0);
        }
        
        public virtual void EndCurrentGame()
        {
            DebugLogger.LogStateChange(this, "Ending current game", debugMode);
            SetGameState(GameState.Ended);
            OnGameEnd?.Invoke(0);
        }
        
        #endregion
        
        #region Timer Management
        
        protected virtual string StartGameTimer(float duration, Action onComplete, Action<float> onProgress = null)
        {
            if (timerManager == null)
            {
                DebugLogger.LogWarning(this, "TimerManager not available for timer");
                return null;
            }
            
            CancelGameTimer();
            
            gameTimerId = timerManager.StartTimer(duration, onComplete, onProgress);
            DebugLogger.LogTimer(this, $"Started game timer: {gameTimerId}, duration: {duration}s", "game", duration, debugMode);
            
            return gameTimerId;
        }
        
        protected virtual bool CancelGameTimer()
        {
            if (string.IsNullOrEmpty(gameTimerId) || timerManager == null) 
                return false;
            
            bool result = timerManager.CancelTimer(gameTimerId);
            if (result)
            {
                DebugLogger.LogTimer(this, $"Cancelled game timer: {gameTimerId}", "cancel", 0, debugMode);
                gameTimerId = null;
            }
            
            return result;
        }
        
        protected virtual string StartResultsTimer(float duration, Action onComplete, Action<float> onProgress = null)
        {
            if (timerManager == null)
            {
                DebugLogger.LogWarning(this, "TimerManager not available for timer");
                return null;
            }
            
            CancelResultsTimer();
            
            resultsTimerId = timerManager.StartTimer(duration, onComplete, onProgress);
            DebugLogger.LogTimer(this, $"Started results timer: {resultsTimerId}, duration: {duration}s", "results", duration, debugMode);
            
            return resultsTimerId;
        }
        
        protected virtual bool CancelResultsTimer()
        {
            if (string.IsNullOrEmpty(resultsTimerId) || timerManager == null) 
                return false;
            
            bool result = timerManager.CancelTimer(resultsTimerId);
            if (result)
            {
                DebugLogger.LogTimer(this, $"Cancelled results timer: {resultsTimerId}", "cancel", 0, debugMode);
                resultsTimerId = null;
            }
            
            return result;
        }
        
        protected virtual void CancelAllTimers()
        {
            CancelGameTimer();
            CancelResultsTimer();
        }
        
        protected float GetGameTimerRemainingTime()
        {
            if (timerManager == null || string.IsNullOrEmpty(gameTimerId))
                return 0f;
                
            return timerManager.GetRemainingTime(gameTimerId);
        }
        
        protected float GetResultsTimerRemainingTime()
        {
            if (timerManager == null || string.IsNullOrEmpty(resultsTimerId))
                return 0f;
                
            return timerManager.GetRemainingTime(resultsTimerId);
        }
        
        protected float GetGameTimerProgress()
        {
            if (timerManager == null || string.IsNullOrEmpty(gameTimerId))
                return 1f;
                
            float remaining = timerManager.GetRemainingTime(gameTimerId);
            if (remaining <= 0f || gameDuration <= 0f)
                return 1f;
                
            return 1f - (remaining / gameDuration);
        }
        
        protected float GetResultsTimerProgress()
        {
            if (timerManager == null || string.IsNullOrEmpty(resultsTimerId))
                return 1f;
                
            float remaining = timerManager.GetRemainingTime(resultsTimerId);
            if (remaining <= 0f || resultDisplayDuration <= 0f)
                return 1f;
                
            return 1f - (remaining / resultDisplayDuration);
        }
        
        #endregion
        
        #region UI Management
        
        protected virtual void MarkUIForUpdate()
        {
            uiManager?.MarkUIForUpdate();
        }
        
        protected virtual void UpdateUIImmediate()
        {
            uiManager?.UpdateUIImmediate();
        }
        
        protected virtual void UpdateGameUI()
        {
            // Base implementation does nothing - override in derived classes
        }
        
        #endregion
        
        #region Reward Distribution
        
        protected virtual void DistributeRewards(List<string> winners, int rewardPerWinner, string rewardSource)
        {
            if (winners == null || winners.Count == 0 || rewardPerWinner <= 0)
            {
                DebugLogger.LogWarning(this, "No rewards to distribute: empty winners list or zero reward amount");
                return;
            }
            
            DebugLogger.LogStateChange(this, $"Distributing {rewardPerWinner} gems to each of {winners.Count} winners. Source: {rewardSource}", debugMode);
            
            if (winnerTracker != null)
            {
                foreach (string winner in winners)
                {
                    winnerTracker.AddWinner(winner, rewardPerWinner, rewardSource);
                }
                
                DebugLogger.LogStateChange(this, "Rewards distributed via WinnerTracker", debugMode);
            }
            else
            {
                PlayerDataManager playerDataManager = FindObjectOfType<PlayerDataManager>();
                if (playerDataManager != null)
                {
                    foreach (string winner in winners)
                    {
                        playerDataManager.AddGems(winner, rewardPerWinner, rewardSource);
                    }
                    
                    DebugLogger.LogStateChange(this, "Rewards distributed via PlayerDataManager", debugMode);
                }
                else
                {
                    DebugLogger.LogWarning(this, "Cannot distribute rewards: neither WinnerTracker nor PlayerDataManager found");
                }
            }
        }
        
        protected virtual void AnnounceWinners(List<string> winners, int rewardPerWinner, string gameType)
        {
            if (twitchConnection == null || !twitchConnection.IsConnected)
            {
                DebugLogger.LogWarning(this, "Cannot announce winners: Twitch connection not available");
                return;
            }
            
            if (winners == null || winners.Count == 0)
                return;
                
            string winnersList = string.Join(", ", winners);
            string announcement = $"Congratulations to our {gameType} winners: {winnersList}! Each wins {rewardPerWinner} gems!";
            
            twitchConnection.SendChatMessage(announcement);
            DebugLogger.LogStateChange(this, $"Announced winners: {announcement}", debugMode);
        }
        
        #endregion
        
        #region Cleanup
        
        protected virtual void Cleanup()
        {
            CancelAllTimers();
            
            if (commandsRegistered)
            {
                UnregisterCommands();
            }
        }
        
        #endregion
    }
} 