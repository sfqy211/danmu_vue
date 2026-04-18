<#
.SYNOPSIS
    Danmu Replay System - Quick Start Script
.DESCRIPTION
    Start frontend and backend services in separate terminal windows
#>

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Danmu Replay System - Starting" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Set-Location $PSScriptRoot

Write-Host "[1/2] Starting backend..." -ForegroundColor Yellow
Start-Process powershell -ArgumentList "-NoExit", "-Command", "npm run dev:server"

Start-Sleep -Seconds 2

Write-Host "[2/2] Starting frontend..." -ForegroundColor Yellow
Start-Process powershell -ArgumentList "-NoExit", "-Command", "npm run dev"

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Started!" -ForegroundColor Green
Write-Host "  Frontend: http://localhost:5200" -ForegroundColor White
Write-Host "  Backend:  http://localhost:3001" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Green
