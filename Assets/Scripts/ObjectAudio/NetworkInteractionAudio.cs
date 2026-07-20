using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(AudioSource))]
public class NetworkInteractionAudio : NetworkBehaviour
{
    public enum InteractionSoundType : byte
    {
        Grab = 0,
        Release = 1,
        Impact = 2,
        Activate = 3
    }

    [Header("Audio Source")]
    [SerializeField]
    private AudioSource audioSource;

    [Header("Interaction Clips")]
    [SerializeField]
    private AudioClip grabClip;

    [SerializeField]
    private AudioClip releaseClip;

    [SerializeField]
    private AudioClip impactClip;

    [SerializeField]
    private AudioClip activateClip;

    [Header("Volume")]
    [SerializeField, Range(0f, 1f)]
    private float grabVolume = 1f;

    [SerializeField, Range(0f, 1f)]
    private float releaseVolume = 1f;

    [SerializeField, Range(0f, 1f)]
    private float impactVolume = 1f;

    [SerializeField, Range(0f, 1f)]
    private float activateVolume = 1f;

    [Header("Impact Settings")]
    [SerializeField]
    private float minimumImpactVelocity = 0.5f;

    [SerializeField]
    private float maximumImpactVelocity = 6f;

    [SerializeField]
    private float impactCooldown = 0.1f;

    private float nextAllowedImpactTime;

    private void Reset()
    {
        audioSource = GetComponent<AudioSource>();
    }

    public override void Spawned()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        ConfigureAudioSource();
    }

    private void ConfigureAudioSource()
    {
        if (audioSource == null)
        {
            return;
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 1f;
    }

    /// <summary>
    /// 在任何客户端调用此方法，请求所有客户端播放指定音效。
    /// </summary>
    public void RequestPlaySound(InteractionSoundType soundType)
    {
        if (Object == null || !Object.IsValid)
        {
            // 网络对象尚未 Spawn 时，只在本地播放。
            PlaySoundLocally(soundType, 1f);
            return;
        }

        RPC_RequestPlaySound(soundType, 1f);
    }

    /// <summary>
    /// 播放带强度参数的音效，适合碰撞音量。
    /// </summary>
    public void RequestPlaySound(
        InteractionSoundType soundType,
        float intensity)
    {
        intensity = Mathf.Clamp01(intensity);

        if (Object == null || !Object.IsValid)
        {
            PlaySoundLocally(soundType, intensity);
            return;
        }

        RPC_RequestPlaySound(soundType, intensity);
    }

    /// <summary>
    /// 任意客户端向 State Authority 发起播放请求。
    /// </summary>
    [Rpc(
        RpcSources.All,
        RpcTargets.StateAuthority,
        Channel = RpcChannel.Reliable)]
    private void RPC_RequestPlaySound(
        InteractionSoundType soundType,
        float intensity,
        RpcInfo info = default)
    {
        intensity = Mathf.Clamp01(intensity);

        // 可以在这里做服务器验证，例如：
        // 1. 检查请求者是否靠近物体；
        // 2. 检查物体当前是否允许交互；
        // 3. 限制调用频率。

        RPC_PlaySoundForEveryone(soundType, intensity);
    }

    /// <summary>
    /// 由 State Authority 通知所有客户端播放。
    /// </summary>
    [Rpc(
        RpcSources.StateAuthority,
        RpcTargets.All,
        Channel = RpcChannel.Reliable)]
    private void RPC_PlaySoundForEveryone(
        InteractionSoundType soundType,
        float intensity)
    {
        PlaySoundLocally(soundType, intensity);
    }

    private void PlaySoundLocally(
        InteractionSoundType soundType,
        float intensity)
    {
        if (audioSource == null)
        {
            return;
        }

        AudioClip clip = null;
        float baseVolume = 1f;

        switch (soundType)
        {
            case InteractionSoundType.Grab:
                clip = grabClip;
                baseVolume = grabVolume;
                break;

            case InteractionSoundType.Release:
                clip = releaseClip;
                baseVolume = releaseVolume;
                break;

            case InteractionSoundType.Impact:
                clip = impactClip;
                baseVolume = impactVolume;
                break;

            case InteractionSoundType.Activate:
                clip = activateClip;
                baseVolume = activateVolume;
                break;
        }

        if (clip == null)
        {
            return;
        }

        float finalVolume = Mathf.Clamp01(baseVolume * intensity);
        audioSource.PlayOneShot(clip, finalVolume);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (Time.time < nextAllowedImpactTime)
        {
            return;
        }

        float impactVelocity = collision.relativeVelocity.magnitude;

        if (impactVelocity < minimumImpactVelocity)
        {
            return;
        }

        nextAllowedImpactTime = Time.time + impactCooldown;

        float intensity = Mathf.InverseLerp(
            minimumImpactVelocity,
            maximumImpactVelocity,
            impactVelocity);

        /*
         * 只有 State Authority 检测并广播物理碰撞，
         * 防止 Host 和 Client 都检测同一次碰撞，
         * 导致音效播放两次。
         */
        if (Object != null &&
            Object.IsValid &&
            Object.HasStateAuthority)
        {
            RPC_PlaySoundForEveryone(
                InteractionSoundType.Impact,
                intensity);
        }
    }
}