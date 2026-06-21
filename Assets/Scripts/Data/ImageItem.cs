using UnityEngine;

[System.Serializable]
public class ImageItem
{

    public string ImageFileName;

    public string ParentNodeId;

    public int PlanetIndex;

    public Vector3 Position;

    public Color ImageColor;

    public ClusterNode ParentNode;

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