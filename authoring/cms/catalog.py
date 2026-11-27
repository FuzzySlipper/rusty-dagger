"""Compose the asset catalog by category, from the reference exports, game data, the art pack and character stages.

Every asset id is `<prefix>-<key>`: the prefix names its category (`CATEGORIES`) and the key is its classic source key,
except that people keep their editorial `npc-` numbers. People and creatures drawn as characters have character pages
with pipelines and concept art; every other record is a reviewable asset with its facts and previews.
"""

import csv
from collections import defaultdict

from base_pack import read_section as read_base_section
from cache import FileCache, dir_stamp, file_stamp
from library import MODEL_CATEGORIES, asset_url, file_link, read_json
from packs import PackStore
from pipelines import REVIEW_STATES, character_index
from router import NotFoundError

CATEGORIES = {"npc": "people", "creature": "creatures", "nature": "nature", "prop": "props", "arch": "architecture",
              "texture": "textures", "anim": "animations", "sound": "audio", "music": "audio"}
ASSETS = ("people", "creatures", "nature", "props", "architecture", "textures", "animations", "audio")
PREFIXES = {"people": "npc", "creatures": "creature", "nature": "nature", "props": "prop", "architecture": "arch"}
# The flora review's display groups, as nature kinds.
FLORA_KINDS = {"Trees": "tree", "Bushes": "bush", "Flowers": "flower", "Other plants": "plant", "Cacti": "cactus",
               "Fungi": "fungus", "Rocks": "rock", "Deadwood": "deadwood", "Other scenery": "scenery"}
# The classic mobiles below this id are creatures; the human classes from it on are people's enemy class records.
FIRST_HUMAN_MOBILE = 128

# Canonical domestic animal creature records consolidating classic TEXTURE.201 sprites (skipping bird record 11).
# Each record is a first-class creature subject with concept-art stages and pipeline links instead of per-angle scenery rows.
DOMESTIC_ANIMALS = (
    {"id": "creature-202", "name": "Horse (Bay)", "classicRecords": (0,)},
    {"id": "creature-203", "name": "Horse (Black)", "classicRecords": (1,)},
    {"id": "creature-204", "name": "Camel", "classicRecords": (2,)},
    {"id": "creature-205", "name": "Cow", "classicRecords": (3, 4)},
    {"id": "creature-206", "name": "Pig", "classicRecords": (5, 6)},
    {"id": "creature-207", "name": "Cat", "classicRecords": (7, 8)},
    {"id": "creature-208", "name": "Dog", "classicRecords": (9, 10)},
)


def category_of(id):
    """The category an asset id names by its prefix, or None for an id of no category."""
    return CATEGORIES.get(id.partition("-")[0])


class Catalog:
    def __init__(self, library, links, cache: FileCache | None = None):
        self.library = library
        self.cache = cache
        npc_manifest = self._json(library.npcs / "manifest.json", {})
        self.briefs = self._json(library.npcs / "briefs.json", {})
        self.npcs = {row["id"]: row for row in npc_manifest.get("entries", [])}
        self.creatures = {}
        creature_aliases = {}
        for row in self._json(library.mobiles / "manifest.json", {}).get("mobiles", []):
            if row["mobileId"] < FIRST_HUMAN_MOBILE:
                id = f"creature-{row['mobileId']:03d}"
                self.creatures[id] = row
                creature_aliases[row["directory"]] = id
        scenery_201 = {
            int(row["record"]): row
            for row in _cached_csv(cache, library.scenery / "usage.csv")
            if int(row["archive"]) == 201
        }
        for animal in DOMESTIC_ANIMALS:
            aid = animal["id"]
            recs = animal["classicRecords"]
            placements = sum(
                sum(int(scenery_201[r][f"{p}_placements"]) for p in ("exterior", "interior", "dungeon"))
                for r in recs if r in scenery_201
            )
            self.creatures[aid] = {
                "id": aid,
                "name": animal["name"],
                "animal": True,
                "classicRecords": list(recs),
                "placements": placements,
                "aliases": [f"201-{r}" for r in recs],
            }
            for r in recs:
                creature_aliases[f"201-{r}"] = aid
                creature_aliases[f"creature-201-{r}"] = aid
        self.subjects = self.npcs | self.creatures
        self.aliases = npc_manifest.get("aliases", {}) | creature_aliases
        self.raw_links = links
        self.links = {}
        self.conflicts = {}
        grouped = defaultdict(list)
        for key, choice in links.items():
            grouped[self.resolve(key)].append(choice)
        for subject, group in grouped.items():
            combined = {"pipelines": list(dict.fromkeys(id for choice in group for id in choice.get("pipelines", [])))}
            for role in ("concept", "final"):
                canonical = links.get(subject, {}).get(role)
                inherited = [choice[role] for choice in group if choice.get(role)]
                if canonical:
                    combined[role] = canonical
                elif inherited and all(item == inherited[0] for item in inherited):
                    combined[role] = inherited[0]
                elif inherited:
                    self.conflicts.setdefault(subject, []).append(role)
            self.links[subject] = combined
        self.pipelines = character_index(library)
        self.by_subject = {key: [] for key in self.subjects}
        for pipeline_id in self.pipelines:
            # A townsperson's stages are named by its archive, a mobile's by its mobile id (and "-female").
            subject = self.aliases.get(pipeline_id) or self.aliases.get(f"mobile-{pipeline_id}")
            if subject in self.by_subject:
                self.by_subject[subject].append(pipeline_id)

    def _json(self, path, default):
        return self.cache.json(path, default) if self.cache is not None else read_json(path, default)

    def resolve(self, key):
        """A character's id from its id or any earlier id: canonical ids first, since an imported mobile and a villager
        alias can use different namespaces."""
        return key if key in self.subjects else self.aliases.get(key, key)

    def get(self, key):
        key = self.resolve(key)
        row = self.subjects.get(key)
        if not row:
            return None
        creature = key in self.creatures
        reference = self.summary(key, row)
        pipeline = [self.pipelines[id] for id in reference["pipelineIds"]]
        choice = self.links.get(key, {})
        concepts = [art for item in pipeline for art in item["concepts"]]
        finals = [item["final"] for item in pipeline if item["final"]]
        canonical = assigned(choice.get("concept"), concepts)
        conflicts = self.conflicts.get(key, [])
        if not choice.get("concept") and "concept" not in conflicts:
            approved = [art for art in concepts if art["approved"] and art["name"] == "Front concept"]
            canonical = approved[0] if len(approved) == 1 else None
        final = assigned(choice.get("final"), finals)
        if not choice.get("final") and "final" not in conflicts and len(finals) == 1:
            final = finals[0]
        turnaround = next((name for name in [row.get("directory"), *row.get("aliases", [])]
                           if name and file_link(self.library.turnarounds, "/media/turnarounds/", f"{name}/turnaround.mp4")), None)
        return reference | {
            "pipelines": pipeline, "automaticPipelines": self.by_subject[key],
            "concept": canonical, "conceptChoices": concepts, "final": final, "finalChoices": finals,
            "explicitConcept": choice.get("concept"), "explicitFinal": choice.get("final"), "conflicts": conflicts,
            "game": (self.animal_game(row) if row.get("animal") else self.mobile_game(row)) if creature else self.npc_game(key),
            "turnaround": turnaround and asset_url("/media/turnarounds/", f"{turnaround}/turnaround.mp4"),
            "turnaroundStill": turnaround and file_link(self.library.turnarounds, "/media/turnarounds/", f"{turnaround}/views/body-front.png"),
        }

    def summary(self, key, row):
        choice = self.links.get(key, {})
        pipeline_ids = list(dict.fromkeys([*self.by_subject[key], *choice.get("pipelines", [])]))
        pipeline_ids = [id for id in pipeline_ids if id in self.pipelines]
        missing_ids = [id for id in choice.get("pipelines", []) if id not in self.pipelines]
        if key in self.creatures:
            if row.get("animal"):
                rec = row["classicRecords"][0]
                stem = f"texture-201-record-{rec:03d}"
                classic = file_link(self.library.scenery, "/media/scenery/", f"creatures/texture-201/sprite/{stem}-frame-000-source.png")
                sources = None
                kind, aliases = "Animal", list(row.get("aliases", []))
            else:
                image = next((frame["file"] for frame in row.get("frames", []) if frame["direction"] == "front"), None)
                classic = file_link(self.library.mobiles, "/media/classic/", image)
                sources = asset_url("/media/classic/", f"{row['directory']}/manifest.json")
                kind, aliases = "Monster", [row["directory"]]
        else:
            image = f"art/{row['image']}" if row.get("image") else None
            classic = file_link(self.library.npcs, "/media/npc/", image)
            sources = asset_url("/media/npc/", f"{key}/index.html")
            kind, aliases = row["kind"], row.get("aliases", [])
        return {
            "id": key, "category": category_of(key), "name": row["name"], "kind": kind,
            "classic": classic, "classicUrl": sources, "brief": self.briefs.get(key),
            "aliases": aliases, "pipelineIds": pipeline_ids, "missingPipelines": missing_ids,
        }

    def npc_game(self, key):
        detail = self._json(self.library.npcs / key / "manifest.json", {})
        return {"facts": detail.get("facts", {}), "placementCount": len(detail.get("placements", [])),
                "url": asset_url("/media/npc/", f"{key}/manifest.json")}

    def animal_game(self, row):
        recs = ", ".join(str(r) for r in row["classicRecords"])
        facts = {"Classic archive": "TEXTURE.201", "Classic records": recs, "Placements": row["placements"]}
        return {"facts": facts, "placementCount": row["placements"]}

    def mobile_game(self, row):
        specs = []
        for section in ("actorSpecsCreatures", "actorSpecsEnemies"):
            specs.extend((read_base_section(self.library.base_payload, section, {}, self.cache) or {}).get("specs", []))
        spec = next((record for record in specs if (record.get("classicBinding") or {}).get("mobileId") == row["mobileId"]), None)
        if spec is not None:
            binding = spec.get("classicBinding") or {}
            templates = {}
            for section in ("actorTemplateBases", "actorTemplateCreatures", "actorTemplateClasses", "actorTemplateRoles"):
                for tmpl in (read_base_section(self.library.base_payload, section, {}, self.cache) or {}).get("templates", []):
                    templates[tmpl["id"]] = tmpl
            caps = {}
            chain = []
            cur = spec.get("template")
            while cur and cur in templates and cur not in chain:
                chain.append(cur)
                cur = templates[cur].get("extends")
            for tid in reversed(chain):
                caps.update(templates[tid].get("capabilities") or {})
            facts = {"Classic archive": f"TEXTURE.{row['sourceArchive']:03d}",
                     "Display variant": row["variant"], "Mobile ID": row["mobileId"],
                     "Spawner": spec["id"], "Template": spec["template"]}
            for label, value in (("Identity", binding.get("identity")),
                                 ("Behaviour", binding.get("behaviour")),
                                 ("Affinity", binding.get("affinity")),
                                 ("Team", spec.get("team") or binding.get("donorTeam")),
                                 ("Casts magic", caps.get("castsMagic")),
                                 ("Opens doors", caps.get("canOpenDoors"))):
                if value is not None:
                    facts[label] = value
            return {"facts": facts, "record": spec}
        data = (read_base_section(self.library.base_payload, "mobiles", {}, self.cache) or {}).get("mobiles", [])
        donor = next((record for record in data if record["donorId"] == row["mobileId"]), {})
        facts = {"Classic archive": f"TEXTURE.{row['sourceArchive']:03d}",
                 "Display variant": row["variant"], "Mobile ID": row["mobileId"]}
        for label, field in (("Identity", "identity"), ("Behaviour", "behaviour"), ("Affinity", "affinity"),
                             ("Team", "team"), ("Casts magic", "castsMagic"), ("Opens doors", "canOpenDoors")):
            if field in donor:
                facts[label] = donor[field]
        return {"facts": facts, "record": donor}

    def overview(self):
        scenery = scenery_index(self.library, self.cache)
        models = model_index(self.library, self.cache)
        flora = flora_index(self.library, self.cache)
        assets = {
            "people": [self.summary(key, row) for key, row in self.npcs.items()],
            "creatures": [self.summary(key, row) for key, row in self.creatures.items()] + scenery["creatures"],
            "nature": flora + [row for row in scenery["nature"] if row["id"] not in {item["id"] for item in flora}] + models["nature"],
            "props": scenery["props"] + models["props"],
            "architecture": models["architecture"],
            "textures": texture_index(self.library, self.cache),
            "animations": animation_index(self.library, self.cache),
            "audio": audio_index(self.library, self.cache),
        }
        return assets | {
            "categories": CATEGORIES,
            "pipelines": [{"id": row["id"], "name": row["name"], "stages": row["stages"],
                           "final": row["final"], "thumbnail": next((art["url"] for art in row["concepts"]), None),
                           "subjects": [key for key, ids in self.by_subject.items()
                                        if row["id"] in ids or row["id"] in self.links.get(key, {}).get("pipelines", [])]}
                          for row in self.pipelines.values()],
            "review": [{**stage, "pipeline": id, "character": row["name"]}
                       for id, row in self.pipelines.items() for stage in row["stages"] if stage["state"] in REVIEW_STATES],
        }


def assigned(choice, available):
    if choice:
        return next((item for item in available
                     if item["pipeline"] == choice["pipeline"] and item["path"] == choice["path"]), None)
    return None


def asset(id, name, kind, preview=None, facts=None, **extra):
    return {"id": id, "category": category_of(id), "name": name, "kind": kind, "preview": preview, "facts": facts or {}} | extra


def _cached_json(cache, path, default):
    return cache.json(path, default) if cache is not None else read_json(path, default)


def _cached_csv(cache, path):
    return cache.csv(path) if cache is not None else read_csv(path)


def flora_index(library, cache=None):
    """The flora review's records of the nature sets, with their engine-rendered views."""
    rows = []
    for key, row in _cached_json(cache, library.flora / "catalogue.json", {}).items():
        views = {angle: url for angle in ("sprite", "front", "side", "rear")
                 if (url := file_link(library.flora, "/media/flora/", f"images/{key}-{angle}.webp"))}
        kind = FLORA_KINDS.get(row["category"], "")
        rows.append(asset(f"nature-{key}", row["form"] or row["category"], kind, views.get("front") or views.get("sprite"),
                          {"Climate": row["climate"], "Height": f"{float(row['height']):.2f} m", "Triangles": row["triangles"]},
                          views=views, climate=row["climate"]))
    return rows


def read_csv(path):
    try:
        with open(path, encoding="utf-8") as file:
            return list(csv.DictReader(file))
    except FileNotFoundError:
        return []


def scenery_index(library, cache=None):
    """The scenery export's records by category: nature, props and creatures (the placed animals)."""
    found = {"nature": [], "props": [], "creatures": []}
    rows = _cached_csv(cache, library.scenery / "usage.csv")
    for row in rows:
        archive, record = int(row["archive"]), int(row["record"])
        if archive == 201:
            continue
        stem = f"texture-{archive:03d}-record-{record:03d}"
        preview = file_link(library.scenery, "/media/scenery/", f"{row['directory']}/sprite/{stem}-frame-000-source.png")
        placements = sum(int(row[f"{place}_placements"]) for place in ("exterior", "interior", "dungeon"))
        facts = {"Classic record": f"TEXTURE.{archive:03d} record {record}", "Placements": placements,
                 "Frames": row["frames"], "Size": f"{row['width']} × {row['height']}"}
        if row["climate"]:
            facts["Climate"] = row["climate"]
        if row["light"] == "1":
            facts["Gives light"] = True
        category = CATEGORIES[row["category"]]
        found[category].append(asset(row["id"], f"TEXTURE.{archive:03d} record {record}", row["kind"], preview, facts,
                                     climate=row["climate"]))
    return found


def model_category(model, bands):
    """An ARCH3D model's category and kind by the first band that holds its id; architecture where none does."""
    return next(((band["category"], band["kind"]) for band in bands if band["from"] <= model <= band["to"]), ("architecture", ""))


def model_index(library, cache=None):
    """The remodelled ARCH3D models, by the category each shows: nature, props or architecture."""
    bands = _cached_json(cache, MODEL_CATEGORIES, {}).get("bands", [])
    payload = _cached_json(cache, library.art_payload, {}).get("artReplacements", {}).get("models", {})
    found = {"nature": [], "props": [], "architecture": []}
    for key, note in sorted(_cached_json(cache, library.model_notes, {}).items(), key=lambda item: int(item[0])):
        category, kind = model_category(int(key), bands)
        facts = {"ARCH3D model": key, "Status": note.get("status", ""), "Note": note.get("note", "")}
        if key in payload:
            facts["Triangles"] = payload[key]["triangles"]
        found[category].append(asset(f"{PREFIXES[category]}-m{key}", f"ARCH3D {key}", kind, None, facts))
    return found


def texture_index(library, cache=None):
    """Every classic surface texture record a block or the terrain draws, by its texture set."""
    found = []
    for row in _cached_csv(cache, library.surfaces / "usage.csv"):
        if not any(int(row[field]) for field in ("exterior_blocks", "interior_blocks", "dungeon_blocks", "flat_planes", "standing_planes")):
            continue
        archive, record = int(row["archive"]), int(row["record"])
        stem = f"texture-{archive:03d}-record-{record:03d}"
        preview = file_link(library.surfaces, "/media/surfaces/", f"{row['set']}/texture-{archive:03d}/surface/{stem}-frame-000-source.png")
        found.append(asset(f"texture-{archive:03d}-{record}", f"TEXTURE.{archive:03d} record {record}", row["set"], preview,
                           {"Climate": row["climate"], "Size": f"{row['width']} × {row['height']}"}, climate=row["climate"]))
    return found


def animation_index(library, cache=None):
    """The motion clips the art pack plays: the gsplat townsfolk's clips, shipped in animations.rpak, and the parts rig's."""
    art = _cached_json(cache, library.art_payload, {}).get("artReplacements", {})
    splat, rig = art.get("splatPeople", {}), art.get("peopleRig", {})
    walks, idles = {*splat.get("walks", []), *splat.get("skirtWalks", [])}, set(splat.get("idles", []))
    found = [asset(f"anim-{clip}", clip, "walk" if clip in walks else "idle" if clip in idles else "gesture", None,
                   {"Rig": "gsplat townsfolk (Mixamo)", "Container": "animations.rpak"})
             for clip in dict.fromkeys([*splat.get("clips", []), *splat.get("idles", [])])]
    found += [asset(f"anim-rig-{clip}", clip, "walk" if clip == rig.get("walk") else "idle" if clip == rig.get("idle") else "gesture",
                    None, {"Rig": f"parts rig {rig.get('figure', '')}".strip()}) for clip in rig.get("clips", [])]
    return found


def audio_index(library, cache=None):
    """The classic sound clips the import publishes, by their archive ordinal, and the score's tracks."""
    def kind(clip):
        group, dot, _ = clip.partition(".")
        return ("" if group == "sound" else group) if dot else "effect"
    found = [asset(f"sound-{clip['sourceRecordOrdinal']}", clip["clip"], kind(clip["clip"]), None,
                   {"Media id": clip["mediaId"], "Sample rate": clip["sampleRate"]})
             for clip in _cached_json(cache, library.classic_media, {}).get("audio", [])]
    try:
        tracks = [line.split("\t")[0] for line in library.music_sources.read_text(encoding="utf-8").splitlines() if line.strip()]
    except FileNotFoundError:
        tracks = []
    return found + [asset(f"music-{track}", track, "music", None, {"Container": "music.rpak"}) for track in tracks]


class CatalogStore:
    """Mtime-cached access to the asset catalog, overview snapshot, and character pipeline links."""

    def __init__(self, library, links, cache: FileCache, packs: PackStore):
        self.library = library
        self.links = links
        self.cache = cache
        self.packs = packs

    def _catalog_stamp(self):
        return (
            file_stamp(self.library.npcs / "manifest.json"),
            file_stamp(self.library.npcs / "briefs.json"),
            file_stamp(self.library.mobiles / "manifest.json"),
            file_stamp(self.links),
            file_stamp(self.library.characters / "ready.json"),
            dir_stamp(self.library.characters, "*/character.json"),
        )

    def _overview_stamp(self):
        return (
            self._catalog_stamp(),
            file_stamp(self.library.flora / "catalogue.json"),
            file_stamp(self.library.scenery / "usage.csv"),
            file_stamp(self.library.surfaces / "usage.csv"),
            file_stamp(self.library.model_notes),
            file_stamp(self.library.art_payload),
            file_stamp(self.library.classic_media),
            file_stamp(self.library.music_sources),
            file_stamp(MODEL_CATEGORIES),
        )

    def catalog(self) -> Catalog:
        stamp = self._catalog_stamp()
        return self.cache.get(
            ("catalog", self.library, self.links),
            stamp,
            lambda: Catalog(self.library, self.packs.read_json(self.links, {}), self.cache),
        )

    def overview(self) -> dict:
        stamp = self._overview_stamp()
        cached = self.cache.get(
            ("catalog-overview", self.library, self.links),
            stamp,
            lambda: self.catalog().overview(),
        )
        return dict(cached)

    def update_links(self, key: str, body) -> str:
        if not isinstance(body, dict):
            raise ValueError("A link update needs a JSON object.")

        def mutate(raw_links):
            catalog = Catalog(self.library, raw_links or {}, self.cache)
            subject = catalog.get(key)
            if not subject:
                raise ValueError("Unknown character.")
            identity = subject["id"]
            current = dict(catalog.links.get(identity, {}))
            if "addPipeline" in body:
                pipeline = body["addPipeline"]
                if not isinstance(pipeline, str) or pipeline not in catalog.pipelines:
                    raise ValueError("Choose an existing character pipeline.")
                current["pipelines"] = list(dict.fromkeys([*current.get("pipelines", []), pipeline]))
            if "removePipeline" in body:
                current["pipelines"] = [id for id in current.get("pipelines", []) if id != body["removePipeline"]]
            allowed = set(subject["automaticPipelines"]) | set(current.get("pipelines", []))
            for role in ("concept", "final"):
                if role in body:
                    choice = body[role]
                    if choice is None:
                        current.pop(role, None)
                        continue
                    if not isinstance(choice, dict) or not isinstance(choice.get("pipeline"), str) or choice["pipeline"] not in allowed:
                        raise ValueError("Choose an asset from a linked pipeline.")
                    pipeline = catalog.pipelines[choice["pipeline"]]
                    choices = pipeline["concepts"] if role == "concept" else [pipeline["final"]] if pipeline["final"] else []
                    if not assigned(choice, choices):
                        raise ValueError(f"The selected {role} is no longer available.")
                    current[role] = {"pipeline": choice["pipeline"], "path": choice["path"]}
                elif current.get(role, {}).get("pipeline") not in allowed:
                    current.pop(role, None)
            links = {}
            for id, link in catalog.raw_links.items():
                if id == identity:
                    continue
                if catalog.aliases.get(id) != identity:
                    links[id] = link
                    continue
                unresolved = {role: link[role] for role in catalog.conflicts.get(identity, [])
                              if role not in body and role in link and link[role]["pipeline"] in allowed}
                if unresolved:
                    links[id] = unresolved
            if any(current.values()):
                links[identity] = current
            return links, identity

        identity = self.packs.update_json(self.links, mutate, default=dict, sort_keys=True)
        self.cache.invalidate(("catalog", self.library, self.links))
        self.cache.invalidate(("catalog-overview", self.library, self.links))
        return identity


def register_routes(router, workspace):
    """Register asset catalog, character, pipeline, stage-review, and NPC merge routes."""

    def get_character(req):
        subject = workspace.character(req.params["key"])
        if subject is None:
            raise NotFoundError("Character not found.")
        return subject

    def get_pipeline(req):
        record = workspace.catalog().pipelines.get(req.params["key"])
        if record is None:
            raise NotFoundError("Pipeline not found.")
        return record

    def get_stage_review(req):
        review = workspace.stage_review(req.params["pipeline"], req.params["stage"])
        if review is None:
            raise NotFoundError("Stage not found.")
        return review

    router.get("/api/catalog", lambda _req: workspace.overview())
    router.get("/api/characters/{key}", get_character)
    router.post("/api/characters/{key}/links", lambda req: workspace.update_links(req.params["key"], req.json()))
    router.get("/api/pipelines/{key}", get_pipeline)
    router.get("/api/stage-review/{pipeline}/{stage:path}", get_stage_review)
    router.post("/api/npcs/merge", lambda req: workspace.merge_npcs(req.json()))
