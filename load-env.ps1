<#
.SYNOPSIS
  Charge les variables APS_* du fichier .env dans la session PowerShell courante.
.DESCRIPTION
  À exécuter "dot-sourcé" pour que les variables restent dans la session :
      . .\load-env.ps1
  Les valeurs ne sont jamais affichées. Les lignes vides, les commentaires (#) et les valeurs vides sont ignorés.
  Pour retirer les variables de la session : . .\load-env.ps1 -Clear
#>
param(
    [string]$Path = (Join-Path $PSScriptRoot '.env'),
    [switch]$Clear
)

$known = 'APS_CLIENT_ID', 'APS_CLIENT_SECRET', 'APS_NICKNAME', 'APS_BUCKET', 'APS_TOKEN', 'APS_DA_BASE_URL'

if ($Clear) {
    foreach ($name in $known) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
    Write-Host 'Variables APS_* retirées de la session.'
    return
}

if (-not (Test-Path -LiteralPath $Path)) {
    Write-Error "Fichier introuvable : $Path (copier .env.example en .env puis le renseigner)."
    return
}

$loaded = @()
foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8) {
    $text = $line.Trim()
    if ($text -eq '' -or $text.StartsWith('#')) { continue }

    $separator = $text.IndexOf('=')
    if ($separator -lt 1) { continue }

    $name = $text.Substring(0, $separator).Trim()
    $value = $text.Substring($separator + 1).Trim()
    if ($value.Length -ge 2 -and (($value[0] -eq '"' -and $value[-1] -eq '"') -or ($value[0] -eq "'" -and $value[-1] -eq "'"))) {
        $value = $value.Substring(1, $value.Length - 2)
    }

    if ($known -notcontains $name) {
        Write-Warning "Variable ignorée (non reconnue) : $name"
        continue
    }

    if ($value -eq '') { continue }

    Set-Item -Path "Env:$name" -Value $value
    $loaded += $name
}

Write-Host ("Variables chargées : " + ($(if ($loaded.Count) { $loaded -join ', ' } else { 'aucune (valeurs vides ?)' })))
