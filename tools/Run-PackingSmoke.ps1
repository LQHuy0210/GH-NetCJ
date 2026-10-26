$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    dotnet build GH-Net.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    $suites = @('LicenseSmoke', 'PackingDomainSmoke', 'CsvImportSmoke', 'ScannerProtocolSmoke',
        'ZebraPrinterSmoke', 'DeviceConfigSmoke', 'PackingRuntimeSmoke', 'PackingApplicationSmoke',
        'DirectWpfSmoke', 'TraceExportSmoke', 'AutomationSmoke', 'TechnicalSettingsSmoke')
    $results = @()
    foreach ($suite in $suites) {
        Write-Host "Checking $suite"
        dotnet run --project "tools/$suite/$suite.csproj" -c Release --verbosity quiet
        $passed = $LASTEXITCODE -eq 0
        $results += [pscustomobject]@{ Suite = $suite; Passed = $passed }
        if (-not $passed) { throw "$suite failed." }
    }
    dotnet GH-Net/bin/Release/net8.0-windows/GH-Net.dll --self-test
    if ($LASTEXITCODE -ne 0) { throw 'WPF self-test failed.' }
    New-Item -ItemType Directory -Path artifacts -Force | Out-Null
    $results | ConvertTo-Json | Set-Content -LiteralPath artifacts/packing-smoke-results.json -Encoding utf8
    Write-Host 'All 12 suites and WPF self-test passed.'
}
finally { Pop-Location }
