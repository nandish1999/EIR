using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class CSVParser
{

    private const int MIN_COLUMN_COUNT = 10;

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

    private static CSVRow ParseRow(string line, int lineNumber)
    {
        string[] fields = line.Split(',');


        if (fields.Length < MIN_COLUMN_COUNT)
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Expected at least {MIN_COLUMN_COUNT} fields.");
            return null;
        }


        for (int i = 0; i < fields.Length; i++)
        {
            fields[i] = fields[i].Trim();
        }

        string type = fields[0];


        if (type != "node" && type != "image")
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Unknown type \"{type}\".");
            return null;
        }


        string parentId = fields[2];

        if (string.IsNullOrEmpty(parentId))
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Missing parent_id.");
            return null;
        }


        if (!int.TryParse(fields[3], out int planetIndex))
        {
            Debug.LogWarning($"[CSVParser] Line {lineNumber}: Invalid planet_id.");
            return null;
        }


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


        int size = 0;

        if (!string.IsNullOrEmpty(fields[5]))
        {
            int.TryParse(fields[5], out size);
        }


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


            R = r,
            G = g,
            B = b,


            ImageId = fields[fields.Length - 1]
        };


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