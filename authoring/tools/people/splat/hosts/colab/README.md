# Google Colab runtimes as splat-bake GPU hosts (first used 2026-11-24, npc-026)

Patch has Colab compute units (1,000 on 2026-11-24) that were otherwise unused. A Colab runtime takes the same gsplat
steps a RunPod pod does: round 1, the bound bake, the distil, the clip and idle renders. ComfyUI work stays local: the
clay edits, the H3 orbit clip and the VACE rings run on the 5090 (Patch 2026-11-24: "get all that stuff done local and then
just use colab for the big splat bakes"). Camera solves and round 0 also run on the 5090.

## Pieces

- **CLI:** the official `colab` CLI (github.com/googlecolab/google-colab-cli), installed on den-agents in its own venv
  (`~/.local/share/colab-cli`, linked as `~/.local/bin/colab`). Patch signed it in to the Google account that holds the units.
  `colab usage` shows the balance and the current rate per hour.
- **SSH:** `colab ssh --proxy-mode` is an OpenSSH ProxyCommand, so a Colab runtime is an ssh alias like a Halo or a pod.
  `~/.ssh/config` on den-agents:

  ```
  Host colab-bake
    ProxyCommand /home/agent/.local/bin/colab ssh --proxy-mode -s bake --gpu L4 -i /home/agent/.ssh/colab_ed25519.pub
    IdentityFile /home/agent/.ssh/colab_ed25519
    IdentitiesOnly yes
    User root
    StrictHostKeyChecking no
    UserKnownHostsFile /dev/null
    ControlMaster auto
    ControlPath ~/.ssh/cm-colab-%r@%h
    ControlPersist 30m
    ServerAliveInterval 30
    LogLevel ERROR
  ```

  The first connection **creates** the runtime (`-s bake`, an L4 here), and units are charged from then until
  `colab stop -s bake`. A runtime allows only a few concurrent SSH connections, so the alias multiplexes one
  (ControlMaster). Change `--gpu` (T4, L4, G4, A100, H100) for another card.
- **Environment:** `hosts/runpod/setup.sh colab-bake` builds it in `/workspace/asset-gpu`, as on a pod. `bake.sh` and
  `bound_run.sh` treat `GPU=colab-*` like `runpod-*`, and static-npc takes `npc.json "gpu_host": "colab-bake"`.

## Per-character order

1. Locally: `static-npc keys/clay/mesh/rig/guide/clip`, then `bake` (to the round-1 data) with `gpu_host` gpu-lab.
2. Switch `gpu_host` to `colab-bake`, run `hosts/runpod/setup.sh colab-bake`, then `r1`, `bound`, `graft`, `preview`.
3. `colab stop -s bake`, then `publish`.

## Measured

See the table in asset-pipeline `docs/runpod-bake-pods.md` (RunPod and Colab side by side).
