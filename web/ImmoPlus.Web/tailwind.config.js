/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ["./**/*.razor", "./wwwroot/index.html"],
  theme: {
    extend: {
      colors: {
        'immo-dark': '#181818',
        'immo-gold': '#D4A94A',
        'immo-cream': '#F0EDE4',
      },
    },
  },
  corePlugins: {
    preflight: false,
  },
  plugins: [],
}
