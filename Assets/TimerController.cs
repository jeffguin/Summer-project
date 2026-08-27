using UnityEngine;
using TMPro;
using System;

public class TimerController : MonoBehaviour
{
    public TMP_Text timerText;

    private float elapsedTime = 0f;
    private bool isRunning = false;

    void Update()
    {
        if (isRunning)
        {
            elapsedTime += Time.deltaTime;
            UpdateTimerDisplay();
        }
    }

    public void StartTimer()
    {
        isRunning = true;
    }

    public void StopTimer()
    {
        isRunning = false;
    }

    public void ResetTimer()
    {
        isRunning = false;
        elapsedTime = 0f;
        UpdateTimerDisplay();
    }

    private void UpdateTimerDisplay()
    {
        TimeSpan time = TimeSpan.FromSeconds(elapsedTime);

        timerText.text = string.Format(
            "{0:00}:{1:00}.{2:00}",
            (int)time.TotalMinutes,
            time.Seconds,
            time.Milliseconds / 10
        );
    }
}