using UnityEngine;

public class PlanetVisual : MonoBehaviour
{

    public ClusterNode Node { get; private set; }

    private Renderer planetRenderer;
    private Material instanceMaterial;

    public void Initialize(ClusterNode node, float radius, Color color)
    {
        Node = node;


        gameObject.name = $"Planet_{node.NodeId}";


        float diameter = radius * 2f;
        transform.localScale = new Vector3(diameter, diameter, diameter);


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

    public void SetTransparent(bool transparent)
    {
        if (instanceMaterial == null) return;

        Color c = instanceMaterial.color;

        if (transparent)
        {

            instanceMaterial.SetFloat("_Mode", 3f);
            instanceMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            instanceMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            instanceMaterial.SetInt("_ZWrite", 0);
            instanceMaterial.DisableKeyword("_ALPHATEST_ON");
            instanceMaterial.EnableKeyword("_ALPHABLEND_ON");
            instanceMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            instanceMaterial.renderQueue = 3000;
            c.a = 1.0f;
        }
        else
        {

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

    public void SetAlpha(float alpha)
    {
        if (instanceMaterial == null) return;

        Color c = instanceMaterial.color;
        c.a = Mathf.Clamp01(alpha);
        instanceMaterial.color = c;
    }

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
