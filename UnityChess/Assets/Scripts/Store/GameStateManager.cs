using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityChess;
using Unity.Netcode;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;

/// <summary>
/// Manages saving and restoring game states to/from Firebase
/// </summary>
public class GameStateManager : MonoBehaviour
{
    // Singleton instance
    public static GameStateManager Instance { get; private set; }
    
    // Firebase references
    private DatabaseReference databaseReference;
    private bool firebaseInitialized = false;
    
    // UI elements
    [Header("UI References")]
    [SerializeField] private GameObject saveGamePanel;
    [SerializeField] private GameObject loadGamePanel;
    [SerializeField] private Transform savedGamesContainer;
    [SerializeField] private GameObject savedGameItemPrefab;
    [SerializeField] private TMP_InputField saveGameNameInput;
    [SerializeField] private TextMeshProUGUI statusText;
    
    // Cached saved games
    private List<SavedGameInfo> savedGames = new List<SavedGameInfo>();
    
    [Serializable]
    public class SavedGameInfo
    {
        public string Id;
        public string Name;
        public string Date;
        public string FEN;
        public string PlayerWhite;
        public string PlayerBlack;
        public int MoveCount;
    }
    
    private void Awake()
    {
        // Setup singleton instance
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    
    private void Start()
    {
        // Initialize Firebase
        InitializeFirebase();
        
        // Hide UI panels
        if (saveGamePanel != null)
            saveGamePanel.SetActive(false);
            
        if (loadGamePanel != null)
            loadGamePanel.SetActive(false);
    }
    
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
    
    private async void InitializeFirebase()
    {
        try
        {
            // Check dependencies
            await FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => 
            {
                if (task.Exception != null)
                {
                    Debug.LogError($"Failed to initialize Firebase: {task.Exception}");
                    return;
                }
                
                // Initialize Firebase Database
                databaseReference = FirebaseDatabase.DefaultInstance.RootReference;
                
                firebaseInitialized = true;
                Debug.Log("Firebase Database initialized successfully!");
            });
        }
        catch (Exception e)
        {
            Debug.LogError($"Firebase initialization error: {e.Message}");
        }
    }
    
    // Save the current game state
    public void SaveCurrentGame()
    {
        if (saveGamePanel != null)
        {
            saveGamePanel.SetActive(true);
        }
    }
    
    // Open the load game panel and refresh the list of saved games
    public void OpenLoadGamePanel()
    {
        if (loadGamePanel != null)
        {
            loadGamePanel.SetActive(true);
            RefreshSavedGamesList();
        }
    }
    
    // Close UI panels
    public void CloseAllPanels()
    {
        if (saveGamePanel != null)
            saveGamePanel.SetActive(false);
            
        if (loadGamePanel != null)
            loadGamePanel.SetActive(false);
    }
    
    // Save the current game to Firebase
    public void SaveGameToFirebase()
    {
        if (!firebaseInitialized || databaseReference == null)
        {
            ShowStatus("Firebase not initialized. Try again later.");
            return;
        }
        
        // Get save game name from input field
        string saveName = saveGameNameInput != null ? saveGameNameInput.text.Trim() : "";
        
        if (string.IsNullOrEmpty(saveName))
        {
            ShowStatus("Please enter a name for your saved game.");
            return;
        }
        
        StartCoroutine(SaveGameCoroutine(saveName));
    }
    
    private IEnumerator SaveGameCoroutine(string saveName)
    {
        ShowStatus("Saving game...");
        
        // Variables needed for the save operation
        string fenString = "";
        int moveCount = 0;
        string saveId = "";
        Dictionary<string, object> saveData = null;
        string userId = "";
        
        // Collect all data needed before the try block
        try
        {
            // Get the current game state
            fenString = GameManager.Instance.SerializeGame();
            moveCount = GameManager.Instance.HalfMoveTimeline.Count;
            
            // Generate a unique ID for this saved game
            saveId = Guid.NewGuid().ToString();
            
            // Create the saved game data
            saveData = new Dictionary<string, object>
            {
                { "id", saveId },
                { "name", saveName },
                { "date", DateTime.UtcNow.ToString("o") },
                { "fen", fenString },
                { "moveCount", moveCount },
                { "playerWhite", PlayerPrefs.GetString("PlayerName", "Player") },
                { "playerBlack", "Opponent" }
            };
            
            // Get the current user ID (or generate one if not available)
            userId = NetworkManager.Singleton != null 
                ? NetworkManager.Singleton.LocalClientId.ToString() 
                : SystemInfo.deviceUniqueIdentifier;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error preparing save data: {e.Message}");
            ShowStatus("Failed to prepare save data: " + e.Message);
            yield break;
        }
        
        // Now save to Firebase outside the try block
        var saveTask = databaseReference.Child("savedGames").Child(userId).Child(saveId).SetValueAsync(saveData);
        yield return new WaitUntil(() => saveTask.IsCompleted);
        
        // Check if the save was successful
        if (saveTask.Exception != null)
        {
            Debug.LogError($"Failed to save game: {saveTask.Exception}");
            ShowStatus("Failed to save game. Please try again.");
            yield break;
        }
        
        ShowStatus("Game saved successfully!");
        
        // Close the save panel after a short delay
        yield return new WaitForSeconds(1.5f);
        
        if (saveGamePanel != null)
            saveGamePanel.SetActive(false);
            
        // Clear the input field
        if (saveGameNameInput != null)
            saveGameNameInput.text = "";
    }
    
    // Refresh the list of saved games from Firebase
    public void RefreshSavedGamesList()
    {
        if (!firebaseInitialized || databaseReference == null)
        {
            ShowStatus("Firebase not initialized. Try again later.");
            return;
        }
        
        StartCoroutine(RefreshSavedGamesCoroutine());
    }
    
    private IEnumerator RefreshSavedGamesCoroutine()
    {
        ShowStatus("Loading saved games...");
        
        // Clear existing saved game items before the try block
        foreach (Transform child in savedGamesContainer)
        {
            Destroy(child.gameObject);
        }
        
        // Clear cached list
        savedGames.Clear();
        
        // Get the current user ID
        string userId = NetworkManager.Singleton != null 
            ? NetworkManager.Singleton.LocalClientId.ToString() 
            : SystemInfo.deviceUniqueIdentifier;
        
        // Query saved games for this user
        var queryTask = databaseReference.Child("savedGames").Child(userId).GetValueAsync();
        yield return new WaitUntil(() => queryTask.IsCompleted);
        
        // Handle query results
        if (queryTask.Exception != null)
        {
            Debug.LogError($"Failed to query saved games: {queryTask.Exception}");
            ShowStatus("Failed to load saved games. Please try again.");
            yield break;
        }
        
        DataSnapshot snapshot = queryTask.Result;
        
        if (!snapshot.Exists)
        {
            ShowStatus("No saved games found.");
            yield break;
        }
        
        // Process each saved game
        try
        {
            // Process each saved game
            foreach (DataSnapshot childSnapshot in snapshot.Children)
            {
                SavedGameInfo savedGame = new SavedGameInfo
                {
                    Id = childSnapshot.Child("id").Value?.ToString(),
                    Name = childSnapshot.Child("name").Value?.ToString(),
                    Date = childSnapshot.Child("date").Value?.ToString(),
                    FEN = childSnapshot.Child("fen").Value?.ToString(),
                    PlayerWhite = childSnapshot.Child("playerWhite").Value?.ToString(),
                    PlayerBlack = childSnapshot.Child("playerBlack").Value?.ToString(),
                    MoveCount = Convert.ToInt32(childSnapshot.Child("moveCount").Value)
                };
                
                // Add to cached list
                savedGames.Add(savedGame);
                
                // Create UI element
                GameObject itemGO = Instantiate(savedGameItemPrefab, savedGamesContainer);
                SavedGameItemUI itemUI = itemGO.GetComponent<SavedGameItemUI>();
                
                if (itemUI != null)
                {
                    // Parse date for display
                    string displayDate = "Unknown date";
                    if (DateTime.TryParse(savedGame.Date, out DateTime date))
                    {
                        displayDate = date.ToString("g"); // Short date and time pattern
                    }
                    
                    // Setup the UI item
                    itemUI.Setup(
                        savedGame.Id,
                        savedGame.Name,
                        displayDate,
                        $"Moves: {savedGame.MoveCount}",
                        savedGame.PlayerWhite + " vs " + savedGame.PlayerBlack
                    );
                    
                    // Add click handler
                    itemUI.OnLoadClicked += LoadSavedGame;
                    itemUI.OnDeleteClicked += DeleteSavedGame;
                }
            }
            
            ShowStatus($"Found {savedGames.Count} saved games.");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error processing saved games: {e.Message}");
            ShowStatus("Error processing saved games: " + e.Message);
        }
    }
    
    // Load a saved game by ID
    public void LoadSavedGame(string saveId)
    {
        SavedGameInfo savedGame = savedGames.Find(g => g.Id == saveId);
        
        if (savedGame == null || string.IsNullOrEmpty(savedGame.FEN))
        {
            ShowStatus("Error: Could not find saved game data.");
            return;
        }
        
        StartCoroutine(LoadGameCoroutine(savedGame));
    }
    
    private IEnumerator LoadGameCoroutine(SavedGameInfo savedGame)
    {
        ShowStatus($"Loading game: {savedGame.Name}...");
        
        // Handle loading the game
        try
        {
            // Check if we're in a networked game
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
            {
                // In a networked game, the host needs to load the game for all players
                if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
                {
                    LoadGameForAllClients(savedGame.FEN);
                }
                else
                {
                    // Clients should request the host to load the game
                    RequestLoadGame(savedGame.FEN);
                }
            }
            else
            {
                // In a local game, just load it directly
                GameManager.Instance.LoadGame(savedGame.FEN);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading game: {e.Message}");
            ShowStatus("Failed to load game: " + e.Message);
            yield break;
        }
        
        // Wait a bit and then close the panel
        yield return new WaitForSeconds(1.0f);
        
        if (loadGamePanel != null)
            loadGamePanel.SetActive(false);
            
        ShowStatus($"Game '{savedGame.Name}' loaded successfully!");
    }
    
    // Delete a saved game by ID
    public void DeleteSavedGame(string saveId)
    {
        if (!firebaseInitialized || databaseReference == null)
        {
            ShowStatus("Firebase not initialized. Try again later.");
            return;
        }
        
        StartCoroutine(DeleteSavedGameCoroutine(saveId));
    }
    
    private IEnumerator DeleteSavedGameCoroutine(string saveId)
    {
        ShowStatus("Deleting saved game...");
        
        // Get the current user ID outside the try block
        string userId = NetworkManager.Singleton != null 
            ? NetworkManager.Singleton.LocalClientId.ToString() 
            : SystemInfo.deviceUniqueIdentifier;
        
        // Delete from Firebase
        var deleteTask = databaseReference.Child("savedGames").Child(userId).Child(saveId).RemoveValueAsync();
        yield return new WaitUntil(() => deleteTask.IsCompleted);
        
        if (deleteTask.Exception != null)
        {
            Debug.LogError($"Failed to delete saved game: {deleteTask.Exception}");
            ShowStatus("Failed to delete saved game. Please try again.");
            yield break;
        }
        
        ShowStatus("Saved game deleted successfully!");
        
        // Refresh the list
        RefreshSavedGamesList();
    }
    
    // Helper method to show status messages
    private void ShowStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        
        Debug.Log(message);
    }
    
    // Methods for networked game loading
    // Note: These would be implemented as ServerRpc and ClientRpc in a NetworkBehaviour
    
    private void RequestLoadGame(string fenString)
    {
        Debug.Log($"Client would request server to load game: {fenString.Substring(0, Math.Min(20, fenString.Length))}...");
        // In a real implementation, this would be a ServerRpc
    }
    
    private void LoadGameForAllClients(string fenString)
    {
        Debug.Log($"Server would broadcast game state to all clients: {fenString.Substring(0, Math.Min(20, fenString.Length))}...");
        // In a real implementation, this would be a ClientRpc
        
        // For now, just load it locally
        GameManager.Instance.LoadGame(fenString);
    }
}