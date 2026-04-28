using UnityEngine;

/// <summary>
/// Represents a single butterfly image attached to a leaf cluster node.
/// 
/// Image items are always children of leaf-level ClusterNodes.
/// They carry their own UMAP 3D position (individual position in
/// embedding space) and a filename for loading the actual texture.
/// </summary>
[System.Serializable]
public class ImageItem
{
    // -----------------------------------------------------------
    // Identity
    // -----------------------------------------------------------

    /// <summary>
    /// The butterfly image filename (e.g. "10720.jpg").
    /// Used to load the actual texture from StreamingAssets/Images/.
    /// </summary>
    public string ImageFileName;

    /// <summary>
    /// The node_id of the parent leaf cluster node this image belongs to.
    /// </summary>
    public string ParentNodeId;

    /// <summary>
    /// Index of the top-level planet this image belongs to (0, 1, or 2).
    /// </summary>
    public int PlanetIndex;

    // -----------------------------------------------------------
    // Spatial data
    // -----------------------------------------------------------

    /// <summary>
    /// Individual UMAP 3D position of this image in embedding space.
    /// Can be used to scatter images in 3D around their parent node.
    /// </summary>
    public Vector3 Position;

    // -----------------------------------------------------------
    // Runtime reference (set by TreeBuilder)
    // -----------------------------------------------------------

    /// <summary>
    /// Direct reference to the parent ClusterNode.
    /// Set during tree construction by TreeBuilder.
    /// </summary>
    public ClusterNode ParentNode;

    // -----------------------------------------------------------
    // Convenience
    // -----------------------------------------------------------

    /// <summary>
    /// Returns the position relative to the parent node's position.
    /// Useful for laying out images around their parent cluster.
    /// </summary>
    public Vector3 GetLocalPosition()
    {
        if (ParentNode != null)
            return Position - ParentNode.Position;
        return Position;
    }

    public override string ToString()
    {
        return $"[Image] {ImageFileName} parent={ParentNodeId} planet={PlanetIndex} " +
               $"pos=({Position.x:F2},{Position.y:F2},{Position.z:F2})";
    }
}
