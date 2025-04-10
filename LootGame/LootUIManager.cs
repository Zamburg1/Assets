using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using Shared.UI;

public class LootUIManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject gameUI;
    [SerializeField] private GameObject resultsUI;
    
    [Header("Chest Display")]
    [SerializeField] private GameObject[] chestObjects = new GameObject[8];
    [SerializeField] private TextMeshProUGUI[] chestEntryCountTexts = new TextMeshProUGUI[8];
    
    [Header("Game Info")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI gemPoolText;
    [SerializeField] private Slider timerSlider;
    
    [Header("Results Panel")]
    [SerializeField] private TextMeshProUGUI winningChestText;
    [SerializeField] private TextMeshProUGUI winnersText;
    [SerializeField] private TextMeshProUGUI gemsAwardedText;
    [SerializeField] private TextMeshProUGUI noWinnersText;
    
    private int winningChestIndex = -1;
    private Coroutine gemAnimationCoroutine;
    private int currentGemPoolAmount = 0;
    
    void Awake()
    {
        // Hide results UI at start
        if (gameUI != null) gameUI.SetActive(true);
        if (resultsUI != null) resultsUI.SetActive(false);
    }
    
    /// <summary>
    /// Updates the timer and slider based on the remaining time ratio
    /// </summary>
    public void UpdateTimer(float timeRemainingRatio)
    {
        // Update the slider
        if (timerSlider != null)
        {
            timerSlider.value = timeRemainingRatio;
        }
        
        // Update the timer text (with seconds remaining)
        if (timerText != null)
        {
            int secondsRemaining = Mathf.CeilToInt(999 * timeRemainingRatio); // Assuming max time is 999s
            timerText.text = $"{secondsRemaining}s";
        }
    }
    
    /// <summary>
    /// Updates the gem pool display with the new amount
    /// </summary>
    /// <param name="newAmount">The new gem pool amount</param>
    public void UpdateGemPool(int newAmount)
    {
        // Stop any existing animation
        StopGemAnimation();

        // If no text component, just update the stored value
        if (gemPoolText == null)
        {
            currentGemPoolAmount = newAmount;
            return;
        }

        // Start animation from current value to new value
        gemAnimationCoroutine = UIAnimationUtility.AnimateTextCounter(
            this,
            gemPoolText,
            currentGemPoolAmount,
            newAmount,
            0.5f,
            "{0} Gems",
            true);

        // Update the stored value
        currentGemPoolAmount = newAmount;
    }
    
    /// <summary>
    /// Updates the entry count display for a specific chest
    /// </summary>
    public void UpdateChestEntryCount(int chestIndex, int entryCount)
    {
        if (chestIndex >= 0 && chestIndex < chestEntryCountTexts.Length && chestEntryCountTexts[chestIndex] != null)
        {
            chestEntryCountTexts[chestIndex].text = entryCount.ToString();
        }
    }
    
    /// <summary>
    /// Updates all chest entry counts at once
    /// </summary>
    public void UpdateAllChestEntryCounts(int[] entryCounts)
    {
        for (int i = 0; i < Math.Min(entryCounts.Length, chestEntryCountTexts.Length); i++)
        {
            UpdateChestEntryCount(i, entryCounts[i]);
        }
    }
    
    /// <summary>
    /// Shows the results panel with the winning chest and winners
    /// </summary>
    public void ShowResults(int winningChestIndex, List<string> winners, int gemsPerWinner)
    {
        // Switch to results UI
        if (gameUI != null) gameUI.SetActive(false);
        if (resultsUI != null) resultsUI.SetActive(true);
        
        this.winningChestIndex = winningChestIndex;
        
        // Set the winning chest text
        if (winningChestText != null)
        {
            winningChestText.text = $"Chest {winningChestIndex + 1} contains the treasure!";
        }
        
        // Show winners or no winners message
        if (winners.Count > 0)
        {
            // Calculate total gems awarded
            int totalGemsAwarded = winners.Count * gemsPerWinner;
            
            // Format the winners list (limit to 10 displayed names)
            string winnersList = FormatWinnersList(winners, 10);
            
            // Display winners text
            if (winnersText != null)
            {
                winnersText.text = $"{winners.Count} player{(winners.Count != 1 ? "s" : "")} won!\n\n{winnersList}";
                winnersText.gameObject.SetActive(true);
            }
            
            // Display gems awarded text
            if (gemsAwardedText != null)
            {
                gemsAwardedText.text = $"{gemsPerWinner} gems each ({totalGemsAwarded} total)";
                gemsAwardedText.gameObject.SetActive(true);
            }
            
            // Hide no winners text
            if (noWinnersText != null)
            {
                noWinnersText.gameObject.SetActive(false);
            }
        }
        else
        {
            // Hide winners text
            if (winnersText != null)
            {
                winnersText.gameObject.SetActive(false);
            }
            
            // Hide gems awarded text
            if (gemsAwardedText != null)
            {
                gemsAwardedText.gameObject.SetActive(false);
            }
            
            // Show no winners text
            if (noWinnersText != null)
            {
                noWinnersText.text = "No one selected the winning chest!";
                noWinnersText.gameObject.SetActive(true);
            }
        }
    }
    
    /// <summary>
    /// Formats a list of winners for display, limiting the number shown
    /// </summary>
    private string FormatWinnersList(List<string> winners, int maxToShow)
    {
        if (winners == null || winners.Count == 0)
            return "No winners";
            
        if (winners.Count <= maxToShow)
        {
            return string.Join(", ", winners);
        }
        else
        {
            // Show a limited number of winners plus a count of others
            var visibleWinners = winners.GetRange(0, maxToShow);
            return string.Join(", ", visibleWinners) + $" and {winners.Count - maxToShow} others";
        }
    }
    
    /// <summary>
    /// Resets the UI for a new round
    /// </summary>
    public void ResetForNewRound()
    {
        // Return to game UI
        if (gameUI != null) gameUI.SetActive(true);
        if (resultsUI != null) resultsUI.SetActive(false);
        
        // Reset entry counts
        for (int i = 0; i < chestEntryCountTexts.Length; i++)
        {
            if (chestEntryCountTexts[i] != null)
            {
                chestEntryCountTexts[i].text = "0";
            }
        }
        
        // Reset gem pool and storage value
        currentGemPoolAmount = 0;
        UpdateGemPool(0);
        
        // Reset winning chest index
        winningChestIndex = -1;
    }
    
    /// <summary>
    /// Stops gem animations
    /// </summary>
    private void StopGemAnimation()
    {
        if (gemAnimationCoroutine != null)
        {
            StopCoroutine(gemAnimationCoroutine);
            gemAnimationCoroutine = null;
        }
    }
    
    /// <summary>
    /// OnDisable is called when the behaviour becomes disabled
    /// </summary>
    private void OnDisable()
    {
        StopGemAnimation();
    }

    /// <summary>
    /// OnDestroy is called when the object is being destroyed
    /// </summary>
    private void OnDestroy()
    {
        StopGemAnimation();
    }
} 