#!/usr/bin/env python3
"""
Final Evaluation — Candidate K (Current Project State)
======================================================
Evaluates BEFORE (raw CSV / UMAP positions) vs AFTER (Candidate K
global-anchor transformed positions) for ButterflyClusterViz.

Usage:  python3 final_evaluation.py
Output: prints structured results to terminal + final_evaluation_results.json
"""

import csv, json, math, os, sys
from collections import defaultdict
from datetime import datetime
import numpy as np
from scipy.spatial.distance import cdist

# ── Configuration (matches CURRENT Unity Inspector values) ──
POSITION_SCALE = 1.0
PLANET_SCALE = 5.0
RADIUS_SCALE = 0.3
MIN_RADIUS = 0.3
DEPTH_RADIUS_FACTOR = 0.65
MIN_EFFECTIVE_RADIUS = 0.1
SIBLING_RADIUS_CAP_FACTOR = 0.6
IMAGE_QUAD_SIZE = 0.3
MIN_IMAGE_QUAD_SIZE = 0.05

PROJECT_ROOT = os.path.dirname(os.path.abspath(__file__))
CSV_PATH = os.path.join(PROJECT_ROOT, "Assets", "StreamingAssets", "Data",
                        "unity_pruned_density_tree_3d_colors.csv")

# ── Data classes ──
class Node:
    __slots__ = ["nid","pid","planet","size","pos","children","images",
                 "depth","parent","world_pos","sphere_radius"]
    def __init__(self, nid, pid, planet, size, pos):
        self.nid, self.pid, self.planet = nid, pid, int(planet)
        self.size = int(size)
        self.pos = np.array(pos, dtype=np.float64)
        self.children, self.images = [], []
        self.depth = 0
        self.parent = None
        self.world_pos = None
        self.sphere_radius = 0.0

class Image:
    __slots__ = ["fname","pid","planet","pos","parent_node","before_pos","world_pos"]
    def __init__(self, fname, pid, planet, pos):
        self.fname, self.pid, self.planet = fname, pid, int(planet)
        self.pos = np.array(pos, dtype=np.float64)
        self.parent_node = None
        self.before_pos = None
        self.world_pos = None

# ── Parse CSV ──
def parse_csv(path):
    with open(path, newline="") as f:
        rows = list(csv.DictReader(f))
    nodes = {}
    for r in rows:
        if r["type"] != "node": continue
        nid = r["node_id"]
        if nid in nodes: continue
        nodes[nid] = Node(nid, r["parent_id"], r["planet_id"], r["size"],
                          [float(r["x"]), float(r["y"]), float(r["z"])])
    planets = []
    for n in nodes.values():
        if n.pid == "root":
            planets.append(n)
        elif n.pid in nodes:
            n.parent = nodes[n.pid]
            nodes[n.pid].children.append(n)
    planets.sort(key=lambda p: p.planet)
    def set_depth(nd, d):
        nd.depth = d
        for c in nd.children: set_depth(c, d+1)
    for p in planets: set_depth(p, 0)
    images = []
    for r in rows:
        if r["type"] != "image": continue
        img = Image(r["image_id"], r["parent_id"], r["planet_id"],
                    [float(r["x"]), float(r["y"]), float(r["z"])])
        if img.pid in nodes:
            img.parent_node = nodes[img.pid]
            nodes[img.pid].images.append(img)
        images.append(img)
    return nodes, planets, images

# ── Before positions ──
def compute_before(images):
    for img in images:
        img.before_pos = img.pos * POSITION_SCALE

# ── Candidate K after positions ──
def get_planet_ancestor(node):
    c = node
    while c.parent is not None: c = c.parent
    return c

def compute_sphere_radius(node):
    log_size = math.log2(node.size + 1)
    r = max(log_size * RADIUS_SCALE, MIN_RADIUS)
    ds = DEPTH_RADIUS_FACTOR ** node.depth
    return max(r * ds, MIN_EFFECTIVE_RADIUS)

def simulate_candidateK(planets):
    for p in planets:
        p.world_pos = p.pos * POSITION_SCALE
        p.sphere_radius = compute_sphere_radius(p)
    def expand(node):
        planet = get_planet_ancestor(node)
        pwp, prp = planet.world_pos, planet.pos * POSITION_SCALE
        for child in node.children:
            go = child.pos * POSITION_SCALE - prp
            child.world_pos = pwp + go * PLANET_SCALE
            child.sphere_radius = compute_sphere_radius(child)
        for child in node.children:
            expand(child)
        for im in node.images:
            go = im.pos * POSITION_SCALE - prp
            im.world_pos = pwp + go * PLANET_SCALE
    for p in planets: expand(p)

# ── Within-planet fidelity (the primary metric) ──
def compute_within_planet_fidelity(images):
    """Compute k-NN overlap restricted to images within the same planet.
    This isolates the effect of the uniform per-planet scaling and
    excludes cross-planet interference."""
    valid = [i for i in images if i.world_pos is not None]
    by_planet = defaultdict(list)
    for img in valid:
        by_planet[img.planet].append(img)

    results = {}
    for planet_id in sorted(by_planet.keys()):
        imgs = by_planet[planet_id]
        n = len(imgs)
        if n < 2:
            results[planet_id] = {"n": n, "k5_mean": None, "k5_min": None, "k5_zero": 0}
            continue
        bp = np.array([i.before_pos for i in imgs])
        ap = np.array([i.world_pos for i in imgs])
        Db = cdist(bp, bp)
        Da = cdist(ap, ap)
        sb = np.argsort(Db, axis=1)
        sa = np.argsort(Da, axis=1)
        for k in [5, 10]:
            kk = min(k, n - 1)
            ovs = []
            for i in range(n):
                kb = set(sb[i, 1:kk+1])
                ka = set(sa[i, 1:kk+1])
                ovs.append(len(kb & ka) / kk)
            key = f"k{k}"
            results.setdefault(planet_id, {"n": n})
            results[planet_id][f"{key}_mean"] = float(np.mean(ovs))
            results[planet_id][f"{key}_min"] = float(np.min(ovs))
            results[planet_id][f"{key}_zero"] = sum(1 for o in ovs if o == 0.0)
    return results

# ── Global fidelity metrics ──
def compute_global_fidelity(images):
    valid = [i for i in images if i.world_pos is not None]
    N = len(valid)
    bp = np.array([i.before_pos for i in valid])
    ap = np.array([i.world_pos for i in valid])
    pids = np.array([i.pid for i in valid])
    plids = np.array([i.planet for i in valid])
    Db = cdist(bp, bp); Da = cdist(ap, ap)
    sb = np.argsort(Db, axis=1); sa = np.argsort(Da, axis=1)
    res = {"n": N}
    for k in [5, 10]:
        ovs = []
        for i in range(N):
            kb = set(sb[i,1:k+1]); ka = set(sa[i,1:k+1])
            ovs.append(len(kb & ka) / k)
        res[f"k{k}"] = {"mean": float(np.mean(ovs)), "median": float(np.median(ovs)),
                         "min": float(np.min(ovs)),
                         "zero_count": sum(1 for o in ovs if o==0.0),
                         "zero_pct": round(100*sum(1 for o in ovs if o==0.0)/N, 2)}
    for k in [5, 10]:
        cb, ca, tot = 0, 0, 0
        for i in range(N):
            for j in sb[i,1:k+1]:
                tot += 1
                if pids[i] != pids[j]: cb += 1
            for j in sa[i,1:k+1]:
                if pids[i] != pids[j]: ca += 1
        res[f"cl{k}"] = {"before": round(100*cb/tot,2), "after": round(100*ca/tot,2),
                          "delta": round(100*(ca-cb)/tot,2)}
    for k in [5, 10]:
        cb, ca, tot = 0, 0, 0
        for i in range(N):
            for j in sb[i,1:k+1]:
                tot += 1
                if plids[i] != plids[j]: cb += 1
            for j in sa[i,1:k+1]:
                if plids[i] != plids[j]: ca += 1
        res[f"cp{k}"] = {"before": round(100*cb/tot,2), "after": round(100*ca/tot,2),
                          "delta": round(100*(ca-cb)/tot,2)}
    k = 5
    ip, it, xp, xt = 0, 0, 0, 0
    for i in range(N):
        kb = set(sb[i,1:k+1]); ka = set(sa[i,1:k+1])
        for j in kb:
            if pids[i] == pids[j]:
                it += 1
                if j in ka: ip += 1
            else:
                xt += 1
                if j in ka: xp += 1
    res["pres"] = {"intra_p": ip, "intra_t": it,
                   "intra_pct": round(100*ip/it,2) if it else 0,
                   "cross_p": xp, "cross_t": xt,
                   "cross_pct": round(100*xp/xt,2) if xt else 0}
    return res

# ── Readability metrics ──
def compute_readability(nodes, planets, images):
    valid = [i for i in images if i.world_pos is not None]
    N = len(valid)
    groups = defaultdict(list)
    for img in valid: groups[img.pid].append(img)
    t_op, t_tp, nn_lt_q, g_min = 0, 0, 0, float("inf")
    for pid, imgs in groups.items():
        pos = np.array([i.world_pos for i in imgs]); n = len(imgs)
        cen = pos.mean(axis=0)
        gr = max(np.max(np.linalg.norm(pos - cen, axis=1)), 0.1)
        aqs = max(MIN_IMAGE_QUAD_SIZE, min(IMAGE_QUAD_SIZE, gr*0.4/math.sqrt(n)))
        if n < 2: continue
        D = cdist(pos, pos); np.fill_diagonal(D, np.inf)
        mn = np.min(D, axis=1); m = float(mn.min())
        if m < g_min: g_min = m
        om = D < aqs; np.fill_diagonal(om, False)
        t_op += int(np.sum(om))//2; t_tp += n*(n-1)//2
        nn_lt_q += int(np.sum(mn < aqs))
    opr = t_op/t_tp if t_tp else 0
    iip = 0
    for img in valid:
        pn = img.parent_node
        if pn and pn.world_pos is not None:
            if np.linalg.norm(img.world_pos - pn.world_pos) < pn.sphere_radius:
                iip += 1
    nco, tnc = 0, 0
    for nd in nodes.values():
        if nd.parent and nd.world_pos is not None and nd.parent.world_pos is not None:
            tnc += 1
            if np.linalg.norm(nd.world_pos - nd.parent.world_pos) < nd.parent.sphere_radius + nd.sphere_radius:
                nco += 1
    ap = np.array([i.world_pos for i in valid])
    Da = cdist(ap, ap); np.fill_diagonal(Da, np.inf)
    nnd = np.min(Da, axis=1)
    pcts = {f"P{p}": round(float(np.percentile(nnd, p)),4) for p in [10,25,50,75,90]}
    iop = nn_lt_q/N if N else 0
    ppp = iip/N if N else 0
    nop = nco/tnc if tnc else 0
    sp = max(0, 1 - float(np.median(nnd))/0.2)
    rs = max(0, min(1, 1 - 0.4*iop - 0.3*ppp - 0.2*nop - 0.1*sp))
    if rs >= 0.90: v = "Excellent"
    elif rs >= 0.75: v = "Acceptable"
    elif rs >= 0.55: v = "Moderate concern"
    else: v = "Poor"
    return {"t_op":t_op,"t_tp":t_tp,"opr_pct":round(100*opr,4),
            "nn_lt_q":nn_lt_q,"nn_lt_q_pct":round(100*nn_lt_q/N,2),
            "g_min":round(g_min,4) if g_min<float("inf") else None,
            "iip":iip,"iip_pct":round(100*iip/N,2),
            "nco":nco,"tnc":tnc,"nco_pct":round(100*nco/tnc,2) if tnc else 0,
            "nn_min":round(float(nnd.min()),4),"nn_mean":round(float(nnd.mean()),4),
            "nn_med":round(float(np.median(nnd)),4),"nn_max":round(float(nnd.max()),4),
            "pcts":pcts,"rs":round(rs,4),"verdict":v}

# ── Main ──
def main():
    if not os.path.exists(CSV_PATH):
        print(f"ERROR: CSV not found at {CSV_PATH}"); sys.exit(1)
    print("Final Evaluation — Candidate K")
    print("="*50)
    print(f"CSV: {CSV_PATH}")

    nodes, planets, images = parse_csv(CSV_PATH)
    nn, ni, np_ = len(nodes), len(images), len(planets)
    print(f"Dataset: {nn} nodes, {ni} images, {np_} planets")

    compute_before(images)
    simulate_candidateK(planets)
    valid_count = sum(1 for i in images if i.world_pos is not None)
    print(f"Valid images: {valid_count}/{ni}")

    # ── Within-planet fidelity (PRIMARY) ──
    print("\nComputing within-planet fidelity...")
    wp = compute_within_planet_fidelity(images)
    print("\n--- PRIMARY: Within-Planet Fidelity ---")
    for pid in sorted(wp.keys()):
        d = wp[pid]
        print(f"  Planet {pid}: {d['n']:>5} images | "
              f"k=5 mean={d['k5_mean']:.6f} min={d['k5_min']:.6f} zero={d['k5_zero']} | "
              f"k=10 mean={d['k10_mean']:.6f} min={d['k10_min']:.6f} zero={d['k10_zero']}")

    # ── Global fidelity (SECONDARY) ──
    print("\nComputing global fidelity metrics...")
    f = compute_global_fidelity(images)

    # ── Readability ──
    print("Computing readability metrics...")
    r = compute_readability(nodes, planets, images)

    k5, k10 = f["k5"], f["k10"]
    cl5, cl10 = f["cl5"], f["cl10"]
    cp5, cp10 = f["cp5"], f["cp10"]
    pres = f["pres"]

    print("\n" + "="*50)
    print("RESULTS SUMMARY")
    print("="*50)

    print(f"\n--- SECONDARY: Global Fidelity ---")
    print(f"Mean k-NN overlap (k=5):   {k5['mean']:.4f}")
    print(f"Mean k-NN overlap (k=10):  {k10['mean']:.4f}")
    print(f"Median k-NN overlap (k=5): {k5['median']:.4f}")
    print(f"Min k-NN overlap (k=5):    {k5['min']:.4f}")
    print(f"Zero-overlap imgs (k=5):   {k5['zero_count']} ({k5['zero_pct']}%)")
    print(f"Zero-overlap imgs (k=10):  {k10['zero_count']} ({k10['zero_pct']}%)")
    print(f"Intra-leaf pres (k=5):     {pres['intra_pct']:.2f}% ({pres['intra_p']}/{pres['intra_t']})")
    print(f"Cross-leaf pres (k=5):     {pres['cross_pct']:.2f}% ({pres['cross_p']}/{pres['cross_t']})")

    print(f"\n--- Intrusion ---")
    print(f"Cross-leaf (k=5):  Before={cl5['before']:.2f}% After={cl5['after']:.2f}% Δ={cl5['delta']:+.2f}pp")
    print(f"Cross-leaf (k=10): Before={cl10['before']:.2f}% After={cl10['after']:.2f}% Δ={cl10['delta']:+.2f}pp")
    print(f"Cross-planet (k=5):  Before={cp5['before']:.2f}% After={cp5['after']:.2f}% Δ={cp5['delta']:+.2f}pp")
    print(f"Cross-planet (k=10): Before={cp10['before']:.2f}% After={cp10['after']:.2f}% Δ={cp10['delta']:+.2f}pp")

    print(f"\n--- Readability ---")
    print(f"Overlapping pairs: {r['t_op']}/{r['t_tp']} ({r['opr_pct']:.2f}%)")
    print(f"NN < quad size:    {r['nn_lt_q']} ({r['nn_lt_q_pct']:.1f}%)")
    print(f"Global min NN:     {r['g_min']}")
    print(f"Inside parent:     {r['iip']} ({r['iip_pct']:.1f}%)")
    print(f"Node child overlap:{r['nco']}/{r['tnc']} ({r['nco_pct']:.1f}%)")
    print(f"Median NN dist:    {r['nn_med']:.4f}")
    print(f"Mean NN dist:      {r['nn_mean']:.4f}")
    print(f"Readability score: {r['rs']:.4f}")
    print(f"Verdict:           {r['verdict']}")

    print(f"\n--- Percentiles ---")
    for kk, v in r['pcts'].items(): print(f"  {kk}: {v:.4f}")
    print(f"  Min: {r['nn_min']:.4f}  Max: {r['nn_max']:.4f}  Mean: {r['nn_mean']:.4f}")

    # Output as JSON for report generation
    results = {
        "within_planet": {str(k): v for k, v in wp.items()},
        "global_fidelity": f,
        "readability": r,
        "meta": {"nodes": nn, "images": ni, "planets": np_}
    }
    results_path = os.path.join(PROJECT_ROOT, "final_evaluation_results.json")
    with open(results_path, "w") as fp:
        json.dump(results, fp, indent=2)
    print(f"\nResults JSON: {results_path}")
    print("Done.")

if __name__ == "__main__":
    main()
