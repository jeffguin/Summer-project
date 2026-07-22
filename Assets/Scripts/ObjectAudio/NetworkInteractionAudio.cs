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

    private void Awake()
    {
        EnsureAudioSource();
        ConfigureAudioSource();
    }

    private void Reset()
    {
        audioSource = GetComponent<AudioSource>();
        ConfigureAudioSource();
    }

    public override void Spawned()
    {
        EnsureAudioSource();
        ConfigureAudioSource();
    }

    private void EnsureAudioSource()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }

    private void ConfigureAudioSource()
    {
        if (audioSource == null)
        {
            return;
        }

        audioSource.enabled = true;
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 1f;
    }

    // These parameterless methods can be connected directly to UnityEvents.
    public void RequestPlayGrab()
    {
        RequestPlaySound(InteractionSoundType.Grab);
    }

    public void RequestPlayRelease()
    {
        RequestPlaySound(InteractionSoundType.Release);
    }

    public void RequestPlayActivate()
    {
        RequestPlaySound(InteractionSoundType.Activate);
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
    /// 供已经由 State Authority 验证通过的交互调用，避免再次绕行请求 RPC。
    /// </summary>
    public void PlayFromStateAuthority(
        InteractionSoundType soundType,
        float intensity = 1f)
    {
        intensity = Mathf.Clamp01(intensity);

        if (Object == null || !Object.IsValid)
        {
            PlaySoundLocally(soundType, intensity);
            return;
        }

        if (!Object.HasStateAuthority)
        {
            Debug.LogWarning(
                $"[NetworkInteractionAudio] {gameObject.name}: " +
                "PlayFromStateAuthority can only be called by State Authority."
            );
            return;
        }

        RPC_PlaySoundForEveryone(soundType, intensity);
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
        EnsureAudioSource();

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
        // 只让 State Authority 判断物理碰撞，避免双方对同一次碰撞重复广播。
        if (Object == null ||
            !Object.IsValid ||
            !Object.HasStateAuthority)
        {
            return;
        }

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

        PlayFromStateAuthority(
            InteractionSoundType.Impact,
            intensity);
    }
}
