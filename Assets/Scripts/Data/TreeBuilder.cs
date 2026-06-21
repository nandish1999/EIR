using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class TreeBuilder
{

    public static ClusterTree Build(List<CSVRow> rows)
    {
        if (rows == null || rows.Count == 0)
        {
            Debug.LogError("[TreeBuilder] No rows provided. Returning empty tree.");
            return new ClusterTree();
        }

        var tree = new ClusterTree();

        int duplicateCount = 0;

        foreach (var row in rows)
        {
            if (!row.IsNode) continue;


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
                Depth = -1,


                RepresentativeColor = new Color(row.R, row.G, row.B, 1f)
            };

            tree.NodeLookup[node.NodeId] = node;
        }

        Debug.Log($"[TreeBuilder] Pass 1: Created {tree.NodeLookup.Count} cluster nodes. " +
                  $"Duplicates skipped: {duplicateCount}.");

        int orphanNodeCount = 0;

        foreach (var node in tree.NodeLookup.Values)
        {
            if (node.ParentId == "root")
            {

                tree.Planets.Add(node);
            }
            else if (tree.NodeLookup.TryGetValue(node.ParentId, out ClusterNode parent))
            {

                node.Parent = parent;
                parent.Children.Add(node);
            }
            else
            {

                Debug.LogWarning($"[TreeBuilder] Orphan node: \"{node.NodeId}\" references " +
                                 $"missing parent \"{node.ParentId}\".");
                orphanNodeCount++;
            }
        }


        tree.Planets.Sort((a, b) => a.PlanetIndex.CompareTo(b.PlanetIndex));

        Debug.Log($"[TreeBuilder] Pass 2: Linked hierarchy. " +
                  $"Planets: {tree.Planets.Count}. Orphan nodes: {orphanNodeCount}.");

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
                Position = row.GetPosition(),

            };

            if (tree.NodeLookup.TryGetValue(row.ParentId, out ClusterNode parentNode))
            {
                image.ParentNode = parentNode;
                parentNode.Images.Add(image);

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

        tree.MaxDepth = 0;

        foreach (var planet in tree.Planets)
        {
            ComputeDepthRecursive(planet, 0, ref tree.MaxDepth);
        }

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

        RunValidation(tree);

        return tree;
    }

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

    private static void RunValidation(ClusterTree tree)
    {
        Debug.Log("[TreeBuilder] ===== VALIDATION REPORT =====");


        Debug.Log($"[TreeBuilder] Total nodes: {tree.TotalNodeCount}");
        Debug.Log($"[TreeBuilder] Total images: {tree.TotalImageCount}");
        Debug.Log($"[TreeBuilder] Planets: {tree.Planets.Count}");
        Debug.Log($"[TreeBuilder] Max depth: {tree.MaxDepth}");


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
