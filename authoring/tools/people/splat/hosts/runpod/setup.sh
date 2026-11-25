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
CU=$(nvcc --version | sed -n "s/.*release \\([0-9]*\\)\\.\\([0-9]*\\).*/cu\\1\\2/p"); case $CU in cu12*) CU=cu128 ;; esac   # torch built for the host nvcc: RunPod images have 12.8, Colab 13.0 (npc-026, 2026-11-24)
uv pip install -q --index-url https://download.pytorch.org/whl/$CU torch
uv pip install -q numpy scipy pillow trimesh ninja setuptools wheel jaxtyping rich
[ -d gsplat ] || git clone -q --recursive --branch v1.5.3 https://github.com/nerfstudio-project/gsplat.git
cd gsplat; git checkout -q -- setup.py; [ "$CU" = cu128 ] || sed -i "s/-std=c++17/-std=c++20/" setup.py   # torch 2.14 (CUDA 13) headers need C++20 (Colab)
CAP=$(nvidia-smi --query-gpu=compute_cap --format=csv,noheader | head -1); case ";8.0;8.6;8.9;9.0;" in *";$CAP;"*) CAP= ;; *) CAP=";$CAP" ;; esac   # the card'"'"'s own too (a Colab G4 is Blackwell, 12.0; 2026-11-25)
export TORCH_CUDA_ARCH_LIST="8.0;8.6;8.9;9.0$CAP" MAX_JOBS=$(nproc)   # A100, 3090/A5000, 4090/L4, H100
uv pip install -q --no-build-isolation . && python -c "import gsplat, torch; print(\"gsplat\", gsplat.__version__, torch.__version__, torch.cuda.get_device_name(0))"'
