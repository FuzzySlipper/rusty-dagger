# Cinematic publication

`Daggerfall.Import.Tool cinematic-media` converts retained VID or FLC sources into
VP9/Opus WebM artifacts. Source bytes must match the cinematic provenance already
in the pack. The command publishes artifact path, hash, size, dimensions, frame
count, duration and audio presence beside the source record; narrative bindings
remain source identities. Use `--kind vid` or `--kind flc`, with `--arena2`, `--pack`,
`--out` and explicit `--update` to write output. FFmpeg and ffprobe are offline
conversion dependencies.

VID decoding implements the donor palette, full-frame, delta and row-offset
blocks. Its timeline follows DFU's active 11025 Hz audio cadence, including the
740-sample minimum scheduling interval. This is an explicit donor adaptation;
it does not claim independently recovered original executable timing. Frames
pass through deterministic PNG intermediates because FFmpeg's direct VID and
PPM-concat paths fail on retained corpus variants. FLC conversion excludes the
terminal ring frame from the declared display cycle. Conversion verifies decoded
frame counts and duration before moving the final artifact. Temporary files stay
outside the content tree. VP9 encoding is lossless within the selected YUV 4:2:0
representation; the conversion is not a claim of pixel-identical RGB preservation.

The Host declares the lazy `daggerfall.cinematics` Engine bundle.
`DaggerfallCinematicPresentation` resolves catalog identities and passes a retained
content reference to the safe Engine video service. The Engine owns playback,
resource delivery and terminal observations. Session updates poll that owner;
replacement, skip, completion and disposal release the active playback. A source
without an accepted artifact reports that absence explicitly.

Story ordering and Daedric interaction policy belong to the story/special owners.
A video completion result never means a quest, reward or magic effect completed.
The same presentation contract supports their later semantic callers.
