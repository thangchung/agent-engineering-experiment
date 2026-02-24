import { cn } from "@/lib/utils";

const STEPS = ["Cart", "Shipping", "Payment"] as const;

interface StepIndicatorProps {
  step: 1 | 2 | 3 | "success";
}

export function StepIndicator({ step }: StepIndicatorProps) {
  const currentStep = step === "success" ? 3 : step;

  return (
    <div className="flex items-center justify-center gap-0 mb-8">
      {STEPS.map((label, idx) => {
        const num = idx + 1;
        const isCompleted = num < currentStep;
        const isCurrent = num === currentStep;

        return (
          <div key={label} className="flex items-center">
            <div className="flex flex-col items-center gap-1">
              <div
                className={cn(
                  "h-8 w-8 rounded-full flex items-center justify-center text-sm font-medium border-2 transition-colors",
                  isCompleted &&
                    "bg-foreground text-white border-foreground",
                  isCurrent &&
                    "bg-white text-foreground border-foreground ring-2 ring-foreground ring-offset-2",
                  !isCompleted &&
                    !isCurrent &&
                    "bg-white text-muted-foreground border-black/20"
                )}
              >
                {isCompleted ? "✓" : num}
              </div>
              <span
                className={cn(
                  "text-xs font-medium",
                  isCurrent ? "text-foreground" : "text-muted-foreground"
                )}
              >
                {label}
              </span>
            </div>
            {idx < STEPS.length - 1 && (
              <div
                className={cn(
                  "h-0.5 w-16 mx-2 mb-5 transition-colors",
                  num < currentStep ? "bg-foreground" : "bg-black/15"
                )}
              />
            )}
          </div>
        );
      })}
    </div>
  );
}
