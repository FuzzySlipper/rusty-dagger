#!/usr/bin/env python3
"""Focus the native Dagger product and send one physical gameplay key."""

import ctypes
import sys
import time

key = sys.argv[1].lower() if len(sys.argv) > 1 else "w"
if key not in {"w", "a", "s", "d", "l"}:
    raise SystemExit(f"unsupported Dagger key: {key}")

Display = ctypes.c_void_p
Window = ctypes.c_ulong
x11 = ctypes.CDLL("libX11.so")
xtst = ctypes.CDLL("libXtst.so")
x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
x11.XOpenDisplay.restype = Display
x11.XDefaultRootWindow.argtypes = [Display]
x11.XDefaultRootWindow.restype = Window
x11.XQueryTree.argtypes = [
    Display,
    Window,
    ctypes.POINTER(Window),
    ctypes.POINTER(Window),
    ctypes.POINTER(ctypes.POINTER(Window)),
    ctypes.POINTER(ctypes.c_uint),
]
x11.XQueryTree.restype = ctypes.c_int
x11.XFetchName.argtypes = [Display, Window, ctypes.POINTER(ctypes.c_char_p)]
x11.XFetchName.restype = ctypes.c_int
x11.XFree.argtypes = [ctypes.c_void_p]
x11.XMapRaised.argtypes = [Display, Window]
x11.XSetInputFocus.argtypes = [Display, Window, ctypes.c_int, ctypes.c_ulong]
x11.XDefaultScreen.argtypes = [Display]
x11.XDefaultScreen.restype = ctypes.c_int
x11.XGetGeometry.argtypes = [
    Display, Window, ctypes.POINTER(Window), ctypes.POINTER(ctypes.c_int),
    ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_uint),
    ctypes.POINTER(ctypes.c_uint), ctypes.POINTER(ctypes.c_uint),
    ctypes.POINTER(ctypes.c_uint),
]
x11.XGetGeometry.restype = ctypes.c_int
x11.XTranslateCoordinates.argtypes = [
    Display, Window, Window, ctypes.c_int, ctypes.c_int,
    ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int),
    ctypes.POINTER(Window),
]
x11.XTranslateCoordinates.restype = ctypes.c_int
x11.XKeysymToKeycode.argtypes = [Display, ctypes.c_ulong]
x11.XKeysymToKeycode.restype = ctypes.c_uint
x11.XSync.argtypes = [Display, ctypes.c_int]
x11.XCloseDisplay.argtypes = [Display]
xtst.XTestFakeKeyEvent.argtypes = [Display, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
xtst.XTestFakeKeyEvent.restype = ctypes.c_int
xtst.XTestFakeMotionEvent.argtypes = [
    Display, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_ulong,
]
xtst.XTestFakeMotionEvent.restype = ctypes.c_int
xtst.XTestFakeButtonEvent.argtypes = [Display, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
xtst.XTestFakeButtonEvent.restype = ctypes.c_int


def window_name(display, window):
    name = ctypes.c_char_p()
    if not x11.XFetchName(display, window, ctypes.byref(name)) or not name.value:
        return ""
    try:
        return name.value.decode("utf-8", errors="replace")
    finally:
        x11.XFree(name)


def child_windows(display, window):
    root = Window()
    parent = Window()
    children = ctypes.POINTER(Window)()
    count = ctypes.c_uint()
    if not x11.XQueryTree(
        display,
        window,
        ctypes.byref(root),
        ctypes.byref(parent),
        ctypes.byref(children),
        ctypes.byref(count),
    ):
        return []
    try:
        return [children[index] for index in range(count.value)]
    finally:
        if children:
            x11.XFree(children)


def find_dagger_window(display):
    pending = [x11.XDefaultRootWindow(display)]
    while pending:
        window = pending.pop()
        if "Privateer's Hold" in window_name(display, window):
            return window
        pending.extend(child_windows(display, window))
    return None


display = x11.XOpenDisplay(None)
if not display:
    raise SystemExit("XOpenDisplay failed")
try:
    deadline = time.monotonic() + 10
    window = None
    while window is None and time.monotonic() < deadline:
        window = find_dagger_window(display)
        if window is None:
            time.sleep(0.05)
    if window is None:
        raise SystemExit("Dagger native window not found")
    x11.XMapRaised(display, window)
    x11.XSetInputFocus(display, window, 2, 0)
    x11.XSync(display, 0)
    geometry_root = Window()
    x = ctypes.c_int()
    y = ctypes.c_int()
    width = ctypes.c_uint()
    height = ctypes.c_uint()
    border = ctypes.c_uint()
    depth = ctypes.c_uint()
    if not x11.XGetGeometry(
        display, window, ctypes.byref(geometry_root), ctypes.byref(x), ctypes.byref(y),
        ctypes.byref(width), ctypes.byref(height), ctypes.byref(border), ctypes.byref(depth),
    ):
        raise SystemExit("XGetGeometry failed")
    root_x = ctypes.c_int()
    root_y = ctypes.c_int()
    child = Window()
    root = x11.XDefaultRootWindow(display)
    if not x11.XTranslateCoordinates(
        display, window, root, int(width.value // 2), int(height.value // 2),
        ctypes.byref(root_x), ctypes.byref(root_y), ctypes.byref(child),
    ):
        raise SystemExit("XTranslateCoordinates failed")
    if not xtst.XTestFakeMotionEvent(
        display, x11.XDefaultScreen(display), root_x.value, root_y.value, 0,
    ):
        raise SystemExit("physical pointer move failed")
    if not xtst.XTestFakeButtonEvent(display, 1, 1, 0):
        raise SystemExit("physical pointer-down failed")
    x11.XSync(display, 0)
    time.sleep(0.15)
    if not xtst.XTestFakeButtonEvent(display, 1, 0, 0):
        raise SystemExit("physical pointer-up failed")
    x11.XSync(display, 0)
    time.sleep(0.20)
    keycode = x11.XKeysymToKeycode(display, ord(key))
    if not keycode:
        raise SystemExit(f"XKeysymToKeycode({key}) failed")
    if not xtst.XTestFakeKeyEvent(display, keycode, 1, 0):
        raise SystemExit(f"physical {key.upper()} key-down injection failed")
    x11.XSync(display, 0)
    # Software-rendered CI can take several input polling intervals to return
    # one webview readback. Keep this a real physical hold long enough for the
    # native host to observe it under load.
    time.sleep(0.75)
    if not xtst.XTestFakeKeyEvent(display, keycode, 0, 0):
        raise SystemExit(f"physical {key.upper()} key-up injection failed")
    x11.XSync(display, 0)
    # Do not let the next Lab command race ahead of Engine's asynchronous
    # physical-input readback for this falling edge. In particular, a reset
    # followed by a late movement readout would immediately leave the spawn.
    time.sleep(0.75)
    marker = "DAGGER_LAB_PHYSICAL_OPEN_OK" if key == "l" else "DAGGER_LAB_PHYSICAL_MOVE_OK"
    print(f"{marker} key={key.upper()} window={window}")
finally:
    x11.XCloseDisplay(display)
