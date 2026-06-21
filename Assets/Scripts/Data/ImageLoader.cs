using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;

public class ImageLoader : MonoBehaviour
{

    public static ImageLoader Instance { get; private set; }


    private Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>();

    void Awake()
    {

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void LoadImage(string fileName, System.Action<Texture2D> onLoaded)
    {

        if (textureCache.TryGetValue(fileName, out Texture2D cached))
        {
            onLoaded?.Invoke(cached);
            return;
        }

        StartCoroutine(LoadImageCoroutine(fileName, onLoaded));
    }

    public void LoadImages(List<ImageItem> images,
                           System.Action<ImageItem, Texture2D> onEachLoaded,
                           System.Action onAllDone = null)
    {
        StartCoroutine(LoadImagesCoroutine(images, onEachLoaded, onAllDone));
    }

    private IEnumerator LoadImageCoroutine(string fileName, System.Action<Texture2D> onLoaded)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Images", fileName);


        string url = "file://" + path;

        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                textureCache[fileName] = texture;
                onLoaded?.Invoke(texture);
            }
            else
            {
                Debug.LogWarning($"[ImageLoader] Failed to load \"{fileName}\": {request.error}");
                onLoaded?.Invoke(null);
            }
        }
    }

    private IEnumerator LoadImagesCoroutine(List<ImageItem> images,
                                             System.Action<ImageItem, Texture2D> onEachLoaded,
                                             System.Action onAllDone)
    {
        foreach (var image in images)
        {

            if (textureCache.TryGetValue(image.ImageFileName, out Texture2D cached))
            {
                onEachLoaded?.Invoke(image, cached);
                continue;
            }

            string path = Path.Combine(Application.streamingAssetsPath, "Images", image.ImageFileName);
            string url = "file://" + path;

            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    textureCache[image.ImageFileName] = texture;
                    onEachLoaded?.Invoke(image, texture);
                }
                else
                {
                    Debug.LogWarning($"[ImageLoader] Failed to load \"{image.ImageFileName}\": {request.error}");
                    onEachLoaded?.Invoke(image, null);
                }
            }
        }

        onAllDone?.Invoke();
    }


    public void ClearCache()
    {
        foreach (var tex in textureCache.Values)
        {
            if (tex != null) Destroy(tex);
        }
        textureCache.Clear();
    }

    void OnDestroy()
    {
        ClearCache();
    }
}
