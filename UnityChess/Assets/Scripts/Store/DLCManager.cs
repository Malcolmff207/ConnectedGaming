using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Networking;
using Unity.Netcode;
using Firebase;
using Firebase.Storage;
using Firebase.Extensions;
using Firebase.Database;

/// <summary>
/// Manages DLC content, Firebase integration, and purchasing functionality
/// </summary>
public class DLCManager : NetworkBehaviour
{
    // Singleton instance
    public static DLCManager Instance { get; private set; }

    // References to UI elements
    [Header("UI References")]
    [SerializeField] private GameObject storeCanvas;
    [SerializeField] private Transform itemsContainer;
    [SerializeField] private GameObject storeItemPrefab;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Image playerProfileImage;
    [SerializeField] private TextMeshProUGUI playerCreditsText;
    
    // Reference to GameUI DLC Integration
    [SerializeField] private GameUIDLCIntegration gameUIDLCIntegration;

    // Firebase references
    private FirebaseStorage storage;
    private StorageReference storageReference;
    private DatabaseReference databaseReference;
    
    // Player data
    private int playerCredits = 1000; // Default starting credits
    private List<ProfilePicture> availableProfilePictures = new List<ProfilePicture>();
    private List<string> purchasedProfileIds = new List<string>();
    private string selectedProfileId = "";
    
    // Network variables (for synchronization across clients)
    private NetworkVariable<ProfilePictureNetworkData> playerProfileData = new NetworkVariable<ProfilePictureNetworkData>(
        new ProfilePictureNetworkData { ProfileId = "", PlayerName = "Player" },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    // Custom network struct for profile data
    public struct ProfilePictureNetworkData : INetworkSerializable
    {
        public string ProfileId;
        public string PlayerName;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ProfileId);
            serializer.SerializeValue(ref PlayerName);
        }
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
        
        // Validate references
        ValidateReferences();
    }

    private void Start()
    {
        // Initialize Firebase
        InitializeFirebase();
        
        // Subscribe to network variable change
        playerProfileData.OnValueChanged += OnProfileDataChanged;
        
        // Load player data from PlayerPrefs
        LoadPlayerData();
        
        // Update UI
        UpdatePlayerCreditsUI();
        
        // Find GameUIDLCIntegration if not assigned
        if (gameUIDLCIntegration == null)
        {
            gameUIDLCIntegration = FindObjectOfType<GameUIDLCIntegration>();
        }
        
        // Setup UI visibility
        SetupUI();
    }

    private void ValidateReferences()
    {
        // Check key references
        if (storeCanvas == null)
            Debug.LogError("Store Canvas reference is missing!");
            
        if (itemsContainer == null)
            Debug.LogError("Items Container reference is missing!");
            
        if (storeItemPrefab == null)
            Debug.LogError("Store Item Prefab reference is missing!");
            
        if (playerProfileImage == null)
            Debug.LogError("Player Profile Image reference is missing!");
            
        if (playerCreditsText == null)
            Debug.LogError("Player Credits Text reference is missing!");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        Debug.Log($"DLCManager spawned: IsOwner={IsOwner}, IsLocalPlayer={IsLocalPlayer}, ClientId={OwnerClientId}");
        
        // On spawn, set initial profile data but ONLY if we're the owner
        if (IsOwner)
        {
            // This is safe because IsOwner only returns true for the object we own
            playerProfileData.Value = new ProfilePictureNetworkData
            {
                ProfileId = selectedProfileId,
                PlayerName = PlayerPrefs.GetString("PlayerName", "Player" + NetworkManager.Singleton.LocalClientId)
            };
            
            Debug.Log($"Set initial profile data: ProfileId={selectedProfileId}, Name={PlayerPrefs.GetString("PlayerName", "Player" + NetworkManager.Singleton.LocalClientId)}");
        }
        
        // Make sure everyone is subscribed to changes
        playerProfileData.OnValueChanged += OnProfileDataChanged;
    }

    public override void OnDestroy()
    {
        // Unsubscribe from events
        playerProfileData.OnValueChanged -= OnProfileDataChanged;
        
        if (Instance == this)
        {
            Instance = null;
        }
        
        base.OnDestroy();
    }

    private void OnProfileDataChanged(ProfilePictureNetworkData previousValue, ProfilePictureNetworkData newValue)
    {
        // Log for debugging
        Debug.Log($"Profile data changed from {previousValue.ProfileId} to {newValue.ProfileId}");
        
        // If this is not our own NetworkObject, update the UI
        if (!IsOwner)
        {
            Debug.Log($"Remote player profile changed: {newValue.PlayerName} with profile ID: {newValue.ProfileId}");
            
            // Update other player's profile in the UI
            if (gameUIDLCIntegration != null)
            {
                gameUIDLCIntegration.UpdateOtherPlayerProfile(newValue.ProfileId, newValue.PlayerName);
            }
        }
    }

    private void InitializeFirebase()
    {
        try
        {
            // Check dependencies
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => 
            {
                if (task.Exception != null)
                {
                    Debug.LogError($"Failed to initialize Firebase: {task.Exception}");
                    ShowStatus("Failed to connect to store. Check internet connection.");
                    return;
                }
                
                DependencyStatus dependencyStatus = task.Result;
                if (dependencyStatus == DependencyStatus.Available)
                {
                    try
                    {
                        // Create app with explicit options if needed
                        FirebaseApp app = FirebaseApp.DefaultInstance;
                        if (app == null)
                        {
                            app = FirebaseApp.Create();
                        }
                        
                        // Initialize Storage first with exact URL from screenshots
                        storage = FirebaseStorage.DefaultInstance;
                        storageReference = storage.GetReferenceFromUrl("gs://connectedgaming-18bcb.firebasestorage.app");
                        
                        // Initialize Database with exact URL from screenshots
                        var db = FirebaseDatabase.GetInstance("https://connectedgaming-18bcb-default-rtdb.europe-west1.firebasedatabase.app/");
                        databaseReference = db.RootReference;
                        
                        Debug.Log("Firebase initialized successfully!");
                        
                        // Once Firebase is initialized, load profile pictures
                        LoadProfilePicturesFromFirebase();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Firebase component initialization error: {e.Message}");
                        ShowStatus("Store initialization failed. Firebase components error.");
                    }
                }
                else
                {
                    Debug.LogError($"Could not resolve all Firebase dependencies: {dependencyStatus}");
                    ShowStatus("Firebase initialization failed. Please check your connection.");
                }
            });
        }
        catch (Exception e)
        {
            Debug.LogError($"Firebase initialization error: {e.Message}");
            ShowStatus("Store initialization failed. Try again later.");
        }
    }

    private void LoadProfilePicturesFromFirebase()
    {
        if (storage == null || storageReference == null)
        {
            Debug.LogError("Firebase not initialized yet. Cannot load profile pictures.");
            ShowStatus("Firebase not initialized. Try again later.");
            return;
        }

        ShowStatus("Loading profile pictures...");

        // Clear existing profile pictures
        availableProfilePictures.Clear();
        
        // Exact file names from your Firebase Storage (from your screenshot)
        string[] knownProfiles = new string[] 
        {
            "Bishop.jpg",
            "King.jpg", 
            "Knight.jpg",
            "Queen.jpg"
        };
        
        int itemsFound = 0;
        
        // Create profile picture entries based on known files
        foreach (string fileName in knownProfiles)
        {
            try
            {
                // Extract profile name from the file name (remove extension)
                string profileName = System.IO.Path.GetFileNameWithoutExtension(fileName);
                
                // Generate an ID based on the file name
                string profileId = "profile_" + profileName.ToLower();
                
                // Set price based on item type
                int price = GetPriceForProfile(profileName);
                
                // Create the profile picture entry
                ProfilePicture profilePic = new ProfilePicture {
                    Id = profileId,
                    Name = FormatProfileName(profileName) + " Avatar",
                    Price = price,
                    ImageUrl = "Chess/" + fileName
                };
                
                // Add to our list
                availableProfilePictures.Add(profilePic);
                itemsFound++;
                
                Debug.Log($"Added profile picture from Firebase: {profilePic.Name} ({profilePic.Id})");
            }
            catch (Exception e) 
            {
                Debug.LogError($"Error adding profile {fileName}: {e.Message}");
            }
        }
        
        // Notify UI that pictures are loaded
        if (itemsFound > 0)
        {
            PopulateStoreItems();
            ShowStatus($"Loaded {availableProfilePictures.Count} profile pictures!");
        }
        else
        {
            LoadFallbackItems();
        }
    }
    
    private void LoadFallbackItems()
    {
        // Clear list and add fallback items
        availableProfilePictures.Clear();
        availableProfilePictures = new List<ProfilePicture>
        {
            new ProfilePicture { Id = "profile_knight", Name = "Knight Avatar", Price = 100, ImageUrl = "Chess/Knight.jpg" },
            new ProfilePicture { Id = "profile_queen", Name = "Queen Avatar", Price = 200, ImageUrl = "Chess/Queen.jpg" },
            new ProfilePicture { Id = "profile_king", Name = "King Avatar", Price = 300, ImageUrl = "Chess/King.jpg" },
            new ProfilePicture { Id = "profile_bishop", Name = "Bishop Avatar", Price = 150, ImageUrl = "Chess/Bishop.jpg" }
        };
        
        // Populate the store UI with fallback items
        PopulateStoreItems();
        ShowStatus($"Loaded {availableProfilePictures.Count} fallback profile pictures");
    }
    
    private int GetPriceForProfile(string profileName)
    {
        // Set price based on profile type
        string lowerName = profileName.ToLower();
        if (lowerName.Contains("pawn")) return 50;
        if (lowerName.Contains("knight")) return 100;
        if (lowerName.Contains("bishop")) return 150;
        if (lowerName.Contains("rook")) return 175;
        if (lowerName.Contains("queen")) return 200;
        if (lowerName.Contains("king")) return 300;
        
        // Default price
        return 100;
    }
    
    private string FormatProfileName(string rawName)
    {
        // Format profile name nicely
        if (string.IsNullOrEmpty(rawName)) return "Profile";
        
        // Capitalize first letter
        if (rawName.Length == 1) return rawName.ToUpper();
        
        return char.ToUpper(rawName[0]) + rawName.Substring(1);
    }

    private void PopulateStoreItems()
    {
        // First check if itemsContainer exists
        if (itemsContainer == null)
        {
            Debug.LogError("Items container is null! Cannot populate store items.");
            return;
        }
        
        // Check if store item prefab exists
        if (storeItemPrefab == null)
        {
            Debug.LogError("Store item prefab is null! Cannot instantiate store items.");
            return;
        }

        // Clear existing items
        foreach (Transform child in itemsContainer)
        {
            Destroy(child.gameObject);
        }
        
        // Make sure the container has a Grid Layout Group
        GridLayoutGroup gridLayout = itemsContainer.GetComponent<GridLayoutGroup>();
        if (gridLayout == null)
        {
            // Add a Grid Layout Group if missing
            gridLayout = itemsContainer.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(150, 200);  // Set appropriate cell size
            gridLayout.spacing = new Vector2(10, 10);     // Set spacing between items
            gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            gridLayout.childAlignment = TextAnchor.UpperLeft;
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 2;  // Show 2 items per row
            
            Debug.Log("Added Grid Layout Group to ItemsContainer");
        }
        
        // Enable content size fitter if needed for scrolling
        ContentSizeFitter sizeFitter = itemsContainer.GetComponent<ContentSizeFitter>();
        if (sizeFitter == null)
        {
            sizeFitter = itemsContainer.gameObject.AddComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
        
        // Check if the container is in a ScrollRect
        ScrollRect scrollRect = itemsContainer.GetComponentInParent<ScrollRect>();
        if (scrollRect != null)
        {
            // Make sure this container is set as the content
            scrollRect.content = itemsContainer as RectTransform;
        }
        
        // Create UI elements for each available profile picture
        foreach (ProfilePicture profilePicture in availableProfilePictures)
        {
            try
            {
                GameObject itemGO = Instantiate(storeItemPrefab, itemsContainer);
                
                // Ensure proper sizing of the store item
                RectTransform itemRect = itemGO.GetComponent<RectTransform>();
                if (itemRect != null)
                {
                    // Let the Grid Layout Group handle the positioning and sizing
                    itemRect.anchorMin = new Vector2(0, 1);
                    itemRect.anchorMax = new Vector2(0, 1);
                    itemRect.pivot = new Vector2(0.5f, 0.5f);
                }
                
                StoreItemUI storeItem = itemGO.GetComponent<StoreItemUI>();
                
                if (storeItem != null)
                {
                    bool isPurchased = purchasedProfileIds.Contains(profilePicture.Id);
                    bool isSelected = selectedProfileId == profilePicture.Id;
                    
                    storeItem.Setup(profilePicture, isPurchased, isSelected);
                    storeItem.OnPurchaseClicked += PurchaseProfilePicture;
                    storeItem.OnSelectClicked += SelectProfilePicture;
                    
                    // Start loading the image preview from Firebase Storage
                    if (storage != null && storageReference != null)
                    {
                        StartCoroutine(LoadImagePreview(profilePicture.ImageUrl, storeItem.ItemImage));
                    }
                }
                else
                {
                    Debug.LogWarning($"StoreItemUI component not found on instantiated prefab for {profilePicture.Name}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error instantiating store item: {e.Message}");
            }
        }
        
        // Force layout update
        Canvas.ForceUpdateCanvases();
        
        // Refresh layout group
        if (gridLayout != null)
        {
            gridLayout.enabled = false;
            gridLayout.enabled = true;
        }
        
        // If items were added, scroll to top
        if (scrollRect != null)
        {
            scrollRect.normalizedPosition = new Vector2(0, 1);
        }
    }

    private IEnumerator LoadImagePreview(string imageUrl, Image targetImage)
    {
        if (string.IsNullOrEmpty(imageUrl) || storageReference == null)
        {
            yield break;
        }
        
        Debug.Log($"Loading image from Firebase: {imageUrl}");
        
        // Get the storage reference
        StorageReference imageRef = null;
        
        try
        {
            imageRef = storageReference.Child(imageUrl);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error getting storage reference: {e.Message}");
            yield break;
        }
        
        if (imageRef == null)
        {
            Debug.LogError("Could not get storage reference");
            yield break;
        }
        
        // Get the download URL outside of try/catch
        var downloadUrlTask = imageRef.GetDownloadUrlAsync();
        
        // Wait until task completes
        while (!downloadUrlTask.IsCompleted)
        {
            yield return null;
        }
        
        // Check for errors
        if (downloadUrlTask.IsFaulted || downloadUrlTask.IsCanceled)
        {
            Debug.LogError("Failed to get download URL: " + 
                (downloadUrlTask.Exception != null ? downloadUrlTask.Exception.Message : "Unknown error"));
            yield break;
        }
        
        string url = null;
        
        try
        {
            // Get the URL
            Uri uri = downloadUrlTask.Result;
            url = uri.ToString();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error getting download URL result: {e.Message}");
            yield break;
        }
        
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogError("Download URL is null or empty");
            yield break;
        }
        
        Debug.Log($"Got download URL: {url}");
        
        // Download the image using UnityWebRequest
        UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
        yield return request.SendWebRequest();
        
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Failed to download image: {request.error}");
            yield break;
        }
        
        // Process the downloaded texture
        Texture2D texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
        if (texture == null)
        {
            Debug.LogError("Downloaded texture is null");
            yield break;
        }
        
        try
        {
            // Create a sprite from the texture
            Sprite sprite = Sprite.Create(
                texture, 
                new Rect(0, 0, texture.width, texture.height), 
                new Vector2(0.5f, 0.5f)
            );
            
            // Set the sprite to the target image
            if (targetImage != null)
            {
                targetImage.sprite = sprite;
                Debug.Log($"Successfully loaded image for {imageUrl}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error creating sprite: {e.Message}");
        }
    }

    public void PurchaseProfilePicture(ProfilePicture profilePicture)
    {
        if (purchasedProfileIds.Contains(profilePicture.Id))
        {
            ShowStatus("You already own this profile picture!");
            return;
        }
        
        if (playerCredits < profilePicture.Price)
        {
            ShowStatus("Not enough credits to purchase this item!");
            return;
        }
        
        StartCoroutine(PurchaseProfilePictureCoroutine(profilePicture));
    }

    private IEnumerator PurchaseProfilePictureCoroutine(ProfilePicture profilePicture)
    {
        ShowStatus("Processing purchase...");
        
        // Simulate server validation and processing
        yield return new WaitForSeconds(0.5f);
        
        // Deduct credits
        playerCredits -= profilePicture.Price;
        
        // Add to purchased items
        purchasedProfileIds.Add(profilePicture.Id);
        
        // Save to player data
        SavePlayerData();
        
        // Update UI
        UpdatePlayerCreditsUI();
        PopulateStoreItems();
        
        // Select the newly purchased profile picture
        SelectProfilePicture(profilePicture);
        
        // Record purchase in Firebase (in a real implementation)
        RecordPurchaseInFirebase(profilePicture);
        
        ShowStatus($"Successfully purchased {profilePicture.Name}!");
        
        // Download the full size image immediately
        yield return StartCoroutine(DownloadProfilePicture(profilePicture));
        
        // Notify other players about the purchase through the network
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            NotifyProfileUpdateServerRpc(profilePicture.Id);
        }
    }

    private IEnumerator DownloadProfilePicture(ProfilePicture profilePicture)
    {
        ShowStatus("Downloading profile picture...");
        
        // If Firebase is not initialized, we can't download
        if (storage == null || storageReference == null)
        {
            ShowStatus("Cannot download. Firebase not initialized.");
            yield break;
        }
        
        // Get the storage reference
        StorageReference imageRef = null;
        
        try
        {
            imageRef = storageReference.Child(profilePicture.ImageUrl);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error getting storage reference: {e.Message}");
            ShowStatus("Download failed. Please try again.");
            yield break;
        }
        
        // Get the download URL - outside try-catch
        var downloadUrlTask = imageRef.GetDownloadUrlAsync();
        
        // Wait until task completes
        while (!downloadUrlTask.IsCompleted)
        {
            yield return null;
        }
        
        // Check for errors
        if (downloadUrlTask.IsFaulted || downloadUrlTask.IsCanceled)
        {
            Debug.LogError("Failed to get download URL: " + 
                (downloadUrlTask.Exception != null ? downloadUrlTask.Exception.Message : "Unknown error"));
            ShowStatus("Download failed. Please try again.");
            yield break;
        }
        
        string url = null;
        
        try
        {
            // Get the URL
            Uri uri = downloadUrlTask.Result;
            url = uri.ToString();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error getting download URL result: {e.Message}");
            ShowStatus("Download failed. Please try again.");
            yield break;
        }
        
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogError("Download URL is null or empty");
            ShowStatus("Download failed. Please try again.");
            yield break;
        }
        
        // Download the image
        UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
        yield return request.SendWebRequest();
        
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Failed to download image: {request.error}");
            ShowStatus("Download failed. Please try again.");
            yield break;
        }
        
        // Process the downloaded texture
        Texture2D texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
        if (texture == null)
        {
            Debug.LogError("Downloaded texture is null");
            ShowStatus("Download failed. Please try again.");
            yield break;
        }
        
        // Save texture to file
        try
        {
            SaveTextureToFile(texture, profilePicture.Id);
            ShowStatus("Download complete!");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error saving texture to file: {e.Message}");
            ShowStatus("Failed to save downloaded image.");
        }
    }

    private void SaveTextureToFile(Texture2D texture, string profileId)
    {
        try
        {
            // Convert texture to PNG bytes
            byte[] pngBytes = texture.EncodeToPNG();
            
            // Save to persistent data path
            string filePath = System.IO.Path.Combine(Application.persistentDataPath, $"profile_{profileId}.png");
            System.IO.File.WriteAllBytes(filePath, pngBytes);
            
            Debug.Log($"Saved profile picture to: {filePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error saving texture to file: {e.Message}");
        }
    }

    public void SelectProfilePicture(ProfilePicture profilePicture)
    {
        if (!purchasedProfileIds.Contains(profilePicture.Id))
        {
            ShowStatus("You need to purchase this item first!");
            return;
        }
        
        // Always update local state
        selectedProfileId = profilePicture.Id;
        
        // Save the selection locally
        SavePlayerData();
        
        // Update UI
        PopulateStoreItems();
        UpdatePlayerProfileImage();
        
        ShowStatus($"Selected {profilePicture.Name} as your profile picture!");
        
        // Notify GameUIDLCIntegration about profile selection
        if (gameUIDLCIntegration != null)
        {
            gameUIDLCIntegration.OnProfileSelected(selectedProfileId);
        }
        
        // Network sync - use ServerRpc to tell server about our profile selection
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            // Use ServerRpc instead of directly writing to network variable
            UpdateProfileServerRpc(selectedProfileId, PlayerPrefs.GetString("PlayerName", "Player" + NetworkManager.Singleton.LocalClientId));
        }
    }

    // Add these methods to DLCManager.cs
    [ServerRpc(RequireOwnership = false)]
    private void UpdateProfileServerRpc(string profileId, string playerName, ServerRpcParams rpcParams = default)
    {
        // Get the client ID from the RPC parameters
        ulong clientId = rpcParams.Receive.SenderClientId;
        
        Debug.Log($"Server received profile update from client {clientId}: Profile={profileId}, Name={playerName}");
        
        // Find the client's NetworkObject
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient networkClient))
        {
            // Find the player's DLCManager object
            DLCManager clientDLCManager = networkClient.PlayerObject.GetComponent<DLCManager>();
            if (clientDLCManager != null)
            {
                // Update the NetworkVariable on the server for this specific client's object
                // This works because we're now on the server, which can modify any NetworkVariable
                clientDLCManager.playerProfileData.Value = new ProfilePictureNetworkData
                {
                    ProfileId = profileId,
                    PlayerName = playerName
                };
                
                // Broadcast to all clients
                NotifyProfileUpdateClientRpc(profileId, playerName, clientId);
            }
        }
    }

    [ClientRpc]
    private void NotifyProfileUpdateClientRpc(string profileId, string playerName, ulong clientId)
    {
        // Only process if it's not our own update (we already updated our local UI)
        if (NetworkManager.Singleton.LocalClientId != clientId)
        {
            Debug.Log($"Client received profile update for player {clientId}: {profileId}");
            
            // Update the other player's UI
            if (gameUIDLCIntegration != null)
            {
                gameUIDLCIntegration.UpdateOtherPlayerProfile(profileId, playerName);
            }
        }
    }

    private void UpdatePlayerProfileImage()
    {
        if (string.IsNullOrEmpty(selectedProfileId) || playerProfileImage == null)
        {
            // Use default image
            if (playerProfileImage != null)
                playerProfileImage.sprite = null;
            return;
        }
        
        // Try to load from saved file
        string filePath = System.IO.Path.Combine(Application.persistentDataPath, $"profile_{selectedProfileId}.png");
        
        if (System.IO.File.Exists(filePath))
        {
            StartCoroutine(LoadImageFromFile(filePath, playerProfileImage));
        }
        else
        {
            // Try to find the profile picture in the available list
            ProfilePicture profile = availableProfilePictures.Find(p => p.Id == selectedProfileId);
            
            if (profile != null && storage != null && storageReference != null)
            {
                // Download the image
                StartCoroutine(LoadImagePreview(profile.ImageUrl, playerProfileImage));
            }
        }
    }

    private IEnumerator LoadImageFromFile(string filePath, Image targetImage)
    {
        if (string.IsNullOrEmpty(filePath) || targetImage == null)
        {
            yield break;
        }
        
        byte[] data = null;
        
        // Load data from file outside of try-catch
        try
        {
            data = System.IO.File.ReadAllBytes(filePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error reading file: {e.Message}");
            yield break;
        }
        
        if (data == null || data.Length == 0)
        {
            Debug.LogError("File data is null or empty");
            yield break;
        }
        
        // Create texture
        Texture2D texture = new Texture2D(2, 2);
        bool loaded = false;
        
        try
        {
            loaded = texture.LoadImage(data);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading image data: {e.Message}");
            yield break;
        }
        
        if (!loaded || texture == null)
        {
            Debug.LogError("Failed to load image data into texture");
            yield break;
        }
        
        Sprite sprite = null;
        
        // Create sprite outside try-catch
        try
        {
            sprite = Sprite.Create(
                texture, 
                new Rect(0, 0, texture.width, texture.height), 
                new Vector2(0.5f, 0.5f)
            );
        }
        catch (Exception e)
        {
            Debug.LogError($"Error creating sprite: {e.Message}");
            yield break;
        }
        
        if (sprite == null)
        {
            Debug.LogError("Failed to create sprite");
            yield break;
        }
        
        // Apply sprite to image
        targetImage.sprite = sprite;
        
        yield return null;
    }

    private void RecordPurchaseInFirebase(ProfilePicture profilePicture)
    {
        if (databaseReference == null || NetworkManager.Singleton == null)
        {
            return;
        }
        
        try
        {
            string userId = NetworkManager.Singleton.LocalClientId.ToString();
            
            // Create purchase record
            Dictionary<string, object> purchaseData = new Dictionary<string, object>
            {
                { "profileId", profilePicture.Id },
                { "profileName", profilePicture.Name },
                { "price", profilePicture.Price },
                { "purchaseTime", DateTime.UtcNow.ToString("o") }
            };
            
            // Save to Firebase
            databaseReference.Child("purchases").Child(userId).Child(profilePicture.Id).SetValueAsync(purchaseData)
                .ContinueWithOnMainThread(task => 
                {
                    if (task.Exception != null)
                    {
                        Debug.LogWarning($"Failed to record purchase in Firebase: {task.Exception}");
                    }
                    else
                    {
                        Debug.Log("Purchase recorded in Firebase successfully!");
                    }
                });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Error recording purchase in Firebase: {e.Message}");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyProfileUpdateServerRpc(string profileId)
    {
        // Broadcast to all clients
        NotifyProfileUpdateClientRpc(profileId, NetworkManager.Singleton.LocalClientId);
    }

    [ClientRpc]
    private void NotifyProfileUpdateClientRpc(string profileId, ulong clientId)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId)
        {
            Debug.Log($"Player {clientId} purchased/selected profile: {profileId}");
            // Update the UI representation of the other player
        }
    }

    private void SavePlayerData()
    {
        // Save selected profile
        PlayerPrefs.SetString("SelectedProfileId", selectedProfileId);
        
        // Save credits
        PlayerPrefs.SetInt("PlayerCredits", playerCredits);
        
        // Save purchased profiles
        PlayerPrefs.SetString("PurchasedProfiles", string.Join(",", purchasedProfileIds));
        
        // Save immediately
        PlayerPrefs.Save();
    }

    private void LoadPlayerData()
    {
        // Load selected profile
        selectedProfileId = PlayerPrefs.GetString("SelectedProfileId", "");
        
        // Load credits
        playerCredits = PlayerPrefs.GetInt("PlayerCredits", 1000);
        
        // Load purchased profiles
        string purchasedProfilesStr = PlayerPrefs.GetString("PurchasedProfiles", "");
        
        if (!string.IsNullOrEmpty(purchasedProfilesStr))
        {
            purchasedProfileIds = new List<string>(purchasedProfilesStr.Split(','));
        }
        
        // Update player profile image
        UpdatePlayerProfileImage();
    }

    private void UpdatePlayerCreditsUI()
    {
        if (playerCreditsText != null)
        {
            playerCreditsText.text = $"Credits: {playerCredits}";
        }
    }

    private void ShowStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log(message);
    }

    private void SetupUI()
    {
        // Make sure the canvas itself is active
        if (storeCanvas != null)
        {
            storeCanvas.SetActive(true);
        }
        
        // Find the panels (only if necessary)
        Transform parentCanvas = storeCanvas.transform;
        
        // Find ProfilePanel and make it active
        Transform profilePanel = parentCanvas.Find("ProfilePanel");
        if (profilePanel != null)
        {
            profilePanel.gameObject.SetActive(true);
            Debug.Log("ProfilePanel activated");
        }
        
        // Find GameUIPanel and make it active
        Transform gameUIPanel = parentCanvas.Find("GameUIPanel");
        if (gameUIPanel != null)
        {
            gameUIPanel.gameObject.SetActive(true);
            Debug.Log("GameUIPanel activated");
        }
        
        // Find StorePanel and make it inactive
        Transform storePanel = parentCanvas.Find("StorePanel");
        if (storePanel != null)
        {
            storePanel.gameObject.SetActive(false);
            Debug.Log("StorePanel deactivated");
        }
    }

    // UI Button handlers
    public void OpenStore()
    {
        // First check if the storeCanvas exists
        if (storeCanvas == null)
        {
            Debug.LogError("Store canvas is null! Cannot open store.");
            return;
        }
        
        // Find the StorePanel
        Transform panel = storeCanvas.transform.Find("StorePanel");
        if (panel == null)
        {
            Debug.LogError("StorePanel not found as a child of storeCanvas");
            return;
        }
        
        // Activate the panel
        panel.gameObject.SetActive(true);
        
        // Check if storeItemPrefab is assigned before loading items
        if (storeItemPrefab == null)
        {
            Debug.LogError("Store item prefab is not assigned! Please assign a valid prefab in the inspector.");
            return;
        }
        
        // Load available profile pictures
        try {
            LoadProfilePicturesFromFirebase();
        }
        catch (Exception e) {
            Debug.LogError($"Error loading profile pictures: {e.Message}");
            ShowStatus("Error loading store items. Please try again.");
        }
    }

    public void CloseStore()
    {
        // First check if the storeCanvas exists
        if (storeCanvas == null)
        {
            Debug.LogError("Store canvas is null! Cannot close store.");
            return;
        }
        
        // Find the StorePanel
        Transform panel = storeCanvas.transform.Find("StorePanel");
        if (panel == null)
        {
            Debug.LogError("StorePanel not found as a child of storeCanvas");
            return;
        }
        
        // Deactivate the panel
        panel.gameObject.SetActive(false);
    }

    // Debug methods for testing
    public void AddCredits(int amount)
    {
        playerCredits += amount;
        UpdatePlayerCreditsUI();
        SavePlayerData();
        ShowStatus($"Added {amount} credits!");
    }
    
    // Helper property for Firebase initialization state
    private bool firebaseInitialized => storage != null && databaseReference != null;
}