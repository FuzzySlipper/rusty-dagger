#!/usr/bin/env python3
"""One round of the sprite -> splat -> repaint loop (gsplat 1.5.3, GPU lab).

  python train_loop_splat.py DATA_DIR OUT_DIR [--steps 6000] [--cap 48000]
      [--anchor-weight 3] [--ring 81] [--ring-elevation 8] [--ring-fov 30 --ring-distance 3]
      [--ring-sweep=LOW,HIGH] [--flat] [--freeze-poses] [--pose-lr 1e-3]
      [--opacity-reg 0.01] [--scale-reg 0.01] [--firm 0 --firm-from 0.6] [--pin --pin-opacity 0.95]
      [--shell SHELL.npz --shell-reach 0.015]

DATA_DIR (anchor_dataset.py / loop_dataset.py) holds images/NNN.png (RGBA), optionally ignore/NNN.png (pixels not judged),
cameras.npz, init_points.npz and views.json with a role per image:
  anchor   a directional sprite; trained with its pose refined (rotation,
           translation, focal scale), sampled ANCHOR_WEIGHT times as often.
           Anchor 0 (front) is never moved, which fixes the frame.
  ring     a repainted view from the previous round on an exact camera.
  heldout  never trained on. Scored after training by fitting only its pose
           to the frozen splat, then PSNR against it (whole image on the
           sprite's grey, and inside the subject's silhouette).
  exact    never trained on, like heldout, but on a known camera (a repainted
           ring frame kept back by loop_dataset.py). Scored without a pose fit.
--freeze-poses trains on the anchors' given cameras. --pose-lr sets the step for
refined anchor poses; solved cameras need far less than nominal ones. --ring 0
skips the ring. --opacity-reg and --scale-reg are the MCMC penalties. --firm adds
a penalty on half-transparent Gaussians, o * (1 - o), over the last part of
training from --firm-from, so the splat settles into fewer, more opaque ones.
--pin keeps every Gaussian where init_points.npz puts it, as a flat disc of fixed
--pin-opacity (lower values blend neighbouring discs and hide per-disc noise)
on the given `quats` (authoring/tools/people/splat/mesh-splat/mesh_seed.py). Only colour and disc size
are learned, and only inside each picture's subject, so a picture that disagrees
with the shape cannot bend it. splat.npz then also holds `seen`, how often each
disc received a colour gradient.
--shell holds every Gaussian centre within --shell-reach (scene units) of a scaffold surface while it trains as usual
(authoring/tools/people/splat/scaffold/bound_data.py writes SHELL.npz: `dist`, an unsigned distance grid indexed [z, y, x], with `origin`
and `voxel`). After each step a centre farther out than the reach is moved back to it along the grid's gradient, so
pictures that disagree cannot grow shape away from the scaffold; inside the reach the fit is free. score.json then
records the reach and how far the centres ended from the surface.
--flat trains disc-shaped Gaussians (third axis fixed near zero), for thin
membranes such as wings. --ring-sweep makes the ring rise and fall between
two elevations over the circle (low at the front, high at the back), so one
repaint covers views from above and below.
Writes OUT/splat.npz, OUT/poses.npz (refined anchor viewmats/Ks), OUT/score.json,
OUT/anchors.png (sprite | render rows) and OUT/ring/: control/NNN.png (RGB on
the sprite grey, for vace_ring.py), alpha/NNN.png, cameras.npz and ring.json.
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


def rodrigues(v):
    theta = v.norm(dim=-1, keepdim=True).clamp_min(1e-8)
    k = v / theta
    K = torch.zeros(v.shape[0], 3, 3, device=v.device)
    K[:, 0, 1], K[:, 0, 2], K[:, 1, 0] = -k[:, 2], k[:, 1], k[:, 2]
    K[:, 1, 2], K[:, 2, 0], K[:, 2, 1] = -k[:, 0], -k[:, 1], k[:, 0]
    s, c = torch.sin(theta)[..., None], torch.cos(theta)[..., None]
    return torch.eye(3, device=v.device) + s * K + (1 - c) * (K @ K)


def ring_cameras(n, el, dist, fov, size, sweep=None):
    mats, elevations = [], []
    for i in range(n):
        el_i = el if sweep is None else sweep[0] + (sweep[1] - sweep[0]) * (0.5 - 0.5 * math.cos(2 * math.pi * i / n))
        elevations.append(el_i)
        e, a = math.radians(el_i), math.radians(360 * i / n)
        pos = dist * np.array([math.cos(e) * math.sin(a), math.sin(e), math.cos(e) * math.cos(a)])
        fwd = -pos / np.linalg.norm(pos)
        right = np.cross(fwd, [0.0, 1.0, 0.0]); right /= np.linalg.norm(right)
        R = np.stack([right, np.cross(fwd, right), fwd], 1)
        M = np.eye(4); M[:3, :3] = R.T; M[:3, 3] = -R.T @ pos
        mats.append(M)
    f = (size / 2) / math.tan(math.radians(fov / 2))
    K = np.array([[f, 0, size / 2], [0, f, size / 2], [0, 0, 1]])
    if sweep is None:
        return np.array(mats, np.float32), np.tile(K, (n, 1, 1)).astype(np.float32)
    return np.array(mats, np.float32), np.tile(K, (n, 1, 1)).astype(np.float32), elevations


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('data'); ap.add_argument('out')
    ap.add_argument('--steps', type=int, default=6000); ap.add_argument('--cap', type=int, default=48000)
    ap.add_argument('--anchor-weight', type=float, default=3.0); ap.add_argument('--seed', type=int, default=0)
    ap.add_argument('--ring', type=int, default=81); ap.add_argument('--ring-elevation', type=float, default=8)
    ap.add_argument('--ring-fov', type=float, default=30); ap.add_argument('--ring-distance', type=float, default=3.0)
    ap.add_argument('--ring-sweep', help='LOW,HIGH elevations for a rising and falling ring')
    ap.add_argument('--flat', action='store_true', help='disc-shaped Gaussians for thin membranes')
    ap.add_argument('--freeze-poses', action='store_true', help='train on the given anchor cameras, without refining them')
    ap.add_argument('--pose-lr', type=float, default=1e-3)
    ap.add_argument('--opacity-reg', type=float, default=0.01); ap.add_argument('--scale-reg', type=float, default=0.01)
    ap.add_argument('--firm', type=float, default=0.0); ap.add_argument('--firm-from', type=float, default=0.6)
    ap.add_argument('--pin', action='store_true', help='fixed opaque discs on the init surface; learn colour and size only')
    ap.add_argument('--pin-opacity', type=float, default=0.95)
    ap.add_argument('--shell', help='unsigned distance grid of a scaffold surface; Gaussian centres stay within --shell-reach of it')
    ap.add_argument('--shell-reach', type=float, default=0.015)
    a = ap.parse_args()
    sweep = [float(v) for v in a.ring_sweep.split(',')] if a.ring_sweep else None
    a.flat = a.flat or a.pin
    os.makedirs(a.out, exist_ok=True)
    if a.ring:
        os.makedirs(a.out + '/ring/control', exist_ok=True); os.makedirs(a.out + '/ring/alpha', exist_ok=True)
    torch.manual_seed(a.seed); np.random.seed(a.seed); random.seed(a.seed)
    meta = json.load(open(a.data + '/views.json'))
    # Refuse fields this trainer does not know rather than ignore them (2026-11-15: a stale lab copy that predated 'weight' silently
    # trained every weight-0 view, so single-clip fits were joint fits). Add a field here when the trainer learns to read it.
    known = {'index', 'name', 'role', 'cloud_iou', 'background', 'source', 'azimuth', 'elevation', 'weight', 'loss_scale'}
    unknown = sorted(set().union(*(v.keys() for v in meta['views'])) - known)
    if unknown:
        raise SystemExit(f'train_loop_splat.py: views.json carries per-view fields this trainer does not read: {unknown} (a stale copy, '
                         f'or a new field the trainer must learn first) - refusing to ignore them')
    roles = [v['role'] for v in meta['views']]
    grey = torch.tensor(np.array(meta['views'][0].get('background', [122, 122, 122])) / 255, dtype=torch.float32, device='cuda')
    cams = np.load(a.data + '/cameras.npz')
    base_view = torch.from_numpy(cams['viewmats']).float().cuda()
    base_K = torch.from_numpy(cams['Ks']).float().cuda()
    # Numeric order: with 1,000 or more images, '1000.png' sorts before '101.png' as text.
    files = sorted(os.listdir(a.data + '/images'), key=lambda f: (len(f), f))
    rgba = np.stack([np.asarray(Image.open(f'{a.data}/images/{f}').convert('RGBA'), np.uint8) for f in files])
    H, W = rgba.shape[1:3]
    images = torch.from_numpy(rgba)
    # Per-view ignore masks (DATA_DIR/ignore/NNN.png, 255 = not judged; loop_dataset.py from repaint_match.py): inside one, the
    # render is replaced by the target for both the colour and the alpha loss, as --pin does outside the subject.
    ignore = {}
    if os.path.isdir(a.data + '/ignore'):
        for f in os.listdir(a.data + '/ignore'):
            if f in files:
                ignore[files.index(f)] = torch.from_numpy(np.asarray(Image.open(f'{a.data}/ignore/{f}').convert('L')) > 127)
        print(f'ignore masks on {len(ignore)} views', flush=True)
    n = len(files)
    anchors = [i for i, r in enumerate(roles) if r == 'anchor']
    rings = [i for i, r in enumerate(roles) if r == 'ring']
    held = [i for i, r in enumerate(roles) if r == 'heldout']
    exact = [i for i, r in enumerate(roles) if r == 'exact']
    weights = [a.anchor_weight if roles[i] == 'anchor' else 1.0 for i in anchors + rings]
    # Optional per-view fields in views.json (2026-11-14, joint fits of clips made at different sizes): 'weight' multiplies a view's
    # sampling weight; 'loss_scale' < 1 compares render and target after average-pooling to that fraction of the size, so a frame that
    # was upscaled from a smaller clip supervises at its own resolution and does not ask for detail it does not have. Absent = unchanged.
    vweight = [float(meta['views'][i].get('weight', 1.0)) if i < len(meta['views']) else 1.0 for i in range(n)]
    vscale = [float(meta['views'][i].get('loss_scale', 1.0)) if i < len(meta['views']) else 1.0 for i in range(n)]
    weights = [w * vweight[i] for i, w in zip(anchors + rings, weights)]
    if any(x != 1.0 for x in vweight) or any(x != 1.0 for x in vscale):
        print(f'per-view weights {sorted(set(vweight))} loss scales {sorted(set(vscale))}', flush=True)
    # Rings are many frames; give anchors ANCHOR_WEIGHT x the share of an average ring frame overall.
    if rings:
        weights = [w * len(rings) / max(len(anchors), 1) / 4 if roles[i] == 'anchor' else w for i, w in zip(anchors + rings, weights)]
    train = anchors + rings

    init = np.load(a.data + '/init_points.npz')
    p = torch.from_numpy(init['points']).float().cuda(); col = torch.from_numpy(init['colors']).float().cuda()
    N = p.shape[0]; spacing = float(init['spacing'])
    params = torch.nn.ParameterDict({
        'means': torch.nn.Parameter(p.clone()),
        'scales': torch.nn.Parameter(torch.log(torch.full((N, 3), spacing * 0.6, device='cuda'))),
        'quats': torch.nn.Parameter(torch.cat([torch.ones(N, 1, device='cuda'), torch.zeros(N, 3, device='cuda')], 1)),
        'opacities': torch.nn.Parameter(torch.logit(torch.full((N,), 0.6, device='cuda'))),
        'sh0': torch.nn.Parameter(((col - 0.5) / C0).unsqueeze(1)),
    })
    pose = torch.nn.ParameterDict({
        'rot': torch.nn.Parameter(torch.zeros(n, 3, device='cuda')),
        'trans': torch.nn.Parameter(torch.zeros(n, 3, device='cuda')),
        'logf': torch.nn.Parameter(torch.zeros(n, 1, device='cuda')),
    })
    movable = torch.zeros(n, 1, device='cuda')
    for i in anchors + held:
        if i != 0 and not (a.freeze_poses and roles[i] == 'anchor'):
            movable[i] = 1
    scene_scale = float((p.max(0).values - p.min(0).values).norm() / 2)
    lrs = {'means': 1.6e-4 * scene_scale, 'scales': 5e-3, 'quats': 1e-3, 'opacities': 5e-2, 'sh0': 2.5e-3}
    opts = {k: torch.optim.Adam([{'params': [params[k]], 'name': k}], lr=lrs[k], eps=1e-15) for k in params}
    pose_opt = torch.optim.Adam(pose.parameters(), lr=a.pose_lr)
    mean_sched = torch.optim.lr_scheduler.ExponentialLR(opts['means'], gamma=0.01 ** (1.0 / a.steps))
    strategy = MCMCStrategy(cap_max=a.cap, refine_start_iter=300, refine_stop_iter=int(a.steps * 0.8), refine_every=100)
    strategy.check_sanity(params, opts)
    state = strategy.initialize_state()
    if a.pin:
        with torch.no_grad():
            params['quats'].copy_(torch.from_numpy(init['quats']).float().cuda())
            params['scales'].fill_(math.log(0.9 * spacing))
            params['opacities'].fill_(math.log(a.pin_opacity / (1 - a.pin_opacity)))
        for k in ('means', 'quats', 'opacities'):
            params[k].requires_grad_(False)
        seen = torch.zeros(N, device='cuda')

    shell = None
    if a.shell:
        sh = np.load(a.shell)
        dist = torch.from_numpy(sh['dist'].astype(np.float32)).cuda()                       # [z, y, x]
        gz, gy, gx = torch.gradient(dist, spacing=float(sh['voxel']))
        grad = F.normalize(torch.stack([gx, gy, gz]), dim=0)
        shell = (torch.cat([dist[None], grad])[None], torch.from_numpy(sh['origin']).float().cuda(), float(sh['voxel']),
                 torch.tensor(dist.shape[::-1], device='cuda').float())
        print(f'shell {tuple(dist.shape)} voxel {float(sh["voxel"]):.4f} reach {a.shell_reach}', flush=True)

    def shell_at(pts):
        vol, origin, voxel, dims = shell
        g = ((pts - origin) / voxel / (dims - 1) * 2 - 1).clamp(-1, 1)
        v = F.grid_sample(vol, g[None, None, None], mode='bilinear', padding_mode='border', align_corners=True)[0, :, 0, 0]
        return v[0], v[1:].T

    def hold_in_shell():
        with torch.no_grad():
            d, g = shell_at(params['means'])
            params['means'].sub_(g * (d - a.shell_reach).clamp(min=0)[:, None])

    def camera(i):
        R = rodrigues(pose['rot'][i:i + 1] * movable[i] + 1e-9)
        delta = torch.eye(4, device='cuda')[None].repeat(1, 1, 1)
        delta[:, :3, :3] = R; delta[:, :3, 3] = pose['trans'][i:i + 1] * movable[i]
        view = delta @ base_view[i:i + 1]
        K = base_K[i:i + 1].clone()
        s = torch.exp(pose['logf'][i] * movable[i])
        K = torch.cat([K[:, :2, :2] * s, K[:, :2, 2:]], 2)
        K = torch.cat([K, base_K[i:i + 1, 2:]], 1)
        return view, K

    def scales():
        sc = torch.exp(params['scales'])
        return torch.cat([sc[:, :2], torch.full_like(sc[:, 2:], 1e-4)], 1) if a.flat else sc

    def render(view, K, bg, mode='RGB'):
        out = rasterization(params['means'], F.normalize(params['quats'], dim=-1), scales(),
                            torch.sigmoid(params['opacities']), params['sh0'], view, K, W, H,
                            sh_degree=0, packed=False, near_plane=0.01, far_plane=50.0, backgrounds=bg[None], render_mode=mode)
        return out[0][0].permute(2, 0, 1), out[1][0].permute(2, 0, 1), out[2]

    def target(i, bg):
        t = images[i].cuda().float().div(255).permute(2, 0, 1)
        return t[:3] * t[3:] + bg[:, None, None] * (1 - t[3:]), t[3:]

    t0 = time.time()
    for step in range(a.steps):
        i = random.choices(train, weights)[0]
        bg = torch.rand(3, device='cuda')
        view, K = camera(i)
        r, alpha, info = render(view, K, bg)
        t, m = target(i, bg)
        if a.pin:
            r = r * m + bg[:, None, None] * (1 - m)   # what lies outside the picture's subject is not judged
        if i in ignore:
            keep = (~ignore[i]).cuda().float()[None]
            r = r * keep + t * (1 - keep); alpha = alpha * keep + m * (1 - keep)
        if vscale[i] < 1.0:   # judge at the frame's own resolution (see 'loss_scale' above)
            k = max(1, round(1 / vscale[i]))
            r, t, alpha, m = (F.avg_pool2d(x[None], k)[0] for x in (r, t, alpha, m))
        loss = 0.8 * (r - t).abs().mean() + 0.2 * (1 - ssim(r[None], t[None])) + 0.1 * (alpha - m).abs().mean()
        opacity = torch.sigmoid(params['opacities'])
        loss = loss + a.opacity_reg * opacity.mean() + a.scale_reg * torch.exp(params['scales']).mean()
        if a.firm and step >= a.firm_from * a.steps:
            loss = loss + a.firm * (opacity * (1 - opacity)).mean()
        if not a.pin:
            strategy.step_pre_backward(params, opts, state, step, info)
        loss.backward()
        if a.pin:
            seen += params['sh0'].grad.abs().sum((1, 2)) > 1e-7   # a disc behind others still gets a vanishing gradient
        for k, o in opts.items():
            if not a.pin or k in ('sh0', 'scales'):
                o.step()
            o.zero_grad(set_to_none=True)
        if a.pin:   # a disc may grow to close gaps, not to paint beyond the surface
            params['scales'].data.clamp_(max=math.log(1.6 * spacing))
        if step > 200:
            pose_opt.step()
        pose_opt.zero_grad(set_to_none=True)
        mean_sched.step()
        if not a.pin:
            strategy.step_post_backward(params, opts, state, step, info, lr=mean_sched.get_last_lr()[0])
        if shell is not None:
            hold_in_shell()
        if step % 1000 == 0:
            print(f'step {step} loss {loss.item():.4f} gs {len(params["means"])} {time.time() - t0:.0f}s', flush=True)

    if shell is not None:
        for _ in range(4):
            hold_in_shell()
    # Held-out: fit only its pose to the frozen splat, then score.
    for k in params:
        params[k].requires_grad_(False)
    opacity = torch.sigmoid(params['opacities'])
    score = {'gaussians': len(params['means']), 'seconds': round(time.time() - t0, 1),
             'opacity': {'median': round(float(opacity.median()), 4), 'over_0.5': int((opacity > 0.5).sum()),
                         'over_0.02': int((opacity > 0.02).sum())}}
    if shell is not None:
        d = shell_at(params['means'])[0]
        score['shell'] = {'reach': a.shell_reach, 'median': round(float(d.median()), 5), 'p99': round(float(d.quantile(0.99)), 5), 'max': round(float(d.max()), 5)}
    for i in held:
        fit = torch.optim.Adam([pose['rot'], pose['trans'], pose['logf']], lr=2e-3)
        for _ in range(400):
            view, K = camera(i)
            bg = torch.rand(3, device='cuda')
            r, alpha, _ = render(view, K, bg); t, m = target(i, bg)
            l = (r - t).abs().mean() + 0.5 * (alpha - m).abs().mean()
            l.backward(); fit.step(); fit.zero_grad(set_to_none=True)
    with torch.no_grad():
        def psnr(x, y, mask=None):
            e = (x - y) ** 2
            e = e.mean() if mask is None else (e * mask).sum() / (mask.sum() * 3).clamp_min(1)
            return round(float(-10 * math.log10(float(e) + 1e-12)), 3)
        rows = []
        shown = anchors + held + exact
        if len(shown) > 24:  # keep the sheet readable: a sample of each role
            shown = anchors[::max(1, len(anchors) // 8)] + held + exact[::max(1, len(exact) // 8)]
        shown = set(shown)
        for i in anchors + held + exact:
            view, K = camera(i)
            r, alpha, _ = render(view, K, grey); t, m = target(i, grey)
            union = ((m + alpha) > 0.1).float()
            entry = {'name': meta['views'][i]['name'], 'role': roles[i], 'psnr': psnr(r, t), 'subject_psnr': psnr(r, t, union)}
            score.setdefault('views', []).append(entry)
            if i in shown:
                rows.append(torch.cat([t, r], 2))
        Image.fromarray(torch.cat(rows, 1).permute(1, 2, 0).clamp(0, 1).mul(255).byte().cpu().numpy()).resize((W // 2, H * len(rows) // 2)).save(a.out + '/anchors.png')
        for role in ('anchor', 'heldout', 'exact'):
            vals = [v for v in score['views'] if v['role'] == role]
            if vals:
                score[role] = {'psnr': round(float(np.mean([v['psnr'] for v in vals])), 3),
                               'subject_psnr': round(float(np.mean([v['subject_psnr'] for v in vals])), 3)}
        print('SCORE', json.dumps({k: score[k] for k in ('anchor', 'heldout', 'exact', 'opacity') if k in score}), flush=True)
        json.dump(score, open(a.out + '/score.json', 'w'), indent=1)
        # Refined anchor cameras, for the next round's dataset.
        vm, ks = [], []
        for i in range(n):
            view, K = camera(i); vm.append(view[0].cpu().numpy()); ks.append(K[0].cpu().numpy())
        np.savez_compressed(a.out + '/poses.npz', viewmats=np.array(vm), Ks=np.array(ks))
        # Ring for the next repaint.
        if a.ring:
            rings = ring_cameras(a.ring, a.ring_elevation, a.ring_distance, a.ring_fov, W, sweep)
            rv, rk = rings[0], rings[1]
            for j in range(a.ring):
                r, alpha, _ = render(torch.from_numpy(rv[j:j + 1]).cuda(), torch.from_numpy(rk[j:j + 1]).cuda(), grey)
                Image.fromarray(r.permute(1, 2, 0).clamp(0, 1).mul(255).byte().cpu().numpy()).save(f'{a.out}/ring/control/{j:03d}.png')
                Image.fromarray(alpha[0].clamp(0, 1).mul(255).byte().cpu().numpy()).save(f'{a.out}/ring/alpha/{j:03d}.png')
            np.savez_compressed(a.out + '/ring/cameras.npz', viewmats=rv, Ks=rk)
            json.dump({'size': [W, H], 'frames': a.ring, 'elevation': a.ring_elevation if sweep is None else rings[2],
                       'fov_deg': a.ring_fov, 'distance': a.ring_distance, 'flat': a.flat},
                      open(a.out + '/ring/ring.json', 'w'), indent=1)
        rgb = (params['sh0'][:, 0] * C0 + 0.5).clamp(0, 1)
        np.savez_compressed(a.out + '/splat.npz', means=params['means'].cpu().numpy(), scales=torch.log(scales()).cpu().numpy(),
                            quats=F.normalize(params['quats'], dim=-1).cpu().numpy(),
                            opacities=params['opacities'].cpu().numpy(), rgb=rgb.cpu().numpy(),
                            **({'seen': seen.cpu().numpy()} if a.pin else {}))
    print('saved', a.out, flush=True)


if __name__ == '__main__':
    main()
