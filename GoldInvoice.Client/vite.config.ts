import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  base: "./",
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: false,
    rollupOptions: {
      onwarn(warning, defaultHandler) {
        const isKnownSignalRAnnotation =
          warning.code === "INVALID_ANNOTATION" &&
          warning.id?.includes("@microsoft/signalr/dist/esm/Utils.js");
        if (isKnownSignalRAnnotation) return;
        defaultHandler(warning);
      },
    },
  },
});
