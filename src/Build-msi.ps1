$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$rebuild = $true
$verbosePublish = $false
$waitAtEnd = $true
$checkOnly = $false
try {
    if ($args -contains "-NoRebuild") { $rebuild = $false }
    if ($args -contains "-VerbosePublish") { $verbosePublish = $true }
    if ($args -contains "-NoWait") { $waitAtEnd = $false }
    if ($args -contains "-CheckOnly") { $checkOnly = $true }
} catch {
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$wixProj = Join-Path $repoRoot "WiX_packaged_msi\WiX_packaged_msi.Setup.wixproj"
$winuiProj = Join-Path $repoRoot "UMManager.WinUI\UMManager.WinUI.csproj"
$publishDir = Join-Path $repoRoot "UMManager.WinUI\bin\x64\Release\net9.0-windows10.0.22621.0\win-x64\publish"

function Test-CommandAvailable {
    param([Parameter(Mandatory = $true)][string]$Name)
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Get-DotnetSdkMajorVersion {
    try {
        $versionText = & dotnet --version 2>$null
        if ([string]::IsNullOrWhiteSpace($versionText)) { return $null }
        $parts = $versionText.Trim().Split(".")
        if ($parts.Count -lt 1) { return $null }
        return [int]$parts[0]
    } catch {
        return $null
    }
}

function Get-InstalledWindowsSdkVersions {
    $kitsRoot = $null
    try {
        $kitsRoot = (Get-ItemProperty -LiteralPath "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots" -ErrorAction SilentlyContinue)."KitsRoot10"
    } catch {
        $kitsRoot = $null
    }

    $libRoot = $null
    if (-not [string]::IsNullOrWhiteSpace($kitsRoot)) {
        $candidate = Join-Path $kitsRoot "Lib"
        if (Test-Path -LiteralPath $candidate) { $libRoot = $candidate }
    }

    if ($null -eq $libRoot) {
        $fallback = "C:\Program Files (x86)\Windows Kits\10\Lib"
        if (Test-Path -LiteralPath $fallback) { $libRoot = $fallback }
    }

    if ($null -eq $libRoot) { return @() }

    $versions = @()
    $dirs = Get-ChildItem -LiteralPath $libRoot -Directory -ErrorAction SilentlyContinue
    foreach ($dir in $dirs) {
        $name = $dir.Name
        if ($name -notmatch '^\d+\.\d+\.\d+\.\d+$') { continue }
        try {
            $versions += [version]::Parse($name)
        } catch {
        }
    }

    return $versions | Sort-Object -Descending -Unique
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

function Wait-ForContinue {
    param([string]$Message = "检测到潜在不兼容项。按回车键继续执行，或按 Ctrl+C 取消...")
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
    param([bool]$Interactive = $true)
    Write-Host ""
    Write-Host "环境检测..." -ForegroundColor Cyan

    $missing = New-Object System.Collections.Generic.List[string]
    $warnings = New-Object System.Collections.Generic.List[string]

    $psMajor = $PSVersionTable.PSVersion.Major
    $psOk = ($psMajor -ge 5)
    if (-not $psOk) { $missing.Add("PowerShell") | Out-Null }
    $psState = if ($psOk) { "OK" } else { "MISSING" }
    Write-PreflightLine -Name "PowerShell" -State $psState -Details ("v{0}" -f $PSVersionTable.PSVersion)

    $is64BitOs = [Environment]::Is64BitOperatingSystem
    if (-not $is64BitOs) { $missing.Add("64-bit OS") | Out-Null }
    $osState = if ($is64BitOs) { "OK" } else { "MISSING" }
    Write-PreflightLine -Name "64 位操作系统" -State $osState

    $dotnetOk = Test-CommandAvailable -Name "dotnet"
    $dotnetDetails = $null
    $dotnetState = "OK"
    if ($dotnetOk) {
        $sdkMajor = Get-DotnetSdkMajorVersion
        if ($null -eq $sdkMajor) {
            $dotnetOk = $false
            $dotnetState = "MISSING"
            $dotnetDetails = "无法读取: dotnet --version"
        } elseif ($sdkMajor -lt 9) {
            $dotnetOk = $false
            $dotnetState = "MISSING"
            $dotnetDetails = ("检测到 SDK v{0}，需要 >= 9" -f $sdkMajor)
        } else {
            $dotnetDetails = ("SDK v{0}" -f $sdkMajor)
            if ($sdkMajor -ne 9) {
                $dotnetState = "WARN"
                $warnings.Add(".NET SDK") | Out-Null
            }
        }
    } else {
        $dotnetState = "MISSING"
    }
    if (-not $dotnetOk) { $missing.Add(".NET SDK (dotnet)") | Out-Null }
    Write-PreflightLine -Name ".NET SDK（dotnet）" -State $dotnetState -Details $dotnetDetails

    $requiredWinSdk = [version]::Parse("10.0.22621.0")
    $installedWinSdks = @(Get-InstalledWindowsSdkVersions)
    $windowsSdkState = "OK"
    $windowsSdkDetails = $null
    if ($installedWinSdks.Count -le 0) {
        $windowsSdkState = "MISSING"
        $missing.Add("Windows SDK") | Out-Null
        $windowsSdkDetails = "未检测到 Windows Kits 10 SDK"
    } else {
        $highest = $installedWinSdks | Select-Object -First 1
        $windowsSdkDetails = ("已安装: {0}" -f $highest)
        if ($installedWinSdks -contains $requiredWinSdk) {
            $windowsSdkState = "OK"
            $windowsSdkDetails = ("已安装: {0}（目标 {1}）" -f $highest, $requiredWinSdk)
        } else {
            $windowsSdkState = "WARN"
            $warnings.Add("Windows SDK") | Out-Null
            $windowsSdkDetails = ("已安装: {0}（目标 {1}）" -f $highest, $requiredWinSdk)
        }
    }
    Write-PreflightLine -Name "Windows SDK" -State $windowsSdkState -Details $windowsSdkDetails

    $winuiProjOk = Test-Path -LiteralPath $winuiProj
    if (-not $winuiProjOk) { $missing.Add("WinUI project") | Out-Null }
    $winuiState = if ($winuiProjOk) { "OK" } else { "MISSING" }
    Write-PreflightLine -Name "WinUI 项目" -State $winuiState -Details $winuiProj

    $wixProjOk = Test-Path -LiteralPath $wixProj
    if (-not $wixProjOk) { $missing.Add("WiX project") | Out-Null }
    $wixState = if ($wixProjOk) { "OK" } else { "MISSING" }
    Write-PreflightLine -Name "WiX 项目" -State $wixState -Details $wixProj

    if ($missing.Count -gt 0) {
        Write-Host ""
        Write-Host "环境检测未通过：缺少必需项。请先安装/修复后再运行，或使用 -CheckOnly 仅检测。" -ForegroundColor Red
        throw "Preflight checks failed."
    }

    if ($Interactive -and $warnings.Count -gt 0) {
        Write-Host ""
        Write-Host "环境检测警告：检测到版本/工具链不一致，可能导致 build/publish 失败。" -ForegroundColor Yellow
        Wait-ForContinue
    }
}

$script:hadError = $false
try {
    Invoke-PreflightOrThrow -Interactive (-not $checkOnly)
    if ($checkOnly) {
        Write-Host ""
        Write-Host "CheckOnly: 环境检测通过。" -ForegroundColor Green
        return
    }

    if ($rebuild -or -not (Test-Path -LiteralPath $publishDir)) {
        Write-Host "正在发布 WinUI（win-x64）..." -ForegroundColor Cyan
        $publishArgs = @($winuiProj, "-c", "Release", "-r", "win-x64", "-p:Platform=x64", "-p:UseXamlCompilerExecutable=true", "-nologo")
        if (-not $verbosePublish) {
            $publishArgs += @("-v:q", "-clp:ErrorsOnly")
        }

        $maxAttempts = 3
        $attempt = 1
        while ($attempt -le $maxAttempts) {
            & dotnet publish @publishArgs
            if ($LASTEXITCODE -eq 0) { break }

            if ($attempt -lt $maxAttempts) {
                Write-Host "dotnet publish 失败。正在关闭构建服务器并重试（$attempt/$maxAttempts）..." -ForegroundColor Yellow
                try { & dotnet build-server shutdown | Out-Null } catch { }
                Start-Sleep -Seconds 1
            }

            $attempt++
        }

        if ($LASTEXITCODE -ne 0) {
            Write-Host "dotnet publish 仍然失败。将以更高详细度重跑以输出更多信息..." -ForegroundColor Yellow
            & dotnet publish $winuiProj -c Release -r win-x64 -p:Platform=x64 -p:UseXamlCompilerExecutable=true -v:m -nologo
            throw "dotnet publish failed (exit code $LASTEXITCODE)"
        }
    }

    Write-Host "正在构建 MSI..." -ForegroundColor Cyan
    if ($rebuild) {
        dotnet build $wixProj -c Release /t:Rebuild
    } else {
        dotnet build $wixProj -c Release
    }

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed (exit code $LASTEXITCODE)"
    }

    $msiPath = Join-Path $repoRoot "WiX_packaged_msi\bin\x64\Release\UMManager.msi"
    if (-not (Test-Path $msiPath)) {
        $msi = Get-ChildItem (Join-Path $repoRoot "WiX_packaged_msi\bin") -Recurse -Filter "*.msi" |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($null -eq $msi) {
            throw "MSI not found under WiX_packaged_msi\bin"
        }
        $msiPath = $msi.FullName
    }

    $msiItem = Get-Item $msiPath
    $mb = [math]::Round($msiItem.Length / 1MB, 2)
    Write-Host ""
    Write-Host "MSI: $($msiItem.FullName)" -ForegroundColor Green
    Write-Host "Size: $mb MB"
    Write-Host "LastWriteTime: $($msiItem.LastWriteTime)"
} catch {
    $script:hadError = $true
    Write-Host ""
    Write-Host $_ -ForegroundColor Red
} finally {
    if ($waitAtEnd) {
        Write-Host ""
        Wait-ForExit
    }
    if ($script:hadError) { exit 1 }
}
