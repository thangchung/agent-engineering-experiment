"use client";

import { MenuItemDto } from "@/types";

interface MenuGridProps {
  items: MenuItemDto[];
  onItemSelect: (item: MenuItemDto) => void;
  errorMessage?: string | null;
}

/**
 * MenuGrid — displays all product-catalog items (including unavailable ones).
 *
 * Unavailable items are shown with a greyed-out style and "Unavailable" badge
 * (FR-016: never filter — always display with a visual indicator).
 *
 * Each item button has aria-label="{displayName} — ${price}" for WCAG 2.1 AA.
 * Keyboard activatable via Enter/Space (native <button> behaviour).
 */
export function MenuGrid({ items, onItemSelect, errorMessage }: MenuGridProps) {
  if (errorMessage) {
    return (
      <div
        className="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700"
        role="alert"
      >
        {errorMessage}
      </div>
    );
  }

  if (items.length === 0) {
    return (
      <p className="text-sm text-gray-500" role="status">
        No items available at this time.
      </p>
    );
  }

  // Group by category for readability
  const byCategory: Record<string, MenuItemDto[]> = {};
  for (const item of items) {
    (byCategory[item.category] ??= []).push(item);
  }

  return (
    <div className="space-y-6">
      {Object.entries(byCategory).map(([category, categoryItems]) => (
        <section key={category} aria-labelledby={`category-${category}`}>
          <h3
            id={`category-${category}`}
            className="mb-2 text-xs font-semibold uppercase tracking-wider text-gray-500"
          >
            {category}
          </h3>
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
            {categoryItems.map((item) => (
              <button
                key={item.type}
                aria-label={`${item.displayName} — $${item.price.toFixed(2)}`}
                disabled={!item.isAvailable}
                onClick={() => onItemSelect(item)}
                className={[
                  "relative rounded-lg border px-3 py-2 text-left text-sm transition-colors",
                  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2",
                  item.isAvailable
                    ? "border-gray-200 bg-white hover:bg-gray-50 focus-visible:ring-blue-500"
                    : "cursor-not-allowed border-gray-100 bg-gray-50 text-gray-400",
                ].join(" ")}
              >
                <span className="block font-medium">{item.displayName}</span>
                <span className="block text-xs">
                  ${item.price.toFixed(2)}
                </span>
                {!item.isAvailable && (
                  <span className="absolute right-1 top-1 rounded bg-gray-200 px-1 py-0.5 text-[10px] font-semibold uppercase text-gray-500">
                    Unavailable
                  </span>
                )}
              </button>
            ))}
          </div>
        </section>
      ))}
    </div>
  );
}
