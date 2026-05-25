using UnityEngine;

/// <summary>
/// Represents a single raw parsed row from the CSV file.
/// This is an intermediate data object — it holds the raw values
/// before they are organized into the tree hierarchy.
/// Both "node" and "image" rows are parsed into this same struct.
/// </summary>
[System.Serializable]
public class CSVRow
{
    // -----------------------------------------------------------
    // Fields directly mapped from CSV columns
    // -----------------------------------------------------------

    /// <summary>Row type discriminator: "node" or "image".</summary>
    public string Type;

    /// <summary>
    /// Unique identifier for cluster nodes (e.g. "planet_0", "0_node_617").
    /// Empty for image rows.
    /// </summary>
    public string NodeId;

    /// <summary>
    /// The node_id of this row's parent.
    /// Planets have "root". Images reference their owning leaf node.
    /// </summary>
    public string ParentId;

    /// <summary>
    /// Index of the top-level planet this row belongs to (0, 1, or 2).
    /// Acts as a partition key.
    /// </summary>
    public int PlanetIndex;

    /// <summary>
    /// Depth in the hierarchy. Only set to 0.0 for planet-level nodes.
    /// -1 indicates "not provided" (the vast majority of rows).
    /// Unity computes actual depth at runtime via TreeBuilder.
    /// </summary>
    public int Depth;

    /// <summary>
    /// For nodes: total image count in the subtree (from the original un-pruned tree).
    /// For images: always 1.
    /// </summary>
    public int Size;

    /// <summary>UMAP 3D x-coordinate.</summary>
    public float X;

    /// <summary>UMAP 3D y-coordinate.</summary>
    public float Y;

    /// <summary>UMAP 3D z-coordinate.</summary>
    public float Z;

    /// <summary>Representative image color (parsed from CSV).</summary>
    public float R;
    public float G;
    public float B;

    /// <summary>
    /// Butterfly image filename (e.g. "10720.jpg").
    /// Empty for node rows.
    /// </summary>
    public string ImageId;

    // -----------------------------------------------------------
    // Convenience helpers
    // -----------------------------------------------------------

    /// <summary>True if this row represents a cluster node.</summary>
    public bool IsNode => Type == "node";

    /// <summary>True if this row represents an image item.</summary>
    public bool IsImage => Type == "image";

    /// <summary>Returns the UMAP position as a Unity Vector3.</summary>
    public Vector3 GetPosition() => new Vector3(X, Y, Z);

    public override string ToString()
    {
        if (IsNode)
            return $"[Node] {NodeId} parent={ParentId} planet={PlanetIndex} size={Size} pos=({X:F2},{Y:F2},{Z:F2})";
        else
            return $"[Image] {ImageId} parent={ParentId} planet={PlanetIndex} pos=({X:F2},{Y:F2},{Z:F2})";
    }
}
