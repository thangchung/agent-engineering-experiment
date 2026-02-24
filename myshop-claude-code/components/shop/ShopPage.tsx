"use client";

import { useState, useMemo } from "react";
import { FilterSidebar } from "./FilterSidebar";
import { ProductGrid } from "./ProductGrid";
import { products, PRICE_MIN, PRICE_MAX, type Brand } from "@/lib/products";

export function ShopPage() {
  const [priceRange, setPriceRange] = useState<[number, number]>([
    PRICE_MIN,
    PRICE_MAX,
  ]);
  const [selectedBrands, setSelectedBrands] = useState<Brand[]>([]);

  const filteredProducts = useMemo(() => {
    return products.filter((p) => {
      const inPrice = p.price >= priceRange[0] && p.price <= priceRange[1];
      const inBrand =
        selectedBrands.length === 0 || selectedBrands.includes(p.brand);
      return inPrice && inBrand;
    });
  }, [priceRange, selectedBrands]);

  function handleBrandChange(brand: Brand, checked: boolean) {
    setSelectedBrands((prev) =>
      checked ? [...prev, brand] : prev.filter((b) => b !== brand)
    );
  }

  function handleReset() {
    setPriceRange([PRICE_MIN, PRICE_MAX]);
    setSelectedBrands([]);
  }

  return (
    <div className="min-h-screen bg-white flex">
      <FilterSidebar
        priceRange={priceRange}
        selectedBrands={selectedBrands}
        onPriceRangeChange={setPriceRange}
        onBrandChange={handleBrandChange}
        onReset={handleReset}
      />
      <main className="flex-1 p-6 overflow-auto">
        <ProductGrid products={filteredProducts} />
      </main>
    </div>
  );
}
