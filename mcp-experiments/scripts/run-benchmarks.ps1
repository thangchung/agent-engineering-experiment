param(
    [ValidateSet("local", "hyperlight", "opensandbox")]
    [string[]]$Runners = @("local", "hyperlight"),
    [switch]$IncludeOpensandbox
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$artifactsDir = Join-Path $repoRoot "docs" "benchmarks" $timestamp
$bdnResultsCandidates = @(
    (Join-Path $repoRoot "BenchmarkDotNet.Artifacts" "results"),
    (Join-Path $repoRoot "tests" "McpServer.Benchmarks" "BenchmarkDotNet.Artifacts" "results")
)

function Get-CleanCellValue {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    return $Value.Replace("*", "").Trim()
}

function Test-EndpointReachable {
    param([string]$Domain)

    if ([string]::IsNullOrWhiteSpace($Domain)) {
        return $false
    }

    $parts = $Domain.Split(':', 2)
    $hostName = $parts[0]
    $port = if ($parts.Length -gt 1) { [int]$parts[1] } else { 8080 }

    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $async = $client.BeginConnect($hostName, $port, $null, $null)
        $ok = $async.AsyncWaitHandle.WaitOne(1500)
        if (-not $ok) {
            return $false
        }

        $client.EndConnect($async)
        return $true
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Test-HyperlightHostReady {
    if (-not $IsWindows) {
        return $true
    }

    try {
        $cs = Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop
        if (-not $cs.HypervisorPresent) {
            Write-Warning "Skipping hyperlight: Hypervisor not present. Enable virtualization / Hyper-V features first."
            return $false
        }
    }
    catch {
        # If detection fails, do not block execution.
    }

    return $true
}

function Get-MeanMs {
    param([string]$MeanText)

    if ([string]::IsNullOrWhiteSpace($MeanText) -or $MeanText -eq "NA") {
        return $null
    }

    if ($MeanText -match '^([0-9]+(?:\.[0-9]+)?)\s*(ns|us|ms|s)$') {
        $value = [double]$matches[1]
        $unit = $matches[2]
        switch ($unit) {
            "ns" { return $value / 1000000.0 }
            "us" { return $value / 1000.0 }
            "ms" { return $value }
            "s"  { return $value * 1000.0 }
        }
    }

    return $null
}

function Get-BenchmarkRows {
    param([string]$ReportPath)

    if (-not (Test-Path $ReportPath)) {
        return @()
    }

    $rows = @()
    foreach ($line in (Get-Content -Path $ReportPath)) {
        if (-not ($line -match 'Execute' -and $line.Contains('|'))) {
            continue
        }

        $cells = $line.Split('|') |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

        if ($cells.Length -lt 5) {
            continue
        }

        $method = Get-CleanCellValue $cells[0]
        if ($method -ne "Execute") {
            continue
        }

        $rows += [PSCustomObject]@{
            Method = $method
            Workload = Get-CleanCellValue $cells[1]
            Style = Get-CleanCellValue $cells[2]
            Runner = Get-CleanCellValue $cells[3]
            Mean = Get-CleanCellValue $cells[4]
        }
    }

    return $rows
}

function Get-CompareValue {
    param(
        [object[]]$Rows,
        [string]$Workload,
        [string]$Style
    )

    $match = $Rows | Where-Object {
        $_.Workload -eq $Workload -and $_.Style -eq $Style
    } | Select-Object -First 1

    if ($null -eq $match) {
        return "NA"
    }

    return $match.Mean
}

function Get-RunnerStatus {
    param(
        [string]$Runner,
        [string]$RunnerLog,
        [object]$SummaryRow
    )

    if ($SummaryRow.WarmSimple -ne "NA" -or $SummaryRow.WarmComplex -ne "NA" -or $SummaryRow.ColdSimple -ne "NA") {
        return "OK"
    }

    if ([string]::IsNullOrWhiteSpace($RunnerLog) -or -not (Test-Path $RunnerLog)) {
        return "FAILED (no log)"
    }

    $logText = Get-Content -Path $RunnerLog -Raw

    if ($Runner -eq "hyperlight" -and $logText -match "SurrogateProcessManager") {
        return "FAILED: Hyperlight surrogate manager init"
    }

    if ($Runner -eq "opensandbox" -and $logText -match "OpenSandbox:Domain is required") {
        return "FAILED: OpenSandbox domain missing"
    }

    if ($logText -match "Detected error exit code") {
        return "FAILED: benchmark process error"
    }

    return "NA"
}

$summaryRows = @()

if (-not (Test-Path $artifactsDir)) {
    New-Item -ItemType Directory -Path $artifactsDir | Out-Null
    Write-Host "Created $artifactsDir"
}

$runnerList = @($Runners)
if ($IncludeOpensandbox -and $runnerList -notcontains "opensandbox") {
    $runnerList += "opensandbox"
}

Write-Host "Bench drivers: $($runnerList -join ', ')" -ForegroundColor Cyan

foreach ($runner in $runnerList) {
    Write-Host "`n=== Running $runner ===" -ForegroundColor Yellow
    
    $env:MCP_BENCH_RUNNER = $runner
    
    if ($runner -eq "opensandbox") {
        $env:BENCH_OPENSANDBOX = "1"

        if ([string]::IsNullOrWhiteSpace($env:OpenSandbox__Domain)) {
            $env:OpenSandbox__Domain = "localhost:8080"
        }

        if (-not (Test-EndpointReachable -Domain $env:OpenSandbox__Domain)) {
            Write-Warning "Skipping opensandbox: endpoint '$($env:OpenSandbox__Domain)' not reachable. Start AppHost/container first."
            $summaryRows += [PSCustomObject]@{
                Runner = $runner
                WarmSimple = "NA"
                WarmComplex = "NA"
                ColdSimple = "NA"
                WarmSimpleMs = $null
                WarmComplexMs = $null
                ColdSimpleMs = $null
                Status = "SKIPPED: endpoint unreachable"
                RunnerLog = ""
            }
            continue
        }
    } else {
        $env:BENCH_OPENSANDBOX = ""

        if ($runner -eq "hyperlight") {
            if (-not (Test-HyperlightHostReady)) {
                $summaryRows += [PSCustomObject]@{
                    Runner = $runner
                    WarmSimple = "NA"
                    WarmComplex = "NA"
                    ColdSimple = "NA"
                    WarmSimpleMs = $null
                    WarmComplexMs = $null
                    ColdSimpleMs = $null
                    Status = "SKIPPED: hypervisor not ready"
                    RunnerLog = ""
                }
                continue
            }
        }
    }
    
    $runnerLog = Join-Path $artifactsDir "bench-$runner.log"
    
    $env:MCP_BENCH_RUNNER = $runner
    dotnet run -c Release --project tests/McpServer.Benchmarks/McpServer.Benchmarks.csproj -- --filter "*" 2>&1 | Tee-Object -FilePath $runnerLog
    
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Bench failed for $runner"
        continue
    }

    $runnerMetricsDir = Join-Path $artifactsDir "metrics-$runner"
    if (-not (Test-Path $runnerMetricsDir)) {
        New-Item -ItemType Directory -Path $runnerMetricsDir | Out-Null
    }

    $copiedCount = 0
    foreach ($bdnResultsDir in $bdnResultsCandidates) {
        if (-not (Test-Path $bdnResultsDir)) {
            continue
        }

        Get-ChildItem -Path $bdnResultsDir -File |
            Where-Object { $_.Extension -in @('.md', '.json') } |
            ForEach-Object {
                Copy-Item -Path $_.FullName -Destination (Join-Path $runnerMetricsDir $_.Name) -Force
                $copiedCount++
            }
    }

    if ($copiedCount -gt 0) {
        Write-Host "Metrics copied ($copiedCount files): $runnerMetricsDir" -ForegroundColor DarkCyan

        $warmReport = Join-Path $runnerMetricsDir "McpServer.Benchmarks.CodeModeBench-report-default.md"
        $coldReport = Join-Path $runnerMetricsDir "McpServer.Benchmarks.CodeModeColdBench-report-default.md"

        $warmRows = Get-BenchmarkRows -ReportPath $warmReport
        $coldRows = Get-BenchmarkRows -ReportPath $coldReport

        $warmSimple = Get-CompareValue -Rows $warmRows -Workload "simple" -Style "a_requests"
        $warmComplex = Get-CompareValue -Rows $warmRows -Workload "complex" -Style "a_requests"
        $coldSimple = Get-CompareValue -Rows $coldRows -Workload "simple" -Style "a_requests"

        $summaryRows += [PSCustomObject]@{
            Runner = $runner
            WarmSimple = $warmSimple
            WarmComplex = $warmComplex
            ColdSimple = $coldSimple
            WarmSimpleMs = Get-MeanMs -MeanText $warmSimple
            WarmComplexMs = Get-MeanMs -MeanText $warmComplex
            ColdSimpleMs = Get-MeanMs -MeanText $coldSimple
            Status = "NA"
            RunnerLog = $runnerLog
        }
    } else {
        Write-Warning "No BenchmarkDotNet metric files found (.md/.json) for $runner"
    }
}

$summaryPath = Join-Path $artifactsDir "summary.md"

foreach ($row in $summaryRows) {
    if ($row.Status -eq "NA") {
        $row.Status = Get-RunnerStatus -Runner $row.Runner -RunnerLog $row.RunnerLog -SummaryRow $row
    }
}

$winner = $summaryRows |
    Where-Object { $null -ne $_.WarmSimpleMs -and $null -ne $_.WarmComplexMs -and $null -ne $_.ColdSimpleMs } |
    Sort-Object { ($_.WarmSimpleMs + $_.WarmComplexMs + $_.ColdSimpleMs) / 3.0 } |
    Select-Object -First 1

$winnerText = if ($null -ne $winner) {
    "$($winner.Runner) (avg=$([Math]::Round((($winner.WarmSimpleMs + $winner.WarmComplexMs + $winner.ColdSimpleMs) / 3.0), 2)) ms)"
} else {
    "No clear winner (missing or failed metrics)."
}

$lines = @()
$lines += "# Benchmark Consolidated Report"
$lines += ""
$lines += "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$lines += ""
$lines += "## Compare (lower is better)"
$lines += ""
$lines += "| Runner | Warm Simple (a_requests) | Warm Complex (a_requests) | Cold Simple (a_requests) | Status |"
$lines += "|---|---:|---:|---:|---|"

foreach ($row in $summaryRows) {
    $lines += "| $($row.Runner) | $($row.WarmSimple) | $($row.WarmComplex) | $($row.ColdSimple) | $($row.Status) |"
}

$lines += ""
$lines += "## Winner"
$lines += ""
$lines += $winnerText
$lines += ""
$lines += "## Notes"
$lines += ""
$lines += "- 'NA' means benchmark failed or no valid measurement run."
$lines += "- Source reports are in metrics-<runner> folders."

Set-Content -Path $summaryPath -Value $lines
Write-Host "Consolidated report: $summaryPath" -ForegroundColor Cyan

Write-Host "`nArtifacts: $artifactsDir" -ForegroundColor Green
Write-Host "Contents:" -ForegroundColor Green
Get-ChildItem -Path $artifactsDir | ForEach-Object { Write-Host "  $_" }
