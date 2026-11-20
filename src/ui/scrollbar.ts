/**
 * A drawn scrollbar on the right of a scrolling surface. The window's Servo draws no scrollbar of its own, so a long
 * surface (Settings) shows where it is and how much more there is only through this one.
 *
 * The track sits in the surface's right padding and follows its scroll, so it stays in view; the thumb's size is the
 * share of the content in view. The mouse drags the thumb (pointer capture, never native drag) or clicks the track to
 * page toward the click, and the wheel scrolls the surface as ever. A keyboard or pad scrolls it by moving focus, which
 * brings the focused control into view; the bar follows whatever moved the surface. It is not focusable itself, so it
 * never takes a step of keyed navigation.
 */
export interface Scrollbar {
  /** Redraw after the surface's content or size changed. */
  update(): void;
  dispose(): void;
}

/** The smallest thumb, in rem, so a very long surface keeps something to grab. */
const MIN_THUMB_REM = 2;
/** A track click pages by this share of the view. */
const PAGE_SHARE = .9;

export function mountScrollbar(scroller: HTMLElement): Scrollbar {
  const document_ = scroller.ownerDocument;
  const track = document_.createElement('div');
  track.className = 'dagger-scrollbar';
  track.setAttribute('aria-hidden', 'true');
  const thumb = document_.createElement('div');
  thumb.className = 'dagger-scrollbar-thumb';
  track.append(thumb);
  scroller.append(track);

  const rem = (): number => parseFloat(document_.defaultView?.getComputedStyle(document_.documentElement).fontSize ?? '') || 16;
  const range = (): number => Math.max(0, scroller.scrollHeight - scroller.clientHeight);
  let thumbHeight = 0;
  const update = (): void => {
    const view = scroller.clientHeight, content = scroller.scrollHeight, travel = range();
    track.hidden = travel <= 1 || view <= 0;
    if (track.hidden) return;
    thumbHeight = Math.min(view, Math.max(MIN_THUMB_REM * rem(), view * view / content));
    // The track spans the view wherever the surface has scrolled to.
    track.style.transform = `translateY(${scroller.scrollTop}px)`;
    track.style.height = `${view}px`;
    thumb.style.height = `${thumbHeight}px`;
    thumb.style.transform = `translateY(${(view - thumbHeight) * scroller.scrollTop / travel}px)`;
  };

  let grab: { pointer: number; startY: number; startScroll: number } | null = null;
  const onDown = (event: PointerEvent): void => {
    if (event.button !== 0 || track.hidden) return;
    event.preventDefault();
    event.stopPropagation();
    if (event.target === thumb) {
      grab = { pointer: event.pointerId, startY: event.clientY, startScroll: scroller.scrollTop };
      thumb.setPointerCapture?.(event.pointerId);
      thumb.classList.add('is-dragging');
      return;
    }
    // A click on the track pages toward it.
    const at = thumb.getBoundingClientRect();
    const direction = event.clientY < at.top ? -1 : event.clientY > at.bottom ? 1 : 0;
    scroller.scrollTop += direction * scroller.clientHeight * PAGE_SHARE;
    update();
  };
  const onMove = (event: PointerEvent): void => {
    if (grab === null || event.pointerId !== grab.pointer) return;
    const free = scroller.clientHeight - thumbHeight;
    if (free <= 0) return;
    scroller.scrollTop = grab.startScroll + (event.clientY - grab.startY) * range() / free;
    update();
  };
  const onUp = (event: PointerEvent): void => {
    if (grab === null || event.pointerId !== grab.pointer) return;
    grab = null;
    thumb.classList.remove('is-dragging');
  };
  track.addEventListener('pointerdown', onDown);
  thumb.addEventListener('pointermove', onMove);
  thumb.addEventListener('pointerup', onUp);
  thumb.addEventListener('pointercancel', onUp);
  thumb.addEventListener('lostpointercapture', onUp);
  scroller.addEventListener('scroll', update, { passive: true });
  const Observer = document_.defaultView?.ResizeObserver;
  const resized = Observer ? new Observer(update) : null;
  resized?.observe(scroller);
  update();

  return {
    update,
    dispose() {
      scroller.removeEventListener('scroll', update);
      resized?.disconnect();
      track.remove();
    },
  };
}
