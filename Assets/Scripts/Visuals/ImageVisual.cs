using UnityEngine;

/// <summary>
/// Attached to each runtime-spawned image quad.
/// Stores a reference to its ImageItem data and manages the material.
/// </summary>
public class ImageVisual : MonoBehaviour
{
    // -----------------------------------------------------------
    // Data reference (set at spawn time)
    // -----------------------------------------------------------

    /// <summary>The image item this quad represents.</summary>
    public ImageItem ImageData { get; private set; }

    /// <summary>The material instance for this quad.</summary>
    private Material instanceMaterial;

    /// <summary>
    /// Initializes this image visual with placeholder appearance.
    /// Called by VisualizationManager immediately after instantiation.
    /// </summary>
    /// <param name="imageItem">The ImageItem this quad represents.</param>
    /// <param name="quadSize">The world-space size of the quad.</param>
    public void Initialize(ImageItem imageItem, float quadSize)
    {
        ImageData = imageItem;

        // Name for easy identification in Hierarchy
        gameObject.name = $"Image_{imageItem.ImageFileName}";

        // Scale the quad
        transform.localScale = new Vector3(quadSize, quadSize, 1f);

        // Create a material instance with a placeholder color
        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            instanceMaterial = new Material(Shader.Find("Unlit/Texture"));
            instanceMaterial.color = new Color(0.25f, 0.25f, 0.28f, 1f); // dark grey placeholder
            renderer.material = instanceMaterial;
        }

        // Remove the default collider — we don't need to click individual images
        Collider col = GetComponent<Collider>();
        if (col != null) Destroy(col);
    }

    /// <summary>
    /// Applies a loaded texture to this quad's material.
    /// Called by VisualizationManager after ImageLoader finishes loading.
    /// </summary>
    public void ApplyTexture(Texture2D texture)
    {
        if (texture == null || instanceMaterial == null) return;

        instanceMaterial.mainTexture = texture;
        instanceMaterial.color = Color.white; // remove grey tint so texture shows true color

        // Adjust aspect ratio to match the image
        float aspect = (float)texture.width / texture.height;
        Vector3 scale = transform.localScale;
        transform.localScale = new Vector3(scale.y * aspect, scale.y, scale.z);
    }

    /// <summary>
    /// Makes this quad face the camera (billboard effect).
    /// Call from Update or after spawning.
    /// </summary>
    public void FaceCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // Look at camera, then flip 180° because Quad's front faces -Z
        transform.LookAt(cam.transform);
        transform.Rotate(0, 180f, 0);
    }

    void LateUpdate()
    {
        // Continuously face the camera so images are always readable
        FaceCamera();
    }

    void OnDestroy()
    {
        if (instanceMaterial != null)
        {
            Destroy(instanceMaterial);
        }
    }
}
