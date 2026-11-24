import { defineConfig, loadEnv } from 'vite';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', '');
  const proxy = {
    '/api': {
      target: env.VITE_API_URL || 'https://localhost:7076',
      changeOrigin: true,
      secure: false
    }
  };
  return { server: { port: 5173, strictPort: true, proxy }, preview: { proxy } };
});
