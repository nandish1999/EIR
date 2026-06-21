using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ClusterNode
{

    public string NodeId;

    public string ParentId;

    public int PlanetIndex;

    public int Depth;

    public ClusterNode Parent;

    public List<ClusterNode> Children;

    public List<ImageItem> Images;

    public int Size;

    public Vector3 Position;

    public Color RepresentativeColor = Color.white;

    [System.NonSerialized]
    public float RepresentativeDistance = float.MaxValue;

    [System.NonSerialized]
    public bool HasRepresentativeColor = false;

    [System.NonSerialized]
    public bool IsExpanded = false;

    [System.NonSerialized]
    public List<GameObject> SpawnedChildObjects = new List<GameObject>();

    public bool IsLeaf => Children == null || Children.Count == 0;


    public bool IsPlanet => ParentId == "root";


    public bool HasImages => Images != null && Images.Count > 0;

    public int ActualImageCount => Images != null ? Images.Count : 0;


    public int ChildCount => Children != null ? Children.Count : 0;

    public bool IsPrunedLeaf => IsLeaf && Size > 0 && !HasImages;

    public ClusterNode()
    {
        Children = new List<ClusterNode>();
        Images = new List<ImageItem>();
    }

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
