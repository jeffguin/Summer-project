using Fusion;
using UnityEngine;

public class ActorAvatarNetworkSync : NetworkBehaviour
{
    [Header("Avatar Targets")]
    public Transform headTarget;
    public Transform leftHandTarget;
    public Transform rightHandTarget;

    [Header("Local Sources")]
    public Transform localHeadSource;
    public Transform localLeftHandSource;
    public Transform localRightHandSource;

    [Header("Smoothing")]
    public float positionLerpSpeed = 20f;
    public float rotationLerpSpeed = 20f;

    [Networked] public Vector3 NetHeadPosition { get; set; }
    [Networked] public Quaternion NetHeadRotation { get; set; }

    [Networked] public Vector3 NetLeftHandPosition { get; set; }
    [Networked] public Quaternion NetLeftHandRotation { get; set; }

    [Networked] public Vector3 NetRightHandPosition { get; set; }
    [Networked] public Quaternion NetRightHandRotation { get; set; }

    public void SetLocalSources(
        Transform head,
        Transform leftHand,
        Transform rightHand)
    {
        localHeadSource = head;
        localLeftHandSource = leftHand;
        localRightHandSource = rightHand;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        if (localHeadSource != null)
        {
            NetHeadPosition = localHeadSource.position;
            NetHeadRotation = localHeadSource.rotation;
        }

        if (localLeftHandSource != null)
        {
            NetLeftHandPosition = localLeftHandSource.position;
            NetLeftHandRotation = localLeftHandSource.rotation;
        }

        if (localRightHandSource != null)
        {
            NetRightHandPosition = localRightHandSource.position;
            NetRightHandRotation = localRightHandSource.rotation;
        }
    }

    private void Update()
    {
        if (Object == null)
            return;

        if (Object.HasStateAuthority)
        {
            ApplyLocalDirectly();
        }
        else
        {
            ApplyNetworkSmoothly();
        }
    }

    private void ApplyLocalDirectly()
    {
        if (localHeadSource != null && headTarget != null)
            headTarget.SetPositionAndRotation(localHeadSource.position, localHeadSource.rotation);

        if (localLeftHandSource != null && leftHandTarget != null)
            leftHandTarget.SetPositionAndRotation(localLeftHandSource.position, localLeftHandSource.rotation);

        if (localRightHandSource != null && rightHandTarget != null)
            rightHandTarget.SetPositionAndRotation(localRightHandSource.position, localRightHandSource.rotation);
    }

    private void ApplyNetworkSmoothly()
    {
        if (headTarget != null)
        {
            headTarget.position = Vector3.Lerp(
                headTarget.position,
                NetHeadPosition,
                Time.deltaTime * positionLerpSpeed
            );

            headTarget.rotation = Quaternion.Slerp(
                headTarget.rotation,
                NetHeadRotation,
                Time.deltaTime * rotationLerpSpeed
            );
        }

        if (leftHandTarget != null)
        {
            leftHandTarget.position = Vector3.Lerp(
                leftHandTarget.position,
                NetLeftHandPosition,
                Time.deltaTime * positionLerpSpeed
            );

            leftHandTarget.rotation = Quaternion.Slerp(
                leftHandTarget.rotation,
                NetLeftHandRotation,
                Time.deltaTime * rotationLerpSpeed
            );
        }

        if (rightHandTarget != null)
        {
            rightHandTarget.position = Vector3.Lerp(
                rightHandTarget.position,
                NetRightHandPosition,
                Time.deltaTime * positionLerpSpeed
            );

            rightHandTarget.rotation = Quaternion.Slerp(
                rightHandTarget.rotation,
                NetRightHandRotation,
                Time.deltaTime * rotationLerpSpeed
            );
        }
    }
}