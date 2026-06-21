using UnityEngine;

[System.Serializable]
public class CSVRow
{

    public string Type;

    public string NodeId;

    public string ParentId;

    public int PlanetIndex;

    public int Depth;

    public int Size;


    public float X;


    public float Y;


    public float Z;


    public float R;
    public float G;
    public float B;

    public string ImageId;

    public bool IsNode => Type == "node";


    public bool IsImage => Type == "image";


    public Vector3 GetPosition() => new Vector3(X, Y, Z);

    public override string ToString()
    {
        if (IsNode)
            return $"[Node] {NodeId} parent={ParentId} planet={PlanetIndex} size={Size} pos=({X:F2},{Y:F2},{Z:F2})";
        else
            return $"[Image] {ImageId} parent={ParentId} planet={PlanetIndex} pos=({X:F2},{Y:F2},{Z:F2})";
    }
}
