#!/bin/bash
# vram.sh LOG: sample the lab 5090's used memory (MiB) and the container's used RAM (GiB) every 10 s until killed.
while true; do
  echo "$(date +%s) $(~/server-access/gpu-lab/connect 'gpu-lab exec nvidia-smi --query-gpu=memory.used,utilization.gpu --format=csv,noheader,nounits; free -g | awk "/Mem/{print \$3}"' 2>/dev/null | grep -v Container | tr '\n' ' ')" >> "$1"
  sleep 10
done
