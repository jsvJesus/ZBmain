$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $root 'ZoneReborn.sln'
$bin = Join-Path $root 'bin'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Не установлен .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0'
}

dotnet build $solution --configuration Release -p:Platform=x64 -p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) {
    throw 'Сборка завершилась ошибкой.'
}

$artifacts = @('Editor.exe', 'Game.exe', 'Server.exe', 'Core.dll')
foreach ($artifact in $artifacts) {
    $path = Join-Path $bin $artifact
    if (-not (Test-Path $path)) {
        throw "Не найден файл сборки: $path"
    }
}

Write-Host "Сборка завершена: $bin" -ForegroundColor Green
