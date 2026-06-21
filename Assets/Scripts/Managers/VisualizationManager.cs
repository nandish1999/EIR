using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VisualizationManager : MonoBehaviour
{

    [Header("References")]
    [Tooltip("The DataManager that provides the ClusterTree.")]
    public DataManager dataManager;

    [Tooltip("The planet sphere prefab to instantiate.")]
    public GameObject planetPrefab;

    [Tooltip("The CameraController for smooth focus transitions on expand.")]
    public CameraController cameraController;

    [Header("Scaling")]
    [Tooltip("Global multiplier for UMAP coordinates → Unity world units. " +
             "Applied uniformly to all nodes and images.")]
    public float positionScale = 1.0f;

    [Tooltip("Multiplier applied to the logarithmic size formula for sphere radius.")]
    public float radiusScale = 0.3f;

    [Tooltip("Minimum radius so that very small nodes are still visible.")]
    public float minRadius = 0.3f;

    [Header("Depth Scaling")]
    [Tooltip("Radius multiplier per depth level. Children appear smaller than parents. " +
             "Value of 0.65 means each depth level is 65% the size of the previous.")]
    [Range(0.3f, 1.0f)]
    public float depthRadiusFactor = 0.65f;

    [Tooltip("Minimum effective radius after depth scaling, so deep nodes stay visible.")]
    public float minEffectiveRadius = 0.1f;


    [Header("Global Anchor Placement")]
    [Tooltip("Uniform scale applied to UMAP offsets from planet center. " +
             "Higher values spread entities further apart for better readability. " +
             "Does not affect neighbour relationships (only applies a uniform scale).")]
    [Range(1f, 10f)]
    public float planetScale = 5.0f;

    [Header("Overlap Resolution")]
    [Tooltip("Sibling radius cap factor: max_radius = capFactor × avg_NN/2. " +
             "Lower = more aggressive shrinking in dense groups. 0 = disabled.")]
    [Range(0f, 2f)]
    public float siblingRadiusCapFactor = 0.6f;

    [Header("Image Display")]
    [Tooltip("Maximum size of each image quad in world units. " +
             "Actual size is adaptive based on image count and expansion radius.")]
    public float imageQuadSize = 0.3f;

    [Tooltip("Minimum image quad size so images don't become invisible.")]
    public float minImageQuadSize = 0.05f;

    [Header("Planet Colors")]
    [Tooltip("Colors assigned to each planet by index.")]
    public Color[] planetColors = new Color[]
    {
        new Color(0.29f, 0.56f, 0.85f, 1f),
        new Color(0.85f, 0.44f, 0.29f, 1f),
        new Color(0.29f, 0.78f, 0.47f, 1f),
    };

    [Header("Depth Colors")]
    [Tooltip("If true, darken/lighten the planet color based on depth.")]
    public bool tintByDepth = true;

    private Transform objectContainer;

    private Stack<ClusterNode> expansionHistory = new Stack<ClusterNode>();

    private List<GameObject> planetObjects = new List<GameObject>();

    private Dictionary<ClusterNode, GameObject> nodeSphereMap = new Dictionary<ClusterNode, GameObject>();

    private Dictionary<ClusterNode, Vector3> nodeWorldPositionMap = new Dictionary<ClusterNode, Vector3>();

    private Dictionary<ClusterNode, List<GameObject>> nodeConnectionLines = new Dictionary<ClusterNode, List<GameObject>>();

    private const float FadeOutDuration = 3f;

    private Dictionary<ClusterNode, Coroutine> activeFadeCoroutines = new Dictionary<ClusterNode, Coroutine>();

    private bool ghostPlanetOverlayActive = false;


    private bool ghostLineOverlayActive = false;


    private List<GameObject> ghostSphereObjects = new List<GameObject>();


    private List<GameObject> ghostLineObjects = new List<GameObject>();

    private List<ClusterNode> ghostedNodes = new List<ClusterNode>();

    void Start()
    {

        if (dataManager == null)
        {
            Debug.LogError("[VisualizationManager] DataManager reference is not assigned!");
            return;
        }
        if (planetPrefab == null)
        {
            Debug.LogError("[VisualizationManager] PlanetPrefab reference is not assigned!");
            return;
        }
        if (!dataManager.IsReady)
        {
            Debug.LogError("[VisualizationManager] DataManager is not ready.");
            return;
        }


        var containerObj = new GameObject("ObjectContainer");
        objectContainer = containerObj.transform;


        SpawnPlanets();


        FrameInitialView();
    }

    public void ToggleNode(ClusterNode node)
    {
        if (node == null) return;

        if (node.IsExpanded)
        {
            CollapseNode(node);
        }
        else
        {
            ExpandNode(node);
        }
    }

    public bool CollapseLastExpanded()
    {
        while (expansionHistory.Count > 0)
        {
            ClusterNode node = expansionHistory.Pop();
            if (node.IsExpanded)
            {
                CollapseNode(node);
                return true;
            }

        }
        return false;
    }


    public bool HasExpandedNodes => expansionHistory.Count > 0;

    private void SpawnPlanets()
    {
        var planets = dataManager.Tree.Planets;

        foreach (var planet in planets)
        {
            GameObject obj = SpawnNodeSphere(planet);
            planetObjects.Add(obj);
        }

        Debug.Log($"[VisualizationManager] ✅ Spawned {planets.Count} top-level planets.");
    }

    private void ExpandNode(ClusterNode node)
    {
        ClearAllGhostOverlays();
        if (node.IsExpanded) return;

        if (node.IsLeaf)
        {

            if (!node.HasImages)
            {
                Debug.Log($"[VisualizationManager] Leaf \"{node.NodeId}\" has no images (pruned).");
                return;
            }

            SpawnImages(node);
            node.IsExpanded = true;
            expansionHistory.Push(node);


            if (nodeSphereMap.TryGetValue(node, out GameObject leafSphere))
            {
                Collider col = leafSphere.GetComponent<Collider>();
                if (col != null) col.enabled = false;
            }


            SpawnConnectionLines(node);


            StartFadeOut(node);

            Debug.Log($"[VisualizationManager] 🖼 Expanded leaf \"{node.NodeId}\" — " +
                      $"{node.ActualImageCount} images spawned at global-anchor positions.");


            FocusCameraOnExpansion(node);
        }
        else
        {

            SpawnChildren(node);
            node.IsExpanded = true;
            expansionHistory.Push(node);


            if (nodeSphereMap.TryGetValue(node, out GameObject branchSphere))
            {
                Collider col = branchSphere.GetComponent<Collider>();
                if (col != null) col.enabled = false;
            }


            SpawnConnectionLines(node);


            StartFadeOut(node);

            Debug.Log($"[VisualizationManager] 🔽 Expanded \"{node.NodeId}\" — " +
                      $"{node.ChildCount} children spawned at global-anchor positions.");


            FocusCameraOnExpansion(node);
        }
    }

    private void CollapseNode(ClusterNode node)
    {
        ClearAllGhostOverlays();
        if (!node.IsExpanded) return;


        if (!node.IsLeaf && node.Children != null)
        {
            foreach (var child in node.Children)
            {
                if (child.IsExpanded)
                {
                    CollapseNode(child);
                }
            }
        }


        foreach (var obj in node.SpawnedChildObjects)
        {
            if (obj != null)
            {

                PlanetVisual pv = obj.GetComponent<PlanetVisual>();
                if (pv != null && pv.Node != null)
                {
                    nodeSphereMap.Remove(pv.Node);
                    nodeWorldPositionMap.Remove(pv.Node);
                }
                Object.Destroy(obj);
            }
        }
        node.SpawnedChildObjects.Clear();

        node.IsExpanded = false;


        CancelFadeOut(node);


        if (cameraController != null)
            cameraController.CancelTransition();


        DestroyConnectionLines(node);


        if (nodeSphereMap.TryGetValue(node, out GameObject sphereObj) && sphereObj != null)
        {
            PlanetVisual visual = sphereObj.GetComponent<PlanetVisual>();
            if (visual != null)
            {
                visual.SetVisible(true);
                visual.SetTransparent(false);
            }
            Collider col = sphereObj.GetComponent<Collider>();
            if (col != null) col.enabled = true;
        }

        Debug.Log($"[VisualizationManager] 🔼 Collapsed \"{node.NodeId}\".");
    }

    private void SpawnChildren(ClusterNode parentNode)
    {
        var children = parentNode.Children;


        ClusterNode planetNode = GetPlanetAncestor(parentNode);
        Vector3 planetWorldPos = nodeWorldPositionMap[planetNode];
        Vector3 planetRawPos = planetNode.Position * positionScale;


        Vector3[] childPositions = new Vector3[children.Count];
        float[] uncappedRadii = new float[children.Count];

        for (int i = 0; i < children.Count; i++)
        {
            Vector3 globalOffset = children[i].Position * positionScale - planetRawPos;
            childPositions[i] = planetWorldPos + globalOffset * planetScale;

            float baseRadius = ComputeRadius(children[i].Size);
            float depthScale = Mathf.Pow(depthRadiusFactor, children[i].Depth);
            uncappedRadii[i] = Mathf.Max(baseRadius * depthScale, minEffectiveRadius);
        }


        float[] cappedRadii = ComputeSiblingCappedRadii(childPositions, uncappedRadii);


        for (int i = 0; i < children.Count; i++)
        {
            GameObject obj = SpawnNodeSphere(children[i], childPositions[i], cappedRadii[i]);
            parentNode.SpawnedChildObjects.Add(obj);
        }
    }

    private float[] ComputeSiblingCappedRadii(Vector3[] positions, float[] uncappedRadii)
    {
        int count = positions.Length;
        float[] capped = new float[count];
        System.Array.Copy(uncappedRadii, capped, count);


        if (siblingRadiusCapFactor <= 0f || count <= 1)
            return capped;


        float sumNN = 0f;
        for (int i = 0; i < count; i++)
        {
            float minDist = float.MaxValue;
            for (int j = 0; j < count; j++)
            {
                if (i == j) continue;
                float dist = Vector3.Distance(positions[i], positions[j]);
                if (dist < minDist) minDist = dist;
            }
            sumNN += minDist;
        }


        float avgNN = sumNN / count;
        float maxAllowedRadius = siblingRadiusCapFactor * avgNN / 2f;


        int cappedCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (uncappedRadii[i] > maxAllowedRadius)
            {
                capped[i] = Mathf.Max(maxAllowedRadius, minEffectiveRadius);
                cappedCount++;
            }
        }

        if (cappedCount > 0)
        {
            Debug.Log($"[VisualizationManager] Sibling cap: avgNN={avgNN:F3} " +
                      $"maxR={maxAllowedRadius:F3}, {cappedCount}/{count} children capped.");
        }

        return capped;
    }

    private GameObject SpawnNodeSphere(ClusterNode node, Vector3? worldPosition = null,
                                       float radiusOverride = -1f)
    {
        GameObject obj = Instantiate(planetPrefab, objectContainer);


        Vector3 finalPos = worldPosition ?? (node.Position * positionScale);
        obj.transform.position = finalPos;


        nodeWorldPositionMap[node] = finalPos;


        float radius;
        if (radiusOverride >= 0f)
        {
            radius = radiusOverride;
        }
        else
        {
            radius = ComputeRadius(node.Size);
            float depthScale = Mathf.Pow(depthRadiusFactor, node.Depth);
            radius = Mathf.Max(radius * depthScale, minEffectiveRadius);
        }
        Color color = GetNodeColor(node);


        PlanetVisual visual = obj.AddComponent<PlanetVisual>();
        visual.Initialize(node, radius, color);


        nodeSphereMap[node] = obj;

        return obj;
    }

    private void SpawnImages(ClusterNode leafNode)
    {
        List<ImageVisual> imageVisuals = new List<ImageVisual>();

        var images = leafNode.Images;


        ClusterNode planetNode = GetPlanetAncestor(leafNode);
        Vector3 planetWorldPos = nodeWorldPositionMap[planetNode];
        Vector3 planetRawPos = planetNode.Position * positionScale;


        Vector3[] scaledPositions = new Vector3[images.Count];
        for (int i = 0; i < images.Count; i++)
        {
            Vector3 globalOffset = images[i].Position * positionScale - planetRawPos;
            scaledPositions[i] = planetWorldPos + globalOffset * planetScale;
        }


        Vector3 imgCentroid = Vector3.zero;
        foreach (var sp in scaledPositions)
            imgCentroid += sp;
        imgCentroid /= scaledPositions.Length;

        float groupRadius = 0f;
        foreach (var sp in scaledPositions)
        {
            float dist = Vector3.Distance(sp, imgCentroid);
            if (dist > groupRadius) groupRadius = dist;
        }
        groupRadius = Mathf.Max(groupRadius, 0.1f);


        float adaptiveQuadSize = groupRadius * 0.4f / Mathf.Sqrt(images.Count);
        adaptiveQuadSize = Mathf.Clamp(adaptiveQuadSize, minImageQuadSize, imageQuadSize);


        for (int i = 0; i < images.Count; i++)
        {
            GameObject quadObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadObj.transform.SetParent(objectContainer);

            quadObj.transform.position = scaledPositions[i];


            ImageVisual visual = quadObj.AddComponent<ImageVisual>();
            visual.Initialize(images[i], adaptiveQuadSize);

            imageVisuals.Add(visual);
            leafNode.SpawnedChildObjects.Add(quadObj);
        }


        if (ImageLoader.Instance != null)
        {
            ImageLoader.Instance.LoadImages(
                leafNode.Images,
                onEachLoaded: (imageItem, texture) =>
                {
                    foreach (var iv in imageVisuals)
                    {
                        if (iv != null && iv.ImageData == imageItem)
                        {
                            iv.ApplyTexture(texture);
                            break;
                        }
                    }
                },
                onAllDone: () =>
                {
                    Debug.Log($"[VisualizationManager] ✅ All {leafNode.ActualImageCount} " +
                              $"images loaded for \"{leafNode.NodeId}\".");
                }
            );
        }
        else
        {
            Debug.LogWarning("[VisualizationManager] ImageLoader instance not found! " +
                             "Images will show as grey quads.");
        }
    }

    private void SpawnConnectionLines(ClusterNode node)
    {
        if (node.SpawnedChildObjects == null || node.SpawnedChildObjects.Count == 0) return;


        Vector3 parentPos;
        if (!nodeWorldPositionMap.TryGetValue(node, out parentPos))
            parentPos = node.Position * positionScale;

        var lines = new List<GameObject>(node.SpawnedChildObjects.Count);

        for (int i = 0; i < node.SpawnedChildObjects.Count; i++)
        {
            GameObject childObj = node.SpawnedChildObjects[i];
            if (childObj == null) continue;

            Vector3 childPos = childObj.transform.position;


            GameObject lineObj = new GameObject($"Line_{node.NodeId}_to_{i}");
            lineObj.transform.SetParent(objectContainer);

            LineRenderer lr = lineObj.AddComponent<LineRenderer>();


            lr.positionCount = 2;
            lr.SetPosition(0, parentPos);
            lr.SetPosition(1, childPos);


            lr.startWidth = 0.03f;
            lr.endWidth   = 0.03f;


            lr.material = new Material(Shader.Find("Sprites/Default"));
            Color lineColor = GetNodeColor(node);
            lineColor.a = 0.5f;
            lr.startColor = lineColor;
            lr.endColor   = lineColor;


            lr.useWorldSpace = true;

            lines.Add(lineObj);
        }

        nodeConnectionLines[node] = lines;

        Debug.Log($"[VisualizationManager] 📎 Spawned {lines.Count} connection lines for \"{node.NodeId}\".");
    }

    private void DestroyConnectionLines(ClusterNode node)
    {
        if (!nodeConnectionLines.TryGetValue(node, out var lines)) return;

        foreach (var lineObj in lines)
        {
            if (lineObj != null) Object.Destroy(lineObj);
        }

        nodeConnectionLines.Remove(node);

        Debug.Log($"[VisualizationManager] 🗑 Destroyed connection lines for \"{node.NodeId}\".");
    }

    private (Vector3 centroid, float extent) ComputeExpansionBounds(ClusterNode node)
    {

        Vector3 parentPos = nodeWorldPositionMap.TryGetValue(node, out Vector3 pp)
            ? pp
            : node.Position * positionScale;

        var positions = new List<Vector3> { parentPos };
        var radii = new List<float> { 0f };

        foreach (var childObj in node.SpawnedChildObjects)
        {
            if (childObj == null) continue;
            positions.Add(childObj.transform.position);

            radii.Add(childObj.transform.localScale.x / 2f);
        }


        Vector3 centroid = Vector3.zero;
        foreach (var pos in positions)
            centroid += pos;
        centroid /= positions.Count;


        float maxExtent = 0f;
        for (int i = 0; i < positions.Count; i++)
        {
            float dist = Vector3.Distance(positions[i], centroid) + radii[i];
            if (dist > maxExtent) maxExtent = dist;
        }


        maxExtent = Mathf.Max(maxExtent, 0.5f);

        return (centroid, maxExtent);
    }

    private void FocusCameraOnExpansion(ClusterNode node)
    {
        if (cameraController == null) return;

        var (centroid, extent) = ComputeExpansionBounds(node);
        cameraController.FocusOnRegion(centroid, extent);

        Debug.Log($"[VisualizationManager] 📷 Camera focus triggered for \"{node.NodeId}\" " +
                  $"(centroid={centroid}, extent={extent:F2}).");
    }

    private void StartFadeOut(ClusterNode node)
    {
        CancelFadeOut(node);

        Coroutine fade = StartCoroutine(FadeOutParent(node, FadeOutDuration));
        activeFadeCoroutines[node] = fade;
    }

    private void CancelFadeOut(ClusterNode node)
    {
        if (activeFadeCoroutines.TryGetValue(node, out Coroutine existing))
        {
            StopCoroutine(existing);
            activeFadeCoroutines.Remove(node);
        }
    }

    private IEnumerator FadeOutParent(ClusterNode node, float duration)
    {
        if (!nodeSphereMap.TryGetValue(node, out GameObject sphereObj)) yield break;
        PlanetVisual visual = sphereObj?.GetComponent<PlanetVisual>();
        if (visual == null) yield break;

        visual.SetTransparent(true);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float alpha = Mathf.Lerp(1f, 0f, t);
            visual.SetAlpha(alpha);
            yield return null;
        }


        visual.SetAlpha(0f);
        visual.SetVisible(false);


        DestroyConnectionLines(node);


        activeFadeCoroutines.Remove(node);

        Debug.Log($"[VisualizationManager] 👻 Parent \"{node.NodeId}\" fully faded out after {duration}s.");
    }

    private float ComputeRadius(int size)
    {
        float logSize = Mathf.Log(size + 1, 2);
        float radius = logSize * radiusScale;
        return Mathf.Max(radius, minRadius);
    }

    private ClusterNode GetPlanetAncestor(ClusterNode node)
    {
        ClusterNode current = node;
        while (current.Parent != null)
            current = current.Parent;
        return current;
    }

    private Color GetNodeColor(ClusterNode node)
    {
        return node.RepresentativeColor;
    }

    private Color GetPlanetColor(int planetIndex)
    {
        if (planetColors != null && planetIndex >= 0 && planetIndex < planetColors.Length)
            return planetColors[planetIndex];
        return Color.white;
    }

    private void FrameInitialView()
    {
        if (planetObjects.Count == 0) return;

        Camera cam = Camera.main;
        if (cam == null) return;


        Vector3 centroid = Vector3.zero;
        foreach (var obj in planetObjects)
            centroid += obj.transform.position;
        centroid /= planetObjects.Count;


        float maxDistance = 0f;
        foreach (var obj in planetObjects)
        {
            float dist = Vector3.Distance(obj.transform.position, centroid);
            float planetRadius = obj.transform.localScale.x / 2f;
            if (dist + planetRadius > maxDistance)
                maxDistance = dist + planetRadius;
        }


        maxDistance = Mathf.Max(maxDistance, 8.0f);


        float fov = cam.fieldOfView * Mathf.Deg2Rad;
        float cameraDistance = maxDistance / Mathf.Tan(fov / 2f);
        cameraDistance *= 1.3f;

        Vector3 cameraOffset = new Vector3(0f, cameraDistance * 0.3f, -cameraDistance);
        cam.transform.position = centroid + cameraOffset;
        cam.transform.LookAt(centroid);

        Debug.Log($"[VisualizationManager] 📷 Initial camera framed at distance {cameraDistance:F1}.");
    }

    public void ToggleGhostPlanetOverlay()
    {
        if (ghostPlanetOverlayActive)
        {

            int lineCount = ghostLineObjects.Count;
            int sphereCount = ghostSphereObjects.Count;
            ClearGhostLines();
            ghostLineOverlayActive = false;
            ClearGhostSpheres();
            ghostPlanetOverlayActive = false;
            Debug.Log($"[VisualizationManager] 👻 Ghost overlay P → OFF (cleared {sphereCount} spheres, {lineCount} lines).");
        }
        else
        {

            ShowGhostSpheres();
            ghostPlanetOverlayActive = true;
            Debug.Log($"[VisualizationManager] 👻 Ghost overlay P → ON ({ghostedNodes.Count} nodes ghosted).");
        }
    }

    private List<ClusterNode> GetFadedExpandedNodes()
    {
        var result = new List<ClusterNode>();
        foreach (var kvp in nodeSphereMap)
        {
            ClusterNode node = kvp.Key;
            GameObject sphere = kvp.Value;

            if (node.IsExpanded && sphere != null)
            {
                Renderer r = sphere.GetComponent<Renderer>();


                if (r != null && !r.enabled && !activeFadeCoroutines.ContainsKey(node))
                {
                    result.Add(node);
                }
            }
        }
        return result;
    }

    private void ShowGhostSpheres()
    {

        ClearGhostSpheres();

        List<ClusterNode> candidates = GetFadedExpandedNodes();

        foreach (ClusterNode node in candidates)
        {

            GameObject ghostObj = Instantiate(planetPrefab, objectContainer);


            if (nodeWorldPositionMap.TryGetValue(node, out Vector3 worldPos))
                ghostObj.transform.position = worldPos;
            else
                ghostObj.transform.position = node.Position * positionScale;


            float radius = ComputeRadius(node.Size);
            float depthScale = Mathf.Pow(depthRadiusFactor, node.Depth);
            radius = Mathf.Max(radius * depthScale, minEffectiveRadius);
            float diameter = radius * 2f;
            ghostObj.transform.localScale = new Vector3(diameter, diameter, diameter);


            Renderer renderer = ghostObj.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material ghostMat = new Material(renderer.sharedMaterial);


                Color ghostColor = GetNodeColor(node);
                ghostColor.a = 0.25f;
                ghostMat.color = ghostColor;


                ghostMat.SetFloat("_Mode", 3f);
                ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                ghostMat.SetInt("_ZWrite", 0);
                ghostMat.DisableKeyword("_ALPHATEST_ON");
                ghostMat.EnableKeyword("_ALPHABLEND_ON");
                ghostMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                ghostMat.renderQueue = 3000;

                renderer.material = ghostMat;
            }


            Collider col = ghostObj.GetComponent<Collider>();
            if (col != null) Destroy(col);

            ghostObj.name = $"Ghost_{node.NodeId}";


            ghostSphereObjects.Add(ghostObj);
            ghostedNodes.Add(node);
        }
    }

    private void ClearGhostSpheres()
    {
        foreach (var obj in ghostSphereObjects)
        {
            if (obj != null) Destroy(obj);
        }
        ghostSphereObjects.Clear();
        ghostedNodes.Clear();
    }

    private void ClearGhostLines()
    {
        foreach (var obj in ghostLineObjects)
        {
            if (obj != null) Destroy(obj);
        }
        ghostLineObjects.Clear();
    }

    public void ToggleGhostLineOverlay()
    {

        if (!ghostPlanetOverlayActive) return;

        if (ghostLineOverlayActive)
        {
            ClearGhostLines();
            ghostLineOverlayActive = false;
            Debug.Log("[VisualizationManager] 👻 Ghost overlay L → OFF.");
        }
        else
        {
            ShowGhostLines();
            ghostLineOverlayActive = true;
            Debug.Log($"[VisualizationManager] 👻 Ghost overlay L → ON ({ghostLineObjects.Count} lines created).");
        }
    }

    private void ShowGhostLines()
    {

        ClearGhostLines();

        foreach (ClusterNode node in ghostedNodes)
        {

            Vector3 parentPos;
            if (!nodeWorldPositionMap.TryGetValue(node, out parentPos))
                parentPos = node.Position * positionScale;


            if (node.SpawnedChildObjects == null || node.SpawnedChildObjects.Count == 0)
                continue;

            Color lineColor = GetNodeColor(node);
            lineColor.a = 0.25f;

            for (int i = 0; i < node.SpawnedChildObjects.Count; i++)
            {
                GameObject childObj = node.SpawnedChildObjects[i];
                if (childObj == null) continue;

                Vector3 childPos = childObj.transform.position;


                GameObject lineObj = new GameObject($"GhostLine_{node.NodeId}_to_{i}");
                lineObj.transform.SetParent(objectContainer);

                LineRenderer lr = lineObj.AddComponent<LineRenderer>();


                lr.positionCount = 2;
                lr.SetPosition(0, parentPos);
                lr.SetPosition(1, childPos);


                lr.startWidth = 0.015f;
                lr.endWidth   = 0.015f;


                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.startColor = lineColor;
                lr.endColor   = lineColor;


                lr.useWorldSpace = true;

                ghostLineObjects.Add(lineObj);
            }
        }
    }

    private void ClearAllGhostOverlays()
    {
        if (!ghostPlanetOverlayActive && !ghostLineOverlayActive) return;

        int lineCount = ghostLineObjects.Count;
        int sphereCount = ghostSphereObjects.Count;

        ClearGhostLines();
        ghostLineOverlayActive = false;
        ClearGhostSpheres();
        ghostPlanetOverlayActive = false;

        Debug.Log($"[VisualizationManager] 👻 Ghost overlays auto-cleared (cleared {sphereCount} spheres, {lineCount} lines).");
    }
}
