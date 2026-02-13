[CmdletBinding(SupportsShouldProcess = $true)]
param(
  [switch]$IncludeWixOutputs = $true,
  [switch]$NoBuildServerShutdown,
  [switch]$NoWait,
  [switch]$CheckOnly
)

$repoRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$progressId = 1

function Test-CommandAvailable {
  param([Parameter(Mandatory = $true)][string]$Name)
  return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Write-PreflightLine {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][ValidateSet("OK","WARN","MISSING")][string]$State,
    [string]$Details
  )

  $color = switch ($State) {
    "OK" { "Green" }
    "WARN" { "Yellow" }
    default { "Red" }
  }
  if ([string]::IsNullOrWhiteSpace($Details)) {
    Write-Host ("[{0}] {1}" -f $State, $Name) -ForegroundColor $color
  } else {
    Write-Host ("[{0}] {1} - {2}" -f $State, $Name, $Details) -ForegroundColor $color
  }
}

function Wait-ForExit {
  param([string]$Message = "按回车键退出...")
  try {
    if ($null -ne $Host -and $null -ne $Host.UI -and $null -ne $Host.UI.RawUI) {
      [void](Read-Host $Message)
    } else {
      Start-Sleep -Seconds 3
    }
  } catch {
    Start-Sleep -Seconds 3
  }
}

function Invoke-PreflightOrThrow {
  Write-Host ""
  Write-Host "环境检测..." -ForegroundColor Cyan

  $missing = New-Object System.Collections.Generic.List[string]

  $psMajor = $PSVersionTable.PSVersion.Major
  $psOk = ($psMajor -ge 5)
  if (-not $psOk) { $missing.Add("PowerShell") | Out-Null }
  $psState = if ($psOk) { "OK" } else { "MISSING" }
  Write-PreflightLine -Name "PowerShell" -State $psState -Details ("v{0}" -f $PSVersionTable.PSVersion)

  $repoRootOk = Test-Path -LiteralPath $repoRoot
  if (-not $repoRootOk) { $missing.Add("Repo root") | Out-Null }
  $repoRootState = if ($repoRootOk) { "OK" } else { "MISSING" }
  Write-PreflightLine -Name "仓库根目录" -State $repoRootState -Details $repoRoot

  $dotnetRequired = -not $NoBuildServerShutdown
  $dotnetOk = $true
  $dotnetState = "OK"
  $dotnetDetails = $null
  if ($dotnetRequired) {
    $dotnetOk = Test-CommandAvailable -Name "dotnet"
    if (-not $dotnetOk) {
      $dotnetState = "MISSING"
      $dotnetDetails = "未找到 dotnet；可使用 -NoBuildServerShutdown 跳过"
      $missing.Add("dotnet") | Out-Null
    }
  } else {
    $dotnetOk = Test-CommandAvailable -Name "dotnet"
    if (-not $dotnetOk) {
      $dotnetState = "WARN"
      $dotnetDetails = "未找到 dotnet（已跳过 build-server shutdown）"
    }
  }
  Write-PreflightLine -Name "dotnet" -State $dotnetState -Details $dotnetDetails

  if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "环境检测未通过：缺少必需项。请先安装/修复后再运行，或使用 -CheckOnly 仅检测。" -ForegroundColor Red
    throw "Preflight checks failed."
  }
}

$fixedTargets = @(
  Join-Path $repoRoot ".vs"
  Join-Path $repoRoot ".tmp"
  Join-Path $repoRoot ".artifacts"
  Join-Path $repoRoot ".trae"
  Join-Path $repoRoot "UpgradeLog.htm"
)

$dirNamesToRemove = @("bin", "obj", "TestResults", "AppPackages", "BundleArtifacts")
$dirs = Get-ChildItem -LiteralPath $repoRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
  Where-Object {
    $_.Name -in $dirNamesToRemove -and
    $_.FullName -notlike "*\.git\*"
  }

$wixFiles = @()
if ($IncludeWixOutputs) {
  $wixGlobs = @("*.msi", "*.msm", "*.msp", "*.cab", "#*.cab", "*.wixpdb", "*.wixobj", "*.wixlib")
  $wixSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($glob in $wixGlobs) {
    $files = Get-ChildItem -LiteralPath $repoRoot -Recurse -Force -File -Filter $glob -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -notlike "*\.git\*" }
    foreach ($file in $files) {
      [void]$wixSet.Add($file.FullName)
    }
  }
  $wixFiles = $wixSet | Sort-Object
}

$existingFixedTargets = $fixedTargets | Where-Object { Test-Path -LiteralPath $_ }
$totalWork = $existingFixedTargets.Count + $dirs.Count + $wixFiles.Count
$done = 0

function Update-CleanProgress {
  param(
    [Parameter(Mandatory = $true)][string]$Status
  )

  if ($totalWork -le 0) { return }
  $percent = [math]::Min(100, [math]::Floor(($done / $totalWork) * 100))
  Write-Progress -Id $progressId -Activity "Clean cache" -Status $Status -PercentComplete $percent
}

Update-CleanProgress -Status "准备清理..."

$script:hadError = $false
try {
  Invoke-PreflightOrThrow
  if ($CheckOnly) {
    Write-Host ""
    Write-Host "CheckOnly: 环境检测通过。" -ForegroundColor Green
    return
  }

  foreach ($target in $existingFixedTargets) {
    $done++
    Update-CleanProgress -Status "删除: $target"
    if ($PSCmdlet.ShouldProcess($target, "Remove")) {
      Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
    }
  }

  foreach ($dir in $dirs) {
    $done++
    Update-CleanProgress -Status "删除目录: $($dir.FullName)"
    if ($PSCmdlet.ShouldProcess($dir.FullName, "Remove")) {
      Remove-Item -LiteralPath $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
  }

  if ($IncludeWixOutputs) {
    foreach ($filePath in $wixFiles) {
      $done++
      Update-CleanProgress -Status "删除文件: $filePath"
      if ($PSCmdlet.ShouldProcess($filePath, "Remove")) {
        Remove-Item -LiteralPath $filePath -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Write-Progress -Id $progressId -Activity "Clean cache" -Completed

  if (-not $NoBuildServerShutdown) {
    if ($PSCmdlet.ShouldProcess("dotnet build-server shutdown", "Run")) {
      & dotnet build-server shutdown | Out-Null
    }
  }

  Write-Output "清理完成: $repoRoot"
} catch {
  $script:hadError = $true
  Write-Host ""
  Write-Host $_ -ForegroundColor Red
} finally {
  if (-not $NoWait) {
    Write-Host ""
    Wait-ForExit
  }
  if ($script:hadError) { exit 1 }
}
