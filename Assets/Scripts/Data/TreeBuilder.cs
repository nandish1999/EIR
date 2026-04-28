using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Constructs a fully linked ClusterTree from a list of parsed CSVRows.
/// 
/// The build process has 5 passes:
///   1. Create all ClusterNode objects and index them by node_id
///   2. Link parent-child relationships
///   3. Attach image items to their parent leaf nodes
///   4. Compute depth for every node via recursive traversal
///   5. Run integrity validation and log results
///
/// Usage:
///   List<CSVRow> rows = CSVParser.Parse(csvText);
///   ClusterTree tree = TreeBuilder.Build(rows);
/// </summary>
public static class TreeBuilder
{
    /// <summary>
    /// Builds a ClusterTree from parsed CSV rows.
    /// This is the main entry point for the hierarchy construction pipeline.
    /// </summary>
    /// <param name="rows">Parsed CSV rows from CSVParser.</param>
    /// <returns>A fully linked and validated ClusterTree.</returns>
    public static ClusterTree Build(List<CSVRow> rows)
    {
        if (rows == null || rows.Count == 0)
        {
            Debug.LogError("[TreeBuilder] No rows provided. Returning empty tree.");
            return new ClusterTree();
        }

        var tree = new ClusterTree();

        // -----------------------------------------------------------
        // Pass 1: Create ClusterNode objects for all "node" rows
        // -----------------------------------------------------------
        int duplicateCount = 0;

        foreach (var row in rows)
        {
            if (!row.IsNode) continue;

            // Check for duplicate node IDs
            if (tree.NodeLookup.ContainsKey(row.NodeId))
            {
                Debug.LogWarning($"[TreeBuilder] Duplicate node_id \"{row.NodeId}\". " +
                                 $"Keeping first occurrence, skipping duplicate.");
                duplicateCount++;
                continue;
            }

            var node = new ClusterNode
            {
                NodeId = row.NodeId,
                ParentId = row.ParentId,
                PlanetIndex = row.PlanetIndex,
                Size = row.Size,
                Position = row.GetPosition(),
                Depth = -1 // will be computed in Pass 4
            };

            tree.NodeLookup[node.NodeId] = node;
        }

        Debug.Log($"[TreeBuilder] Pass 1: Created {tree.NodeLookup.Count} cluster nodes. " +
                  $"Duplicates skipped: {duplicateCount}.");

        // -----------------------------------------------------------
        // Pass 2: Link parent-child relationships
        // -----------------------------------------------------------
        int orphanNodeCount = 0;

        foreach (var node in tree.NodeLookup.Values)
        {
            if (node.ParentId == "root")
            {
                // This is a top-level planet node
                tree.Planets.Add(node);
            }
            else if (tree.NodeLookup.TryGetValue(node.ParentId, out ClusterNode parent))
            {
                // Link to parent
                node.Parent = parent;
                parent.Children.Add(node);
            }
            else
            {
                // Parent not found — orphan node
                Debug.LogWarning($"[TreeBuilder] Orphan node: \"{node.NodeId}\" references " +
                                 $"missing parent \"{node.ParentId}\".");
                orphanNodeCount++;
            }
        }

        // Sort planets by PlanetIndex for consistent ordering
        tree.Planets.Sort((a, b) => a.PlanetIndex.CompareTo(b.PlanetIndex));

        Debug.Log($"[TreeBuilder] Pass 2: Linked hierarchy. " +
                  $"Planets: {tree.Planets.Count}. Orphan nodes: {orphanNodeCount}.");

        // -----------------------------------------------------------
        // Pass 3: Attach image items to their parent nodes
        // -----------------------------------------------------------
        int orphanImageCount = 0;
        int imagesOnNonLeaf = 0;

        foreach (var row in rows)
        {
            if (!row.IsImage) continue;

            var image = new ImageItem
            {
                ImageFileName = row.ImageId,
                ParentNodeId = row.ParentId,
                PlanetIndex = row.PlanetIndex,
                Position = row.GetPosition()
            };

            if (tree.NodeLookup.TryGetValue(row.ParentId, out ClusterNode parentNode))
            {
                image.ParentNode = parentNode;
                parentNode.Images.Add(image);

                // Check if this image is attached to a non-leaf node
                // (shouldn't happen based on CSV analysis, but validate anyway)
                if (!parentNode.IsLeaf)
                {
                    imagesOnNonLeaf++;
                }
            }
            else
            {
                Debug.LogWarning($"[TreeBuilder] Orphan image: \"{row.ImageId}\" references " +
                                 $"missing parent node \"{row.ParentId}\".");
                orphanImageCount++;
            }

            tree.AllImages.Add(image);
        }

        if (imagesOnNonLeaf > 0)
        {
            Debug.LogWarning($"[TreeBuilder] {imagesOnNonLeaf} image(s) attached to non-leaf nodes. " +
                             $"This is unexpected based on CSV analysis.");
        }

        Debug.Log($"[TreeBuilder] Pass 3: Attached {tree.AllImages.Count} images. " +
                  $"Orphan images: {orphanImageCount}. On non-leaf: {imagesOnNonLeaf}.");

        // -----------------------------------------------------------
        // Pass 4: Compute depth for every node (recursive from planets)
        // -----------------------------------------------------------
        tree.MaxDepth = 0;

        foreach (var planet in tree.Planets)
        {
            ComputeDepthRecursive(planet, 0, ref tree.MaxDepth);
        }

        // Warn about any nodes that didn't get a depth assigned
        // (would indicate disconnected nodes not reachable from any planet)
        int unreachableCount = 0;
        foreach (var node in tree.NodeLookup.Values)
        {
            if (node.Depth < 0)
            {
                Debug.LogWarning($"[TreeBuilder] Node \"{node.NodeId}\" is unreachable from any planet root. " +
                                 $"Depth remains uncomputed.");
                unreachableCount++;
            }
        }

        Debug.Log($"[TreeBuilder] Pass 4: Computed depths. Max depth: {tree.MaxDepth}. " +
                  $"Unreachable nodes: {unreachableCount}.");

        // -----------------------------------------------------------
        // Pass 5: Validation summary
        // -----------------------------------------------------------
        RunValidation(tree);

        return tree;
    }

    /// <summary>
    /// Recursively computes and sets the depth of each node in the tree.
    /// </summary>
    private static void ComputeDepthRecursive(ClusterNode node, int depth, ref int maxDepth)
    {
        node.Depth = depth;
        if (depth > maxDepth)
        {
            maxDepth = depth;
        }

        foreach (var child in node.Children)
        {
            ComputeDepthRecursive(child, depth + 1, ref maxDepth);
        }
    }

    /// <summary>
    /// Runs post-build validation checks and logs a comprehensive summary.
    /// This does NOT modify the tree — it only reports findings.
    /// </summary>
    private static void RunValidation(ClusterTree tree)
    {
        Debug.Log("[TreeBuilder] ===== VALIDATION REPORT =====");

        // --- Basic counts ---
        Debug.Log($"[TreeBuilder] Total nodes: {tree.TotalNodeCount}");
        Debug.Log($"[TreeBuilder] Total images: {tree.TotalImageCount}");
        Debug.Log($"[TreeBuilder] Planets: {tree.Planets.Count}");
        Debug.Log($"[TreeBuilder] Max depth: {tree.MaxDepth}");

        // --- Planet details ---
        foreach (var planet in tree.Planets)
        {
            int subtreeImages = planet.GetSubtreeImageCount();
            var leaves = planet.GetLeafNodes();
            int leavesWithImages = leaves.Count(l => l.HasImages);

            Debug.Log($"[TreeBuilder] Planet \"{planet.NodeId}\": " +
                      $"size={planet.Size}, children={planet.ChildCount}, " +
                      $"subtreeImages={subtreeImages}, " +
                      $"leaves={leaves.Count} (with images: {leavesWithImages})");
        }

        // --- Leaf analysis ---
        var allLeaves = tree.GetAllLeafNodes();
        var prunedLeaves = tree.GetPrunedLeafNodes();

        Debug.Log($"[TreeBuilder] Total leaf nodes: {allLeaves.Count}");
        Debug.Log($"[TreeBuilder] Leaves with images: {allLeaves.Count - prunedLeaves.Count}");
        Debug.Log($"[TreeBuilder] Pruned leaves (size>0 but no images): {prunedLeaves.Count}");

        if (prunedLeaves.Count > 0)
        {
            Debug.Log("[TreeBuilder] Pruned leaf details:");
            foreach (var leaf in prunedLeaves)
            {
                Debug.Log($"[TreeBuilder]   - \"{leaf.NodeId}\" (planet {leaf.PlanetIndex}, " +
                          $"size={leaf.Size}, depth={leaf.Depth})");
            }
        }

        // --- Single-child intermediate nodes ---
        int singleChildCount = 0;
        foreach (var node in tree.NodeLookup.Values)
        {
            if (!node.IsLeaf && node.ChildCount == 1)
            {
                singleChildCount++;
            }
        }
        if (singleChildCount > 0)
        {
            Debug.Log($"[TreeBuilder] Nodes with exactly 1 child (potential trivial drill-downs): " +
                      $"{singleChildCount}");
        }

        Debug.Log("[TreeBuilder] ===== END VALIDATION =====");
    }
}
