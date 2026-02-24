"use client";

import { useState } from "react";
import { StepIndicator } from "./StepIndicator";
import { CartStep1 } from "./CartStep1";
import { CartStep2, type ShippingData } from "./CartStep2";
import { CartStep3 } from "./CartStep3";

const EMPTY_SHIPPING: ShippingData = {
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

export function CheckoutWizard() {
  const [step, setStep] = useState<1 | 2 | 3 | "success">(1);
  const [shippingData, setShippingData] = useState<ShippingData>(EMPTY_SHIPPING);

  const stepNum = step === "success" ? 3 : step;

  return (
    <div className="min-h-screen bg-white">
      <div className="max-w-2xl mx-auto px-6 py-10">
        <h1 className="text-2xl font-semibold text-foreground mb-8 text-center">
          {step === 1 && "Your Cart"}
          {step === 2 && "Shipping Information"}
          {step === 3 && "Payment"}
          {step === "success" && "Order Confirmed"}
        </h1>

        <StepIndicator step={step === "success" ? 3 : step} />

        {step === 1 && <CartStep1 onNext={() => setStep(2)} />}
        {step === 2 && (
          <CartStep2
            initialData={shippingData}
            onNext={(data) => {
              setShippingData(data);
              setStep(3);
            }}
            onBack={() => setStep(1)}
          />
        )}
        {(step === 3 || step === "success") && (
          <CartStep3 onBack={() => setStep(2)} />
        )}
      </div>
    </div>
  );
}
