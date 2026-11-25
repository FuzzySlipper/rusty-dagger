import * as THREE from 'three';
import type { Quat } from '../types';

interface RstatmshDescriptor {
  payload: {
    layout: {
      vertexCount: number;
      indexCount: number;
      indexWidth: 'u16' | 'u32';
    };
    source: {
      positionsByteOffset: number;
      normalsByteOffset?: number;
      uvsByteOffset?: number;
      indicesByteOffset: number;
    };
    groups: Array<{
      materialSlot: number;
      start: number;
      count: number;
    }>;
  };
  materialSlots: Array<{
    slot: number;
    material: string;
  }>;
}

export interface ParsedRstatmsh {
  geometry: THREE.BufferGeometry;
  materialNames: string[];
  materialSlots: number[];
}

const MAGIC = 'RSTATMSH';

/**
 * Parse a Rusty Engine `.rstatmsh` binary file directly into a Three.js BufferGeometry
 * with one geometry group per material slot.
 */
export function parseRstatmsh(buffer: ArrayBuffer): ParsedRstatmsh {
  if (buffer.byteLength < 12) {
    throw new Error('rstatmsh buffer too small');
  }
  const magic = new TextDecoder().decode(new Uint8Array(buffer, 0, 8));
  if (magic !== MAGIC) {
    throw new Error(`Invalid rstatmsh magic: ${magic}`);
  }
  const view = new DataView(buffer);
  const jsonLength = view.getUint32(8, true);
  const jsonText = new TextDecoder().decode(new Uint8Array(buffer, 12, jsonLength));
  const descriptor = JSON.parse(jsonText) as RstatmshDescriptor;

  const resourceStart = 12 + jsonLength;
  const { layout, source, groups } = descriptor.payload;
  const vCount = layout.vertexCount;
  const iCount = layout.indexCount;

  // Copy slices to aligned typed arrays (resourceStart may not be 4-byte aligned in general).
  const readF32 = (byteOffset: number, count: number): Float32Array => {
    const start = resourceStart + byteOffset;
    return new Float32Array(buffer.slice(start, start + count * 4));
  };

  const positions = readF32(source.positionsByteOffset, vCount * 3);
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));

  if (source.normalsByteOffset !== undefined) {
    const normals = readF32(source.normalsByteOffset, vCount * 3);
    geometry.setAttribute('normal', new THREE.BufferAttribute(normals, 3));
  }

  if (source.uvsByteOffset !== undefined) {
    const uvs = readF32(source.uvsByteOffset, vCount * 2);
    geometry.setAttribute('uv', new THREE.BufferAttribute(uvs, 2));
  }

  const idxStart = resourceStart + source.indicesByteOffset;
  if (layout.indexWidth === 'u16') {
    const indices = new Uint16Array(buffer.slice(idxStart, idxStart + iCount * 2));
    geometry.setIndex(new THREE.BufferAttribute(indices, 1));
  } else {
    const indices = new Uint32Array(buffer.slice(idxStart, idxStart + iCount * 4));
    geometry.setIndex(new THREE.BufferAttribute(indices, 1));
  }

  if (source.normalsByteOffset === undefined) {
    geometry.computeVertexNormals();
  }

  // Build ordered material list indexed by sequential materialIndex
  const slotToMaterial = new Map<number, string>();
  for (const slot of descriptor.materialSlots ?? []) {
    slotToMaterial.set(slot.slot, slot.material);
  }

  const materialNames: string[] = [];
  const materialSlots: number[] = [];
  const slotToIndex = new Map<number, number>();
  for (const group of groups ?? []) {
    let matIdx = slotToIndex.get(group.materialSlot);
    if (matIdx === undefined) {
      matIdx = materialNames.length;
      slotToIndex.set(group.materialSlot, matIdx);
      materialNames.push(slotToMaterial.get(group.materialSlot) ?? '');
      materialSlots.push(group.materialSlot);
    }
    geometry.addGroup(group.start, group.count, matIdx);
  }

  geometry.computeBoundingBox();
  geometry.computeBoundingSphere();

  return { geometry, materialNames, materialSlots };
}

/** A published right-handed rotation, which the block documents state for every model, door and gate. */
export function blockRotation(q: Quat): THREE.Quaternion {
  return new THREE.Quaternion(q.x, q.y, q.z, q.w);
}
