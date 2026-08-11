import { readFileSync } from "node:fs";
import { URL, fileURLToPath } from "node:url";

import tailwindcss from "@tailwindcss/vite";
import vue from "@vitejs/plugin-vue";
import { defineConfig, loadEnv } from "vite";

const pkg = JSON.parse(readFileSync("./package.json", "utf-8"));

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd());

  return {
    define: {
      __APP_VERSION__: JSON.stringify(pkg.version),
    },
    plugins: [
      vue(),
      tailwindcss(),
      {
        name: "markdown-raw",
        transform(code, id) {
          if (id.endsWith(".md")) return `export default ${JSON.stringify(code)}`;
        },
      },
    ],
    resolve: {
      alias: {
        "@": fileURLToPath(new URL("./src", import.meta.url)),
      },
    },
    server: {
      proxy: {
        "/api": {
          target: env.VITE_API_TARGET_URL,
          secure: false,
          changeOrigin: true,
          headers: {
            "Cache-Control": "no-store",
          },
        },
      },
    },
    build: {
      outDir: env.VITE_BUILD_OUT_DIR ?? "../Api/wwwroot",
      emptyOutDir: true,
    },
  };
});
