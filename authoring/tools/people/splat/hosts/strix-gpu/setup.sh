#!/bin/bash
# setup.sh HOST: build the splat-training env on a Strix Halo (gfx1151) in ~/.local/state/asset-gpu: AMD's ROCm 10.1 torch (the
# Halo ComfyUI's wheels), the ROCm SDK devel package, and upstream gsplat 1.5.3 (the GPU lab's version) with
# gsplat-1.5.3-rocm-gfx1151.patch: C++20 and no nvcc flags for hipcc, glm included from ~/.local/include so hipify leaves it,
# a shuffle-xor stand-in for cooperative_groups::reduce, one-thread groups for labeled_partition (per-thread atomics),
# hipcub as cub, const void* kernels for hipFuncSetAttribute, std::array [] for .at() in device code.
# Checked 2026-11-18 on den-m5: forward+backward match the 5090 to ~1e-5 relative; training ~5x slower than the 5090.
# AMD's own fork (github.com/ROCm/gsplat release/1.5.3b2) assumes 64-lane waves and does not run on RDNA (wave32).
set -euo pipefail
H=$1; D=$(cd "$(dirname "$0")" && pwd)
scp -q "$D/gsplat-1.5.3-rocm-gfx1151.patch" "$H:/tmp/gsplat-rocm.patch"
ssh -o BatchMode=yes "$H" 'set -e; S=~/.local/state/asset-gpu; mkdir -p $S ~/.local/include; cd $S
[ -d venv ] || ~/.local/bin/uv venv -q -p 3.13 venv; . venv/bin/activate
uv pip install -q --index-url https://stable.repo.amd.com/rocm/whl-next/ "torch[device-gfx1151]==2.14.0+rocm10.1.0" "rocm[devel]==10.1.0"
uv pip install -q numpy scipy pillow ninja setuptools wheel jaxtyping rich
[ -d gsplat ] || git clone -q --recursive --branch v1.5.3 https://github.com/nerfstudio-project/gsplat.git
cd gsplat; git checkout -q -- . ; git apply /tmp/gsplat-rocm.patch; [ -d ~/.local/include/glm ] || cp -r gsplat/cuda/csrc/third_party/glm/glm ~/.local/include/glm
R=$(rocm-sdk path --root); export ROCM_HOME=$R ROCM_PATH=$R HIP_PATH=$R PATH=$R/bin:$PATH PYTORCH_ROCM_ARCH=gfx1151 MAX_JOBS=24
rm -rf build gsplat/hip; uv pip install -q --no-build-isolation . && python -c "import gsplat, torch; print(\"gsplat\", gsplat.__version__, torch.cuda.get_device_name(0))"'
