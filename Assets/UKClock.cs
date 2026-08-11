using UnityEngine;
using TMPro;
using System;

public class UKClock : MonoBehaviour
{
    public TMP_Text clockText;

    void Update()
    {
        DateTime now = DateTime.Now;

        clockText.text = now.ToString("HH:mm:ss");
    }
}