"use client";

import Image from "next/image";
import Link from "next/link";
import { useState } from "react";
import { useRouter } from "next/navigation";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { useCart } from "@/lib/cart-context";
import type { Product } from "@/lib/products";

interface ProductDetailClientProps {
  product: Product;
}

export function ProductDetailClient({ product }: ProductDetailClientProps) {
  const router = useRouter();
  const { addToCart } = useCart();
  const [quantity, setQuantity] = useState(1);

  function handleAddToCart() {
    for (let i = 0; i < quantity; i++) {
      addToCart(product);
    }
  }

  return (
    <div className="min-h-screen bg-white">
      <div className="max-w-5xl mx-auto px-6 py-8">
        <button
          onClick={() => router.back()}
          className="text-sm text-muted-foreground hover:text-foreground transition-colors mb-6 flex items-center gap-1"
        >
          ← Back
        </button>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-10">
          {/* Image */}
          <div className="relative h-[400px] rounded-xl overflow-hidden border border-black/10 bg-[#f8f8f8]">
            <Image
              src={product.image}
              alt={product.name}
              fill
              className="object-cover"
              unoptimized
            />
          </div>

          {/* Details */}
          <div className="flex flex-col gap-4">
            <span className="text-sm text-muted-foreground font-medium">
              {product.brand}
            </span>

            <h1 className="text-2xl font-semibold text-foreground leading-snug">
              {product.name}
            </h1>

            <p className="text-3xl font-bold text-foreground">
              ${product.price}
            </p>

            <Badge
              variant="secondary"
              className="w-fit text-xs font-normal bg-[#eceef2] text-foreground border-0 rounded-md px-2 py-0.5"
            >
              {product.category}
            </Badge>

            <p className="text-sm text-muted-foreground leading-relaxed">
              {product.description}
            </p>

            {/* Quantity */}
            <div className="flex flex-col gap-2">
              <span className="text-sm font-medium text-foreground">
                Quantity
              </span>
              <div className="flex items-center gap-3">
                <Button
                  variant="outline"
                  size="icon"
                  className="h-9 w-9 text-base"
                  onClick={() => setQuantity((q) => Math.max(1, q - 1))}
                  aria-label="Decrease quantity"
                >
                  −
                </Button>
                <span className="w-8 text-center text-sm font-semibold">
                  {quantity}
                </span>
                <Button
                  variant="outline"
                  size="icon"
                  className="h-9 w-9 text-base"
                  onClick={() => setQuantity((q) => q + 1)}
                  aria-label="Increase quantity"
                >
                  +
                </Button>
              </div>
            </div>

            <Button className="w-full mt-2" onClick={handleAddToCart}>
              Add to Cart
            </Button>

            <Link
              href="/cart"
              className="text-sm text-center text-muted-foreground hover:text-foreground underline underline-offset-2 transition-colors"
            >
              View Cart
            </Link>
          </div>
        </div>
      </div>
    </div>
  );
}
