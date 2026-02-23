// -----------------------------------------------------------------------
// Shared TypeScript interfaces for the CoffeeShop frontend.
// These mirror the C# records in contracts/orders.md and contracts/customers.md
// exactly — no extra/missing fields allowed (build gate).
// -----------------------------------------------------------------------

// ----- Enumerations (string unions match C# enum name casing) -----------

export type CustomerTier = "gold" | "silver" | "standard";

export type ItemCategory = "Beverages" | "Food" | "Others";

export type ItemType =
  | "CAPPUCCINO"
  | "COFFEE_BLACK"
  | "COFFEE_WITH_ROOM"
  | "ESPRESSO"
  | "ESPRESSO_DOUBLE"
  | "LATTE"
  | "CAKEPOP"
  | "CROISSANT"
  | "MUFFIN"
  | "CROISSANT_CHOCOLATE"
  | "CHICKEN_MEATBALLS";

export type OrderStatus =
  | "pending"
  | "confirmed"
  | "preparing"
  | "ready"
  | "completed"
  | "cancelled";

// ----- DTOs (read-only — from server) -----------------------------------

/** Matches C# `MenuItemDto` in contracts/menu.md */
export interface MenuItemDto {
  readonly type: ItemType;
  readonly displayName: string;
  readonly category: ItemCategory;
  readonly price: number;
  readonly isAvailable: boolean;
}

/** Matches C# `OrderItemDto` in contracts/orders.md */
export interface OrderItemDto {
  readonly type: ItemType;
  readonly displayName: string;
  readonly quantity: number;
  readonly unitPrice: number;
  readonly lineTotal: number;
}

/** Matches C# `OrderDto` in contracts/orders.md */
export interface OrderDto {
  readonly id: string;
  readonly customerId: string;
  readonly items: ReadonlyArray<OrderItemDto>;
  readonly totalPrice: number;
  readonly status: OrderStatus;
  readonly notes: string | null;
  readonly estimatedPickup: string | null;
  readonly createdAt: string; // ISO-8601
  readonly updatedAt: string; // ISO-8601
}

/** Matches C# `CustomerLookupResponse` in contracts/customers.md */
export interface CustomerDto {
  readonly customerId: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly email: string;
  readonly phone: string;
  readonly tier: CustomerTier;
  readonly greeting: string;
}

// ----- Response envelopes -----------------------------------------------

/** Matches C# `MenuResponse` in contracts/menu.md */
export interface MenuResponse {
  readonly items: ReadonlyArray<MenuItemDto>;
}

/** Matches C# `OrderHistoryResponse` in contracts/orders.md */
export interface OrderHistoryResponse {
  readonly customerId: string;
  readonly orders: ReadonlyArray<OrderDto>; // sorted most-recent first
}

/** Alias for customer lookup response (named consistently with plan.md) */
export type CustomerLookupResponse = CustomerDto;

// ----- Request types (write) --------------------------------------------

/** Matches C# `OrderItemRequest` */
export interface OrderItemRequest {
  type: ItemType;
  quantity: number; // 1–5 (FR-024)
}

/** Matches C# `PlaceOrderRequest` */
export interface PlaceOrderRequest {
  customerId: string;
  items: OrderItemRequest[];
  notes?: string;
}

/** Matches C# `ModifyOrderRequest` */
export interface ModifyOrderRequest {
  notes?: string;
  items?: OrderItemRequest[];
}

/** Matches C# `CancelOrderResponse` */
export interface CancelOrderResponse {
  readonly orderId: string;
  readonly status: "cancelled";
}
