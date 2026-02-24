"use client";

import Link from "next/link";
import { ShoppingCart } from "lucide-react";
import { useCart } from "@/lib/cart-context";

export function Header() {
  const { totalItems } = useCart();

  return (
    <header className="h-14 bg-white border-b border-black/10 flex items-center justify-between px-6 sticky top-0 z-50">
      <Link
        href="/"
        className="text-lg font-semibold text-foreground hover:opacity-70 transition-opacity"
      >
        MyShop
      </Link>

      <Link
        href="/cart"
        className="relative flex items-center gap-2 text-foreground hover:opacity-70 transition-opacity"
        aria-label="Cart"
      >
        <ShoppingCart className="h-5 w-5" />
        {totalItems > 0 && (
          <span className="absolute -top-2 -right-2 flex h-4 w-4 items-center justify-center rounded-full bg-foreground text-white text-[10px] font-semibold leading-none">
            {totalItems > 99 ? "99+" : totalItems}
          </span>
        )}
      </Link>
    </header>
  );
}
