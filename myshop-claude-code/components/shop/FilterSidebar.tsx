"use client";

import { Slider } from "@/components/ui/slider";
import { Checkbox } from "@/components/ui/checkbox";
import { Button } from "@/components/ui/button";
import { brands, PRICE_MIN, PRICE_MAX, type Brand } from "@/lib/products";

interface FilterSidebarProps {
  priceRange: [number, number];
  selectedBrands: Brand[];
  onPriceRangeChange: (range: [number, number]) => void;
  onBrandChange: (brand: Brand, checked: boolean) => void;
  onReset: () => void;
}

const QUICK_PRICE_RANGES: { label: string; min: number; max: number }[] = [
  { label: "Under $50", min: 0, max: 50 },
  { label: "$50-$100", min: 50, max: 100 },
  { label: "$100-$150", min: 100, max: 150 },
  { label: "$150+", min: 150, max: 300 },
];

export function FilterSidebar({
  priceRange,
  selectedBrands,
  onPriceRangeChange,
  onBrandChange,
  onReset,
}: FilterSidebarProps) {
  return (
    <aside className="w-[280px] shrink-0 border-r border-black/10 flex flex-col gap-6 px-6 pt-6 pb-8 self-start">
      {/* Header */}
      <div className="flex items-center justify-between">
        <span className="text-base font-normal text-foreground tracking-tight">
          Filters
        </span>
        <Button
          variant="outline"
          size="sm"
          onClick={onReset}
          className="h-8 px-3 text-sm font-medium border-black/10 rounded-lg"
        >
          Reset Filters
        </Button>
      </div>

      <div className="h-px bg-black/10" />

      {/* Price Range */}
      <div className="flex flex-col gap-4">
        <span className="text-base font-normal text-foreground tracking-tight">
          Price Range
        </span>

        <div className="flex flex-col gap-3">
          <Slider
            min={PRICE_MIN}
            max={PRICE_MAX}
            step={1}
            value={[priceRange[0], priceRange[1]]}
            onValueChange={(vals) =>
              onPriceRangeChange([vals[0], vals[1]] as [number, number])
            }
            className="w-full"
          />
          <div className="flex items-center justify-between text-sm text-muted-foreground">
            <span>${priceRange[0]}</span>
            <span>${priceRange[1]}</span>
          </div>
        </div>

        {/* Quick Select */}
        <div className="flex flex-col gap-2">
          <span className="text-sm text-foreground">Quick Select:</span>
          <div className="grid grid-cols-2 gap-2">
            {QUICK_PRICE_RANGES.map((range) => {
              const isActive =
                priceRange[0] === range.min && priceRange[1] === range.max;
              return (
                <Button
                  key={range.label}
                  variant={isActive ? "default" : "outline"}
                  size="sm"
                  onClick={() =>
                    onPriceRangeChange([range.min, range.max])
                  }
                  className="h-8 text-xs font-normal border-black/10 rounded-lg"
                >
                  {range.label}
                </Button>
              );
            })}
          </div>
        </div>
      </div>

      <div className="h-px bg-black/10" />

      {/* Luxury Brands */}
      <div className="flex flex-col gap-4">
        <span className="text-base font-normal text-foreground tracking-tight">
          Luxury Brands
        </span>

        <div className="flex flex-col gap-3">
          {brands.map(({ name, count }) => (
            <div key={name} className="flex items-center gap-3">
              <Checkbox
                id={`brand-${name}`}
                checked={selectedBrands.includes(name)}
                onCheckedChange={(checked) =>
                  onBrandChange(name, checked === true)
                }
                className="rounded-[4px] border-black/10 shadow-sm"
              />
              <label
                htmlFor={`brand-${name}`}
                className="flex-1 text-sm text-foreground cursor-pointer select-none"
              >
                {name}
              </label>
              <span className="text-sm text-muted-foreground bg-[#eceef2] rounded-lg px-2 py-0.5 min-w-[24px] text-center">
                {count}
              </span>
            </div>
          ))}
        </div>
      </div>
    </aside>
  );
}
