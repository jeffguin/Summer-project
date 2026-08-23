#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Meta.XR.Movement.FaceTracking.Samples;
using Meta.XR.Movement.Networking;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Movement.MSDKUtility;

public static class NetworkFaceMappingRepair
{
    private static readonly string[] NetworkCharacterPrefabPaths =
    {
        "Assets/Prefabs/ActorPrefab/Suisei V2.3(Atlas-sui_al )  Variant.prefab",
        "Assets/Prefabs/ActorPrefab/Suisei V3.1 Networked Host.prefab"
    };

    private const int MaximumFusionPacketBytes = 1024;

    [MenuItem("Tools/Summer Project/Repair Network Face Mappings")]
    public static void RepairNetworkFaceMappings()
    {
        foreach (string prefabPath in NetworkCharacterPrefabPaths)
        {
            RepairPrefab(prefabPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "NetworkFaceMappingRepair: All network character face mappings " +
            "were repaired successfully."
        );
    }

    // Entry point for a headless Unity verification/repair run.
    public static void RepairNetworkFaceMappingsBatch()
    {
        try
        {
            RepairNetworkFaceMappings();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void RepairPrefab(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

        try
        {
            NetworkCharacterRetargeter retargeter =
                root.GetComponentInChildren<NetworkCharacterRetargeter>(true);

            if (retargeter == null)
            {
                throw new InvalidOperationException(
                    $"{prefabPath} has no NetworkCharacterRetargeter."
                );
            }

            OVRCustomFace directFace =
                root.GetComponentInChildren<OVRCustomFace>(true);

            if (directFace == null)
            {
                throw new InvalidOperationException(
                    $"{prefabPath} has no OVRCustomFace component."
                );
            }

            SkinnedMeshRenderer faceMesh =
                directFace.GetComponent<SkinnedMeshRenderer>();

            if (faceMesh == null || faceMesh.sharedMesh == null)
            {
                throw new InvalidOperationException(
                    $"{prefabPath} has no face mesh assigned to OVRCustomFace."
                );
            }

            int blendShapeCount = faceMesh.sharedMesh.blendShapeCount;
            int mappingCount = directFace.Mappings?.Length ?? 0;

            // These Suisei meshes have 83 face blend shapes. Refuse to save
            // if Unity has not loaded the real mesh/mapping data; this avoids
            // silently replacing a valid prefab mapping with an empty array.
            if (blendShapeCount <= 31 || mappingCount != blendShapeCount)
            {
                throw new InvalidOperationException(
                    $"Face mapping validation failed for {prefabPath}: " +
                    $"mesh blend shapes={blendShapeCount}, " +
                    $"OVRCustomFace mappings={mappingCount}."
                );
            }

            var trackedFaceIndices = new List<int>(mappingCount);

            for (int i = 0; i < mappingCount; i++)
            {
                OVRFaceExpressions.FaceExpression expression =
                    directFace.Mappings[i];

                if (expression >= 0 &&
                    expression < OVRFaceExpressions.FaceExpression.Max)
                {
                    trackedFaceIndices.Add(i);
                }
            }

            if (trackedFaceIndices.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{prefabPath} has no tracked OVRCustomFace mappings."
                );
            }

            int fullSnapshotBytes = ValidateNativeFaceConfigurationAndPacket(
                retargeter,
                faceMesh.sharedMesh,
                trackedFaceIndices,
                prefabPath
            );

            SerializedObject serializedRetargeter =
                new SerializedObject(retargeter);
            SerializedProperty shapePoseData =
                serializedRetargeter.FindProperty("_shapePoseData");
            SerializedProperty faceIndices =
                serializedRetargeter.FindProperty("_faceIndicesToSend");

            if (shapePoseData == null || faceIndices == null)
            {
                throw new InvalidOperationException(
                    $"Movement serialization fields were not found on {prefabPath}."
                );
            }

            shapePoseData.arraySize = blendShapeCount;
            faceIndices.arraySize = trackedFaceIndices.Count;

            for (int blendShapeIndex = 0;
                 blendShapeIndex < blendShapeCount;
                 blendShapeIndex++)
            {
                string blendShapeName =
                    faceMesh.sharedMesh.GetBlendShapeName(blendShapeIndex);

                if (string.IsNullOrEmpty(blendShapeName))
                {
                    throw new InvalidOperationException(
                        $"Face blend shape {blendShapeIndex} has no name in {prefabPath}."
                    );
                }

                SerializedProperty shape =
                    shapePoseData.GetArrayElementAtIndex(blendShapeIndex);
                shape.FindPropertyRelative("SkinnedMesh").objectReferenceValue =
                    faceMesh;
                shape.FindPropertyRelative("ShapeName").stringValue =
                    blendShapeName;
                shape.FindPropertyRelative("ShapeIndex").intValue =
                    blendShapeIndex;
            }

            for (int i = 0; i < trackedFaceIndices.Count; i++)
            {
                faceIndices.GetArrayElementAtIndex(i).intValue =
                    trackedFaceIndices[i];
            }

            serializedRetargeter.ApplyModifiedPropertiesWithoutUndo();

            // OVRCustomFace is the intended local Quest Pro driver. These
            // A2E sample components are an alternative pipeline and the
            // current prefab has no source WeightsProvider assigned.
            foreach (FaceDriver faceDriver in
                     root.GetComponentsInChildren<FaceDriver>(true))
            {
                faceDriver.enabled = false;
            }

            foreach (FaceRetargeterComponent faceRetargeter in
                     root.GetComponentsInChildren<FaceRetargeterComponent>(true))
            {
                faceRetargeter.enabled = false;
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

            Debug.Log(
                $"NetworkFaceMappingRepair: {prefabPath} now maps and " +
                $"receives all {blendShapeCount} face mesh blend shapes, " +
                $"sends {trackedFaceIndices.Count} Quest-tracked shapes, " +
                $"and produces a {fullSnapshotBytes}-byte full snapshot."
            );
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static int ValidateNativeFaceConfigurationAndPacket(
        NetworkCharacterRetargeter retargeter,
        Mesh faceMesh,
        List<int> trackedFaceIndices,
        string prefabPath)
    {
        if (string.IsNullOrEmpty(retargeter.Config) ||
            !CreateOrUpdateHandle(retargeter.Config, out ulong handle))
        {
            throw new InvalidOperationException(
                $"Could not open the Movement config for {prefabPath}."
            );
        }

        try
        {
            if (!GetSkeletonInfo(
                    handle,
                    SkeletonType.TargetSkeleton,
                    out SkeletonInfo targetInfo) ||
                !GetBlendShapeNames(
                    handle,
                    SkeletonType.TargetSkeleton,
                    out string[] nativeShapeNames))
            {
                throw new InvalidOperationException(
                    $"Could not read native target face data for {prefabPath}."
                );
            }

            if (targetInfo.BlendShapeCount != faceMesh.blendShapeCount ||
                nativeShapeNames.Length != faceMesh.blendShapeCount)
            {
                throw new InvalidOperationException(
                    $"Native face count mismatch for {prefabPath}: " +
                    $"native info={targetInfo.BlendShapeCount}, " +
                    $"native names={nativeShapeNames.Length}, " +
                    $"mesh={faceMesh.blendShapeCount}."
                );
            }

            for (int i = 0; i < nativeShapeNames.Length; i++)
            {
                string meshShapeName = faceMesh.GetBlendShapeName(i);

                if (!string.Equals(
                        nativeShapeNames[i],
                        meshShapeName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Native face shape {i} mismatch for {prefabPath}: " +
                        $"config='{nativeShapeNames[i]}', " +
                        $"mesh='{meshShapeName}'."
                    );
                }
            }

            if (targetInfo.JointCount != retargeter.NumberOfJoints)
            {
                throw new InvalidOperationException(
                    $"Native joint count mismatch for {prefabPath}: " +
                    $"native={targetInfo.JointCount}, " +
                    $"configured={retargeter.NumberOfJoints}."
                );
            }

            if (!GetSerializationSettings(
                    handle,
                    out SerializationSettings settings))
            {
                throw new InvalidOperationException(
                    $"Could not read serialization settings for {prefabPath}."
                );
            }

            settings.CompressionType = retargeter.CompressionType;
            settings.PositionThreshold = retargeter.PositionThreshold;
            settings.RotationAngleThresholdDegrees =
                retargeter.RotationAngleThreshold;
            settings.ShapeThreshold = retargeter.ShapeThreshold;

            if (!SetSerializationSettings(handle, settings))
            {
                throw new InvalidOperationException(
                    $"Could not set serialization settings for {prefabPath}."
                );
            }

            var bodyPose = new Unity.Collections.NativeArray<NativeTransform>(
                targetInfo.JointCount,
                Unity.Collections.Allocator.Temp,
                Unity.Collections.NativeArrayOptions.UninitializedMemory
            );
            var facePose = new Unity.Collections.NativeArray<float>(
                faceMesh.blendShapeCount,
                Unity.Collections.Allocator.Temp,
                Unity.Collections.NativeArrayOptions.UninitializedMemory
            );
            Unity.Collections.NativeArray<byte> packet = default;

            try
            {
                for (int i = 0; i < bodyPose.Length; i++)
                {
                    bodyPose[i] = NativeTransform.Identity();
                }

                for (int i = 0; i < facePose.Length; i++)
                {
                    facePose[i] = 0.15f + (i % 7) * 0.1f;
                }

                int[] bodyIndices = CreateSequentialIndices(bodyPose.Length);

                if (!SerializeSkeletonAndFace(
                        handle,
                        0f,
                        bodyPose,
                        facePose,
                        -1,
                        bodyIndices,
                        trackedFaceIndices.ToArray(),
                        ref packet))
                {
                    throw new InvalidOperationException(
                        $"Full snapshot serialization failed for {prefabPath}."
                    );
                }

                if (!packet.IsCreated || packet.Length == 0 ||
                    packet.Length > MaximumFusionPacketBytes)
                {
                    throw new InvalidOperationException(
                        $"Full snapshot for {prefabPath} is " +
                        $"{(packet.IsCreated ? packet.Length : 0)} bytes; " +
                        $"Fusion allows {MaximumFusionPacketBytes}."
                    );
                }

                ValidateSnapshotRoundTrip(
                    retargeter.Config,
                    packet,
                    targetInfo.JointCount,
                    faceMesh.blendShapeCount,
                    trackedFaceIndices,
                    prefabPath
                );

                return packet.Length;
            }
            finally
            {
                if (packet.IsCreated)
                {
                    packet.Dispose();
                }

                bodyPose.Dispose();
                facePose.Dispose();
            }
        }
        finally
        {
            DestroyHandle(handle);
        }
    }

    private static void ValidateSnapshotRoundTrip(
        string config,
        Unity.Collections.NativeArray<byte> packet,
        int bodyCount,
        int faceCount,
        List<int> trackedFaceIndices,
        string prefabPath)
    {
        if (!CreateOrUpdateHandle(config, out ulong receiverHandle))
        {
            throw new InvalidOperationException(
                $"Could not create the receiver handle for {prefabPath}."
            );
        }

        var receivedBody =
            new Unity.Collections.NativeArray<NativeTransform>(
                bodyCount,
                Unity.Collections.Allocator.Temp,
                Unity.Collections.NativeArrayOptions.ClearMemory
            );
        var receivedFace = new Unity.Collections.NativeArray<float>(
            faceCount,
            Unity.Collections.Allocator.Temp,
            Unity.Collections.NativeArrayOptions.ClearMemory
        );

        try
        {
            if (!DeserializeSkeletonAndFace(
                    receiverHandle,
                    packet,
                    SERIALIZATION_VERSION_CURRENT,
                    out _,
                    out _,
                    out _,
                    ref receivedBody,
                    ref receivedFace))
            {
                throw new InvalidOperationException(
                    $"Receiver deserialization failed for {prefabPath}."
                );
            }

            foreach (int trackedIndex in trackedFaceIndices)
            {
                if (receivedFace[trackedIndex] <= 0.01f)
                {
                    throw new InvalidOperationException(
                        $"Face shape {trackedIndex} did not survive the " +
                        $"native send/receive round trip for {prefabPath}."
                    );
                }
            }
        }
        finally
        {
            receivedBody.Dispose();
            receivedFace.Dispose();
            DestroyHandle(receiverHandle);
        }
    }

    private static int[] CreateSequentialIndices(int count)
    {
        var indices = new int[count];

        for (int i = 0; i < count; i++)
        {
            indices[i] = i;
        }

        return indices;
    }

}
#endif
