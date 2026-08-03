using System;
using UnityEngine;

[DisallowMultipleComponent]
public class VideoDisplayScreen : MonoBehaviour
{
    [Header("Display Target")]
    [SerializeField] private Renderer targetRenderer;

    [Header("Screen Identity")]
    [Tooltip("Optional stable id. When empty, the hierarchy path is used.")]
    [SerializeField] private string screenId;
    [Tooltip("Optional name shown in the Performer camera menu.")]
    [SerializeField] private string displayName;

    [Header("Runtime Selection")]
    [SerializeField] private string selectedStreamId;

    private Material runtimeMaterial;
    private Texture currentTexture;

    public static event Action RegistryChanged;

    public string ScreenId =>
        string.IsNullOrWhiteSpace(screenId) ? BuildHierarchyPath(transform) : screenId.Trim();

    public string DisplayName =>
        string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName.Trim();

    public string SelectedStreamId => selectedStreamId ?? "";

    public Texture CurrentTexture => currentTexture;

    private void Awake()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
        }

        if (targetRenderer == null)
        {
            Debug.LogWarning("VideoDisplayScreen: Target Renderer is missing on " + gameObject.name);
            return;
        }

        runtimeMaterial = targetRenderer.material;

        if (runtimeMaterial == null)
        {
            Debug.LogWarning("VideoDisplayScreen: Runtime material is missing on " + gameObject.name);
            return;
        }

        Debug.Log("VideoDisplayScreen: Ready. Renderer = " + targetRenderer.name);
    }

    private void OnEnable()
    {
        RegistryChanged?.Invoke();
    }

    private void OnDisable()
    {
        RegistryChanged?.Invoke();
    }

    public void SelectStream(string streamId)
    {
        selectedStreamId = streamId ?? "";
    }

    public void SetTexture(Texture texture)
    {
        if (runtimeMaterial == null)
        {
            Debug.LogWarning("VideoDisplayScreen: No material found.");
            return;
        }

        if (texture == null)
        {
            Debug.LogWarning("VideoDisplayScreen: SetTexture received null texture.");
            return;
        }

        if (currentTexture == texture)
            return;

        currentTexture = texture;
        runtimeMaterial.mainTexture = texture;

        Debug.Log(
            "VideoDisplayScreen: Texture applied. " +
            "Texture = " + texture.name +
            ", Size = " + texture.width + "x" + texture.height
        );
    }

    public void ClearTexture()
    {
        if (runtimeMaterial != null)
        {
            currentTexture = null;
            runtimeMaterial.mainTexture = null;
            Debug.Log("VideoDisplayScreen: Texture cleared.");
        }
    }

    private static string BuildHierarchyPath(Transform target)
    {
        string path = target.name + "[" + target.GetSiblingIndex() + "]";
        Transform parent = target.parent;

        while (parent != null)
        {
            path = parent.name + "[" + parent.GetSiblingIndex() + "]/" + path;
            parent = parent.parent;
        }

        return path;
    }
}
