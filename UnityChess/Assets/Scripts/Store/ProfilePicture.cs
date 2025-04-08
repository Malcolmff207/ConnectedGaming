using System;
using UnityEngine;

/// <summary>
/// Data class representing a purchasable profile picture in the DLC store
/// </summary>
[Serializable]
public class ProfilePicture
{
    public string Id;
    public string Name;
    public int Price;
    public string ImageUrl;
    
    // Default constructor
    public ProfilePicture()
    {
    }
    
    // Parameterized constructor
    public ProfilePicture(string id, string name, int price, string imageUrl)
    {
        Id = id;
        Name = name;
        Price = price;
        ImageUrl = imageUrl;
    }
}