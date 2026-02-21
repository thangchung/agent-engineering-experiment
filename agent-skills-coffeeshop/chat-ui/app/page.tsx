"use client";

import { CopilotChat } from "@copilotkit/react-core/v2";

export default function Page() {
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100vh",
        background: "#faf6f1",
      }}
    >
      {/* Header */}
      <header
        style={{
          padding: "12px 24px",
          background: "#4a2f1a",
          color: "#fff",
          display: "flex",
          alignItems: "center",
          gap: "10px",
          flexShrink: 0,
        }}
      >
        <span style={{ fontSize: "1.5rem" }}>☕</span>
        <span style={{ fontSize: "1.2rem", fontWeight: 600 }}>Coffee Shop</span>
      </header>

      {/* Full-screen chat */}
      <div style={{ flex: 1, overflow: "hidden" }}>
        <CopilotChat
          className="h-full"
          style={{ borderRadius: 0 }}
        />
      </div>
    </div>
  );
}
