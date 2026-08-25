# Dagger UI elements

This directory is the Dagger-local portability boundary for reusable DOM UI
elements. It intentionally stays downstream of `rusty-engine`: the elements
present application-host UI and do not mount a renderer, read Engine state, or
evaluate gameplay meaning.

## Transient messages

`TransientMessageOverlayService.spawn` accepts a small generic payload:

```ts
const message = overlay.spawn({
  text: 'Drop rejected: the slot is occupied.',
  severity: 'error',
  position: { x: 512, y: 320 },
  lifetime: 1_400,
});
```

Mount one `<dagger-transient-message-overlay />` alongside the product's other
DOM overlays and import `TransientMessageOverlayComponent` in the Angular
standalone component that owns that template. The service can be called before
the component mounts; active records are replayed when its host attaches.

### Coordinates and clipping

`position.x/y` are CSS pixels relative to the overlay host's top-left padding
box, not viewport coordinates and not renderer/world coordinates. Convert
viewport points with the host's `getBoundingClientRect()` before calling
`spawn`. The host fills its containing product surface and clips overflow, so
off-edge messages are intentionally clipped rather than clamped into a
different semantic location. Messages at the same point receive a deterministic
six-level upward stack offset; positions beyond that depth overlap by policy.

### Lifecycle and performance

The default cap is 128 active messages. Spawning at the cap evicts the oldest
record, keeping DOM work bounded during bursts. IDs are instance-local,
monotonic (`dagger-transient-000001`, ...), and expiries are computed once from
a monotonic clock. One shared timer wakes at the earliest expiry and removes
all records due at that time in one batch. CSS `transform` and `opacity`
animation runs on the compositor; the dynamic nodes are created imperatively
so Angular change detection is not involved per frame.

Lifetimes default to 1.5 seconds and are bounded to 100 ms–10 seconds. These
limits and the active cap can be changed through
`TRANSIENT_MESSAGE_OVERLAY_CONFIG` or a controller's test options.

### Accessibility and tests

Announcing messages use `role="status"`, `aria-live="polite"`, and
`aria-atomic="true"`. Pass `announce: false` for decorative or high-volume
visual-only messages; those nodes are marked `aria-hidden`. Text is inserted as
text content, not HTML. `prefers-reduced-motion: reduce` disables the float/fade
while preserving deterministic expiry.

`TransientMessageOverlayController` is the test seam. Inject a fake clock and
scheduler, call `flushExpired(atMs)`, inspect `snapshots()`,
`debugSnapshot()`, and `elementForTest(id)`, and assert the active cap and
batched expiry count without needing Angular or a renderer.

