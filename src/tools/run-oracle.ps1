param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$envRoot = Join-Path $root 'artifacts\oracle-venv'
$python = Join-Path $envRoot 'Scripts\python.exe'
Push-Location $root
try {
    if (-not (Test-Path $python)) {
        python -m venv $envRoot
        if ($LASTEXITCODE -ne 0) { throw 'Virtual environment creation failed' }
    }
    & $python -m pip install -r src/tools/oracle-requirements.txt
    if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed' }
    $env:PYTHONDONTWRITEBYTECODE = '1'
    if ($CheckOnly) { & $python -B src/tools/check_oracle_environment.py }
    else { & $python -B src/tools/extract_corpus.py }
    if ($LASTEXITCODE -ne 0) { throw 'Oracle verification failed; see recorded output and log' }
} finally { Pop-Location }
