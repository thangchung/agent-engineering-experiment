"use client";

/**
 * Main CoffeeShop page.
 *
 * Phase 3 (T026): setCustomer action + CustomerPanel
 * Phase 4 (T041): updateMenu + updateOrderSummary actions + MenuGrid + OrderSummaryPanel
 */

import { useState } from "react";
import { useCopilotAction, useCopilotReadable } from "@copilotkit/react-core";
import { CopilotSidebar } from "@copilotkit/react-ui";
import { CustomerPanel } from "@/components/ui/CustomerPanel";
import { MenuGrid } from "@/components/ui/MenuGrid";
import { OrderSummaryPanel } from "@/components/ui/OrderSummaryPanel";
import type { CustomerDto, MenuItemDto, OrderDto, OrderItemDto } from "@/types";

export default function HomePage() {
  const [customer, setCustomerState] = useState<CustomerDto | null>(null);
  const [menuItems, setMenuItems] = useState<MenuItemDto[]>([]);
  const [menuError, setMenuError] = useState<string | null>(null);
  const [currentOrder, setCurrentOrder] = useState<OrderDto | null>(null);

  // Make state readable by the agent for context-aware responses
  useCopilotReadable({
    description: "The currently identified customer",
    value: customer,
  });

  useCopilotReadable({
    description: "The current product catalog menu items",
    value: menuItems,
  });

  useCopilotReadable({
    description: "The current active order (null if none)",
    value: currentOrder,
  });

  // ------------------------------------------------------------------
  // T026 — setCustomer action
  // ------------------------------------------------------------------
  useCopilotAction({
    name: "setCustomer",
    description:
      "Set the identified customer after a successful lookup. " +
      "Shows the customer name and tier badge in the UI.",
    parameters: [
      { name: "customerId",  type: "string", description: "Customer ID (C-XXXX)",                   required: true },
      { name: "firstName",   type: "string", description: "Customer first name",                    required: true },
      { name: "lastName",    type: "string", description: "Customer last name",                     required: true },
      { name: "email",       type: "string", description: "Customer email address",                 required: true },
      { name: "phone",       type: "string", description: "Customer phone number",                  required: true },
      { name: "tier",        type: "string", description: "Customer tier: gold | silver | standard",required: true },
      { name: "greeting",    type: "string", description: "Personalised greeting string",           required: true },
    ],
    handler: ({ customerId, firstName, lastName, email, phone, tier, greeting }) => {
      setCustomerState({
        customerId,
        firstName,
        lastName,
        email,
        phone,
        tier: tier as CustomerDto["tier"],
        greeting,
      });
    },
  });

  // ------------------------------------------------------------------
  // T041 — updateMenu action
  // Called by agent after GetMenu tool succeeds.
  // ------------------------------------------------------------------
  useCopilotAction({
    name: "updateMenu",
    description:
      "Populate the menu grid with items from the product catalog. " +
      "Pass an empty array on 503 error — the grid will show the error message.",
    parameters: [
      {
        name: "items",
        type: "object[]",
        description: "Array of MenuItemDto objects from GET /api/v1/menu",
        required: true,
      },
      {
        name: "errorMessage",
        type: "string",
        description: "Error message to display if the catalog is unavailable",
        required: false,
      },
    ],
    handler: ({ items, errorMessage }) => {
      setMenuItems(items as MenuItemDto[]);
      setMenuError(errorMessage ?? null);
    },
  });

  // ------------------------------------------------------------------
  // T041 — updateOrderSummary action
  // Called by agent after PlaceOrder tool succeeds (or ModifyOrder).
  // ------------------------------------------------------------------
  useCopilotAction({
    name: "updateOrderSummary",
    description:
      "Update the order summary panel with the current order. " +
      "Called after PlaceOrder or ModifyOrder tool returns.",
    parameters: [
      {
        name: "order",
        type: "object",
        description: "OrderDto object returned by POST /api/v1/orders",
        required: true,
      },
    ],
    handler: ({ order }) => {
      setCurrentOrder(order as OrderDto);
    },
  });

  // Handler for clicking a menu item — adds to cart via agent context
  function handleItemSelect(item: MenuItemDto) {
    if (!item.isAvailable) return;
    // Selection intent is handled via agent chat; page holds display state only.
    // Agent picks up the readable "menuItems" state and drives order placement.
  }

  return (
    <main className="min-h-screen flex flex-col items-center p-8 gap-6">
      <h1 className="text-3xl font-bold">☕ CoffeeShop</h1>

      {/* Customer identification panel */}
      <div className="w-full max-w-2xl">
        <CustomerPanel customer={customer} />
      </div>

      {/* Menu + Order side by side */}
      {menuItems.length > 0 || menuError ? (
        <div className="w-full max-w-2xl grid grid-cols-1 gap-6 sm:grid-cols-2">
          <section aria-labelledby="menu-heading">
            <h2 id="menu-heading" className="mb-3 text-lg font-semibold text-gray-800">
              Menu
            </h2>
            <MenuGrid
              items={menuItems}
              onItemSelect={handleItemSelect}
              errorMessage={menuError}
            />
          </section>

          {currentOrder && (
            <section aria-labelledby="order-heading">
              <h2 id="order-heading" className="mb-3 text-lg font-semibold text-gray-800">
                Your Order
              </h2>
              <OrderSummaryPanel order={currentOrder} />
            </section>
          )}
        </div>
      ) : null}

      {/* CopilotSidebar — aria-label "Order assistant" set via labels prop */}
      <CopilotSidebar
        labels={{ title: "Order assistant" }}
        defaultOpen={true}
        clickOutsideToClose={false}
      />
    </main>
  );
}

