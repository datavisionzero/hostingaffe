import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
// From vitest rather than vite, so that the test section below is typed too.
import { defineConfig } from "vitest/config";

/**
 * Where the instance is (ADR 0002). Development runs the two toolchains side
 * by side (docs/codebase.md): Vite serves the SPA and forwards `/api` to the
 * instance, so that the application reaches the API at its own origin there as
 * well as in the image. One prefix, so a new endpoint is never a second place
 * to register it.
 */
const instance = "/api";

export default defineConfig({
  plugins: [react(), tailwindcss()],

  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },

  // A local `npm run build` lands where the server serves static files from, so
  // that one `dotnet run` gives the whole product. The image does the same in
  // two stages (deploy/Dockerfile).
  build: {
    outDir: "../Hostingaffe.Api/wwwroot",
    emptyOutDir: true,
  },

  server: {
    port: 5173,
    proxy: { [instance]: "http://localhost:5142" },
  },

  test: {
    environment: "jsdom",
    setupFiles: ["./src/shared/setupTests.tsx"],
  },
});
