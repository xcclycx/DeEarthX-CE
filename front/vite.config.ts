import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import tailwindcss from "@tailwindcss/vite";
import { resolve } from "path";

// @ts-expect-error process is a nodejs global
const host = process.env.TAURI_DEV_HOST;

// https://vite.dev/config/
export default defineConfig(async () => ({
  plugins: [vue(),
    tailwindcss()
  ],

  // 路径别名配置
  resolve: {
    alias: {
      "@": resolve(__dirname, "src")
    }
  },

  // 构建优化
  build: {
    // 代码分割
    rollupOptions: {
      output: {
        manualChunks: {
          // 将第三方库单独打包
          vendor: ['vue', 'vue-router', 'vue-i18n'],
          // 将 Ant Design Vue 单独打包
          ant: ['ant-design-vue', '@ant-design/icons-vue'],
          // 将网络请求库单独打包
          network: ['axios', 'socket.io-client'],
          // Tauri API 单独打包
          tauri: ['@tauri-apps/api', '@tauri-apps/plugin-shell', '@tauri-apps/plugin-notification', '@tauri-apps/plugin-dialog', '@tauri-apps/plugin-opener', '@tauri-apps/plugin-store'],
        }
      }
    },
    // 启用压缩
    minify: 'esbuild',
    // 启用 CSS 代码分割
    cssCodeSplit: true,
    // 生成源映射文件
    sourcemap: false,
    // 资源内联阈值
    assetsInlineLimit: 4096,
    // 分块大小警告阈值
    chunkSizeWarningLimit: 600,
  },

  // 缓存策略
  optimizeDeps: {
    include: ['vue', 'vue-router', 'vue-i18n', 'ant-design-vue', '@ant-design/icons-vue', 'axios', 'socket.io-client'],
    exclude: ['@tauri-apps/api']
  },

  // Vite options tailored for Tauri development and only applied in `tauri dev` or `tauri build`
  //
  // 1. prevent Vite from obscuring rust errors
  clearScreen: false,
  // 2. tauri expects a fixed port, fail if that port is not available
  server: {
    port: 9888,
    strictPort: true,
    host: host || false,
    hmr: host
      ? {
          protocol: "ws",
          host,
          port: 1421,
        }
      : undefined,
    watch: {
      // 3. tell Vite to ignore watching `src-tauri`
      ignored: ["**/src-tauri/**"],
    },
  },
}));
