#!/bin/bash
# setup.sh HOST: build the splat-training env on a rented RunPod pod in /workspace/asset-gpu (the pod's volume, so a restarted
# pod keeps it): the newest CUDA 12.8 PyTorch (the lab has 2.14.1+cu130; matching it would need CUDA 13 nvcc for the gsplat build), upstream
# gsplat 1.5.3 (the lab's and the Halos' version) built for Ampere and Ada (sm_86 3090/A5000, sm_89 4090), and the packages the
# bake tools import. HOST is an ssh alias for the pod (docs/runpod-bake-pods.md writes one). Needs a -devel image (nvcc).
# Prepared 2026-11-20 in asset-pipeline (tools/runpod-bake) for Patch: rented 24 GB cards as extra bake hosts; first used 2026-11-22
# (npc-023's hands-ring bake on a community RTX 3090). The image's nvcc is in /usr/local/cuda/bin, off PATH. Mirrors ../strix-gpu/setup.sh.
set -euo pipefail
H=$1
ssh -o BatchMode=yes "$H" 'set -e; S=/workspace/asset-gpu; mkdir -p $S/work; cd $S; export PATH=/usr/local/cuda/bin:$PATH CUDA_HOME=/usr/local/cuda
command -v uv > /dev/null || curl -LsSf https://astral.sh/uv/install.sh | sh; export PATH=$HOME/.local/bin:$PATH UV_HTTP_TIMEOUT=600 UV_CONCURRENT_DOWNLOADS=4
[ -d venv ] || uv venv -q -p 3.12 venv; . venv/bin/activate
uv pip install -q --index-url https://download.pytorch.org/whl/cu128 torch   # the newest cu128 build: the lab runs 2.14.1+cu130, which needs CUDA 13 nvcc to build gsplat against; the image has 12.8
uv pip install -q numpy scipy pillow trimesh ninja setuptools wheel jaxtyping rich
[ -d gsplat ] || git clone -q --recursive --branch v1.5.3 https://github.com/nerfstudio-project/gsplat.git
cd gsplat; export TORCH_CUDA_ARCH_LIST="8.6;8.9" MAX_JOBS=$(nproc)
uv pip install -q --no-build-isolation . && python -c "import gsplat, torch; print(\"gsplat\", gsplat.__version__, torch.__version__, torch.cuda.get_device_name(0))"'
