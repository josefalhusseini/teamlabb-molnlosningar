@echo off
echo [*] Skapar commit och pushar till GitHub main...
git commit --allow-empty -m "Live demo: release till Container Apps"
git push origin main
echo.
echo ========================================================
echo [OK] PUSH KLAR! Ga till Azure DevOps nu!
echo ========================================================
