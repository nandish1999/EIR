#!/usr/bin/env python3
"""
Candidate K Readability Evaluation
===================================
Evaluates the VISUAL READABILITY (not fidelity) of the current Candidate K
implementation in ButterflyClusterViz.

Simulates the fully unified Candidate K global-anchor placement (including
single-child nodes) and computes practical readability metrics:
- image quad overlap
- parent proximity
- spacing distribution
- per-group readability breakdown
- overall readability score

Usage:
    python3 scripts/candidateK_readability_evaluation.py

Output:
    Prints results to stdout.
    Writes results to scripts/candidateK_readability_results.json.
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
# Configuration — must match CURRENT Unity Inspector defaults
# ---------------------------------------------------------------------------
POSITION_SCALE = 1.0
RADIUS_SCALE = 0.3
MIN_RADIUS = 0.3
DEPTH_RADIUS_FACTOR = 0.65
MIN_EFFECTIVE_RADIUS = 0.1
PLANET_SCALE = 5.0
IMAGE_QUAD_SIZE = 0.3       # max
MIN_IMAGE_QUAD_SIZE = 0.05  # min

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
        "children", "images", "depth", "parent", "world_pos", "sphere_radius"
    ]
    def __init__(self, nid, pid, planet, size, pos):
        self.nid = nid; self.pid = pid; self.planet = int(planet)
        self.size = int(size); self.pos = np.array(pos, dtype=np.float64)
        self.children = []; self.images = []; self.depth = 0
        self.parent = None; self.world_pos = None; self.sphere_radius = 0.0

class Image:
    __slots__ = ["fname", "pid", "planet", "pos", "parent_node", "world_pos"]
    def __init__(self, fname, pid, planet, pos):
        self.fname = fname; self.pid = pid; self.planet = int(planet)
        self.pos = np.array(pos, dtype=np.float64)
        self.parent_node = None; self.world_pos = None


# ---------------------------------------------------------------------------
# Parse CSV and build tree
# ---------------------------------------------------------------------------
def parse_and_build_tree(csv_path):
    with open(csv_path, newline="") as f:
        rows = list(csv.DictReader(f))

    all_nodes = {}
    for r in rows:
        if r["type"] != "node": continue
        nid = r["node_id"]
        if nid in all_nodes: continue
        all_nodes[nid] = Node(nid, r["parent_id"], r["planet_id"], r["size"],
                               [float(r["x"]), float(r["y"]), float(r["z"])])

    planets = []
    for n in all_nodes.values():
        if n.pid == "root": planets.append(n)
        elif n.pid in all_nodes:
            n.parent = all_nodes[n.pid]
            all_nodes[n.pid].children.append(n)
    planets.sort(key=lambda p: p.planet)

    def set_depth(node, d):
        node.depth = d
        for c in node.children: set_depth(c, d + 1)
    for p in planets: set_depth(p, 0)

    all_images = []
    for r in rows:
        if r["type"] != "image": continue
        img = Image(r["image_id"], r["parent_id"], r["planet_id"],
                    [float(r["x"]), float(r["y"]), float(r["z"])])
        if img.pid in all_nodes:
            img.parent_node = all_nodes[img.pid]
            all_nodes[img.pid].images.append(img)
        all_images.append(img)

    return all_nodes, planets, all_images


# ---------------------------------------------------------------------------
# Compute sphere radius (replicates SpawnNodeSphere lines 420-422)
# ---------------------------------------------------------------------------
def compute_sphere_radius(node):
    """Matches ComputeRadius + depth scaling in SpawnNodeSphere."""
    log_size = math.log2(node.size + 1)
    radius = log_size * RADIUS_SCALE
    radius = max(radius, MIN_RADIUS)
    depth_scale = DEPTH_RADIUS_FACTOR ** node.depth
    radius = max(radius * depth_scale, MIN_EFFECTIVE_RADIUS)
    return radius


# ---------------------------------------------------------------------------
# Simulate Candidate K expansion (fully unified)
# ---------------------------------------------------------------------------
def get_planet_ancestor(node):
    current = node
    while current.parent is not None:
        current = current.parent
    return current


def simulate_expansion(planets):
    """Simulates the CURRENT unified Candidate K implementation."""
    for p in planets:
        p.world_pos = p.pos * POSITION_SCALE
        p.sphere_radius = compute_sphere_radius(p)

    def expand_node(node):
        if node.children:
            planet = get_planet_ancestor(node)
            pwp = planet.world_pos
            prp = planet.pos * POSITION_SCALE
            for child in node.children:
                go = child.pos * POSITION_SCALE - prp
                child.world_pos = pwp + go * PLANET_SCALE
                child.sphere_radius = compute_sphere_radius(child)
            for child in node.children:
                expand_node(child)

        if node.images:
            planet = get_planet_ancestor(node)
            pwp = planet.world_pos
            prp = planet.pos * POSITION_SCALE
            for im in node.images:
                go = im.pos * POSITION_SCALE - prp
                im.world_pos = pwp + go * PLANET_SCALE

    for p in planets:
        expand_node(p)


# ---------------------------------------------------------------------------
# Compute adaptive quad sizes per leaf group (replicates SpawnImages sizing)
# ---------------------------------------------------------------------------
def compute_group_quad_sizes(all_nodes, all_images):
    """Returns dict: leaf_nid -> (quad_size, group_radius, positions)."""
    leaf_groups = defaultdict(list)
    for img in all_images:
        if img.world_pos is not None:
            leaf_groups[img.pid].append(img)

    result = {}
    for pid, images in leaf_groups.items():
        positions = np.array([img.world_pos for img in images])
        centroid = positions.mean(axis=0)
        group_radius = max(np.linalg.norm(p - centroid) for p in positions)
        group_radius = max(group_radius, 0.1)
        n = len(images)
        aqs = group_radius * 0.4 / math.sqrt(n)
        aqs = max(MIN_IMAGE_QUAD_SIZE, min(IMAGE_QUAD_SIZE, aqs))
        result[pid] = {
            "quad_size": aqs,
            "group_radius": group_radius,
            "positions": positions,
            "n": n,
            "depth": all_nodes[pid].depth if pid in all_nodes else -1,
        }
    return result


# ---------------------------------------------------------------------------
# Compute readability metrics
# ---------------------------------------------------------------------------
def compute_readability(all_nodes, planets, all_images):
    valid_imgs = [img for img in all_images if img.world_pos is not None]
    N = len(valid_imgs)

    # Group info
    group_info = compute_group_quad_sizes(all_nodes, all_images)

    results = {
        "n_images": N,
        "parameters": {
            "positionScale": POSITION_SCALE,
            "planetScale": PLANET_SCALE,
            "radiusScale": RADIUS_SCALE,
            "minRadius": MIN_RADIUS,
            "depthRadiusFactor": DEPTH_RADIUS_FACTOR,
            "minEffectiveRadius": MIN_EFFECTIVE_RADIUS,
            "imageQuadSize": IMAGE_QUAD_SIZE,
            "minImageQuadSize": MIN_IMAGE_QUAD_SIZE,
        },
        "method": "Candidate K — Unified Global Anchor (including single-child)",
    }

    # ===================================================================
    # CATEGORY A: Image Overlap
    # ===================================================================
    print("Category A: Image Overlap...")

    total_overlapping_pairs = 0
    total_image_pairs = 0
    images_with_nn_lt_quad = 0
    global_min_nn = float("inf")
    group_stats = []

    for pid, info in group_info.items():
        positions = info["positions"]
        n = info["n"]
        qs = info["quad_size"]

        if n < 2:
            group_stats.append({
                "pid": pid, "n": n, "depth": info["depth"],
                "group_radius": round(info["group_radius"], 4),
                "quad_size": round(qs, 4),
                "min_nn": None, "mean_nn": None,
                "overlapping_pairs": 0, "total_pairs": 0,
                "quad_to_gap_ratio": None,
            })
            continue

        D = cdist(positions, positions)
        np.fill_diagonal(D, np.inf)
        min_nn_per_img = np.min(D, axis=1)
        min_nn = float(min_nn_per_img.min())
        mean_nn = float(min_nn_per_img.mean())

        if min_nn < global_min_nn:
            global_min_nn = min_nn

        # Overlapping: distance < quad_size
        overlap_mask = D < qs
        np.fill_diagonal(overlap_mask, False)
        overlapping = int(np.sum(overlap_mask)) // 2  # each pair counted twice
        total_pairs = n * (n - 1) // 2

        total_overlapping_pairs += overlapping
        total_image_pairs += total_pairs

        # Images with NN < quad size
        nn_lt_quad = int(np.sum(min_nn_per_img < qs))
        images_with_nn_lt_quad += nn_lt_quad

        quad_to_gap = qs / mean_nn if mean_nn > 0 else float("inf")

        group_stats.append({
            "pid": pid, "n": n, "depth": info["depth"],
            "group_radius": round(info["group_radius"], 4),
            "quad_size": round(qs, 4),
            "min_nn": round(min_nn, 4), "mean_nn": round(mean_nn, 4),
            "overlapping_pairs": overlapping, "total_pairs": total_pairs,
            "quad_to_gap_ratio": round(quad_to_gap, 4),
        })

    overlap_rate = total_overlapping_pairs / total_image_pairs if total_image_pairs > 0 else 0
    imgs_nn_lt_quad_pct = 100.0 * images_with_nn_lt_quad / N

    results["category_A"] = {
        "total_overlapping_pairs": total_overlapping_pairs,
        "total_image_pairs": total_image_pairs,
        "overlap_rate_pct": round(100.0 * overlap_rate, 4),
        "images_with_nn_lt_quad": images_with_nn_lt_quad,
        "images_with_nn_lt_quad_pct": round(imgs_nn_lt_quad_pct, 2),
        "global_min_nn": round(global_min_nn, 4) if global_min_nn < float("inf") else None,
    }

    print(f"  Overlapping pairs: {total_overlapping_pairs}/{total_image_pairs} "
          f"({100*overlap_rate:.2f}%)")
    print(f"  Images with NN < quad size: {images_with_nn_lt_quad}/{N} "
          f"({imgs_nn_lt_quad_pct:.1f}%)")
    print(f"  Global min NN: {global_min_nn:.4f}")

    # ===================================================================
    # CATEGORY B: Parent Proximity
    # ===================================================================
    print("\nCategory B: Parent Proximity...")

    # B1: Images inside parent sphere
    imgs_inside_parent = 0
    for img in valid_imgs:
        pn = img.parent_node
        if pn and pn.world_pos is not None:
            d = np.linalg.norm(img.world_pos - pn.world_pos)
            if d < pn.sphere_radius:
                imgs_inside_parent += 1

    # B2: Node children overlapping parent sphere
    node_children_overlap_parent = 0
    total_node_children = 0
    for nid, n in all_nodes.items():
        if n.parent and n.world_pos is not None and n.parent.world_pos is not None:
            total_node_children += 1
            d = np.linalg.norm(n.world_pos - n.parent.world_pos)
            if d < n.parent.sphere_radius + n.sphere_radius:
                node_children_overlap_parent += 1

    results["category_B"] = {
        "images_inside_parent_sphere": imgs_inside_parent,
        "images_inside_parent_pct": round(100.0 * imgs_inside_parent / N, 2),
        "node_children_overlap_parent": node_children_overlap_parent,
        "total_node_children": total_node_children,
        "node_children_overlap_pct": round(
            100.0 * node_children_overlap_parent / total_node_children, 2
        ) if total_node_children > 0 else 0,
    }

    print(f"  Images inside parent sphere: {imgs_inside_parent}/{N} "
          f"({100*imgs_inside_parent/N:.1f}%)")
    print(f"  Node children overlapping parent: {node_children_overlap_parent}/"
          f"{total_node_children} "
          f"({100*node_children_overlap_parent/total_node_children:.1f}%)")

    # ===================================================================
    # CATEGORY C: Spacing Distribution
    # ===================================================================
    print("\nCategory C: Spacing Distribution...")

    all_positions = np.array([img.world_pos for img in valid_imgs])
    D_all = cdist(all_positions, all_positions)
    np.fill_diagonal(D_all, np.inf)
    nn_dists = np.min(D_all, axis=1)

    percentiles = {}
    for p in [10, 25, 50, 75, 90]:
        percentiles[f"P{p}"] = round(float(np.percentile(nn_dists, p)), 4)

    results["category_C"] = {
        "nn_distance_min": round(float(nn_dists.min()), 4),
        "nn_distance_mean": round(float(nn_dists.mean()), 4),
        "nn_distance_max": round(float(nn_dists.max()), 4),
        "percentiles": percentiles,
    }

    print(f"  Min:  {nn_dists.min():.4f}")
    print(f"  P10:  {percentiles['P10']}")
    print(f"  P25:  {percentiles['P25']}")
    print(f"  P50:  {percentiles['P50']}")
    print(f"  P75:  {percentiles['P75']}")
    print(f"  P90:  {percentiles['P90']}")
    print(f"  Mean: {nn_dists.mean():.4f}")

    # ===================================================================
    # CATEGORY D: Group-Level Readability
    # ===================================================================
    print("\nCategory D: Group-Level Readability...")

    # Sort by overlap count
    worst_by_overlap = sorted(
        [g for g in group_stats if g["overlapping_pairs"] > 0],
        key=lambda x: -x["overlapping_pairs"]
    )
    # Sort by quad-to-gap ratio
    worst_by_ratio = sorted(
        [g for g in group_stats if g["quad_to_gap_ratio"] is not None],
        key=lambda x: -x["quad_to_gap_ratio"]
    )

    results["category_D"] = {
        "worst_by_overlap": worst_by_overlap[:10],
        "worst_by_quad_gap_ratio": [
            {k: v for k, v in g.items()} for g in worst_by_ratio[:10]
        ],
        "all_groups": sorted(group_stats, key=lambda x: -x["n"]),
        "total_groups": len(group_stats),
        "groups_with_overlaps": len(worst_by_overlap),
    }

    print(f"  Total leaf groups: {len(group_stats)}")
    print(f"  Groups with overlaps: {len(worst_by_overlap)}")
    if worst_by_overlap:
        w = worst_by_overlap[0]
        print(f"  Worst group: {w['pid']} (n={w['n']}, "
              f"{w['overlapping_pairs']} overlapping pairs)")
    if worst_by_ratio:
        w = worst_by_ratio[0]
        print(f"  Worst quad-to-gap ratio: {w['pid']} "
              f"(ratio={w['quad_to_gap_ratio']:.3f})")

    # ===================================================================
    # CATEGORY E: Overall Summary
    # ===================================================================
    print("\nCategory E: Overall Summary...")

    # Readability score (0 = terrible, 1 = perfect)
    img_overlap_penalty = images_with_nn_lt_quad / N if N > 0 else 0
    parent_prox_penalty = imgs_inside_parent / N if N > 0 else 0
    node_overlap_penalty = (node_children_overlap_parent / total_node_children
                            if total_node_children > 0 else 0)
    spacing_penalty = max(0, 1 - float(np.median(nn_dists)) / 0.2)

    readability_score = 1.0 \
        - 0.4 * img_overlap_penalty \
        - 0.3 * parent_prox_penalty \
        - 0.2 * node_overlap_penalty \
        - 0.1 * spacing_penalty

    readability_score = max(0.0, min(1.0, readability_score))

    if readability_score >= 0.90:
        verdict = "Excellent"
    elif readability_score >= 0.75:
        verdict = "Acceptable"
    elif readability_score >= 0.55:
        verdict = "Moderate concern"
    else:
        verdict = "Poor"

    results["category_E"] = {
        "readability_score": round(readability_score, 4),
        "verdict": verdict,
        "component_penalties": {
            "image_overlap_penalty": round(img_overlap_penalty, 4),
            "parent_proximity_penalty": round(parent_prox_penalty, 4),
            "node_overlap_penalty": round(node_overlap_penalty, 4),
            "spacing_penalty": round(spacing_penalty, 4),
        },
    }

    print(f"  Readability score: {readability_score:.4f}")
    print(f"  Verdict: {verdict}")

    return results


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
def main():
    print("=" * 60)
    print("CANDIDATE K READABILITY EVALUATION")
    print("=" * 60)
    print()

    if not os.path.exists(CSV_PATH):
        print(f"ERROR: CSV not found at {CSV_PATH}")
        sys.exit(1)

    print("Parsing CSV and building tree...")
    all_nodes, planets, all_images = parse_and_build_tree(CSV_PATH)
    print(f"  Nodes: {len(all_nodes)}, Images: {len(all_images)}, "
          f"Planets: {len(planets)}")

    print("\nSimulating unified Candidate K expansion...")
    print(f"  planetScale = {PLANET_SCALE}")
    simulate_expansion(planets)
    valid = sum(1 for img in all_images if img.world_pos is not None)
    print(f"  Images with positions: {valid}/{len(all_images)}")

    print("\nComputing readability metrics...")
    print("-" * 50)
    results = compute_readability(all_nodes, planets, all_images)
    print("-" * 50)

    # Save JSON
    json_path = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                             "candidateK_readability_results.json")
    with open(json_path, "w") as f:
        # Convert numpy arrays for JSON serialization
        def clean(obj):
            if isinstance(obj, np.ndarray):
                return obj.tolist()
            if isinstance(obj, np.floating):
                return float(obj)
            if isinstance(obj, np.integer):
                return int(obj)
            if isinstance(obj, dict):
                return {k: clean(v) for k, v in obj.items()}
            if isinstance(obj, list):
                return [clean(v) for v in obj]
            return obj
        json.dump(clean(results), f, indent=2)
    print(f"\nResults saved to: {json_path}")

    # Print summary table
    r = results
    a = r["category_A"]
    b = r["category_B"]
    c = r["category_C"]
    e = r["category_E"]

    print("\n" + "=" * 60)
    print("MAIN RESULT TABLE")
    print("=" * 60)
    print(f"{'Metric':<50s} {'Value':>12s}")
    print("-" * 64)
    print(f"{'Total overlapping image pairs':<50s} "
          f"{a['total_overlapping_pairs']:>12d}")
    print(f"{'Overlap rate':<50s} "
          f"{a['overlap_rate_pct']:>11.2f}%")
    print(f"{'Images with NN < quad size':<50s} "
          f"{a['images_with_nn_lt_quad']:>5d} ({a['images_with_nn_lt_quad_pct']:.1f}%)")
    print(f"{'Global min NN distance':<50s} "
          f"{a['global_min_nn']:>12.4f}")
    print(f"{'Images inside parent sphere':<50s} "
          f"{b['images_inside_parent_sphere']:>5d} ({b['images_inside_parent_pct']:.1f}%)")
    print(f"{'Node children overlapping parent':<50s} "
          f"{b['node_children_overlap_parent']:>5d} ({b['node_children_overlap_pct']:.1f}%)")
    print()
    print(f"{'NN Distance Percentiles:':<50s}")
    for k, v in c["percentiles"].items():
        print(f"  {k:<48s} {v:>12.4f}")
    print()
    print(f"{'Readability score':<50s} {e['readability_score']:>12.4f}")
    print(f"{'Verdict':<50s} {e['verdict']:>12s}")

    # Per-group table (top 15 by size)
    print("\n" + "=" * 60)
    print("PER-GROUP SUMMARY (top 15 by image count)")
    print("=" * 60)
    groups = r["category_D"]["all_groups"][:15]
    print(f"{'Leaf':<16s} {'n':>4s} {'spread':>8s} {'quadSz':>8s} "
          f"{'minNN':>8s} {'ovlp':>6s} {'q/gap':>7s}")
    print("-" * 59)
    for g in groups:
        min_nn = f"{g['min_nn']:.4f}" if g['min_nn'] is not None else "   N/A"
        q_ratio = f"{g['quad_to_gap_ratio']:.3f}" if g['quad_to_gap_ratio'] is not None else "  N/A"
        print(f"{g['pid']:<16s} {g['n']:>4d} {g['group_radius']:>8.3f} "
              f"{g['quad_size']:>8.4f} {min_nn:>8s} {g['overlapping_pairs']:>6d} "
              f"{q_ratio:>7s}")

    print("\n" + "=" * 60)
    print("EVALUATION COMPLETE")
    print("=" * 60)


if __name__ == "__main__":
    main()
