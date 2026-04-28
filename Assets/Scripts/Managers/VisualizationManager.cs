using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the in-place expansion visualization of the cluster tree.
///
/// Placement strategy: Candidate K (global anchor + uniform planet scale).
/// - Planets are placed at their raw UMAP positions × positionScale.
/// - All children and images within a planet are positioned relative to
///   the planet's world position using their UMAP offset × planetScale.
/// - This preserves all within-planet distance ratios exactly.
/// - Clicking a node toggles expansion: reveals/hides its direct children.
/// - Collapsing is recursive: all expanded descendants are also collapsed.
/// </summary>
public class VisualizationManager : MonoBehaviour
{
    // -----------------------------------------------------------
    // Inspector-assigned references
    // -----------------------------------------------------------

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
        new Color(0.29f, 0.56f, 0.85f, 1f),  // planet_0: soft blue
        new Color(0.85f, 0.44f, 0.29f, 1f),  // planet_1: warm orange
        new Color(0.29f, 0.78f, 0.47f, 1f),  // planet_2: fresh green
    };

    [Header("Depth Colors")]
    [Tooltip("If true, darken/lighten the planet color based on depth.")]
    public bool tintByDepth = true;

    // -----------------------------------------------------------
    // Runtime state
    // -----------------------------------------------------------

    /// <summary>Container transform for all spawned objects.</summary>
    private Transform objectContainer;

    /// <summary>
    /// Stack tracking the order of expansions for Escape-undo behavior.
    /// Most recently expanded node is on top.
    /// </summary>
    private Stack<ClusterNode> expansionHistory = new Stack<ClusterNode>();

    /// <summary>
    /// References to the top-level planet GameObjects (always visible).
    /// </summary>
    private List<GameObject> planetObjects = new List<GameObject>();

    /// <summary>
    /// Maps each ClusterNode to the GameObject representing its sphere.
    /// Used to hide/show the parent sphere when expanding/collapsing.
    /// </summary>
    private Dictionary<ClusterNode, GameObject> nodeSphereMap = new Dictionary<ClusterNode, GameObject>();

    /// <summary>
    /// Maps each ClusterNode to its actual spawned world position.
    /// Used by the Candidate K global-anchor strategy: children and images
    /// are positioned relative to their planet ancestor's world position.
    /// </summary>
    private Dictionary<ClusterNode, Vector3> nodeWorldPositionMap = new Dictionary<ClusterNode, Vector3>();

    /// <summary>
    /// Maps each expanded ClusterNode to the list of connection line GameObjects
    /// drawn from the parent to each of its direct children.
    /// Lines are created on expand and destroyed on collapse.
    /// </summary>
    private Dictionary<ClusterNode, List<GameObject>> nodeConnectionLines = new Dictionary<ClusterNode, List<GameObject>>();


    /// <summary>Duration in seconds for the parent sphere fade-out after expansion.</summary>
    private const float FadeOutDuration = 3f;

    /// <summary>
    /// Maps each expanded node to an active fade-out coroutine.
    /// Used to cancel the fade safely on collapse.
    /// </summary>
    private Dictionary<ClusterNode, Coroutine> activeFadeCoroutines = new Dictionary<ClusterNode, Coroutine>();

    // -----------------------------------------------------------
    // Ghost overlay state (P = ghost spheres, L = ghost lines)
    // -----------------------------------------------------------

    /// <summary>Whether the ghost planet overlay (P key) is currently active.</summary>
    private bool ghostPlanetOverlayActive = false;

    /// <summary>Whether the ghost line overlay (L key) is currently active. L only works when P is ON.</summary>
    private bool ghostLineOverlayActive = false;

    /// <summary>Tracked ghost sphere GameObjects for cleanup.</summary>
    private List<GameObject> ghostSphereObjects = new List<GameObject>();

    /// <summary>Tracked ghost line GameObjects for cleanup.</summary>
    private List<GameObject> ghostLineObjects = new List<GameObject>();

    /// <summary>
    /// Nodes that currently have ghost spheres rendered.
    /// Used by ShowGhostLines() to know which parents to draw lines from.
    /// </summary>
    private List<ClusterNode> ghostedNodes = new List<ClusterNode>();

    // -----------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------

    void Start()
    {
        // Validate references
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

        // Create a container for spawned objects
        var containerObj = new GameObject("ObjectContainer");
        objectContainer = containerObj.transform;

        // Spawn top-level planets (always visible, never destroyed)
        SpawnPlanets();

        // One-time camera framing to show all planets at startup
        FrameInitialView();
    }

    // -----------------------------------------------------------
    // Public API (called by InteractionManager)
    // -----------------------------------------------------------

    /// <summary>
    /// Toggles expansion state of a node.
    /// If collapsed → expand (reveal children/images).
    /// If expanded → collapse (hide descendants).
    /// </summary>
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

    /// <summary>
    /// Collapses the most recently expanded node (Escape-undo).
    /// Returns true if something was collapsed.
    /// </summary>
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
            // If it was already collapsed (e.g., by parent collapse), skip it
        }
        return false;
    }

    /// <summary>True if there is at least one expanded node to undo.</summary>
    public bool HasExpandedNodes => expansionHistory.Count > 0;

    // -----------------------------------------------------------
    // Internal: spawn top-level planets
    // -----------------------------------------------------------

    /// <summary>
    /// Spawns the 3 top-level planet spheres at their true CSV positions.
    /// These are always visible — never destroyed during navigation.
    /// </summary>
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

    // -----------------------------------------------------------
    // Internal: expansion
    // -----------------------------------------------------------

    /// <summary>
    /// Expands a node: reveals its direct children (or images for leaves).
    /// </summary>
    private void ExpandNode(ClusterNode node)
    {
        ClearAllGhostOverlays();
        if (node.IsExpanded) return;

        if (node.IsLeaf)
        {
            // --- Leaf node: spawn image quads ---
            if (!node.HasImages)
            {
                Debug.Log($"[VisualizationManager] Leaf \"{node.NodeId}\" has no images (pruned).");
                return;
            }

            SpawnImages(node);
            node.IsExpanded = true;
            expansionHistory.Push(node);

            // Disable collider so children are clickable
            if (nodeSphereMap.TryGetValue(node, out GameObject leafSphere))
            {
                Collider col = leafSphere.GetComponent<Collider>();
                if (col != null) col.enabled = false;
            }

            // Draw connection lines from parent to each image
            SpawnConnectionLines(node);

            // Start fade-out (parent fades to invisible, lines destroyed at end)
            StartFadeOut(node);

            Debug.Log($"[VisualizationManager] 🖼 Expanded leaf \"{node.NodeId}\" — " +
                      $"{node.ActualImageCount} images spawned at global-anchor positions.");

            // Smoothly focus camera on the expansion region
            FocusCameraOnExpansion(node);
        }
        else
        {
            // --- Branch node: spawn child spheres ---
            SpawnChildren(node);
            node.IsExpanded = true;
            expansionHistory.Push(node);

            // Disable collider so children are clickable
            if (nodeSphereMap.TryGetValue(node, out GameObject branchSphere))
            {
                Collider col = branchSphere.GetComponent<Collider>();
                if (col != null) col.enabled = false;
            }

            // Draw connection lines from parent to each child
            SpawnConnectionLines(node);

            // Start fade-out (parent fades to invisible, lines destroyed at end)
            StartFadeOut(node);

            Debug.Log($"[VisualizationManager] 🔽 Expanded \"{node.NodeId}\" — " +
                      $"{node.ChildCount} children spawned at global-anchor positions.");

            // Smoothly focus camera on the expansion region
            FocusCameraOnExpansion(node);
        }
    }

    /// <summary>
    /// Collapses a node: hides its spawned children/images, recursively
    /// collapses any expanded descendants.
    /// </summary>
    private void CollapseNode(ClusterNode node)
    {
        ClearAllGhostOverlays();
        if (!node.IsExpanded) return;

        // First, recursively collapse any expanded children
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

        // Destroy all spawned child objects for this node
        foreach (var obj in node.SpawnedChildObjects)
        {
            if (obj != null)
            {
                // Remove from nodeSphereMap and nodeWorldPositionMap
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

        // Cancel the fade-out coroutine if it is still running
        CancelFadeOut(node);

        // Cancel any active camera transition so user control resumes
        if (cameraController != null)
            cameraController.CancelTransition();

        // Destroy connection lines (safe no-op if already destroyed by fade)
        DestroyConnectionLines(node);

        // Restore this node: visible + opaque + clickable
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

    // -----------------------------------------------------------
    // Internal: spawning
    // -----------------------------------------------------------

    /// <summary>
    /// Spawns child node spheres around a parent node using global-anchor
    /// placement (Candidate K — unified).
    ///
    /// Every child (whether single or multiple) is positioned using its raw
    /// UMAP offset from the planet ancestor, scaled uniformly by planetScale.
    /// This preserves all within-planet distance ratios and eliminates
    /// recursive compounding.
    ///
    /// After computing Candidate K positions, applies sibling-local radius
    /// capping to prevent visual overlap among dense sibling groups.
    /// Positions are NOT modified — only visual sphere radii are capped.
    /// </summary>
    private void SpawnChildren(ClusterNode parentNode)
    {
        var children = parentNode.Children;

        // Global anchor placement (Candidate K) — unified for all child counts
        ClusterNode planetNode = GetPlanetAncestor(parentNode);
        Vector3 planetWorldPos = nodeWorldPositionMap[planetNode];
        Vector3 planetRawPos = planetNode.Position * positionScale;

        // Phase A: Pre-compute all Candidate K positions and uncapped radii
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

        // Phase B–D: Sibling-local radius capping (only for groups with 2+ children)
        float[] cappedRadii = ComputeSiblingCappedRadii(childPositions, uncappedRadii);

        // Phase E: Spawn each child at its exact Candidate K position with capped radius
        for (int i = 0; i < children.Count; i++)
        {
            GameObject obj = SpawnNodeSphere(children[i], childPositions[i], cappedRadii[i]);
            parentNode.SpawnedChildObjects.Add(obj);
        }
    }

    /// <summary>
    /// Computes sibling-capped radii for a group of children.
    /// For groups with 2+ members: computes average nearest-neighbour distance,
    /// then caps each radius to siblingRadiusCapFactor × avgNN / 2.
    /// Single-child groups or disabled capping returns uncapped radii.
    /// Positions are never modified — only radii are adjusted.
    /// </summary>
    private float[] ComputeSiblingCappedRadii(Vector3[] positions, float[] uncappedRadii)
    {
        int count = positions.Length;
        float[] capped = new float[count];
        System.Array.Copy(uncappedRadii, capped, count);

        // Skip capping if disabled or single child (no siblings to overlap)
        if (siblingRadiusCapFactor <= 0f || count <= 1)
            return capped;

        // Phase B: Compute nearest-neighbour distance for each child
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

        // Phase C: Compute maximum allowed radius
        float avgNN = sumNN / count;
        float maxAllowedRadius = siblingRadiusCapFactor * avgNN / 2f;

        // Phase D: Cap each radius
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

    /// <summary>
    /// Spawns a single node sphere.
    /// If worldPosition is provided, uses that instead of the node's CSV position.
    /// Top-level planets pass null (use CSV position). Children pass spread positions.
    /// If radiusOverride >= 0, uses that radius instead of computing from node size/depth.
    /// </summary>
    private GameObject SpawnNodeSphere(ClusterNode node, Vector3? worldPosition = null,
                                       float radiusOverride = -1f)
    {
        GameObject obj = Instantiate(planetPrefab, objectContainer);

        // Position: use override if provided, otherwise true CSV position
        Vector3 finalPos = worldPosition ?? (node.Position * positionScale);
        obj.transform.position = finalPos;

        // Track this node's actual world position for future child expansions
        nodeWorldPositionMap[node] = finalPos;

        // Size: use override if provided, otherwise compute from node size + depth
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

        // Attach PlanetVisual
        PlanetVisual visual = obj.AddComponent<PlanetVisual>();
        visual.Initialize(node, radius, color);

        // Track the mapping so we can hide/show this sphere on expand/collapse
        nodeSphereMap[node] = obj;

        return obj;
    }

    /// <summary>
    /// Spawns image quads for a leaf node using global-anchor placement (Candidate K).
    ///
    /// Each image is positioned using its raw UMAP offset from the planet
    /// ancestor, scaled uniformly by planetScale. This preserves all
    /// within-planet distance ratios and eliminates recursive compounding.
    ///
    /// Adaptive quad sizing is based on the actual group spread in scaled
    /// space, replacing the previous effectiveRadius-based formula.
    ///
    /// Loads textures asynchronously via ImageLoader.
    /// </summary>
    private void SpawnImages(ClusterNode leafNode)
    {
        List<ImageVisual> imageVisuals = new List<ImageVisual>();

        var images = leafNode.Images;

        // Global anchor: compute each image's position relative to the planet
        ClusterNode planetNode = GetPlanetAncestor(leafNode);
        Vector3 planetWorldPos = nodeWorldPositionMap[planetNode];
        Vector3 planetRawPos = planetNode.Position * positionScale;

        // Pre-compute all scaled positions
        Vector3[] scaledPositions = new Vector3[images.Count];
        for (int i = 0; i < images.Count; i++)
        {
            Vector3 globalOffset = images[i].Position * positionScale - planetRawPos;
            scaledPositions[i] = planetWorldPos + globalOffset * planetScale;
        }

        // Compute actual group spread for adaptive quad sizing
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
        groupRadius = Mathf.Max(groupRadius, 0.1f); // floor to prevent zero-size quads

        // Adaptive quad size based on actual group spread
        float adaptiveQuadSize = groupRadius * 0.4f / Mathf.Sqrt(images.Count);
        adaptiveQuadSize = Mathf.Clamp(adaptiveQuadSize, minImageQuadSize, imageQuadSize);

        // Spawn each image quad at its global-anchor position
        for (int i = 0; i < images.Count; i++)
        {
            GameObject quadObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadObj.transform.SetParent(objectContainer);

            quadObj.transform.position = scaledPositions[i];

            // Attach ImageVisual with adaptive size
            ImageVisual visual = quadObj.AddComponent<ImageVisual>();
            visual.Initialize(images[i], adaptiveQuadSize);

            imageVisuals.Add(visual);
            leafNode.SpawnedChildObjects.Add(quadObj);
        }

        // Load textures asynchronously
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


    // -----------------------------------------------------------
    // Connection lines (Stage 2)
    // -----------------------------------------------------------

    /// <summary>
    /// Creates thin LineRenderer objects from the parent node to each of its
    /// direct spawned children. Lines are purely visual (no collider).
    /// Called after SpawnChildren/SpawnImages during ExpandNode.
    /// </summary>
    private void SpawnConnectionLines(ClusterNode node)
    {
        if (node.SpawnedChildObjects == null || node.SpawnedChildObjects.Count == 0) return;

        // Get parent world position
        Vector3 parentPos;
        if (!nodeWorldPositionMap.TryGetValue(node, out parentPos))
            parentPos = node.Position * positionScale;

        var lines = new List<GameObject>(node.SpawnedChildObjects.Count);

        for (int i = 0; i < node.SpawnedChildObjects.Count; i++)
        {
            GameObject childObj = node.SpawnedChildObjects[i];
            if (childObj == null) continue;

            Vector3 childPos = childObj.transform.position;

            // Create a dedicated GameObject for the line
            GameObject lineObj = new GameObject($"Line_{node.NodeId}_to_{i}");
            lineObj.transform.SetParent(objectContainer);

            LineRenderer lr = lineObj.AddComponent<LineRenderer>();

            // Configure the line
            lr.positionCount = 2;
            lr.SetPosition(0, parentPos);
            lr.SetPosition(1, childPos);

            // Thin width
            lr.startWidth = 0.03f;
            lr.endWidth   = 0.03f;

            // Simple unlit material with the node's planet color
            lr.material = new Material(Shader.Find("Sprites/Default"));
            Color lineColor = GetNodeColor(node);
            lineColor.a = 0.5f;
            lr.startColor = lineColor;
            lr.endColor   = lineColor;

            // Ensure it renders in world space
            lr.useWorldSpace = true;

            lines.Add(lineObj);
        }

        nodeConnectionLines[node] = lines;

        Debug.Log($"[VisualizationManager] 📎 Spawned {lines.Count} connection lines for \"{node.NodeId}\".");
    }

    /// <summary>
    /// Destroys all connection line GameObjects for a node and removes them from tracking.
    /// Called during CollapseNode before restoring appearance.
    /// </summary>
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

    // -----------------------------------------------------------
    // Camera focus on expansion
    // -----------------------------------------------------------

    /// <summary>
    /// Computes the bounding region of an expansion: the parent’s world position
    /// plus all spawned child object positions, including their visual radii.
    /// Returns the centroid and the maximum extent (radius from centroid to
    /// farthest point including object size).
    /// </summary>
    private (Vector3 centroid, float extent) ComputeExpansionBounds(ClusterNode node)
    {
        // Collect all positions: parent + each spawned child
        Vector3 parentPos = nodeWorldPositionMap.TryGetValue(node, out Vector3 pp)
            ? pp
            : node.Position * positionScale;

        var positions = new List<Vector3> { parentPos };
        var radii = new List<float> { 0f }; // parent radius not critical for framing

        foreach (var childObj in node.SpawnedChildObjects)
        {
            if (childObj == null) continue;
            positions.Add(childObj.transform.position);
            // Half the scale gives the visual radius of the sphere/quad
            radii.Add(childObj.transform.localScale.x / 2f);
        }

        // Compute centroid
        Vector3 centroid = Vector3.zero;
        foreach (var pos in positions)
            centroid += pos;
        centroid /= positions.Count;

        // Compute max extent from centroid
        float maxExtent = 0f;
        for (int i = 0; i < positions.Count; i++)
        {
            float dist = Vector3.Distance(positions[i], centroid) + radii[i];
            if (dist > maxExtent) maxExtent = dist;
        }

        // Ensure a minimum extent so the camera doesn't zoom in too aggressively
        maxExtent = Mathf.Max(maxExtent, 0.5f);

        return (centroid, maxExtent);
    }

    /// <summary>
    /// Triggers the camera to smoothly focus on the expansion region of a node.
    /// Called at the end of ExpandNode after children, lines, and fade are set up.
    /// Safe to call even if cameraController is not assigned (no-op).
    /// </summary>
    private void FocusCameraOnExpansion(ClusterNode node)
    {
        if (cameraController == null) return;

        var (centroid, extent) = ComputeExpansionBounds(node);
        cameraController.FocusOnRegion(centroid, extent);

        Debug.Log($"[VisualizationManager] 📷 Camera focus triggered for \"{node.NodeId}\" " +
                  $"(centroid={centroid}, extent={extent:F2}).");
    }

    // -----------------------------------------------------------
    // Parent fade-out on expansion
    // -----------------------------------------------------------

    /// <summary>
    /// Starts the parent sphere fade-out coroutine for a node.
    /// Cancels any existing fade for the same node first.
    /// Assumes the node's sphere already has its collider disabled.
    /// </summary>
    private void StartFadeOut(ClusterNode node)
    {
        CancelFadeOut(node);

        Coroutine fade = StartCoroutine(FadeOutParent(node, FadeOutDuration));
        activeFadeCoroutines[node] = fade;
    }

    /// <summary>
    /// Cancels the fade-out coroutine for a node if one is running.
    /// Called during collapse to stop the fade mid-animation.
    /// </summary>
    private void CancelFadeOut(ClusterNode node)
    {
        if (activeFadeCoroutines.TryGetValue(node, out Coroutine existing))
        {
            StopCoroutine(existing);
            activeFadeCoroutines.Remove(node);
        }
    }

    /// <summary>
    /// Coroutine: gradually fades the parent sphere from alpha 1.0 to 0.0
    /// over the specified duration. At the end:
    ///   - disables the parent's renderer (fully invisible)
    ///   - destroys connection lines
    /// The parent's collider must already be disabled before this runs.
    /// </summary>
    private IEnumerator FadeOutParent(ClusterNode node, float duration)
    {
        if (!nodeSphereMap.TryGetValue(node, out GameObject sphereObj)) yield break;
        PlanetVisual visual = sphereObj?.GetComponent<PlanetVisual>();
        if (visual == null) yield break;

        // Switch to transparent shader mode so alpha changes are visible
        // (starts at alpha 1.0 — fully opaque appearance in transparent mode)
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

        // Fade complete: make parent fully invisible
        visual.SetAlpha(0f);
        visual.SetVisible(false);

        // Destroy connection lines (they served their purpose)
        DestroyConnectionLines(node);

        // Clean up coroutine reference
        activeFadeCoroutines.Remove(node);

        Debug.Log($"[VisualizationManager] 👻 Parent \"{node.NodeId}\" fully faded out after {duration}s.");
    }

    // -----------------------------------------------------------
    // Scaling
    // -----------------------------------------------------------

    /// <summary>
    /// Computes the visual radius for a node based on its size.
    /// Formula: radius = log2(size + 1) * radiusScale
    /// </summary>
    private float ComputeRadius(int size)
    {
        float logSize = Mathf.Log(size + 1, 2);
        float radius = logSize * radiusScale;
        return Mathf.Max(radius, minRadius);
    }

    /// <summary>
    /// Walks up the Parent chain from any node to find its top-level planet ancestor.
    /// Used by the global-anchor placement strategy (Candidate K) to compute
    /// entity positions relative to the planet's coordinate frame.
    /// Guaranteed to terminate: the Parent chain always ends at a planet node
    /// (where Parent == null).
    /// </summary>
    private ClusterNode GetPlanetAncestor(ClusterNode node)
    {
        ClusterNode current = node;
        while (current.Parent != null)
            current = current.Parent;
        return current;
    }

    // -----------------------------------------------------------
    // Color assignment
    // -----------------------------------------------------------

    /// <summary>
    /// Returns a color for any node in the tree.
    /// Uses the planet family color and optionally tints by depth.
    /// </summary>
    private Color GetNodeColor(ClusterNode node)
    {
        Color baseColor = GetPlanetColor(node.PlanetIndex);

        if (tintByDepth && node.Depth > 0)
        {
            float lightenAmount = node.Depth * 0.06f;
            baseColor = Color.Lerp(baseColor, Color.white, Mathf.Clamp01(lightenAmount));
        }

        return baseColor;
    }

    /// <summary>
    /// Returns the base color for a planet by its index.
    /// </summary>
    private Color GetPlanetColor(int planetIndex)
    {
        if (planetColors != null && planetIndex >= 0 && planetIndex < planetColors.Length)
            return planetColors[planetIndex];
        return Color.white;
    }

    // -----------------------------------------------------------
    // Camera: one-time initial framing only
    // -----------------------------------------------------------

    /// <summary>
    /// Frames the camera once at startup to show all top-level planets.
    /// After this, the camera is fully user-controlled.
    /// </summary>
    private void FrameInitialView()
    {
        if (planetObjects.Count == 0) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        // Compute centroid of all planets
        Vector3 centroid = Vector3.zero;
        foreach (var obj in planetObjects)
            centroid += obj.transform.position;
        centroid /= planetObjects.Count;

        // Find farthest planet from centroid
        float maxDistance = 0f;
        foreach (var obj in planetObjects)
        {
            float dist = Vector3.Distance(obj.transform.position, centroid);
            float planetRadius = obj.transform.localScale.x / 2f;
            if (dist + planetRadius > maxDistance)
                maxDistance = dist + planetRadius;
        }

        // Ensure minimum distance
        maxDistance = Mathf.Max(maxDistance, 8.0f);

        // Pull camera back to see everything
        float fov = cam.fieldOfView * Mathf.Deg2Rad;
        float cameraDistance = maxDistance / Mathf.Tan(fov / 2f);
        cameraDistance *= 1.3f; // padding

        Vector3 cameraOffset = new Vector3(0f, cameraDistance * 0.3f, -cameraDistance);
        cam.transform.position = centroid + cameraOffset;
        cam.transform.LookAt(centroid);

        Debug.Log($"[VisualizationManager] 📷 Initial camera framed at distance {cameraDistance:F1}.");
    }

    // -----------------------------------------------------------
    // Ghost Overlay: P = ghost spheres, L = ghost lines
    // -----------------------------------------------------------

    /// <summary>
    /// Toggles the ghost planet overlay ON/OFF.
    /// When ON: creates semi-transparent duplicate spheres for all
    /// currently faded expanded parent nodes.
    /// When OFF: destroys all ghost spheres and resets state.
    /// Called by InteractionManager when the P key is pressed.
    /// </summary>
    public void ToggleGhostPlanetOverlay()
    {
        if (ghostPlanetOverlayActive)
        {
            // Turn OFF: clear lines first (L depends on P), then spheres
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
            // Turn ON: show ghosted spheres for all faded expanded parents
            ShowGhostSpheres();
            ghostPlanetOverlayActive = true;
            Debug.Log($"[VisualizationManager] 👻 Ghost overlay P → ON ({ghostedNodes.Count} nodes ghosted).");
        }
    }

    /// <summary>
    /// Identifies all nodes that qualify for ghost overlay:
    /// - IsExpanded == true
    /// - Real sphere still exists in nodeSphereMap
    /// - Renderer is disabled (fade has completed)
    /// - Not currently mid-fade (no active fade coroutine)
    /// </summary>
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
                // Only ghost if renderer is disabled (fade completed)
                // AND no active fade coroutine is running (not mid-fade)
                if (r != null && !r.enabled && !activeFadeCoroutines.ContainsKey(node))
                {
                    result.Add(node);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Creates ghost overlay spheres for all faded expanded parent nodes.
    /// Ghost spheres are visual-only duplicates: no collider, no PlanetVisual,
    /// semi-transparent (alpha 0.25), positioned at the real node's world position.
    /// </summary>
    private void ShowGhostSpheres()
    {
        // Clear any existing ghost spheres first (safety)
        ClearGhostSpheres();

        List<ClusterNode> candidates = GetFadedExpandedNodes();

        foreach (ClusterNode node in candidates)
        {
            // 1. Instantiate from planetPrefab (clean sphere, no scripts baked in)
            GameObject ghostObj = Instantiate(planetPrefab, objectContainer);

            // 2. Position at the real node's world position
            if (nodeWorldPositionMap.TryGetValue(node, out Vector3 worldPos))
                ghostObj.transform.position = worldPos;
            else
                ghostObj.transform.position = node.Position * positionScale;

            // 3. Compute the same radius/diameter as the real node sphere
            float radius = ComputeRadius(node.Size);
            float depthScale = Mathf.Pow(depthRadiusFactor, node.Depth);
            radius = Mathf.Max(radius * depthScale, minEffectiveRadius);
            float diameter = radius * 2f;
            ghostObj.transform.localScale = new Vector3(diameter, diameter, diameter);

            // 4. Configure transparent material manually (do NOT use PlanetVisual)
            Renderer renderer = ghostObj.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material ghostMat = new Material(renderer.sharedMaterial);

                // Set node color with ghost alpha
                Color ghostColor = GetNodeColor(node);
                ghostColor.a = 0.25f;
                ghostMat.color = ghostColor;

                // Configure Standard shader transparent render mode
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

            // 5. Destroy collider — ghost must not block raycasts
            Collider col = ghostObj.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // 6. Do NOT add PlanetVisual — safety reasons:
            //    - PlanetVisual.Node would reference a real ClusterNode
            //    - CollapseNode checks GetComponent<PlanetVisual>() to clean nodeSphereMap
            //    - FindObjectsOfType<PlanetVisual>() would find ghosts

            // 7. Name for easy identification
            ghostObj.name = $"Ghost_{node.NodeId}";

            // 8. Track for cleanup
            ghostSphereObjects.Add(ghostObj);
            ghostedNodes.Add(node);
        }
    }

    /// <summary>
    /// Destroys all ghost overlay spheres and clears tracking lists.
    /// </summary>
    private void ClearGhostSpheres()
    {
        foreach (var obj in ghostSphereObjects)
        {
            if (obj != null) Destroy(obj);
        }
        ghostSphereObjects.Clear();
        ghostedNodes.Clear();
    }

    /// <summary>
    /// Destroys all ghost overlay lines and clears the tracking list.
    /// </summary>
    private void ClearGhostLines()
    {
        foreach (var obj in ghostLineObjects)
        {
            if (obj != null) Destroy(obj);
        }
        ghostLineObjects.Clear();
    }

    /// <summary>
    /// Toggles the ghost line overlay ON/OFF.
    /// L only works when P (ghost planet overlay) is already ON.
    /// When L is turned ON: creates overlay LineRenderers from each ghosted
    /// parent to its spawned children.
    /// When L is turned OFF: destroys all ghost lines, ghost spheres remain.
    /// Called by InteractionManager when the L key is pressed.
    /// </summary>
    public void ToggleGhostLineOverlay()
    {
        // L does nothing when P is OFF — enforces the invariant
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

    /// <summary>
    /// Creates ghost overlay lines from each ghosted parent node to its
    /// spawned children. Uses ghostedNodes (populated by ShowGhostSpheres)
    /// as the source of which parents to draw lines from.
    ///
    /// Ghost lines are visually distinct from normal connection lines:
    /// - Thinner width (0.015f vs normal 0.03f)
    /// - Lower alpha (0.25 vs normal 0.5)
    /// - Same planet family color
    ///
    /// Covers ALL entries in node.SpawnedChildObjects (both child spheres
    /// and image quads), matching the original SpawnConnectionLines behavior.
    /// </summary>
    private void ShowGhostLines()
    {
        // Clear any existing ghost lines first (safety)
        ClearGhostLines();

        foreach (ClusterNode node in ghostedNodes)
        {
            // Get parent world position
            Vector3 parentPos;
            if (!nodeWorldPositionMap.TryGetValue(node, out parentPos))
                parentPos = node.Position * positionScale;

            // Skip if no spawned children
            if (node.SpawnedChildObjects == null || node.SpawnedChildObjects.Count == 0)
                continue;

            Color lineColor = GetNodeColor(node);
            lineColor.a = 0.25f;

            for (int i = 0; i < node.SpawnedChildObjects.Count; i++)
            {
                GameObject childObj = node.SpawnedChildObjects[i];
                if (childObj == null) continue; // defensive null-check

                Vector3 childPos = childObj.transform.position;

                // Create a dedicated GameObject for the ghost line
                GameObject lineObj = new GameObject($"GhostLine_{node.NodeId}_to_{i}");
                lineObj.transform.SetParent(objectContainer);

                LineRenderer lr = lineObj.AddComponent<LineRenderer>();

                // Configure the line
                lr.positionCount = 2;
                lr.SetPosition(0, parentPos);
                lr.SetPosition(1, childPos);

                // Thinner than normal lines (0.015f vs 0.03f)
                lr.startWidth = 0.015f;
                lr.endWidth   = 0.015f;

                // Simple unlit material with ghost alpha
                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.startColor = lineColor;
                lr.endColor   = lineColor;

                // Ensure it renders in world space
                lr.useWorldSpace = true;

                ghostLineObjects.Add(lineObj);
            }
        }
    }

    /// <summary>
    /// Clears ALL ghost overlays (spheres and lines) and resets state flags.
    /// Called at the start of ExpandNode and CollapseNode to ensure overlays
    /// are dismissed before the scene state changes.
    /// Safe no-op if no overlays are active.
    /// Guarded against redundant calls during recursive collapse chains.
    /// </summary>
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
