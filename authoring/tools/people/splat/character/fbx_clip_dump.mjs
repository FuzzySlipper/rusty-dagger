// fbx_clip_dump.mjs CLIP.fbx OUT.json [FPS]: a Mixamo FBX clip (ASCII or binary, read with three.js's FBXLoader) sampled at FPS
// (30): per frame every mixamorig bone's local rotation (xyzw), the Hips' world rotation and position, and every bone's world
// position, all in metres (the file's units scaled by its root), Y up. Bone names keep the 'mixamorig:' prefix. For mocap.py.
import fs from 'fs';
globalThis.self = globalThis;
const T = `${process.env.ASSET_PIPELINE || process.env.HOME + '/dev/asset-pipeline'}/legacy/experiments/animations/node_modules/three/`;
const THREE = await import(T + 'build/three.module.js');
const { FBXLoader } = await import(T + 'examples/jsm/loaders/FBXLoader.js');
const [path, out, fpsArg] = process.argv.slice(2); const fps = Number(fpsArg || 30);
const buf = fs.readFileSync(path);
const root = new FBXLoader().parse(buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.byteLength), '');
const clip = root.animations[0]; const mixer = new THREE.AnimationMixer(root); mixer.clipAction(clip).play();
const bones = []; root.traverse((o) => { if (o.isBone && /mixamorig/.test(o.name)) bones.push(o); });
const name = (o) => 'mixamorig:' + o.name.split(':').pop().replace(/^mixamorig\d*/, '');
const idx = new Map(bones.map((b, i) => [b, i]));
root.updateMatrixWorld(true);
const ws = new THREE.Vector3(); bones[0].parent.getWorldScale(ws);   // units: the scale above the top bone (cm files carry 0.01 or not)
const n = Math.round(clip.duration * fps);
const frames = [];
for (let f = 0; f <= n; f++) {
  mixer.setTime(f / fps); root.updateMatrixWorld(true);
  frames.push(bones.map((b) => {
    const p = new THREE.Vector3(); const q = new THREE.Quaternion(); b.getWorldPosition(p); b.getWorldQuaternion(q);
    return { local: b.quaternion.toArray(), world_q: q.toArray(), world_p: p.toArray() };
  }));
}
mixer.setTime(0); root.updateMatrixWorld(true);
fs.writeFileSync(out, JSON.stringify({
  source: path, fps, duration: clip.duration, frames: n + 1, parent_scale: ws.toArray(),
  bones: bones.map((b) => ({ name: name(b), parent: idx.has(b.parent) ? idx.get(b.parent) : -1, rest_t: b.position.toArray(), rest_q: b.quaternion.toArray() })),
  samples: frames,
}));
console.log(JSON.stringify({ bones: bones.length, frames: n + 1, duration: clip.duration, parent_scale: ws.toArray(), hips_y0: frames[0][0].world_p[1] }));
