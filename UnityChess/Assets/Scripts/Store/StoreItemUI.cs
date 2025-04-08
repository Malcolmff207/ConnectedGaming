using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Manages the UI for a single store item in the DLC store
/// </summary>
public class StoreItemUI : MonoBehaviour
{
    // UI Elements
    [Header("UI Elements")]
    public Image ItemImage;
    public TextMeshProUGUI ItemNameText;
    public TextMeshProUGUI ItemPriceText;
    public Button PurchaseButton;
    public Button SelectButton;
    public GameObject PurchasedBadge;
    public GameObject SelectedBadge;
    
    // Events
    public event Action<ProfilePicture> OnPurchaseClicked;
    public event Action<ProfilePicture> OnSelectClicked;
    
    // Data
    private ProfilePicture profilePicture;
    private bool isPurchased = false;
    private bool isSelected = false;
    
    private void OnEnable()
    {
        // Make sure references are valid
        ValidateReferences();
    }
    
    private void ValidateReferences()
    {
        // Check UI references and log errors for debugging
        if (ItemImage == null)
            Debug.LogError($"ItemImage is missing on {gameObject.name}", this);
            
        if (ItemNameText == null)
            Debug.LogError($"ItemNameText is missing on {gameObject.name}", this);
            
        if (ItemPriceText == null)
            Debug.LogError($"ItemPriceText is missing on {gameObject.name}", this);
            
        if (PurchaseButton == null)
            Debug.LogError($"PurchaseButton is missing on {gameObject.name}", this);
            
        if (SelectButton == null)
            Debug.LogError($"SelectButton is missing on {gameObject.name}", this);
            
        if (PurchasedBadge == null)
            Debug.LogError($"PurchasedBadge is missing on {gameObject.name}", this);
            
        if (SelectedBadge == null)
            Debug.LogError($"SelectedBadge is missing on {gameObject.name}", this);
    }
    
    // Initialize the store item with data
    public void Setup(ProfilePicture profile, bool purchased, bool selected)
    {
        this.profilePicture = profile;
        this.isPurchased = purchased;
        this.isSelected = selected;
        
        if (profile == null)
        {
            Debug.LogError("Profile is null in Setup");
            return;
        }
        
        // Update UI
        if (ItemNameText != null)
            ItemNameText.text = profile.Name;
            
        if (ItemPriceText != null)
            ItemPriceText.text = isPurchased ? "OWNED" : profile.Price.ToString() + " Credits";
        
        // Update button states
        if (PurchaseButton != null)
        {
            PurchaseButton.gameObject.SetActive(!isPurchased);
            PurchaseButton.onClick.RemoveAllListeners();
            PurchaseButton.onClick.AddListener(OnPurchaseButtonClicked);
        }
        
        if (SelectButton != null)
        {
            SelectButton.gameObject.SetActive(isPurchased && !isSelected);
            SelectButton.onClick.RemoveAllListeners();
            SelectButton.onClick.AddListener(OnSelectButtonClicked);
        }
        
        // Update indicators
        if (PurchasedBadge != null)
            PurchasedBadge.SetActive(isPurchased);
            
        if (SelectedBadge != null)
            SelectedBadge.SetActive(isSelected);
    }
    
    private void OnPurchaseButtonClicked()
    {
        if (profilePicture == null)
        {
            Debug.LogError("Profile picture is null in purchase button click");
            return;
        }
        
        OnPurchaseClicked?.Invoke(profilePicture);
    }
    
    private void OnSelectButtonClicked()
    {
        if (profilePicture == null)
        {
            Debug.LogError("Profile picture is null in select button click");
            return;
        }
        
        OnSelectClicked?.Invoke(profilePicture);
    }
    
    private void OnDestroy()
    {
        // Clean up event listeners
        if (PurchaseButton != null)
            PurchaseButton.onClick.RemoveAllListeners();
            
        if (SelectButton != null)
            SelectButton.onClick.RemoveAllListeners();
    }
}