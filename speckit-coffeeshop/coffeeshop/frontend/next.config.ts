import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Disable React Strict Mode to prevent CopilotKit subscriptions from being
  // registered twice in dev (StrictMode double-mounts every component).
  // Strict Mode is on by default in Next.js 13+; the double-mount causes each
  // message to appear duplicated because the SDK registers its event handler
  // twice before the first cleanup runs.
  reactStrictMode: false,
  // Keep the CopilotKit server-side runtime out of the browser bundle and out
  // of Next.js's module-splitting so the singleton instance is never cloned.
  serverExternalPackages: ["@copilotkit/runtime"],
};

export default nextConfig;
