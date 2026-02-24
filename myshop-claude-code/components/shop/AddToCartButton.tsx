"use client";

import { Button } from "@/components/ui/button";
import { useCart } from "@/lib/cart-context";
import type { Product } from "@/lib/products";

interface AddToCartButtonProps {
  product: Product;
  className?: string;
}

export function AddToCartButton({ product, className }: AddToCartButtonProps) {
  const { addToCart } = useCart();

  return (
    <Button
      onClick={(e) => {
        e.preventDefault();
        addToCart(product);
      }}
      className={className}
      size="sm"
    >
      Add to Cart
    </Button>
  );
}
