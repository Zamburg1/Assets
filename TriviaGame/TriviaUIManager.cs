using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Shared.Trivia;
using Shared.UI;
using System;
using Alphasquad.GamesShared;

public class TriviaUIManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TriviaController triviaController;
    [SerializeField] private RectTransform mainContainer;

    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI questionText;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI resultsText;
    [SerializeField] private TextMeshProUGUI correctAnswerText;
    [SerializeField] private TextMeshProUGUI winnersText;
    
    [Header("Panels")]
    [SerializeField] private GameObject waitingPanel;
    [SerializeField] private GameObject questionPanel;
    [SerializeField] private GameObject resultsPanel;
    
    [Header("Options")]
    [SerializeField] private GameObject optionsContainer;
    [SerializeField] private GameObject optionPrefab;
    
    private List<GameObject> currentOptions = new List<GameObject>();
    private bool isInitialized = false;
    
    public void Initialize(TriviaController controller)
    {
        if (triviaController == null)
        {
            triviaController = controller;
        }
        
        // Ensure we have the required UI elements
        ValidateUIElements();
        
        // Set initial UI state
        UpdateWaitingState();
        
        isInitialized = true;
    }
    
    private void ValidateUIElements()
    {
        if (waitingPanel == null || questionPanel == null || resultsPanel == null)
        {
            DebugLogger.LogError(this, "One or more UI panels are missing!");
        }
        
        if (questionText == null || timerText == null || progressText == null)
        {
            DebugLogger.LogError(this, "One or more question UI elements are missing!");
        }
        
        if (resultsText == null || correctAnswerText == null || winnersText == null)
        {
            DebugLogger.LogError(this, "One or more results UI elements are missing!");
        }
    }
    
    public void UpdateWaitingState()
    {
        ShowPanel(waitingPanel);
        
        if (statusText != null)
        {
            statusText.text = "Waiting to start trivia...";
        }
    }
    
    public void UpdateActiveState(string question, List<string> options, float remainingTime, int currentQuestionIndex, int totalQuestions)
    {
        ShowPanel(questionPanel);
        
        // Update question text
        if (questionText != null)
        {
            questionText.text = question;
        }
        
        // Update timer
        if (timerText != null)
        {
            timerText.text = $"{Mathf.CeilToInt(remainingTime)}s";
        }
        
        // Update progress
        if (progressText != null)
        {
            progressText.text = $"Question {currentQuestionIndex + 1} of {totalQuestions}";
        }
        
        // Clear and regenerate options
        ClearOptions();
        GenerateOptions(options);
    }
    
    public void UpdateResultsState(string question, string correctAnswer, List<string> winners, float remainingTime)
    {
        ShowPanel(resultsPanel);
        
        // Update results text
        if (resultsText != null)
        {
            resultsText.text = question;
        }
        
        // Update correct answer text
        if (correctAnswerText != null)
        {
            correctAnswerText.text = $"Correct Answer: {correctAnswer}";
        }
        
        // Update winners text
        if (winnersText != null)
        {
            if (winners != null && winners.Count > 0)
            {
                string winnersList = winners.Count <= 5 
                    ? string.Join(", ", winners)
                    : $"{winners.Count} players";
                
                winnersText.text = $"Winners: {winnersList}";
            }
            else
            {
                winnersText.text = "No winners this round";
            }
        }
        
        // Update timer if applicable
        if (timerText != null)
        {
            timerText.text = $"{Mathf.CeilToInt(remainingTime)}s";
        }
    }
    
    public void UpdateEndedState()
    {
        ShowPanel(waitingPanel);
        
        if (statusText != null)
        {
            statusText.text = "Trivia game has ended";
        }
    }
    
    private void ShowPanel(GameObject panelToShow)
    {
        if (waitingPanel != null) waitingPanel.SetActive(panelToShow == waitingPanel);
        if (questionPanel != null) questionPanel.SetActive(panelToShow == questionPanel);
        if (resultsPanel != null) resultsPanel.SetActive(panelToShow == resultsPanel);
    }
    
    private void ClearOptions()
    {
        foreach (GameObject option in currentOptions)
        {
            Destroy(option);
        }
        
        currentOptions.Clear();
    }
    
    private void GenerateOptions(List<string> options)
    {
        if (optionsContainer == null || optionPrefab == null) return;
        
        for (int i = 0; i < options.Count; i++)
        {
            GameObject optionObj = Instantiate(optionPrefab, optionsContainer.transform);
            TextMeshProUGUI optionText = optionObj.GetComponentInChildren<TextMeshProUGUI>();
            
            if (optionText != null)
            {
                optionText.text = $"{(char)('A' + i)}. {options[i]}";
            }
            
            currentOptions.Add(optionObj);
        }
    }
    
    public void UpdateQuestionTimer(float remainingTime)
    {
        if (timerText != null)
        {
            timerText.text = $"{Mathf.CeilToInt(remainingTime)}s";
        }
    }
    
    public void UpdateResultsTimer(float remainingTime)
    {
        if (timerText != null && resultsPanel.activeSelf)
        {
            timerText.text = $"{Mathf.CeilToInt(remainingTime)}s";
        }
    }
    
    public void ResetUI()
    {
        ClearOptions();
        ShowPanel(waitingPanel);
    }
}