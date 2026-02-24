# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
npm run dev      # Start dev server at http://localhost:3000
npm run build    # Production build (also type-checks via Next.js)
npm run start    # Start production server after build
npm run lint     # Run ESLint (Next.js core-web-vitals + TypeScript rules)
```

No test runner is configured. Type-checking is done implicitly by `next build`; for a standalone check, run `npx tsc --noEmit` if needed (requires TypeScript to be installed globally or via npx).

## Tech Stack

- **Next.js 16** (App Router, Turbopack in dev), **React 19**, **TypeScript 5** (strict mode)
- **Tailwind CSS v4** — imported via `@import "tailwindcss"` in `globals.css`; there is no `tailwind.config.js`
- **shadcn/ui v3** — components live in `components/ui/`, generated via `npx shadcn add <component>`
- **Radix UI** primitives (via `radix-ui` package, not `@radix-ui/*` sub-packages)
- **`cn()` utility** in `lib/utils.ts` wraps `clsx` + `tailwind-merge` — always use it when conditionally composing class names

## Architecture

### Server vs. Client boundary

`app/page.tsx` is a server component that renders `<ShopPage />`, which is the **client component root** (`"use client"`). All interactive state (filters, price range) lives in `ShopPage`. Presentational leaf components (`ProductCard`, `ProductGrid`) are server components — keep them that way unless interactivity requires otherwise.

### State flow

All filter state is owned by `ShopPage` and passed down as props. `FilterSidebar` is a controlled component: it receives current state and fires callbacks (`onPriceRangeChange`, `onBrandChange`, `onReset`) — it holds no local state. Filtering logic (`useMemo`) lives exclusively in `ShopPage`.

### Data layer

`lib/products.ts` is the single source of truth for all product data, domain types (`Product`, `Brand`, `Category`), and filter constants (`PRICE_MIN`, `PRICE_MAX`, `brands`). Keep types co-located with the data they describe. If a real API is introduced later, this file is the adapter layer to replace.

### Component organisation

```
components/ui/       ← shadcn-generated, do not edit manually
components/shop/     ← project components; each is a named export, not default
lib/                 ← data, types, and utilities (no React)
app/                 ← Next.js routes and layout only
```

## Do / Don't

**Do:**
- Use `cn()` from `lib/utils` for all conditional class composition.
- Keep `components/ui/` untouched — customise shadcn components via `className` props at the call site.
- Add new domain types and constants to `lib/products.ts` (or a new `lib/*.ts` file); avoid defining them inline in components.
- Use the `@/*` path alias for all imports (maps to the project root).
- Mark a component `"use client"` only when it uses hooks, browser APIs, or event handlers. Push the boundary as deep as possible.
- Use `next/image` for all `<img>` tags; add new external image domains to `next.config.ts` → `images.remotePatterns`.

**Don't:**
- Don't add a `tailwind.config.js` — Tailwind v4 is configured entirely in `app/globals.css` via `@theme`.
- Don't install `@radix-ui/*` sub-packages — the project uses the unified `radix-ui` package.
- Don't duplicate filter logic outside `ShopPage`; filtering is intentionally centralised there.
- Don't use arbitrary pixel values in Tailwind unless matching an exact Figma spec — prefer design token classes (`text-foreground`, `text-muted-foreground`, `border-border`).
- Don't use `"use client"` on page or layout files; keep route-level files as server components.
