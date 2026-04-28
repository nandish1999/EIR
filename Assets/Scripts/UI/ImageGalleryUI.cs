using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays landscape images for a leaf node in a scrollable UI grid overlay.
/// 
/// This script builds the entire UI programmatically at runtime — no manual
/// Canvas/Panel setup is needed. Just attach it to the Managers GameObject.
/// </summary>
public class ImageGalleryUI : MonoBehaviour
{
    // -----------------------------------------------------------
    // Configuration
    // -----------------------------------------------------------

    [Header("Gallery Settings")]
    [Tooltip("Width/height of each thumbnail in the grid (pixels).")]
    public int thumbnailSize = 120;

    [Tooltip("Spacing between thumbnails (pixels).")]
    public int thumbnailSpacing = 8;

    [Tooltip("Padding inside the gallery panel (pixels).")]
    public int panelPadding = 16;

    // -----------------------------------------------------------
    // Runtime state
    // -----------------------------------------------------------

    /// <summary>True when the gallery panel is visible.</summary>
    public bool IsOpen { get; private set; }

    private Canvas canvas;
    private GameObject panelObj;
    private GameObject headerObj;
    private Text headerText;
    private GameObject scrollViewObj;
    private Transform contentTransform;
    private List<GameObject> thumbnailObjects = new List<GameObject>();

    // -----------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------

    void Awake()
    {
        BuildUI();
        HideGallery();
    }

    // -----------------------------------------------------------
    // Public API
    // -----------------------------------------------------------

    /// <summary>
    /// Opens the gallery and displays images for the given leaf node.
    /// </summary>
    public void ShowGallery(ClusterNode leafNode)
    {
        if (leafNode == null) return;

        // Clear previous thumbnails
        ClearThumbnails();

        // Update header
        string imageCountText = leafNode.HasImages
            ? $"{leafNode.ActualImageCount} images"
            : "No images (pruned)";
        headerText.text = $"{leafNode.NodeId}  —  {imageCountText}";

        // Show the panel
        panelObj.SetActive(true);
        IsOpen = true;

        // Load and display images
        if (leafNode.HasImages && ImageLoader.Instance != null)
        {
            Debug.Log($"[ImageGalleryUI] Loading {leafNode.ActualImageCount} images " +
                      $"for \"{leafNode.NodeId}\"...");

            ImageLoader.Instance.LoadImages(
                leafNode.Images,
                onEachLoaded: (imageItem, texture) =>
                {
                    AddThumbnail(imageItem, texture);
                },
                onAllDone: () =>
                {
                    Debug.Log($"[ImageGalleryUI] ✅ All images loaded for \"{leafNode.NodeId}\".");
                }
            );
        }
        else if (!leafNode.HasImages)
        {
            Debug.Log($"[ImageGalleryUI] Pruned leaf \"{leafNode.NodeId}\" — no images to display.");
        }
        else
        {
            Debug.LogWarning("[ImageGalleryUI] ImageLoader instance not found!");
        }
    }

    /// <summary>Hides the gallery panel.</summary>
    public void HideGallery()
    {
        if (panelObj != null)
            panelObj.SetActive(false);
        IsOpen = false;
        ClearThumbnails();
    }

    // -----------------------------------------------------------
    // UI construction (all done programmatically)
    // -----------------------------------------------------------

    private void BuildUI()
    {
        // --- Canvas ---
        var canvasObj = new GameObject("GalleryCanvas");
        canvasObj.transform.SetParent(transform);
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // on top of everything
        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObj.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();

        // --- Dark semi-transparent background panel ---
        panelObj = new GameObject("GalleryPanel");
        panelObj.transform.SetParent(canvasObj.transform, false);

        var panelRect = panelObj.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.15f, 0.05f);
        panelRect.anchorMax = new Vector2(0.85f, 0.95f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        var panelImage = panelObj.AddComponent<Image>();
        panelImage.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);

        // --- Header bar ---
        headerObj = new GameObject("Header");
        headerObj.transform.SetParent(panelObj.transform, false);

        var headerRect = headerObj.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1);
        headerRect.sizeDelta = new Vector2(0, 50);
        headerRect.anchoredPosition = Vector2.zero;

        var headerBg = headerObj.AddComponent<Image>();
        headerBg.color = new Color(0.15f, 0.15f, 0.18f, 1f);

        // Header text
        var headerTextObj = new GameObject("HeaderText");
        headerTextObj.transform.SetParent(headerObj.transform, false);

        var htRect = headerTextObj.AddComponent<RectTransform>();
        htRect.anchorMin = Vector2.zero;
        htRect.anchorMax = Vector2.one;
        htRect.offsetMin = new Vector2(panelPadding, 0);
        htRect.offsetMax = new Vector2(-50, 0);

        headerText = headerTextObj.AddComponent<Text>();
        headerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        headerText.fontSize = 20;
        headerText.color = Color.white;
        headerText.alignment = TextAnchor.MiddleLeft;
        headerText.text = "Gallery";

        // --- Close button (X) ---
        var closeBtnObj = new GameObject("CloseButton");
        closeBtnObj.transform.SetParent(headerObj.transform, false);

        var closeRect = closeBtnObj.AddComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(1, 0);
        closeRect.anchorMax = new Vector2(1, 1);
        closeRect.pivot = new Vector2(1, 0.5f);
        closeRect.sizeDelta = new Vector2(50, 0);
        closeRect.anchoredPosition = Vector2.zero;

        var closeBtnImage = closeBtnObj.AddComponent<Image>();
        closeBtnImage.color = new Color(0.8f, 0.2f, 0.2f, 1f);

        var closeBtn = closeBtnObj.AddComponent<Button>();
        closeBtn.onClick.AddListener(HideGallery);

        var closeTxtObj = new GameObject("CloseText");
        closeTxtObj.transform.SetParent(closeBtnObj.transform, false);

        var ctRect = closeTxtObj.AddComponent<RectTransform>();
        ctRect.anchorMin = Vector2.zero;
        ctRect.anchorMax = Vector2.one;
        ctRect.offsetMin = Vector2.zero;
        ctRect.offsetMax = Vector2.zero;

        var closeTxt = closeTxtObj.AddComponent<Text>();
        closeTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        closeTxt.fontSize = 24;
        closeTxt.color = Color.white;
        closeTxt.alignment = TextAnchor.MiddleCenter;
        closeTxt.text = "✕";

        // --- Scroll View ---
        scrollViewObj = new GameObject("ScrollView");
        scrollViewObj.transform.SetParent(panelObj.transform, false);

        var svRect = scrollViewObj.AddComponent<RectTransform>();
        svRect.anchorMin = new Vector2(0, 0);
        svRect.anchorMax = new Vector2(1, 1);
        svRect.offsetMin = new Vector2(panelPadding, panelPadding);
        svRect.offsetMax = new Vector2(-panelPadding, -50 - panelPadding);

        var scrollView = scrollViewObj.AddComponent<ScrollRect>();
        scrollView.horizontal = false;
        scrollView.vertical = true;
        scrollView.movementType = ScrollRect.MovementType.Clamped;

        var svImage = scrollViewObj.AddComponent<Image>();
        svImage.color = new Color(0, 0, 0, 0.01f);
        scrollViewObj.AddComponent<Mask>().showMaskGraphic = false;

        // --- Content container ---
        var contentObj = new GameObject("Content");
        contentObj.transform.SetParent(scrollViewObj.transform, false);
        contentTransform = contentObj.transform;

        var contentRect = contentObj.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.anchoredPosition = Vector2.zero;

        var grid = contentObj.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(thumbnailSize, thumbnailSize);
        grid.spacing = new Vector2(thumbnailSpacing, thumbnailSpacing);
        grid.constraint = GridLayoutGroup.Constraint.Flexible;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.padding = new RectOffset(0, 0, 0, 0);

        var fitter = contentObj.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollView.content = contentRect;
        scrollView.viewport = svRect;
    }

    // -----------------------------------------------------------
    // Thumbnail management
    // -----------------------------------------------------------

    private void AddThumbnail(ImageItem imageItem, Texture2D texture)
    {
        var thumbObj = new GameObject($"Thumb_{imageItem.ImageFileName}");
        thumbObj.transform.SetParent(contentTransform, false);

        var rawImage = thumbObj.AddComponent<RawImage>();

        if (texture != null)
        {
            rawImage.texture = texture;
        }
        else
        {
            rawImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);
        }

        thumbnailObjects.Add(thumbObj);
    }

    private void ClearThumbnails()
    {
        foreach (var obj in thumbnailObjects)
        {
            if (obj != null) Destroy(obj);
        }
        thumbnailObjects.Clear();

        if (scrollViewObj != null)
        {
            var scrollRect = scrollViewObj.GetComponent<ScrollRect>();
            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;
        }
    }
}
