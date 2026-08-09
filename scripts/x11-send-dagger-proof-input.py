#!/usr/bin/env python3
"""Focus Dagger and exercise real X11 diagnostic toggles plus Return."""

import ctypes
import sys
import time
from pathlib import Path


Display = ctypes.c_void_p
Window = ctypes.c_ulong
x11 = ctypes.CDLL("libX11.so")
xtst = ctypes.CDLL("libXtst.so")
x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
x11.XOpenDisplay.restype = Display
x11.XDefaultRootWindow.argtypes = [Display]
x11.XDefaultRootWindow.restype = Window
x11.XDefaultScreen.argtypes = [Display]
x11.XDefaultScreen.restype = ctypes.c_int
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
x11.XQueryKeymap.argtypes = [Display, ctypes.POINTER(ctypes.c_char)]
x11.XQueryKeymap.restype = ctypes.c_int
x11.XGetGeometry.argtypes = [
    Display,
    Window,
    ctypes.POINTER(Window),
    ctypes.POINTER(ctypes.c_int),
    ctypes.POINTER(ctypes.c_int),
    ctypes.POINTER(ctypes.c_uint),
    ctypes.POINTER(ctypes.c_uint),
    ctypes.POINTER(ctypes.c_uint),
    ctypes.POINTER(ctypes.c_uint),
]
x11.XGetGeometry.restype = ctypes.c_int
x11.XTranslateCoordinates.argtypes = [
    Display,
    Window,
    Window,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.POINTER(ctypes.c_int),
    ctypes.POINTER(ctypes.c_int),
    ctypes.POINTER(Window),
]
x11.XTranslateCoordinates.restype = ctypes.c_int
x11.XSync.argtypes = [Display, ctypes.c_int]
x11.XKeysymToKeycode.argtypes = [Display, ctypes.c_ulong]
x11.XKeysymToKeycode.restype = ctypes.c_uint
x11.XFlush.argtypes = [Display]
x11.XCloseDisplay.argtypes = [Display]
xtst.XTestFakeKeyEvent.argtypes = [Display, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
xtst.XTestFakeKeyEvent.restype = ctypes.c_int
xtst.XTestFakeMotionEvent.argtypes = [
    Display,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_ulong,
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
    if len(sys.argv) != 2:
        raise SystemExit("usage: x11-send-dagger-proof-input.py PROOF_LOG")
    proof_log = Path(sys.argv[1])

    def marker_count(marker):
        try:
            return proof_log.read_text(errors="replace").count(marker)
        except FileNotFoundError:
            return 0

    def wait_for_marker(marker, occurrence, timeout=60.0):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if marker_count(marker) >= occurrence:
                return
            time.sleep(0.05)
        raise SystemExit(
            f"timed out waiting for proof marker {marker!r} occurrence {occurrence}"
        )

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
    geometry_root = Window()
    window_x = ctypes.c_int()
    window_y = ctypes.c_int()
    width = ctypes.c_uint()
    height = ctypes.c_uint()
    border_width = ctypes.c_uint()
    depth = ctypes.c_uint()
    if not x11.XGetGeometry(
        display,
        window,
        ctypes.byref(geometry_root),
        ctypes.byref(window_x),
        ctypes.byref(window_y),
        ctypes.byref(width),
        ctypes.byref(height),
        ctypes.byref(border_width),
        ctypes.byref(depth),
    ):
        raise SystemExit("XGetGeometry(Dagger native window) failed")
    root_x = ctypes.c_int()
    root_y = ctypes.c_int()
    child = Window()
    root = x11.XDefaultRootWindow(display)
    if not x11.XTranslateCoordinates(
        display,
        window,
        root,
        int(width.value // 2),
        int(height.value // 2),
        ctypes.byref(root_x),
        ctypes.byref(root_y),
        ctypes.byref(child),
    ):
        raise SystemExit("XTranslateCoordinates(Dagger native window) failed")
    if not xtst.XTestFakeMotionEvent(
        display,
        x11.XDefaultScreen(display),
        root_x.value,
        root_y.value,
        0,
    ):
        raise SystemExit("XTest pointer motion failed")
    if not xtst.XTestFakeButtonEvent(display, 1, 1, 0):
        raise SystemExit("XTest pointer-down injection failed")
    x11.XSync(display, 0)
    time.sleep(0.2)
    if not xtst.XTestFakeButtonEvent(display, 1, 0, 0):
        raise SystemExit("XTest pointer-up injection failed")
    x11.XSync(display, 0)
    time.sleep(0.3)
    print(f"DAGGER_X11_SURFACE_CLICK_OK x={root_x.value} y={root_y.value}")

    def release_key(keycode, label, marker, occurrence, timeout=15.0):
        if not xtst.XTestFakeKeyEvent(display, keycode, 0, 0):
            raise SystemExit(f"XTest {label} key-up injection failed")
        x11.XSync(display, 0)
        keymap = (ctypes.c_char * 32)()
        if not x11.XQueryKeymap(display, keymap):
            raise SystemExit("XQueryKeymap failed after key release")
        server_pressed = bool(ord(keymap[keycode // 8]) & (1 << (keycode % 8)))
        if server_pressed:
            raise SystemExit(f"X11 still reports {label} pressed after key-up")
        release_focus = Window()
        release_revert = ctypes.c_int()
        x11.XGetInputFocus(
            display,
            ctypes.byref(release_focus),
            ctypes.byref(release_revert),
        )
        print(
            f"DAGGER_X11_KEY_RELEASE_INJECTED label={label} "
            f"server_pressed=false focus={release_focus.value}"
        )
        wait_for_marker(marker, occurrence, timeout)
        print(
            f"DAGGER_X11_KEY_RELEASE_OK label={label} "
            f"marker={marker} occurrence={occurrence}"
        )

    def key_cycle(keysym, code, label, marker, occurrence):
        keycode = x11.XKeysymToKeycode(display, keysym)
        if not keycode:
            raise SystemExit(f"XKeysymToKeycode({label}) failed")
        release_marker = f"DAGGER_NATIVE_INPUT_RELEASED code={code}"
        release_occurrence = marker_count(release_marker) + 1
        if not xtst.XTestFakeKeyEvent(display, keycode, 1, 0):
            raise SystemExit(f"XTest {label} key-down injection failed")
        x11.XSync(display, 0)
        wait_for_marker(marker, occurrence)
        release_key(keycode, label, release_marker, release_occurrence)

    # Turn both diagnostic families on, off, and on again. The native proof
    # observes renderer receipts for each retained transition and proves that
    # the replacement handle differs from the retired one.
    controls = [
        (ord("g"), "KeyG", "G-on", "DAGGER_DIAGNOSTIC_CONTROL kind=patrol enabled=true", 1),
        (ord("n"), "KeyN", "N-on", "DAGGER_DIAGNOSTIC_CONTROL kind=navgrid enabled=true", 1),
        (ord("g"), "KeyG", "G-off", "DAGGER_DIAGNOSTIC_CONTROL kind=patrol enabled=false", 1),
        (ord("n"), "KeyN", "N-off", "DAGGER_DIAGNOSTIC_CONTROL kind=navgrid enabled=false", 1),
        (ord("g"), "KeyG", "G-reenabled", "DAGGER_DIAGNOSTIC_CONTROL kind=patrol enabled=true", 2),
        (ord("n"), "KeyN", "N-reenabled", "DAGGER_DIAGNOSTIC_CONTROL kind=navgrid enabled=true", 2),
    ]
    for keysym, code, label, marker, occurrence in controls:
        key_cycle(keysym, code, label, marker, occurrence)
    print("DAGGER_X11_DIAGNOSTIC_TOGGLES_OK")

    # The Lab is a companion to this exact native session. Exercise the
    # discoverable product shortcut through Engine physical-input readback;
    # proof mode acknowledges the route without spawning a desktop browser.
    key_cycle(
        ord("l"),
        "KeyL",
        "L-open-lab",
        "DAGGER_LAB_OPENED",
        1,
    )

    key_cycle(
        0xFF0D,
        "Enter",
        "Return",
        "DAGGER_NATIVE_ACTION_APPLIED kind=look",
        1,
    )  # XK_Return
finally:
    x11.XCloseDisplay(display)
