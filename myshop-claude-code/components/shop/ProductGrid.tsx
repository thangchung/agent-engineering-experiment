import { ProductCard } from "./ProductCard";
import type { Product } from "@/lib/products";

interface ProductGridProps {
  products: Product[];
}

export function ProductGrid({ products }: ProductGridProps) {
  return (
    <div className="flex-1 flex flex-col gap-6 min-w-0">
      <div>
        <h1 className="text-2xl font-semibold text-foreground tracking-tight">
          Luxury Beauty Products
        </h1>
        <p className="text-sm text-muted-foreground mt-1">
          Showing {products.length} products
        </p>
      </div>

      <div className="grid grid-cols-4 gap-6">
        {products.map((product) => (
          <ProductCard key={product.id} product={product} />
        ))}
      </div>

      {products.length === 0 && (
        <div className="flex items-center justify-center h-64 text-muted-foreground text-sm">
          No products match the selected filters.
        </div>
      )}
    </div>
  );
}
