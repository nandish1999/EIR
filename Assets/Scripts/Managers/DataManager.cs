using UnityEngine;

/// <summary>
/// Phase 0 data pipeline orchestrator.
/// Loads the CSV, builds the cluster tree, and logs validation results.
/// Attach this to an empty GameObject named "Managers" in your scene.
/// </summary>
public class DataManager : MonoBehaviour
{
    [Header("CSV Settings")]
    [Tooltip("Path to the CSV file, relative to StreamingAssets/")]
    public string csvRelativePath = "Data/unity_pruned_density_tree_3d_colors.csv";

    /// <summary>
    /// The fully constructed cluster tree.
    /// Other scripts will use this later.
    /// </summary>
    public ClusterTree Tree { get; private set; }

    /// <summary>
    /// True after the tree has been successfully built.
    /// </summary>
    public bool IsReady { get; private set; }

    void Awake()
    {
        IsReady = false;

        Debug.Log("[DataManager] Starting CSV load...");
        var rows = CSVParser.ParseFromStreamingAssets(csvRelativePath);

        if (rows == null || rows.Count == 0)
        {
            Debug.LogError("[DataManager] CSV parsing returned no rows. Check the file path.");
            return;
        }

        Debug.Log("[DataManager] Building cluster tree...");
        Tree = TreeBuilder.Build(rows);

        if (Tree == null || Tree.Planets.Count == 0)
        {
            Debug.LogError("[DataManager] Tree building failed — no planets found.");
            return;
        }

        Debug.Log(Tree.GetSummary());

        Debug.Log("[DataManager] --- Spot Checks ---");
        Debug.Log($"[DataManager] Planet 0 node_id: {Tree.Planets[0].NodeId}");
        Debug.Log($"[DataManager] Planet 0 position: {Tree.Planets[0].Position}");
        Debug.Log($"[DataManager] Planet 0 size: {Tree.Planets[0].Size}");
        Debug.Log($"[DataManager] Planet 0 children: {Tree.Planets[0].Children.Count}");

        var testNode = Tree.GetNode("0_node_11");
        if (testNode != null)
        {
            Debug.Log($"[DataManager] Lookup test — 0_node_11: depth={testNode.Depth}, images={testNode.ActualImageCount}, isLeaf={testNode.IsLeaf}");
            Debug.Log($"[DataManager] Breadcrumb: {string.Join(" > ", testNode.GetPathFromRoot())}");
        }

        IsReady = true;
        Debug.Log("[DataManager] Data pipeline complete. Ready for visualization.");
    }
}