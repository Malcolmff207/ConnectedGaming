using UnityEngine;
using TMPro;

/// <summary>
/// Represents a single statistic item in the analytics dashboard
/// </summary>
public class StatItemUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TextMeshProUGUI valueText;
    
    public void SetData(string label, string value)
    {
        if (labelText != null)
            labelText.text = label;
            
        if (valueText != null)
            valueText.text = value;
    }
}