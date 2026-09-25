import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The production build is served by Embroidery.Host from its wwwroot.
// In development, /api is proxied to the host (changeOrigin keeps the Host header on localhost).
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../../src/Embroidery.Host/wwwroot",
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      "/api": { target: "http://localhost:5170", changeOrigin: true },
    },
  },
});
