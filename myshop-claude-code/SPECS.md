# MyShop — Product Specifications

## Overview

MyShop is a luxury beauty e-commerce storefront. Shoppers can browse and filter products, view full product details, add items to a cart, and complete a guided 3-step checkout flow.

---

## Epics

| ID | Epic |
|----|------|
| E1 | Product Browsing & Discovery |
| E2 | Product Detail |
| E3 | Shopping Cart Management |
| E4 | Checkout Flow |
| E5 | Site Navigation |

---

## User Stories

### E1 — Product Browsing & Discovery

**US-1.1 View product catalogue**
> As a shopper, I want to see all available products in a grid layout so that I can quickly scan the catalogue.

Acceptance criteria:
- Products are displayed in a responsive 4-column grid.
- Each card shows the product image, brand, name, short description, price, and category badge.
- A count of visible products is shown above the grid ("Showing N products").
- When no products match the active filters, an empty-state message is displayed.

---

**US-1.2 Filter by price range**
> As a shopper, I want to narrow the catalogue to products within my budget so that I don't see items I can't afford.

Acceptance criteria:
- A dual-handle slider lets me drag min and max price endpoints anywhere between $0 and $300.
- The current min/max values update in real time below the slider.
- Four quick-select buttons (Under $50 · $50–$100 · $100–$150 · $150+) snap the slider to preset ranges.
- The active quick-select button is visually highlighted.
- The product grid updates immediately without a page reload.

---

**US-1.3 Filter by brand**
> As a shopper, I want to show only products from brands I care about so that I can focus my browsing.

Acceptance criteria:
- Each brand is listed with a checkbox and a product-count badge.
- Multiple brands can be selected simultaneously; the grid shows products matching any selected brand.
- When no brands are selected, all brands' products are shown.
- The product grid updates immediately on checkbox change.

---

**US-1.4 Reset all filters**
> As a shopper, I want to clear all active filters at once so that I can start a fresh search.

Acceptance criteria:
- A "Reset Filters" button in the sidebar resets price range to $0–$300 and deselects all brand checkboxes.
- The product grid returns to showing all products.

---

### E2 — Product Detail

**US-2.1 View product detail page**
> As a shopper, I want to see full information about a product so that I can make an informed purchase decision.

Acceptance criteria:
- Clicking anywhere on the product card's image or info area navigates to `/products/[id]`.
- The detail page displays a large product image, brand, name, price, category badge, and full description.
- A "← Back" button returns to the previous page without reloading the catalogue.

---

**US-2.2 Select quantity on detail page**
> As a shopper, I want to choose how many units to add before adding to my cart so that I don't have to add the same item multiple times.

Acceptance criteria:
- A quantity selector shows the current quantity with − and + buttons.
- Quantity cannot go below 1.
- The displayed quantity is used when clicking "Add to Cart".

---

**US-2.3 Add to cart from detail page**
> As a shopper, I want to add a product (with my chosen quantity) to my cart from the detail page.

Acceptance criteria:
- Clicking "Add to Cart" on the detail page adds the product at the selected quantity.
- If the product is already in the cart, its quantity is incremented accordingly.
- The cart item count in the header updates immediately.
- A "View Cart" link lets me navigate directly to the cart.

---

### E3 — Shopping Cart Management

**US-3.1 Add to cart from product grid**
> As a shopper, I want to add a product directly from the catalogue without navigating away so that I can keep browsing.

Acceptance criteria:
- Each product card has an "Add to Cart" button below the product info.
- Clicking it adds one unit of the product to the cart.
- The link area of the card (image + info) still navigates to the product detail page; the button click does not.

---

**US-3.2 View cart contents**
> As a shopper, I want to see everything in my cart so that I can review my selection before checkout.

Acceptance criteria:
- The cart page (Step 1) lists all items with thumbnail, brand, name, unit price, quantity, and line total.
- An order summary shows subtotal, a shipping placeholder, and total.
- If the cart is empty, an empty-state message and a "Continue Shopping" link are shown.

---

**US-3.3 Adjust item quantities in the cart**
> As a shopper, I want to increase or decrease the quantity of any item in the cart so that I can fine-tune my order.

Acceptance criteria:
- Each cart item has − and + buttons.
- The line total updates immediately.
- Decreasing quantity to 0 removes the item from the cart.

---

**US-3.4 Remove an item from the cart**
> As a shopper, I want to remove an item entirely from my cart so that I don't have to reduce its quantity one by one.

Acceptance criteria:
- Each cart item has a remove (×) button.
- Clicking it removes the item immediately.
- The order summary totals update instantly.

---

**US-3.5 See live cart count in the header**
> As a shopper, I want to always see how many items are in my cart so that I know my cart status without navigating to it.

Acceptance criteria:
- The header displays a ShoppingCart icon linking to `/cart`.
- A badge showing the total item count (sum of quantities) overlays the icon whenever the cart is non-empty.
- The badge is hidden when the cart is empty.
- The count caps at "99+" display if it exceeds 99.

---

### E4 — Checkout Flow

**US-4.1 Navigate a guided 3-step checkout**
> As a shopper, I want a clear step-by-step checkout so that I know exactly where I am in the process and what comes next.

Acceptance criteria:
- A step indicator shows Cart → Shipping → Payment with visual states: completed (filled), current (ring), upcoming (muted).
- Each step's heading matches the active step.
- "Back" on steps 2 and 3 return to the previous step, preserving entered data.

---

**US-4.2 Enter shipping information**
> As a shopper, I want to provide my delivery address so that my order can be sent to the right place.

Acceptance criteria:
- Step 2 collects: First Name, Last Name, Email, Phone, Address, City, State/Province, ZIP/Postal Code, Country.
- All fields except Phone are required.
- Attempting to advance with any required field empty shows a per-field "Required" error message.
- Valid data is preserved if the shopper navigates back to Step 1 and returns.

---

**US-4.3 Enter payment details**
> As a shopper, I want to enter my payment card details so that I can pay for my order.

Acceptance criteria:
- Step 3 collects: Card Number, Name on Card, Expiry (MM/YY), CVV.
- All four fields are required; empty fields show per-field error messages.
- An order summary (items, quantities, line totals, grand total) is visible alongside the payment form.

---

**US-4.4 Place an order**
> As a shopper, I want to confirm and submit my order so that I receive my products.

Acceptance criteria:
- Clicking "Place Order" validates payment fields first.
- On success, the cart is cleared and a success screen is shown within the same page.
- The success screen displays a checkmark icon, "Order Confirmed!", a randomly generated order number (format `ORD-XXXXXX`), and a confirmation message.
- A "Continue Shopping" button returns the shopper to the home page (`/`).

---

### E5 — Site Navigation

**US-5.1 Access the home page from anywhere**
> As a shopper, I want a persistent header link so that I can return to the product catalogue at any time.

Acceptance criteria:
- The "MyShop" logo text in the header links to `/`.
- The header is sticky at the top and visible on all pages and scroll positions.

**US-5.2 Navigate to the cart from anywhere**
> As a shopper, I want a single click to reach my cart from any page.

Acceptance criteria:
- The cart icon in the header links to `/cart` from any page.

---

## Non-Functional Requirements

### Performance

| ID | Requirement |
|----|-------------|
| NFR-P1 | All product detail pages are statically pre-rendered at build time (`generateStaticParams`). No server round-trip is needed to serve them. |
| NFR-P2 | The product grid page renders on the server; client-side JS is limited to filter interactivity and cart state. |
| NFR-P3 | Images must use `next/image` with `fill` layout to avoid layout shift and leverage automatic format optimisation. |

### Reliability & Correctness

| ID | Requirement |
|----|-------------|
| NFR-R1 | Navigating to `/products/[id]` with an unknown id must return a 404 (Next.js `notFound()`). |
| NFR-R2 | Cart state is consistent across all pages during a session; adding from the grid, the detail page, or adjusting in the cart all reflect in the same shared context. |
| NFR-R3 | Filter logic (price range + brand) is applied in a single `useMemo` in `ShopPage` — there is no duplicated filtering elsewhere. |

### Maintainability

| ID | Requirement |
|----|-------------|
| NFR-M1 | All domain types (`Product`, `Brand`, `Category`) and constants (`PRICE_MIN`, `PRICE_MAX`, `brands`) are defined exclusively in `lib/products.ts`. |
| NFR-M2 | shadcn-generated components in `components/ui/` must not be edited directly; customisation is applied via `className` props at call sites. |
| NFR-M3 | The `cn()` utility from `lib/utils.ts` is used for all conditional class composition. |
| NFR-M4 | The `@/*` path alias is used for all cross-directory imports; no relative `../../` chains. |

### Architecture & Boundaries

| ID | Requirement |
|----|-------------|
| NFR-A1 | `app/layout.tsx` remains a server component. The `<Providers>` client wrapper is a separate file (`components/providers/Providers.tsx`) that holds all client-only providers. |
| NFR-A2 | Route-level files (`app/**/page.tsx`, `app/layout.tsx`) must not be marked `"use client"`. |
| NFR-A3 | The `"use client"` directive is pushed as deep as possible — presentational leaf components that need no interactivity are server components. |
| NFR-A4 | Cart state is managed with React Context only; no external state management library is introduced. |
| NFR-A5 | The Radix UI unified package (`radix-ui`) is used; individual `@radix-ui/*` sub-packages must not be installed. |

### Security

| ID | Requirement |
|----|-------------|
| NFR-S1 | External image hostnames must be explicitly allowlisted in `next.config.ts` under `images.remotePatterns`; wildcard hostname patterns are not permitted. |
| NFR-S2 | No user-supplied data is rendered as raw HTML; all dynamic values are passed as React children or props. |

### Accessibility

| ID | Requirement |
|----|-------------|
| NFR-AC1 | All icon-only interactive elements (cart icon, quantity buttons, remove button) carry an `aria-label`. |
| NFR-AC2 | Form fields in shipping and payment steps use `<Label>` components associated to their `<Input>` via matching `htmlFor`/`id` attributes. |
| NFR-AC3 | The step indicator communicates completed, current, and upcoming states visually and through sufficient colour contrast. |

### Build & Tooling

| ID | Requirement |
|----|-------------|
| NFR-B1 | `npm run build` must complete with zero TypeScript errors and zero ESLint errors. |
| NFR-B2 | There is no `tailwind.config.js`; Tailwind v4 is configured entirely via `@theme` in `app/globals.css`. |
| NFR-B3 | Arbitrary pixel values in Tailwind classes are avoided unless matching an exact design spec; design-token classes (`text-foreground`, `text-muted-foreground`, `border-border`) are preferred. |
