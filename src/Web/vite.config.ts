import { copyFileSync, existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { URL, fileURLToPath } from "node:url";

import tailwindcss from "@tailwindcss/vite";
import vue from "@vitejs/plugin-vue";
import { defineConfig, loadEnv } from "vite";

const pkg = JSON.parse(readFileSync("./package.json", "utf-8"));

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd());
  const rootDir = fileURLToPath(new URL(".", import.meta.url));
  const outDir = env.VITE_BUILD_OUT_DIR ?? "../Api/wwwroot";

  return {
    // Relative asset URLs, so they resolve against the <base href> in index.html rather
    // than against the server root. Deployments under a sub-path depend on it.
    base: "./",
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
      {
        // The one file the build needs from outside src/Web: the repository's own
        // LICENSE.txt, so the footer's License link has something to serve without a
        // second copy of the license text living in the frontend tree to drift out of
        // sync. Two directories up is the repository root in the source tree; the
        // Dockerfile stages the same file at the matching path for the containerized
        // build, where that nesting doesn't otherwise exist.
        name: "copy-license",
        apply: "build",
        closeBundle() {
          const licensePath = path.resolve(rootDir, "../../LICENSE.txt");
          if (!existsSync(licensePath)) {
            this.warn(
              `LICENSE.txt not found at ${licensePath}; the footer's License link will 404.`,
            );
            return;
          }
          // resolve, not join: VITE_BUILD_OUT_DIR may be absolute (env.VITE_BUILD_OUT_DIR
          // above), and join would wrongly nest it under rootDir instead of replacing it.
          copyFileSync(licensePath, path.resolve(rootDir, outDir, "LICENSE.txt"));
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
      outDir,
      emptyOutDir: true,
    },
  };
});
