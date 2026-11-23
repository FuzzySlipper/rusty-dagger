#!/usr/bin/env python3
"""Save Solv3R checkpoints and localize reserved frames against frozen training views."""
import argparse
import json
from pathlib import Path
from time import perf_counter

import numpy as np
import torch
from solv3r import Solv3R, load_images
from solv3r.model import Context, cameras_from_pose_enc, cameras_from_rays

TRAIN = (0, 6, 8, 10, 12, 13, 15, 17, 18, 20, 22, 23, 25, 26, 28, 30, 32)
RESERVED = (7, 11, 16, 21, 24, 29)


def context_on(context, device):
    return Context(context.x0.to(device), context.pos.to(device), context.H, context.W)


def decode(model, state, context, chunk_size=None):
    if chunk_size and context.S > chunk_size:
        decoded = {}
        for start in range(0, context.S, chunk_size):
            stop = min(start + chunk_size, context.S)
            indices = list(range(start, stop))
            piece = decode(model, state[indices], context.subset(indices))
            for key, value in piece.items():
                if key not in decoded:
                    decoded[key] = np.empty((context.S, *value.shape[1:]), dtype=value.dtype)
                decoded[key][start:stop] = value
        return decoded
    pred = model.decode(state.to(model.device), context_on(context, model.device))
    c2w, intrinsics = cameras_from_rays(pred["rays"], pred["depth_conf"])
    head_pose, head_intrinsics = cameras_from_pose_enc(
        pred["pose_enc"], (context.H, context.W)
    )
    return {
        "points": pred["world_points"].float().cpu().numpy(),
        "confidence": pred["depth_conf"].float().cpu().numpy(),
        "c2w": c2w.float().cpu().numpy(),
        "K": intrinsics.float().cpu().numpy(),
        "head_c2w": head_pose.numpy(),
        "head_K": head_intrinsics.numpy(),
    }


def context_indices(indices, anchors, limit):
    if len(indices) <= limit:
        return indices
    fixed = [index for index in indices if index < anchors]
    if len(fixed) >= limit:
        positions = np.linspace(0, len(fixed) - 1, limit).round().astype(int)
        return [fixed[position] for position in positions]
    other = [index for index in indices if index >= anchors]
    positions = np.linspace(0, len(other) - 1, limit - len(fixed)).round().astype(int)
    return [*fixed, *(other[position] for position in positions)]


def refine_with_cache(model, state, context, calls, new_count, active_limit, wide_count, context_limit):
    """Refine incoming views against cached geometry, revisiting shared wide anchors."""
    if context.S <= active_limit:
        return model.solve(context_on(context, model.device), calls,
                           z=state.to(model.device)).to(state.device)
    held_count = context.S - new_count
    anchors = min(8, held_count - 1, active_limit - new_count)
    old = np.linspace(1, min(wide_count, held_count) - 1, anchors).round().astype(int).tolist()
    active = [*old, *range(held_count, context.S)]
    active_set = set(active)
    frozen = context_indices([index for index in range(context.S) if index not in active_set],
                             wide_count, context_limit)
    with torch.autocast("cuda", dtype=torch.bfloat16, cache_enabled=False):
        cache = model.cache(state[frozen].to(model.device), context_on(context.subset(frozen), model.device))
        active_state = state[active].to(model.device)
        active_context = context_on(context.subset(active), model.device)
        for _ in range(calls):
            active_state = model.step_cached(active_state, active_context, cache)
        state[active] = active_state.to(state.device)
    return state


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("images", type=Path)
    parser.add_argument("checkpoint")
    parser.add_argument("out", type=Path)
    parser.add_argument("--size", type=int, default=392)
    parser.add_argument("--calls", type=int, nargs="+", default=(8, 16, 32))
    parser.add_argument("--split", type=Path, help="JSON train and reserved capture indices")
    parser.add_argument("--join-calls", type=int, default=16, help="calls after adding each batch of views")
    parser.add_argument("--active-views", type=int, help="maximum active views for cached batches")
    parser.add_argument("--decode-chunk", type=int, help="decode this many views at a time into CPU arrays")
    parser.add_argument("--context-views", type=int, default=96, help="maximum cached context views in the bounded update mode")
    parser.add_argument("--offload-state", action="store_true", help="keep inactive image states in system RAM during cached updates")
    args = parser.parse_args()
    if args.offload_state:
        args.active_views = args.active_views or 24
        args.decode_chunk = args.decode_chunk or 8
    split = json.loads(args.split.read_text()) if args.split else {"train": TRAIN, "reserved": RESERVED}
    added = split.get("join", [])
    batches = split.get("join_batches", [])
    cached_from = split.get("cached_from_batch", len(batches))
    save_batches = split.get("save_batches")
    if added and batches:
        parser.error("use join or join_batches, since both specify views to add")
    added_captures = added or [capture for batch in batches for capture in batch]
    captures = [*split["train"], *added_captures, *split["reserved"]]
    if not split["train"] or not split["reserved"] or len(set(captures)) != len(captures):
        parser.error("train and reserved must be nonempty and use distinct capture indices")
    if args.active_views and any(len(batch) >= args.active_views for batch in batches[cached_from:]):
        parser.error("cached batches need room for at least one earlier anchor view")
    args.out.mkdir(parents=True, exist_ok=True)
    torch.set_num_threads(2)
    started = perf_counter()
    model = Solv3R.from_pretrained(args.checkpoint)
    torch.cuda.synchronize()
    model_ready = perf_counter()
    names = [f"capture_{i:04d}.png" for i in captures]
    images = load_images([str(args.images / name) for name in names], size=(args.size,) * 2)
    count = len(split["train"])
    wide_count = split.get("cached_anchors", count)
    images_ready = perf_counter()
    context = model.encode(images[:count])
    reserved_start = count + len(added_captures)
    queries = [model.encode(image[None], reference_first=False) for image in images[reserved_start:]]
    state = model.start(context)
    torch.cuda.synchronize()
    encoded = perf_counter()
    timings = []
    schedule = []
    previous = 0
    for calls in sorted(set(args.calls)):
        schedule.append((f"solve-{calls:02d}", calls, calls - previous, added if calls == max(args.calls) else [], False, True))
        previous = calls
    for number, batch in enumerate(batches, 1):
        save = save_batches is None or number in save_batches
        schedule.append((f"solve-pass-{number:02d}", previous, 0, batch,
                         args.active_views is not None and number > cached_from, save))
    for label, calls, steps, batch, cached, save in schedule:
        block_start = perf_counter()
        if steps:
            state = model.solve(context, steps, z=state)
        torch.cuda.synchronize()
        solved = perf_counter()
        if batch:
            offset = context.S
            if cached and args.offload_state:
                state = state.cpu()
                context = context_on(context, "cpu")
                incoming = model.encode(images[offset:offset + len(batch)], reference_first=False)
                state = torch.cat([state, model.start(incoming).cpu()])
                context = Context.cat([context, context_on(incoming, "cpu")])
                del incoming
            else:
                state, context = model.add_views(state, context, images[offset:offset + len(batch)])
            if cached:
                state = refine_with_cache(model, state, context, args.join_calls, len(batch),
                                          args.active_views, wide_count, args.context_views)
            else:
                state = model.solve(context, args.join_calls, z=state)
            torch.cuda.synchronize()
        joined_at = perf_counter()
        if not save:
            timings.append({
                "checkpoint": label, "saved": False, "cached": cached, "views": context.S,
                "join_seconds": joined_at - solved, "elapsed_seconds": joined_at - started,
            })
            mode = "cached context" if cached else "joint solving"
            print(f"{label}: {context.S} views updated with {mode}", flush=True)
            continue
        reconstruction = decode(model, state, context, args.decode_chunk)
        torch.cuda.synchronize()
        decoded_at = perf_counter()
        if args.active_views:
            held = context_indices(list(range(context.S)), wide_count, args.context_views)
            with torch.autocast("cuda", dtype=torch.bfloat16, cache_enabled=False):
                cache = model.cache(state[held].to(model.device), context_on(context.subset(held), model.device))
        else:
            cache = model.cache(state, context)
        reserved = []
        for query in queries:
            query_state = model.start(query)
            with torch.autocast("cuda", dtype=torch.bfloat16, cache_enabled=False):
                for _ in range(16):
                    query_state = model.step_cached(query_state, query, cache)
            decoded = decode(model, query_state, query)
            reserved.append({key: decoded[key] for key in ("c2w", "K", "head_c2w", "head_K")})
        solved_count = context.S
        for key in reserved[0]:
            reconstruction[key] = np.concatenate([reconstruction[key], *(r[key] for r in reserved)])
        localized = perf_counter()
        np.savez(args.out / f"{label}.npz",
                 names=[*names[:solved_count], *names[reserved_start:]], base_count=count, **reconstruction)
        saved = perf_counter()
        timings.append({
            "checkpoint": label, "calls": calls, "views": solved_count, "joint_seconds": solved - block_start,
            "saved": True, "cached": cached,
            "join_seconds": joined_at - solved if batch else 0.0,
            "decode_seconds": decoded_at - joined_at, "reserved_seconds": localized - decoded_at,
            "write_seconds": saved - localized, "elapsed_seconds": saved - started,
        })
        print(f"{label}: {solved_count} solved views, {len(queries)} reserved cameras", flush=True)
    (args.out / "split.json").write_text(json.dumps(split, indent=2))
    (args.out / "timing.json").write_text(json.dumps({
        "size": args.size, "views": count + len(added_captures), "initial_views": count, "reserved": len(queries),
        "active_views": args.active_views, "context_views": args.context_views,
        "decode_chunk": args.decode_chunk, "offload_state": args.offload_state,
        "load_model_seconds": model_ready - started, "load_images_seconds": images_ready - model_ready,
        "encode_seconds": encoded - images_ready, "checkpoints": timings,
        "peak_cuda_gib": torch.cuda.max_memory_allocated() / 2**30,
    }, indent=2) + "\n")
    print(f"peak CUDA allocation: {torch.cuda.max_memory_allocated() / 2**30:.2f} GiB", flush=True)


if __name__ == "__main__":
    main()
