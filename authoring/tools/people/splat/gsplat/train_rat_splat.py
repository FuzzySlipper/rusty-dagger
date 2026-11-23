#!/usr/bin/env python3
"""Fit a capped, RGB-only 3DGS to orbit_render.py output (gsplat 1.5.3, GPU lab).

  python train_rat_splat.py DATA_DIR OUT_DIR [--steps 7000] [--cap 48000]

DATA_DIR holds images/NNN.png (RGBA), cameras.npz and init_points.npz from
orbit_render.py. Writes OUT_DIR/splat.npz (means, log scales, wxyz quats, logit
opacities, linear RGB), train-log.json and held-out comparison renders.
SH degree 0 because the Viggle VSPLAT format stores plain RGB.
"""
import argparse, json, math, os, random, time
import numpy as np
import torch, torch.nn.functional as F
from PIL import Image
from gsplat import rasterization, MCMCStrategy

C0 = 0.28209479177387814


def ssim(x, y, ws=11):
    c = torch.arange(ws, device=x.device) - ws // 2
    g = torch.exp(-(c ** 2) / (2 * 1.5 ** 2)); g = g / g.sum()
    w = (g[:, None] * g[None, :]).expand(x.shape[1], 1, ws, ws)
    f = lambda t: F.conv2d(t, w, padding=ws // 2, groups=t.shape[1])
    mx, my = f(x), f(y)
    sx, sy, sxy = f(x * x) - mx ** 2, f(y * y) - my ** 2, f(x * y) - mx * my
    C1, C2 = 0.01 ** 2, 0.03 ** 2
    return (((2 * mx * my + C1) * (2 * sxy + C2)) / ((mx ** 2 + my ** 2 + C1) * (sx + sy + C2))).mean()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('data'); ap.add_argument('out')
    ap.add_argument('--steps', type=int, default=7000)
    ap.add_argument('--cap', type=int, default=48000)
    ap.add_argument('--seed', type=int, default=0)
    a = ap.parse_args()
    os.makedirs(a.out + '/renders', exist_ok=True)
    torch.manual_seed(a.seed); np.random.seed(a.seed); random.seed(a.seed)
    cams = np.load(a.data + '/cameras.npz')
    viewmats = torch.from_numpy(cams['viewmats']).float().cuda()
    Ks = torch.from_numpy(cams['Ks']).float().cuda()
    files = sorted(os.listdir(a.data + '/images'))
    rgba = np.stack([np.asarray(Image.open(f'{a.data}/images/{f}').convert('RGBA'), np.uint8) for f in files])
    H, W = rgba.shape[1:3]
    images = torch.from_numpy(rgba)  # CPU, uint8
    n = len(files)
    held = [i for i in range(n) if i % 10 == 0]
    train = [i for i in range(n) if i % 10 != 0]

    init = np.load(a.data + '/init_points.npz')
    p = torch.from_numpy(init['points']).float().cuda()
    col = torch.from_numpy(init['colors']).float().cuda()
    N = p.shape[0]
    spacing = float(init['spacing'])
    params = torch.nn.ParameterDict({
        'means': torch.nn.Parameter(p.clone()),
        'scales': torch.nn.Parameter(torch.log(torch.full((N, 3), spacing * 0.6, device='cuda'))),
        'quats': torch.nn.Parameter(torch.cat([torch.ones(N, 1, device='cuda'), torch.zeros(N, 3, device='cuda')], 1)),
        'opacities': torch.nn.Parameter(torch.logit(torch.full((N,), 0.6, device='cuda'))),
        'sh0': torch.nn.Parameter(((col - 0.5) / C0).unsqueeze(1)),
    })
    scene_scale = float((p.max(0).values - p.min(0).values).norm() / 2)
    lrs = {'means': 1.6e-4 * scene_scale, 'scales': 5e-3, 'quats': 1e-3, 'opacities': 5e-2, 'sh0': 2.5e-3}
    opts = {k: torch.optim.Adam([{'params': [params[k]], 'name': k}], lr=lrs[k], eps=1e-15) for k in params}
    mean_sched = torch.optim.lr_scheduler.ExponentialLR(opts['means'], gamma=0.01 ** (1.0 / a.steps))
    strategy = MCMCStrategy(cap_max=a.cap, refine_start_iter=300, refine_stop_iter=int(a.steps * 0.8), refine_every=100)
    strategy.check_sanity(params, opts)
    state = strategy.initialize_state()

    def render(i, bg):
        out = rasterization(params['means'], F.normalize(params['quats'], dim=-1), torch.exp(params['scales']),
                            torch.sigmoid(params['opacities']), params['sh0'], viewmats[i:i + 1], Ks[i:i + 1], W, H,
                            sh_degree=0, packed=False, near_plane=0.01, far_plane=50.0,
                            backgrounds=bg[None], render_mode='RGB')
        return out[0][0].permute(2, 0, 1), out[1][0].permute(2, 0, 1), out[2]

    def target(i, bg):
        t = images[i].cuda().float().div(255).permute(2, 0, 1)
        return t[:3] * t[3:] + bg[:, None, None] * (1 - t[3:]), t[3:]

    @torch.no_grad()
    def evaluate(idx):
        bg = torch.tensor([0.5, 0.5, 0.5], device='cuda')
        vals = []
        for i in idx:
            r, _, _ = render(i, bg); t, m = target(i, bg)
            vals.append(float(-10 * math.log10(float(((r - t) ** 2).mean()) + 1e-12)))
        return round(float(np.mean(vals)), 3)

    log = {'config': vars(a) | {'views': n, 'held_out': len(held), 'size': [W, H], 'init': N, 'sh_degree': 0,
                                 'strategy': 'MCMC', 'scene_scale': scene_scale}, 'evals': []}
    t0 = time.time()
    for step in range(a.steps):
        i = random.choice(train)
        bg = torch.rand(3, device='cuda')
        r, alpha, info = render(i, bg)
        t, m = target(i, bg)
        loss = 0.8 * (r - t).abs().mean() + 0.2 * (1 - ssim(r[None], t[None])) + 0.1 * (alpha - m).abs().mean()
        loss = loss + 0.01 * torch.sigmoid(params['opacities']).mean() + 0.01 * torch.exp(params['scales']).mean()
        strategy.step_pre_backward(params, opts, state, step, info)
        loss.backward()
        for o in opts.values():
            o.step(); o.zero_grad(set_to_none=True)
        mean_sched.step()
        strategy.step_post_backward(params, opts, state, step, info, lr=mean_sched.get_last_lr()[0])
        if step % 1000 == 0 or step == a.steps - 1:
            psnr = evaluate(held)
            log['evals'].append({'step': step, 'heldout_psnr': psnr, 'gaussians': len(params['means'])})
            print(f'step {step} loss {loss.item():.4f} held {psnr} gs {len(params["means"])} '
                  f'{time.time() - t0:.0f}s mem {torch.cuda.max_memory_allocated() / 1e9:.2f}GB', flush=True)
    log['final'] = {'heldout_psnr': evaluate(held), 'gaussians': len(params['means']),
                    'seconds': round(time.time() - t0, 1), 'vram_gb': round(torch.cuda.max_memory_allocated() / 1e9, 2)}
    print('FINAL', json.dumps(log['final']), flush=True)
    with torch.no_grad():
        bg = torch.tensor([0.5, 0.5, 0.5], device='cuda')
        for i in held[::3]:
            r, _, _ = render(i, bg); t, _ = target(i, bg)
            row = torch.cat([t, r], 2).permute(1, 2, 0).clamp(0, 1).mul(255).byte().cpu().numpy()
            Image.fromarray(row).save(f'{a.out}/renders/heldout_{i:03d}.png')
        rgb = (params['sh0'][:, 0] * C0 + 0.5).clamp(0, 1)
        np.savez_compressed(a.out + '/splat.npz', means=params['means'].cpu().numpy(),
                            scales=params['scales'].cpu().numpy(),
                            quats=F.normalize(params['quats'], dim=-1).cpu().numpy(),
                            opacities=params['opacities'].cpu().numpy(), rgb=rgb.cpu().numpy())
    json.dump(log, open(a.out + '/train-log.json', 'w'), indent=1)
    print('saved', a.out, flush=True)


if __name__ == '__main__':
    main()
