#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps every already-grabbable prefab in the Actor BasicSpawner list usable
/// by either tracked hand. HandGrabPose lists are scale interpolation keys in
/// Interaction SDK, not a place to combine a left pose and a right pose.
/// Removing the pose constraint preserves the existing Grabbable/controller/
/// networking setup while making the hand interactable handedness-neutral.
/// </summary>
public static class SpawnListDualHandGrabRepair
{
    private const string ActorScenePath =
        "Assets/Scenes/Actor_triggerAnimTuesday.unity";

    private static readonly Regex SpawnItemRegex = new Regex(
        @"- name:\s*(?<name>[^\r\n]+)\r?\n\s*prefab:\r?\n" +
        @"\s*RawGuidValue:\s*(?<guid>[0-9a-fA-F]{32})",
        RegexOptions.Compiled
    );

    private readonly struct SpawnItem
    {
        public SpawnItem(string name, string prefabPath)
        {
            Name = name;
            PrefabPath = prefabPath;
        }

        public string Name { get; }
        public string PrefabPath { get; }
    }

    [MenuItem("Tools/Summer Project/Repair Actor Spawn List Dual-Hand Grab")]
    public static void RepairActorSpawnListDualHandGrab()
    {
        IReadOnlyList<SpawnItem> spawnItems = ReadActorSpawnItems();
        int repairedPrefabCount = 0;
        int clearedPoseListCount = 0;
        int validatedGrabbablePrefabCount = 0;

        foreach (SpawnItem item in spawnItems)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(item.PrefabPath);

            try
            {
                Grabbable[] grabbables =
                    root.GetComponentsInChildren<Grabbable>(true);
                HandGrabInteractable[] handInteractables =
                    root.GetComponentsInChildren<HandGrabInteractable>(true);

                // Board, SpawnBridge and animation/controller prefabs are in
                // the same network spawn list but are not movable props.
                if (grabbables.Length == 0 && handInteractables.Length == 0)
                {
                    Debug.Log(
                        $"SpawnListDualHandGrabRepair: skipped functional " +
                        $"spawn item '{item.Name}' ({item.PrefabPath})."
                    );
                    continue;
                }

                if (grabbables.Length == 0 || handInteractables.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Spawn item '{item.Name}' has an incomplete grab " +
                        $"setup: Grabbable={grabbables.Length}, " +
                        $"HandGrabInteractable={handInteractables.Length}. " +
                        $"Prefab: {item.PrefabPath}"
                    );
                }

                bool changed = false;

                foreach (HandGrabInteractable handInteractable in
                         handInteractables)
                {
                    SerializedObject serializedInteractable =
                        new SerializedObject(handInteractable);
                    SerializedProperty handGrabPoses =
                        serializedInteractable.FindProperty("_handGrabPoses");

                    if (handGrabPoses == null)
                    {
                        throw new InvalidOperationException(
                            "Interaction SDK field '_handGrabPoses' was not " +
                            $"found on {item.PrefabPath}."
                        );
                    }

                    if (handGrabPoses.arraySize == 0)
                    {
                        continue;
                    }

                    handGrabPoses.ClearArray();
                    serializedInteractable.ApplyModifiedPropertiesWithoutUndo();
                    clearedPoseListCount++;
                    changed = true;
                }

                ValidatePrefab(item, root, requireGrabbable: true);
                validatedGrabbablePrefabCount++;

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, item.PrefabPath);
                    repairedPrefabCount++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "SpawnListDualHandGrabRepair: completed. " +
            $"SpawnItems={spawnItems.Count}, " +
            $"GrabbablePrefabs={validatedGrabbablePrefabCount}, " +
            $"ChangedPrefabs={repairedPrefabCount}, " +
            $"ClearedPoseLists={clearedPoseListCount}."
        );
    }

    [MenuItem("Tools/Summer Project/Validate Actor Spawn List Dual-Hand Grab")]
    public static void ValidateActorSpawnListDualHandGrab()
    {
        IReadOnlyList<SpawnItem> spawnItems = ReadActorSpawnItems();
        int validatedGrabbablePrefabCount = 0;
        int skippedFunctionalPrefabCount = 0;

        foreach (SpawnItem item in spawnItems)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(item.PrefabPath);

            try
            {
                Grabbable[] grabbables =
                    root.GetComponentsInChildren<Grabbable>(true);
                HandGrabInteractable[] handInteractables =
                    root.GetComponentsInChildren<HandGrabInteractable>(true);

                if (grabbables.Length == 0 && handInteractables.Length == 0)
                {
                    skippedFunctionalPrefabCount++;
                    continue;
                }

                ValidatePrefab(item, root, requireGrabbable: true);
                validatedGrabbablePrefabCount++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        Debug.Log(
            "SpawnListDualHandGrabRepair: validation passed. " +
            $"SpawnItems={spawnItems.Count}, " +
            $"DualHandGrabbablePrefabs={validatedGrabbablePrefabCount}, " +
            $"FunctionalPrefabsSkipped={skippedFunctionalPrefabCount}."
        );
    }

    // Headless entry points used by CI and command-line verification.
    public static void RepairBatch()
    {
        RunBatch(RepairActorSpawnListDualHandGrab);
    }

    public static void ValidateBatch()
    {
        RunBatch(ValidateActorSpawnListDualHandGrab);
    }

    private static void ValidatePrefab(
        SpawnItem item,
        GameObject root,
        bool requireGrabbable)
    {
        Grabbable[] grabbables =
            root.GetComponentsInChildren<Grabbable>(true);
        HandGrabInteractable[] handInteractables =
            root.GetComponentsInChildren<HandGrabInteractable>(true);

        if (requireGrabbable && grabbables.Length == 0)
        {
            throw new InvalidOperationException(
                $"Spawn item '{item.Name}' has HandGrabInteractable but no " +
                $"Grabbable. Prefab: {item.PrefabPath}"
            );
        }

        int activeDualHandInteractableCount = 0;

        foreach (HandGrabInteractable handInteractable in handInteractables)
        {
            SerializedObject serializedInteractable =
                new SerializedObject(handInteractable);
            SerializedProperty handGrabPoses =
                serializedInteractable.FindProperty("_handGrabPoses");

            if (handGrabPoses == null || handGrabPoses.arraySize != 0)
            {
                throw new InvalidOperationException(
                    $"Spawn item '{item.Name}' still has a handed or malformed " +
                    $"HandGrabPose constraint on '{GetHierarchyPath(handInteractable.transform)}'. " +
                    $"Prefab: {item.PrefabPath}"
                );
            }

            if (handInteractable.enabled &&
                handInteractable.gameObject.activeInHierarchy)
            {
                activeDualHandInteractableCount++;
            }
        }

        if (activeDualHandInteractableCount == 0)
        {
            throw new InvalidOperationException(
                $"Spawn item '{item.Name}' has no active, handedness-neutral " +
                $"HandGrabInteractable. Prefab: {item.PrefabPath}"
            );
        }
    }

    private static IReadOnlyList<SpawnItem> ReadActorSpawnItems()
    {
        if (!File.Exists(ActorScenePath))
        {
            throw new FileNotFoundException(
                "Actor scene was not found.",
                ActorScenePath
            );
        }

        string sceneYaml = File.ReadAllText(ActorScenePath);
        int listStart = sceneYaml.IndexOf(
            "  _networkInteractableSpawnItems:",
            StringComparison.Ordinal
        );

        if (listStart < 0)
        {
            throw new InvalidOperationException(
                $"BasicSpawner spawn list was not found in {ActorScenePath}."
            );
        }

        int listEnd = sceneYaml.IndexOf(
            "\n--- !u!",
            listStart,
            StringComparison.Ordinal
        );
        string spawnListYaml = listEnd >= 0
            ? sceneYaml.Substring(listStart, listEnd - listStart)
            : sceneYaml.Substring(listStart);

        MatchCollection matches = SpawnItemRegex.Matches(spawnListYaml);
        var items = new List<SpawnItem>(matches.Count);
        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
            string itemName = match.Groups["name"].Value.Trim();
            string guid = match.Groups["guid"].Value;
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);

            if (string.IsNullOrEmpty(prefabPath))
            {
                throw new InvalidOperationException(
                    $"Spawn item '{itemName}' references missing GUID {guid}."
                );
            }

            if (!prefabPath.EndsWith(
                    ".prefab",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Spawn item '{itemName}' does not reference a prefab: " +
                    prefabPath
                );
            }

            if (!uniquePaths.Add(prefabPath))
            {
                throw new InvalidOperationException(
                    $"Duplicate spawn prefab path found: {prefabPath}."
                );
            }

            items.Add(new SpawnItem(itemName, prefabPath));
        }

        if (items.Count == 0)
        {
            throw new InvalidOperationException(
                $"No spawn items were parsed from {ActorScenePath}."
            );
        }

        return items;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;

        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }

        return path;
    }

    private static void RunBatch(Action action)
    {
        try
        {
            action();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }
}
#endif
