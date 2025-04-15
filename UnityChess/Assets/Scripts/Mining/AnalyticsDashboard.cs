using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class AnalyticsDashboard : MonoBehaviour
{
    [SerializeField] private GameObject dashboardPanel;
    [SerializeField] private Transform statsContainer; // Just needs to be a GameObject with VerticalLayoutGroup
    [SerializeField] private Button closeButton;
    [SerializeField] private Button refreshButton;
    [SerializeField] private TextMeshProUGUI statusText;
    
    // Template for text style - set this in the inspector
    [SerializeField] private TextMeshProUGUI textTemplate;
    
    private AnalyticsManager analyticsManager;
    
    private void Start()
    {
        // Find analytics manager
        analyticsManager = AnalyticsManager.Instance;
        
        // Setup UI
        if (closeButton != null)
            closeButton.onClick.AddListener(CloseDashboard);
            
        if (refreshButton != null)
            refreshButton.onClick.AddListener(RefreshData);
        
        // Hide dashboard initially
        if (dashboardPanel != null)
            dashboardPanel.SetActive(false);
    }
    
    public void OpenDashboard()
    {
        if (dashboardPanel != null)
        {
            dashboardPanel.SetActive(true);
            RefreshData();
        }
    }
    
    public void CloseDashboard()
    {
        if (dashboardPanel != null)
            dashboardPanel.SetActive(false);
    }
    
    public void RefreshData()
    {
        if (analyticsManager == null)
        {
            ShowStatus("Analytics Manager not found");
            return;
        }
        
        ShowStatus("Loading analytics data...");
        
        // Clear existing stats
        ClearStats();
        
        // Load session stats
        Dictionary<string, int> sessionStats = analyticsManager.GetCurrentSessionStats();
        AddStatItem("Games Played", sessionStats.ContainsKey("games_played") ? sessionStats["games_played"].ToString() : "0");
        AddStatItem("Total Moves", sessionStats.ContainsKey("total_moves") ? sessionStats["total_moves"].ToString() : "0");
        AddStatItem("Checks", sessionStats.ContainsKey("checks") ? sessionStats["checks"].ToString() : "0");
        AddStatItem("Checkmates", sessionStats.ContainsKey("checkmates") ? sessionStats["checkmates"].ToString() : "0");
        
        AddSeparator();
        AddHeader("MATCH RESULTS");
        
        // Load win/loss stats
        analyticsManager.GetWinLossStatistics(stats => 
        {
            int totalMatches = stats.ContainsKey("total_matches") ? stats["total_matches"] : 0;
            int whiteWins = stats.ContainsKey("white_wins") ? stats["white_wins"] : 0;
            int blackWins = stats.ContainsKey("black_wins") ? stats["black_wins"] : 0;
            int draws = stats.ContainsKey("draws") ? stats["draws"] : 0;
            
            AddStatItem("Total Matches", totalMatches.ToString());
            AddStatItem("White Wins", whiteWins.ToString());
            AddStatItem("Black Wins", blackWins.ToString());
            AddStatItem("Draws", draws.ToString());
            
            AddSeparator();
            AddHeader("POPULAR PIECES");
        });
        
        // Load piece stats
        analyticsManager.GetMostUsedPieces(pieces => 
        {
            foreach (var piece in pieces)
            {
                AddStatItem(piece.Key, piece.Value.ToString() + " moves");
            }
            
            AddSeparator();
            AddHeader("POPULAR OPENINGS");
        });
        
        // Load opening moves
        analyticsManager.GetMostPopularOpeningMoves(5, moves => 
        {
            if (moves.Count == 0)
            {
                AddStatItem("No opening data yet", "");
            }
            else
            {
                foreach (var move in moves)
                {
                    AddStatItem(move.Key, move.Value.ToString() + " times");
                }
            }
            
            AddSeparator();
            AddHeader("DLC PURCHASES");
        });
        
        // Load DLC stats
        analyticsManager.GetTopPurchasedDLCItems(5, items => 
        {
            if (items.Count == 0)
            {
                AddStatItem("No purchase data yet", "");
            }
            else
            {
                foreach (var item in items)
                {
                    string displayName = item.Key;
                    if (displayName.StartsWith("profile_"))
                    {
                        displayName = displayName.Substring(8);
                        displayName = char.ToUpper(displayName[0]) + displayName.Substring(1);
                    }
                    AddStatItem(displayName + " Avatar", item.Value.ToString());
                }
            }
            
            ShowStatus("Data loaded successfully");
        });
    }
    
    private void ClearStats()
    {
        if (statsContainer == null) return;
        
        // Destroy all child objects
        foreach (Transform child in statsContainer)
        {
            Destroy(child.gameObject);
        }
    }
    
    private void AddSeparator()
    {
        if (statsContainer == null) return;
        
        // Add an empty space
        GameObject spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(statsContainer, false);
        RectTransform spacerRect = spacer.GetComponent<RectTransform>();
        spacerRect.sizeDelta = new Vector2(100, 20);
    }
    
    private void AddHeader(string text)
    {
        if (statsContainer == null) return;
        
        // Create a text element for the header
        GameObject headerGO = new GameObject("Header", typeof(RectTransform), typeof(TextMeshProUGUI));
        headerGO.transform.SetParent(statsContainer, false);
        
        TextMeshProUGUI headerText = headerGO.GetComponent<TextMeshProUGUI>();
        // Copy font settings from template if available
        if (textTemplate != null)
        {
            headerText.font = textTemplate.font;
            headerText.fontSize = textTemplate.fontSize + 4; // Slightly larger
            headerText.color = Color.white;
        }
        
        headerText.text = text;
        headerText.alignment = TextAlignmentOptions.Center;
        headerText.fontStyle = FontStyles.Bold;
        
        // Set size
        RectTransform rect = headerGO.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(400, 30);
    }
    
    private void AddStatItem(string label, string value)
    {
        if (statsContainer == null) return;
        
        // Create a container for this stat item
        GameObject container = new GameObject("StatItem", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        container.transform.SetParent(statsContainer, false);
        
        // Configure layout
        HorizontalLayoutGroup layout = container.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(5, 5, 2, 2);
        
        // Create label text
        GameObject labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGO.transform.SetParent(container.transform, false);
        
        TextMeshProUGUI labelText = labelGO.GetComponent<TextMeshProUGUI>();
        if (textTemplate != null)
        {
            labelText.font = textTemplate.font;
            labelText.fontSize = textTemplate.fontSize;
            labelText.color = textTemplate.color;
        }
        labelText.text = label;
        labelText.alignment = TextAlignmentOptions.Left;
        
        // Create value text
        GameObject valueGO = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI));
        valueGO.transform.SetParent(container.transform, false);
        
        TextMeshProUGUI valueText = valueGO.GetComponent<TextMeshProUGUI>();
        if (textTemplate != null)
        {
            valueText.font = textTemplate.font;
            valueText.fontSize = textTemplate.fontSize;
            valueText.color = new Color(0.7f, 0.9f, 1f); // Slightly different color for values
        }
        valueText.text = value;
        valueText.alignment = TextAlignmentOptions.Right;
        
        // Set sizes
        RectTransform containerRect = container.GetComponent<RectTransform>();
        containerRect.sizeDelta = new Vector2(400, 30);
        
        RectTransform labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.sizeDelta = new Vector2(250, 30);
        
        RectTransform valueRect = valueGO.GetComponent<RectTransform>();
        valueRect.sizeDelta = new Vector2(150, 30);
    }
    
    private void ShowStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        
        Debug.Log($"[Analytics] {message}");
    }
}