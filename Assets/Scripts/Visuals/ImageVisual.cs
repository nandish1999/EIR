using UnityEngine;

public class ImageVisual : MonoBehaviour
{

    public ImageItem ImageData { get; private set; }


    private Material instanceMaterial;

    public void Initialize(ImageItem imageItem, float quadSize)
    {
        ImageData = imageItem;


        gameObject.name = $"Image_{imageItem.ImageFileName}";


        transform.localScale = new Vector3(quadSize, quadSize, 1f);


        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            instanceMaterial = new Material(Shader.Find("Unlit/Texture"));
            instanceMaterial.color = new Color(0.25f, 0.25f, 0.28f, 1f);
            renderer.material = instanceMaterial;
        }


        Collider col = GetComponent<Collider>();
        if (col != null) Destroy(col);
    }

    public void ApplyTexture(Texture2D texture)
    {
        if (texture == null || instanceMaterial == null) return;

        instanceMaterial.mainTexture = texture;
        instanceMaterial.color = Color.white;


        float aspect = (float)texture.width / texture.height;
        Vector3 scale = transform.localScale;
        transform.localScale = new Vector3(scale.y * aspect, scale.y, scale.z);
    }

    public void FaceCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;


        transform.LookAt(cam.transform);
        transform.Rotate(0, 180f, 0);
    }

    void LateUpdate()
    {

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
