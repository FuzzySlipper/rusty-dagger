#!/usr/bin/env python3
"""Inject one real X11 Return key cycle into the focused Engine webview."""

import ctypes
import time


Display = ctypes.c_void_p
x11 = ctypes.CDLL("libX11.so")
xtst = ctypes.CDLL("libXtst.so")
x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
x11.XOpenDisplay.restype = Display
x11.XKeysymToKeycode.argtypes = [Display, ctypes.c_ulong]
x11.XKeysymToKeycode.restype = ctypes.c_uint
x11.XFlush.argtypes = [Display]
x11.XCloseDisplay.argtypes = [Display]
xtst.XTestFakeKeyEvent.argtypes = [Display, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
xtst.XTestFakeKeyEvent.restype = ctypes.c_int

display = x11.XOpenDisplay(None)
if not display:
    raise SystemExit("XOpenDisplay failed")
try:
    keycode = x11.XKeysymToKeycode(display, 0xFF0D)  # XK_Return
    if not keycode:
        raise SystemExit("XKeysymToKeycode(XK_Return) failed")
    if not xtst.XTestFakeKeyEvent(display, keycode, 1, 0):
        raise SystemExit("XTest key-down injection failed")
    x11.XFlush(display)
    time.sleep(0.4)
    if not xtst.XTestFakeKeyEvent(display, keycode, 0, 0):
        raise SystemExit("XTest key-up injection failed")
    x11.XFlush(display)
    time.sleep(0.2)
finally:
    x11.XCloseDisplay(display)
