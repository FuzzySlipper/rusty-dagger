#!/usr/bin/env python3
"""Focus the Dagger window and inject one real X11 Return key cycle."""

import ctypes
import time


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
x11.XGetInputFocus.argtypes = [Display, ctypes.POINTER(Window), ctypes.POINTER(ctypes.c_int)]
x11.XSync.argtypes = [Display, ctypes.c_int]
x11.XKeysymToKeycode.argtypes = [Display, ctypes.c_ulong]
x11.XKeysymToKeycode.restype = ctypes.c_uint
x11.XFlush.argtypes = [Display]
x11.XCloseDisplay.argtypes = [Display]
xtst.XTestFakeKeyEvent.argtypes = [Display, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
xtst.XTestFakeKeyEvent.restype = ctypes.c_int


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
    root = x11.XDefaultRootWindow(display)
    pending = [root]
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
    deadline = time.monotonic() + 5.0
    window = None
    while window is None and time.monotonic() < deadline:
        window = find_dagger_window(display)
        if window is None:
            time.sleep(0.05)
    if window is None:
        raise SystemExit("Dagger native window not found")
    x11.XMapRaised(display, window)
    x11.XSetInputFocus(display, window, 2, 0)  # RevertToParent, CurrentTime
    x11.XSync(display, 0)
    focused = Window()
    revert_to = ctypes.c_int()
    x11.XGetInputFocus(display, ctypes.byref(focused), ctypes.byref(revert_to))
    if focused.value != window:
        raise SystemExit(
            f"Dagger native window did not receive focus: wanted {window}, got {focused.value}"
        )
    print(f"DAGGER_X11_FOCUS_OK window={window}")
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
