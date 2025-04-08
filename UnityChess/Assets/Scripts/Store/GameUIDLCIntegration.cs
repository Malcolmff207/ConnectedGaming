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
        }
        
        // Set up button listeners
        if (openStoreButton != null)
        {
            openStoreButton.onClick.AddListener(OpenDLCStore);
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
        // Only update UI for the local player's first connection
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
        // Update local data
        localProfileId = profileId;
        
        // Update local UI
        if (playerProfileImage != null)
        {
            StartCoroutine(LoadProfilePictureForImage(profileId, playerProfileImage));
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
        }
    }
    
    // Called when another player changes their profile
    public void UpdateOtherPlayerProfile(string profileId, string playerName)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
            return;
            
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
            try
            {
                // Load file data
                byte[] data = System.IO.File.ReadAllBytes(filePath);
                
                // Create and load texture
                Texture2D texture = new Texture2D(2, 2);
                if (texture.LoadImage(data))
                {
                    // Create sprite from texture
                    Sprite sprite = Sprite.Create(
                        texture, 
                        new Rect(0, 0, texture.width, texture.height), 
                        new Vector2(0.5f, 0.5f)
                    );
                    
                    // Apply sprite to image
                    targetImage.sprite = sprite;
                    LogStatus($"Applied profile image from file: {profileId}");
                }
                else
                {
                    LogStatus($"Failed to load image data for {profileId}");
                    yield return TryLoadFromDLCManager(profileId, targetImage);
                }
            }
            catch (System.Exception e)
            {
                LogStatus($"Error loading profile image from file: {e.Message}");
                yield return TryLoadFromDLCManager(profileId, targetImage);
            }
        }
        else
        {
            LogStatus($"No local file for {profileId}, trying DLCManager");
            yield return TryLoadFromDLCManager(profileId, targetImage);
        }
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
        
        // Try to load the profile directly from Firebase using DLCManager's methods
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
                yield return (IEnumerator)loadMethod.Invoke(dlcManager, new object[] { matchingProfile.ImageUrl, targetImage });
                LogStatus($"Completed image load for {matchingProfile.Name}");
            }
            else
            {
                // If we can't access the DLCManager's method, try a fallback
                LogStatus("Using fallback image loading");
                
                // This switches automatically to Bishop if profile not found
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
        }
        else
        {
            LogStatus($"No matching profile found for {profileId}, using fallback");
            
            // Fallback - try direct path based on ID
            string imageName = profileId.Replace("profile_", "");
            string imagePath = "Chess/" + char.ToUpper(imageName[0]) + imageName.Substring(1) + ".jpg";
            
            LogStatus($"Trying fallback path: {imagePath}");
            
            // Use DLCManager's method
            var loadMethod = dlcManager.GetType().GetMethod("LoadImagePreview", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
            if (loadMethod != null)
            {
                yield return (IEnumerator)loadMethod.Invoke(dlcManager, new object[] { imagePath, targetImage });
            }
            else
            {
                // Placeholder fallback based on ID
                LoadPlaceholderImage(imageName, targetImage);
            }
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