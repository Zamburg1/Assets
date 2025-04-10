using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

namespace Shared.UI
{
    /// <summary>
    /// Utility class for common UI animations
    /// </summary>
    public static class UIAnimationUtility
    {
        // Cache common WaitForSeconds to reduce garbage collection
        private static readonly WaitForSeconds Wait01Second = new WaitForSeconds(0.1f);
        private static readonly WaitForSeconds Wait02Second = new WaitForSeconds(0.2f);
        private static readonly WaitForSeconds Wait025Second = new WaitForSeconds(0.25f);
        private static readonly WaitForSeconds Wait05Second = new WaitForSeconds(0.5f);
        
        /// <summary>
        /// Animates a numeric counter with smooth interpolation
        /// </summary>
        /// <param name="monoBehaviour">MonoBehaviour instance to run the coroutine on</param>
        /// <param name="startValue">Starting value of the counter</param>
        /// <param name="endValue">Target value of the counter</param>
        /// <param name="duration">Duration of the animation in seconds (0 for auto calculation)</param>
        /// <param name="onValueChanged">Callback that receives the current value during animation</param>
        /// <param name="format">Optional format string, e.g. "{0} Gems" (uses formatted value in callback)</param>
        /// <param name="useSmoothing">Whether to use smoothstep easing</param>
        /// <returns>Coroutine handle that can be stored to stop the animation later</returns>
        public static Coroutine AnimateCounter(
            MonoBehaviour monoBehaviour, 
            float startValue, 
            float endValue, 
            float duration = 0f, 
            Action<float> onValueChanged = null,
            string format = "{0}",
            bool useSmoothing = true)
        {
            if (monoBehaviour == null || !monoBehaviour.gameObject.activeInHierarchy) return null;
            return monoBehaviour.StartCoroutine(AnimateCounterCoroutine(
                startValue, endValue, duration, onValueChanged, format, useSmoothing));
        }

        /// <summary>
        /// Animates a numeric counter displayed in a TextMeshProUGUI component
        /// </summary>
        /// <param name="monoBehaviour">MonoBehaviour instance to run the coroutine on</param>
        /// <param name="textComponent">TextMeshProUGUI component to update</param>
        /// <param name="startValue">Starting value of the counter</param>
        /// <param name="endValue">Target value of the counter</param>
        /// <param name="duration">Duration of the animation in seconds (0 for auto calculation)</param>
        /// <param name="format">Format string, e.g. "{0} Gems"</param>
        /// <param name="useSmoothing">Whether to use smoothstep easing</param>
        /// <returns>Coroutine handle that can be stored to stop the animation later</returns>
        public static Coroutine AnimateTextCounter(
            MonoBehaviour monoBehaviour,
            TextMeshProUGUI textComponent,
            float startValue,
            float endValue,
            float duration = 0f,
            string format = "{0}",
            bool useSmoothing = true)
        {
            if (monoBehaviour == null || textComponent == null || !monoBehaviour.gameObject.activeInHierarchy) return null;
            
            // Create a callback that updates the text component
            System.Action<float> updateText = (value) => {
                if (textComponent != null)
                    textComponent.text = string.Format(format, value);
            };
            
            return AnimateCounter(monoBehaviour, startValue, endValue, duration, updateText, format, useSmoothing);
        }
        
        private static IEnumerator AnimateCounterCoroutine(
            float startValue, 
            float endValue, 
            float duration,
            Action<float> onValueChanged,
            string format,
            bool useSmoothing)
        {
            // Prevent division by zero for instant animations
            float actualDuration = Mathf.Max(0.01f, duration);
            float timer = 0;
            float currentValue = startValue;
            
            // Initial update with starting value
            onValueChanged?.Invoke(currentValue);
            
            // Small increment for very short animations
            if (duration <= 0.1f)
            {
                yield return null;
                onValueChanged?.Invoke(endValue);
                yield break;
            }
            
            // Frame timing optimization - use fixed time steps for smoother animation
            // and reduce garbage by using cached WaitForSeconds when possible
            float timeStep;
            WaitForSeconds waitTime;
            
            // Choose an appropriate time step and cache based on duration
            if (duration <= 0.5f)
            {
                timeStep = 0.1f;
                waitTime = Wait01Second;
            }
            else if (duration <= 1.0f)
            {
                timeStep = 0.1f;
                waitTime = Wait01Second;
            }
            else if (duration <= 2.0f)
            {
                timeStep = 0.1f;
                waitTime = Wait01Second;
            }
            else if (duration <= 5.0f)
            {
                timeStep = 0.2f;
                waitTime = Wait02Second;
            }
            else
            {
                timeStep = 0.25f;
                waitTime = Wait025Second;
            }
            
            while (timer < actualDuration)
            {
                yield return waitTime;
                
                timer += timeStep;
                timer = Mathf.Min(timer, actualDuration);
                
                float progress = timer / actualDuration;
                
                // Apply easing if requested
                if (useSmoothing)
                {
                    // Smooth step easing function
                    progress = progress * progress * (3f - 2f * progress);
                }
                
                currentValue = Mathf.Lerp(startValue, endValue, progress);
                onValueChanged?.Invoke(currentValue);
            }
            
            // Ensure final value is exactly what was requested
            if (Mathf.Abs(currentValue - endValue) > 0.01f)
            {
                onValueChanged?.Invoke(endValue);
            }
        }
    }
} 