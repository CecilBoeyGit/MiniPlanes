<#
.SYNOPSIS
  Generate one MiniPlanes model (or all) from Tools/Tripo/assets.json with the Tripo CLI (API credits).

.DESCRIPTION
  The CLI bills the account's API credit balance, which is separate from the website's web credits.
  Downloads land in Tools/Tripo/runs/<Id>/. Nothing is copied into Assets here: run
  Tools/Tripo/install_tripo.py to convert a result into the contract path (see README.md).

  -DryRun prints the exact command and runs the CLI's own offline validation
  (tripo make ... --dry-run --json: no network, no credits, no API key needed).

  The exit code of a real run is the CLI's exit code: 0 ok, 2 params, 3 auth, 4 credits, 5 content,
  6 failed or timeout, 7 network, 9 rate limit.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -List
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -Id all -DryRun
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Tripo\generate.ps1 -Id PlayerPlane
#>
[CmdletBinding()]
param(
    [string]$Id,
    [switch]$DryRun,
    [switch]$Force,
    [switch]$List
)

$ErrorActionPreference = 'Stop'
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path (Join-Path $ScriptDir '..\..')).Path
$Manifest    = Join-Path $ScriptDir 'assets.json'

$env:TRIPO_CLIENT_APP      = 'claude'
$env:TRIPO_NO_UPDATE_CHECK = '1'

$m = Get-Content -Raw -Encoding UTF8 $Manifest | ConvertFrom-Json

function Get-TripoCli {
    $cmd = Get-Command tripo.cmd -ErrorAction SilentlyContinue
    $candidates = @()
    if ($cmd) { $candidates += (Join-Path (Split-Path -Parent $cmd.Source) 'node_modules\tripo-cli\dist\cli.js') }
    try { $root = (& npm.cmd root -g 2>$null); if ($root) { $candidates += (Join-Path $root.Trim() 'tripo-cli\dist\cli.js') } } catch {}
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw 'tripo-cli not found. Install it with: npm install -g tripo-cli'
}

function Format-Cmd([string[]]$a) {
    $parts = foreach ($x in $a) { if ($x -match '[\s,;]') { '"' + $x + '"' } else { $x } }
    'tripo ' + ($parts -join ' ')
}

function Get-Val($obj, [string]$name, $default) {
    if ($null -ne $obj -and $obj.PSObject.Properties[$name] -and $null -ne $obj.$name -and "$($obj.$name)" -ne '') { return $obj.$name }
    return $default
}

function Build-Args($a, [string]$runDir) {
    $d = $m.defaults
    $list = New-Object System.Collections.Generic.List[string]
    $list.Add('make')
    $image = Get-Val $a 'input_image' $null
    if ($image) {
        $path = Join-Path $ProjectRoot $image
        if (-not (Test-Path $path)) { throw "$($a.id): reference image missing: $path (run Tools/Art/crop_refs.py)" }
        $list.Add($path)
    } else {
        $prompt = "$($a.prompt.Trim()), $($m.style.prompt_suffix)"
        if ($prompt.Length -gt 1024) { throw "$($a.id): prompt is $($prompt.Length) chars (max 1024)" }
        if ($prompt -match '["&|<>^%]') { throw "$($a.id): prompt contains a forbidden character" }
        $list.Add($prompt)
    }
    $list.Add('--model'); $list.Add((Get-Val $a 'model' $d.model))
    $list.Add('-p'); $list.Add("face_limit=$($a.face_limit)")
    $list.Add('-p'); $list.Add("texture_quality=$(Get-Val $a 'texture_quality' $d.texture_quality)")
    if (-not $image) {
        $neg = @($m.style.negative_base, (Get-Val $a 'negative_prompt' '')) | Where-Object { $_ }
        $negText = ($neg -join ', ')
        if ($negText.Length -gt 255) { throw "$($a.id): negative prompt is $($negText.Length) chars (max 255)" }
        $list.Add('-p'); $list.Add("negative_prompt=$negText")
    }
    $list.Add('--then'); $list.Add("convert:format=$($d.static_chain.format)")
    $list.Add('--name'); $list.Add('mp-' + $a.id.ToLower())
    $list.Add('-o'); $list.Add($runDir)
    $list.Add('--json'); $list.Add('--yes'); $list.Add('--no-open')
    return ,$list.ToArray()
}

function Invoke-Tripo([string]$cli, [string[]]$cliArgs) {
    # stderr (plan card, progress) goes straight to the console; stdout is the JSON contract.
    $out = & node $cli @cliArgs
    $code = $LASTEXITCODE
    $line = @($out | Where-Object { $_ -and $_.Trim().StartsWith('{') }) | Select-Object -Last 1
    $obj = $null
    if ($line) { try { $obj = $line | ConvertFrom-Json } catch { $obj = $null } }
    return [pscustomobject]@{ code = $code; json = $obj; raw = ($out -join "`n") }
}

if ($List) {
    foreach ($a in $m.assets) {
        "{0,-14} faces {1,-6} input: {2}" -f $a.id, $a.face_limit, (Get-Val $a 'input_image' 'text prompt')
    }
    exit 0
}
if (-not $Id) { throw 'Pass -Id <AssetName|all> (or -List).' }

$cli = Get-TripoCli
$targets = if ($Id -eq 'all') { @($m.assets) } else { @($m.assets | Where-Object { $_.id -eq $Id }) }
if ($targets.Count -eq 0) { throw "Unknown asset id '$Id'. Use -List." }
$exitCode = 0

foreach ($a in $targets) {
    Write-Host ''
    Write-Host "=== $($a.id) ===" -ForegroundColor Cyan
    $runDir = Join-Path $ProjectRoot (Join-Path $m.staging_root $a.id)
    $statePath = Join-Path $runDir 'state.json'
    if ((Test-Path $statePath) -and -not $Force -and -not $DryRun) {
        Write-Host "already generated (see $statePath); skipping. -Force starts a new paid task."
        continue
    }
    $cliArgs = Build-Args $a $runDir
    if ($DryRun) {
        Write-Host ('command: ' + (Format-Cmd $cliArgs))
        $r = Invoke-Tripo $cli ($cliArgs + @('--dry-run'))
        if ($r.json) {
            Write-Host ("cli validation: valid={0} model={1} generation_credits_estimate={2}" -f $r.json.valid, $r.json.model, $r.json.generation_credits_estimate)
            foreach ($w in @($r.json.warnings)) { if ($w) { Write-Host "  warning: $w" -ForegroundColor Yellow } }
            foreach ($e in @($r.json.errors))   { if ($e) { Write-Host "  error: $e" -ForegroundColor Red; $exitCode = 2 } }
        } else {
            Write-Host "cli validation failed (exit $($r.code)): $($r.raw)" -ForegroundColor Red
            $exitCode = 2
        }
        continue
    }
    New-Item -ItemType Directory -Force $runDir | Out-Null
    $r = Invoke-Tripo $cli $cliArgs
    [System.IO.File]::WriteAllText((Join-Path $runDir 'result.json'), $r.raw, (New-Object System.Text.UTF8Encoding($false)))
    if ($r.code -ne 0 -or -not $r.json -or $r.json.status -ne 'success') {
        Write-Host "FAILED (exit $($r.code)). Saved $(Join-Path $runDir 'result.json')" -ForegroundColor Red
        if ($r.code -eq 4) { Write-Host 'Insufficient API credits. Topping up is the account owner''s decision.' }
        $exitCode = $r.code
        break
    }
    $state = [pscustomobject]@{
        status = 'success'; task_id = $r.json.task_id; credits_consumed = $r.json.credits_consumed
        model_file = $r.json.model_file; generated_at = (Get-Date).ToString('s')
    }
    [System.IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("done: task {0}, credits consumed {1}, file {2}" -f $r.json.task_id, $r.json.credits_consumed, $r.json.model_file) -ForegroundColor Green
}
exit $exitCode
