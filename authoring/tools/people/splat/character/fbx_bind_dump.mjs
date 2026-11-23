// fbx_bind_dump.mjs SKINNED.fbx OUT.json: each mixamorig bone's bind world matrix (inverse of three.js's boneInverses; column-major 16) from a
// skinned Mixamo FBX (X Bot.fbx), for mocap.py's retarget.
import fs from 'fs';
globalThis.self = globalThis;
const T = `${process.env.ASSET_PIPELINE || process.env.HOME + '/dev/asset-pipeline'}/legacy/experiments/animations/node_modules/three/`;
const THREE = await import(T + 'build/three.module.js');
const { FBXLoader } = await import(T + 'examples/jsm/loaders/FBXLoader.js');
const buf = fs.readFileSync(process.argv[2]);
const root = new FBXLoader().parse(buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.byteLength), '');
let sk = null; root.traverse((o) => { if (o.isSkinnedMesh && !sk) sk = o.skeleton; });
const out = {};
if (sk) sk.bones.forEach((b, i) => { const m = sk.boneInverses[i].clone().invert(); out['mixamorig:' + b.name.split(':').pop().replace(/^mixamorig\d*/, '')] = m.toArray(); });
fs.writeFileSync(process.argv[3], JSON.stringify(out)); console.log('bones with bind', Object.keys(out).length);
