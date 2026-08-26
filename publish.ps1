# Publish Bookkeeping as a single-file, self-contained win-x64 executable.
#
# Usage:
#   .\publish.ps1
#
# Output: src/Bookkeeping.Wpf/bin/Release/net10.0-windows/win-x64/publish/Bookkeeping.exe
#
# The output is a single self-contained .exe (no .NET runtime needed on the
# target machine). Copy that one file to the target laptop.

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "src\Bookkeeping.Wpf\Bookkeeping.Wpf.csproj"

Write-Host "Publishing Bookkeeping (Release, win-x64, self-contained, single-file)..." -ForegroundColor Cyan

dotnet publish $project -c Release -p:PublishProfile=win-x64

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish FAILED with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

$outputDir = Join-Path $PSScriptRoot "src\Bookkeeping.Wpf\bin\Release\net10.0-windows\win-x64\publish"
$exe = Join-Path $outputDir "Bookkeeping.exe"

# Remove leftover .pdb files from referenced projects so the output is a
# single clean deliverable (the exe is fully self-contained).
Get-ChildItem -Path $outputDir -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force

if (Test-Path $exe) {
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "Publish succeeded." -ForegroundColor Green
    Write-Host "Output: $exe ($size MB)"
} else {
    Write-Host "Publish completed but Bookkeeping.exe was not found at expected path." -ForegroundColor Yellow
    Write-Host "Look in: $outputDir"
}