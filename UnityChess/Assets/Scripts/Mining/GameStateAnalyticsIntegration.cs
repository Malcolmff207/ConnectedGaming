using UnityEngine;
using System.Collections;

/// <summary>
/// Extension component for GameStateManager that adds analytics capabilities
/// </summary>
[RequireComponent(typeof(GameStateManager))]
public class GameStateAnalyticsIntegration : MonoBehaviour
{
    private GameStateManager gameStateManager;
    private AnalyticsManager analyticsManager;
    
    private void Awake()
    {
        // Get references
        gameStateManager = GetComponent<GameStateManager>();
        
        // Find analytics manager
        analyticsManager = AnalyticsManager.Instance;
        if (analyticsManager == null)
        {
            analyticsManager = FindObjectOfType<AnalyticsManager>();
            if (analyticsManager == null)
            {
                Debug.LogWarning("AnalyticsManager not found. Creating a new instance.");
                analyticsManager = new GameObject("AnalyticsManager").AddComponent<AnalyticsManager>();
            }
        }
    }
    
    private void Start()
    {
        // Monitor for save/load events
        StartCoroutine(MonitorGameStateEvents());
    }
    
    private IEnumerator MonitorGameStateEvents()
    {
        // Keep checking for save/load events
        while (true)
        {
            // Check for active save panel to detect save events
            Transform savePanel = transform.Find("SaveGamePanel");
            if (savePanel != null && savePanel.gameObject.activeSelf)
            {
                // Wait for the panel to become inactive (save completed)
                while (savePanel.gameObject.activeSelf)
                {
                    yield return new WaitForSeconds(0.5f);
                }
                
                // Panel closed, likely a save was performed
                Debug.Log("Detected game save event");
                
                // Extract game data from PlayerPrefs as a fallback approach
                string saveId = PlayerPrefs.GetString("LastSaveId", "");
                string saveName = PlayerPrefs.GetString("LastSaveName", "Unnamed Save");
                
                if (!string.IsNullOrEmpty(saveId))
                {
                    // Log the save event
                    analyticsManager.LogGameStateSaved(saveId, saveName);
                }
            }
            
            // Check for active load panel to detect load events
            Transform loadPanel = transform.Find("LoadGamePanel");
            if (loadPanel != null && loadPanel.gameObject.activeSelf)
            {
                // Wait for the panel to become inactive (load completed or canceled)
                while (loadPanel.gameObject.activeSelf)
                {
                    yield return new WaitForSeconds(0.5f);
                }
                
                // Extract game data from PlayerPrefs
                string lastLoadId = PlayerPrefs.GetString("LastLoadId", "");
                string lastLoadName = PlayerPrefs.GetString("LastLoadName", "Unnamed Save");
                
                if (!string.IsNullOrEmpty(lastLoadId))
                {
                    // Log the load event
                    analyticsManager.LogGameStateLoaded(lastLoadId, lastLoadName);
                }
            }
            
            yield return new WaitForSeconds(0.5f);
        }
    }
    
    // Called when a game is saved
    public void OnGameSaved(string saveId, string saveName)
    {
        if (analyticsManager != null)
        {
            analyticsManager.LogGameStateSaved(saveId, saveName);
            Debug.Log($"Analytics: Game saved - ID: {saveId}, Name: {saveName}");
        }
    }
    
    // Called when a game is loaded
    public void OnGameLoaded(string saveId, string saveName)
    {
        if (analyticsManager != null)
        {
            analyticsManager.LogGameStateLoaded(saveId, saveName);
            Debug.Log($"Analytics: Game loaded - ID: {saveId}, Name: {saveName}");
        }
    }
}