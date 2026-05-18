import type { Config } from "tailwindcss";

const config: Config = {
  content: ["./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        ink: {
          950: "#0a0a0f",
          900: "#0e0f15",
          800: "#171821",
          700: "#222432",
          600: "#2f3142",
        },
        brand: {
          50:  "#eef7ff",
          100: "#dbeeff",
          200: "#b5dbff",
          400: "#5fa7ff",
          500: "#3b86ff",
          600: "#1e63e6",
          700: "#1750bd",
        },
        good: { 500: "#16a34a" },
        warn: { 500: "#f59e0b" },
        bad:  { 500: "#ef4444" },
      },
      fontFamily: {
        sans: ["Inter", "ui-sans-serif", "system-ui", "sans-serif"],
      },
    },
  },
  plugins: [],
};

export default config;
