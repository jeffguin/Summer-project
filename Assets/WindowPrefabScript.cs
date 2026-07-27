using UnityEngine;

public class WindowPrefabScript : MonoBehaviour
{
    public void SetSize(float width, float height)
    {
        transform.localScale = new Vector3(
            width,
            height,
            1f
        );
    }
}