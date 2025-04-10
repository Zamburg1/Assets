using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using Shared.Chat;
using Shared.Core;
using Shared.Player;
using Alphasquad.GamesShared;

public class GiveawayController : MiniGameController
{
    [Header("Giveaway Game References")]
    [SerializeField] private GiveawayEntryManager entryManager;
    [SerializeField] private GiveawayMultiplierStateManager multiplierStateManager;
    [SerializeField] private GiveawayUIManager uiManager;

    [Header("UI Settings")]
    [SerializeField] private float nextGiveawayTimerDuration = 300f; // 5 minutes
    
    [Header("Game Configuration")]
    [SerializeField] private string joinCommand = "join";
    [SerializeField] private string ticketCommand = "tickets";
    [SerializeField] private int baseTicketCost = 100;
    [SerializeField] private int winnerCount = 1;

    private RewardProcessor rewardProcessor;
    private string nextGiveawayTimerEventId;
    private bool isProcessingWinners = false;

    protected override void Awake()
    {
        base.Awake();
        
        // Set base class timer durations
        gameDuration = nextGiveawayTimerDuration;
        
        DebugLogger.LogInitialization(this, "GiveawayController initialized", debugMode);
    }

    protected override void Start()
    {
        base.Start();
        
        FindOrCreateComponents();
        
        InitializeComponents();
        
        // Set initial game state
        SetGameState(GameState.WaitingToStart);
        
        // Start the next giveaway timer
        StartNextGiveawayTimer();
    }
    
    private void FindOrCreateComponents()
    {
        if (entryManager == null)
        {
            entryManager = FindAnyObjectByType<GiveawayEntryManager>();
            if (entryManager == null)
                DebugLogger.LogError(this, "GiveawayEntryManager not found!");
        }
        
        if (multiplierStateManager == null)
        {
            multiplierStateManager = FindAnyObjectByType<GiveawayMultiplierStateManager>();
            if (multiplierStateManager == null)
                DebugLogger.LogWarning(this, "GiveawayMultiplierStateManager not found. Multipliers won't be available.");
        }
        
        if (uiManager == null)
        {
            uiManager = FindAnyObjectByType<GiveawayUIManager>();
            if (uiManager == null)
                DebugLogger.LogError(this, "GiveawayUIManager not found!");
        }
        
        // Find auxiliary components
        rewardProcessor = FindAnyObjectByType<RewardProcessor>();
        if (rewardProcessor == null)
            DebugLogger.LogWarning(this, "RewardProcessor not found. Will use backup reward methods.");
    }
    
    private void InitializeComponents()
    {
        if (entryManager != null)
        {
            entryManager.Initialize(this);
        }
        
        if (multiplierStateManager != null)
        {
            multiplierStateManager.Initialize(this);
        }
        
        if (uiManager != null)
        {
            uiManager.Initialize(this);
        }
    }

    protected override void RegisterCommands()
    {
        base.RegisterCommands();
        
        // Register join command
        RegisterCommand(
            joinCommand,
            (username, args, message, tags, sender) => HandleJoinCommand(username),
            "Join the current giveaway",
            $"!{joinCommand}"
        );
        
        // Register tickets command
        RegisterCommand(
            ticketCommand,
            (username, args, message, tags, sender) => HandleTicketCommand(username),
            "Check how many tickets you have for the giveaway",
            $"!{ticketCommand}"
        );
    }
    
    private bool HandleJoinCommand(string username)
    {
        // Check if entries can be added (game is active)
        if (currentGameState != GameState.Active)
        {
            if (currentGameState == GameState.WaitingToStart)
            {
                if (twitchConnection != null && twitchConnection.IsConnected)
                {
                    twitchConnection.SendChatMessage($"@{username}, the next giveaway hasn't started yet. Please wait!");
                }
            }
            return false;
        }
        
        // Process the entry
        if (entryManager != null)
        {
            bool success = entryManager.AddEntry(username);
            
            if (success)
            {
                DebugLogger.LogUserInteraction(this, username, "joined the giveaway", debugMode);
                
                // Show entry in UI
                if (uiManager != null)
                {
                    uiManager.ShowUserEntry(username);
                }
                
                return true;
            }
        }
        
        return false;
    }
    
    private bool HandleTicketCommand(string username)
    {
        if (entryManager != null)
        {
            int tickets = entryManager.GetTicketsForUser(username);
            
            if (twitchConnection != null && twitchConnection.IsConnected)
            {
                twitchConnection.SendChatMessage($"@{username}, you have {tickets} ticket{(tickets != 1 ? "s" : "")} for the current giveaway.");
            }
            
            return true;
        }
        
        return false;
    }
    
    private void StartNextGiveawayTimer()
    {
        // Cancel any existing timer
        if (!string.IsNullOrEmpty(nextGiveawayTimerEventId))
        {
            CancelTimer(nextGiveawayTimerEventId);
            nextGiveawayTimerEventId = null;
        }
        
        // Start a new timer
        nextGiveawayTimerEventId = StartTimer(
            nextGiveawayTimerDuration, 
            StartNewGame,
            ref nextGiveawayTimerEventId,
            (progress) => {
                if (uiManager != null)
                {
                    uiManager.UpdateNextGiveawayTimer(nextGiveawayTimerDuration * (1 - progress));
                }
            }
        );
        
        DebugLogger.LogTimer(this, $"Started next giveaway timer: {nextGiveawayTimerEventId}", debugMode);
    }

    public override void StartNewGame()
    {
        // Cancel any running timers
        CancelAllTimers();
        
        // Reset game state
        if (entryManager != null)
        {
            entryManager.ResetEntries();
        }
        
        // Set game state to active
        SetGameState(GameState.Active);
        
        // Start game timer
        StartGameTimer(gameDuration, EndCurrentGame);
        
        // Announce new giveaway in chat
        AnnounceNewGiveaway();
        
        DebugLogger.LogStateChange(this, "Started new giveaway", debugMode);
    }
    
    public override void EndCurrentGame()
    {
        if (isProcessingWinners) return;
        
        isProcessingWinners = true;
        SetGameState(GameState.Results);
        
        // Process the results
        if (entryManager != null)
        {
            // Select winners
            List<string> winners = entryManager.SelectWinners(winnerCount);
            int rewardAmount = CalculateRewardAmount();
            
            // Log the results
            DebugLogger.LogStateChange(this, $"Giveaway ended. Winners: {winners.Count}, Reward: {rewardAmount}", debugMode);
            
            // Process rewards
            ProcessWinners(winners, rewardAmount);
            
            // Display results in UI
            if (uiManager != null)
            {
                uiManager.ShowResults(winners, rewardAmount);
            }
            
            // Announce results
            AnnounceWinners(winners, rewardAmount);
        }
        
        // Start timer for results display
        StartResultsTimer(resultDisplayDuration, () => {
            isProcessingWinners = false;
            
            // Set waiting state and start next giveaway timer
            SetGameState(GameState.WaitingToStart);
            StartNextGiveawayTimer();
        });
    }
    
    private int CalculateRewardAmount()
    {
        if (entryManager != null)
        {
            // Basic calculation based on number of entries
            int totalEntries = entryManager.GetTotalEntryCount();
            return Mathf.Max(50, totalEntries * 5); // Minimum 50 gems, 5 gems per entry
        }
        
        return 50; // Default fallback amount
    }
    
    private void ProcessWinners(List<string> winners, int rewardAmount)
    {
        if (winners == null || winners.Count == 0) return;
        
        // Use reward processor if available
        if (rewardProcessor != null)
        {
            foreach (string winner in winners)
            {
                rewardProcessor.ProcessReward(winner, rewardAmount, "giveaway");
            }
            return;
        }
        
        // Fallback to winner tracker
        if (winnerTracker != null)
        {
            foreach (string winner in winners)
            {
                winnerTracker.AddWinner(winner, rewardAmount, "giveaway");
            }
            return;
        }
        
        // Log error if no reward system is available
        DebugLogger.LogError(this, "Cannot process winners: no reward processor or winner tracker found");
    }
    
    private void AnnounceNewGiveaway()
    {
        if (twitchConnection == null || !twitchConnection.IsConnected) return;
        
        string message = $"A new giveaway has started! Type !{joinCommand} to enter for a chance to win. The drawing will happen in {gameDuration / 60} minutes!";
        twitchConnection.SendChatMessage(message);
    }
    
    private void AnnounceWinners(List<string> winners, int rewardAmount)
    {
        if (twitchConnection == null || !twitchConnection.IsConnected) return;
        
        if (winners.Count > 0)
        {
            string winnersList = string.Join(", ", winners.ConvertAll(w => "@" + w));
            string message = $"Congratulations to {winnersList} who won {rewardAmount} gems each in the giveaway!";
            twitchConnection.SendChatMessage(message);
        }
        else
        {
            twitchConnection.SendChatMessage("The giveaway ended, but no eligible winners were found. Better luck next time!");
        }
    }
    
    protected override void UpdateGameUI()
    {
        if (uiManager == null) return;
        
        // Update UI based on current game state
        switch (currentGameState)
        {
            case GameState.WaitingToStart:
                if (!string.IsNullOrEmpty(nextGiveawayTimerEventId))
                {
                    float remainingTime = nextGiveawayTimerDuration;
                    if (timeManager != null)
                    {
                        remainingTime = timeManager.GetRemainingTime(nextGiveawayTimerEventId);
                    }
                    uiManager.UpdateWaitingState(remainingTime);
                }
                break;
                
            case GameState.Active:
                uiManager.UpdateActiveState(GetGameTimerRemainingTime(), entryManager.GetTotalEntryCount());
                break;
                
            case GameState.Results:
                uiManager.UpdateResultsState(GetResultsTimerRemainingTime());
                break;
                
            case GameState.Ended:
                uiManager.UpdateEndedState();
                break;
        }
    }
    
    protected override void Cleanup()
    {
        base.Cleanup();
        
        // Reset entries
        if (entryManager != null)
        {
            entryManager.ResetEntries();
        }
        
        // Cancel any running timers
        CancelAllTimers();
    }
    
    public List<string> GetParticipants()
    {
        return entryManager != null ? entryManager.GetParticipants() : new List<string>();
    }
    
    public int GetEntryCount()
    {
        return entryManager != null ? entryManager.GetTotalEntryCount() : 0;
    }
    
    public int GetBaseTicketCost()
    {
        return baseTicketCost;
    }
    
    public int GetTicketMultiplier()
    {
        return multiplierStateManager != null ? multiplierStateManager.GetCurrentMultiplier() : 1;
    }
}