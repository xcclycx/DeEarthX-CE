import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite"; // 导入 v4 插件

export default defineConfig({
  // 这里的插件顺序很重要，把 tailwindcss 放在 react 前面
  plugins: [tailwindcss(), react()],

  // Tauri 相关配置
  clearScreen: false,
  server: {
    port: 1420,
    strictPort: true,
    host: true,
    watch: {
      ignored: ["**/src-tauri/**"],
    },
  },
});
