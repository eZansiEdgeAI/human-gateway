import type { DeliveryState } from '../types/protocol'
import { DELIVERY_STATE_META } from '../lib/delivery'

/**
 * Delivery-status badge (PWA-FR-05, ACC-03).
 *
 * Renders a message's aggregate delivery state as an icon **plus** a visible
 * text label — never colour alone — so a colour-blind teacher loses no
 * information. The icon is decorative (`aria-hidden`) and the label carries
 * the meaning; the colour is layered on top as a redundant visual cue only.
 *
 * `state === null` covers a message with no delivery records yet (rendered as
 * "Not sent" rather than implying a failure).
 */
export function DeliveryStatus({ state }: { state: DeliveryState | null }) {
  if (state === null) {
    return (
      <span className="delivery-status delivery-status--muted" title="Not sent yet.">
        <DeliveryIcon state={null} />
        <span className="delivery-status__label">Not sent</span>
      </span>
    )
  }

  const meta = DELIVERY_STATE_META[state]
  return (
    <span
      className={`delivery-status delivery-status--${meta.tone}`}
      title={meta.description}
    >
      <DeliveryIcon state={state} />
      <span className="delivery-status__label">{meta.label}</span>
    </span>
  )
}

/**
 * Decorative icon per delivery state. Drawn with `currentColor` so it inherits
 * the badge's tone; `aria-hidden` keeps screen readers on the text label.
 */
function DeliveryIcon({ state }: { state: DeliveryState | null }) {
  const common = {
    className: 'delivery-status__icon',
    'aria-hidden': true,
    focusable: false,
    width: 16,
    height: 16,
    viewBox: '0 0 16 16',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 1.5,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
  }

  switch (state) {
    case 'QUEUED':
      return (
        <svg {...common}>
          <circle cx="8" cy="8" r="6.5" />
          <path d="M8 4.75V8l2.25 1.5" />
        </svg>
      )
    case 'SYNCING':
      return (
        <svg {...common}>
          <path d="M14.5 8a6.5 6.5 0 1 1-2-4.7" />
          <path d="M14.5 1.5v3.5H11" />
        </svg>
      )
    case 'DELIVERED':
      return (
        <svg {...common}>
          <path d="M2.75 8.75l3.25 3.25 7.25-7.75" />
        </svg>
      )
    case 'ACKNOWLEDGED':
      return (
        <svg {...common}>
          <path d="M1.5 8.5l3 3 4.5-4.5" />
          <path d="M7 8.5l3 3 4.5-5.5" />
        </svg>
      )
    case 'WAITING_FOR_SYNC':
      return (
        <svg {...common}>
          <path d="M4 12.5a3.25 3.25 0 0 1 .6-6.4A4.5 4.5 0 0 1 13.5 7a2.75 2.75 0 0 1-.25 5.5H4z" />
          <path d="M8 11V7.5M6.5 9L8 7.5 9.5 9" />
        </svg>
      )
    case 'FAILED':
      return (
        <svg {...common}>
          <path d="M8 2.5l6.5 11h-13z" />
          <path d="M8 7v3" />
          <path d="M8 12h.01" />
        </svg>
      )
    case null:
      return (
        <svg {...common}>
          <circle cx="8" cy="8" r="3.5" />
        </svg>
      )
    default: {
      const _exhaustive: never = state
      void _exhaustive
      return null
    }
  }
}
