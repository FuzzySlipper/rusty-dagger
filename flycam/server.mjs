// Static server for the flycam debug view:
//   /                     -> flycam/index.html
//   /vendor/three/*       -> engine's installed three package
//   /content/*            -> rusty-dagger/content
//   /health               -> project identity for den-serve
// Binds LAN-facing (den-serve passes --host). Read-only; path-contained.
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';

const THREE_DIR = '/home/dev/rusty-engine/render/node_modules/.pnpm/three@0.184.0/node_modules/three';
const CONTENT_DIR = new URL('../content/', import.meta.url).pathname;
const VIEWER = new URL('./index.html', import.meta.url).pathname;
const PROJECT = 'rusty-dagger';

const MIME = {
  '.html': 'text/html',
  '.js': 'text/javascript',
  '.mjs': 'text/javascript',
  '.glb': 'model/gltf-binary',
  '.png': 'image/png',
  '.json': 'application/json',
};

function safeJoin(root, rel) {
  const p = normalize(join(root, rel));
  if (!p.startsWith(root)) return null;
  return p;
}

export function startServer({ host = '127.0.0.1', port = 0 } = {}) {
  const server = createServer(async (req, res) => {
    try {
      const url = new URL(req.url, 'http://127.0.0.1/');
      if (url.pathname === '/health') {
        res.writeHead(200, { 'content-type': 'application/json', 'x-den-project': PROJECT });
        res.end(JSON.stringify({ status: 'ok', project: PROJECT, view: 'flycam' }));
        return;
      }
      let file;
      if (url.pathname === '/' || url.pathname === '/index.html') {
        file = VIEWER;
      } else if (url.pathname.startsWith('/vendor/three/')) {
        file = safeJoin(THREE_DIR, url.pathname.slice('/vendor/three/'.length));
      } else if (url.pathname.startsWith('/content/')) {
        file = safeJoin(CONTENT_DIR, url.pathname.slice('/content/'.length));
      } else {
        res.writeHead(404).end('not found');
        return;
      }
      if (!file) {
        res.writeHead(403).end('forbidden');
        return;
      }
      const data = await readFile(file);
      res.writeHead(200, { 'content-type': MIME[extname(file)] ?? 'application/octet-stream' });
      res.end(data);
    } catch (e) {
      res.writeHead(500).end(String(e));
    }
  });
  return new Promise((resolve) => {
    server.listen(port, host, () => resolve({ server, port: server.address().port }));
  });
}

if (process.argv[1] && import.meta.url === new URL(`file://${process.argv[1]}`).href) {
  const host = process.env.FLYCAM_HOST ?? process.argv[2] ?? '127.0.0.1';
  const port = Number(process.env.FLYCAM_PORT ?? process.argv[3] ?? 4174);
  const { port: bound } = await startServer({ host, port });
  console.log(`rusty-dagger flycam listening on http://${host}:${bound}/`);
}
