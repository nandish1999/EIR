using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents a cluster node in the landscape visualization hierarchy.
/// 
/// A ClusterNode can be:
///   - A planet-level node (ParentId == "root", Depth == 0)
///   - An intermediate cluster node (has children, no images)
///   - A leaf cluster node (no children, may have images attached)
///
/// The tree structure is built at runtime by TreeBuilder:
///   - Parent / Children references are linked after all nodes are created
///   - Images are attached to their parent leaf nodes
///   - Depth is computed by walking the tree from planet roots
/// </summary>
[System.Serializable]
public class ClusterNode
{
    // -----------------------------------------------------------
    // Identity
    // -----------------------------------------------------------

    /// <summary>
    /// Unique identifier from the CSV (e.g. "planet_0", "0_node_617").
    /// </summary>
    public string NodeId;

    /// <summary>
    /// The node_id of this node's parent in the CSV.
    /// "root" for top-level planet nodes.
    /// </summary>
    public string ParentId;

    /// <summary>
    /// Index of the top-level planet this node belongs to (0, 1, or 2).
    /// </summary>
    public int PlanetIndex;

    // -----------------------------------------------------------
    // Hierarchy (set by TreeBuilder at runtime)
    // -----------------------------------------------------------

    /// <summary>
    /// Depth in the hierarchy. 0 = planet level, 1 = first child level, etc.
    /// Computed at runtime by TreeBuilder.
    /// </summary>
    public int Depth;

    /// <summary>
    /// Direct reference to the parent node. Null for planet-level nodes.
    /// </summary>
    public ClusterNode Parent;

    /// <summary>
    /// Direct child cluster nodes. Empty for leaf nodes.
    /// </summary>
    public List<ClusterNode> Children;

    /// <summary>
    /// Images directly attached to this node. Only populated for leaf nodes.
    /// </summary>
    public List<ImageItem> Images;

    // -----------------------------------------------------------
    // Data fields from CSV
    // -----------------------------------------------------------

    /// <summary>
    /// Subtree image count from the original (un-pruned) clustering.
    /// Note: this may be larger than Images.Count due to CSV pruning.
    /// Use ActualImageCount for the real count of available images.
    /// </summary>
    public int Size;

    /// <summary>
    /// UMAP 3D position from dimensionality reduction.
    /// Approximately the centroid of this node's contained images.
    /// </summary>
    public Vector3 Position;

    // -----------------------------------------------------------
    // Runtime expansion state (Phase 6 — not from CSV)
    // -----------------------------------------------------------

    /// <summary>
    /// Whether this node is currently expanded (children/images visible).
    /// Runtime-only flag, set to false at construction.
    /// </summary>
    [System.NonSerialized]
    public bool IsExpanded = false;

    /// <summary>
    /// References to GameObjects spawned as this node's children or images.
    /// Used for cleanup when collapsing. Managed by VisualizationManager.
    /// </summary>
    [System.NonSerialized]
    public List<GameObject> SpawnedChildObjects = new List<GameObject>();

    // -----------------------------------------------------------
    // Convenience properties
    // -----------------------------------------------------------

    /// <summary>True if this node has no child cluster nodes.</summary>
    public bool IsLeaf => Children == null || Children.Count == 0;

    /// <summary>True if this is a top-level planet node (parent is virtual root).</summary>
    public bool IsPlanet => ParentId == "root";

    /// <summary>True if this node has at least one image attached.</summary>
    public bool HasImages => Images != null && Images.Count > 0;

    /// <summary>
    /// The actual number of images available in this CSV (may differ from Size
    /// due to pruning of the original tree).
    /// </summary>
    public int ActualImageCount => Images != null ? Images.Count : 0;

    /// <summary>Number of direct child nodes.</summary>
    public int ChildCount => Children != null ? Children.Count : 0;

    /// <summary>
    /// True if this is a leaf node whose Size > 0 but has no image rows
    /// (i.e., the images were pruned from the CSV export).
    /// </summary>
    public bool IsPrunedLeaf => IsLeaf && Size > 0 && !HasImages;

    // -----------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------

    public ClusterNode()
    {
        Children = new List<ClusterNode>();
        Images = new List<ImageItem>();
    }

    // -----------------------------------------------------------
    // Utility methods
    // -----------------------------------------------------------

    /// <summary>
    /// Returns the total number of images in this node's entire subtree
    /// (counting actual image rows, not the Size field).
    /// </summary>
    public int GetSubtreeImageCount()
    {
        int count = ActualImageCount;
        if (Children != null)
        {
            foreach (var child in Children)
            {
                count += child.GetSubtreeImageCount();
            }
        }
        return count;
    }

    /// <summary>
    /// Returns all leaf nodes in this node's subtree (including itself if it's a leaf).
    /// </summary>
    public List<ClusterNode> GetLeafNodes()
    {
        var leaves = new List<ClusterNode>();
        CollectLeaves(this, leaves);
        return leaves;
    }

    private static void CollectLeaves(ClusterNode node, List<ClusterNode> leaves)
    {
        if (node.IsLeaf)
        {
            leaves.Add(node);
            return;
        }
        foreach (var child in node.Children)
        {
            CollectLeaves(child, leaves);
        }
    }

    /// <summary>
    /// Builds the path from the root to this node as a list of node IDs.
    /// Useful for breadcrumb navigation.
    /// </summary>
    public List<string> GetPathFromRoot()
    {
        var path = new List<string>();
        var current = this;
        while (current != null)
        {
            path.Insert(0, current.NodeId);
            current = current.Parent;
        }
        return path;
    }

    public override string ToString()
    {
        string nodeType = IsPlanet ? "Planet" : (IsLeaf ? "Leaf" : "Branch");
        return $"[{nodeType}] {NodeId} depth={Depth} size={Size} " +
               $"children={ChildCount} images={ActualImageCount} " +
               $"pos=({Position.x:F2},{Position.y:F2},{Position.z:F2})";
    }
}
