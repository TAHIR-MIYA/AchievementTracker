# Achievement Tracker — Website

This is the official website for **Achievement Tracker**, a Windows application for tracking Steam achievements in real-time.

## Quick Deploy

### GitHub Pages (Recommended — Free)

1. Push this `website/` folder to your GitHub repository
2. Go to **Settings → Pages**
3. Source: **Deploy from a branch**
4. Branch: `main` / `website` folder (or root if you move files)
5. Save — your site will be live at `https://yourusername.github.io/AchievementTracker/`

### Netlify (Drag & Drop)

1. Go to [app.netlify.com/drop](https://app.netlify.com/drop)
2. Drag the `website/` folder onto the page
3. Instant deploy with custom domain support

### Vercel

```bash
npx vercel deploy website/
```

### Any Static Host

Upload the contents of `website/` to any static hosting:
- Cloudflare Pages
- Firebase Hosting
- AWS S3 + CloudFront
- Surge.sh
- Render Static Sites

## Project Structure

```
website/
├── index.html          # Main landing page (self-contained)
├── README.md           # This file
├── 404.html            # GitHub Pages SPA fallback
└── assets/             # Optional: place images, icons here
```

## Customization

### Update Links

Edit `index.html` and replace:
- `TAHIR-MIYA` → your GitHub username
- `tahir@example.com` → your contact email
- Version numbers in hero badge and download section

### Colors & Branding

The design uses CSS custom properties in `:root`. Modify these tokens:

```css
:root {
  --accent: #00d4aa;        /* Primary brand color */
  --accent-strong: #00ffcc; /* Hover state */
  --steam: #1b2838;         /* Steam brand color */
  --steam-accent: #66c0f4;  /* Steam accent */
  /* ... more tokens */
}
```

### Add Real Screenshots

Replace the placeholder gallery items with real screenshots:

```html
<article class="gallery-item">
  <img src="assets/library-view.png" alt="Library tab showing game list" loading="lazy">
  <div class="gallery-overlay">...</div>
</article>
```

Place images in `website/assets/` and reference them.

## Features Included

- ✅ Responsive (mobile-first, works at 375px)
- ✅ Dark/Light mode with system detection + manual toggle
- ✅ Persisted theme preference (localStorage)
- ✅ Semantic HTML5 + ARIA accessibility
- ✅ Smooth scroll navigation
- ✅ Keyboard navigable
- ✅ Reduced motion support
- ✅ Self-contained (no build step, no dependencies except Google Fonts)
- ✅ CSP-friendly (inline styles/scripts, only Google Fonts external)
- ✅ SEO-ready meta tags
- ✅ Open Graph / Twitter Card ready (add your image)

## Performance

- ~15 KB HTML (gzipped ~5 KB)
- No JavaScript frameworks
- Only external request: Google Fonts (preconnected)
- All CSS/JS inlined
- Optimized for Core Web Vitals

## License

MIT — same as the main project.