"use client";

import Image from "next/image";
import Link from "next/link";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { useCart } from "@/lib/cart-context";

interface CartStep1Props {
  onNext: () => void;
}

export function CartStep1({ onNext }: CartStep1Props) {
  const { items, updateQuantity, removeFromCart, totalPrice } = useCart();

  if (items.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-20 gap-4">
        <p className="text-muted-foreground text-lg">Your cart is empty.</p>
        <Link href="/">
          <Button variant="outline">Continue Shopping</Button>
        </Link>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-4">
        {items.map(({ product, quantity }) => (
          <div key={product.id}>
            <div className="flex items-center gap-4 py-2">
              <div className="relative h-[60px] w-[60px] shrink-0 rounded-md overflow-hidden border border-black/10">
                <Image
                  src={product.image}
                  alt={product.name}
                  fill
                  className="object-cover"
                  unoptimized
                />
              </div>

              <div className="flex-1 min-w-0">
                <p className="text-xs text-muted-foreground">{product.brand}</p>
                <p className="text-sm font-medium text-foreground leading-snug">
                  {product.name}
                </p>
                <p className="text-sm font-semibold text-foreground mt-0.5">
                  ${product.price}
                </p>
              </div>

              <div className="flex items-center gap-2 shrink-0">
                <Button
                  variant="outline"
                  size="icon"
                  className="h-7 w-7 text-base"
                  onClick={() => updateQuantity(product.id, quantity - 1)}
                  aria-label="Decrease quantity"
                >
                  −
                </Button>
                <span className="w-6 text-center text-sm font-medium">
                  {quantity}
                </span>
                <Button
                  variant="outline"
                  size="icon"
                  className="h-7 w-7 text-base"
                  onClick={() => updateQuantity(product.id, quantity + 1)}
                  aria-label="Increase quantity"
                >
                  +
                </Button>
              </div>

              <div className="w-16 text-right shrink-0">
                <p className="text-sm font-semibold">
                  ${product.price * quantity}
                </p>
              </div>

              <button
                onClick={() => removeFromCart(product.id)}
                className="text-muted-foreground hover:text-foreground transition-colors text-lg leading-none shrink-0"
                aria-label="Remove item"
              >
                ×
              </button>
            </div>
            <Separator />
          </div>
        ))}
      </div>

      <div className="flex flex-col gap-3 bg-[#f8f8f8] rounded-lg p-4">
        <div className="flex justify-between text-sm text-muted-foreground">
          <span>Subtotal</span>
          <span className="font-medium text-foreground">${totalPrice}</span>
        </div>
        <div className="flex justify-between text-sm text-muted-foreground">
          <span>Shipping</span>
          <span>Calculated at next step</span>
        </div>
        <Separator />
        <div className="flex justify-between text-base font-semibold text-foreground">
          <span>Total</span>
          <span>${totalPrice}</span>
        </div>
      </div>

      <Button onClick={onNext} className="w-full">
        Proceed to Shipping
      </Button>
    </div>
  );
}
