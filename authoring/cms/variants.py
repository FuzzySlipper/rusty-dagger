"""Author, validate, search models for, and bake WorldBlockVariantRecipe entries in content packs."""

from __future__ import annotations

import json
import math
from pathlib import Path
import re
import subprocess
from typing import Callable

from cache import FileCache, dir_stamp, file_stamp
from packs import PackStore, ValidationError, ValidationIssue
from router import BadRequestError, NotFoundError

VARIANT_NAME_RE = re.compile(r"^[A-Za-z0-9_.-]+$")
MODEL_FILE_RE = re.compile(r"^mesh-(\d+)\.rstatmsh$")
PACK_ID_RE = re.compile(r"^[a-z0-9][a-z0-9._-]*$")


def _is_finite_number(value) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def _round_coord(value: float) -> float:
    rounded = round(float(value), 4)
    return 0.0 if rounded == 0.0 else rounded


def _block_slug(block_name: str) -> str:
    return block_name.strip().lower().replace(".", "-")


def validate_variant_recipe(block: str, variant: str, remove, add) -> dict:
    """Validate a WorldBlockVariantRecipe against WorldBlockVariantRecipe.cs invariants."""
    issues: list[ValidationIssue] = []

    if not isinstance(block, str) or not block.strip():
        issues.append(ValidationIssue("block", "Block name is required (e.g. MARKAA01.RMB)."))
        norm_block = ""
    else:
        norm_block = block.strip()
        if not norm_block.upper().endswith(".RMB"):
            issues.append(ValidationIssue("block", f"Block '{norm_block}' must be an RMB exterior (*.RMB)."))

    if not isinstance(variant, str) or not variant.strip():
        issues.append(ValidationIssue("variant", "Variant name is required."))
        norm_variant = ""
    else:
        norm_variant = variant.strip()
        if norm_variant == "-" or not VARIANT_NAME_RE.match(norm_variant):
            issues.append(
                ValidationIssue(
                    "variant",
                    "Variant name may contain only letters, digits, '.', '_' and '-', and cannot be '-'.",
                ),
            )

    clean_remove: list[str] = []
    if remove is None:
        remove = []
    if not isinstance(remove, list):
        issues.append(ValidationIssue("remove", "Remove list must be an array of placement IDs."))
    else:
        seen: set[str] = set()
        for idx, item in enumerate(remove):
            if not isinstance(item, str) or not item.strip():
                issues.append(ValidationIssue(f"remove[{idx}]", "Placement ID must be a non-empty string."))
                continue
            rid = item.strip()
            if rid in seen:
                issues.append(ValidationIssue(f"remove[{idx}]", f"Duplicate removal '{rid}'."))
                continue
            seen.add(rid)
            clean_remove.append(rid)

    clean_add: list[dict] = []
    if add is None:
        add = []
    if not isinstance(add, list):
        issues.append(ValidationIssue("add", "Added models must be an array."))
    else:
        for idx, item in enumerate(add):
            path = f"add[{idx}]"
            if not isinstance(item, dict):
                issues.append(ValidationIssue(path, "Added model entry must be an object."))
                continue
            raw_model = item.get("model")
            model_str = str(raw_model).strip() if isinstance(raw_model, (str, int)) and not isinstance(raw_model, bool) else ""
            if not model_str or not model_str.isdigit():
                issues.append(ValidationIssue(f"{path}.model", "Model must be a non-negative integer ARCH3D ID."))
            pos = item.get("position")
            if not isinstance(pos, dict):
                issues.append(ValidationIssue(f"{path}.position", "Position must be an object with x, y, z."))
                clean_pos = {"x": 0.0, "y": 0.0, "z": 0.0}
            else:
                px, py, pz = pos.get("x"), pos.get("y"), pos.get("z")
                if not (_is_finite_number(px) and _is_finite_number(py) and _is_finite_number(pz)):
                    issues.append(ValidationIssue(f"{path}.position", "Position coordinates x, y, z must be finite numbers."))
                    clean_pos = {"x": 0.0, "y": 0.0, "z": 0.0}
                else:
                    clean_pos = {
                        "x": _round_coord(px),
                        "y": _round_coord(py),
                        "z": _round_coord(pz),
                    }
            yaw = item.get("yawDegrees", 0)
            if not _is_finite_number(yaw):
                issues.append(ValidationIssue(f"{path}.yawDegrees", "yawDegrees must be a finite number."))
                clean_yaw = 0.0
            else:
                clean_yaw = _round_coord(yaw)
            clean_add.append({
                "model": model_str,
                "position": clean_pos,
                "yawDegrees": clean_yaw,
            })

    if not issues and not clean_remove and not clean_add:
        issues.append(ValidationIssue("recipe", "A block variant must remove at least one placement or add at least one model."))

    if issues:
        raise ValidationError(issues[0].message, issues)

    recipe: dict = {
        "block": norm_block,
        "variant": norm_variant,
    }
    if clean_remove:
        recipe["remove"] = clean_remove
    if clean_add:
        recipe["add"] = clean_add
    return recipe


class WorldVariantsStore:
    """Manage WorldBlockVariantRecipe entries across content packs and bake them via Daggerfall.Source.Tool."""

    def __init__(
        self,
        library,
        cache: FileCache,
        packs: PackStore,
        bake_runner: Callable[[Path, Path, Path], tuple[int, str]] | None = None,
    ):
        self.library = library
        self.cache = cache
        self.packs = packs
        self._bake_runner = bake_runner

    def _pack_dirs(self) -> list[tuple[str, Path]]:
        root = self.library.packs_root
        if not root.is_dir():
            return []
        found = []
        for child in sorted(root.iterdir()):
            if child.is_dir() and (child / "payload.json").is_file():
                found.append((child.name, child))
        # Prefer daggerfall.quests first for author ergonomics
        found.sort(key=lambda item: (0 if item[0] == "daggerfall.quests" else 1, item[0]))
        return found

    def _resolve_pack(self, pack_id: str) -> Path:
        if not isinstance(pack_id, str) or not PACK_ID_RE.match(pack_id.strip()):
            raise ValidationError("Invalid pack ID.", [ValidationIssue("packId", "Pack ID is required.")])
        pid = pack_id.strip()
        root = self.library.packs_root.resolve()
        pack_dir = (root / pid).resolve()
        if not pack_dir.is_relative_to(root) or not (pack_dir / "payload.json").is_file():
            raise NotFoundError(f"Content pack '{pid}' not found.")
        return pack_dir

    def _baked_info(self, pack_dir: Path, block: str, variant: str) -> dict:
        rel = f"variants/rmb/{_block_slug(block)}/exterior-variant-{variant}.json"
        baked_path = pack_dir / rel
        if not baked_path.is_file():
            return {
                "bakedExists": False,
                "bakedPath": rel,
                "baseDigest": None,
                "bakedModelCount": None,
            }
        try:
            doc = self.packs.read_json(baked_path, {}) or {}
        except Exception:
            doc = {}
        return {
            "bakedExists": True,
            "bakedPath": rel,
            "baseDigest": doc.get("baseDigest"),
            "bakedModelCount": len(doc.get("models") or []),
        }

    def list_variants(self, block: str | None = None) -> dict:
        target_block = block.strip().upper() if isinstance(block, str) and block.strip() else None
        pack_list = []
        variants = []
        for pack_id, pack_dir in self._pack_dirs():
            payload_path = pack_dir / "payload.json"
            doc = self.packs.read_json(payload_path, {}) or {}
            wv = doc.get("worldVariants")
            blocks = wv.get("blocks") if isinstance(wv, dict) and isinstance(wv.get("blocks"), list) else []
            pack_list.append({
                "id": pack_id,
                "variantCount": len(blocks),
            })
            for entry in blocks:
                if not isinstance(entry, dict):
                    continue
                bname = str(entry.get("block") or "")
                vname = str(entry.get("variant") or "")
                if not bname or not vname:
                    continue
                if target_block and bname.upper() != target_block:
                    continue
                baked = self._baked_info(pack_dir, bname, vname)
                variants.append({
                    "packId": pack_id,
                    "block": bname,
                    "variant": vname,
                    "remove": list(entry.get("remove") or []),
                    "add": list(entry.get("add") or []),
                    **baked,
                })
        return {
            "packs": pack_list,
            "variants": variants,
            "arena2Available": self.library.arena2.is_dir(),
        }

    def save_variant(self, body: dict) -> dict:
        if not isinstance(body, dict):
            raise BadRequestError("Expected a JSON object.")
        pack_id = (body.get("packId") or "").strip()
        pack_dir = self._resolve_pack(pack_id)
        recipe = validate_variant_recipe(
            body.get("block"),
            body.get("variant"),
            body.get("remove"),
            body.get("add"),
        )
        original_variant = (body.get("originalVariant") or "").strip() or None
        payload_path = pack_dir / "payload.json"

        def mutate(current: dict):
            if not isinstance(current, dict):
                current = {}
            wv = current.get("worldVariants")
            if not isinstance(wv, dict):
                wv = {}
                current["worldVariants"] = wv
            blocks = wv.get("blocks")
            if not isinstance(blocks, list):
                blocks = []
                wv["blocks"] = blocks

            match_variant = original_variant or recipe["variant"]
            replaced_idx = None
            for idx, existing in enumerate(blocks):
                if not isinstance(existing, dict):
                    continue
                same_block = str(existing.get("block") or "").upper() == recipe["block"].upper()
                if same_block and str(existing.get("variant") or "") == match_variant:
                    replaced_idx = idx
                    break

            # Guard against duplicate (block, variant) on rename
            if original_variant and original_variant != recipe["variant"]:
                for idx, existing in enumerate(blocks):
                    if idx == replaced_idx or not isinstance(existing, dict):
                        continue
                    if (
                        str(existing.get("block") or "").upper() == recipe["block"].upper()
                        and str(existing.get("variant") or "") == recipe["variant"]
                    ):
                        raise ValidationError(
                            f"Variant '{recipe['variant']}' already exists for {recipe['block']} in {pack_id}.",
                            [ValidationIssue("variant", "Duplicate variant name for this block.")],
                        )

            if replaced_idx is not None:
                blocks[replaced_idx] = recipe
            else:
                blocks.append(recipe)
            return current, recipe

        saved = self.packs.update_json(payload_path, mutate, sort_keys=False)
        baked = self._baked_info(pack_dir, saved["block"], saved["variant"])
        return {
            "recipe": {
                "packId": pack_id,
                "block": saved["block"],
                "variant": saved["variant"],
                "remove": list(saved.get("remove") or []),
                "add": list(saved.get("add") or []),
                **baked,
            },
            **self.list_variants(saved["block"]),
        }

    def delete_variant(self, pack_id: str, block: str, variant: str) -> dict:
        pack_dir = self._resolve_pack(pack_id)
        norm_block = (block or "").strip()
        norm_variant = (variant or "").strip()
        if not norm_block or not norm_variant:
            raise BadRequestError("Both 'block' and 'variant' query parameters are required.")
        payload_path = pack_dir / "payload.json"

        def mutate(current: dict):
            wv = current.get("worldVariants") if isinstance(current, dict) else None
            blocks = wv.get("blocks") if isinstance(wv, dict) else None
            if not isinstance(blocks, list):
                raise NotFoundError(f"Variant '{norm_variant}' for '{norm_block}' not found in '{pack_id}'.")
            kept = [
                entry for entry in blocks
                if not (
                    isinstance(entry, dict)
                    and str(entry.get("block") or "").upper() == norm_block.upper()
                    and str(entry.get("variant") or "") == norm_variant
                )
            ]
            if len(kept) == len(blocks):
                raise NotFoundError(f"Variant '{norm_variant}' for '{norm_block}' not found in '{pack_id}'.")
            wv["blocks"] = kept
            if not kept and len(wv) == 1:
                current.pop("worldVariants", None)
            return current, True

        self.packs.update_json(payload_path, mutate, sort_keys=False)
        return {
            "deleted": True,
            **self.list_variants(norm_block),
        }

    def bake_pack(self, pack_id: str, block: str | None = None) -> dict:
        pack_dir = self._resolve_pack(pack_id)
        arena2 = self.library.arena2
        if self._bake_runner is not None:
            returncode, output = self._bake_runner(self.library.repo, arena2, pack_dir)
        else:
            if not arena2.is_dir():
                raise ValidationError(
                    f"Arena2 source directory not found at '{arena2}'. Cannot run world-variant-bake without Arena2 files.",
                    [ValidationIssue("arena2", f"Missing directory: {arena2}")],
                )
            cmd = [
                "dotnet", "run", "--project", "src/Daggerfall.Source.Tool",
                "--", "world-variant-bake",
                "--arena2", str(arena2),
                "--pack", str(pack_dir),
            ]
            try:
                proc = subprocess.run(
                    cmd,
                    cwd=self.library.repo,
                    capture_output=True,
                    text=True,
                    timeout=180,
                )
            except (OSError, subprocess.SubprocessError) as err:
                raise ValueError(f"world-variant-bake failed to start: {err}") from err
            returncode = proc.returncode
            output = "\n".join(part.strip() for part in (proc.stdout, proc.stderr) if part and part.strip())

        if returncode != 0:
            raise ValueError(output or f"world-variant-bake exited with status {returncode}.")

        self.cache.invalidate()
        return {
            "ok": True,
            "packId": pack_id,
            "output": output,
            **self.list_variants(block),
        }

    def _available_model_ids(self) -> list[str]:
        roots = (
            self.library.world_shared / "geometry",
            self.library.world_classic_shared / "geometry",
        )
        stamp = tuple(dir_stamp(root, "mesh-*.rstatmsh") for root in roots)

        def scan():
            ids: set[int] = set()
            for root in roots:
                if not root.is_dir():
                    continue
                for entry in root.iterdir():
                    match = MODEL_FILE_RE.match(entry.name)
                    if match:
                        ids.add(int(match.group(1)))
            return [str(mid) for mid in sorted(ids)]

        return self.cache.get(("world-blocks-model-ids", roots), stamp, scan)

    def search_models(self, q: str = "", limit: int = 40, catalog_overview: dict | None = None) -> dict:
        all_ids = self._available_model_ids()
        model_meta: dict[str, dict] = {}
        if isinstance(catalog_overview, dict):
            for category in ("nature", "props", "architecture"):
                for row in catalog_overview.get(category) or []:
                    facts = row.get("facts") or {}
                    arch_id = facts.get("ARCH3D model")
                    if arch_id is not None:
                        model_meta[str(arch_id)] = {
                            "cmsId": row["id"],
                            "name": row["name"],
                            "category": category,
                        }

        # Detect refreshed remodels from art pack payload if present
        remodel_ids: set[str] = set()
        if self.library.art_payload.is_file():
            art_doc = self.packs.read_json(self.library.art_payload, {}) or {}
            models_section = (art_doc.get("worldArtReplacements") or {}).get("models")
            if isinstance(models_section, dict):
                remodel_ids = set(models_section.keys())

        terms = [t for t in (q or "").strip().lower().split() if t]
        exact: list[dict] = []
        prefix: list[dict] = []
        matched: list[dict] = []

        for mid in all_ids:
            meta = model_meta.get(mid)
            name = meta["name"] if meta else None
            category = meta["category"] if meta else None
            cms_id = meta["cmsId"] if meta else None
            has_remodel = mid in remodel_ids

            if terms:
                hay = f"{mid} {name or ''} {category or ''} {cms_id or ''}".lower()
                if not all(t in hay for t in terms):
                    continue

            row = {
                "modelId": mid,
                "meshArtifactId": f"geometry/mesh-{mid}",
                "name": name,
                "category": category,
                "cmsId": cms_id,
                "hasRemodel": has_remodel,
            }
            if len(terms) == 1 and mid == terms[0]:
                exact.append(row)
            elif len(terms) == 1 and mid.startswith(terms[0]):
                prefix.append(row)
            else:
                matched.append(row)

        # When browsing with no query, surface catalogued/named props first so picker is immediately useful
        if not terms:
            matched.sort(key=lambda r: (0 if r["name"] else 1, 0 if r["hasRemodel"] else 1, int(r["modelId"])))

        ordered = exact + prefix + matched
        safe_limit = max(1, min(int(limit), 100))
        return {
            "total": len(ordered),
            "items": ordered[:safe_limit],
        }


def register_routes(router, workspace):
    """Register World Block Variant authoring, baking, and ARCH3D model search routes."""

    def get_variants(req):
        return workspace.world_variants.list_variants(block=req.arg("block"))

    def put_variant(req):
        return workspace.world_variants.save_variant(req.json())

    def delete_variant(req):
        pack_id = req.arg("packId", "") or ""
        block = req.arg("block", "") or ""
        variant = req.arg("variant", "") or ""
        return workspace.world_variants.delete_variant(pack_id, block, variant)

    def post_bake(req):
        body = req.json()
        if not isinstance(body, dict):
            raise BadRequestError("Expected JSON body with packId.")
        pack_id = (body.get("packId") or "").strip()
        block = (body.get("block") or "").strip() or None
        return workspace.world_variants.bake_pack(pack_id, block=block)

    def get_models(req):
        q = req.arg("q", "") or ""
        raw_limit = req.arg("limit", "40") or "40"
        limit = int(raw_limit) if raw_limit.isdigit() else 40
        overview = workspace.catalog_store.overview()
        return workspace.world_variants.search_models(q=q, limit=limit, catalog_overview=overview)

    router.get("/api/world-variants", get_variants)
    router.put("/api/world-variants", put_variant)
    router.delete("/api/world-variants", delete_variant)
    router.post("/api/world-variants/bake", post_bake)
    router.get("/api/world-blocks/models", get_models)
