using UnityEngine;

/// <summary>
/// Handles user input for the in-place expansion visualization.
///
/// - Left-click on a node sphere: toggle expand/collapse
/// - Escape key: collapse the most recently expanded node
/// - P key: toggle ghost planet overlay (semi-transparent faded parents)
/// - L key: toggle ghost line overlay (only when P is ON)
/// </summary>
public class InteractionManager : MonoBehaviour
{
    // -----------------------------------------------------------
    // Inspector-assigned references
    // -----------------------------------------------------------

    [Header("References")]
    [Tooltip("The VisualizationManager that handles expand/collapse.")]
    public VisualizationManager visualizationManager;

    // -----------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------

    void Update()
    {
        // --- Left mouse click ---
        if (Input.GetMouseButtonDown(0))
        {
            HandleClick();
        }

        // --- Escape key ---
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            HandleEscape();
        }

        // --- P key: toggle ghost planet overlay ---
        if (Input.GetKeyDown(KeyCode.P))
        {
            visualizationManager.ToggleGhostPlanetOverlay();
        }

        // --- L key: toggle ghost line overlay (only when P is ON) ---
        if (Input.GetKeyDown(KeyCode.L))
        {
            visualizationManager.ToggleGhostLineOverlay();
        }
    }

    // -----------------------------------------------------------
    // Click handling
    // -----------------------------------------------------------

    private void HandleClick()
    {
        // Skip expand/collapse while right-mouse is held (camera orbit)
        if (Input.GetMouseButton(1)) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            PlanetVisual visual = hit.collider.GetComponent<PlanetVisual>();
            if (visual == null || visual.Node == null) return;

            ClusterNode clickedNode = visual.Node;

            // Expanded parents are not clickable — do nothing
            // (collider is also disabled, so this is a safety check)
            if (clickedNode.IsExpanded)
            {
                return;
            }

            if (clickedNode.IsLeaf)
            {
                if (clickedNode.HasImages)
                {
                    Debug.Log($"[InteractionManager] 🖼 Expanding leaf \"{clickedNode.NodeId}\" " +
                              $"— {clickedNode.ActualImageCount} images");
                }
                else
                {
                    Debug.Log($"[InteractionManager] Pruned leaf: \"{clickedNode.NodeId}\" " +
                              $"— no images in CSV.");
                    return; // Nothing to expand
                }
            }
            else
            {
                Debug.Log($"[InteractionManager] 🔽 Expanding \"{clickedNode.NodeId}\" " +
                          $"— {clickedNode.ChildCount} children");
            }

            visualizationManager.ToggleNode(clickedNode);
        }
    }

    // -----------------------------------------------------------
    // Escape handling
    // -----------------------------------------------------------

    private void HandleEscape()
    {
        if (visualizationManager.HasExpandedNodes)
        {
            Debug.Log("[InteractionManager] ⎋ Escape — collapsing last expanded node.");
            visualizationManager.CollapseLastExpanded();
        }
        else
        {
            Debug.Log("[InteractionManager] Nothing to collapse — all nodes are at base state.");
        }
    }
}
