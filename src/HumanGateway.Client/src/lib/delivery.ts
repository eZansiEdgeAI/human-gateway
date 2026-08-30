/**
 * Delivery-lifecycle presentation helpers (PWA-FR-05, product vision §10).
 *
 * The delivery state machine (PROTO-FR-05) has six states. The UI renders each
 * as icon + text — never colour alone — so a colour-blind teacher loses no
 * information (ACC-03). This module is the single source of truth for the
 * human-facing label, description, and tone of each state, plus the aggregation
 * rule that collapses a message's per-recipient deliveries into one summary
 * status for the Inbox list.
 */

import type { Delivery, DeliveryState, MessageView } from '../types/protocol'

/** Presentation metadata for a delivery state. */
export interface DeliveryStateMeta {
  /** Human-facing label (e.g. "Delivered"). */
  label: string
  /** Longer description for `title`/`aria-label` context. */
  description: string
  /** Visual tone (paired with the icon + text so it is never the only signal). */
  tone: 'muted' | 'active' | 'success' | 'danger'
}

/** Labels and tones for every delivery state, keyed by wire token. */
export const DELIVERY_STATE_META: Record<DeliveryState, DeliveryStateMeta> = {
  QUEUED: {
    label: 'Queued',
    description: 'Waiting to be sent.',
    tone: 'muted',
  },
  SYNCING: {
    label: 'Syncing',
    description: 'Sending now.',
    tone: 'active',
  },
  DELIVERED: {
    label: 'Delivered',
    description: 'Delivered to the recipient.',
    tone: 'success',
  },
  ACKNOWLEDGED: {
    label: 'Acknowledged',
    description: 'The recipient acknowledged receipt.',
    tone: 'success',
  },
  WAITING_FOR_SYNC: {
    label: 'Waiting for sync',
    description: 'Saved on this device; will send when connected.',
    tone: 'muted',
  },
  FAILED: {
    label: 'Failed',
    description: 'Sending failed.',
    tone: 'danger',
  },
}

/**
 * Rank used to summarise several deliveries into one status. Higher rank
 * "wins": an error (FAILED) outranks an in-flight sync, which outranks a
 * completed delivery, so the Inbox surfaces the most important outstanding
 * state for a message with many recipients.
 */
const RANK: Record<DeliveryState, number> = {
  FAILED: 6,
  SYNCING: 5,
  WAITING_FOR_SYNC: 4,
  QUEUED: 3,
  DELIVERED: 2,
  ACKNOWLEDGED: 1,
}

/**
 * Collapses a message's per-recipient delivery records into a single summary
 * state, or `null` when there are no deliveries yet.
 */
export function aggregateDeliveryState(deliveries: Delivery[]): DeliveryState | null {
  if (deliveries.length === 0) return null

  let summary = deliveries[0].state
  for (const delivery of deliveries) {
    if (RANK[delivery.state] > RANK[summary]) summary = delivery.state
  }
  return summary
}

/** Summary delivery state for a message view (envelope + deliveries). */
export function messageDeliveryState(view: MessageView): DeliveryState | null {
  return aggregateDeliveryState(view.deliveries)
}
