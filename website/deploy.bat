@echo off
REM Quick deploy script for Achievement Tracker website
REM Usage: deploy.bat [github|netlify|vercel|preview]

set TARGET=%1
if "%TARGET%"=="" set TARGET=preview

if "%TARGET%"=="preview" (
    echo Starting local preview server...
    echo Opening http://localhost:8080
    npx serve website -l 8080
    goto :eof
)

if "%TARGET%"=="github" (
    echo GitHub Pages deployment:
    echo 1. Commit and push the website/ folder to your repo
    echo 2. Enable Pages in Settings -> Pages -> Deploy from branch (main, /website folder)
    echo 3. Your site will be at: https://YOURUSERNAME.github.io/REPO_NAME/
    goto :eof
)

if "%TARGET%"=="netlify" (
    echo Netlify deployment:
    echo Run: npx netlify-cli deploy --prod --dir=website
    echo Or drag & drop the website/ folder at: https://app.netlify.com/drop
    goto :eof
)

if "%TARGET%"=="vercel" (
    echo Vercel deployment:
    echo Run: npx vercel deploy website --prod
    goto :eof
)

echo Unknown target: %TARGET%
echo Usage: deploy.bat [github|netlify|vercel|preview]