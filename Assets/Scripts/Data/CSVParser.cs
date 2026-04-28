using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Parses the butterfly cluster CSV file into a list of CSVRow objects.
/// 
/// Handles:
///   - Header row (skipped)
///   - Windows line endings (\r\n)
///   - Empty trailing lines
///   - Missing/empty optional fields (depth, node_id, image_id)
///   - Basic validation with Debug.LogWarning for malformed rows
///
/// Usage:
///   string csvText = File.ReadAllText(path);
///   List<CSVRow> rows = CSVParser.Parse(csvText);
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
    // 9: image_id          (10-column format: unity_pruned_density_tree_3d.csv)
    //  — OR —
    // 9: r, 10: g, 11: b, 12: image_id  (13-column format: ..._colors.csv)
    //
    // image_id is always the LAST column in either format.
    // -----------------------------------------------------------

    private const int MIN_COLUMN_COUNT = 10;

    /// <summary>
    /// Parses raw CSV text into a list of CSVRow objects.
    /// Skips the header row and any empty lines.
    /// </summary>
    /// <param name="csvText">The full text content of the CSV file.</param>
    /// <returns>List of parsed CSVRow objects (both node and image rows).</returns>
    public static List<CSVRow> Parse(string csvText)
    {
        var rows = new List<CSVRow>();

        if (string.IsNullOrEmpty(csvText))
        {
            Debug.LogWarning("[CSVParser] CSV text is null or empty.");
            return rows;
        }

        // Split into lines, handling both \r\n and \n
        string[] lines = csvText.Split('\n');

        // Track statistics for validation summary
        int nodeCount = 0;
        int imageCount = 0;
        int skippedCount = 0;
        int errorCount = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            // Strip carriage returns (Windows line endings)
            string line = lines[i].Trim('\r').Trim();

            // Skip empty lines
            if (string.IsNullOrEmpty(line))
                continue;

            // Skip header row (first non-empty line starting with "type")
            if (i == 0 && line.StartsWith("type"))
            {
                // Validate that the header has the expected columns
                string[] headerCols = line.Split(',');
                if (headerCols.Length < MIN_COLUMN_COUNT)
                {
                    Debug.LogWarning($"[CSVParser] Header has {headerCols.Length} columns, expected at least {MIN_COLUMN_COUNT}. " +
                                     $"Header: {line}");
                }
                skippedCount++;
                continue;
            }

            // Parse the data row
            CSVRow row = ParseRow(line, i + 1); // i+1 for 1-based line numbers
            if (row != null)
            {
                rows.Add(row);
                if (row.IsNode) nodeCount++;
                else if (row.IsImage) imageCount++;
            }
            else
            {
                errorCount++;
            }
        }

        // Log parsing summary
        Debug.Log($"[CSVParser] Parsing complete: {rows.Count} data rows " +
                  $"({nodeCount} nodes, {imageCount} images). " +
                  $"Skipped: {skippedCount}. Errors: {errorCount}.");

        return rows;
    }

    /// <summary>
    /// Parses a single CSV line into a CSVRow object.
    /// Returns null if the line is malformed.
    /// </summary>
    private static CSVRow ParseRow(string line, int lineNumber)
    {
        string[] fields = line.Split(',');

        // Validate minimum column count
        if (fields.Length < MIN_COLUMN_COUNT)
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Expected at least {MIN_COLUMN_COUNT} fields, " +
                             $"got {fields.Length}. Skipping. Content: \"{line}\"");
            return null;
        }

        // Trim all fields
        for (int i = 0; i < fields.Length; i++)
        {
            fields[i] = fields[i].Trim();
        }

        string type = fields[0];

        // Validate type field
        if (type != "node" && type != "image")
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Unknown type \"{type}\". Skipping.");
            return null;
        }

        // Validate required fields
        string parentId = fields[2];
        if (string.IsNullOrEmpty(parentId))
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Missing parent_id. Skipping.");
            return null;
        }

        // Parse planet_id (required, integer)
        if (!int.TryParse(fields[3], out int planetIndex))
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Invalid planet_id \"{fields[3]}\". Skipping.");
            return null;
        }

        // Parse depth (optional — only set for planet-level nodes)
        int depth = -1; // -1 means "not provided"
        if (!string.IsNullOrEmpty(fields[4]))
        {
            if (float.TryParse(fields[4], System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float depthFloat))
            {
                depth = Mathf.RoundToInt(depthFloat);
            }
            else
            {
                Debug.LogWarning($"[CSVParser] Line {lineNumber}: Invalid depth \"{fields[4]}\". Using -1.");
            }
        }

        // Parse size (required, integer)
        int size = 0;
        if (!string.IsNullOrEmpty(fields[5]))
        {
            if (!int.TryParse(fields[5], out size))
            {
                Debug.LogWarning($"[CSVParser] Line {lineNumber}: Invalid size \"{fields[5]}\". Using 0.");
                size = 0;
            }
        }

        // Parse x, y, z coordinates (required, float)
        if (!float.TryParse(fields[6], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float x))
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Invalid x \"{fields[6]}\". Using 0.");
            x = 0f;
        }
        if (!float.TryParse(fields[7], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float y))
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Invalid y \"{fields[7]}\". Using 0.");
            y = 0f;
        }
        if (!float.TryParse(fields[8], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float z))
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Invalid z \"{fields[8]}\". Using 0.");
            z = 0f;
        }

        // Build the CSVRow
        CSVRow row = new CSVRow
        {
            Type = type,
            NodeId = fields[1],       // empty string for image rows
            ParentId = parentId,
            PlanetIndex = planetIndex,
            Depth = depth,
            Size = size,
            X = x,
            Y = y,
            Z = z,
            ImageId = fields[fields.Length - 1]  // always the last column (index 9 or 12)
        };

        // Type-specific validation
        if (row.IsNode && string.IsNullOrEmpty(row.NodeId))
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Node row has empty node_id. Skipping.");
            return null;
        }
        if (row.IsImage && string.IsNullOrEmpty(row.ImageId))
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Image row has empty image_id. Skipping.");
            return null;
        }

        return row;
    }

    /// <summary>
    /// Convenience method: loads and parses a CSV file from disk.
    /// Uses Application.streamingAssetsPath as the base path.
    /// </summary>
    /// <param name="relativePath">
    /// Path relative to StreamingAssets, e.g. "Data/unity_pruned_density_tree_3d.csv"
    /// </param>
    /// <returns>List of parsed CSVRow objects, or empty list on failure.</returns>
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
