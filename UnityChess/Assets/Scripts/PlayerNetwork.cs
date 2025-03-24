using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerNetwork : NetworkBehaviour
{
    private NetworkVariable<int> _playerRoll = 
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Owner,
        NetworkVariableWritePermission.Owner);

    private NetworkVariable<FixedString32Bytes> _greeting = new NetworkVariable<FixedString32Bytes>
    ("Greetings",NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        //Subscribe to changes in _playerRoll
        _playerRoll.OnValueChanged += PlayerRollChanged;
        //Subscribe to changes in _greeting
        _greeting.OnValueChanged += PlayerGreetingChanged;
    }


    public override void OnNetworkDespawn()
    {
        //Unsubscribe to changes in _playerRoll
        _playerRoll.OnValueChanged -= PlayerRollChanged;
        //Unsubscribe to changes in _greeting
        _greeting.OnValueChanged -= PlayerGreetingChanged;
    }

    private void PlayerRollChanged(int prevRoll, int newRoll)
    {
        Debug.Log($"The roll value of {OwnerClientId} has changed from {prevRoll} to {newRoll}");    
    }
    
    private void PlayerGreetingChanged(FixedString32Bytes prevGreet, FixedString32Bytes newGreet)
    {
        Debug.Log($"{newGreet}");    
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (Input.GetKeyDown(KeyCode.R))
        {
            _playerRoll.Value = Random.Range(0, 100);
        }
        if (Input.GetKeyDown(KeyCode.G))
        {
            _greeting.Value = "Happy Halloween!";
        }
    }
}
