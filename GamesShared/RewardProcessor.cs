using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using System;

/// <summary>
/// Shared utility for processing winners and rewards across different games
/// </summary>
public class RewardProcessor : MonoBehaviour
{
    [Header("Shared Settings")]
    [Tooltip("The StreamElements API for sending points")]
    public StreamElementsAPI streamElements;
    
    [Tooltip("The WinnerTracker for recording winners")]
    public WinnerTracker winnerTracker;
    
    [Tooltip("The TwitchConnection for announcing winners")]
    public TwitchConnection twitchConnection;
    
    [Header("Performance Settings")]
    [Tooltip("Maximum number of winners to process at once (prevents rate limiting by StreamElements API)")]
    public int maxBatchSize = 20;
    
    [Tooltip("Initial delay between batches in milliseconds")]
    public int initialBatchDelayMs = 200;
    
    [Tooltip("Minimum delay between batches in milliseconds (adaptive)")]
    public int minBatchDelayMs = 100;
    
    [Tooltip("Maximum delay between batches in milliseconds (adaptive)")]
    public int maxBatchDelayMs = 1000;
    
    // Adaptive delay tracking
    private float currentBatchDelayMs;
    private bool isProcessing = false;
    
    // Public property to check if rewards are currently being processed
    public bool IsProcessing => isProcessing;
    
    private void Awake()
    {
        // Initialize with the default delay
        currentBatchDelayMs = initialBatchDelayMs;
    }
    
    /// <summary>
    /// Process a single winner
    /// </summary>
    public void ProcessWinner(string username, int gemAmount, string gameSource)
    {
        // Record winner in tracker
        if (winnerTracker != null)
        {
            winnerTracker.RecordWinner(username, gemAmount);
        }
        
        // Award points through StreamElements
        if (streamElements != null)
        {
            streamElements.AwardPoints(username, gemAmount);
        }
        
        // Announce winner in chat
        if (twitchConnection != null)
        {
            twitchConnection.SendChatMessage($"Congratulations to {username} for winning {gemAmount} gems in the {gameSource}!");
        }
    }
    
    /// <summary>
    /// Process multiple winners
    /// </summary>
    public void ProcessMultipleWinners(List<string> winners, int gemsPerWinner, string gameSource, string additionalMessage = "")
    {
        // Skip if already processing
        if (isProcessing)
        {
            Debug.LogWarning("Skipping ProcessMultipleWinners call - already processing a batch");
            return;
        }

        // Record all winners
        if (winnerTracker != null && winners.Count > 0)
        {
            winnerTracker.RecordMultipleWinners(winners, gemsPerWinner);
            Debug.Log($"Recorded {winners.Count} winners in WinnerTracker with {gemsPerWinner} gems each");
        }
        
        // Process winners in batches (non-coroutine version for direct calls)
        if (streamElements != null)
        {
            for (int i = 0; i < winners.Count; i += maxBatchSize)
            {
                int batchSize = Mathf.Min(maxBatchSize, winners.Count - i);
                for (int j = 0; j < batchSize; j++)
                {
                    string winner = winners[i + j];
                    streamElements.AwardPoints(winner, gemsPerWinner);
                }
            }
        }
        
        // Announce in chat using a summary message rather than tagging users
        if (twitchConnection != null && winners.Count > 0)
        {
            // For Trivia, use a specific format
            if (gameSource.Contains("Trivia"))
            {
                string answerNumber = string.Empty;
                
                // Try to extract answer number from additionalMessage if it exists
                if (!string.IsNullOrEmpty(additionalMessage))
                {
                    // Look for "Answer X" in the additional message
                    int answerIndex = additionalMessage.IndexOf("Answer ");
                    if (answerIndex >= 0 && answerIndex + 7 < additionalMessage.Length)
                    {
                        char possibleNumber = additionalMessage[answerIndex + 7];
                        if (char.IsDigit(possibleNumber))
                        {
                            answerNumber = possibleNumber.ToString();
                        }
                    }
                }
                
                string correctAnswerText = !string.IsNullOrEmpty(answerNumber) ? 
                    $"Answer {answerNumber} was correct! " : 
                    "The answer was correct! ";
                    
                twitchConnection.SendChatMessage($"{correctAnswerText}{winners.Count} winner{(winners.Count != 1 ? "s" : "")} each get {gemsPerWinner} gems. Congratulations!");
            }
            else
            {
                // For Giveaway and other games
                twitchConnection.SendChatMessage($"{winners.Count} winner{(winners.Count != 1 ? "s" : "")} received {gemsPerWinner} gems each from the {gameSource}! {additionalMessage}");
            }
        }
    }
    
    /// <summary>
    /// Coroutine version for processing winners with adaptive delays between batches
    /// </summary>
    public IEnumerator ProcessWinnersCoroutine(List<string> winners, int gemsPerWinner, Action<bool> onComplete = null)
    {
        bool success = true;
        isProcessing = true;
        
        if (streamElements != null)
        {
            // Process in batches to avoid allocation spikes
            for (int i = 0; i < winners.Count; i += maxBatchSize)
            {
                int batchSize = Mathf.Min(maxBatchSize, winners.Count - i);
                float startTime = Time.time;
                
                for (int j = 0; j < batchSize; j++)
                {
                    string winner = winners[i + j];
                    bool awardSuccess = streamElements.AwardPoints(winner, gemsPerWinner);
                    if (!awardSuccess) success = false;
                    
                    Debug.Log($"Awarded {gemsPerWinner} gems to {winner}");
                }
                
                // Calculate how long the API calls took
                float apiCallDuration = (Time.time - startTime) * 1000f; // Convert to ms
                
                // Adjust delay based on API response times
                if (apiCallDuration > 150f) // API is responding slowly
                {
                    // Increase delay to avoid rate limiting
                    currentBatchDelayMs = Mathf.Min(currentBatchDelayMs * 1.2f, maxBatchDelayMs);
                    Debug.Log($"API response is slow ({apiCallDuration:F1}ms), increasing batch delay to {currentBatchDelayMs:F1}ms");
                }
                else if (apiCallDuration < 50f) // API is responding quickly
                {
                    // Decrease delay to optimize throughput
                    currentBatchDelayMs = Mathf.Max(currentBatchDelayMs * 0.8f, minBatchDelayMs);
                    Debug.Log($"API response is fast ({apiCallDuration:F1}ms), decreasing batch delay to {currentBatchDelayMs:F1}ms");
                }
                
                // Add a small delay between batches to spread out processing
                if (i + batchSize < winners.Count)
                {
                    yield return new WaitForSeconds(currentBatchDelayMs / 1000f);
                }
            }
        }
        
        isProcessing = false;
        onComplete?.Invoke(success);
    }
    
    /// <summary>
    /// Format a list of winners for chat display (limits length)
    /// </summary>
    public static string FormatWinnersList(List<string> winners, int maxToShow = 5)
    {
        if (winners.Count <= maxToShow)
        {
            return string.Join(", ", winners);
        }
        else
        {
            // Show first N winners plus count of remaining
            var displayedWinners = new List<string>();
            for (int i = 0; i < maxToShow; i++)
            {
                displayedWinners.Add(winners[i]);
            }
            return string.Join(", ", displayedWinners) + $", and {winners.Count - maxToShow} more";
        }
    }
} 