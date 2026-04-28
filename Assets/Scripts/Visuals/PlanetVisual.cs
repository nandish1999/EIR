using UnityEngine;

/// <summary>
/// Attached to each runtime-spawned planet sphere.
/// Stores the reference to its ClusterNode data and configures
/// the visual appearance (scale, color).
///
/// All spheres at every depth are opaque solid color.
/// Color is determined by the planet family + depth tint.
/// </summary>
public class PlanetVisual : MonoBehaviour
{
    // -----------------------------------------------------------
    // Data reference (set by VisualizationManager at spawn time)
    // -----------------------------------------------------------

    /// <summary>The cluster node this planet represents.</summary>
    public ClusterNode Node { get; private set; }

    // -----------------------------------------------------------
    // Visual configuration
    // -----------------------------------------------------------

    private Renderer planetRenderer;
    private Material instanceMaterial;

    /// <summary>
    /// Initializes this planet visual with its data and appearance.
    /// Called by VisualizationManager immediately after instantiation.
    /// </summary>
    public void Initialize(ClusterNode node, float radius, Color color)
    {
        Node = node;

        // --- Name the GameObject for easy identification in Hierarchy ---
        gameObject.name = $"Planet_{node.NodeId}";

        // --- Scale: uniform sphere, diameter = 2 * radius ---
        float diameter = radius * 2f;
        transform.localScale = new Vector3(diameter, diameter, diameter);

        // --- Color: opaque solid for all nodes at every depth ---
        planetRenderer = GetComponent<Renderer>();
        if (planetRenderer != null)
        {
            instanceMaterial = new Material(planetRenderer.sharedMaterial);
            instanceMaterial.color = color;
            planetRenderer.material = instanceMaterial;
        }

        Debug.Log($"[PlanetVisual] Initialized: {node.NodeId} | " +
                  $"size={node.Size} | radius={radius:F2} | " +
                  $"pos={node.Position} | depth={node.Depth}");
    }

    // -----------------------------------------------------------
    // Transparency and visibility control
    // -----------------------------------------------------------

    /// <summary>
    /// Switches material between transparent rendering mode and opaque mode.
    /// When transparent: alpha starts at 1.0 (fully opaque appearance but in
    /// transparent shader mode, ready for gradual fade via SetAlpha).
    /// When opaque: alpha 1.0, opaque render mode.
    /// Assumes Unity Standard shader on the planet prefab material.
    /// </summary>
    public void SetTransparent(bool transparent)
    {
        if (instanceMaterial == null) return;

        Color c = instanceMaterial.color;

        if (transparent)
        {
            // Switch to Transparent rendering mode
            instanceMaterial.SetFloat("_Mode", 3f);
            instanceMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            instanceMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            instanceMaterial.SetInt("_ZWrite", 0);
            instanceMaterial.DisableKeyword("_ALPHATEST_ON");
            instanceMaterial.EnableKeyword("_ALPHABLEND_ON");
            instanceMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            instanceMaterial.renderQueue = 3000;
            c.a = 1.0f; // Start fully opaque; fade coroutine will drive alpha down
        }
        else
        {
            // Switch back to Opaque rendering mode
            instanceMaterial.SetFloat("_Mode", 0f);
            instanceMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            instanceMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            instanceMaterial.SetInt("_ZWrite", 1);
            instanceMaterial.DisableKeyword("_ALPHABLEND_ON");
            instanceMaterial.renderQueue = -1;
            c.a = 1.0f;
        }

        instanceMaterial.color = c;
    }

    /// <summary>
    /// Sets the material alpha to an arbitrary value (0.0–1.0).
    /// The material must already be in transparent rendering mode
    /// (call SetTransparent(true) first). Used by the fade coroutine
    /// to gradually reduce alpha each frame.
    /// </summary>
    public void SetAlpha(float alpha)
    {
        if (instanceMaterial == null) return;

        Color c = instanceMaterial.color;
        c.a = Mathf.Clamp01(alpha);
        instanceMaterial.color = c;
    }

    /// <summary>
    /// Enables or disables the planet's Renderer component.
    /// When disabled, the sphere is completely invisible (not drawn at all).
    /// Used at the end of a fade-out to fully hide the parent sphere.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (planetRenderer != null)
            planetRenderer.enabled = visible;
    }

    void OnDestroy()
    {
        if (instanceMaterial != null)
        {
            Destroy(instanceMaterial);
        }
    }
}
