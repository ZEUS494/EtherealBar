param(
  [Parameter(Mandatory = $false)]
  [string]$Owner = "ZEUS494",

  [Parameter(Mandatory = $false)]
  [string]$Repo = "EtherealBar",

  [Parameter(Mandatory = $false)]
  [string]$Tag = "v1.2",

  [Parameter(Mandatory = $false)]
  [string]$NotesPath = "$(Join-Path $PSScriptRoot '..\..\docs\release-notes\v1.2.md')",

  [Parameter(Mandatory = $false)]
  [string]$KeepAssetRegex = "\.exe$",

  [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function Get-GitHubToken {
  if ($env:GITHUB_TOKEN -and $env:GITHUB_TOKEN.Trim().Length -gt 0) {
    return $env:GITHUB_TOKEN.Trim()
  }

  $t = Read-Host "Р’РІРµРґРёС‚Рµ GITHUB_TOKEN (РЅСѓР¶РЅС‹ РїСЂР°РІР° РЅР° СЂРµРїРѕР·РёС‚РѕСЂРёР№)"
  if (-not $t -or $t.Trim().Length -eq 0) {
    throw "GITHUB_TOKEN РЅРµ Р·Р°РґР°РЅ."
  }
  return $t.Trim()
}

function Invoke-GhApi {
  param(
    [Parameter(Mandatory = $true)][string]$Method,
    [Parameter(Mandatory = $true)][string]$Url,
    [Parameter(Mandatory = $false)]$Body,
    [Parameter(Mandatory = $true)][hashtable]$Headers
  )

  if ($null -eq $Body) {
    return Invoke-RestMethod -Method $Method -Uri $Url -Headers $Headers
  }

  $json = $Body | ConvertTo-Json -Depth 20
  return Invoke-RestMethod -Method $Method -Uri $Url -Headers $Headers -Body $json -ContentType "application/json"
}

$token = Get-GitHubToken
$apiBase = "https://api.github.com"

$headers = @{
  "Accept" = "application/vnd.github+json"
  "Authorization" = "Bearer $token"
  "X-GitHub-Api-Version" = "2022-11-28"
  "User-Agent" = "EtherealBar-ReleaseScript"
}

$releaseUrl = "$apiBase/repos/$Owner/$Repo/releases/tags/$Tag"
Write-Host "РџРѕР»СѓС‡Р°СЋ СЂРµР»РёР· РїРѕ С‚РµРіСѓ $Tag..."
$release = Invoke-GhApi -Method "GET" -Url $releaseUrl -Headers $headers

if ($null -eq $release -or $null -eq $release.id) {
  throw "РќРµ СѓРґР°Р»РѕСЃСЊ РїРѕР»СѓС‡РёС‚СЊ СЂРµР»РёР· РґР»СЏ С‚РµРіР° $Tag."
}

Write-Host "Р РµР»РёР· РЅР°Р№РґРµРЅ: id=$($release.id), name=$($release.name)"

# 1) РЈРґР°Р»СЏРµРј Р»РёС€РЅРёРµ Р°СЃСЃРµС‚С‹ (РѕСЃС‚Р°РІР»СЏРµРј С‚РѕР»СЊРєРѕ *.exe)
$assets = @()
if ($release.assets) { $assets = @($release.assets) }

if ($assets.Count -gt 0) {
  Write-Host "РџСЂРѕРІРµСЂСЏСЋ Р°СЃСЃРµС‚С‹ (РѕСЃС‚Р°РІР»СЏРµРј С‚РѕР»СЊРєРѕ РїРѕ regex: $KeepAssetRegex)..."
  foreach ($a in $assets) {
    $name = [string]$a.name
    $id = [int]$a.id

    if ($name -match $KeepAssetRegex) {
      Write-Host "OK  : $name"
      continue
    }

    Write-Host "DEL : $name"
    if (-not $DryRun) {
      $delUrl = "$apiBase/repos/$Owner/$Repo/releases/assets/$id"
      Invoke-GhApi -Method "DELETE" -Url $delUrl -Headers $headers | Out-Null
    }
  }
} else {
  Write-Host "РђСЃСЃРµС‚РѕРІ РЅРµС‚."
}

# 2) РћР±РЅРѕРІР»СЏРµРј С‚РµРєСЃС‚ СЂРµР»РёР·Р°
if (-not (Test-Path -LiteralPath $NotesPath)) {
  throw "Р¤Р°Р№Р» Р·Р°РјРµС‚РѕРє РЅРµ РЅР°Р№РґРµРЅ: $NotesPath"
}

$notes = Get-Content -LiteralPath $NotesPath -Raw -Encoding UTF8
if (-not $notes -or $notes.Trim().Length -eq 0) {
  throw "Р¤Р°Р№Р» Р·Р°РјРµС‚РѕРє РїСѓСЃС‚РѕР№: $NotesPath"
}

Write-Host "РћР±РЅРѕРІР»СЏСЋ РѕРїРёСЃР°РЅРёРµ СЂРµР»РёР·Р° РёР·: $NotesPath"
if (-not $DryRun) {
  $patchUrl = "$apiBase/repos/$Owner/$Repo/releases/$($release.id)"
  Invoke-GhApi -Method "PATCH" -Url $patchUrl -Headers $headers -Body @{ body = $notes } | Out-Null
}

Write-Host "Р“РѕС‚РѕРІРѕ."



