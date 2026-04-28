using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Root container for the entire parsed and linked cluster hierarchy.
/// 
/// Built by TreeBuilder from parsed CSVRows. Provides:
///   - The list of top-level planet nodes
///   - O(1) lookup of any node by its node_id
///   - A flat list of all image items
///   - Computed metadata (max depth, counts)
///   - Convenience query methods
/// </summary>
public class ClusterTree
{
    // -----------------------------------------------------------
    // Core data
    // -----------------------------------------------------------

    /// <summary>
    /// The top-level planet nodes (nodes whose parent_id is "root").
    /// Typically 3 planets: planet_0, planet_1, planet_2.
    /// </summary>
    public List<ClusterNode> Planets;

    /// <summary>
    /// Dictionary for O(1) lookup of any cluster node by its node_id.
    /// Contains all 103 nodes (planets + intermediates + leaves).
    /// </summary>
    public Dictionary<string, ClusterNode> NodeLookup;

    /// <summary>
    /// Flat list of all image items across the entire tree.
    /// Each image is also accessible via its parent node's Images list.
    /// </summary>
    public List<ImageItem> AllImages;

    // -----------------------------------------------------------
    // Computed metadata
    // -----------------------------------------------------------

    /// <summary>
    /// Maximum depth found in the tree (0 = planet level).
    /// Computed during tree building.
    /// </summary>
    public int MaxDepth;

    /// <summary>Total number of cluster nodes in the tree.</summary>
    public int TotalNodeCount => NodeLookup != null ? NodeLookup.Count : 0;

    /// <summary>Total number of image items in the tree.</summary>
    public int TotalImageCount => AllImages != null ? AllImages.Count : 0;

    // -----------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------

    public ClusterTree()
    {
        Planets = new List<ClusterNode>();
        NodeLookup = new Dictionary<string, ClusterNode>();
        AllImages = new List<ImageItem>();
        MaxDepth = 0;
    }

    // -----------------------------------------------------------
    // Query methods
    // -----------------------------------------------------------

    /// <summary>
    /// Retrieves a node by its node_id. Returns null if not found.
    /// </summary>
    public ClusterNode GetNode(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return null;
        NodeLookup.TryGetValue(nodeId, out ClusterNode node);
        return node;
    }

    /// <summary>
    /// Returns all leaf nodes across the entire tree.
    /// </summary>
    public List<ClusterNode> GetAllLeafNodes()
    {
        var leaves = new List<ClusterNode>();
        foreach (var planet in Planets)
        {
            leaves.AddRange(planet.GetLeafNodes());
        }
        return leaves;
    }

    /// <summary>
    /// Returns all leaf nodes that have at least one image attached.
    /// </summary>
    public List<ClusterNode> GetLeafNodesWithImages()
    {
        var result = new List<ClusterNode>();
        foreach (var leaf in GetAllLeafNodes())
        {
            if (leaf.HasImages)
                result.Add(leaf);
        }
        return result;
    }

    /// <summary>
    /// Returns all leaf nodes that have Size > 0 but no image rows
    /// (pruned leaves).
    /// </summary>
    public List<ClusterNode> GetPrunedLeafNodes()
    {
        var result = new List<ClusterNode>();
        foreach (var leaf in GetAllLeafNodes())
        {
            if (leaf.IsPrunedLeaf)
                result.Add(leaf);
        }
        return result;
    }

    /// <summary>
    /// Returns a human-readable summary of the tree for debugging.
    /// </summary>
    public string GetSummary()
    {
        var allLeaves = GetAllLeafNodes();
        int leavesWithImages = 0;
        int leavesWithoutImages = 0;
        foreach (var leaf in allLeaves)
        {
            if (leaf.HasImages) leavesWithImages++;
            else leavesWithoutImages++;
        }

        return $"=== ClusterTree Summary ===\n" +
               $"  Planets:                 {Planets.Count}\n" +
               $"  Total nodes:             {TotalNodeCount}\n" +
               $"  Total images:            {TotalImageCount}\n" +
               $"  Max depth:               {MaxDepth}\n" +
               $"  Leaf nodes:              {allLeaves.Count}\n" +
               $"  Leaves with images:      {leavesWithImages}\n" +
               $"  Leaves without images:   {leavesWithoutImages}\n" +
               $"===========================";
    }
}
