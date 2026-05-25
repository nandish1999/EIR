using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Parses the butterfly cluster CSV file into a list of CSVRow objects.
/// </summary>
public static class CSVParser
{
    // -----------------------------------------------------------
    // Expected CSV column layout (0-indexed)
    // -----------------------------------------------------------
    // 0: type
    // 1: node_id
    // 2: parent_id
    // 3: planet_id
    // 4: depth
    // 5: size
    // 6: x
    // 7: y
    // 8: z
    // 9: image_id
    //
    // OR:
    //
    // 9: r
    // 10: g
    // 11: b
    // 12: image_id
    // -----------------------------------------------------------

    private const int MIN_COLUMN_COUNT = 10;

    /// <summary>
    /// Parses raw CSV text into a list of CSVRow objects.
    /// </summary>
    public static List<CSVRow> Parse(string csvText)
    {
        var rows = new List<CSVRow>();

        if (string.IsNullOrEmpty(csvText))
        {
            Debug.LogWarning("[CSVParser] CSV text is null or empty.");
            return rows;
        }

        string[] lines = csvText.Split('\n');

        int nodeCount = 0;
        int imageCount = 0;
        int skippedCount = 0;
        int errorCount = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim('\r').Trim();

            if (string.IsNullOrEmpty(line))
                continue;

            // Skip header
            if (i == 0 && line.StartsWith("type"))
            {
                skippedCount++;
                continue;
            }

            CSVRow row = ParseRow(line, i + 1);

            if (row != null)
            {
                rows.Add(row);

                if (row.IsNode)
                    nodeCount++;
                else if (row.IsImage)
                    imageCount++;
            }
            else
            {
                errorCount++;
            }
        }

        Debug.Log($"[CSVParser] Parsing complete: {rows.Count} data rows " +
                  $"({nodeCount} nodes, {imageCount} images). " +
                  $"Skipped: {skippedCount}. Errors: {errorCount}.");

        return rows;
    }

    /// <summary>
    /// Parses a single CSV line into a CSVRow object.
    /// </summary>
    private static CSVRow ParseRow(string line, int lineNumber)
    {
        string[] fields = line.Split(',');

        // Validate minimum column count
        if (fields.Length < MIN_COLUMN_COUNT)
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Expected at least {MIN_COLUMN_COUNT} fields.");
            return null;
        }

        // Trim all fields
        for (int i = 0; i < fields.Length; i++)
        {
            fields[i] = fields[i].Trim();
        }

        string type = fields[0];

        // Validate type
        if (type != "node" && type != "image")
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Unknown type \"{type}\".");
            return null;
        }

        // Parent ID
        string parentId = fields[2];

        if (string.IsNullOrEmpty(parentId))
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Missing parent_id.");
            return null;
        }

        // Planet index
        if (!int.TryParse(fields[3], out int planetIndex))
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Invalid planet_id.");
            return null;
        }

        // Depth
        int depth = -1;

        if (!string.IsNullOrEmpty(fields[4]))
        {
            if (float.TryParse(
                fields[4],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float depthFloat))
            {
                depth = Mathf.RoundToInt(depthFloat);
            }
        }

        // Size
        int size = 0;

        if (!string.IsNullOrEmpty(fields[5]))
        {
            int.TryParse(fields[5], out size);
        }

        // Coordinates
        float x = 0f;
        float y = 0f;
        float z = 0f;

        float.TryParse(
            fields[6],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out x);

        float.TryParse(
            fields[7],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out y);

        float.TryParse(
            fields[8],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out z);

        // -----------------------------------------------------------
        // RGB parsing (for *_colors.csv)
        // -----------------------------------------------------------

        float r = 1f;
        float g = 1f;
        float b = 1f;

        if (fields.Length >= 13)
        {
            float.TryParse(
                fields[9],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out r);

            float.TryParse(
                fields[10],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out g);

            float.TryParse(
                fields[11],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out b);
        }

        // -----------------------------------------------------------
        // Build CSVRow
        // -----------------------------------------------------------

        CSVRow row = new CSVRow
        {
            Type = type,

            NodeId = fields[1],

            ParentId = parentId,

            PlanetIndex = planetIndex,

            Depth = depth,

            Size = size,

            X = x,
            Y = y,
            Z = z,

            // RGB values
            R = r,
            G = g,
            B = b,

            // image filename is always last column
            ImageId = fields[fields.Length - 1]
        };

        // Validation
        if (row.IsNode && string.IsNullOrEmpty(row.NodeId))
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Empty node_id.");
            return null;
        }

        if (row.IsImage && string.IsNullOrEmpty(row.ImageId))
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Empty image_id.");
            return null;
        }

        return row;
    }

    /// <summary>
    /// Loads and parses a CSV file from StreamingAssets.
    /// </summary>
    public static List<CSVRow> ParseFromStreamingAssets(string relativePath)
    {
        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);

        if (!File.Exists(fullPath))
        {
            Debug.LogError($"[CSVParser] File not found: {fullPath}");
            return new List<CSVRow>();
        }

        Debug.Log($"[CSVParser] Loading CSV from: {fullPath}");

        string csvText = File.ReadAllText(fullPath);

        return Parse(csvText);
    }
}