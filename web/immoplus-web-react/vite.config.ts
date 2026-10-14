import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    host: true,
    // Accès depuis un autre appareil via un tunnel VS Code (*.devtunnels.ms).
    allowedHosts: [".devtunnels.ms"],
    // L'API passe par le serveur du site (/api) : un seul port à exposer, en local comme via un tunnel.
    // Dans Docker, API_PROXY vaut http://api:5107 (nom du service) ; hors Docker, l'API tourne sur localhost:5107.
    proxy: {
      "/api": {
        target: process.env.API_PROXY ?? "http://localhost:5107",
        changeOrigin: true,
        // WebSockets de la messagerie instantanée (SignalR).
        ws: true,
        rewrite: (chemin) => chemin.replace(/^\/api/, ""),
      },
    },
    watch: {
      usePolling: true,
    },
  },
});
