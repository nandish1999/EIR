using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Loads landscape images from StreamingAssets/Images/ at runtime.
/// Uses UnityWebRequestTexture for cross-platform compatibility.
/// Includes a simple in-memory cache to avoid reloading the same image.
/// </summary>
public class ImageLoader : MonoBehaviour
{
    /// <summary>Singleton instance for easy access.</summary>
    public static ImageLoader Instance { get; private set; }

    /// <summary>Cache of already-loaded textures, keyed by filename.</summary>
    private Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>();

    void Awake()
    {
        // Simple singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Loads a single image by filename. Calls onLoaded with the texture
    /// when done (or null if the file doesn't exist).
    /// </summary>
    /// <param name="fileName">Image filename, e.g. "10720.jpg"</param>
    /// <param name="onLoaded">Callback with the loaded Texture2D (or null on failure).</param>
    public void LoadImage(string fileName, System.Action<Texture2D> onLoaded)
    {
        // Check cache first
        if (textureCache.TryGetValue(fileName, out Texture2D cached))
        {
            onLoaded?.Invoke(cached);
            return;
        }

        StartCoroutine(LoadImageCoroutine(fileName, onLoaded));
    }

    /// <summary>
    /// Loads multiple images sequentially, calling onEachLoaded for each.
    /// Calls onAllDone when all are complete.
    /// </summary>
    public void LoadImages(List<ImageItem> images,
                           System.Action<ImageItem, Texture2D> onEachLoaded,
                           System.Action onAllDone = null)
    {
        StartCoroutine(LoadImagesCoroutine(images, onEachLoaded, onAllDone));
    }

    private IEnumerator LoadImageCoroutine(string fileName, System.Action<Texture2D> onLoaded)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Images", fileName);

        // Use file:// protocol for local files
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
            // Check cache
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

    /// <summary>Clears the texture cache and frees memory.</summary>
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
