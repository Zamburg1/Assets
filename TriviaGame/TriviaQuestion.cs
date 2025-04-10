using System;
using UnityEngine;

namespace Shared.Trivia
{
    /// <summary>
    /// Data structure representing a trivia question
    /// </summary>
    [Serializable]
    public class TriviaQuestion
    {
        public string question;
        public string[] options;
        public int correctAnswer;
        public string difficulty;
        public string category;
        public int[] voteCounts = new int[4];

        // Default constructor
        public TriviaQuestion()
        {
            question = "";
            options = new string[4];
            correctAnswer = 0;
            difficulty = "easy";
            category = "";
            voteCounts = new int[4];
        }

        // Constructor with parameters
        public TriviaQuestion(string question, string[] options, int correctAnswer, string difficulty = "easy", string category = "")
        {
            this.question = question;
            this.options = options ?? new string[4];
            this.correctAnswer = Mathf.Clamp(correctAnswer, 0, 3);
            this.difficulty = difficulty;
            this.category = category;
            this.voteCounts = new int[4];
        }

        // Get the correct answer
        public string GetCorrectAnswerText()
        {
            if (options != null && correctAnswer >= 0 && correctAnswer < options.Length)
            {
                return options[correctAnswer];
            }
            return "Unknown";
        }

        // Check if an answer is correct
        public bool IsCorrectAnswer(int answerIndex)
        {
            return answerIndex == correctAnswer;
        }
    }
} 