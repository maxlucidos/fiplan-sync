# Gera em dist\:
#   FIPLAN Sync.exe          o programa (app de bandeja + comandos), com o driver do Oracle e o SQL embutidos
#   fiplan-sync.ico          ícone do programa e do instalador
#   FIPLAN-Sync-Setup.exe    instalador (Inno Setup) que é entregue ao usuário
#
# Nenhuma credencial vai embutida: usuário e senha do FIPLAN e chave do Supabase são
# informados no instalador e guardados criptografados no computador de quem usa.
#
# Uso: powershell -ExecutionPolicy Bypass -File build.ps1   (precisa do Inno Setup 6.3+)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup não encontrado. Instale com: winget install JRSoftware.InnoSetup' }

if (Test-Path dist) {
    try { Remove-Item -Recurse -Force dist }
    catch { throw 'Não consegui limpar a pasta dist: feche o instalador ou o FIPLAN Sync que estiver aberto a partir dela.' }
}
New-Item -ItemType Directory dist | Out-Null

function Compilar([string[]] $extras) {
    $fontes = Get-ChildItem src\*.cs | ForEach-Object { $_.FullName }
    & $csc /nologo /target:winexe '/out:dist\FIPLAN Sync.exe' /codepage:65001 /optimize `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Web.Extensions.dll `
        /r:System.Net.Http.dll /r:lib\Oracle.ManagedDataAccess.dll `
        '/resource:lib\Oracle.ManagedDataAccess.dll,Oracle.ManagedDataAccess.dll' '/resource:sql\supabase.sql,supabase.sql' `
        @extras @fontes
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar' }
}

Write-Host '1. Compilando o programa...'
# Primeiro sem ícone, para o próprio programa desenhar o .ico; depois de novo, já com ele
Compilar @()
& 'dist\FIPLAN Sync.exe' --gerar-icone 'dist\fiplan-sync.ico' | Out-Null
Compilar @('/win32icon:dist\fiplan-sync.ico')

Write-Host '2. Gerando o instalador...'
& $iscc /Q instalador\setup.iss
if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar o instalador' }

Write-Host "`nPronto: dist\FIPLAN-Sync-Setup.exe"
