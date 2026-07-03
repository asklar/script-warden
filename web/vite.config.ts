import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";

// Produces a single self-contained index.html (JS/CSS inlined) that the AOT exe embeds and serves.
export default defineConfig({
    plugins: [react(), viteSingleFile()],
    base: "./",
    build: {
        outDir: "dist",
        assetsInlineLimit: 100000000,
        cssCodeSplit: false,
        reportCompressedSize: false,
        chunkSizeWarningLimit: 100000,
    },
    server: {
        // For `npm run dev`, proxy API calls to a running `script-warden serve`.
        proxy: {
            "/api": "http://127.0.0.1:8787",
        },
    },
});
