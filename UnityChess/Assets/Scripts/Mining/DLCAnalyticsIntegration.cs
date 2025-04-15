using UnityEngine;
using System.Collections;

/// <summary>
/// Extension component for DLCManager that adds analytics capabilities
/// </summary>
[RequireComponent(typeof(DLCManager))]
public class DLCAnalyticsIntegration : MonoBehaviour
{
    private DLCManager dlcManager;
    private AnalyticsManager analyticsManager;
    
    // Track last known values to detect changes
    private int lastKnownCredits = -1;
    private System.Collections.Generic.List<string> lastKnownPurchases = new System.Collections.Generic.List<string>();
    
    private void Awake()
    {
        // Get references
        dlcManager = GetComponent<DLCManager>();
        
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
        // Initialize last known values
        lastKnownCredits = PlayerPrefs.GetInt("PlayerCredits", 1000);
        
        string purchasedProfiles = PlayerPrefs.GetString("PurchasedProfiles", "");
        if (!string.IsNullOrEmpty(purchasedProfiles))
        {
            lastKnownPurchases = new System.Collections.Generic.List<string>(purchasedProfiles.Split(','));
        }
        
        // Start monitoring for purchase events
        StartCoroutine(MonitorPurchaseEvents());
    }
    
    private IEnumerator MonitorPurchaseEvents()
    {
        while (true)
        {
            // Check if credits have decreased (possible purchase)
            int currentCredits = PlayerPrefs.GetInt("PlayerCredits", 1000);
            if (currentCredits < lastKnownCredits)
            {
                // Credits decreased, could be a purchase
                int creditsDifference = lastKnownCredits - currentCredits;
                
                // Check for new purchases
                string purchasedProfiles = PlayerPrefs.GetString("PurchasedProfiles", "");
                if (!string.IsNullOrEmpty(purchasedProfiles))
                {
                    string[] currentPurchases = purchasedProfiles.Split(',');
                    
                    // Find new purchases by comparing with last known purchases
                    foreach (string profile in currentPurchases)
                    {
                        if (!lastKnownPurchases.Contains(profile) && !string.IsNullOrEmpty(profile))
                        {
                            // New purchase detected
                            OnProfilePurchased(profile, creditsDifference);
                            
                            // Add to last known purchases
                            lastKnownPurchases.Add(profile);
                        }
                    }
                }
            }
            
            // Update last known credits
            lastKnownCredits = currentCredits;
            
            yield return new WaitForSeconds(1.0f);
        }
    }
    
    // Called when a profile is purchased
    private void OnProfilePurchased(string profileId, int price)
    {
        if (analyticsManager != null)
        {
            // Extract profile name from ID
            string profileName = profileId;
            if (profileId.StartsWith("profile_"))
            {
                profileName = profileId.Substring(8); // Remove "profile_" prefix
                profileName = char.ToUpper(profileName[0]) + profileName.Substring(1) + " Avatar"; // Capitalize and add "Avatar"
            }
            
            analyticsManager.LogDLCPurchase(profileId, profileName, price);
            Debug.Log($"Analytics: DLC purchased - ID: {profileId}, Name: {profileName}, Price: {price}");
        }
    }
    
    // Can be called from DLCManager with reflection or SendMessage
    public void OnDLCPurchased(ProfilePicture profile)
    {
        if (analyticsManager != null && profile != null)
        {
            analyticsManager.LogDLCPurchase(profile.Id, profile.Name, profile.Price);
            Debug.Log($"Analytics: DLC purchased directly - ID: {profile.Id}, Name: {profile.Name}, Price: {profile.Price}");
        }
    }
}