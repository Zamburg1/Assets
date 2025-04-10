using System;
using UnityEngine;
using System.Collections.Generic;

namespace Shared.Trivia
{
    public class TriviaQuestionBehaviour : MonoBehaviour
    {
        // Hide in inspector to prevent accidental writing
        [HideInInspector]
        [SerializeField]
        private string question;
        
        // Always have 4 answer options
        [HideInInspector]
        [SerializeField]
        private string[] answers = new string[4];
        
        [HideInInspector]
        [SerializeField]
        private int correctAnswerIndex;
        
        [HideInInspector]
        [SerializeField]
        private string category;
        
        [HideInInspector]
        [SerializeField]
        private string difficulty;

        public string Question {
            get { return question; }
            set { question = value; }
        }

        public string[] Answers {
            get { return answers; }
            set { 
                if (value == null || value.Length == 0) {
                    answers = new string[4];
                } else if (value.Length != 4) {
                    // Always ensure exactly 4 answers
                    answers = new string[4];
                    for (int i = 0; i < Math.Min(value.Length, 4); i++) {
                        answers[i] = value[i];
                    }
                } else {
                    answers = value;
                }
            }
        }

        public int CorrectAnswerIndex {
            get { return correctAnswerIndex; }
            set { correctAnswerIndex = Mathf.Clamp(value, 0, 3); } // Ensure it's within the valid range for 4 answers
        }

        public string Category {
            get { return category; }
            set { category = value; }
        }

        public string Difficulty {
            get { return difficulty; }
            set { difficulty = value; }
        }

        public void Initialize(string question, string[] answers, int correctAnswerIndex, string category, string difficulty)
        {
            this.question = question;
            this.Answers = answers; // Use the property to ensure 4 answers
            this.CorrectAnswerIndex = correctAnswerIndex; // Use the property to ensure valid index
            this.category = category;
            this.difficulty = difficulty;
        }
    }

    public class TriviaQuestionList : MonoBehaviour
    {
        [HideInInspector]
        [SerializeField]
        private TriviaQuestionBehaviour[] questions;

        public TriviaQuestionBehaviour[] Questions {
            get { return questions; }
            set { questions = value; }
        }
    }
}