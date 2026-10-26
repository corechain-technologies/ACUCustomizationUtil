<#
.SYNOPSIS
  Black-box regression for configuration precedence: acu.json < acu.json.user < command line,
  with defaults applied once to the merged result.

.DESCRIPTION
  Runs `acu config show` (local only: no target contact, no credentials) against synthetic
  fixtures in a fresh subdirectory of -Scratch, and asserts the effective values.
  Exit code 0 = all cases passed, 1 = a case failed, 2 = bad arguments.

.EXAMPLE
  ./Test-ConfigMerge.ps1 -Acu C:\src\ACUCustomizationUtil\src\ACUCustomizationUtil\bin\Debug\net10.0\acu.exe -Scratch $env:TEMP
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Acu,
    [Parameter(Mandatory)] [string] $Scratch
)
$ErrorActionPreference = 'Stop'
# Works on Windows PowerShell 5.1 (no Path.IsPathFullyQualified) and PowerShell 7.
function Test-Absolute([string] $p) { $p -match '^(?:[A-Za-z]:[\\/]|\\\\|/)' }
if (-not (Test-Absolute $Acu) -or -not (Test-Path -LiteralPath $Acu -PathType Leaf)) {
    Write-Error 'Pass the absolute path of a built acu.exe or acu.dll.'; exit 2
}
if (-not (Test-Absolute $Scratch) -or -not (Test-Path -LiteralPath $Scratch -PathType Container)) {
    Write-Error 'Pass an existing absolute scratch directory.'; exit 2
}
$root = Join-Path $Scratch ('config-merge-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null

$env:ACU_CONSOLE_ONLY = 'true'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:DOTNET_SYSTEM_GLOBALIZATION_INVARIANT = '1'

function Invoke-ConfigShow([string] $dir, [string] $base, [string] $user) {
    $basePath = Join-Path $dir 'acu.json'; $userPath = Join-Path $dir 'acu.json.user'
    if ($null -ne $base) { Set-Content -LiteralPath $basePath -Value $base -Encoding utf8 }
    if ($null -ne $user) { Set-Content -LiteralPath $userPath -Value $user -Encoding utf8 }
    $exe, $pre = if ($Acu.EndsWith('.dll')) { 'dotnet', @($Acu) } else { $Acu, @() }
    Push-Location $dir
    try { $out = & $exe @pre --config $basePath --user-config $userPath config show 2>&1 | Out-String }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "acu config show exited $LASTEXITCODE" }
    $start = $out.IndexOf('{')
    return ($out.Substring($start) | ConvertFrom-Json)
}

$cases = @(
    @{ name = 'base site values survive a user overlay without site'
       base = '{"site":{"instanceName":"X","iisAppPool":"BasePool","sqlServerName":"BaseSql","iisWebSite":"BaseSite"}}'
       user = '{"pkg":{"url":"http://synthetic/"}}'
       expect = @{ 'site.iisAppPool' = 'BasePool'; 'site.sqlServerName' = 'BaseSql'; 'site.iisWebSite' = 'BaseSite' } },
    @{ name = 'user overlay overrides base'
       base = '{"site":{"instanceName":"X","iisAppPool":"BasePool","sqlServerName":"BaseSql"}}'
       user = '{"site":{"iisAppPool":"UserPool","sqlServerName":"UserSql"}}'
       expect = @{ 'site.iisAppPool' = 'UserPool'; 'site.sqlServerName' = 'UserSql' } },
    @{ name = 'user-only site values apply'
       base = '{"site":{"instanceName":"X"}}'
       user = '{"site":{"iisAppPool":"UserPool","iisWebSite":"UserSite"}}'
       expect = @{ 'site.iisAppPool' = 'UserPool'; 'site.iisWebSite' = 'UserSite'; 'site.sqlServerName' = 'localhost' } },
    @{ name = 'defaults when nobody sets the field'
       base = '{"site":{"instanceName":"X"}}'
       user = '{}'
       expect = @{ 'site.iisAppPool' = 'DefaultAppPool'; 'site.sqlServerName' = 'localhost'; 'site.iisWebSite' = 'Default Web Site'; 'site.dbName' = 'XDB' } },
    @{ name = 'explicit base dbName beats a name derived from the overlay instanceName'
       base = '{"site":{"instanceName":"X","dbName":"CustomDb"}}'
       user = '{"site":{"instanceName":"Y"}}'
       expect = @{ 'site.instanceName' = 'Y'; 'site.dbName' = 'CustomDb' } },
    @{ name = 'derived dbName follows the merged instanceName'
       base = '{"site":{"instanceName":"X"}}'
       user = '{"site":{"instanceName":"Y"}}'
       expect = @{ 'site.dbName' = 'YDB' } },
    @{ name = 'explicit erp installationFileName survives'
       base = '{"erp":{"erpVersion":"25.201.0213","installationFileName":"Custom.msi"}}'
       user = '{}'
       expect = @{ 'erp.installationFileName' = 'Custom.msi' } }
)

$failed = 0; $i = 0
foreach ($case in $cases) {
    $i++
    $dir = Join-Path $root ('{0:D2}' -f $i); New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $doc = Invoke-ConfigShow $dir $case.base $case.user
        $bad = @()
        foreach ($k in $case.expect.Keys) {
            $section, $field = $k.Split('.')
            $actual = $doc.$section.$field
            if ($actual -ne $case.expect[$k]) { $bad += "$k expected '$($case.expect[$k])' got '$actual'" }
        }
        if ($bad) { $failed++; Write-Output "FAIL $($case.name): $($bad -join '; ')" }
        else { Write-Output "ok   $($case.name)" }
    } catch { $failed++; Write-Output "FAIL $($case.name): $_" }
}
Write-Output "$($cases.Count - $failed)/$($cases.Count) passed"
Remove-Item -LiteralPath $root -Recurse -Force
exit ([int]($failed -gt 0))
