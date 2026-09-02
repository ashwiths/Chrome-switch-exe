/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./popup.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        dark: {
          base: '#090d16',
          surface: '#111726',
          card: 'rgba(22, 30, 46, 0.75)',
          hover: 'rgba(30, 41, 64, 0.85)',
          active: 'rgba(16, 185, 129, 0.08)',
        }
      },
      boxShadow: {
        glow: '0 0 15px rgba(56, 189, 248, 0.25)',
        'glow-emerald': '0 0 15px rgba(16, 185, 129, 0.35)',
      }
    },
  },
  plugins: [],
}
