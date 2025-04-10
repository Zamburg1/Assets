using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using Shared.Core;
using Shared.Chat;
using Shared.Player;
using Alphasquad.GamesShared;

public class LootController : MiniGameController
{
    [Header("Loot Game References")]
    [SerializeField] private LootUIManager lootUiManager;
    [SerializeField] private LootEntryManager entryManager;
    [SerializeField] private RewardProcessor rewardProcessor;
    
    [Header("Game Configuration")]
    [SerializeField] private int roundsPerGame = 3;
    [SerializeField] private float roundDuration = 60f;
    [SerializeField] private float resultsDuration = 10f;
    [SerializeField] private int maxChestSelection = 3;
    [SerializeField] private int gemReward = 50;
    [SerializeField] private string chestCommand = "chest";
    
    [Header("Chest Configuration")]
    [SerializeField] private int totalChests = 9;
    [SerializeField] private int winningChests = 3;
    
    private PlayerDataManager playerDataManager;
    private int currentRound = 0;
    private bool isRoundActive = false;
    private List<int> availableChests;
    private List<int> winningChestIndices;
    private string roundTimerId;
    private Dictionary<string, List<int>> userSelections = new Dictionary<string, List<int>>();
    private List<string> roundWinners = new List<string>();
    
    protected override void Awake()
    {
        base.Awake();
        
        // Set base class timer durations
        gameDuration = roundDuration;
        resultDisplayDuration = resultsDuration;
        
        DebugLogger.LogInitialization(this, "LootController initialized", debugMode);
    }
    
    protected override void Start()
    {
        base.Start();
        
        FindOrCreateComponents();
        
        InitializeComponents();
        
        // Set initial game state
        SetGameState(GameState.WaitingToStart);
        
        // Initialize chests
        InitializeChests();
        
        DebugLogger.LogStateChange(this, "LootController started and waiting to begin", debugMode);
    }
    
    private void FindOrCreateComponents()
    {
        if (lootUiManager == null)
        {
            lootUiManager = FindAnyObjectByType<LootUIManager>();
            if (lootUiManager == null)
                DebugLogger.LogError(this, "LootUIManager not found!");
        }
        
        if (entryManager == null)
        {
            entryManager = FindAnyObjectByType<LootEntryManager>();
            if (entryManager == null)
                DebugLogger.LogError(this, "LootEntryManager not found!");
        }
        
        if (rewardProcessor == null)
        {
            rewardProcessor = FindAnyObjectByType<RewardProcessor>();
            if (rewardProcessor == null)
                DebugLogger.LogWarning(this, "RewardProcessor not found");
        }
        
        // Cache common references
        playerDataManager = FindAnyObjectByType<PlayerDataManager>();
    }
    
    private void InitializeComponents()
    {
        if (lootUiManager != null)
        {
            lootUiManager.Initialize(this);
        }
        
        if (entryManager != null)
        {
            entryManager.Initialize(this);
        }
    }
    
    private void InitializeChests()
    {
        availableChests = new List<int>();
        for (int i = 0; i < totalChests; i++)
        {
            availableChests.Add(i);
        }
    }
    
    protected override void RegisterCommands()
    {
        base.RegisterCommands();
        
        // Register chest command
        RegisterCommand(
            chestCommand,
            (username, args, message, tags, sender) => HandleChestCommand(username, args),
            "Select a chest to loot",
            $"!{chestCommand} [number]"
        );
    }
    
    private bool HandleChestCommand(string username, string[] args)
    {
        // Check if game is active
        if (currentGameState != GameState.Active || !isRoundActive)
        {
            if (currentGameState == GameState.WaitingToStart)
            {
                if (twitchConnection != null && twitchConnection.IsConnected)
                {
                    twitchConnection.SendChatMessage($"@{username}, the loot game hasn't started yet!");
                }
            }
            return false;
        }
        
        // Check for valid chest number
        if (args.Length == 0)
        {
            if (twitchConnection != null && twitchConnection.IsConnected)
            {
                twitchConnection.SendChatMessage($"@{username}, please specify a chest number from 1 to {totalChests}. Example: !{chestCommand} 5");
            }
            return false;
        }
        
        // Parse chest number
        if (!int.TryParse(args[0], out int chestNumber) || chestNumber < 1 || chestNumber > totalChests)
        {
            if (twitchConnection != null && twitchConnection.IsConnected)
            {
                twitchConnection.SendChatMessage($"@{username}, please specify a valid chest number from 1 to {totalChests}.");
            }
            return false;
        }
        
        // Convert to 0-based index
        int chestIndex = chestNumber - 1;
        
        // Check if chest is still available
        if (!availableChests.Contains(chestIndex))
        {
            if (twitchConnection != null && twitchConnection.IsConnected)
            {
                twitchConnection.SendChatMessage($"@{username}, chest {chestNumber} has already been selected! Choose another chest.");
            }
            return false;
        }
        
        // Check if user has already selected the maximum number of chests
        if (!userSelections.ContainsKey(username))
        {
            userSelections[username] = new List<int>();
        }
        
        if (userSelections[username].Count >= maxChestSelection)
        {
            if (twitchConnection != null && twitchConnection.IsConnected)
            {
                twitchConnection.SendChatMessage($"@{username}, you have already selected the maximum of {maxChestSelection} chests for this round.");
            }
            return false;
        }
        
        // Process the selection
        availableChests.Remove(chestIndex);
        userSelections[username].Add(chestIndex);
        
        // Check if this is a winning chest
        bool isWinningChest = winningChestIndices.Contains(chestIndex);
        
        // Display selection in UI
        if (lootUiManager != null)
        {
            lootUiManager.ShowChestSelection(username, chestIndex, isWinningChest);
        }
        
        // Tracking winners - if user selects any winning chest, they're eligible for rewards
        if (isWinningChest && !roundWinners.Contains(username))
        {
            roundWinners.Add(username);
        }
        
        DebugLogger.LogUserInteraction(this, username, $"selected chest {chestNumber}, winning: {isWinningChest}", debugMode);
        
        return true;
    }
    
    public override void StartNewGame()
    {
        // Reset game state
        currentRound = 0;
        roundWinners.Clear();
        
        // Set game state to active
        SetGameState(GameState.Active);
        
        // Start first round
        StartNewRound();
        
        // Announce new game
        AnnounceNewGame();
        
        DebugLogger.LogStateChange(this, "Started new loot game", debugMode);
    }
    
    private void StartNewRound()
    {
        // Reset round data
        userSelections.Clear();
        isRoundActive = true;
        currentRound++;
        
        // Reset chest selections
        InitializeChests();
        
        // Select winning chests for this round
        SelectWinningChests();
        
        // Start round timer
        roundTimerId = StartTimer(
            roundDuration,
            EndCurrentRound,
            ref roundTimerId,
            (progress) => {
                if (lootUiManager != null)
                {
                    lootUiManager.UpdateRoundTimer(roundDuration * (1 - progress));
                }
            }
        );
        
        // Update UI
        if (lootUiManager != null)
        {
            lootUiManager.StartNewRound(currentRound, roundsPerGame);
        }
        
        // Announce new round
        AnnounceNewRound();
        
        DebugLogger.LogStateChange(this, $"Started round {currentRound} of {roundsPerGame}", debugMode);
    }
    
    private void SelectWinningChests()
    {
        winningChestIndices = new List<int>();
        List<int> available = new List<int>(availableChests);
        
        // Randomly select winning chests
        for (int i = 0; i < winningChests; i++)
        {
            if (available.Count == 0) break;
            
            int randomIndex = UnityEngine.Random.Range(0, available.Count);
            int chestIndex = available[randomIndex];
            winningChestIndices.Add(chestIndex);
            available.RemoveAt(randomIndex);
        }
        
        DebugLogger.LogTimer(this, $"Selected {winningChestIndices.Count} winning chests for round {currentRound}", debugMode);
    }
    
    private void EndCurrentRound()
    {
        isRoundActive = false;
        
        // Process round results
        ProcessRoundResults();
        
        // Show round results in UI
        if (lootUiManager != null)
        {
            lootUiManager.ShowRoundResults(currentRound, roundWinners, winningChestIndices);
        }
        
        // Announce round results
        AnnounceRoundResults();
        
        // Check if this was the last round
        if (currentRound >= roundsPerGame)
        {
            EndCurrentGame();
            return;
        }
        
        // Start timer for next round
        string nextRoundTimerId = null;
        StartTimer(
            resultDisplayDuration,
            StartNewRound,
            ref nextRoundTimerId
        );
        
        DebugLogger.LogStateChange(this, $"Ended round {currentRound} with {roundWinners.Count} winners", debugMode);
    }
    
    public override void EndCurrentGame()
    {
        // Cancel any active timers
        CancelAllTimers();
        
        // Set game state to results
        SetGameState(GameState.Results);
        
        // Process winners
        ProcessWinners();
        
        // Display final results in UI
        if (lootUiManager != null)
        {
            lootUiManager.ShowGameResults(roundWinners);
        }
        
        // Announce game results
        AnnounceGameResults();
        
        // Start timer to reset game
        StartResultsTimer(resultDisplayDuration, () => {
            SetGameState(GameState.WaitingToStart);
        });
        
        DebugLogger.LogStateChange(this, $"Ended loot game with {roundWinners.Count} winners", debugMode);
    }
    
    private void ProcessRoundResults()
    {
        // Show all winning chests that weren't selected
        if (lootUiManager != null)
        {
            foreach (int winningIndex in winningChestIndices)
            {
                if (availableChests.Contains(winningIndex))
                {
                    lootUiManager.RevealWinningChest(winningIndex);
                }
            }
        }
    }
    
    private void ProcessWinners()
    {
        // Process winners from all rounds
        if (roundWinners.Count > 0)
        {
            foreach (string winner in roundWinners)
            {
                if (rewardProcessor != null)
                {
                    rewardProcessor.ProcessReward(winner, gemReward, "loot");
                }
                else if (winnerTracker != null)
                {
                    winnerTracker.AddWinner(winner, gemReward, "loot");
                }
                else
                {
                    DebugLogger.LogWarning(this, $"No reward processor found for winner: {winner}");
                }
            }
        }
    }
    
    private void AnnounceNewGame()
    {
        if (twitchConnection == null || !twitchConnection.IsConnected) return;
        
        string message = $"A new Loot Game has started! Type !{chestCommand} [number] to select a chest (1-{totalChests}). You can select up to {maxChestSelection} chests per round.";
        twitchConnection.SendChatMessage(message);
    }
    
    private void AnnounceNewRound()
    {
        if (twitchConnection == null || !twitchConnection.IsConnected) return;
        
        string message = $"Round {currentRound} of {roundsPerGame} has begun! Use !{chestCommand} [number] to select a chest (1-{totalChests}). {winningChests} chests contain loot!";
        twitchConnection.SendChatMessage(message);
    }
    
    private void AnnounceRoundResults()
    {
        if (twitchConnection == null || !twitchConnection.IsConnected) return;
        
        if (roundWinners.Count > 0)
        {
            string winnersList = roundWinners.Count <= 3 ? 
                string.Join(", ", roundWinners.ConvertAll(w => "@" + w)) :
                $"{roundWinners.Count} players";
                
            string message = $"Round {currentRound} has ended! {winnersList} found treasure in the chests!";
            twitchConnection.SendChatMessage(message);
        }
        else
        {
            twitchConnection.SendChatMessage($"Round {currentRound} has ended! No one found treasure in the chests this round!");
        }
    }
    
    private void AnnounceGameResults()
    {
        if (twitchConnection == null || !twitchConnection.IsConnected) return;
        
        if (roundWinners.Count > 0)
        {
            string winnersList = roundWinners.Count <= 5 ?
                string.Join(", ", roundWinners.ConvertAll(w => "@" + w)) :
                $"{roundWinners.Count} players";
                
            string message = $"The Loot Game has ended! {winnersList} won {gemReward} gems each!";
            twitchConnection.SendChatMessage(message);
        }
        else
        {
            twitchConnection.SendChatMessage("The Loot Game has ended, but no one found any treasure! Better luck next time!");
        }
    }
    
    protected override void UpdateGameUI()
    {
        if (lootUiManager == null) return;
        
        // Update UI based on current game state
        switch (currentGameState)
        {
            case GameState.WaitingToStart:
                lootUiManager.UpdateWaitingState();
                break;
                
            case GameState.Active:
                float remainingTime = roundDuration;
                if (!string.IsNullOrEmpty(roundTimerId) && timeManager != null)
                {
                    remainingTime = timeManager.GetRemainingTime(roundTimerId);
                }
                
                lootUiManager.UpdateActiveState(
                    currentRound, 
                    roundsPerGame, 
                    remainingTime,
                    availableChests.Count,
                    userSelections.Count
                );
                break;
                
            case GameState.Results:
                lootUiManager.UpdateResultsState(GetResultsTimerRemainingTime());
                break;
                
            case GameState.Ended:
                lootUiManager.UpdateEndedState();
                break;
        }
    }
    
    protected override void Cleanup()
    {
        base.Cleanup();
        
        // Reset game data
        userSelections.Clear();
        roundWinners.Clear();
        currentRound = 0;
        isRoundActive = false;
        
        // Cancel any running timers
        CancelAllTimers();
    }
    
    public int GetTotalChests()
    {
        return totalChests;
    }
    
    public int GetCurrentRound()
    {
        return currentRound;
    }
    
    public int GetTotalRounds()
    {
        return roundsPerGame;
    }
    
    public List<string> GetRoundWinners()
    {
        return new List<string>(roundWinners);
    }
    
    public List<int> GetWinningChestIndices()
    {
        return new List<int>(winningChestIndices);
    }
} 