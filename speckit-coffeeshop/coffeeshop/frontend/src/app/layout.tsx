import type { Metadata } from "next";
import { Inter } from "next/font/google";
import { CopilotKit } from "@copilotkit/react-core";
import "@copilotkit/react-ui/styles.css";
import "./globals.css";

const inter = Inter({ subsets: ["latin"] });

export const metadata: Metadata = {
  title: "CoffeeShop",
  description: "Order your coffee with AI assistance",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body className={inter.className}>
        {/*
         * runtimeUrl MUST be the Next.js-relative path "/api/copilotkit".
         * The route.ts handler at that path forwards requests to the counter
         * service via process.env.COUNTER_URL (Aspire-injected).
         * Do NOT set runtimeUrl to COUNTER_URL directly — that URL is
         * server-side only and bypasses the Next.js SSE bridge.
         *
         * agent MUST match the name passed to new GitHubCopilotAgent(...)
         * in Program.cs. Without it, CopilotKit throws
         * "No default agent provided" during discovery.
         */}
        <CopilotKit runtimeUrl="/api/copilotkit" agent="CoffeeShopCounter">
          {children}
        </CopilotKit>
      </body>
    </html>
  );
}
