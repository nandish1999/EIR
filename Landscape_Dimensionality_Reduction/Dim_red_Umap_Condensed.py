#LANDSCAPE DIMENSIONALITY REDUCTION + HIERARCHICAL CLUSTERING
import os
import numpy as np
import pandas as pd
import umap
import hdbscan
import cv2

from sklearn.preprocessing import StandardScaler
from sklearn.neighbors import NearestNeighbors
from sklearn.metrics import rand_score
from collections import defaultdict

# ============================================================
# CONFIG
# ============================================================
BASE_DIR      = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FOLDER = os.path.join(BASE_DIR, "outputs")
os.makedirs(OUTPUT_FOLDER, exist_ok=True)

RANDOM_STATE = 42
MAX_CHILDREN = 8
RESCUE_K     = 5

UMAP_N_NEIGHBORS = 30
UMAP_MIN_DIST    = 0.1
UMAP_METRIC      = "cosine"

MAIN_MIN_CLUSTER_SIZE      = 25
MAIN_MIN_SAMPLES           = 5
MAIN_CLUSTER_SELECTION     = "eom"
MAIN_CLUSTER_SELECTION_EPS = 0.3

SUB_MIN_CLUSTER_SIZE      = 5
SUB_MIN_SAMPLES           = 2
SUB_CLUSTER_SELECTION     = "eom"
SUB_CLUSTER_SELECTION_EPS = 0.2

OUTPUT_CSV = os.path.join(OUTPUT_FOLDER, "unity_pruned_density_tree_3d_colors.csv")

# ============================================================
# LOAD DATA
# ============================================================
print("Loading data...")

features     = np.load(os.path.join(OUTPUT_FOLDER, "resnet_features.npy"))
labels       = np.load(os.path.join(OUTPUT_FOLDER, "resnet_labels.npy"))
image_ids    = np.load(os.path.join(OUTPUT_FOLDER, "image_ids.npy"))
image_colors = np.load(os.path.join(OUTPUT_FOLDER, "image_colors.npy"))

print(f"Loaded {len(features)} samples across {len(np.unique(labels))} classes")

# ============================================================
# STANDARDIZE
# ============================================================
scaler          = StandardScaler()
features_scaled = scaler.fit_transform(features)

# ============================================================
# NOISE RESCUE
# ============================================================
def rescue_noise(embedding, cluster_labels, k=RESCUE_K):
    noise_mask  = cluster_labels == -1
    known_mask  = ~noise_mask
    new_labels  = cluster_labels.copy()
    still_noise = np.zeros(len(cluster_labels), dtype=bool)

    if not noise_mask.any() or not known_mask.any():
        return new_labels, still_noise

    nbrs = NearestNeighbors(n_neighbors=k, algorithm="auto").fit(
        embedding[known_mask]
    )
    _, indices      = nbrs.kneighbors(embedding[noise_mask])
    known_labels    = cluster_labels[known_mask]
    noise_positions = np.where(noise_mask)[0]

    for i, neighbour_idxs in enumerate(indices):
        neighbour_labels = known_labels[neighbour_idxs]
        valid            = neighbour_labels[neighbour_labels != -1]
        if len(valid) == 0:
            still_noise[noise_positions[i]] = True
            continue
        counts = np.bincount(valid.astype(int) + 1)
        new_labels[noise_positions[i]] = int(counts.argmax()) - 1

    return new_labels, still_noise

# ============================================================
# HELPERS
# ============================================================
def lab_to_rgb(lab):
    lab_arr = np.array(lab, dtype=np.uint8).reshape(1, 1, 3)
    rgb     = cv2.cvtColor(lab_arr, cv2.COLOR_LAB2RGB)
    return rgb[0, 0] / 255.0

def nid(planet_id, node):
    node_clean = int(node) if isinstance(node, (float, np.floating)) else node
    return f"{planet_id}_node_{node_clean}"

def compute_depth(node, parent_map):
    depth = 0
    cur   = node
    while cur in parent_map:
        depth += 1
        cur    = parent_map[cur]
    return depth + 1

# ============================================================
# POST-PROCESSING - collapse branch-only nodes
# ============================================================
def collapse_branch_only_nodes(df):
    changed       = True
    total_removed = 0

    while changed:
        changed  = False
        node_ids = set(df["node_id"].dropna())

        branch_only = []
        for node_id in node_ids:
            row = df[df["node_id"] == node_id]
            if len(row) == 0:
                continue
            row = row.iloc[0]
            if row["depth"] == 0 or row["type"] != "node":
                continue
            children = df[df["parent_id"] == node_id]
            if len(children) > 0 and (children["type"] == "node").all():
                branch_only.append(node_id)

        if not branch_only:
            break

        for node_id in branch_only:
            grandparent = df[df["node_id"] == node_id]["parent_id"].values[0]
            df.loc[df["parent_id"] == node_id, "parent_id"] = grandparent
            df = df[df["node_id"] != node_id].copy()

        total_removed += len(branch_only)
        changed = True

    print(f"  Branch-only nodes removed : {total_removed}")
    return df

# ============================================================
# DUAL UMAP
# ============================================================
print("Fitting UMAP 10D ...")
umap_10d = umap.UMAP(
    n_components = 10,
    n_neighbors  = UMAP_N_NEIGHBORS,
    min_dist     = UMAP_MIN_DIST,
    metric       = UMAP_METRIC,
    random_state = RANDOM_STATE,
    low_memory   = False,
)
embedding_10d = umap_10d.fit_transform(features_scaled)

print("Fitting UMAP 3D ...")
umap_3d = umap.UMAP(
    n_components = 3,
    n_neighbors  = UMAP_N_NEIGHBORS,
    min_dist     = UMAP_MIN_DIST,
    metric       = UMAP_METRIC,
    random_state = RANDOM_STATE,
    low_memory   = False,
)
embedding_3d = umap_3d.fit_transform(features_scaled)

# ============================================================
# MAIN HDBSCAN
# ============================================================
print("Running main HDBSCAN ...")
main_clusterer = hdbscan.HDBSCAN(
    min_cluster_size          = MAIN_MIN_CLUSTER_SIZE,
    min_samples               = MAIN_MIN_SAMPLES,
    cluster_selection_method  = MAIN_CLUSTER_SELECTION,
    cluster_selection_epsilon = MAIN_CLUSTER_SELECTION_EPS,
    prediction_data           = True,
)
raw_main_labels = main_clusterer.fit_predict(embedding_10d)

noise_before = (raw_main_labels == -1).sum()
main_labels, main_still_noise = rescue_noise(embedding_10d, raw_main_labels)

n_clusters     = len(set(main_labels[~main_still_noise]))
unique, counts = np.unique(main_labels[~main_still_noise], return_counts=True)
cluster_sizes  = dict(zip(unique.tolist(), counts.tolist()))

print("\n" + "="*50)
print("CLUSTER DETECTION SUMMARY")
print("="*50)
print(f"Total images          : {len(features)}")
print(f"Clusters detected     : {n_clusters}")
print(f"Noise points rescued  : {int(noise_before - main_still_noise.sum())}")
print(f"Unrescued (noise)     : {int(main_still_noise.sum())}")
print(f"Rand Score            : {round(rand_score(labels, main_labels), 4)}")
print(f"\nCluster sizes:")
for cid, csize in sorted(cluster_sizes.items()):
    bar = "#" * (csize * 30 // max(counts))
    print(f"  Cluster {cid:>3d} : {csize:>5d} images  {bar}")
print("="*50)

# ============================================================
# BUILD TREE
# ============================================================
rows                 = []
total_images_written = 0

noise_global_idx = np.where(main_still_noise)[0]
if len(noise_global_idx) > 0:
    nc_xyz = embedding_3d[noise_global_idx].mean(axis=0)
    nc_rgb = lab_to_rgb(image_colors[noise_global_idx].mean(axis=0))
    rows.append({
        "type":      "noise_cluster",
        "node_id":   "noise_cluster",
        "parent_id": "root",
        "planet_id": -1,
        "depth":     0,
        "size":      len(noise_global_idx),
        "x": float(nc_xyz[0]), "y": float(nc_xyz[1]), "z": float(nc_xyz[2]),
        "r": float(nc_rgb[0]), "g": float(nc_rgb[1]), "b": float(nc_rgb[2]),
        "image_id":  None,
    })
    for gi in noise_global_idx:
        rows.append({
            "type":      "noise_image",
            "node_id":   None,
            "parent_id": "noise_cluster",
            "planet_id": -1,
            "depth":     1,
            "size":      1,
            "x": float(embedding_3d[gi][0]),
            "y": float(embedding_3d[gi][1]),
            "z": float(embedding_3d[gi][2]),
            "r": None, "g": None, "b": None,
            "image_id":  image_ids[gi],
        })

for planet_id in np.unique(main_labels):

    if planet_id == -1:
        continue

    idx        = np.where((main_labels == planet_id) & ~main_still_noise)[0]
    subset_10d = embedding_10d[idx]
    subset_3d  = embedding_3d[idx]

    sub_clusterer = hdbscan.HDBSCAN(
        min_cluster_size          = SUB_MIN_CLUSTER_SIZE,
        min_samples               = SUB_MIN_SAMPLES,
        cluster_selection_method  = SUB_CLUSTER_SELECTION,
        cluster_selection_epsilon = SUB_CLUSTER_SELECTION_EPS,
    )
    sub_clusterer.fit(subset_10d)

    raw_sub_labels              = sub_clusterer.labels_
    sub_labels, sub_still_noise = rescue_noise(subset_10d, raw_sub_labels)

    condensed     = sub_clusterer.condensed_tree_.to_pandas()
    n_samples_sub = len(subset_10d)

    leaf_rows_ct = condensed[condensed["child"] < n_samples_sub]
    point_to_cluster_raw = {}
    for _, r in leaf_rows_ct.iterrows():
        point_to_cluster_raw[int(r["child"])] = int(r["parent"])

    children_map = defaultdict(list)
    parent_map   = {}

    for _, r in condensed.iterrows():
        p = r["parent"]
        c = r["child"]
        children_map[p].append(c)
        parent_map[c] = p

    new_children_map = defaultdict(list)
    new_parent_map   = dict(parent_map)
    group_counter    = 0

    for parent, children in children_map.items():
        if len(children) <= MAX_CHILDREN:
            new_children_map[parent] = children
            continue
        for i in range(0, len(children), MAX_CHILDREN):
            chunk      = children[i : i + MAX_CHILDREN]
            group_node = f"{parent}_grp_{group_counter}"
            group_counter += 1
            new_children_map[parent].append(group_node)
            new_children_map[group_node] = chunk
            for child in chunk:
                new_parent_map[child] = group_node

    children_map = new_children_map
    parent_map   = new_parent_map

    all_nodes = set(children_map.keys())
    for ch in children_map.values():
        all_nodes.update(ch)

    def resolve_cluster(cl, pm):
        cur = cl
        while cur is not None and cur not in all_nodes:
            cur = pm.get(cur)
        return cur

    point_to_cluster = {
        pt: resolve_cluster(cl, parent_map)
        for pt, cl in point_to_cluster_raw.items()
    }

    leaf_members = defaultdict(list)
    for pt, cl in point_to_cluster.items():
        if cl is not None:
            leaf_members[cl].append(pt)

    assigned      = set(pt for pt, cl in point_to_cluster.items() if cl is not None)
    all_local_pts = set(range(n_samples_sub))
    orphan_locals = list(all_local_pts - assigned)

    node_members = {leaf: set(members) for leaf, members in leaf_members.items()}
    for node in all_nodes:
        if node not in node_members:
            node_members[node] = set()
        for child in children_map.get(node, []):
            node_members[node] |= node_members.get(child, set())

    node_sizes = {n: len(m) for n, m in node_members.items() if len(m) > 0}

    pruned_nodes = set()
    for node, size in node_sizes.items():
        parent      = parent_map.get(node)
        parent_size = node_sizes.get(parent)
        if parent is None or parent_size != size:
            pruned_nodes.add(node)

    planet_node_id  = f"planet_{planet_id}"
    planet_centroid = subset_3d.mean(axis=0)
    planet_lab      = image_colors[idx].mean(axis=0)
    planet_rgb      = lab_to_rgb(planet_lab)

    rows.append({
        "type":      "node",
        "node_id":   planet_node_id,
        "parent_id": "root",
        "planet_id": int(planet_id),
        "depth":     0,
        "size":      len(idx),
        "x": float(planet_centroid[0]),
        "y": float(planet_centroid[1]),
        "z": float(planet_centroid[2]),
        "r": float(planet_rgb[0]),
        "g": float(planet_rgb[1]),
        "b": float(planet_rgb[2]),
        "image_id":  None,
    })

    for node in pruned_nodes:
        members = node_members[node]
        if not members:
            continue

        centroid    = subset_3d[list(members)].mean(axis=0)
        cluster_lab = image_colors[idx[list(members)]].mean(axis=0)
        cluster_rgb = lab_to_rgb(cluster_lab)

        parent = parent_map.get(node)
        while parent not in pruned_nodes and parent in parent_map:
            parent = parent_map.get(parent)

        parent_id = (
            nid(planet_id, parent)
            if parent in pruned_nodes
            else planet_node_id
        )

        rows.append({
            "type":      "node",
            "node_id":   nid(planet_id, node),
            "parent_id": parent_id,
            "planet_id": int(planet_id),
            "depth":     compute_depth(node, parent_map),
            "size":      len(members),
            "x": float(centroid[0]),
            "y": float(centroid[1]),
            "z": float(centroid[2]),
            "r": float(cluster_rgb[0]),
            "g": float(cluster_rgb[1]),
            "b": float(cluster_rgb[2]),
            "image_id":  None,
        })

    for leaf, members in leaf_members.items():

        if leaf in pruned_nodes:
            parent_node = nid(planet_id, leaf)
        else:
            ancestor = parent_map.get(leaf)
            while ancestor is not None and ancestor not in pruned_nodes:
                ancestor = parent_map.get(ancestor)
            parent_node = (
                nid(planet_id, ancestor)
                if ancestor in pruned_nodes
                else planet_node_id
            )

        image_depth = compute_depth(leaf, parent_map) + 1

        for local_i in members:
            global_i = idx[local_i]
            rows.append({
                "type":      "image",
                "node_id":   None,
                "parent_id": parent_node,
                "planet_id": int(planet_id),
                "depth":     image_depth,
                "size":      1,
                "x": float(embedding_3d[global_i][0]),
                "y": float(embedding_3d[global_i][1]),
                "z": float(embedding_3d[global_i][2]),
                "r": None, "g": None, "b": None,
                "image_id":  image_ids[global_i],
            })
            total_images_written += 1

    for local_i in orphan_locals:
        global_i = idx[local_i]
        rows.append({
            "type":      "image",
            "node_id":   None,
            "parent_id": planet_node_id,
            "planet_id": int(planet_id),
            "depth":     1,
            "size":      1,
            "x": float(embedding_3d[global_i][0]),
            "y": float(embedding_3d[global_i][1]),
            "z": float(embedding_3d[global_i][2]),
            "r": None, "g": None, "b": None,
            "image_id":  image_ids[global_i],
        })
        total_images_written += 1

# ============================================================
# POST-PROCESSING - collapse branch-only nodes
# ============================================================
df = pd.DataFrame(rows)

print("\nPost-processing: collapsing branch-only nodes ...")
df = collapse_branch_only_nodes(df)

# ============================================================
# EXPORT
# ============================================================
df.to_csv(OUTPUT_CSV, index=False)

all_node_ids    = set(df["node_id"].dropna().tolist()) | {"root"}
img_rows        = df[df["type"].isin(["image", "noise_image"])]
bad             = img_rows[~img_rows["parent_id"].isin(all_node_ids)]

node_ids_set    = set(df["node_id"].dropna())
used_as_parents = set(df["parent_id"].dropna())
ghost_nodes     = node_ids_set - used_as_parents - {
    f"planet_{p}" for p in np.unique(main_labels) if p != -1
}

branch_only_remaining = 0
for node_id in node_ids_set:
    row = df[df["node_id"] == node_id]
    if len(row) == 0:
        continue
    if row.iloc[0]["depth"] == 0:
        continue
    children = df[df["parent_id"] == node_id]
    if len(children) > 0 and (children["type"] == "node").all():
        branch_only_remaining += 1

total_imgs   = len(df[df["type"] == "image"])
total_noise  = len(df[df["type"] == "noise_image"])
total_in_csv = total_imgs + total_noise

print(f"\n" + "="*50)
print("OUTPUT SUMMARY")
print("="*50)
print(f"Input images           : {len(features)}")
print(f"Classified images      : {total_imgs}")
print(f"Noise images           : {total_noise}")
print(f"Retention rate         : {total_in_csv / len(features) * 100:.1f}%")
print(f"Orphaned images        : {len(bad)}  <- should be 0")
print(f"Ghost nodes            : {len(ghost_nodes)}  <- should be 0")
print(f"Branch-only nodes left : {branch_only_remaining}  <- should be 0")
if ghost_nodes:
    print(f"  Ghost IDs: {list(ghost_nodes)[:10]}")
print(f"\nDepth breakdown:")
for d, grp in df.groupby("depth"):
    n  = (grp["type"] == "node").sum()
    i  = (grp["type"] == "image").sum()
    ns = (grp["type"] == "noise_image").sum()
    print(f"  depth {int(d):2d}: {len(grp):6d} rows  "
          f"({n} nodes, {i} images, {ns} noise)")
print(f"Saved: {OUTPUT_CSV}")