using System.Collections.Generic;
using UnityEngine;

public class ClusterTree
{

    public List<ClusterNode> Planets;

    public Dictionary<string, ClusterNode> NodeLookup;

    public List<ImageItem> AllImages;

    public int MaxDepth;


    public int TotalNodeCount => NodeLookup != null ? NodeLookup.Count : 0;


    public int TotalImageCount => AllImages != null ? AllImages.Count : 0;

    public ClusterTree()
    {
        Planets = new List<ClusterNode>();
        NodeLookup = new Dictionary<string, ClusterNode>();
        AllImages = new List<ImageItem>();
        MaxDepth = 0;
    }

    public ClusterNode GetNode(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return null;
        NodeLookup.TryGetValue(nodeId, out ClusterNode node);
        return node;
    }

    public List<ClusterNode> GetAllLeafNodes()
    {
        var leaves = new List<ClusterNode>();
        foreach (var planet in Planets)
        {
            leaves.AddRange(planet.GetLeafNodes());
        }
        return leaves;
    }

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
