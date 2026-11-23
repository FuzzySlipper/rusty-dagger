#!/usr/bin/env python3
"""queue.py: the lab ComfyUI queue as 'running N pending M' (or 'unreachable')."""
import json, urllib.request
try:
    q = json.loads(urllib.request.urlopen('http://192.168.1.20:8188/queue', timeout=10).read())
    print('running', len(q['queue_running']), 'pending', len(q['queue_pending']))
except Exception as e:
    print('unreachable', type(e).__name__)
