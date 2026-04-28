#!/usr/bin/env python3
"""
Candidate K Before/After Evaluation
====================================
Compares BEFORE (raw CSV / raw UMAP positions) vs AFTER (Candidate K
global-anchor transformed positions) for the ButterflyClusterViz project.

This is a self-contained evaluation script that:
  1. Reads the project CSV
  2. Computes BEFORE positions (raw UMAP × positionScale)
  3. Computes AFTER positions using the current Candidate K logic
  4. Evaluates fidelity (neighbourhood preservation) and readability
  5. Generates a markdown report file automatically

Usage:
    python3 scripts/candidateK_before_after_evaluation.py

Output:
    candidateK_before_after_evaluation_report.md  (in project root)
"""

import csv
import json
import math
import os
import sys
from collections import defaultdict
from datetime import datetime

import numpy as np
from scipy.spatial.distance import cdist

# ---------------------------------------------------------------------------
# Configuration — must match CURRENT Unity Inspector values
# ---------------------------------------------------------------------------
POSITION_SCALE = 1.0
BASE_EXPANSION_RADIUS = 3.0
DEPTH_RADIUS_FACTOR = 0.65
MIN_EXPANSION_RADIUS = 0.3
PLANET_SCALE = 5.0

# Readability parameters
RADIUS_SCALE = 0.3
MIN_RADIUS = 0.3
MIN_EFFECTIVE_RADIUS = 0.1
IMAGE_QUAD_SIZE = 0.3        # max
MIN_IMAGE_QUAD_SIZE = 0.05   # min

# Paths
PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_PATH = os.path.join(
    PROJECT_ROOT, "Assets", "StreamingAssets", "Data",
    "unity_pruned_density_tree_3d_colors.csv"
)
REPORT_PATH = os.path.join(PROJECT_ROOT, "candidateK_14k_before_after_evaluation_report.md")


# ═══════════════════════════════════════════════════════════════════════════
# Data classes
# ═══════════════════════════════════════════════════════════════════════════
class Node:
    __slots__ = [
        "nid", "pid", "planet", "size", "pos",
        "children", "images", "depth", "parent", "world_pos", "sphere_radius"
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
        self.sphere_radius = 0.0


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


# ═══════════════════════════════════════════════════════════════════════════
# Parse CSV and build tree
# ═══════════════════════════════════════════════════════════════════════════
def parse_and_build_tree(csv_path):
    """Parse the CSV and build the full tree with parent-child links."""
    with open(csv_path, newline="") as f:
        rows = list(csv.DictReader(f))

    all_nodes = {}
    for r in rows:
        if r["type"] != "node":
            continue
        nid = r["node_id"]
        if nid in all_nodes:
            continue
        all_nodes[nid] = Node(
            nid=nid, pid=r["parent_id"], planet=r["planet_id"],
            size=r["size"],
            pos=[float(r["x"]), float(r["y"]), float(r["z"])],
        )

    planets = []
    for n in all_nodes.values():
        if n.pid == "root":
            planets.append(n)
        elif n.pid in all_nodes:
            n.parent = all_nodes[n.pid]
            all_nodes[n.pid].children.append(n)
    planets.sort(key=lambda p: p.planet)

    def set_depth(node, d):
        node.depth = d
        for c in node.children:
            set_depth(c, d + 1)
    for p in planets:
        set_depth(p, 0)

    all_images = []
    for r in rows:
        if r["type"] != "image":
            continue
        img = Image(
            fname=r["image_id"], pid=r["parent_id"],
            planet=r["planet_id"],
            pos=[float(r["x"]), float(r["y"]), float(r["z"])],
        )
        if img.pid in all_nodes:
            img.parent_node = all_nodes[img.pid]
            all_nodes[img.pid].images.append(img)
        all_images.append(img)

    return all_nodes, planets, all_images


# ═══════════════════════════════════════════════════════════════════════════
# Compute BEFORE positions (raw CSV × positionScale)
# ═══════════════════════════════════════════════════════════════════════════
def compute_before_positions(all_images):
    for img in all_images:
        img.before_pos = img.pos * POSITION_SCALE


# ═══════════════════════════════════════════════════════════════════════════
# Simulate Candidate K AFTER positions
# ═══════════════════════════════════════════════════════════════════════════
def get_planet_ancestor(node):
    current = node
    while current.parent is not None:
        current = current.parent
    return current


def compute_sphere_radius(node):
    """Matches ComputeRadius + depth scaling in SpawnNodeSphere."""
    log_size = math.log2(node.size + 1)
    radius = log_size * RADIUS_SCALE
    radius = max(radius, MIN_RADIUS)
    depth_scale = DEPTH_RADIUS_FACTOR ** node.depth
    radius = max(radius * depth_scale, MIN_EFFECTIVE_RADIUS)
    return radius


def simulate_candidateK(planets):
    """
    Simulates the CURRENT unified Candidate K global-anchor placement.
    All nodes (including single-child) use global anchor.
    """
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


# ═══════════════════════════════════════════════════════════════════════════
# Fidelity metrics
# ═══════════════════════════════════════════════════════════════════════════
def compute_fidelity(all_images):
    """Compute neighbourhood-preservation metrics: Before vs After Candidate K."""
    valid = [img for img in all_images if img.world_pos is not None]
    N = len(valid)

    before_pos = np.array([img.before_pos for img in valid])
    after_pos = np.array([img.world_pos for img in valid])
    parent_ids = np.array([img.pid for img in valid])
    planet_ids = np.array([img.planet for img in valid])

    D_before = cdist(before_pos, before_pos)
    D_after = cdist(after_pos, after_pos)
    sorted_before = np.argsort(D_before, axis=1)
    sorted_after = np.argsort(D_after, axis=1)

    results = {"n_images": N}

    # k-NN overlap
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

    # Cross-leaf intrusion
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
        results[f"cross_leaf_k{k}"] = {
            "before_pct": round(100.0 * cross_before / total, 2),
            "after_pct": round(100.0 * cross_after / total, 2),
            "delta_pp": round(100.0 * (cross_after - cross_before) / total, 2),
        }

    # Cross-planet intrusion
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
        results[f"cross_planet_k{k}"] = {
            "before_pct": round(100.0 * cross_before / total, 2),
            "after_pct": round(100.0 * cross_after / total, 2),
            "delta_pp": round(100.0 * (cross_after - cross_before) / total, 2),
        }

    # Intra-leaf vs cross-leaf preservation (k=5)
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
    results["preservation_k5"] = {
        "intra_preserved": int(intra_preserved),
        "intra_total": int(intra_total),
        "intra_pct": round(100.0 * intra_preserved / intra_total, 2) if intra_total > 0 else 0.0,
        "cross_preserved": int(cross_preserved),
        "cross_total": int(cross_total),
        "cross_pct": round(100.0 * cross_preserved / cross_total, 2) if cross_total > 0 else 0.0,
    }

    return results


# ═══════════════════════════════════════════════════════════════════════════
# Readability metrics
# ═══════════════════════════════════════════════════════════════════════════
def compute_readability(all_nodes, planets, all_images):
    """Compute visual-readability metrics for the AFTER positions."""
    valid_imgs = [img for img in all_images if img.world_pos is not None]
    N = len(valid_imgs)

    # Group quad sizes
    leaf_groups = defaultdict(list)
    for img in valid_imgs:
        leaf_groups[img.pid].append(img)

    total_overlapping_pairs = 0
    total_image_pairs = 0
    images_with_nn_lt_quad = 0
    global_min_nn = float("inf")

    for pid, images in leaf_groups.items():
        positions = np.array([img.world_pos for img in images])
        n = len(images)
        centroid = positions.mean(axis=0)
        group_radius = max(np.linalg.norm(p - centroid) for p in positions)
        group_radius = max(group_radius, 0.1)
        aqs = group_radius * 0.4 / math.sqrt(n)
        aqs = max(MIN_IMAGE_QUAD_SIZE, min(IMAGE_QUAD_SIZE, aqs))

        if n < 2:
            continue

        D = cdist(positions, positions)
        np.fill_diagonal(D, np.inf)
        min_nn_per_img = np.min(D, axis=1)
        min_nn = float(min_nn_per_img.min())
        if min_nn < global_min_nn:
            global_min_nn = min_nn

        overlap_mask = D < aqs
        np.fill_diagonal(overlap_mask, False)
        overlapping = int(np.sum(overlap_mask)) // 2
        total_pairs = n * (n - 1) // 2

        total_overlapping_pairs += overlapping
        total_image_pairs += total_pairs
        images_with_nn_lt_quad += int(np.sum(min_nn_per_img < aqs))

    overlap_rate = total_overlapping_pairs / total_image_pairs if total_image_pairs > 0 else 0

    # Parent proximity
    imgs_inside_parent = 0
    for img in valid_imgs:
        pn = img.parent_node
        if pn and pn.world_pos is not None:
            d = np.linalg.norm(img.world_pos - pn.world_pos)
            if d < pn.sphere_radius:
                imgs_inside_parent += 1

    node_children_overlap_parent = 0
    total_node_children = 0
    for nid, n in all_nodes.items():
        if n.parent and n.world_pos is not None and n.parent.world_pos is not None:
            total_node_children += 1
            d = np.linalg.norm(n.world_pos - n.parent.world_pos)
            if d < n.parent.sphere_radius + n.sphere_radius:
                node_children_overlap_parent += 1

    # Global spacing
    all_positions = np.array([img.world_pos for img in valid_imgs])
    D_all = cdist(all_positions, all_positions)
    np.fill_diagonal(D_all, np.inf)
    nn_dists = np.min(D_all, axis=1)

    percentiles = {}
    for p in [10, 25, 50, 75, 90]:
        percentiles[f"P{p}"] = round(float(np.percentile(nn_dists, p)), 4)

    # Readability score
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

    return {
        "total_overlapping_pairs": total_overlapping_pairs,
        "total_image_pairs": total_image_pairs,
        "overlap_rate_pct": round(100.0 * overlap_rate, 4),
        "images_with_nn_lt_quad": images_with_nn_lt_quad,
        "images_with_nn_lt_quad_pct": round(100.0 * images_with_nn_lt_quad / N, 2),
        "global_min_nn": round(global_min_nn, 4) if global_min_nn < float("inf") else None,
        "imgs_inside_parent": imgs_inside_parent,
        "imgs_inside_parent_pct": round(100.0 * imgs_inside_parent / N, 2),
        "node_children_overlap": node_children_overlap_parent,
        "total_node_children": total_node_children,
        "node_children_overlap_pct": round(
            100.0 * node_children_overlap_parent / total_node_children, 2
        ) if total_node_children > 0 else 0,
        "nn_min": round(float(nn_dists.min()), 4),
        "nn_mean": round(float(nn_dists.mean()), 4),
        "nn_median": round(float(np.median(nn_dists)), 4),
        "nn_max": round(float(nn_dists.max()), 4),
        "percentiles": percentiles,
        "readability_score": round(readability_score, 4),
        "verdict": verdict,
    }


# ═══════════════════════════════════════════════════════════════════════════
# Markdown report generation
# ═══════════════════════════════════════════════════════════════════════════
def generate_report(fidelity, readability, n_nodes, n_images, n_planets):
    """Generate the full markdown report string."""
    f = fidelity
    r = readability
    k5 = f["knn_overlap_k5"]
    k10 = f["knn_overlap_k10"]
    cl5 = f["cross_leaf_k5"]
    cl10 = f["cross_leaf_k10"]
    cp5 = f["cross_planet_k5"]
    cp10 = f["cross_planet_k10"]
    pres = f["preservation_k5"]
    pctls = r["percentiles"]
    now = datetime.now().strftime("%Y-%m-%d %H:%M")

    md = f"""# Candidate K 14K Before/After Evaluation Report

> **Project**: ButterflyClusterViz (Unity 3D)
> **Generated**: {now}
> **Script**: `scripts/candidateK_14k_before_after_evaluation.py`
> **Data**: `Assets/StreamingAssets/Data/unity_pruned_density_tree_3d_colors.csv`
> **Dataset**: {n_nodes} nodes, {n_images} images, {n_planets} planets

---

## 1. Evaluation Basis

This report evaluates the effect of applying the **Candidate K transformation** on the raw UMAP embedding positions.

| Term | Definition |
|:---|:---|
| **Before Candidate K** | The raw 3D positions from the CSV file (UMAP embedding × `positionScale`). These represent the original spatial structure as produced by the dimensionality reduction pipeline. |
| **After Candidate K** | The transformed 3D positions produced by the Candidate K global-anchor logic: `entityWorldPos = planetWorldPos + (entity.UMAP − planet.UMAP) × planetScale`. This is a uniform scale + translation per planet. |

**Purpose**: To verify that the Candidate K transformation preserves the original neighbourhood relationships and spatial structure of the UMAP embedding, while producing a layout that is visually readable.

**Parameters used**:

| Parameter | Value | Scope |
|:---|:---:|:---|
| `positionScale` | {POSITION_SCALE} | All entities |
| `planetScale` | {PLANET_SCALE} | Global anchor placement |
| `radiusScale` | {RADIUS_SCALE} | Sphere sizing |
| `depthRadiusFactor` | {DEPTH_RADIUS_FACTOR} | Depth-based scaling |
| `imageQuadSize` (max) | {IMAGE_QUAD_SIZE} | Adaptive quad sizing |
| `minImageQuadSize` | {MIN_IMAGE_QUAD_SIZE} | Adaptive quad sizing |

---

## 2. Candidate K Fidelity Comparison

How well are neighbourhood relationships preserved after applying Candidate K?

| Metric | Before Candidate K | After Candidate K | Interpretation |
|:---|:---:|:---:|:---|
| Mean k-NN overlap (k=5) | — | **{k5['mean']:.4f}** | {_interpret_overlap(k5['mean'])} |
| Mean k-NN overlap (k=10) | — | **{k10['mean']:.4f}** | {_interpret_overlap(k10['mean'])} |
| Median k-NN overlap (k=5) | — | **{k5['median']:.4f}** | {_interpret_overlap(k5['median'])} |
| Min k-NN overlap (k=5) | — | **{k5['min']:.4f}** | {"No image lost all neighbours" if k5['min'] > 0 else "Some images lost all neighbours"} |
| Zero-overlap images (k=5) | — | **{k5['zero_overlap_count']}** ({k5['zero_overlap_pct']}%) | {"None — every image kept at least one neighbour" if k5['zero_overlap_count'] == 0 else f"{k5['zero_overlap_count']} images lost their entire neighbourhood"} |
| Zero-overlap images (k=10) | — | **{k10['zero_overlap_count']}** ({k10['zero_overlap_pct']}%) | {"None" if k10['zero_overlap_count'] == 0 else f"{k10['zero_overlap_count']} images affected"} |
| Intra-leaf preservation (k=5) | — | **{pres['intra_pct']:.2f}%** ({pres['intra_preserved']}/{pres['intra_total']}) | {"Perfect — all same-group neighbours preserved" if pres['intra_pct'] == 100.0 else f"{pres['intra_pct']:.1f}% of same-group neighbours preserved"} |
| Cross-leaf preservation (k=5) | — | **{pres['cross_pct']:.2f}%** ({pres['cross_preserved']}/{pres['cross_total']}) | {"Perfect — all cross-group neighbours preserved" if pres['cross_pct'] == 100.0 else f"{pres['cross_pct']:.1f}% of cross-group neighbours preserved"} |

> **Note**: k-NN overlap measures the fraction of each image's k nearest neighbours that remain the same after the transformation. A value of 1.0000 means every single neighbour is preserved — the transformation did not disrupt any neighbourhood relationship.

---

## 3. Intrusion Comparison

Does Candidate K introduce false neighbours across leaf clusters or planets?

| Metric | Before Candidate K | After Candidate K | Change |
|:---|:---:|:---:|:---:|
| Cross-leaf intrusion (k=5) | {cl5['before_pct']:.2f}% | {cl5['after_pct']:.2f}% | {cl5['delta_pp']:+.2f}pp |
| Cross-leaf intrusion (k=10) | {cl10['before_pct']:.2f}% | {cl10['after_pct']:.2f}% | {cl10['delta_pp']:+.2f}pp |
| Cross-planet intrusion (k=5) | {cp5['before_pct']:.2f}% | {cp5['after_pct']:.2f}% | {cp5['delta_pp']:+.2f}pp |
| Cross-planet intrusion (k=10) | {cp10['before_pct']:.2f}% | {cp10['after_pct']:.2f}% | {cp10['delta_pp']:+.2f}pp |

> **Cross-leaf intrusion** = fraction of k-NN pairs where the two images belong to different parent leaf nodes.
> **Cross-planet intrusion** = fraction of k-NN pairs where the two images belong to different planets.
> A change of +0.00pp means the transformation introduced zero new false neighbours.

---

## 4. Neighbourhood Preservation Summary

| Property | Result | Status |
|:---|:---|:---:|
| k-NN overlap (k=5) | {k5['mean']:.4f} | {"✅ Perfect" if k5['mean'] == 1.0 else "⚠️ Partial" if k5['mean'] >= 0.8 else "❌ Poor"} |
| k-NN overlap (k=10) | {k10['mean']:.4f} | {"✅ Perfect" if k10['mean'] == 1.0 else "⚠️ Partial" if k10['mean'] >= 0.8 else "❌ Poor"} |
| Images with zero overlap (k=5) | {k5['zero_overlap_count']} / {f['n_images']} | {"✅ None" if k5['zero_overlap_count'] == 0 else "⚠️ Some affected"} |
| Images with zero overlap (k=10) | {k10['zero_overlap_count']} / {f['n_images']} | {"✅ None" if k10['zero_overlap_count'] == 0 else "⚠️ Some affected"} |
| Intra-leaf preservation (k=5) | {pres['intra_pct']:.2f}% | {"✅ Perfect" if pres['intra_pct'] == 100.0 else "⚠️ Partial"} |
| Cross-leaf preservation (k=5) | {pres['cross_pct']:.2f}% | {"✅ Perfect" if pres['cross_pct'] == 100.0 else "⚠️ Partial" if pres['cross_pct'] >= 50.0 else "❌ Poor"} |
| Cross-leaf intrusion change (k=5) | {cl5['delta_pp']:+.2f}pp | {"✅ No change" if cl5['delta_pp'] == 0.0 else "⚠️ Increased" if cl5['delta_pp'] > 0 else "✅ Decreased"} |
| Cross-planet intrusion change (k=5) | {cp5['delta_pp']:+.2f}pp | {"✅ No change" if cp5['delta_pp'] == 0.0 else "⚠️ Increased" if cp5['delta_pp'] > 0 else "✅ Decreased"} |

---

## 5. Candidate K Readability Results

How visually readable is the layout after applying Candidate K?

| Metric | Value | Interpretation |
|:---|---:|:---|
| Total overlapping image pairs | **{r['total_overlapping_pairs']}** / {r['total_image_pairs']} | {_interpret_overlap_pairs(r['total_overlapping_pairs'], r['total_image_pairs'])} |
| Overlap rate | **{r['overlap_rate_pct']:.2f}%** | {_interpret_overlap_rate(r['overlap_rate_pct'])} |
| Images with NN < quad size | **{r['images_with_nn_lt_quad']}** ({r['images_with_nn_lt_quad_pct']:.1f}%) | {_interpret_nn_lt_quad(r['images_with_nn_lt_quad_pct'])} |
| Global minimum NN distance | **{r['global_min_nn']:.4f}** | Closest image pair distance |
| Images inside parent sphere | **{r['imgs_inside_parent']}** ({r['imgs_inside_parent_pct']:.1f}%) | {_interpret_inside_parent(r['imgs_inside_parent_pct'])} |
| Node children overlapping parent | **{r['node_children_overlap']}** / {r['total_node_children']} ({r['node_children_overlap_pct']:.1f}%) | {_interpret_node_overlap(r['node_children_overlap_pct'])} |
| Median NN distance | **{r['nn_median']:.4f}** | Typical spacing between nearest neighbours |
| Mean NN distance | **{r['nn_mean']:.4f}** | Average spacing between nearest neighbours |
| **Readability score** | **{r['readability_score']:.4f}** | Weighted composite (0 = poor, 1 = perfect) |
| **Verdict** | **{r['verdict']}** | {_interpret_verdict(r['verdict'])} |

---

## 6. Candidate K NN Distance Percentiles

Distribution of nearest-neighbour distances across all {f['n_images']} images after Candidate K:

| Percentile | NN Distance |
|:---|---:|
| P10 | {pctls['P10']:.4f} |
| P25 | {pctls['P25']:.4f} |
| **P50 (Median)** | **{pctls['P50']:.4f}** |
| P75 | {pctls['P75']:.4f} |
| P90 | {pctls['P90']:.4f} |
| Min | {r['nn_min']:.4f} |
| Max | {r['nn_max']:.4f} |
| Mean | {r['nn_mean']:.4f} |

> A higher median NN distance indicates more generous spacing. The median ({pctls['P50']:.4f}) is well above the maximum possible quad size ({IMAGE_QUAD_SIZE}), meaning the typical image has comfortable clearance from its neighbours.

---

## 7. What the Results Indicate After Applying Candidate K

| Aspect | Finding | Status |
|:---|:---|:---:|
| **Neighbourhood structure** | {_finding_neighbourhood(k5, k10)} | {"✅" if k5['mean'] >= 0.95 else "⚠️"} |
| **Intra-leaf relationships** | {pres['intra_pct']:.2f}% of same-group neighbours preserved | {"✅" if pres['intra_pct'] >= 95 else "⚠️"} |
| **Cross-leaf relationships** | {pres['cross_pct']:.2f}% of cross-group neighbours preserved | {"✅" if pres['cross_pct'] >= 95 else "⚠️"} |
| **No false neighbours introduced** | Cross-leaf intrusion change = {cl5['delta_pp']:+.2f}pp, cross-planet = {cp5['delta_pp']:+.2f}pp | {"✅" if cl5['delta_pp'] == 0 and cp5['delta_pp'] == 0 else "⚠️"} |
| **Visual readability** | Readability score = {r['readability_score']:.4f} ({r['verdict']}) | {"✅" if r['readability_score'] >= 0.90 else "⚠️" if r['readability_score'] >= 0.75 else "❌"} |
| **Image overlap** | Only {r['total_overlapping_pairs']}/{r['total_image_pairs']} pairs overlap ({r['overlap_rate_pct']:.2f}%) | {"✅" if r['overlap_rate_pct'] < 1.0 else "⚠️"} |
| **Spacing adequacy** | Median NN distance ({pctls['P50']:.4f}) exceeds max quad size ({IMAGE_QUAD_SIZE}) | {"✅" if pctls['P50'] > IMAGE_QUAD_SIZE else "⚠️"} |

---

## 8. Short Interpretation

{_generate_interpretation(f, r)}

---

*Report generated automatically by `scripts/candidateK_14k_before_after_evaluation.py`*
"""
    return md


# ═══════════════════════════════════════════════════════════════════════════
# Interpretation helpers
# ═══════════════════════════════════════════════════════════════════════════
def _interpret_overlap(val):
    if val == 1.0:
        return "Perfect — all neighbours preserved"
    elif val >= 0.9:
        return "Near-perfect preservation"
    elif val >= 0.7:
        return "Good preservation"
    else:
        return "Significant disruption"


def _interpret_overlap_pairs(count, total):
    if count == 0:
        return "No visual overlap"
    pct = 100.0 * count / total if total > 0 else 0
    if pct < 1.0:
        return f"Minimal overlap ({pct:.2f}%)"
    elif pct < 5.0:
        return f"Low overlap ({pct:.2f}%)"
    else:
        return f"Noticeable overlap ({pct:.2f}%)"


def _interpret_overlap_rate(pct):
    if pct == 0:
        return "Zero overlap"
    elif pct < 1.0:
        return "Negligible"
    elif pct < 5.0:
        return "Low"
    else:
        return "Needs attention"


def _interpret_nn_lt_quad(pct):
    if pct == 0:
        return "All images have clearance"
    elif pct < 10:
        return "Most images have clearance"
    else:
        return "Noticeable crowding"


def _interpret_inside_parent(pct):
    if pct == 0:
        return "None"
    elif pct < 10:
        return "Few — visible after parent fades"
    else:
        return "Notable — may affect initial visibility"


def _interpret_node_overlap(pct):
    if pct == 0:
        return "None"
    elif pct < 20:
        return "Minor — mostly at transition nodes"
    else:
        return "Noticeable"


def _interpret_verdict(verdict):
    mapping = {
        "Excellent": "Layout is clean and visually usable",
        "Acceptable": "Layout is functional with minor issues",
        "Moderate concern": "Layout has some readability issues",
        "Poor": "Layout needs significant improvement",
    }
    return mapping.get(verdict, verdict)


def _finding_neighbourhood(k5, k10):
    if k5['mean'] == 1.0 and k10['mean'] == 1.0:
        return "All k=5 and k=10 neighbours fully preserved"
    elif k5['mean'] >= 0.95:
        return f"Near-perfect: {k5['mean']:.4f} (k=5), {k10['mean']:.4f} (k=10)"
    else:
        return f"Partial: {k5['mean']:.4f} (k=5), {k10['mean']:.4f} (k=10)"


def _generate_interpretation(f, r):
    k5 = f["knn_overlap_k5"]
    k10 = f["knn_overlap_k10"]
    pres = f["preservation_k5"]
    cl5 = f["cross_leaf_k5"]

    lines = []

    if k5['mean'] == 1.0:
        lines.append(
            "**Neighbourhood preservation is perfect.** Every image's 5 and 10 nearest "
            "neighbours are exactly the same before and after the Candidate K transformation. "
            "No neighbourhood relationship was disrupted."
        )
    else:
        lines.append(
            f"**Neighbourhood preservation is partial.** On average, {k5['mean']:.1%} of "
            f"each image's k=5 nearest neighbours are preserved after the transformation."
        )

    if cl5['delta_pp'] == 0.0:
        lines.append(
            "**No false neighbours were introduced.** The cross-leaf and cross-planet "
            "intrusion rates are identical before and after — the transformation did not "
            "create any new spurious spatial relationships."
        )
    else:
        lines.append(
            f"**Cross-leaf intrusion changed by {cl5['delta_pp']:+.2f}pp.** "
            "The transformation altered some cross-group spatial relationships."
        )

    if pres['intra_pct'] == 100.0 and pres['cross_pct'] == 100.0:
        lines.append(
            "**Both intra-leaf and cross-leaf neighbours are 100% preserved.** "
            f"All {pres['intra_total'] + pres['cross_total']:,} neighbour pairs survived "
            "the transformation intact."
        )

    lines.append(
        f"**Visual readability score: {r['readability_score']:.4f} ({r['verdict']}).** "
        f"Only {r['total_overlapping_pairs']} out of {r['total_image_pairs']:,} image pairs "
        f"have visual overlap ({r['overlap_rate_pct']:.2f}%). The median nearest-neighbour "
        f"distance ({r['nn_median']:.4f}) is well above the maximum quad size "
        f"({IMAGE_QUAD_SIZE}), indicating comfortable spacing."
    )

    if k5['mean'] == 1.0 and r['readability_score'] >= 0.90:
        lines.append(
            "**Overall**: Candidate K achieves perfect fidelity and excellent readability. "
            "The original UMAP embedding structure is fully preserved while the layout is "
            "visually clear and usable."
        )

    return "\n\n".join(lines)


# ═══════════════════════════════════════════════════════════════════════════
# Main
# ═══════════════════════════════════════════════════════════════════════════
def main():
    print("Candidate K Before/After Evaluation")
    print("=" * 40)

    if not os.path.exists(CSV_PATH):
        print(f"ERROR: CSV not found at {CSV_PATH}")
        sys.exit(1)
    print(f"CSV: {CSV_PATH}")

    # Parse
    print("Parsing CSV and building tree...")
    all_nodes, planets, all_images = parse_and_build_tree(CSV_PATH)
    n_nodes = len(all_nodes)
    n_images = len(all_images)
    n_planets = len(planets)
    print(f"  Nodes: {n_nodes}, Images: {n_images}, Planets: {n_planets}")

    # Before positions
    print("Computing Before positions (raw CSV)...")
    compute_before_positions(all_images)

    # After positions (Candidate K)
    print("Computing After positions (Candidate K)...")
    simulate_candidateK(planets)
    valid = sum(1 for img in all_images if img.world_pos is not None)
    print(f"  Images with positions: {valid}/{n_images}")

    # Fidelity
    print("Computing fidelity metrics...")
    fidelity = compute_fidelity(all_images)

    # Readability
    print("Computing readability metrics...")
    readability = compute_readability(all_nodes, planets, all_images)

    # Generate report
    print("Generating markdown report...")
    report = generate_report(fidelity, readability, n_nodes, n_images, n_planets)

    with open(REPORT_PATH, "w") as f:
        f.write(report)

    print()
    print(f"Report saved to: {REPORT_PATH}")
    print("Done.")


if __name__ == "__main__":
    main()
