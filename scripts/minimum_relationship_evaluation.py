#!/usr/bin/env python3
"""
Minimum Relationship Preservation Evaluation
=============================================
Compares BEFORE (raw UMAP CSV positions) vs AFTER (centroid-normalised
expansion positions) for the ButterflyClusterViz project.

BEFORE = entity.Position × positionScale  (raw CSV coords)
AFTER  = positions produced by the centroid/depth/effectiveRadius
         transformation logic from VisualizationManager.SpawnChildren()
         and SpawnImages(), simulated via a full top-down expansion.

This script reads the CSV directly, replicates the Unity transformation
logic in Python, and computes the minimum evaluation metrics.

Usage:
    python3 scripts/minimum_relationship_evaluation.py

Output:
    Prints results to stdout.
    Also writes results to minimum_evaluation_results.json for reproducibility.
"""

import csv
import json
import math
import os
import sys
from collections import defaultdict

import numpy as np
from scipy.spatial.distance import cdist

# ---------------------------------------------------------------------------
# Configuration — must match Unity Inspector defaults
# ---------------------------------------------------------------------------
POSITION_SCALE = 1.0
BASE_EXPANSION_RADIUS = 3.0
DEPTH_RADIUS_FACTOR = 0.65
MIN_EXPANSION_RADIUS = 0.3

# Path to the CSV (relative to project root)
CSV_PATH = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "StreamingAssets", "Data",
    "unity_pruned_density_tree_3d.csv"
)


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------
class Node:
    __slots__ = [
        "nid", "pid", "planet", "size", "pos",
        "children", "images", "depth", "parent", "world_pos"
    ]

    def __init__(self, nid, pid, planet, size, pos):
        self.nid = nid
        self.pid = pid
        self.planet = int(planet)
        self.size = int(size)
        self.pos = np.array(pos, dtype=np.float64)
        self.children = []
        self.images = []
        self.depth = 0
        self.parent = None
        self.world_pos = None


class Image:
    __slots__ = [
        "fname", "pid", "planet", "pos",
        "parent_node", "before_pos", "world_pos"
    ]

    def __init__(self, fname, pid, planet, pos):
        self.fname = fname
        self.pid = pid
        self.planet = int(planet)
        self.pos = np.array(pos, dtype=np.float64)
        self.parent_node = None
        self.before_pos = None
        self.world_pos = None


# ---------------------------------------------------------------------------
# Step 1: Parse CSV and build tree
# ---------------------------------------------------------------------------
def parse_and_build_tree(csv_path):
    """Parse the CSV and build the full tree with parent-child links."""
    with open(csv_path, newline="") as f:
        reader = csv.DictReader(f)
        rows = list(reader)

    # Build nodes
    all_nodes = {}
    for r in rows:
        if r["type"] != "node":
            continue
        nid = r["node_id"]
        if nid in all_nodes:
            continue  # skip duplicates (matches Unity logic)
        n = Node(
            nid=nid,
            pid=r["parent_id"],
            planet=r["planet_id"],
            size=r["size"],
            pos=[float(r["x"]), float(r["y"]), float(r["z"])],
        )
        all_nodes[nid] = n

    # Link parent-child
    planets = []
    for n in all_nodes.values():
        if n.pid == "root":
            planets.append(n)
        elif n.pid in all_nodes:
            n.parent = all_nodes[n.pid]
            all_nodes[n.pid].children.append(n)

    # Sort planets by planet index (matches Unity: tree.Planets.Sort)
    planets.sort(key=lambda p: p.planet)

    # Compute depths
    def set_depth(node, d):
        node.depth = d
        for c in node.children:
            set_depth(c, d + 1)

    for p in planets:
        set_depth(p, 0)

    # Attach images to leaf nodes
    all_images = []
    for r in rows:
        if r["type"] != "image":
            continue
        img = Image(
            fname=r["image_id"],
            pid=r["parent_id"],
            planet=r["planet_id"],
            pos=[float(r["x"]), float(r["y"]), float(r["z"])],
        )
        if img.pid in all_nodes:
            img.parent_node = all_nodes[img.pid]
            all_nodes[img.pid].images.append(img)
        all_images.append(img)

    return all_nodes, planets, all_images


# ---------------------------------------------------------------------------
# Step 2: Compute BEFORE positions
# ---------------------------------------------------------------------------
def compute_before_positions(all_images):
    """BEFORE = raw CSV position × positionScale."""
    for img in all_images:
        img.before_pos = img.pos * POSITION_SCALE


# ---------------------------------------------------------------------------
# Step 3: Simulate AFTER positions (full top-down expansion)
# ---------------------------------------------------------------------------
def effective_radius(depth):
    """Matches VisualizationManager lines 383-385 / 504-506."""
    return max(BASE_EXPANSION_RADIUS * (DEPTH_RADIUS_FACTOR ** depth),
               MIN_EXPANSION_RADIUS)


def simulate_expansion(planets):
    """
    Simulates the full top-down expansion, replicating the exact logic from:
    - SpawnChildren() lines 373-434 of VisualizationManager.cs
    - SpawnImages()   lines 475-563 of VisualizationManager.cs
    - SpawnNodeSphere() lines 441-466 (records world_pos in nodeWorldPositionMap)
    """
    # Planets are placed at their raw CSV positions (line 220 → SpawnNodeSphere
    # called without worldPosition override → line 446 uses node.Position × positionScale)
    for p in planets:
        p.world_pos = p.pos * POSITION_SCALE

    def expand_node(node):
        parent_wp = node.world_pos

        # --- Branch node: expand children ---
        if node.children:
            children = node.children
            er = effective_radius(node.depth)

            if len(children) == 1:
                # Single-child case: lines 388-401
                child = children[0]
                direction = child.pos * POSITION_SCALE - node.pos * POSITION_SCALE
                mag = np.linalg.norm(direction)
                if mag > 0.0001:
                    child.world_pos = parent_wp + (direction / mag) * er
                else:
                    child.world_pos = parent_wp + np.array([0.0, er, 0.0])
            else:
                # Multi-child case: lines 403-433
                centroid = np.mean([c.pos for c in children], axis=0) * POSITION_SCALE
                offsets = [c.pos * POSITION_SCALE - centroid for c in children]
                max_mag = max(np.linalg.norm(o) for o in offsets)

                for i, child in enumerate(children):
                    if max_mag > 0.0001:
                        norm_offset = (offsets[i] / max_mag) * er
                    else:
                        norm_offset = np.zeros(3)
                    child.world_pos = parent_wp + norm_offset

            # Recurse into children
            for child in children:
                expand_node(child)

        # --- Leaf node: expand images ---
        if node.images:
            imgs = node.images
            er = effective_radius(node.depth)
            centroid = np.mean([im.pos for im in imgs], axis=0) * POSITION_SCALE
            offsets = [im.pos * POSITION_SCALE - centroid for im in imgs]
            max_mag = max(np.linalg.norm(o) for o in offsets)

            for i, im in enumerate(imgs):
                if max_mag > 0.0001:
                    norm_offset = (offsets[i] / max_mag) * er
                else:
                    norm_offset = np.zeros(3)
                im.world_pos = parent_wp + norm_offset

    for p in planets:
        expand_node(p)


# ---------------------------------------------------------------------------
# Step 4: Compute metrics
# ---------------------------------------------------------------------------
def compute_metrics(all_images):
    """Compute the minimum evaluation metrics."""
    valid = [img for img in all_images if img.world_pos is not None]
    N = len(valid)

    before_pos = np.array([img.before_pos for img in valid])
    after_pos = np.array([img.world_pos for img in valid])
    parent_ids = np.array([img.pid for img in valid])
    planet_ids = np.array([img.planet for img in valid])

    print(f"Computing pairwise distances for {N} images...")
    D_before = cdist(before_pos, before_pos)
    D_after = cdist(after_pos, after_pos)

    # Pre-compute sorted neighbour indices for each image
    print("Computing neighbour rankings...")
    sorted_before = np.argsort(D_before, axis=1)
    sorted_after = np.argsort(D_after, axis=1)

    results = {
        "n_images": N,
        "parameters": {
            "positionScale": POSITION_SCALE,
            "baseExpansionRadius": BASE_EXPANSION_RADIUS,
            "depthRadiusFactor": DEPTH_RADIUS_FACTOR,
            "minExpansionRadius": MIN_EXPANSION_RADIUS,
        },
    }

    # --- Metric 1: k-NN Overlap ---
    print("Computing k-NN overlap...")
    for k in [5, 10]:
        overlaps = []
        for i in range(N):
            knn_b = set(sorted_before[i, 1:k + 1])
            knn_a = set(sorted_after[i, 1:k + 1])
            overlaps.append(len(knn_b & knn_a) / k)
        mean_ov = float(np.mean(overlaps))
        median_ov = float(np.median(overlaps))
        min_ov = float(np.min(overlaps))
        zero_count = sum(1 for o in overlaps if o == 0.0)
        results[f"knn_overlap_k{k}"] = {
            "mean": mean_ov,
            "median": median_ov,
            "min": min_ov,
            "zero_overlap_count": zero_count,
            "zero_overlap_pct": round(100.0 * zero_count / N, 2),
        }
        print(f"  k={k}: mean={mean_ov:.4f}, median={median_ov:.4f}, "
              f"min={min_ov:.4f}, zero_count={zero_count}")

    # --- Metric 2: Cross-leaf intrusion ratio ---
    print("Computing cross-leaf intrusion ratios...")
    for k in [5, 10]:
        cross_before = 0
        cross_after = 0
        total = 0
        for i in range(N):
            knn_b = sorted_before[i, 1:k + 1]
            knn_a = sorted_after[i, 1:k + 1]
            for j in knn_b:
                total += 1
                if parent_ids[i] != parent_ids[j]:
                    cross_before += 1
            for j in knn_a:
                if parent_ids[i] != parent_ids[j]:
                    cross_after += 1
        results[f"cross_leaf_intrusion_k{k}"] = {
            "before_count": int(cross_before),
            "after_count": int(cross_after),
            "total_pairs": int(total),
            "before_pct": round(100.0 * cross_before / total, 2),
            "after_pct": round(100.0 * cross_after / total, 2),
            "delta_pp": round(100.0 * (cross_after - cross_before) / total, 2),
        }
        print(f"  k={k}: before={100 * cross_before / total:.2f}%, "
              f"after={100 * cross_after / total:.2f}%, "
              f"delta={100 * (cross_after - cross_before) / total:+.2f}pp")

    # --- Metric 3: Cross-planet intrusion ratio ---
    print("Computing cross-planet intrusion ratios...")
    for k in [5, 10]:
        cross_before = 0
        cross_after = 0
        total = 0
        for i in range(N):
            knn_b = sorted_before[i, 1:k + 1]
            knn_a = sorted_after[i, 1:k + 1]
            for j in knn_b:
                total += 1
                if planet_ids[i] != planet_ids[j]:
                    cross_before += 1
            for j in knn_a:
                if planet_ids[i] != planet_ids[j]:
                    cross_after += 1
        results[f"cross_planet_intrusion_k{k}"] = {
            "before_count": int(cross_before),
            "after_count": int(cross_after),
            "total_pairs": int(total),
            "before_pct": round(100.0 * cross_before / total, 2),
            "after_pct": round(100.0 * cross_after / total, 2),
        }
        print(f"  k={k}: before={100 * cross_before / total:.2f}%, "
              f"after={100 * cross_after / total:.2f}%")

    # --- Metric 4: Intra-leaf vs cross-leaf neighbour preservation (k=5) ---
    print("Computing intra-leaf vs cross-leaf preservation (k=5)...")
    k = 5
    intra_preserved = 0
    intra_total = 0
    cross_preserved = 0
    cross_total = 0
    for i in range(N):
        knn_b = set(sorted_before[i, 1:k + 1])
        knn_a = set(sorted_after[i, 1:k + 1])
        for j in knn_b:
            if parent_ids[i] == parent_ids[j]:
                intra_total += 1
                if j in knn_a:
                    intra_preserved += 1
            else:
                cross_total += 1
                if j in knn_a:
                    cross_preserved += 1

    results["intra_cross_preservation_k5"] = {
        "intra_preserved": int(intra_preserved),
        "intra_total": int(intra_total),
        "intra_pct": round(100.0 * intra_preserved / intra_total, 2) if intra_total > 0 else 0.0,
        "cross_preserved": int(cross_preserved),
        "cross_total": int(cross_total),
        "cross_pct": round(100.0 * cross_preserved / cross_total, 2) if cross_total > 0 else 0.0,
    }
    print(f"  Intra-leaf: {intra_preserved}/{intra_total} "
          f"({100 * intra_preserved / intra_total:.1f}%)")
    print(f"  Cross-leaf: {cross_preserved}/{cross_total} "
          f"({100 * cross_preserved / cross_total:.1f}%)")

    return results


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
def main():
    print("=" * 60)
    print("MINIMUM RELATIONSHIP PRESERVATION EVALUATION")
    print("=" * 60)
    print()

    # Verify CSV exists
    if not os.path.exists(CSV_PATH):
        print(f"ERROR: CSV not found at {CSV_PATH}")
        sys.exit(1)
    print(f"CSV: {CSV_PATH}")
    print()

    # Step 1: Parse and build tree
    print("Step 1: Parsing CSV and building tree...")
    all_nodes, planets, all_images = parse_and_build_tree(CSV_PATH)
    n_nodes = len(all_nodes)
    n_images = len(all_images)
    n_planets = len(planets)
    print(f"  Nodes: {n_nodes}, Images: {n_images}, Planets: {n_planets}")

    # Step 2: Compute BEFORE positions
    print("\nStep 2: Computing BEFORE positions (raw CSV × positionScale)...")
    compute_before_positions(all_images)
    print(f"  positionScale = {POSITION_SCALE}")

    # Step 3: Simulate full expansion for AFTER positions
    print("\nStep 3: Simulating full top-down expansion for AFTER positions...")
    print(f"  baseExpansionRadius = {BASE_EXPANSION_RADIUS}")
    print(f"  depthRadiusFactor   = {DEPTH_RADIUS_FACTOR}")
    print(f"  minExpansionRadius  = {MIN_EXPANSION_RADIUS}")
    simulate_expansion(planets)
    valid_count = sum(1 for img in all_images if img.world_pos is not None)
    print(f"  Images with AFTER positions: {valid_count}/{n_images}")

    # Step 4: Compute metrics
    print("\nStep 4: Computing metrics...")
    print("-" * 40)
    results = compute_metrics(all_images)
    print("-" * 40)

    # Save JSON results for reproducibility
    output_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    json_path = os.path.join(output_dir, "scripts", "minimum_evaluation_results.json")
    with open(json_path, "w") as f:
        json.dump(results, f, indent=2)
    print(f"\nResults saved to: {json_path}")

    # Print summary table
    print("\n" + "=" * 60)
    print("SUMMARY TABLE")
    print("=" * 60)
    r = results
    print(f"{'Metric':<45s} {'k=5':>10s} {'k=10':>10s}")
    print("-" * 67)
    print(f"{'Mean k-NN overlap':<45s} "
          f"{r['knn_overlap_k5']['mean']:>10.4f} "
          f"{r['knn_overlap_k10']['mean']:>10.4f}")
    print(f"{'Cross-leaf intrusion BEFORE':<45s} "
          f"{r['cross_leaf_intrusion_k5']['before_pct']:>9.2f}% "
          f"{r['cross_leaf_intrusion_k10']['before_pct']:>9.2f}%")
    print(f"{'Cross-leaf intrusion AFTER':<45s} "
          f"{r['cross_leaf_intrusion_k5']['after_pct']:>9.2f}% "
          f"{r['cross_leaf_intrusion_k10']['after_pct']:>9.2f}%")
    print(f"{'Cross-leaf intrusion DELTA':<45s} "
          f"{r['cross_leaf_intrusion_k5']['delta_pp']:>+9.2f}pp "
          f"{r['cross_leaf_intrusion_k10']['delta_pp']:>+9.2f}pp")
    print(f"{'Cross-planet intrusion BEFORE':<45s} "
          f"{r['cross_planet_intrusion_k5']['before_pct']:>9.2f}% "
          f"{r['cross_planet_intrusion_k10']['before_pct']:>9.2f}%")
    print(f"{'Cross-planet intrusion AFTER':<45s} "
          f"{r['cross_planet_intrusion_k5']['after_pct']:>9.2f}% "
          f"{r['cross_planet_intrusion_k10']['after_pct']:>9.2f}%")
    print()
    p = r["intra_cross_preservation_k5"]
    print(f"{'Intra-leaf neighbour preservation (k=5)':<45s} "
          f"{p['intra_pct']:>9.2f}%")
    print(f"{'Cross-leaf neighbour preservation (k=5)':<45s} "
          f"{p['cross_pct']:>9.2f}%")
    print()
    z = r["knn_overlap_k5"]
    print(f"{'Images with ZERO overlap (k=5)':<45s} "
          f"{z['zero_overlap_count']:>5d} ({z['zero_overlap_pct']:.1f}%)")

    print("\n" + "=" * 60)
    print("EVALUATION COMPLETE")
    print("=" * 60)


if __name__ == "__main__":
    main()
