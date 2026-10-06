$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot '..\out\preview') | Out-Null
Set-Location (Join-Path $PSScriptRoot '..')

foreach ($f in 25, 200, 330) {
  npx remotion still src/index.jsx AirGlassPromo "out\preview\new_$f.png" "--frame=$f" 2>&1 | Select-Object -Last 1
}

Get-ChildItem out\preview\new_*.png | Select-Object Name, Length