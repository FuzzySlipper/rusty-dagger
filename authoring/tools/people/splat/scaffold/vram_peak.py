#!/usr/bin/env python3
"""vram_peak.py SCRIPT ARGS...: run SCRIPT as __main__ and print PyTorch's peak GPU memory when it exits.

bound_run.sh puts it in front of the bound bake's and the distil's trainer when VRAM_PEAK=1 (Patch 2026-11-20: would rented
24 GB cards such as a 3090 or an A5000 take these steps?). The line is 'VRAM_PEAK SCRIPT allocated X GiB reserved Y GiB'; reserved
is what the card must hold.
"""
import atexit
import os
import runpy
import sys

import torch


def report():
    if torch.cuda.is_available():
        print(f'VRAM_PEAK {os.path.basename(sys.argv[0])} allocated {torch.cuda.max_memory_allocated() / 2**30:.2f} GiB '
              f'reserved {torch.cuda.max_memory_reserved() / 2**30:.2f} GiB', flush=True)


atexit.register(report)
sys.argv = sys.argv[1:]
sys.path.insert(0, os.path.dirname(os.path.abspath(sys.argv[0])))
runpy.run_path(sys.argv[0], run_name='__main__')
