"use client";

import { useState } from "react";
import Link from "next/link";
import { CheckCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Separator } from "@/components/ui/separator";
import { useCart } from "@/lib/cart-context";

interface PaymentData {
  cardNumber: string;
  cardName: string;
  expiry: string;
  cvv: string;
}

interface CartStep3Props {
  onBack: () => void;
}

const EMPTY_PAYMENT: PaymentData = {
  cardNumber: "",
  cardName: "",
  expiry: "",
  cvv: "",
};

export function CartStep3({ onBack }: CartStep3Props) {
  const { items, totalPrice, clearCart } = useCart();
  const [form, setForm] = useState<PaymentData>(EMPTY_PAYMENT);
  const [errors, setErrors] = useState<Partial<Record<keyof PaymentData, string>>>({});
  const [orderNumber, setOrderNumber] = useState<string | null>(null);

  function set(field: keyof PaymentData, value: string) {
    setForm((prev) => ({ ...prev, [field]: value }));
    if (errors[field]) setErrors((prev) => ({ ...prev, [field]: undefined }));
  }

  function validate(): boolean {
    const newErrors: Partial<Record<keyof PaymentData, string>> = {};
    if (!form.cardNumber.trim()) newErrors.cardNumber = "Required";
    if (!form.cardName.trim()) newErrors.cardName = "Required";
    if (!form.expiry.trim()) newErrors.expiry = "Required";
    if (!form.cvv.trim()) newErrors.cvv = "Required";
    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  }

  function handlePlaceOrder() {
    if (!validate()) return;
    const num = `ORD-${Math.floor(100000 + Math.random() * 900000)}`;
    setOrderNumber(num);
    clearCart();
  }

  if (orderNumber) {
    return (
      <div className="flex flex-col items-center justify-center py-16 gap-6 text-center">
        <CheckCircle className="h-16 w-16 text-green-500" />
        <div className="flex flex-col gap-2">
          <h2 className="text-2xl font-semibold text-foreground">
            Order Confirmed!
          </h2>
          <p className="text-muted-foreground">
            Thank you for your purchase. Your order number is{" "}
            <span className="font-semibold text-foreground">{orderNumber}</span>
            .
          </p>
          <p className="text-sm text-muted-foreground">
            You will receive a confirmation email shortly.
          </p>
        </div>
        <Link href="/">
          <Button>Continue Shopping</Button>
        </Link>
      </div>
    );
  }

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
      {/* Order summary */}
      <div className="flex flex-col gap-4">
        <h3 className="text-sm font-semibold text-foreground uppercase tracking-wide">
          Order Summary
        </h3>
        <div className="flex flex-col gap-3">
          {items.map(({ product, quantity }) => (
            <div key={product.id} className="flex justify-between text-sm">
              <span className="text-muted-foreground">
                {product.name}{" "}
                <span className="text-foreground font-medium">× {quantity}</span>
              </span>
              <span className="font-medium">${product.price * quantity}</span>
            </div>
          ))}
        </div>
        <Separator />
        <div className="flex justify-between text-base font-semibold">
          <span>Total</span>
          <span>${totalPrice}</span>
        </div>
      </div>

      {/* Payment form */}
      <div className="flex flex-col gap-4">
        <h3 className="text-sm font-semibold text-foreground uppercase tracking-wide">
          Payment Details
        </h3>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="cardNumber">Card Number</Label>
          <Input
            id="cardNumber"
            value={form.cardNumber}
            onChange={(e) => set("cardNumber", e.target.value)}
            placeholder="1234 5678 9012 3456"
            maxLength={19}
            className={errors.cardNumber ? "border-red-500" : ""}
          />
          {errors.cardNumber && (
            <p className="text-xs text-red-500">{errors.cardNumber}</p>
          )}
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="cardName">Name on Card</Label>
          <Input
            id="cardName"
            value={form.cardName}
            onChange={(e) => set("cardName", e.target.value)}
            placeholder="Jane Doe"
            className={errors.cardName ? "border-red-500" : ""}
          />
          {errors.cardName && (
            <p className="text-xs text-red-500">{errors.cardName}</p>
          )}
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="expiry">Expiry</Label>
            <Input
              id="expiry"
              value={form.expiry}
              onChange={(e) => set("expiry", e.target.value)}
              placeholder="MM/YY"
              maxLength={5}
              className={errors.expiry ? "border-red-500" : ""}
            />
            {errors.expiry && (
              <p className="text-xs text-red-500">{errors.expiry}</p>
            )}
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="cvv">CVV</Label>
            <Input
              id="cvv"
              value={form.cvv}
              onChange={(e) => set("cvv", e.target.value)}
              placeholder="123"
              maxLength={4}
              className={errors.cvv ? "border-red-500" : ""}
            />
            {errors.cvv && (
              <p className="text-xs text-red-500">{errors.cvv}</p>
            )}
          </div>
        </div>

        <div className="flex flex-col gap-3 mt-2">
          <Button onClick={handlePlaceOrder} className="w-full">
            Place Order · ${totalPrice}
          </Button>
          <Button variant="outline" onClick={onBack} className="w-full">
            Back
          </Button>
        </div>
      </div>
    </div>
  );
}
