export type Category = "Skincare" | "Makeup" | "Fragrance";
export type Brand = "Estée Lauder" | "Chanel" | "Dior" | "Tom Ford" | "La Mer";

export interface Product {
  id: number;
  name: string;
  brand: Brand;
  description: string;
  price: number;
  category: Category;
  image: string;
}

const imgSerum =
  "https://www.figma.com/api/mcp/asset/3afd6836-08cf-4c27-8010-0a06ff1230e2";
const imgMakeup =
  "https://www.figma.com/api/mcp/asset/c63627d2-4616-40b4-a87c-25ddaa673296";
const imgFragrance =
  "https://www.figma.com/api/mcp/asset/d371f5c9-8635-415d-9d6d-3da03e55ac12";

export const products: Product[] = [
  {
    id: 1,
    name: "Revitalizing Night Serum",
    brand: "Estée Lauder",
    description: "Advanced anti-aging night serum with retinol",
    price: 89,
    category: "Skincare",
    image: imgSerum,
  },
  {
    id: 2,
    name: "Luxury Foundation",
    brand: "Chanel",
    description: "Full coverage foundation with SPF 30",
    price: 68,
    category: "Makeup",
    image: imgMakeup,
  },
  {
    id: 3,
    name: "Signature Fragrance",
    brand: "Dior",
    description: "Elegant floral fragrance with lasting power",
    price: 125,
    category: "Fragrance",
    image: imgFragrance,
  },
  {
    id: 4,
    name: "Hydrating Face Cream",
    brand: "Estée Lauder",
    description: "Rich moisturizing cream for dry skin",
    price: 75,
    category: "Skincare",
    image: imgSerum,
  },
  {
    id: 5,
    name: "Premium Lipstick",
    brand: "Tom Ford",
    description: "Long-lasting matte lipstick in bold colors",
    price: 58,
    category: "Makeup",
    image: imgMakeup,
  },
  {
    id: 6,
    name: "Eye Cream",
    brand: "La Mer",
    description: "Anti-aging eye cream with marine ingredients",
    price: 195,
    category: "Skincare",
    image: imgSerum,
  },
  {
    id: 7,
    name: "Rouge Intense",
    brand: "Chanel",
    description: "Classic red lipstick with satin finish",
    price: 45,
    category: "Makeup",
    image: imgMakeup,
  },
  {
    id: 8,
    name: "Midnight Eau de Parfum",
    brand: "Tom Ford",
    description: "Mysterious and seductive evening fragrance",
    price: 180,
    category: "Fragrance",
    image: imgFragrance,
  },
  {
    id: 9,
    name: "Vitamin C Serum",
    brand: "Estée Lauder",
    description: "Brightening vitamin C serum for radiant skin",
    price: 92,
    category: "Skincare",
    image: imgSerum,
  },
  {
    id: 10,
    name: "Cleansing Miracle",
    brand: "La Mer",
    description: "Gentle foam cleanser with marine extracts",
    price: 110,
    category: "Skincare",
    image: imgSerum,
  },
  {
    id: 11,
    name: "Classic Perfume",
    brand: "Dior",
    description: "Timeless floral scent for everyday elegance",
    price: 98,
    category: "Fragrance",
    image: imgFragrance,
  },
  {
    id: 12,
    name: "Concealer Pro",
    brand: "Tom Ford",
    description: "Full coverage concealer with natural finish",
    price: 72,
    category: "Makeup",
    image: imgMakeup,
  },
  {
    id: 13,
    name: "Brightening Mask",
    brand: "Estée Lauder",
    description: "Weekly treatment mask for glowing complexion",
    price: 65,
    category: "Skincare",
    image: imgSerum,
  },
  {
    id: 14,
    name: "Mascara Volume",
    brand: "Chanel",
    description: "Volumizing mascara for dramatic lashes",
    price: 38,
    category: "Makeup",
    image: imgMakeup,
  },
  {
    id: 15,
    name: "Body Lotion Luxury",
    brand: "La Mer",
    description: "Nourishing body lotion with marine ingredients",
    price: 155,
    category: "Skincare",
    image: imgSerum,
  },
];

export const brands: { name: Brand; count: number }[] = [
  { name: "Estée Lauder", count: 4 },
  { name: "Chanel", count: 3 },
  { name: "Dior", count: 2 },
  { name: "Tom Ford", count: 3 },
  { name: "La Mer", count: 3 },
];

export const PRICE_MIN = 0;
export const PRICE_MAX = 300;
