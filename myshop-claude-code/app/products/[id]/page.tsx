import { notFound } from "next/navigation";
import { products } from "@/lib/products";
import { ProductDetailClient } from "@/components/product/ProductDetailClient";

interface ProductPageProps {
  params: Promise<{ id: string }>;
}

export default async function ProductPage({ params }: ProductPageProps) {
  const { id } = await params;
  const product = products.find((p) => p.id === Number(id));

  if (!product) notFound();

  return <ProductDetailClient product={product} />;
}

export function generateStaticParams() {
  return products.map((p) => ({ id: String(p.id) }));
}
