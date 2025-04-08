using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI component for a saved game item in the load game panel
/// </summary>
public class SavedGameItemUI : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI gameNameText;
    [SerializeField] private TextMeshProUGUI gameDateText;
    [SerializeField] private TextMeshProUGUI gameMoveCountText;
    [SerializeField] private TextMeshProUGUI gamePlayersText;
    [SerializeField] private Button loadButton;
    [SerializeField] private Button deleteButton;
    
    // Events
    public event Action<string> OnLoadClicked;
    public event Action<string> OnDeleteClicked;
    
    // Save ID for reference
    private string saveId;
    
    public void Setup(string id, string name, string date, string moveCount, string players)
    {
        this.saveId = id;
        
        // Set text elements
        if (gameNameText != null)
            gameNameText.text = name;
            
        if (gameDateText != null)
            gameDateText.text = date;
            
        if (gameMoveCountText != null)
            gameMoveCountText.text = moveCount;
            
        if (gamePlayersText != null)
            gamePlayersText.text = players;
        
        // Set up button events
        if (loadButton != null)
        {
            loadButton.onClick.RemoveAllListeners();
            loadButton.onClick.AddListener(OnLoadButtonClicked);
        }
        
        if (deleteButton != null)
        {
            deleteButton.onClick.RemoveAllListeners();
            deleteButton.onClick.AddListener(OnDeleteButtonClicked);
        }
    }
    
    private void OnLoadButtonClicked()
    {
        OnLoadClicked?.Invoke(saveId);
    }
    
    private void OnDeleteButtonClicked()
    {
        OnDeleteClicked?.Invoke(saveId);
    }
}