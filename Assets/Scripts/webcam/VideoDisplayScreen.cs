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
            runtimeMaterial.mainTexture = null;
            Debug.Log("VideoDisplayScreen: Texture cleared.");
        }
    }
}