using UnityEngine;

public class VideoDisplayScreen : MonoBehaviour
{
    [Header("Display Target")]
    [SerializeField] private Renderer targetRenderer;

    private Material runtimeMaterial;

    private void Awake()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
        }

        if (targetRenderer != null)
        {
            runtimeMaterial = targetRenderer.material;
        }
    }

    public void SetTexture(Texture texture)
    {
        if (runtimeMaterial == null)
        {
            Debug.LogWarning("VideoDisplayScreen: No material found.");
            return;
        }

        runtimeMaterial.mainTexture = texture;
    }

    public void ClearTexture()
    {
        if (runtimeMaterial != null)
        {
            runtimeMaterial.mainTexture = null;
        }
    }
}