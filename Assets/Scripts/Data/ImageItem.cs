using UnityEngine;

/// <summary>
/// Represents a single butterfly image attached to a leaf cluster node.
/// </summary>
[System.Serializable]
public class ImageItem
{
    // -----------------------------------------------------------
    // Identity
    // -----------------------------------------------------------

    public string ImageFileName;

    public string ParentNodeId;

    public int PlanetIndex;

    // -----------------------------------------------------------
    // Spatial data
    // -----------------------------------------------------------

    public Vector3 Position;

    // -----------------------------------------------------------
    // Representative image color
    // -----------------------------------------------------------

    public Color ImageColor;

    // -----------------------------------------------------------
    // Runtime reference
    // -----------------------------------------------------------

    public ClusterNode ParentNode;

    // -----------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------

    public Vector3 GetLocalPosition()
    {
        if (ParentNode != null)
            return Position - ParentNode.Position;

        return Position;
    }

    public override string ToString()
    {
        return $"[Image] {ImageFileName} parent={ParentNodeId} " +
               $"planet={PlanetIndex} pos=({Position.x:F2},{Position.y:F2},{Position.z:F2})";
    }
}