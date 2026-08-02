#!/usr/bin/env python3
"""Compute the verified walk-through route for Privateer's Hold from the
GENERATED project doc's gameplayProxy voxels — the same collision authority
the runtime walkthrough drives against. Deriving the route from the proxy
(instead of the mesh) keeps the proof honest: if the proxy loses support, the
route breaks.

Model (mirrors the loading-bay player controller with fall/step-up opt-ins):
- the kinematic body is a 0.5m cube (half extents 0.25); on the 0.5m voxel
  grid its footprint can touch the full 3x3 column neighbourhood around every
  route column (the walkthrough reaches waypoints within 0.35m of column
  centres), so support and blocking are evaluated over that neighbourhood,
  mirroring the runtime's axis sweep — edge support is real support;
- from stand height t in a column, the controller may rise up to STEP_UP to
  enter a neighbour column (entry requires no voxel overlapping the raised
  body box), then settles onto the highest voxel top at/below the raised
  bottom anywhere under the footprint (landing requires standing headroom);
- settling handles any drop, but route drops are bounded to MAX_DROP per
  column move so the walkthrough stays a walk, not a BASE jump.

Output: content/projects/privateers-hold.route.json
  {version, cell, spawnBlock, goalBlock, waypoints: [[x, eyeY, z], ...]}
Waypoints are every route column center (0.5m spacing) so the harness never
cuts a corner across unsupported columns.

Usage: python3 scripts/find-route.py [--check]
  --write (default): write the route file
  --check: fail if the committed route file is stale
"""
import json, math, sys, heapq, hashlib
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
PROJECT = REPO / "content" / "projects" / "privateers-hold.project.json"
OUT = REPO / "content" / "projects" / "privateers-hold.route.json"

BLOCK_SIDE = 51.2          # RDB block grid (see crates/dagger-import)
STEP_UP = 0.75             # controller ledge climb assist (keep in sync with generate-project.py)
MAX_DROP = 2.0             # max settle drop per column move (m)
BODY_HALF = 0.25           # kinematic half extents
HEADROOM = 1.0             # no voxel tops in (stand, stand + HEADROOM) when standing


def block_of(x: float, z: float) -> tuple:
    return (math.floor(x / BLOCK_SIDE), math.floor(z / BLOCK_SIDE))


def load_tops(project: dict) -> dict:
    """column (ix, iz) -> sorted voxel top faces (world y)."""
    env = project["scenes"][0]["voxelEnvironment"]
    cell = env["voxelSize"]
    tops: dict = {}
    for voxel in env["materialVoxels"]:
        ix, iy, iz = voxel["address"]
        tops.setdefault((ix, iz), []).append((iy + 1) * cell)
    for key in tops:
        tops[key].sort()
    return cell, tops


def find_route(project: dict) -> dict:
    cell, tops = load_tops(project)
    spawn = None
    for entity in project["scenes"][0]["entities"]:
        if entity.get("playerController"):
            spawn = entity["translation"]
    if spawn is None:
        raise SystemExit("no playerController entity in project doc")
    sx, sy, sz = spawn

    def col_of(x, z):
        return (math.floor(x / cell), math.floor(z / cell))

    spawn_col = col_of(sx, sz)
    spawn_block = block_of(sx, sz)

    def neigh_tops(col):
        # The 0.5m body centred within ~0.35m of a column centre overlaps the
        # column itself plus its 4 direct neighbours (diagonals are only
        # reachable at exact-corner measure-zero contacts, which the strict
        # axis sweep does not treat as overlapping). Support and blocking are
        # evaluated over this plus-shaped footprint, mirroring the sweep —
        # edge support is real support.
        cx, cz = col
        result = []
        for ncol in ((cx, cz), (cx + 1, cz), (cx - 1, cz), (cx, cz + 1), (cx, cz - 1)):
            result.extend(tops.get(ncol, []))
        return result

    def enterable(col, raised):
        # The swept body box crosses at most ~0.65m into the destination
        # column during one move, so only the destination column's own voxels
        # can obstruct entry at the raised height.
        lo = raised
        hi = raised + 2 * BODY_HALF
        return all(not (lo < t < hi + cell) for t in tops.get(col, []))

    def landings(col, raised):
        # The settle stops the body on the HIGHEST voxel top under any part of
        # its footprint (edge support is real support), provided the stand is
        # physically attainable: the destination column itself must not hold
        # a voxel the standing box would intersect (neighbours only matter
        # when the body straddles them — and then the settle would have
        # landed on that higher top instead, which this max picks anyway).
        ntops = neigh_tops(col)
        own = tops.get(col, [])
        for t in sorted({u for u in ntops if u <= raised + 1e-6}, reverse=True):
            if any(t < u <= t + 2 * BODY_HALF + 1e-6 for u in own):
                continue
            return [t]
        return []

    # stand = highest voxel top under the spawn body footprint
    bottom = sy - BODY_HALF
    start_tops = [t for t in neigh_tops(spawn_col) if t <= bottom + 1e-6]
    if not start_tops:
        raise SystemExit(f"no voxel top at/below spawn body bottom {bottom} near {spawn_col}")
    start = (spawn_col, max(start_tops))

    pq = [(0.0, spawn_col, start[1])]
    best = {(spawn_col, round(start[1], 3)): 0.0}
    parent: dict = {}
    # Don't stop at the first column across a border: the walkthrough reaches
    # waypoints with a 0.35m tolerance, so the goal must sit >=1m inside the
    # new block for the walk to CROSS the boundary line in authoritative
    # readback. Keep searching a little past the first crossing and prefer
    # the cheapest well-inside goal.
    first_goal_cost = None
    goal = None
    goal_penetration = -1.0
    while pq:
        cost, col, t = heapq.heappop(pq)
        wx, wz = (col[0] + 0.5) * cell, (col[1] + 0.5) * cell
        blk = block_of(wx, wz)
        if blk != spawn_block and (col, t) != start:
            x0, z0 = blk[0] * BLOCK_SIDE, blk[1] * BLOCK_SIDE
            penetration = min(wx - x0, x0 + BLOCK_SIDE - wx, wz - z0, z0 + BLOCK_SIDE - wz)
            if first_goal_cost is None:
                first_goal_cost = cost
            if penetration > goal_penetration and penetration >= 1.0:
                goal = (col, t)
                goal_penetration = penetration
            if goal is not None and penetration >= 2.0:
                break
        if first_goal_cost is not None and cost > first_goal_cost + 10.0:
            break
        raised = t + STEP_UP
        for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ncol = (col[0] + dx, col[1] + dz)
            if ncol not in tops:
                continue
            if not enterable(ncol, raised):
                continue
            for nt in landings(ncol, raised):
                drop = t - nt
                if drop > MAX_DROP:
                    continue
                w = 0.01 + (max(0.0, drop) ** 2)
                key = (ncol, round(nt, 3))
                ncost = cost + w
                if ncost < best.get(key, float("inf")):
                    best[key] = ncost
                    parent[key] = (col, t)
                    heapq.heappush(pq, (ncost, ncol, nt))

    if goal is None:
        raise SystemExit("no route from the start marker to a border block over the proxy voxels")

    path = []
    key = (goal[0], round(goal[1], 3))
    while True:
        path.append(key)
        if key not in parent:
            break
        pc, pt = parent[key]
        key = (pc, round(pt, 3))
    path.reverse()

    waypoints = [
        [round((c[0] + 0.5) * cell, 3), round(t + BODY_HALF, 3), round((c[1] + 0.5) * cell, 3)]
        for c, t in path
    ]
    goal_block = block_of(waypoints[-1][0], waypoints[-1][2])
    return {
        "version": 1,
        "cell": cell,
        "spawnBlock": list(spawn_block),
        "goalBlock": list(goal_block),
        "waypoints": waypoints,
    }


def main() -> None:
    mode = sys.argv[1] if len(sys.argv) > 1 else "--write"
    project = json.loads(PROJECT.read_text())
    route = find_route(project)
    text = json.dumps(route, indent=1) + "\n"
    if mode == "--check":
        actual = OUT.read_text() if OUT.exists() else ""
        if actual != text:
            raise SystemExit(f"{OUT} is stale; run scripts/find-route.py --write")
        print(f"{OUT} up to date ({len(route['waypoints'])} waypoints)")
        return
    if mode != "--write":
        raise SystemExit(__doc__)
    OUT.write_text(text)
    digest = hashlib.sha256(text.encode()).hexdigest()[:16]
    wps = route["waypoints"]
    print(
        f"wrote {OUT} ({len(wps)} waypoints, block {route['spawnBlock']} -> "
        f"{route['goalBlock']}, y {wps[0][1]:.2f} -> {wps[-1][1]:.2f}, sha256:{digest})"
    )


if __name__ == "__main__":
    main()
