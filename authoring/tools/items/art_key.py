"""One name for a source image across specs, finals and the ledger.

World sprites (area `items`) are named by their texture archive, record and frame, as the importer
names them. Every other area names an image by its subject directory and file, so the same source
file always lands on the same final.
"""
import re

TEXTURE = re.compile(r"texture-(\d+)-record-(\d+)-frame-(\d+)")


# Areas whose images are named by texture archive; `worn` reads the player layers of the items export.
TEXTURE_AREAS = ("items", "worn")
SOURCE_DIR = {"worn": "items"}


def source_dir(area):
    """df-raw subdirectory that holds an area's source images."""
    return SOURCE_DIR.get(area, area)


def art_key(area, directory, image):
    """Ledger key, which is also the final's file stem."""
    if area in TEXTURE_AREAS:
        a, r, f = TEXTURE.search(image).groups()
        return f"texture-{a}-record-{r}-frame-{f}"
    stem = image.rsplit(".", 1)[0].removesuffix("-source")
    return f"{directory}/{stem}".replace("/", "--")
