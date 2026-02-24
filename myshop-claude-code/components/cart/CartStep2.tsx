"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

export interface ShippingData {
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  address: string;
  city: string;
  state: string;
  zip: string;
  country: string;
}

interface CartStep2Props {
  initialData: ShippingData;
  onNext: (data: ShippingData) => void;
  onBack: () => void;
}

const EMPTY: ShippingData = {
  firstName: "",
  lastName: "",
  email: "",
  phone: "",
  address: "",
  city: "",
  state: "",
  zip: "",
  country: "",
};

export function CartStep2({ initialData, onNext, onBack }: CartStep2Props) {
  const [form, setForm] = useState<ShippingData>(
    initialData.firstName ? initialData : EMPTY
  );
  const [errors, setErrors] = useState<Partial<Record<keyof ShippingData, string>>>({});

  function set(field: keyof ShippingData, value: string) {
    setForm((prev) => ({ ...prev, [field]: value }));
    if (errors[field]) setErrors((prev) => ({ ...prev, [field]: undefined }));
  }

  function validate(): boolean {
    const required: (keyof ShippingData)[] = [
      "firstName",
      "lastName",
      "email",
      "address",
      "city",
      "state",
      "zip",
      "country",
    ];
    const newErrors: Partial<Record<keyof ShippingData, string>> = {};
    for (const key of required) {
      if (!form[key].trim()) newErrors[key] = "Required";
    }
    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  }

  function handleSubmit() {
    if (validate()) onNext(form);
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="grid grid-cols-2 gap-4">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="firstName">First Name</Label>
          <Input
            id="firstName"
            value={form.firstName}
            onChange={(e) => set("firstName", e.target.value)}
            placeholder="Jane"
            className={errors.firstName ? "border-red-500" : ""}
          />
          {errors.firstName && (
            <p className="text-xs text-red-500">{errors.firstName}</p>
          )}
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="lastName">Last Name</Label>
          <Input
            id="lastName"
            value={form.lastName}
            onChange={(e) => set("lastName", e.target.value)}
            placeholder="Doe"
            className={errors.lastName ? "border-red-500" : ""}
          />
          {errors.lastName && (
            <p className="text-xs text-red-500">{errors.lastName}</p>
          )}
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="email">Email</Label>
          <Input
            id="email"
            type="email"
            value={form.email}
            onChange={(e) => set("email", e.target.value)}
            placeholder="jane@example.com"
            className={errors.email ? "border-red-500" : ""}
          />
          {errors.email && (
            <p className="text-xs text-red-500">{errors.email}</p>
          )}
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="phone">Phone</Label>
          <Input
            id="phone"
            type="tel"
            value={form.phone}
            onChange={(e) => set("phone", e.target.value)}
            placeholder="+1 (555) 000-0000"
          />
        </div>

        <div className="col-span-2 flex flex-col gap-1.5">
          <Label htmlFor="address">Address</Label>
          <Input
            id="address"
            value={form.address}
            onChange={(e) => set("address", e.target.value)}
            placeholder="123 Main Street, Apt 4B"
            className={errors.address ? "border-red-500" : ""}
          />
          {errors.address && (
            <p className="text-xs text-red-500">{errors.address}</p>
          )}
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="city">City</Label>
          <Input
            id="city"
            value={form.city}
            onChange={(e) => set("city", e.target.value)}
            placeholder="New York"
            className={errors.city ? "border-red-500" : ""}
          />
          {errors.city && (
            <p className="text-xs text-red-500">{errors.city}</p>
          )}
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="state">State / Province</Label>
          <Input
            id="state"
            value={form.state}
            onChange={(e) => set("state", e.target.value)}
            placeholder="NY"
            className={errors.state ? "border-red-500" : ""}
          />
          {errors.state && (
            <p className="text-xs text-red-500">{errors.state}</p>
          )}
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="zip">ZIP / Postal Code</Label>
          <Input
            id="zip"
            value={form.zip}
            onChange={(e) => set("zip", e.target.value)}
            placeholder="10001"
            className={errors.zip ? "border-red-500" : ""}
          />
          {errors.zip && (
            <p className="text-xs text-red-500">{errors.zip}</p>
          )}
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="country">Country</Label>
          <Input
            id="country"
            value={form.country}
            onChange={(e) => set("country", e.target.value)}
            placeholder="United States"
            className={errors.country ? "border-red-500" : ""}
          />
          {errors.country && (
            <p className="text-xs text-red-500">{errors.country}</p>
          )}
        </div>
      </div>

      <div className="flex gap-3">
        <Button variant="outline" onClick={onBack} className="flex-1">
          Back
        </Button>
        <Button onClick={handleSubmit} className="flex-1">
          Continue to Payment
        </Button>
      </div>
    </div>
  );
}
