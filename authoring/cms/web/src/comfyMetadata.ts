/** Zero-dependency ComfyUI PNG tEXt/iTXt metadata extractor for concept art uploads. */

export interface ExtractedComfyMetadata {
  detected: boolean;
  prompt: string;
  negativePrompt?: string;
  seed: string | null;
  model?: string;
  sampler?: string;
  scheduler?: string;
  steps?: number;
  cfg?: number;
  denoise?: number;
  loras?: string[];
}

interface ComfyNode {
  class_type?: string;
  inputs?: Record<string, unknown>;
}

const PNG_SIG = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

function readPngTextChunks(buffer: ArrayBuffer): Record<string, string> {
  const bytes = new Uint8Array(buffer);
  if (bytes.length < 8) return {};
  for (let i = 0; i < 8; i++) {
    if (bytes[i] !== PNG_SIG[i]) return {};
  }
  const view = new DataView(buffer);
  const decoder = new TextDecoder('utf-8', { fatal: false });
  const latin1 = new TextDecoder('latin1');
  const out: Record<string, string> = {};

  let offset = 8;
  while (offset + 12 <= bytes.length) {
    const length = view.getUint32(offset, false);
    const type = latin1.decode(bytes.subarray(offset + 4, offset + 8));
    const dataStart = offset + 8;
    const dataEnd = dataStart + length;
    if (dataEnd + 4 > bytes.length) break;

    if (type === 'tEXt') {
      const chunk = bytes.subarray(dataStart, dataEnd);
      const nul = chunk.indexOf(0);
      if (nul > 0) {
        const key = latin1.decode(chunk.subarray(0, nul));
        const val = decoder.decode(chunk.subarray(nul + 1));
        out[key] = val;
      }
    } else if (type === 'iTXt') {
      const chunk = bytes.subarray(dataStart, dataEnd);
      const nul1 = chunk.indexOf(0);
      if (nul1 > 0 && nul1 + 2 < chunk.length) {
        const key = latin1.decode(chunk.subarray(0, nul1));
        const compressionFlag = chunk[nul1 + 1];
        // Skip compression method byte, language tag (nul-terminated), translated keyword (nul-terminated)
        const nul2 = chunk.indexOf(0, nul1 + 3);
        const nul3 = nul2 >= 0 ? chunk.indexOf(0, nul2 + 1) : -1;
        if (compressionFlag === 0 && nul3 >= 0) {
          out[key] = decoder.decode(chunk.subarray(nul3 + 1));
        }
      }
    }

    offset = dataEnd + 4;
    if (type === 'IEND') break;
  }
  return out;
}

function followRef(value: unknown, nodes: Record<string, ComfyNode>): ComfyNode | null {
  if (Array.isArray(value) && value.length >= 1) {
    const id = String(value[0]);
    return nodes[id] ?? null;
  }
  return null;
}

function extractTextFromNode(
  node: ComfyNode | null,
  nodes: Record<string, ComfyNode>,
  visited = new Set<ComfyNode>(),
): string {
  if (!node || visited.has(node)) return '';
  visited.add(node);
  const inputs = node.inputs ?? {};
  for (const key of ['prompt', 'text', 't5xxl', 'clip_l', 'positive', 'string']) {
    const val = inputs[key];
    if (typeof val === 'string' && val.trim()) {
      return val.trim();
    }
    const ref = followRef(val, nodes);
    if (ref) {
      const nested = extractTextFromNode(ref, nodes, visited);
      if (nested) return nested;
    }
  }
  // Walk conditioning / guider wrappers (e.g. BasicGuider, CFGGuider, FluxGuidance, ConditioningSetArea)
  for (const key of ['conditioning', 'guider', 'conditioning_1', 'conditioning_to']) {
    const ref = followRef(inputs[key], nodes);
    if (ref) {
      const nested = extractTextFromNode(ref, nodes, visited);
      if (nested) return nested;
    }
  }
  return '';
}

function asFiniteNumber(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

export function extractComfyMetadata(buffer: ArrayBuffer): ExtractedComfyMetadata {
  const chunks = readPngTextChunks(buffer);
  const rawPrompt = chunks.prompt;
  if (!rawPrompt) {
    return { detected: false, prompt: '', seed: null };
  }

  let graph: Record<string, ComfyNode>;
  try {
    const parsed = JSON.parse(rawPrompt);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
      return { detected: false, prompt: '', seed: null };
    }
    graph = parsed as Record<string, ComfyNode>;
  } catch {
    return { detected: false, prompt: '', seed: null };
  }

  const allNodes = Object.values(graph).filter((n): n is ComfyNode => Boolean(n && typeof n === 'object'));
  if (!allNodes.length) {
    return { detected: false, prompt: '', seed: null };
  }

  let prompt = '';
  let negativePrompt: string | undefined;
  let seed: string | null = null;
  let model: string | undefined;
  let sampler: string | undefined;
  let scheduler: string | undefined;
  let steps: number | undefined;
  let cfg: number | undefined;
  let denoise: number | undefined;
  const loras: string[] = [];

  // 1. Inspect samplers first (best source for positive/negative conditioning, seed, steps, cfg)
  for (const node of allNodes) {
    const ct = node.class_type ?? '';
    const inp = node.inputs ?? {};
    if (ct === 'KSampler' || ct === 'KSamplerAdvanced' || ct === 'SamplerCustom' || ct === 'SamplerCustomAdvanced') {
      if (!prompt) {
        const posNode = followRef(inp.positive, graph) ?? followRef(inp.guider, graph);
        if (posNode) prompt = extractTextFromNode(posNode, graph);
      }
      if (!negativePrompt) {
        const negNode = followRef(inp.negative, graph);
        if (negNode) {
          const neg = extractTextFromNode(negNode, graph);
          if (neg) negativePrompt = neg;
        }
      }
      if (seed === null) {
        const rawSeed = inp.seed ?? inp.noise_seed;
        if (typeof rawSeed === 'number' || typeof rawSeed === 'string') {
          seed = String(rawSeed);
        } else {
          const noiseNode = followRef(inp.noise, graph);
          const nSeed = noiseNode?.inputs?.noise_seed ?? noiseNode?.inputs?.seed;
          if (typeof nSeed === 'number' || typeof nSeed === 'string') {
            seed = String(nSeed);
          }
        }
      }
      if (typeof inp.sampler_name === 'string') sampler = inp.sampler_name;
      else if (!sampler) {
        const sNode = followRef(inp.sampler, graph);
        if (typeof sNode?.inputs?.sampler_name === 'string') sampler = sNode.inputs.sampler_name;
      }
      if (typeof inp.scheduler === 'string') scheduler = inp.scheduler;
      if (steps === undefined) steps = asFiniteNumber(inp.steps);
      if (cfg === undefined) cfg = asFiniteNumber(inp.cfg);
      if (denoise === undefined) denoise = asFiniteNumber(inp.denoise);
    }
  }

  // 2. Scan remaining nodes for model, LoRAs, RandomNoise seed, sampler/scheduler, and fallback prompt
  for (const node of allNodes) {
    const ct = node.class_type ?? '';
    const inp = node.inputs ?? {};

    if (!model) {
      for (const k of ['unet_name', 'ckpt_name', 'model_name', 'diffusion_model']) {
        if (typeof inp[k] === 'string' && (inp[k] as string).trim()) {
          model = (inp[k] as string).trim();
          break;
        }
      }
    }

    if (ct.toLowerCase().includes('lora') && typeof inp.lora_name === 'string' && inp.lora_name.trim()) {
      const strength = asFiniteNumber(inp.strength_model);
      loras.push(strength !== undefined ? `${inp.lora_name.trim()} (${strength})` : inp.lora_name.trim());
    }

    if (seed === null && (ct === 'RandomNoise' || ct.toLowerCase().includes('seed'))) {
      const s = inp.noise_seed ?? inp.seed;
      if (typeof s === 'number' || typeof s === 'string') seed = String(s);
    }

    if (!sampler && ct === 'KSamplerSelect' && typeof inp.sampler_name === 'string') {
      sampler = inp.sampler_name;
    }

    if (!scheduler && ct.toLowerCase().includes('scheduler')) {
      if (typeof inp.scheduler === 'string') scheduler = inp.scheduler;
      if (steps === undefined) steps = asFiniteNumber(inp.steps);
    }

    if (!prompt && (ct.includes('TextEncode') || ct.includes('Conditioning'))) {
      const txt = extractTextFromNode(node, graph);
      if (txt) prompt = txt;
    }
  }

  return {
    detected: true,
    prompt,
    negativePrompt,
    seed,
    model,
    sampler,
    scheduler,
    steps,
    cfg,
    denoise,
    loras: loras.length ? loras : undefined,
  };
}

export function arrayBufferToBase64(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  const CHUNK = 0x8000;
  let binary = '';
  for (let i = 0; i < bytes.length; i += CHUNK) {
    binary += String.fromCharCode(...bytes.subarray(i, i + CHUNK));
  }
  return btoa(binary);
}
