"use client";

import { OrderDto } from "@/types";

interface StatusBadgeProps {
  status: string;
}

const STATUS_STYLES: Record<string, string> = {
  pending:    "bg-yellow-100 text-yellow-800",
  confirmed:  "bg-blue-100 text-blue-800",
  preparing:  "bg-purple-100 text-purple-800",
  ready:      "bg-green-100 text-green-800",
  completed:  "bg-gray-100 text-gray-600",
  cancelled:  "bg-red-100 text-red-700",
};

/**
 * StatusBadge — displays the current order status.
 *
 * WCAG 2.1 AA: role="status" + aria-live="polite" so screen readers
 * announce status transitions within 2 seconds of the update event.
 */
export function StatusBadge({ status }: StatusBadgeProps) {
  const style = STATUS_STYLES[status.toLowerCase()] ?? "bg-gray-100 text-gray-600";
  return (
    <span
      role="status"
      aria-live="polite"
      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold capitalize ${style}`}
    >
      {status}
    </span>
  );
}

interface OrderSummaryPanelProps {
  order: OrderDto | null;
}

/**
 * OrderSummaryPanel — shows items, quantities, line totals, total price, and status.
 *
 * Hidden when no active order. The status update region has aria-live="polite"
 * so WCAG success criterion 4.1.3 (Status Messages) is satisfied.
 */
export function OrderSummaryPanel({ order }: OrderSummaryPanelProps) {
  if (!order) return null;

  return (
    <section
      className="rounded-xl border border-gray-200 bg-white p-4 shadow-sm"
      aria-labelledby="order-summary-title"
    >
      <div className="mb-3 flex items-center justify-between">
        <h2
          id="order-summary-title"
          className="text-sm font-semibold text-gray-800"
        >
          Order {order.id}
        </h2>
        {/* aria-live="polite" region for WCAG status updates */}
        <div aria-live="polite">
          <StatusBadge status={order.status} />
        </div>
      </div>

      <ul className="mb-3 space-y-1 text-sm">
        {order.items.map((item) => (
          <li key={item.type} className="flex justify-between text-gray-700">
            <span>
              {item.displayName} × {item.quantity}
            </span>
            <span>${item.lineTotal.toFixed(2)}</span>
          </li>
        ))}
      </ul>

      <div className="flex justify-between border-t border-gray-100 pt-2 text-sm font-semibold text-gray-800">
        <span>Total</span>
        <span>${order.totalPrice.toFixed(2)}</span>
      </div>

      {order.estimatedPickup && (
        <p className="mt-2 text-xs text-gray-500">{order.estimatedPickup}</p>
      )}

      {order.notes && (
        <p className="mt-1 text-xs italic text-gray-400">"{order.notes}"</p>
      )}
    </section>
  );
}
