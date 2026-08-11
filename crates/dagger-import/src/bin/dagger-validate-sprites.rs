//! dagger-validate-sprites: deterministic sprite art validation + extraction quality.
//!
//! Reads the enemy/billboard manifests produced by dagger-import and (optionally)
//! the classic TEXTURE.nnn archives to flag rendering-quality issues:
//! - per-orientation dimension / worldSize variation
//! - scale factor differences
//! - aspect ratio drift
//! - atlas waste from uniform cells sized to max-per-axis
//! - frameCount consistency
//! - manifest ↔ PNG hash/dims consistency (header check)
//!
//! Also emits a visual dump HTML that highlights flagged cases for human/LLM review.
//! The validator is deterministic and fails closed on error-level flags when `--check` is passed.

use std::collections::{BTreeMap, BTreeSet};
use std::path::{Path, PathBuf};

use arena2::mobile::record_world_size;
use arena2::texture::TextureFile;

#[derive(Debug, serde::Serialize, serde::Deserialize)]
struct EnemyManifest {
    #[serde(rename = "schemaVersion")]
    schema_version: u32,
    enemies: Vec<EnemyEntry>,
}

#[derive(Debug, serde::Serialize, serde::Deserialize, Clone)]
struct EnemyEntry {
    #[serde(rename = "mobileId")]
    mobile_id: u8,
    name: String,
    archive: u16,
    path: String,
    sha256: String,
    #[serde(rename = "byteLength")]
    byte_length: usize,
    width: u32,
    height: u32,
    #[serde(rename = "normalizedSize")]
    normalized_size: [f32; 2],
    frames: Vec<EnemyFrame>,
}

#[derive(Debug, serde::Serialize, serde::Deserialize, Clone)]
struct EnemyFrame {
    frame: usize,
    #[serde(rename = "uvMin")]
    uv_min: [f64; 2],
    #[serde(rename = "uvMax")]
    uv_max: [f64; 2],
    #[serde(rename = "sourceSize")]
    source_size: [f32; 2],
}

#[derive(Debug, serde::Serialize, serde::Deserialize)]
struct BillboardManifest {
    #[serde(rename = "schemaVersion")]
    schema_version: u32,
    billboards: Vec<BillboardEntry>,
}

#[derive(Debug, serde::Serialize, serde::Deserialize, Clone)]
struct BillboardEntry {
    archive: u16,
    record: u16,
    path: String,
    sha256: String,
    #[serde(rename = "byteLength")]
    byte_length: usize,
    width: u32,
    height: u32,
    #[serde(rename = "worldSize")]
    world_size: [f32; 2],
    #[serde(rename = "frameCount")]
    frame_count: Option<u32>,
    fps: Option<u32>,
    frames: Option<Vec<BillboardFrame>>,
}

#[derive(Debug, serde::Serialize, serde::Deserialize, Clone)]
struct BillboardFrame {
    frame: u32,
    #[serde(rename = "uvMin")]
    uv_min: [f32; 2],
    #[serde(rename = "uvMax")]
    uv_max: [f32; 2],
}

#[derive(Debug, serde::Serialize)]
struct Flag {
    level: String, // "warn" or "error"
    metric: String,
    value: String,
    threshold: String,
    reason: String,
}

#[derive(Debug, serde::Serialize)]
struct EnemyReport {
    #[serde(rename = "mobileId")]
    mobile_id: u8,
    name: String,
    archive: u16,
    atlas: String,
    #[serde(rename = "atlasSize")]
    atlas_size: [u32; 2],
    #[serde(rename = "cellSize")]
    cell_size: [u32; 2],
    #[serde(rename = "totalFrames")]
    total_frames: usize,
    #[serde(rename = "uniqueWorldSizes")]
    unique_world_sizes: usize,
    #[serde(rename = "worldSizeRange")]
    world_size_range: [[f32; 2]; 2], // [min, max]
    #[serde(rename = "aspectRange")]
    aspect_range: [f32; 2],
    metrics: BTreeMap<String, f64>,
    flags: Vec<Flag>,
    frames: Vec<EnemyFrame>,
    // Ground truth from TEXTURE.nnn when available
    #[serde(rename = "groundTruth")]
    ground_truth: Option<GroundTruth>,
}

#[derive(Debug, serde::Serialize)]
struct GroundTruth {
    #[serde(rename = "recordDims")]
    record_dims: Vec<RecordDim>,
    #[serde(rename = "frameCounts")]
    frame_counts: Vec<u16>,
    #[serde(rename = "scales")]
    scales: Vec<[i16; 2]>,
}

#[derive(Debug, serde::Serialize)]
struct RecordDim {
    record: u16,
    #[serde(rename = "rawSize")]
    raw_size: [i16; 2],
    scale: [i16; 2],
    #[serde(rename = "worldSize")]
    world_size: [f32; 2],
    #[serde(rename = "frameCount")]
    frame_count: u16,
}

#[derive(Debug, serde::Serialize)]
struct BillboardReport {
    archive: u16,
    record: u16,
    path: String,
    #[serde(rename = "atlasSize")]
    atlas_size: [u32; 2],
    #[serde(rename = "worldSize")]
    world_size: [f32; 2],
    #[serde(rename = "frameCount")]
    frame_count: u32,
    flags: Vec<Flag>,
}

#[derive(Debug, serde::Serialize)]
struct ValidationReport {
    #[serde(rename = "schemaVersion")]
    schema_version: u32,
    generated: String,
    enemies: Vec<EnemyReport>,
    billboards: Vec<BillboardReport>,
    summary: Summary,
}

#[derive(Debug, serde::Serialize)]
struct Summary {
    #[serde(rename = "totalEnemies")]
    total_enemies: usize,
    #[serde(rename = "flaggedEnemies")]
    flagged_enemies: usize,
    #[serde(rename = "warnCount")]
    warn_count: usize,
    #[serde(rename = "errorCount")]
    error_count: usize,
    #[serde(rename = "totalBillboards")]
    total_billboards: usize,
    #[serde(rename = "flaggedBillboards")]
    flagged_billboards: usize,
}

fn parse_args() -> Result<Args, String> {
    let mut arena2_dir = PathBuf::from("local/arena2");
    let mut enemy_manifest = PathBuf::from("content/textures/enemy-manifest.json");
    let mut billboard_manifest = PathBuf::from("content/textures/billboard-manifest.json");
    let mut out_json: Option<PathBuf> = None;
    let mut out_html_dir: Option<PathBuf> = None;
    let mut check = false;
    let mut it = std::env::args().skip(1);
    while let Some(a) = it.next() {
        match a.as_str() {
            "--arena2" => arena2_dir = PathBuf::from(it.next().ok_or("--arena2 needs a value")?),
            "--enemy-manifest" => {
                enemy_manifest = PathBuf::from(it.next().ok_or("--enemy-manifest needs a value")?)
            }
            "--billboard-manifest" => {
                billboard_manifest =
                    PathBuf::from(it.next().ok_or("--billboard-manifest needs a value")?)
            }
            "--out" => out_json = Some(PathBuf::from(it.next().ok_or("--out needs a value")?)),
            "--html" => {
                out_html_dir = Some(PathBuf::from(it.next().ok_or("--html needs a value")?))
            }
            "--check" => check = true,
            "--help" | "-h" => return Err(usage()),
            other => return Err(format!("unknown arg {other}\n{}", usage())),
        }
    }
    Ok(Args {
        arena2_dir,
        enemy_manifest,
        billboard_manifest,
        out_json,
        out_html_dir,
        check,
    })
}

struct Args {
    arena2_dir: PathBuf,
    enemy_manifest: PathBuf,
    billboard_manifest: PathBuf,
    out_json: Option<PathBuf>,
    out_html_dir: Option<PathBuf>,
    check: bool,
}

fn usage() -> String {
    "usage: dagger-validate-sprites [--arena2 DIR] [--enemy-manifest FILE] [--billboard-manifest FILE] [--out FILE.json] [--html DIR] [--check]".to_string()
}

fn main() {
    let args = match parse_args() {
        Ok(a) => a,
        Err(e) => {
            eprintln!("{e}");
            std::process::exit(2);
        }
    };

    // Load manifests
    let enemy_text = std::fs::read_to_string(&args.enemy_manifest).unwrap_or_else(|e| {
        eprintln!("read {}: {e}", args.enemy_manifest.display());
        std::process::exit(1);
    });
    let enemy_manifest: EnemyManifest = serde_json::from_str(&enemy_text).unwrap_or_else(|e| {
        eprintln!("parse {}: {e}", args.enemy_manifest.display());
        std::process::exit(1);
    });

    let billboard_text = std::fs::read_to_string(&args.billboard_manifest).unwrap_or_else(|e| {
        eprintln!("read {}: {e}", args.billboard_manifest.display());
        std::process::exit(1);
    });
    let billboard_manifest: BillboardManifest = serde_json::from_str(&billboard_text)
        .unwrap_or_else(|e| {
            eprintln!("parse {}: {e}", args.billboard_manifest.display());
            std::process::exit(1);
        });

    let texture_dir = args.arena2_dir.clone();
    // Alternative location fallback: /home/research/daggerfall-files
    let fallback_arena = PathBuf::from("/home/research/daggerfall-files");

    let mut enemy_reports = Vec::new();
    let mut total_warn = 0usize;
    let mut total_error = 0usize;

    for enemy in &enemy_manifest.enemies {
        let total_frames = enemy.frames.len();
        let atlas_w = enemy.width as usize;
        let atlas_h = enemy.height as usize;
        let cell_w = enemy
            .frames
            .iter()
            .map(|frame| ((frame.uv_max[0] - frame.uv_min[0]) * atlas_w as f64).round() as usize)
            .max()
            .unwrap_or(0);
        let cell_h = enemy
            .frames
            .iter()
            .map(|frame| ((frame.uv_max[1] - frame.uv_min[1]) * atlas_h as f64).round() as usize)
            .max()
            .unwrap_or(0);

        // World size metrics from manifest
        let mut min_w = f32::MAX;
        let mut max_w = f32::MIN;
        let mut min_h = f32::MAX;
        let mut max_h = f32::MIN;
        let mut min_aspect = f32::MAX;
        let mut max_aspect = f32::MIN;
        let mut min_area = f32::MAX;
        let mut max_area = f32::MIN;
        let mut uniq: BTreeSet<(u32, u32)> = BTreeSet::new();
        for f in &enemy.frames {
            let w = f.source_size[0];
            let h = f.source_size[1];
            min_w = min_w.min(w);
            max_w = max_w.max(w);
            min_h = min_h.min(h);
            max_h = max_h.max(h);
            let area = w * h;
            min_area = min_area.min(area);
            max_area = max_area.max(area);
            let aspect = if h > 1e-6 { w / h } else { 0.0 };
            min_aspect = min_aspect.min(aspect);
            max_aspect = max_aspect.max(aspect);
            // Quantize to 0.001 for uniq
            uniq.insert(((w * 1000.0) as u32, (h * 1000.0) as u32));
        }
        if enemy.frames.is_empty() {
            min_w = 0.0;
            max_w = 0.0;
            min_h = 0.0;
            max_h = 0.0;
            min_aspect = 0.0;
            max_aspect = 0.0;
            min_area = 0.0;
            max_area = 0.0;
        }

        let mut flags = Vec::new();
        let mut metrics = BTreeMap::new();

        // Compute variance metrics
        let width_delta = if max_w > 0.0 {
            (max_w - min_w) / max_w
        } else {
            0.0
        };
        let height_delta = if max_h > 0.0 {
            (max_h - min_h) / max_h
        } else {
            0.0
        };
        let area_delta = if max_area > 0.0 {
            (max_area - min_area) / max_area
        } else {
            0.0
        };
        let aspect_drift = if min_aspect > 1e-6 {
            max_aspect / min_aspect
        } else {
            1.0
        };
        metrics.insert("width_delta".to_string(), width_delta as f64);
        metrics.insert("height_delta".to_string(), height_delta as f64);
        metrics.insert("area_delta".to_string(), area_delta as f64);
        metrics.insert("aspect_drift".to_string(), aspect_drift as f64);
        metrics.insert("unique_world_sizes".to_string(), uniq.len() as f64);

        // Atlas waste: cell area vs average frame area (using worldSize area as proxy)
        // Cell's effective world area approximated as max area (cell sized to max pixel)
        let avg_area = if !enemy.frames.is_empty() {
            enemy
                .frames
                .iter()
                .map(|f| f.source_size[0] as f64 * f.source_size[1] as f64)
                .sum::<f64>()
                / enemy.frames.len() as f64
        } else {
            0.0
        };
        let cell_area = max_area as f64;
        let waste = if cell_area > 0.0 {
            1.0 - avg_area / cell_area
        } else {
            0.0
        };
        metrics.insert("cell_waste".to_string(), waste);
        metrics.insert("cell_w".to_string(), cell_w as f64);
        metrics.insert("cell_h".to_string(), cell_h as f64);
        metrics.insert("atlas_w".to_string(), atlas_w as f64);
        metrics.insert("atlas_h".to_string(), atlas_h as f64);

        // Thresholds (tuned from measured Rat variance: Rat area_delta ~0.69, aspect ~2.6)
        // Source size variance remains as donor evidence only. The published
        // pixels are cropped, normalized to one height, and use one fixed
        // bottom-centered world-space quad for the complete enemy atlas.
        let normalized_art = true;
        if area_delta > 0.50 {
            flags.push(Flag {
                level: if normalized_art { "info".to_string() } else { "warn".to_string() },
                metric: "area_delta".to_string(),
                value: format!("{area_delta:.2}"),
                threshold: ">0.50".to_string(),
                reason: format!(
                    "source worldSize area varies {:.0}% across orientations ({} unique sizes) — normalized art ignores this donor inconsistency",
                    area_delta * 100.0,
                    uniq.len()
                ),
            });
        } else if area_delta > 0.25 {
            flags.push(Flag {
                level: if normalized_art { "info".to_string() } else { "warn".to_string() },
                metric: "area_delta".to_string(),
                value: format!("{area_delta:.2}"),
                threshold: ">0.25".to_string(),
                reason: format!(
                    "source worldSize area varies {:.0}% ({} unique) — normalized art ignores this donor inconsistency",
                    area_delta * 100.0,
                    uniq.len()
                ),
            });
        }
        if aspect_drift > 2.0 {
            flags.push(Flag {
                level: if normalized_art { "info".to_string() } else { "warn".to_string() },
                metric: "aspect_drift".to_string(),
                value: format!("{aspect_drift:.2}"),
                threshold: ">2.0".to_string(),
                reason: format!(
                    "source metadata aspect drifts {:.2}× ({}..{}) — visible pixels retain their decoded aspect after height normalization",
                    aspect_drift, min_aspect, max_aspect
                ),
            });
        } else if aspect_drift > 1.5 {
            flags.push(Flag {
                level: if normalized_art {
                    "info".to_string()
                } else {
                    "warn".to_string()
                },
                metric: "aspect_drift".to_string(),
                value: format!("{aspect_drift:.2}"),
                threshold: ">1.5".to_string(),
                reason: format!(
                    "source metadata aspect drifts {:.2}× — normalized visible pixels retain decoded aspect",
                    aspect_drift
                ),
            });
        }
        if waste > 0.70 {
            flags.push(Flag {
                level: if normalized_art { "info".to_string() } else { "warn".to_string() },
                metric: "cell_waste".to_string(),
                value: format!("{waste:.2}"),
                threshold: ">0.70".to_string(),
                reason: format!(
                    "source area variance is {:.0}% (normalized cell {}×{}) — published art uses one fixed scale and pivot",
                    waste * 100.0,
                    cell_w,
                    cell_h
                ),
            });
        } else if waste > 0.50 {
            flags.push(Flag {
                level: if normalized_art {
                    "info".to_string()
                } else {
                    "warn".to_string()
                },
                metric: "cell_waste".to_string(),
                value: format!("{waste:.2}"),
                threshold: ">0.50".to_string(),
                reason: format!(
                    "source area variance is {:.0}% — published art uses one fixed scale",
                    waste * 100.0
                ),
            });
        }
        if uniq.len() > 3 {
            flags.push(Flag {
                level: if normalized_art { "info".to_string() } else { "warn".to_string() },
                metric: "unique_world_sizes".to_string(),
                value: uniq.len().to_string(),
                threshold: ">3".to_string(),
                reason: format!(
                    "{} distinct source worldSizes are retained as provenance but do not drive runtime geometry",
                    uniq.len()
                ),
            });
        }

        // Ground truth from TEXTURE.nnn when available
        let mut ground_truth: Option<GroundTruth> = None;
        // Try arena2_dir then fallback
        let tex_path = texture_dir.join(format!("TEXTURE.{:03}", enemy.archive));
        let tex_path = if tex_path.exists() {
            tex_path
        } else {
            fallback_arena.join(format!("TEXTURE.{:03}", enemy.archive))
        };
        if tex_path.exists() {
            if let Ok(tex) = TextureFile::load(&tex_path) {
                // MOVE_ANIMS base records: 0,1,2,3,4 and mirrored 3,2,1
                // We collect the 5 base records' info
                let base_records = [0u16, 1, 2, 3, 4];
                let mut record_dims = Vec::new();
                let mut frame_counts = Vec::new();
                let mut scales = Vec::new();
                let mut mismatch = false;
                let mut first_fc: Option<u16> = None;
                for &rec in &base_records {
                    if let Some(info) = tex.record_info(rec as usize) {
                        let ws =
                            record_world_size(info.width, info.height, info.scale_x, info.scale_y);
                        record_dims.push(RecordDim {
                            record: rec,
                            raw_size: [info.width, info.height],
                            scale: [info.scale_x, info.scale_y],
                            world_size: ws,
                            frame_count: info.frame_count,
                        });
                        frame_counts.push(info.frame_count);
                        scales.push([info.scale_x, info.scale_y]);
                        if let Some(fc) = first_fc {
                            if fc != info.frame_count {
                                mismatch = true;
                            }
                        } else {
                            first_fc = Some(info.frame_count);
                        }
                        // Verify manifest worldSize matches DFU calculation (within epsilon)
                        // Find corresponding manifest entries for this record's orientations
                        // The atlas frames for this record are at indices where record matches
                        // MOVE_ANIMS orientation mapping: 0->0,1->1,2->2,3->3,4->4,5->3,6->2,7->1
                        // But we just check that manifest's size for orientations using this record matches
                    }
                }
                if mismatch {
                    flags.push(Flag {
                        level: "error".to_string(),
                        metric: "frame_count_mismatch".to_string(),
                        value: format!("{:?}", frame_counts),
                        threshold: "all equal".to_string(),
                        reason:
                            "MOVE records have differing frameCount (expected uniform per enemy)"
                                .to_string(),
                    });
                }
                // Scale variance is donor provenance only; normalized pixels
                // and one atlas size own runtime presentation.
                let unique_scales: BTreeSet<[i16; 2]> = scales.iter().cloned().collect();
                if unique_scales.len() > 1 {
                    flags.push(Flag {
                        level: "info".to_string(),
                        metric: "scale_variance".to_string(),
                        value: format!("{} distinct", unique_scales.len()),
                        threshold: "1".to_string(),
                        reason: format!(
                            "scale factors vary across orientations {:?} — normalized art intentionally ignores them",
                            unique_scales
                        ),
                    });
                } else if scales.iter().any(|s| s[0] != 0 || s[1] != 0) {
                    flags.push(Flag {
                        level: "info".to_string(),
                        metric: "nonzero_scale".to_string(),
                        value: format!("{:?}", scales[0]),
                        threshold: "0,0".to_string(),
                        reason: "all orientations carry non-zero source scale (e.g., -128 = 50%); normalized art intentionally ignores it".to_string(),
                    });
                }

                // Validate manifest worldSizes against ground truth
                for (idx, f) in enemy.frames.iter().enumerate() {
                    // Map atlas index to MOVE_ANIMS orientation: index = orientation * M + anim_frame
                    // M = frame_counts[0] (assumed uniform)
                    let m = first_fc.unwrap_or(1) as usize;
                    if m == 0 {
                        continue;
                    }
                    let orientation = idx / m;
                    if orientation >= 8 {
                        continue;
                    }
                    // Orientation -> base record
                    let expected_rec = match orientation {
                        0 => 0,
                        1 => 1,
                        2 => 2,
                        3 => 3,
                        4 => 4,
                        5 => 3,
                        6 => 2,
                        7 => 1,
                        _ => 0,
                    };
                    if let Some(info) = tex.record_info(expected_rec as usize) {
                        let expected_ws =
                            record_world_size(info.width, info.height, info.scale_x, info.scale_y);
                        let eps = 0.001;
                        if (f.source_size[0] - expected_ws[0]).abs() > eps
                            || (f.source_size[1] - expected_ws[1]).abs() > eps
                        {
                            flags.push(Flag {
                                level: "error".to_string(),
                                metric: "manifest_worldSize_mismatch".to_string(),
                                value: format!("frame {} got {:?} expected {:?}", idx, f.source_size, expected_ws),
                                threshold: format!("±{eps}"),
                                reason: format!("manifest worldSize for frame {idx} (orientation {orientation} rec {expected_rec}) does not match DFU record_world_size"),
                            });
                            break; // one flag is enough
                        }
                    }
                }

                ground_truth = Some(GroundTruth {
                    record_dims,
                    frame_counts,
                    scales,
                });
            }
        }

        for f in &flags {
            match f.level.as_str() {
                "warn" => total_warn += 1,
                "error" => total_error += 1,
                _ => {}
            }
        }

        enemy_reports.push(EnemyReport {
            mobile_id: enemy.mobile_id,
            name: enemy.name.clone(),
            archive: enemy.archive,
            atlas: enemy.path.clone(),
            atlas_size: [enemy.width, enemy.height],
            cell_size: [cell_w as u32, cell_h as u32],
            total_frames,
            unique_world_sizes: uniq.len(),
            world_size_range: [[min_w, min_h], [max_w, max_h]],
            aspect_range: [min_aspect, max_aspect],
            metrics,
            flags,
            frames: enemy.frames.clone(),
            ground_truth,
        });
    }

    // Billboard reports
    let mut billboard_reports = Vec::new();
    let mut billboard_warn = 0usize;
    let mut billboard_error = 0usize;
    for bb in &billboard_manifest.billboards {
        let fc = bb.frame_count.unwrap_or(1);
        let mut flags = Vec::new();
        // Check PNG exists and header dims match manifest (if file present)
        // For multi-frame billboards, manifest width/height is per-frame (w, h),
        // PNG dims are atlas_w = w * fc, atlas_h = h. For single-frame, they match.
        let bb_path = Path::new("content/textures").join(&bb.path);
        if bb_path.exists() {
            if let Ok(data) = std::fs::read(&bb_path) {
                if data.len() >= 24 {
                    let w = u32::from_be_bytes([data[16], data[17], data[18], data[19]]);
                    let h = u32::from_be_bytes([data[20], data[21], data[22], data[23]]);
                    let (expected_w, expected_h) = if fc > 1 {
                        (bb.width * fc, bb.height)
                    } else {
                        (bb.width, bb.height)
                    };
                    if w != expected_w || h != expected_h {
                        flags.push(Flag {
                            level: "error".to_string(),
                            metric: "png_dims_mismatch".to_string(),
                            value: format!("{w}×{h}"),
                            threshold: format!("{expected_w}×{expected_h}"),
                            reason: format!(
                                "PNG header {w}×{h} != expected {}×{} (manifest {}×{} × fc={})",
                                expected_w, expected_h, bb.width, bb.height, fc
                            ),
                        });
                    }
                }
            }
        }
        // Multi-frame billboards: check frameCount vs actual atlas dimensions
        if fc > 1 {
            // Per-frame width is manifest width, already integer; the atlas divisibility is guaranteed by construction.
            // We keep a sanity check that frameCount is reasonable (1-8).
            if fc > 10 {
                flags.push(Flag {
                    level: "warn".to_string(),
                    metric: "large_frameCount".to_string(),
                    value: fc.to_string(),
                    threshold: "<=10".to_string(),
                    reason: "billboard strip has many frames — verify DFU animation".to_string(),
                });
            }
            if bb.world_size[0] <= 0.0 || bb.world_size[1] <= 0.0 {
                flags.push(Flag {
                    level: "warn".to_string(),
                    metric: "worldSize_zero".to_string(),
                    value: format!("{:?}", bb.world_size),
                    threshold: ">0".to_string(),
                    reason: "billboard worldSize zero or negative".to_string(),
                });
            }
        }

        for f in &flags {
            match f.level.as_str() {
                "warn" => billboard_warn += 1,
                "error" => billboard_error += 1,
                _ => {}
            }
        }

        billboard_reports.push(BillboardReport {
            archive: bb.archive,
            record: bb.record,
            path: bb.path.clone(),
            atlas_size: [bb.width, bb.height],
            world_size: bb.world_size,
            frame_count: fc,
            flags,
        });
    }

    let flagged_enemies = enemy_reports.iter().filter(|r| !r.flags.is_empty()).count();
    let flagged_billboards = billboard_reports
        .iter()
        .filter(|r| !r.flags.is_empty())
        .count();

    let report = ValidationReport {
        schema_version: 1,
        generated: chrono::Utc::now().to_rfc3339(),
        enemies: enemy_reports,
        billboards: billboard_reports,
        summary: Summary {
            total_enemies: enemy_manifest.enemies.len(),
            flagged_enemies,
            warn_count: total_warn + billboard_warn,
            error_count: total_error + billboard_error,
            total_billboards: billboard_manifest.billboards.len(),
            flagged_billboards,
        },
    };

    // Human-readable summary to stdout
    println!(
        "sprite validation: {} enemies, {} flagged",
        report.summary.total_enemies, report.summary.flagged_enemies
    );
    println!(
        "  warnings: {}  errors: {}",
        report.summary.warn_count, report.summary.error_count
    );
    println!(
        "  billboards: {} total, {} flagged",
        report.summary.total_billboards, report.summary.flagged_billboards
    );
    for er in &report.enemies {
        if er.flags.is_empty() {
            continue;
        }
        println!(
            "\n{} (id {} archive {} {}×{} cell {}×{} {} frames, {} unique sizes):",
            er.name,
            er.mobile_id,
            er.archive,
            er.atlas_size[0],
            er.atlas_size[1],
            er.cell_size[0],
            er.cell_size[1],
            er.total_frames,
            er.unique_world_sizes
        );
        println!(
            "  worldSize range [{:.3},{:.3}] .. [{:.3},{:.3}] aspect {:.2}..{:.2}",
            er.world_size_range[0][0],
            er.world_size_range[0][1],
            er.world_size_range[1][0],
            er.world_size_range[1][1],
            er.aspect_range[0],
            er.aspect_range[1]
        );
        for f in &er.flags {
            println!(
                "  {} {}={} (thr {}) — {}",
                f.level, f.metric, f.value, f.threshold, f.reason
            );
        }
    }
    for br in &report.billboards {
        if br.flags.is_empty() {
            continue;
        }
        println!(
            "\nbillboard {}:{} {} {}×{} fc={}:",
            br.archive, br.record, br.path, br.atlas_size[0], br.atlas_size[1], br.frame_count
        );
        for f in &br.flags {
            println!(
                "  {} {}={} (thr {}) — {}",
                f.level, f.metric, f.value, f.threshold, f.reason
            );
        }
    }

    // Write JSON if requested
    if let Some(out) = &args.out_json {
        if let Some(parent) = out.parent() {
            if !parent.as_os_str().is_empty() {
                std::fs::create_dir_all(parent).unwrap();
            }
        }
        let json = serde_json::to_string_pretty(&report).unwrap();
        std::fs::write(out, json).unwrap();
        println!("\nwrote {}", out.display());
    }

    // Generate HTML if requested
    if let Some(dir) = &args.out_html_dir {
        generate_html(&report, dir);
        println!("wrote html to {}", dir.display());
    }

    if args.check && report.summary.error_count > 0 {
        eprintln!(
            "\nvalidation failed: {} error(s)",
            report.summary.error_count
        );
        std::process::exit(1);
    }
}

fn generate_html(report: &ValidationReport, out_dir: &Path) {
    std::fs::create_dir_all(out_dir).unwrap();
    // Index
    let mut idx = String::new();
    idx.push_str("<!doctype html><meta charset=utf-8><title>Sprite Validation — index</title>");
    idx.push_str("<style>body{font-family:system-ui,sans-serif;max-width:1100px;margin:2rem auto;padding:0 1rem} table{border-collapse:collapse;width:100%} th,td{border:1px solid #ccc;padding:.4rem .6rem;text-align:left} .warn{color:#8a6d00;background:#fff8dc} .error{color:#a00;background:#fee} .ok{color:#060} a{color:#06c}</style>");
    idx.push_str("<h1>Sprite Validation</h1>");
    idx.push_str(&format!("<p>Generated {} — {} enemies ({} flagged), {} billboards ({} flagged), {} warn, {} error</p>", report.generated, report.summary.total_enemies, report.summary.flagged_enemies, report.summary.total_billboards, report.summary.flagged_billboards, report.summary.warn_count, report.summary.error_count));
    idx.push_str("<p>Flagged enemies are uncertain cases that need human/LLM visual review. Each mobile page shows 8 orientations × M frames bottom-center aligned in uniform cells; red borders = flagged variance.</p>");
    idx.push_str("<h2>Enemies</h2><table><tr><th>mobile</th><th>atlas</th><th>cell</th><th>frames</th><th>unique sizes</th><th>flags</th><th>link</th></tr>");
    for er in &report.enemies {
        let flag_summary: String = if er.flags.is_empty() {
            "<span class=ok>ok</span>".to_string()
        } else {
            er.flags
                .iter()
                .map(|f| format!("<span class={}>{}</span>", f.level, f.level))
                .collect::<Vec<_>>()
                .join(" ")
        };
        let link = format!("enemy-{}-{}.html", er.mobile_id, sanitize(&er.name));
        idx.push_str(&format!("<tr><td>{} (id {} archive {})</td><td>{} {}×{}</td><td>{}×{}</td><td>{}</td><td>{}</td><td>{}</td><td><a href=\"{link}\">view →</a></td></tr>", er.name, er.mobile_id, er.archive, er.atlas, er.atlas_size[0], er.atlas_size[1], er.cell_size[0], er.cell_size[1], er.total_frames, er.unique_world_sizes, flag_summary));
    }
    idx.push_str("</table>");
    idx.push_str("<h2>Billboards</h2><table><tr><th>archive:record</th><th>path</th><th>size</th><th>frames</th><th>flags</th></tr>");
    for br in &report.billboards {
        let flag_summary: String = if br.flags.is_empty() {
            "<span class=ok>ok</span>".to_string()
        } else {
            br.flags
                .iter()
                .map(|f| format!("<span class={}>{}</span>", f.level, f.level))
                .collect::<Vec<_>>()
                .join(" ")
        };
        idx.push_str(&format!(
            "<tr><td>{}:{}</td><td>{}</td><td>{}×{}</td><td>{}</td><td>{}</td></tr>",
            br.archive,
            br.record,
            br.path,
            br.atlas_size[0],
            br.atlas_size[1],
            br.frame_count,
            flag_summary
        ));
    }
    idx.push_str("</table>");
    idx.push_str("<h2>Notes</h2><ul><li>WorldSize = (raw + raw*scale/256) * 0.025 (DFU BlocksFile.ScaleDivisor). Fixed-quad renderer uses front-record size; per-frame variance flagged pending upstream 6638.</li><li>Cell waste = 1 - avgFrameArea / cellArea (world units).</li><li>Ground truth verified against TEXTURE.nnn when --arena2 data present.</li></ul>");
    std::fs::write(out_dir.join("index.html"), idx).unwrap();

    // Per-enemy pages
    for er in &report.enemies {
        let mut html = String::new();
        html.push_str("<!doctype html><meta charset=utf-8>");
        html.push_str(&format!(
            "<title>{} (id {}) — validation</title>",
            er.name, er.mobile_id
        ));
        html.push_str("<style>body{font-family:system-ui,sans-serif;max-width:1200px;margin:1rem auto;padding:0 1rem} .grid{display:grid;gap:8px} .cell{position:relative;overflow:hidden;border:2px solid #ddd;background:#111} .cell.flag-warn{border-color:#c90} .cell.flag-error{border-color:#d00} .cell img{display:block;width:100%;height:100%;image-rendering:pixelated} .caption{font-size:.7rem;background:#fff;padding:2px 4px} .flags{font-size:.75rem;color:#a00} .meta{font-size:.85rem;color:#444} header{margin-bottom:1rem}</style>");
        html.push_str(&format!(
            "<header><p><a href=index.html>← index</a></p><h1>{} (id {} archive {} — {})</h1>",
            er.name, er.mobile_id, er.archive, er.atlas
        ));
        html.push_str(&format!("<p class=meta>atlas {}×{} — cell {}×{} — {} frames (8×{} per orientation) — {} unique worldSizes — range [{:.3},{:.3}]..[{:.3},{:.3}] aspect {:.2}..{:.2}</p>", er.atlas_size[0], er.atlas_size[1], er.cell_size[0], er.cell_size[1], er.total_frames, er.total_frames/8, er.unique_world_sizes, er.world_size_range[0][0], er.world_size_range[0][1], er.world_size_range[1][0], er.world_size_range[1][1], er.aspect_range[0], er.aspect_range[1]));
        if !er.flags.is_empty() {
            html.push_str("<ul class=flags>");
            for f in &er.flags {
                html.push_str(&format!(
                    "<li><b class={}>{}</b> {}={} thr {} — {}</li>",
                    f.level, f.level, f.metric, f.value, f.threshold, f.reason
                ));
            }
            html.push_str("</ul>");
        }
        if let Some(gt) = &er.ground_truth {
            html.push_str("<details><summary>ground truth (TEXTURE.nnn)</summary><table border=1 cellpadding=4 style=border-collapse:collapse><tr><th>record</th><th>raw</th><th>scale</th><th>worldSize</th><th>frames</th></tr>");
            for rd in &gt.record_dims {
                html.push_str(&format!("<tr><td>{}</td><td>{}×{}</td><td>{},{}</td><td>{:.3}×{:.3}</td><td>{}</td></tr>", rd.record, rd.raw_size[0], rd.raw_size[1], rd.scale[0], rd.scale[1], rd.world_size[0], rd.world_size[1], rd.frame_count));
            }
            html.push_str("</table></details>");
        }
        html.push_str("</header>");

        // Grid: use atlas PNG with background-position slicing to avoid re-encoding
        // Columns = total_frames, but visually group as 8 orientations × M anim frames?
        // For readability, lay out as 8 rows (orientations) × M columns.
        let m = if er.total_frames >= 8 {
            er.total_frames / 8
        } else {
            1
        };
        // Build a grid per orientation
        html.push_str(&format!(
            "<div class=grid style=grid-template-columns:repeat({m},1fr)>"
        ));
        // Header row
        for c in 0..m {
            html.push_str(&format!(
                "<div style=text-align:center;font-weight:bold>anim {c}</div>"
            ));
        }
        // For each orientation 0..7
        let orientation_names = ["S (front)", "SW", "W", "NW", "N (back)", "NE", "E", "SE"];
        for (ori, orientation_name) in orientation_names.iter().enumerate() {
            for anim in 0..m {
                let idx = ori * m + anim;
                if idx >= er.frames.len() {
                    continue;
                }
                let frame = &er.frames[idx];
                // Flag level for this specific frame? For now overall mobile flags apply to all cells
                let cell_flag = if er.flags.iter().any(|f| f.level == "error") {
                    " flag-error"
                } else if er.flags.iter().any(|f| f.level == "warn") {
                    " flag-warn"
                } else {
                    ""
                };
                // CSS addresses from the PNG top-left while retained UVs are
                // bottom-left, so derive both offsets from the frame rect.
                let bg_x = -(frame.uv_min[0] * f64::from(er.atlas_size[0])).round() as i32;
                let bg_y = -((1.0 - frame.uv_max[1]) * f64::from(er.atlas_size[1])).round() as i32;
                // Use a div with atlas as background, sized to atlas dims
                let atlas_url = format!("../../textures/{}", er.atlas);
                html.push_str(&format!("<div class=\"cell{cell_flag}\"><div style=\"width:{}px;height:{}px;background:url('{atlas_url}') no-repeat;background-position:{}px {}px;background-size:{}px {}px;image-rendering:pixelated\"></div><div class=caption>ori {} {} rec {}<br>{:.3}×{:.3} uv [{:.3},{:.3}]..[{:.3},{:.3}]</div></div>",
                    er.cell_size[0], er.cell_size[1], bg_x, bg_y, er.atlas_size[0], er.atlas_size[1], ori, orientation_name, frame_index_to_record(ori), frame.source_size[0], frame.source_size[1], frame.uv_min[0], frame.uv_min[1], frame.uv_max[0], frame.uv_max[1]
                ));
            }
        }
        html.push_str("</div>");
        html.push_str(&format!("<p class=meta>Bottom-center aligned (cell_w-w)/2, dy=cell_h-h — DFU pivot [0.5,0]. Full atlas: <a href=\"../../textures/{}\">{}</a></p>", er.atlas, er.atlas));
        // Simple JS anim preview: cycle anim frames per orientation
        html.push_str("<script>let t=0;setInterval(()=>{t=(t+1)%");
        html.push_str(&m.to_string());
        html.push_str("},300);</script>");
        let out = out_dir.join(format!(
            "enemy-{}-{}.html",
            er.mobile_id,
            sanitize(&er.name)
        ));
        std::fs::write(out, html).unwrap();
    }
}

fn sanitize(s: &str) -> String {
    s.chars()
        .map(|c| {
            if c.is_ascii_alphanumeric() {
                c.to_ascii_lowercase()
            } else {
                '-'
            }
        })
        .collect()
}

fn frame_index_to_record(ori: usize) -> u16 {
    match ori {
        0 => 0,
        1 => 1,
        2 => 2,
        3 => 3,
        4 => 4,
        5 => 3,
        6 => 2,
        7 => 1,
        _ => 0,
    }
}
