using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    public SessionManager fusionMaager;
    public InputField roomInput;

    public void onClickCreateRoom()
    {
        fusionMaager.CreateRoom(roomInput.text);
    }
}
