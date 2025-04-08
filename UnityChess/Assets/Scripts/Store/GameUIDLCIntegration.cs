using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;
using UnityEngine.Networking;

/// <summary>
/// Integrates the DLC store with the main game UI and manages profile display
/// for both local player and opponent in multiplayer
/// </summary>
public class GameUIDLCIntegration : MonoBehaviour
{
    [Header("Local Player References")]
    [SerializeField] private Image playerProfileImage;
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private Button openStoreButton;
    
    [Header("Game UI References")]
    [SerializeField] private Image hostProfileImage;
    [SerializeField] private Image clientProfileImage;
    [SerializeField] private TextMeshProUGUI hostNameText;
    [SerializeField] private TextMeshProUGUI clientNameText;
    [SerializeField] private GameObject dlcStoreCanvas;
    
    // Optional status text for debugging
    [SerializeField] private TextMeshProUGUI statusText;
    
    // Store local and remote profile data
    private string localProfileId = "";
    private string remoteProfileId = "";
    private string localPlayerName = "Player";
    private string remotePlayerName = "";
    
    // Reference to DLC Manager
    private DLCManager dlcManager;
    
    // Tracking connection state to avoid premature name setting
    private bool hasConnected = false;
    
    private void Start()
    {
        // Find DLC Manager
        dlcManager = DLCManager.Instance;
        if (dlcManager == null)
        {
            Debug.LogWarning("DLCManager not found in scene. DLC features may not work properly.");
            dlcManager = FindObjectOfType<DLCManager>();
            if (dlcManager == null)
                Debug.LogError("Could not find DLCManager even with FindObjectOfType");
        }
        
        // Verify UI components are properly assigned
        VerifyUIComponents();
        
        // Set up button listeners
        if (openStoreButton != null)
        {
            openStoreButton.onClick.AddListener(OpenDLCStore);
        }
        else
        {
            Debug.LogWarning("Open Store Button not assigned");
        }
        
        // Initialize with a generic name - don't use network roles yet
        if (playerNameText != null)
        {
            playerNameText.text = localPlayerName;
        }
        
        // Initialize local profile image - load from PlayerPrefs if available
        LoadInitialProfileImage();
        
        // Check for NetworkManager and subscribe to events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
            
            // Check if already connected
            if (NetworkManager.Singleton.IsConnectedClient)
            {
                OnClientConnected(NetworkManager.Singleton.LocalClientId);
            }
        }
        else
        {
            Debug.LogWarning("NetworkManager.Singleton is null in Start. Network features may not work.");
        }
    }
    
    private void OnDestroy()
    {
        // Clean up button listeners
        if (openStoreButton != null)
        {
            openStoreButton.onClick.RemoveListener(OpenDLCStore);
        }
        
        // Unsubscribe from network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
        }
    }
    
    // Verify UI components are properly assigned
    private void VerifyUIComponents()
    {
        // Check and find image components if they're null
        if (playerProfileImage == null)
        {
            playerProfileImage = GameObject.Find("PlayerProfileImage")?.GetComponent<Image>();
            if (playerProfileImage == null)
                Debug.LogError("Player Profile Image not found in scene");
        }
        
        if (hostProfileImage == null)
        {
            hostProfileImage = GameObject.Find("Player1ProfileImage")?.GetComponent<Image>();
            if (hostProfileImage == null)
                Debug.LogError("Host Profile Image (Player1ProfileImage) not found in scene");
        }
        
        if (clientProfileImage == null)
        {
            clientProfileImage = GameObject.Find("Player2ProfileImage")?.GetComponent<Image>();
            if (clientProfileImage == null)
                Debug.LogError("Client Profile Image (Player2ProfileImage) not found in scene");
        }
        
        // Check and find text components if they're null
        if (hostNameText == null)
        {
            hostNameText = GameObject.Find("HostName")?.GetComponent<TextMeshProUGUI>();
            if (hostNameText == null)
                Debug.LogError("Host Name Text not found in scene");
        }
        
        if (clientNameText == null)
        {
            clientNameText = GameObject.Find("ClientName")?.GetComponent<TextMeshProUGUI>();
            if (clientNameText == null)
                Debug.LogError("Client Name Text not found in scene");
        }
        
        LogStatus("UI Components verification completed");
    }
    
    private void LoadInitialProfileImage()
    {
        // Try to load the saved profile ID
        string savedProfileId = PlayerPrefs.GetString("SelectedProfileId", "");
        
        if (!string.IsNullOrEmpty(savedProfileId))
        {
            localProfileId = savedProfileId;
            
            // Load the image
            if (playerProfileImage != null)
            {
                StartCoroutine(LoadProfilePictureForImage(savedProfileId, playerProfileImage));
            }
        }
    }
    
    // Network event handlers
    private void OnClientConnected(ulong clientId)
    {
        LogStatus($"Client connected: {clientId}, Local client: {NetworkManager.Singleton.LocalClientId}");
        
        // Update UI for the local player's first connection
        if (!hasConnected && clientId == NetworkManager.Singleton.LocalClientId)
        {
            hasConnected = true;
            
            // Update player name based on network role
            UpdatePlayerName();
            
            // Update appropriate profile image
            bool isHost = NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer;
            
            if (isHost && hostProfileImage != null && !string.IsNullOrEmpty(localProfileId))
            {
                StartCoroutine(LoadProfilePictureForImage(localProfileId, hostProfileImage));
            }
            else if (!isHost && clientProfileImage != null && !string.IsNullOrEmpty(localProfileId))
            {
                StartCoroutine(LoadProfilePictureForImage(localProfileId, clientProfileImage));
            }
            
            LogStatus("Network connection established, updated player UI");
            
            // Request a refresh of profile data from all connected clients
            RequestAllProfileRefreshes();
        }
        
        // If another player joins and we're already connected, show our profile to them
        if (hasConnected && clientId != NetworkManager.Singleton.LocalClientId)
        {
            LogStatus($"Another player joined (ID: {clientId}), sharing our profile with them");
            ShareProfileWithNewPlayer();
        }
    }
    
    private void OnClientDisconnect(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            hasConnected = false;
            
            // Reset to generic player name
            localPlayerName = "Player";
            if (playerNameText != null)
            {
                playerNameText.text = localPlayerName;
            }
            
            LogStatus("Disconnected from network");
        }
    }
    
    // New method to request all players to refresh their profile data
    private void RequestAllProfileRefreshes()
    {
        // Small delay to allow network objects to spawn properly
        StartCoroutine(DelayedProfileRefresh(0.5f));
    }

    // New coroutine to add a delay before refreshing profiles
    private IEnumerator DelayedProfileRefresh(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // First make sure our own profile is synced
        if (dlcManager != null)
        {
            dlcManager.SyncProfileWithNetwork();
        }
        else
        {
            // Try to find DLCManager if not already assigned
            dlcManager = FindObjectOfType<DLCManager>();
            if (dlcManager != null)
            {
                dlcManager.SyncProfileWithNetwork();
            }
            else
            {
                LogStatus("Cannot find DLCManager to request profile refresh");
            }
        }
        
        // Now if we're the host/server, trigger a network-wide refresh
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
        {
            LogStatus("Requesting all clients to refresh their profiles");
            TriggerNetworkWideProfileRefreshServerRpc();
        }
    }

    // New method to share our profile with a newly joined player
    private void ShareProfileWithNewPlayer()
    {
        // If we have a profile ID and DLC manager, sync it
        if (!string.IsNullOrEmpty(localProfileId) && dlcManager != null)
        {
            dlcManager.SyncProfileWithNetwork();
        }
        else if (dlcManager == null)
        {
            LogStatus("Cannot share profile - DLCManager not found");
        }
    }

    // Add these RPC methods for network-wide profile refresh
    [ServerRpc(RequireOwnership = false)]
    private void TriggerNetworkWideProfileRefreshServerRpc()
    {
        // Only the server can do this
        if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
        {
            TriggerProfileRefreshClientRpc();
        }
    }

    [ClientRpc]
    private void TriggerProfileRefreshClientRpc()
    {
        LogStatus("Received profile refresh request from server");
        
        // Find DLC manager if needed
        if (dlcManager == null)
        {
            dlcManager = FindObjectOfType<DLCManager>();
        }
        
        // Request refresh
        if (dlcManager != null)
        {
            dlcManager.RequestProfileRefresh();
        }
        else
        {
            LogStatus("Cannot refresh profile - DLCManager not found");
        }
    }
    
    // Update the player name based on connection status
    private void UpdatePlayerName()
    {
        if (!hasConnected)
        {
            // Don't apply network-specific names yet
            return;
        }
        
        localPlayerName = GetDefaultPlayerName();
        PlayerPrefs.SetString("PlayerName", localPlayerName);
        
        if (playerNameText != null)
        {
            playerNameText.text = localPlayerName;
        }
        
        // Also update the respective network UI text
        bool isHost = NetworkManager.Singleton != null && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer);
        
        if (isHost && hostNameText != null)
        {
            hostNameText.text = localPlayerName;
        }
        else if (!isHost && clientNameText != null)
        {
            clientNameText.text = localPlayerName;
        }
    }
    
    // Button handler to open the DLC store
    public void OpenDLCStore()
    {
        if (dlcManager != null)
        {
            dlcManager.OpenStore();
        }
        else
        {
            LogStatus("DLC Manager not found");
        }
    }
    
    // Called by DLCManager when a profile is selected
    public void OnProfileSelected(string profileId)
    {
        // Log that we received the profile selection
        LogStatus($"Profile selected: {profileId}");
        
        // Make sure profileId is not null or empty
        if (string.IsNullOrEmpty(profileId))
        {
            LogStatus("Received empty profile ID");
            return;
        }
        
        // Update local data
        localProfileId = profileId;
        
        // Update local UI with a safety check
        if (playerProfileImage != null)
        {
            StartCoroutine(LoadProfilePictureForImage(profileId, playerProfileImage));
        }
        else
        {
            LogStatus("playerProfileImage is null, can't update local profile");
        }
        
        // If in multiplayer, sync with network
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            bool isHost = NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer;
            LogStatus($"Profile selected: {profileId}, Host: {isHost}");
            
            // Update the right profile image based on if we're host or client
            if (isHost && hostProfileImage != null)
            {
                StartCoroutine(LoadProfilePictureForImage(profileId, hostProfileImage));
                if (hostNameText != null)
                {
                    hostNameText.text = localPlayerName;
                }
            }
            else if (!isHost && clientProfileImage != null)
            {
                StartCoroutine(LoadProfilePictureForImage(profileId, clientProfileImage));
                if (clientNameText != null)
                {
                    clientNameText.text = localPlayerName;
                }
            }
            else
            {
                LogStatus($"Could not update network profile UI: isHost={isHost}, hostImage={hostProfileImage != null}, clientImage={clientProfileImage != null}");
            }
        }
        else
        {
            LogStatus("NetworkManager not available, can't sync profile with network");
        }
    }
    
    // Called when another player changes their profile
    public void UpdateOtherPlayerProfile(string profileId, string playerName)
    {
        LogStatus($"UpdateOtherPlayerProfile called with profileId={profileId}, playerName={playerName}");
        
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
        {
            LogStatus("NetworkManager not available, can't update other player's profile");
            return;
        }
        
        if (string.IsNullOrEmpty(profileId))
        {
            LogStatus("Received empty profile ID for other player");
            return;
        }
                
        bool isLocalPlayerHost = NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer;
        
        // Store remote profile data
        remoteProfileId = profileId;
        remotePlayerName = playerName;
        
        LogStatus($"Updating other player's profile to {profileId}, Name: {playerName}");
        
        // If we're the host, update the client profile image
        // If we're the client, update the host profile image
        if (isLocalPlayerHost && clientProfileImage != null)
        {
            StartCoroutine(LoadProfilePictureForImage(profileId, clientProfileImage));
            if (clientNameText != null)
                clientNameText.text = playerName;
                    
            LogStatus($"Host updated client profile to {profileId}");
        }
        else if (!isLocalPlayerHost && hostProfileImage != null)
        {
            StartCoroutine(LoadProfilePictureForImage(profileId, hostProfileImage));
            if (hostNameText != null)
                hostNameText.text = playerName;
                    
            LogStatus($"Client updated host profile to {profileId}");
        }
        else
        {
            LogStatus($"Could not update other player profile UI: isHost={isLocalPlayerHost}, hostImage={hostProfileImage != null}, clientImage={clientProfileImage != null}");
        }
    }
    
    // Helper method to load profile picture into an image component
    private IEnumerator LoadProfilePictureForImage(string profileId, Image targetImage)
    {
        if (string.IsNullOrEmpty(profileId) || targetImage == null)
            yield break;
                
        LogStatus($"Loading profile picture: {profileId}");
        
        // Try to load from saved file first
        string filePath = System.IO.Path.Combine(Application.persistentDataPath, $"profile_{profileId}.png");
        
        if (System.IO.File.Exists(filePath))
        {
            LogStatus($"Found local file for {profileId}");
            byte[] data = null;
            bool fileReadSuccess = false;
            
            // Try to read the file
            try
            {
                data = System.IO.File.ReadAllBytes(filePath);
                fileReadSuccess = (data != null && data.Length > 0);
            }
            catch (System.Exception e)
            {
                LogStatus($"Error reading file: {e.Message}");
                fileReadSuccess = false;
            }
            
            // If we successfully read the file, try to create a texture
            if (fileReadSuccess)
            {
                Texture2D texture = new Texture2D(2, 2);
                bool textureLoadSuccess = false;
                
                try
                {
                    textureLoadSuccess = texture.LoadImage(data);
                }
                catch (System.Exception e)
                {
                    LogStatus($"Error loading image data: {e.Message}");
                    textureLoadSuccess = false;
                }
                
                // If texture loaded successfully, create sprite
                if (textureLoadSuccess && texture != null)
                {
                    Sprite sprite = null;
                    bool spriteCreateSuccess = false;
                    
                    try
                    {
                        sprite = Sprite.Create(
                            texture, 
                            new Rect(0, 0, texture.width, texture.height), 
                            new Vector2(0.5f, 0.5f)
                        );
                        spriteCreateSuccess = (sprite != null);
                    }
                    catch (System.Exception e)
                    {
                        LogStatus($"Error creating sprite: {e.Message}");
                        spriteCreateSuccess = false;
                    }
                    
                    // If sprite was created successfully, apply to image
                    if (spriteCreateSuccess)
                    {
                        targetImage.sprite = sprite;
                        LogStatus($"Applied profile image from file: {profileId}");
                        yield break; // Success! Exit here
                    }
                }
            }
            
            // If we got here, something failed - try loading from DLCManager
            LogStatus($"Failed to load image from file, trying DLCManager");
        }
        
        // Try loading from DLCManager
        yield return TryLoadFromDLCManager(profileId, targetImage);
    }

    // This helper method tries to load from DLCManager
    private IEnumerator TryLoadFromDLCManager(string profileId, Image targetImage)
    {
        // Check if DLCManager exists
        if (dlcManager == null)
        {
            dlcManager = FindObjectOfType<DLCManager>();
            if (dlcManager == null)
            {
                LogStatus("Cannot find DLCManager");
                yield break;
            }
        }
        
        // Try to find matching profile
        ProfilePicture matchingProfile = null;
        
        // Find a profile with the given ID
        var field = dlcManager.GetType().GetField("availableProfilePictures", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
        if (field != null)
        {
            var profiles = field.GetValue(dlcManager) as List<ProfilePicture>;
            if (profiles != null)
            {
                foreach (var profile in profiles)
                {
                    if (profile.Id == profileId)
                    {
                        matchingProfile = profile;
                        break;
                    }
                }
            }
        }
        
        // If we found a matching profile, try to load its image
        if (matchingProfile != null)
        {
            LogStatus($"Found matching profile: {matchingProfile.Name}");
            
            // Use DLCManager's method to load image
            var loadMethod = dlcManager.GetType().GetMethod("LoadImagePreview", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
            if (loadMethod != null)
            {
                LogStatus($"Starting image load for {matchingProfile.ImageUrl}");
                
                // Call the method from DLCManager using reflection
                var loadCoroutine = (IEnumerator)loadMethod.Invoke(
                    dlcManager, 
                    new object[] { matchingProfile.ImageUrl, targetImage }
                );
                
                // Execute the coroutine
                if (loadCoroutine != null)
                {
                    while (loadCoroutine.MoveNext())
                    {
                        yield return loadCoroutine.Current;
                    }
                    
                    LogStatus($"Completed image load for {matchingProfile.Name}");
                    yield break;
                }
            }
            else
            {
                // If we can't access the DLCManager's method, use fallback
                LogStatus("Using fallback image loading");
                LoadFallbackImage(profileId, targetImage);
            }
        }
        else
        {
            LogStatus($"No matching profile found for {profileId}, using fallback");
            LoadFallbackImage(profileId, targetImage);
        }
    }

    // Helper to load a fallback image based on profile ID
    private void LoadFallbackImage(string profileId, Image targetImage)
    {
        string pieceName = "";
        
        // Extract piece name from profile ID
        if (profileId.Contains("_"))
        {
            pieceName = profileId.Split('_')[1];
            // Capitalize first letter
            if (!string.IsNullOrEmpty(pieceName) && pieceName.Length > 0)
            {
                pieceName = char.ToUpper(pieceName[0]) + (pieceName.Length > 1 ? pieceName.Substring(1) : "");
            }
        }
        
        // Fallback based on ID
        switch (profileId.ToLower().Replace("profile_", ""))
        {
            case "knight":
                LoadPlaceholderImage("Knight", targetImage);
                break;
            case "queen":
                LoadPlaceholderImage("Queen", targetImage);
                break;
            case "king":
                LoadPlaceholderImage("King", targetImage);
                break;
            default:
                LoadPlaceholderImage("Bishop", targetImage);
                break;
        }
    }

    // Helper to load a placeholder image
    private void LoadPlaceholderImage(string pieceName, Image targetImage)
    {
        // Try to load from Resources as last resort
        Sprite placeholderSprite = Resources.Load<Sprite>($"Placeholders/{pieceName}");
        if (placeholderSprite != null)
        {
            targetImage.sprite = placeholderSprite;
            LogStatus($"Using placeholder for {pieceName}");
        }
        else
        {
            LogStatus($"No placeholder available for {pieceName}");
        }
    }
    
    // Get default player name based on network role
    private string GetDefaultPlayerName()
    {
        if (NetworkManager.Singleton == null)
            return "Player";
            
        // Don't assign network-based names until actually connected
        if (!hasConnected)
            return "Player";
            
        return NetworkManager.Singleton.IsHost ? "Host" : "Client";
    }
    
    // Helper method to log status messages
    private void LogStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"[GameUIDLC] {message}");
    }
}