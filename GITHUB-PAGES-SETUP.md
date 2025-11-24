# GitHub Pages Setup Guide

## Current Status

✅ **Jekyll Configuration Complete**
- `docs/_config.yml` - Jekyll site configuration
- `docs/index.md` - Homepage
- Front matter added to all documentation files
- Theme: `jekyll-theme-cayman`
- Navigation structure configured

## Steps to Enable GitHub Pages

### 1. Push Changes to GitHub

```bash
# If not already pushed
git push origin dev

# Or merge dev to main/master if that's your deployment branch
git checkout main
git merge dev
git push origin main
```

### 2. Enable GitHub Pages in Repository Settings

1. Go to: `https://github.com/thorium/FSharp.Azure.Quantum/settings/pages`

2. Under "Build and deployment":
   - **Source**: Select "Deploy from a branch"
   - **Branch**: Select `main` (or `dev`) 
   - **Folder**: Select `/docs`
   - Click **Save**

3. GitHub will automatically build and deploy your site

### 3. Access Your Documentation

After a few minutes (first build takes ~2-5 minutes), your documentation will be available at:

```
https://thorium.github.io/FSharp.Azure.Quantum/
```

### Documentation URLs:

- **Homepage**: `https://thorium.github.io/FSharp.Azure.Quantum/`
- **Getting Started**: `https://thorium.github.io/FSharp.Azure.Quantum/getting-started`
- **API Reference**: `https://thorium.github.io/FSharp.Azure.Quantum/api-reference`
- **Examples**: `https://thorium.github.io/FSharp.Azure.Quantum/examples/tsp-example`
- **FAQ**: `https://thorium.github.io/FSharp.Azure.Quantum/faq`

## Verify Deployment

### Check Build Status:
1. Go to repository **Actions** tab
2. Look for "pages build and deployment" workflow
3. Green checkmark = successful deployment
4. Red X = build failed (check logs)

### Common Issues:

**Issue**: 404 on GitHub Pages URL
- **Solution**: Wait 2-5 minutes for first deployment
- **Solution**: Check that GitHub Pages is enabled in settings
- **Solution**: Verify branch and folder are correct

**Issue**: Site looks unstyled
- **Solution**: Check `_config.yml` baseurl matches repo name
- **Solution**: Verify theme is specified correctly

**Issue**: Links don't work
- **Solution**: Check that all internal links are relative (no leading `/`)
- **Solution**: Verify front matter is present in all `.md` files

## Customization

### Change Theme

Edit `docs/_config.yml`:

```yaml
# Available GitHub-supported themes:
theme: jekyll-theme-cayman        # Current (recommended)
# theme: jekyll-theme-minimal
# theme: jekyll-theme-slate
# theme: jekyll-theme-architect
# theme: jekyll-theme-time-machine
```

### Add Custom Navigation

Edit `docs/_config.yml` navigation section:

```yaml
navigation:
  - title: Your Title
    url: /your-page
```

### Add Custom Styling

Create `docs/assets/css/style.scss`:

```scss
---
---

@import "{{ site.theme }}";

/* Your custom CSS here */
.custom-class {
  color: #0366d6;
}
```

## Alternative: GitHub Actions Workflow

For more control (e.g., automated API doc generation from XML comments), create `.github/workflows/docs.yml`:

```yaml
name: Deploy Documentation

on:
  push:
    branches: [ main ]
    paths:
      - 'docs/**'
      - 'src/**'

permissions:
  contents: read
  pages: write
  id-token: write

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup Pages
        uses: actions/configure-pages@v3
        
      - name: Build with Jekyll
        uses: actions/jekyll-build-pages@v1
        with:
          source: ./docs
          destination: ./_site
          
      - name: Upload artifact
        uses: actions/upload-pages-artifact@v2
        
      - name: Deploy to GitHub Pages
        uses: actions/deploy-pages@v2
```

## Maintenance

### Adding New Pages

1. Create `.md` file in `docs/` or subdirectory
2. Add Jekyll front matter:
   ```yaml
   ---
   layout: default
   title: Page Title
   ---
   ```
3. Add to navigation in `_config.yml` if desired
4. Commit and push

### Updating Existing Pages

1. Edit the `.md` file
2. Commit and push
3. GitHub automatically rebuilds (takes 1-2 minutes)

## Links

- **Repository**: https://github.com/thorium/FSharp.Azure.Quantum
- **GitHub Pages Docs**: https://docs.github.com/en/pages
- **Jekyll Docs**: https://jekyllrb.com/docs/
- **Supported Themes**: https://pages.github.com/themes/
