using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;
using System;
using System.Collections.Generic;
using Shared.Core;

// Add enum for MultiplierType
public enum MultiplierType
{
    None,
    FixedMultiplier,
    PerParticipant,
    GlobalEntryBoost,
    Special
}

public class UIManager : MonoBehaviour
{
    [Header("Recent Winners Display")]
    public TextMeshProUGUI recentWinnersTitle;
    public TextMeshProUGUI[] recentWinnerNameSlots = new TextMeshProUGUI[5];
    public TextMeshProUGUI[] recentWinnerCountSlots = new TextMeshProUGUI[5];
    
    [Header("Daily Winners Display")]
    public TextMeshProUGUI dailyWinnersTitle;
    public TextMeshProUGUI[] dailyWinnerNameSlots = new TextMeshProUGUI[5];
    public TextMeshProUGUI[] dailyWinnerCountSlots = new TextMeshProUGUI[5];

    [Header("Info Panel Elements")]
    [SerializeField] private GameObject infoPanel;
    [SerializeField] private TextMeshProUGUI participantCountText;
    [SerializeField] private TextMeshProUGUI uniqueParticipantCountText;
    [SerializeField] private TextMeshProUGUI gemPoolText;
    [SerializeField] private TextMeshProUGUI multiplierText;
    [SerializeField] private TextMeshProUGUI lastWinnerNameText;
    [SerializeField] private TextMeshProUGUI lastWinnerGemsText;

    [Header("Entry Panel Elements")]
    public GameObject entryAnnouncementPanel;
    public TextMeshProUGUI entryAnnouncementText; // Username
    public TextMeshProUGUI entryAnnouncementCountText; // Personal entry count
    public TextMeshProUGUI ticketCountText; // Current ticket count
    
    [Header("Result Panel Elements")]
    public GameObject winnerPanel;
    public TextMeshProUGUI winnerDisplayText; // Username
    public TextMeshProUGUI winnerEntryCountText; // Personal entry count
    public TextMeshProUGUI wonGemsText; // Won gems
    
    [Header("Animation Settings")]
    [Tooltip("Duration in seconds for count animations (e.g., gem pool updates)")]
    public float countIncrementDuration = 1.5f;
    
    [Tooltip("Time in seconds between updates during animations")]
    public float updateInterval = 0.05f;

    [Header("Multiplier State UI")]
    public GameObject multiplierStatePanel;
    public TextMeshProUGUI multiplierStateText;
    
    // Timer Display - consolidate to a single fuseImage
    [Header("Timer Display")]
    public Image fuseImage; // Fuse timer image
    
    private GiveawayController controller;
    private float announcementDuration;
    private Coroutine currentEntryAnnouncement;
    private Coroutine currentWinnerDisplay;
    private Coroutine currentGemCountAnimation;
    private WinnerTracker winnerTracker;
    private TicketManager ticketManager;
    private int displayedGemCount = 0;
    private int targetGemCount = 0;
    private int currentDisplayedGems = 0;
    private string lastWinnerName = "";
    private string lastWinnerColor = "";
    private int lastWinnerGems = 0;
    
    // Constants for animations
    private const float GEM_COUNTER_SPEED = 100f; // Gems per second for animation
    private const float MIN_ANIMATION_TIME = 0.5f; // Minimum animation time
    private const float MAX_ANIMATION_TIME = 2.0f; // Maximum animation time
    
    private void SetupCanvas(GameObject panel, int sortingOrder)
    {
        Canvas canvas = panel.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = panel.AddComponent<Canvas>();
        }
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;
    }
    
    void Start()
    {
        // Initialize if not already initialized
        if (controller == null)
        {
            controller = FindAnyObjectByType<GiveawayController>();
            
            if (controller != null)
            {
                Initialize(controller, 2.0f); // Default announcement duration
            }
            else
            {
                Debug.LogWarning("GiveawayController not found. UI may not work correctly.");
            }
        }
    }
    
    public void Initialize(GiveawayController controller, float announcementDuration)
    {
        this.controller = controller;
        this.announcementDuration = announcementDuration;
        
        winnerTracker = FindAnyObjectByType<WinnerTracker>();
        if (winnerTracker == null)
        {
            Debug.LogWarning("WinnerTracker not found, winner tracking will not work");
        }
        
        ticketManager = FindAnyObjectByType<TicketManager>();
        if (ticketManager == null)
        {
            Debug.LogWarning("TicketManager not found, ticket counts will not be displayed");
        }
        
        // Set up proper canvas sorting for all panels
        // Info panel is always at the back
        if (infoPanel != null) SetupCanvas(infoPanel, 10);
        // Entry announcement appears over info panel
        if (entryAnnouncementPanel != null) SetupCanvas(entryAnnouncementPanel, 20);
        // Winner panel is on top of everything
        if (winnerPanel != null) SetupCanvas(winnerPanel, 30);
        
        // Initialize displays
        SetupPanelVisibility();
        UpdateWinnerDisplays();
        UpdateEntryCount();
        UpdateGemPool(0);
        UpdateParticipantCount(0);
        UpdateStatus(false);
    }
    
    private void SetupPanelVisibility()
    {
        // Info panel should always be visible
        if (infoPanel != null) infoPanel.SetActive(true);
        
        // Other panels start hidden
        if (entryAnnouncementPanel != null) entryAnnouncementPanel.SetActive(false);
        if (winnerPanel != null) winnerPanel.SetActive(false);
    }

    // This replaces the old HideAnnouncements method
    private void ResetTemporaryPanels()
    {
        // Hide temporary panels but keep info panel visible
        if (entryAnnouncementPanel != null) entryAnnouncementPanel.SetActive(false);
        if (winnerPanel != null) winnerPanel.SetActive(false);
    }

    // Method from GiveawayUIManager for updating participant count
    public void UpdateParticipantCount(int count)
    {
        if (participantCountText != null)
        {
            participantCountText.text = $"Participants: {count}";
        }
    }
    
    // Add missing method for UpdateUniqueParticipantCount
    public void UpdateUniqueParticipantCount(int count)
    {
        if (uniqueParticipantCountText != null)
        {
            uniqueParticipantCountText.text = $"Unique Participants: {count}";
        }
    }
    
    // From GiveawayUIManager
    public void UpdateStatus(bool isActive)
    {
        if (multiplierStateText != null)
        {
            multiplierStateText.text = isActive ? 
                "Giveaway Active! Type !alphasquad to enter." : 
                "Giveaway is currently inactive.";
        }
    }
    
    // Method to update gem pool with animation from GiveawayUIManager
    public void UpdateGemPool(int gemCount)
    {
        if (gemPoolText != null)
        {
            gemPoolText.text = $"Gem Pool: {gemCount}";
            targetGemCount = gemCount;
            
            // Using AnimateGemCounter directly is unnecessary since we're not displaying an animation
            // StartCoroutine(AnimateGemCounter());
        }
    }
    
    // Optimized gem counter animation from GiveawayUIManager
    private IEnumerator AnimateGemCounter()
    {
        // If the counter is already at the target, nothing to do
        if (currentDisplayedGems == targetGemCount)
        {
            yield break;
        }
        
        // Calculate duration based on difference
        int difference = Mathf.Abs(targetGemCount - currentDisplayedGems);
        float duration = Mathf.Clamp(difference / GEM_COUNTER_SPEED, MIN_ANIMATION_TIME, MAX_ANIMATION_TIME);
        
        float startTime = Time.time;
        float endTime = startTime + duration;
        int startValue = currentDisplayedGems;
        
        while (Time.time < endTime)
        {
            // Calculate the new value based on time
            float progress = (Time.time - startTime) / duration;
            
            // Use a slightly eased curve for more professional feel
            progress = Mathf.SmoothStep(0, 1, progress);
            
            currentDisplayedGems = Mathf.RoundToInt(Mathf.Lerp(startValue, targetGemCount, progress));
            
            // Update UI - use either gemPoolText or from GiveawayUIManager implementation
            if (gemPoolText != null)
            {
                gemPoolText.text = $"Gem Pool: {currentDisplayedGems}";
            }
            
            yield return null;
        }
        
        // Ensure we end at exactly the target value
        currentDisplayedGems = targetGemCount;
        if (gemPoolText != null)
        {
            gemPoolText.text = $"Gem Pool: {targetGemCount}";
        }
    }
    
    // Legacy method for compatibility with existing code
    public void UpdateUniqueEntryCount(int uniqueCount)
    {
        if (gemPoolText == null) return;
        
        // For backward compatibility
        targetGemCount = uniqueCount;
        
        // Start animation if it's not already running
        if (currentGemCountAnimation == null)
        {
            currentGemCountAnimation = StartCoroutine(AnimateGemCount());
        }
    }
    
    // Legacy animation method for backward compatibility
    private IEnumerator AnimateGemCount()
    {
        // Start with current displayed value
        int startValue = displayedGemCount;
        int endValue = targetGemCount;
        float timeElapsed = 0;
        
        // If we're already at the target, just exit
        if (startValue == endValue)
        {
            currentGemCountAnimation = null;
            yield break;
        }
        
        // Animate the value over time
        while (timeElapsed < countIncrementDuration)
        {
            timeElapsed += updateInterval;
            float progress = Mathf.Clamp01(timeElapsed / countIncrementDuration);
            
            // Ease-in-out function for smoother progression
            float t = progress < 0.5f ? 2 * progress * progress : -1 + (4 - 2 * progress) * progress;
            
            // Calculate the current value
            displayedGemCount = (int)Mathf.Lerp(startValue, endValue, t);
            
            // Update the text
            if (gemPoolText != null)
            {
                gemPoolText.text = $"Gem Pool: {displayedGemCount}";
            }
            
            yield return new WaitForSeconds(updateInterval);
        }
        
        // Ensure we end with the exact target value
        displayedGemCount = endValue;
        if (gemPoolText != null)
        {
            gemPoolText.text = $"Gem Pool: {displayedGemCount}";
        }
        
        // Check if target changed during animation
        if (targetGemCount != endValue)
        {
            // If target changed, start another animation
            currentGemCountAnimation = StartCoroutine(AnimateGemCount());
        }
        else
        {
            currentGemCountAnimation = null;
        }
    }
    
    // Show winner without animations or confetti
    public void ShowWinner(string winnerUsername, int gemAmount)
    {
        if (string.IsNullOrEmpty(winnerUsername))
        {
            if (winnerDisplayText != null)
            {
                winnerDisplayText.text = "No eligible participants this round.";
            }
        }
        else
        {
            // Check if we have color information for this winner
            string colorHex = "#FFFFFF"; // Default white
            
            if (winnerTracker != null)
            {
                var recentWinners = winnerTracker.GetRecentWinners();
                var winner = recentWinners.Find(w => w.username.Equals(winnerUsername, StringComparison.OrdinalIgnoreCase));
                if (winner != null && !string.IsNullOrEmpty(winner.color))
                {
                    colorHex = winner.color;
                }
            }
            
            // Create colored winner announcement without animation
            if (winnerDisplayText != null)
            {
                string coloredUsername = $"<color={colorHex}>{winnerUsername}</color>";
                winnerDisplayText.text = $"Congratulations {coloredUsername}!\nYou won {gemAmount} gems!";
            }
            
            // Update the last winner information
            lastWinnerName = winnerUsername;
            lastWinnerGems = gemAmount;
            lastWinnerColor = colorHex;
            
            // Update the last winner display
            UpdateLastWinnerDisplay();
        }
        
        if (winnerPanel != null)
        {
            winnerPanel.SetActive(true);
        }
    }
    
    // Legacy method to maintain compatibility
    public void DisplayWinner(string winnerName, int gemsAwarded)
    {
        ShowWinner(winnerName, gemsAwarded);
    }
    
    // Hide the winner display
    public void HideWinner()
    {
        if (winnerPanel != null)
        {
            winnerPanel.SetActive(false);
        }
    }
    
    private void UpdateLastWinnerDisplay()
    {
        if (lastWinnerNameText != null && !string.IsNullOrEmpty(lastWinnerName))
        {
            // Update name with color
            Color color;
            if (!string.IsNullOrEmpty(lastWinnerColor) && ColorUtility.TryParseHtmlString(lastWinnerColor, out color))
            {
                lastWinnerNameText.color = color;
            }
            else
            {
                lastWinnerNameText.color = Color.white;
            }
            
            lastWinnerNameText.text = lastWinnerName;
        }
        
        if (lastWinnerGemsText != null)
        {
            lastWinnerGemsText.text = $"{lastWinnerGems}";
        }
    }
    
    private void StopWinnerDisplay()
    {
        if (currentWinnerDisplay != null)
        {
            StopCoroutine(currentWinnerDisplay);
            currentWinnerDisplay = null;
        }
    }
    
    private void StopEntryAnnouncement()
    {
        if (currentEntryAnnouncement != null)
        {
            StopCoroutine(currentEntryAnnouncement);
            currentEntryAnnouncement = null;
            
            if (entryAnnouncementPanel != null)
                entryAnnouncementPanel.SetActive(false);
        }
    }
    
    // Safely stop all coroutines
    private new void StopAllCoroutines()
    {
        base.StopAllCoroutines();
        
        // Reset references to prevent potential leaks
        currentEntryAnnouncement = null;
        currentWinnerDisplay = null;
        currentGemCountAnimation = null;
    }
    
    // When disabled, ensure we clean up
    private void OnDisable()
    {
        StopAllCoroutines();
    }
    
    public void ShowEntryAnnouncement(string username, int entryCount, float duration)
    {
        // Stop any existing announcement first
        StopEntryAnnouncement();
        
        // Set the announcement text
        if (entryAnnouncementText != null)
            entryAnnouncementText.text = username;
            
        // Set the entry count
        if (entryAnnouncementCountText != null)
            entryAnnouncementCountText.text = $"{entryCount}";
            
        // Show the announcement panel
        if (entryAnnouncementPanel != null)
            entryAnnouncementPanel.SetActive(true);
        
        // Show ticket count if available
        if (ticketManager != null && ticketCountText != null)
        {
            ticketCountText.text = $"{ticketManager.GetRemainingTickets(username)}";
        }
        
        // Update total counts
        UpdateEntryCount();
        
        // Start the temporary announcement display with the specified duration
        currentEntryAnnouncement = StartCoroutine(ShowTemporaryAnnouncement(duration));
    }
    
    // Keep the old version for backward compatibility
    public void ShowEntryAnnouncement(string username, int entryCount)
    {
        ShowEntryAnnouncement(username, entryCount, announcementDuration);
    }
    
    private IEnumerator ShowTemporaryAnnouncement(float duration)
    {
        yield return new WaitForSeconds(duration);
        
        // Hide the announcement panel
        if (entryAnnouncementPanel != null)
            entryAnnouncementPanel.SetActive(false);
            
        // Clear the coroutine reference
        currentEntryAnnouncement = null;
    }
    
    // Modify the old version for backward compatibility
    private IEnumerator ShowTemporaryAnnouncement()
    {
        yield return StartCoroutine(ShowTemporaryAnnouncement(announcementDuration));
    }
    
    // Update all winner displays with data from the WinnerTracker
    private void UpdateWinnerDisplays()
    {
        if (winnerTracker == null) return;
        
        // Update recent winners display
        var recentWinners = winnerTracker.GetRecentWinners();
        
        // Update daily winners
        var dailyWinners = winnerTracker.GetTopDailyWinners();
        
        // Update recent winners display slots
        for (int i = 0; i < recentWinnerNameSlots.Length && i < recentWinnerCountSlots.Length; i++)
        {
            if (i < recentWinners.Count && recentWinnerNameSlots[i] != null && recentWinnerCountSlots[i] != null)
            {
                var winner = recentWinners[i];
                
                // Apply color formatting based on role
                Color winnerColor;
                if (!string.IsNullOrEmpty(winner.color) && ColorUtility.TryParseHtmlString(winner.color, out winnerColor))
                {
                    recentWinnerNameSlots[i].color = winnerColor;
                }
                else
                {
                    recentWinnerNameSlots[i].color = Color.white;
                }
                
                recentWinnerNameSlots[i].text = winner.username;
                recentWinnerCountSlots[i].text = $"{winner.gems}";
                
                recentWinnerNameSlots[i].gameObject.SetActive(true);
                recentWinnerCountSlots[i].gameObject.SetActive(true);
            }
            else
            {
                if (recentWinnerNameSlots[i] != null)
                    recentWinnerNameSlots[i].gameObject.SetActive(false);
                    
                if (recentWinnerCountSlots[i] != null)
                    recentWinnerCountSlots[i].gameObject.SetActive(false);
            }
        }
        
        // Update daily winners display slots
        for (int i = 0; i < dailyWinnerNameSlots.Length && i < dailyWinnerCountSlots.Length; i++)
        {
            if (i < dailyWinners.Count && dailyWinnerNameSlots[i] != null && dailyWinnerCountSlots[i] != null)
            {
                var winner = dailyWinners[i];
                
                // Apply color formatting based on role
                Color winnerColor;
                if (!string.IsNullOrEmpty(winner.color) && ColorUtility.TryParseHtmlString(winner.color, out winnerColor))
                {
                    dailyWinnerNameSlots[i].color = winnerColor;
                }
                else
                {
                    dailyWinnerNameSlots[i].color = Color.white;
                }
                
                dailyWinnerNameSlots[i].text = winner.username;
                dailyWinnerCountSlots[i].text = $"{winner.gems}";
                
                dailyWinnerNameSlots[i].gameObject.SetActive(true);
                dailyWinnerCountSlots[i].gameObject.SetActive(true);
            }
            else
            {
                if (dailyWinnerNameSlots[i] != null)
                    dailyWinnerNameSlots[i].gameObject.SetActive(false);
                    
                if (dailyWinnerCountSlots[i] != null)
                    dailyWinnerCountSlots[i].gameObject.SetActive(false);
            }
        }
    }
    
    public void PrepareForNewGiveaway()
    {
        // Reset all UI elements for a new giveaway
        UpdateParticipantCount(0);
        UpdateGemPool(0);
        UpdateStatus(true);
        
        // Hide the winner panel if it's showing
        HideWinner();
        
        // Hide the entry announcement if it's showing
        StopEntryAnnouncement();
        
        // Update winner displays
        UpdateWinnerDisplays();
        
        // Update the last winner display
        UpdateLastWinnerDisplay();
    }
    
    public void UpdateEntryCount()
    {
        if (controller != null)
        {
            UpdateParticipantCount(controller.GetCurrentEntryCount());
        }
    }
    
    public void UpdateMultiplierStateInfo(MultiplierState state)
    {
        if (multiplierStatePanel == null || multiplierStateText == null) return;
        
        if (state == null)
        {
            multiplierStatePanel.SetActive(false);
            return;
        }
        
        // Activate the panel
        multiplierStatePanel.SetActive(true);
        
        // Fixed: Use the properties available in MultiplierState (name, multiplier)
        // Set the text based on multiplier state
        if (state.multiplier <= 1.0f) 
        {
            multiplierStateText.text = "No Multiplier Active";
        }
        else if (state.multiplier > 1.0f && state.multiplier < 2.0f)
        {
            multiplierStateText.text = $"{state.multiplier}x Multiplier Active!";
        }
        else if (state.multiplier >= 2.0f)
        {
            multiplierStateText.text = $"{state.multiplier}x Multiplier Active!";
        }
        else
        {
            multiplierStateText.text = $"Special Event: {state.name}";
        }
        
        // Set text color to match state color
        if (multiplierStateText != null)
        {
            multiplierStateText.color = state.stateColor;
        }
    }
    
    public bool IsResultPanelActive()
    {
        return winnerPanel != null && winnerPanel.activeSelf;
    }
    
    // Added from GiveawayUIManager to ensure compatibility
    public void UpdateWinnerDisplay(List<WinnerTracker.Winner> winners)
    {
        if (winnerDisplayText != null && winners != null)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            
            foreach (var winner in winners)
            {
                // Add colored username
                sb.AppendLine($"<color={winner.color}>{winner.username}</color> - {winner.gems} gems");
            }
            
            winnerDisplayText.text = sb.ToString();
        }
    }

    // Update timer with the fuse image
    public void UpdateFuseTimer(float remainingTime, float totalDuration)
    {
        if (fuseImage != null)
        {
            fuseImage.fillAmount = Mathf.Max(0, remainingTime / totalDuration);
        }
    }

    public void UpdateMultiplier(float multiplier)
    {
        if (multiplierText != null)
        {
            multiplierText.text = $"Multiplier: {multiplier}x";
        }
    }
}