"""Compose the catalog from the NPC export, game data, and character stages."""

from collections import defaultdict

from library import BASE_PAYLOAD, asset_url, file_link, read_json
from pipelines import REVIEW_STATES, character_index


class Catalog:
    def __init__(self, library, links):
        self.library = library
        npc_manifest = read_json(library.npcs / "manifest.json", {})
        self.aliases = npc_manifest.get("aliases", {})
        self.briefs = read_json(library.npcs / "briefs.json", {})
        self.npcs = {row["id"]: row for row in npc_manifest.get("entries", [])}
        self.mobiles = {row["directory"]: row for row in read_json(library.mobiles / "manifest.json", {}).get("mobiles", [])}
        self.raw_links = links
        self.links = {}
        self.conflicts = {}
        grouped = defaultdict(list)
        for key, choice in links.items():
            subject = key if key in self.npcs or key in self.mobiles else self.aliases.get(key, key)
            grouped[subject].append(choice)
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
        self.by_subject = {key: [] for key in self.npcs | self.mobiles}
        for pipeline_id in self.pipelines:
            subject = self.aliases.get(pipeline_id)
            if subject not in self.npcs:
                candidate = f"mobile-{pipeline_id}"
                subject = candidate if candidate in self.mobiles else None
            if subject in self.by_subject:
                self.by_subject[subject].append(pipeline_id)

    def get(self, key):
        # Consult canonical keys first: an imported mobile and a villager alias can use different namespaces.
        key = key if key in self.npcs or key in self.mobiles else self.aliases.get(key, key)
        row = self.npcs.get(key) or self.mobiles.get(key)
        if not row:
            return None
        mobile = key in self.mobiles
        reference = self.summary(key, row, mobile)
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
        return reference | {
            "pipelines": pipeline, "automaticPipelines": self.by_subject[key],
            "concept": canonical, "conceptChoices": concepts, "final": final, "finalChoices": finals,
            "explicitConcept": choice.get("concept"), "explicitFinal": choice.get("final"), "conflicts": conflicts,
            "game": self.mobile_game(row) if mobile else self.npc_game(key),
            "turnaround": file_link(self.library.turnarounds, "/media/turnarounds/", f"{key}/turnaround.mp4") if mobile else None,
            "turnaroundStill": file_link(self.library.turnarounds, "/media/turnarounds/", f"{key}/views/body-front.png") if mobile else None,
        }

    def summary(self, key, row, mobile=False):
        choice = self.links.get(key, {})
        pipeline_ids = list(dict.fromkeys([*self.by_subject[key], *choice.get("pipelines", [])]))
        pipeline_ids = [id for id in pipeline_ids if id in self.pipelines]
        missing_ids = [id for id in choice.get("pipelines", []) if id not in self.pipelines]
        if mobile:
            image = next((frame["file"] for frame in row.get("frames", []) if frame["direction"] == "front"), None)
            classic = file_link(self.library.mobiles, "/media/classic/", image)
            sources = asset_url("/media/classic/", f"{key}/manifest.json")
        else:
            image = f"art/{row['image']}" if row.get("image") else None
            classic = file_link(self.library.npcs, "/media/npc/", image)
            sources = asset_url("/media/npc/", f"{key}/index.html")
        return {
            "id": key, "name": row["name"], "kind": "Classic mobile" if mobile else row["kind"],
            "classic": classic, "classicUrl": sources, "brief": self.briefs.get(key),
            "aliases": row.get("aliases", []), "pipelineIds": pipeline_ids, "missingPipelines": missing_ids,
        }

    def npc_game(self, key):
        detail = read_json(self.library.npcs / key / "manifest.json", {})
        return {"facts": detail.get("facts", {}), "placementCount": len(detail.get("placements", [])),
                "url": asset_url("/media/npc/", f"{key}/manifest.json")}

    def mobile_game(self, row):
        data = read_json(BASE_PAYLOAD, {}).get("mobiles", {}).get("mobiles", [])
        donor = next((record for record in data if record["donorId"] == row["mobileId"]), {})
        facts = {"Classic archive": f"TEXTURE.{row['sourceArchive']:03d}",
                 "Display variant": row["variant"], "Mobile ID": row["mobileId"]}
        for label, field in (("Identity", "identity"), ("Behaviour", "behaviour"), ("Affinity", "affinity"),
                             ("Team", "team"), ("Casts magic", "castsMagic"), ("Opens doors", "canOpenDoors")):
            if field in donor:
                facts[label] = donor[field]
        return {"facts": facts, "record": donor}

    def overview(self):
        return {
            "npcs": [self.summary(key, row) for key, row in self.npcs.items()],
            "mobiles": [self.summary(key, row, True) for key, row in self.mobiles.items()],
            "pipelines": [{"id": row["id"], "name": row["name"], "stages": row["stages"],
                           "final": row["final"], "thumbnail": next((art["url"] for art in row["concepts"]), None),
                           "subjects": [key for key, ids in self.by_subject.items()
                                        if row["id"] in ids or row["id"] in self.links.get(key, {}).get("pipelines", [])]}
                          for row in self.pipelines.values()],
            "flora": flora_index(self.library),
            "review": [{**stage, "pipeline": id, "character": row["name"]}
                       for id, row in self.pipelines.items() for stage in row["stages"] if stage["state"] in REVIEW_STATES],
        }


def assigned(choice, available):
    if choice:
        return next((item for item in available
                     if item["pipeline"] == choice["pipeline"] and item["path"] == choice["path"]), None)
    return None


def flora_index(library):
    rows = read_json(library.flora / "catalogue.json", {})
    return [row | {"views": {angle: url for angle in ("sprite", "front", "side", "rear")
                             if (url := file_link(library.flora, "/media/flora/", f"images/{key}-{angle}.webp"))}}
            for key, row in rows.items()]
