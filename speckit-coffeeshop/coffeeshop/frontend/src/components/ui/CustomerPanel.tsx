"use client";

import type { CustomerDto } from "@/types";

interface CustomerPanelProps {
  customer: CustomerDto | null;
}

const tierColors: Record<string, string> = {
  gold:     "bg-yellow-100 text-yellow-800 border-yellow-300",
  silver:   "bg-gray-100  text-gray-700   border-gray-300",
  standard: "bg-blue-50   text-blue-700   border-blue-200",
};

const tierLabels: Record<string, string> = {
  gold:     "Gold Member",
  silver:   "Silver Member",
  standard: "Standard Member",
};

/**
 * Displays the currently identified customer's name and tier badge.
 * Hidden when no customer is identified.
 * Driven by the `setCustomer` CopilotKit action registered in page.tsx.
 */
export function CustomerPanel({ customer }: CustomerPanelProps) {
  if (!customer) return null;

  const tierClass = tierColors[customer.tier] ?? tierColors["standard"];
  const tierLabel = tierLabels[customer.tier] ?? customer.tier;

  return (
    <div
      className="flex items-center gap-3 rounded-lg border p-3 bg-white shadow-sm"
      aria-label={`Identified customer: ${customer.firstName} ${customer.lastName}`}
    >
      <div className="flex-1">
        <p className="font-semibold text-gray-900">
          {customer.firstName} {customer.lastName}
        </p>
        <p className="text-sm text-gray-500">{customer.email}</p>
      </div>
      <span
        className={`rounded-full border px-3 py-0.5 text-xs font-medium ${tierClass}`}
        aria-label={`Tier: ${tierLabel}`}
      >
        {tierLabel}
      </span>
    </div>
  );
}
