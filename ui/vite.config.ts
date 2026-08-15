import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  // 发布模式下 WebView2 以 file:// 直载 wwwroot/index.html，
  // 绝对路径 /assets/... 会解析到盘符根目录导致白屏，必须用相对路径
  base: './',
  plugins: [react()],
})
