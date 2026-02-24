"use client";

import Image from "next/image";
import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import { AddToCartButton } from "@/components/shop/AddToCartButton";
import type { Product } from "@/lib/products";

interface ProductCardProps {
  product: Product;
}

export function ProductCard({ product }: ProductCardProps) {
  return (
    <div className="bg-white border border-black/10 rounded-lg overflow-hidden flex flex-col">
      <Link href={`/products/${product.id}`} className="block flex-1">
        <div className="relative h-[258px] w-full shrink-0">
          <Image
            src={product.image}
            alt={product.name}
            fill
            className="object-cover"
            unoptimized
          />
        </div>

        <div className="flex flex-col gap-4 p-4 flex-1">
          <div className="flex flex-col gap-1 flex-1">
            <div className="flex items-start justify-between gap-2">
              <div className="flex flex-col gap-1 flex-1">
                <span className="text-xs text-muted-foreground font-normal">
                  {product.brand}
                </span>
                <h3 className="text-sm font-medium text-foreground leading-snug">
                  {product.name}
                </h3>
                <p className="text-xs text-muted-foreground leading-relaxed line-clamp-2">
                  {product.description}
                </p>
              </div>
            </div>
          </div>

          <div className="flex items-center justify-between">
            <span className="text-lg font-semibold text-foreground">
              ${product.price}
            </span>
            <Badge
              variant="secondary"
              className="text-xs font-normal bg-[#eceef2] text-foreground border-0 rounded-md px-2 py-0.5"
            >
              {product.category}
            </Badge>
          </div>
        </div>
      </Link>

      <div className="px-4 pb-4">
        <AddToCartButton product={product} className="w-full" />
      </div>
    </div>
  );
}
