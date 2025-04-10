using System;
using System.Collections.Generic;
using UnityEngine;

namespace Alphasquad.GamesShared
{
    /// <summary>
    /// Centralized UI management system for all mini-games
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("UI Update Settings")]
        [Tooltip("How often the UI is allowed to update (in seconds)")]
        [SerializeField] private float uiUpdateThrottle = 0.1f;
        
        private bool uiNeedsUpdate = false;
        private float lastUIUpdateTime = 0f;
        
        // Event for UI update
        public event Action OnUIUpdateRequested;
        
        /// <summary>
        /// Flag to determine if UI updates are currently throttled
        /// </summary>
        public bool IsThrottled => Time.time - lastUIUpdateTime < uiUpdateThrottle;
        
        /// <summary>
        /// Sets the throttle time for UI updates
        /// </summary>
        /// <param name="throttleTime">Time in seconds between UI updates</param>
        public void SetUpdateThrottle(float throttleTime)
        {
            uiUpdateThrottle = Mathf.Max(0.01f, throttleTime);
        }
        
        /// <summary>
        /// Marks the UI for update on the next available frame
        /// </summary>
        public void MarkUIForUpdate()
        {
            uiNeedsUpdate = true;
        }
        
        /// <summary>
        /// Updates the UI immediately, bypassing the throttle
        /// </summary>
        public void UpdateUIImmediate()
        {
            lastUIUpdateTime = Time.time;
            uiNeedsUpdate = false;
            OnUIUpdateRequested?.Invoke();
        }
        
        /// <summary>
        /// Call this from Update method of controllers to check if UI needs updating
        /// </summary>
        public void CheckForUIUpdate()
        {
            if (uiNeedsUpdate && !IsThrottled)
            {
                UpdateUIImmediate();
            }
        }
        
        #region Game-specific UI Elements
        
        // Dictionary to store references to UI elements by their ID
        private Dictionary<string, GameObject> uiElements = new Dictionary<string, GameObject>();
        
        /// <summary>
        /// Registers a UI element with a specific ID
        /// </summary>
        /// <param name="elementId">Unique identifier for the UI element</param>
        /// <param name="element">Reference to the GameObject</param>
        public void RegisterUIElement(string elementId, GameObject element)
        {
            if (element == null)
            {
                DebugLogger.LogWarning(this, $"Attempted to register null UI element with ID: {elementId}");
                return;
            }
            
            uiElements[elementId] = element;
        }
        
        /// <summary>
        /// Gets a registered UI element by ID
        /// </summary>
        /// <param name="elementId">ID of the element to retrieve</param>
        /// <returns>The GameObject if found, null otherwise</returns>
        public GameObject GetUIElement(string elementId)
        {
            if (uiElements.TryGetValue(elementId, out var element))
            {
                return element;
            }
            
            DebugLogger.LogWarning(this, $"UI element with ID '{elementId}' not found");
            return null;
        }
        
        /// <summary>
        /// Shows a UI element by ID
        /// </summary>
        /// <param name="elementId">ID of the element to show</param>
        public void ShowUIElement(string elementId)
        {
            var element = GetUIElement(elementId);
            if (element != null)
            {
                element.SetActive(true);
                MarkUIForUpdate();
            }
        }
        
        /// <summary>
        /// Hides a UI element by ID
        /// </summary>
        /// <param name="elementId">ID of the element to hide</param>
        public void HideUIElement(string elementId)
        {
            var element = GetUIElement(elementId);
            if (element != null)
            {
                element.SetActive(false);
                MarkUIForUpdate();
            }
        }
        
        /// <summary>
        /// Sets the active state of a UI element
        /// </summary>
        /// <param name="elementId">ID of the element</param>
        /// <param name="active">Whether the element should be active</param>
        public void SetUIElementActive(string elementId, bool active)
        {
            var element = GetUIElement(elementId);
            if (element != null)
            {
                element.SetActive(active);
                MarkUIForUpdate();
            }
        }
        
        #endregion
        
        #region Panel Management
        
        // Dictionary to track active panels
        private Dictionary<string, GameObject> gamePanels = new Dictionary<string, GameObject>();
        
        /// <summary>
        /// Registers a panel with a specific ID
        /// </summary>
        /// <param name="panelId">Unique identifier for the panel</param>
        /// <param name="panel">Reference to the panel GameObject</param>
        public void RegisterPanel(string panelId, GameObject panel)
        {
            if (panel == null)
            {
                DebugLogger.LogWarning(this, $"Attempted to register null panel with ID: {panelId}");
                return;
            }
            
            gamePanels[panelId] = panel;
        }
        
        /// <summary>
        /// Shows a specific panel and hides all others
        /// </summary>
        /// <param name="panelId">ID of the panel to show</param>
        public void ShowPanel(string panelId)
        {
            foreach (var panel in gamePanels)
            {
                panel.Value.SetActive(panel.Key == panelId);
            }
            
            MarkUIForUpdate();
        }
        
        /// <summary>
        /// Gets a registered panel by ID
        /// </summary>
        /// <param name="panelId">ID of the panel to retrieve</param>
        /// <returns>The GameObject if found, null otherwise</returns>
        public GameObject GetPanel(string panelId)
        {
            if (gamePanels.TryGetValue(panelId, out var panel))
            {
                return panel;
            }
            
            DebugLogger.LogWarning(this, $"Panel with ID '{panelId}' not found");
            return null;
        }
        
        #endregion
    }
} 