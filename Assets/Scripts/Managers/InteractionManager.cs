using UnityEngine;

public class InteractionManager : MonoBehaviour
{

    [Header("References")]
    [Tooltip("The VisualizationManager that handles expand/collapse.")]
    public VisualizationManager visualizationManager;

    void Update()
    {

        if (Input.GetMouseButtonDown(0))
        {
            HandleClick();
        }


        if (Input.GetKeyDown(KeyCode.Escape))
        {
            HandleEscape();
        }


        if (Input.GetKeyDown(KeyCode.P))
        {
            visualizationManager.ToggleGhostPlanetOverlay();
        }


        if (Input.GetKeyDown(KeyCode.L))
        {
            visualizationManager.ToggleGhostLineOverlay();
        }
    }

    private void HandleClick()
    {

        if (Input.GetMouseButton(1)) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            PlanetVisual visual = hit.collider.GetComponent<PlanetVisual>();
            if (visual == null || visual.Node == null) return;

            ClusterNode clickedNode = visual.Node;

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
                    return;
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
