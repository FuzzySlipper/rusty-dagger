import type { ComponentChildren } from 'preact';
import { useEffect, useRef } from 'preact/hooks';

export interface ModalProps {
  open: boolean;
  title: string;
  subtitle?: string;
  fullScreen?: boolean;
  headerActions?: ComponentChildren;
  onClose: () => void;
  children: ComponentChildren;
}

/** Accessible native <dialog> modal reusable for inspectors, visual pickers, and future 3D panels. */
export function Modal({
  open,
  title,
  subtitle,
  fullScreen = false,
  headerActions,
  onClose,
  children,
}: ModalProps) {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  function handleCancel(event: Event) {
    event.preventDefault();
    onClose();
  }

  function handleBackdropClick(event: MouseEvent) {
    if (event.target === ref.current) {
      onClose();
    }
  }

  return (
    <dialog
      ref={ref}
      class={fullScreen ? 'cms-modal cms-modal-full' : 'cms-modal'}
      aria-labelledby="cms-modal-title"
      onCancel={handleCancel}
      onClick={handleBackdropClick}
    >
      {open ? (
        <div class="cms-modal-frame">
          <header class="inspection-bar">
            <div>
              <h2 id="cms-modal-title">{title}</h2>
              {subtitle ? <span class="muted">{subtitle}</span> : null}
            </div>
            <div class="actions">
              {headerActions}
              <button type="button" data-modal-close onClick={onClose}>
                Close
              </button>
            </div>
          </header>
          <div class="cms-modal-body">{children}</div>
        </div>
      ) : null}
    </dialog>
  );
}
