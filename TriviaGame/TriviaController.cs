using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using Shared.Trivia;
using Shared.Core;
using Unity.Collections;
using Shared.Chat;
using Shared.Player;
using TMPro;
using Alphasquad.GamesShared;

public class TriviaController : MiniGameController
{
    [Header("References")]
    [SerializeField] private TriviaUIManager triviaUiManager;
    [SerializeField] private TriviaEntryManager entryManager;
    [SerializeField] private QuestionLoader questionLoader;
    
    [Header("Game Configuration")]
    [SerializeField, Tooltip("Base duration in seconds for easy difficulty questions")]
    private float baseDuration = 120f;
    
    [SerializeField, Tooltip("Command prefix for answers (e.g. !answer for !answer1)")]
    private string answerCommandPrefix = "answer";
    
    [Header("Debug Options")]
    [SerializeField, Tooltip("Enable to see debug logs")]
    private new bool debugMode = false;
    
    [SerializeField, Tooltip("Force load test question instead of from API")]
    private bool useTestQuestion = false;
    
    // Current game state
    private TriviaQuestion currentQuestion;
    private float currentQuestionDuration = 120f; // Default is for easy difficulty
    
    // ID for the current question, incremented each time a new question is started
    private int currentQuestionId = 0;
    
    // Cached reference to PlayerDataManager
    private PlayerDataManager playerDataManager;
    
    // Flag for question loading
    private bool isLoadingQuestion = false;
    
    // Expose the current question for UI access
    public TriviaQuestion CurrentQuestion => currentQuestion;
    
    [Header("Results Settings")]
    [SerializeField] private float resultsDuration = 10f;
    
    protected override void Awake()
    {
        base.Awake();
        
        // Set result display duration
        resultDisplayDuration = resultsDuration;
        
        DebugLogger.LogInitialization(this, "TriviaController initialized", debugMode);
    }
    
    // Start is called before the first frame update
    protected override void Start()
    {
        // Call base implementation first - handles command registration
        base.Start();
        
        // Get references if not assigned
        if (entryManager == null)
        {
            entryManager = FindAnyObjectByType<TriviaEntryManager>();
            if (entryManager == null) 
                DebugLogger.LogError(this, "TriviaEntryManager not found!");
        }
        
        if (triviaUiManager == null)
        {
            triviaUiManager = FindAnyObjectByType<TriviaUIManager>();
            if (triviaUiManager == null) 
                DebugLogger.LogError(this, "TriviaUIManager not found!");
        }
        
        if (questionLoader == null)
        {
            questionLoader = FindAnyObjectByType<QuestionLoader>();
            if (questionLoader == null) 
                DebugLogger.LogError(this, "QuestionLoader not found!");
        }
        
        // Cache the PlayerDataManager reference
        playerDataManager = FindAnyObjectByType<PlayerDataManager>();
        
        // Set initial game state
        SetGameState(GameState.WaitingToStart);
    }
    
    protected override void OnEnable()
    {
        base.OnEnable();
    }
    
    protected override void OnDisable()
    {
        base.OnDisable();
        
        // Clean up any scheduled events
        CancelAllTimers();
        
        // Unregister commands
        UnregisterCommands();
    }
    
    protected override void OnDestroy()
    {
        base.OnDestroy();
        
        // Extra safety cleanup
        CancelAllTimers();
        
        // Clean up commands
        UnregisterCommands();
    }
    
    protected override void RegisterCommands()
    {
        // Call base implementation first
        base.RegisterCommands();
        
        // Register direct answer commands
        for (int i = 1; i <= 4; i++)
        {
            string command = answerCommandPrefix + i;
            int answerIndex = i - 1; // Convert to 0-based index
            
            // Create a handler for this specific answer index
            RegisterCommand(
                command,
                (username, args, message, tags, sender) => {
                    HandleAnswerCommand(username, answerIndex);
                    return true;
                },
                $"Submit answer {i} for the current trivia question",
                $"Usage: {command}"
            );
        }
    }
    
    // Calculate question duration based on difficulty
    private float GetDurationForDifficulty(string difficulty)
    {
        // Default to easy if difficulty is empty or null
        if (string.IsNullOrEmpty(difficulty))
            return baseDuration;
            
        switch (difficulty.ToLower())
        {
            case "easy":
                return baseDuration;
            case "medium":
                return baseDuration * 1.5f; // 150% of base duration
            case "hard":
                return baseDuration * 2.0f; // 200% of base duration
            default:
                return baseDuration; // Default to easy for unknown difficulties
        }
    }
    
    // Override UpdateGameUI to handle specific UI updates for trivia
    protected override void UpdateGameUI()
    {
        if (triviaUiManager == null) return;
        
        // Update UI based on current game state
        switch (currentGameState)
        {
            case GameState.WaitingToStart:
                triviaUiManager.UpdateWaitingState();
                break;
                
            case GameState.Active:
                UpdateActiveGameUI();
                break;
                
            case GameState.Results:
                UpdateResultsUI();
                break;
                
            case GameState.Ended:
                triviaUiManager.UpdateEndedState();
                break;
                
            case GameState.Paused:
                triviaUiManager.UpdateWaitingState(); // Use waiting state for paused as well
                break;
        }
    }
    
    // Update UI elements specific to active gameplay
    private void UpdateActiveGameUI()
    {
        if (currentQuestion != null)
        {
            float remainingTime = GetGameTimerRemainingTime();
            float progress = GetGameTimerProgress();
            
            // Update the UI with current question and timer information
            triviaUiManager.UpdateQuestion(currentQuestion, remainingTime, progress);
        }
    }
    
    // Update UI elements specific to results display
    private void UpdateResultsUI()
    {
        if (currentQuestion != null)
        {
            float remaining = GetResultsTimerRemainingTime();
            float progress = GetResultsTimerProgress();
            
            triviaUiManager.UpdateResultsRemainingTime(remaining);
        }
    }
    
    // Event handler for game state changes
    protected override void OnGameStateChanged(GameState newState)
    {
        // Ensure UI is updated on state changes
        MarkUIForUpdate();
        
        if (newState == GameState.Active && !isLoadingQuestion)
        {
            // Start loading the first question when game becomes active
            StartNewQuestion();
        }
    }
    
    // Start a new trivia question
    public void StartNewQuestion()
    {
        if (currentGameState != GameState.Active)
        {
            DebugLog("Cannot start new question: game is not active");
            return;
        }
        
        if (isLoadingQuestion)
        {
            DebugLog("Already loading a question, ignoring start request");
            return;
        }
        
        // Clear all entries
        entryManager.ClearVotes();
        
        // Increment the question ID
        currentQuestionId++;
        
        // Mark that we're loading a question
        isLoadingQuestion = true;
        
        // Update UI to show loading state
        if (triviaUiManager != null)
        {
            triviaUiManager.ShowLoadingQuestion();
        }
        
        // Load a new question
        LoadNewQuestion();
    }
    
    // End the current question and show results
    public void EndCurrentQuestion()
    {
        if (currentQuestion == null)
        {
            DebugLog("No active question to end");
            return;
        }
        
        // Cancel question timer
        CancelGameTimer();
        
        // End the question and calculate winners
        EndQuestion();
        
        // Schedule returning to the next question after the result display
        StartResultsTimer(resultDisplayDuration, () => {
            // Only proceed to next question if game is still active
            if (currentGameState.AllowsGameplayProgression())
            {
                StartNewQuestion();
            }
        }, (progress) => {
            // Update UI to show remaining time for results display
            float remaining = resultDisplayDuration * (1f - progress);
            triviaUiManager.UpdateResultsRemainingTime(remaining);
        });
    }
    
    public bool ProcessAnswer(string username, string answer)
    {
        if (currentQuestion == null || !currentGameState.AllowsUserInteraction() || currentGameState == GameState.Results)
        {
            DebugLog($"Cannot process answer from {username}: Question not active or game not accepting answers");
            return false;
        }
        
        // Parse answer to get index
        if (int.TryParse(answer, out int index) && index >= 1 && index <= 4)
        {
            // Convert to 0-based index
            int answerIndex = index - 1;
            
            // Record the vote
            bool result = entryManager.TryAddAnswer(username, answerIndex, currentQuestionId);
            
            // If vote was recorded, update UI
            if (result)
            {
                MarkUIForUpdate();
            }
            
            return result;
        }
        
        return false;
    }
    
    private bool IsValidAnswer(string answer)
    {
        if (int.TryParse(answer, out int index))
        {
            return index >= 1 && index <= 4;
        }
        
        // Try to parse letter answers (A, B, C, D)
        if (answer.Length == 1)
        {
            char c = answer.ToUpper()[0];
            return c >= 'A' && c <= 'D';
        }
        
        return false;
    }
    
    // Load a new question from the API or test question
    private void LoadNewQuestion()
    {
        if (isLoadingQuestion || currentGameState == GameState.Ended) 
            return;
            
        isLoadingQuestion = true;
        
        if (triviaUiManager != null)
        {
            triviaUiManager.ShowLoadingQuestion();
        }
        
        if (useTestQuestion)
        {
            ProcessTestQuestion();
            return;
        }
        
        if (questionLoader != null)
        {
            DebugLogger.LogStateChange(this, "Requesting new question from loader", debugMode);
            questionLoader.GetRandomQuestion(ProcessNewQuestion);
        }
        else
        {
            DebugLogger.LogError(this, "No question loader assigned and test questions disabled");
            ProcessTestQuestion();
        }
    }
    
    // Process a newly loaded question
    private void ProcessNewQuestion(TriviaQuestion question)
    {
        isLoadingQuestion = false;
        
        if (question == null)
        {
            DebugLogger.LogError(this, "Received null question from loader");
            ProcessTestQuestion();
            return;
        }
        
        currentQuestion = question;
        currentQuestionId++;
        
        currentQuestionDuration = GetDurationForDifficulty(question.difficulty);
        DebugLogger.LogStateChange(this, $"New question loaded. ID: {currentQuestionId}, Difficulty: {question.difficulty}, Duration: {currentQuestionDuration}s", debugMode);
        
        SetGameState(GameState.Active);
        MarkUIForUpdate();
        
        StartGameTimer(currentQuestionDuration, EndQuestion, (remaining) => {
            MarkUIForUpdate();
        });
    }
    
    private void ProcessTestQuestion()
    {
        TriviaQuestion testQuestion = new TriviaQuestion
        {
            question = "What is the capital of France?",
            options = new string[] { "London", "Paris", "Berlin", "Madrid" },
            correctAnswer = 1, // Paris
            difficulty = "easy"
        };
        
        ProcessNewQuestion(testQuestion);
    }
    
    // End the current question and calculate winners
    private void EndQuestion()
    {
        if (currentQuestion == null) return;
        
        DebugLogger.LogStateChange(this, $"Question {currentQuestionId} ended", debugMode);
        
        SetGameState(GameState.Results);
        List<string> winners = entryManager.CalculateWinners(currentQuestion.correctAnswer);
        
        int rewardPerWinner = 100; // Base reward, could be scaled by difficulty
        
        if (winners.Count > 0)
        {
            DistributeRewards(winners, rewardPerWinner, "trivia");
            AnnounceWinners(winners, rewardPerWinner, "Trivia");
        }
        
        string winnersText = winners.Count > 0 
            ? string.Join(", ", winners) 
            : "No winners this round!";
            
        triviaUiManager.DisplayResults(
            currentQuestion.question,
            currentQuestion.options,
            entryManager.GetAllVoteCounts(),
            currentQuestion.correctAnswer,
            winnersText
        );
        
        StartResultsTimer(resultDisplayDuration, () => {
            entryManager.ClearVotes();
            LoadNewQuestion();
        }, (remaining) => {
            MarkUIForUpdate();
        });
    }
    
    public override void StartNewGame()
    {
        base.StartNewGame();
        DebugLogger.LogStateChange(this, "Starting new Trivia game", debugMode);
        
        LoadNewQuestion();
    }
    
    public override void EndCurrentGame()
    {
        CancelAllTimers();
        SetGameState(GameState.Ended);
        DebugLogger.LogStateChange(this, "Trivia game ended", debugMode);
        
        base.EndCurrentGame();
    }
    
    protected override IEnumerator GameLoop()
    {
        while (true)
        {
            // Process based on current game state
            switch (currentGameState)
            {
                case GameState.WaitingToStart:
                    // Wait until game becomes active
                    yield return new WaitUntil(() => currentGameState != GameState.WaitingToStart);
                    break;
                    
                case GameState.Active:
                    // Game is active, no special processing needed as timers handle progression
                    yield return null;
                    break;
                    
                case GameState.Results:
                    // Results are being displayed, no special processing needed
                    yield return null;
                    break;
                    
                case GameState.Ended:
                    // Game has ended, wait for restart
                    yield return new WaitUntil(() => currentGameState != GameState.Ended);
                    break;
                    
                default:
                    // For any other states, just wait a frame
                    yield return null;
                    break;
            }
        }
    }
    
    public override void ProcessChatMessage(string username, string message)
    {
        // Default behavior is to let the base class command processor handle it
        base.ProcessChatMessage(username, message);
    }
    
    // Handle answer command from user
    private void HandleAnswerCommand(string username, int answerIndex)
    {
        if (currentGameState != GameState.Active || isLoadingQuestion || currentQuestion == null)
            return;
            
        DebugLogger.LogUserInteraction(this, username, $"voted for answer {answerIndex + 1}", debugMode);
        
        entryManager.RecordVote(username, answerIndex);
        MarkUIForUpdate();
    }
    
    protected override void Cleanup()
    {
        base.Cleanup();
        entryManager?.ClearVotes();
    }
}