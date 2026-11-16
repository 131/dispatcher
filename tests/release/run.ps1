param([switch]$SkipBuild, [string]$PackageAppName)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).ProviderPath
Push-Location $root
$oldArchitecture = $env:GOARCH
$oldOutput = $env:DISPATCHER_GO_TEST_OUTPUT
$oldPackage = $env:DISPATCHER_GO_TEST_APP_NAME
try {
    if (!$SkipBuild) {
        & bash ./build --build
        if ($LASTEXITCODE -ne 0) { throw 'C# build failed.' }
        & bash ./build-go
        if ($LASTEXITCODE -ne 0) { throw 'Go build failed.' }
    }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./tests/packaged/run.ps1
    if ($LASTEXITCODE -ne 0) { throw 'C# packaged tests failed.' }
    $env:DISPATCHER_GO_TEST_OUTPUT = (Resolve-Path ./output/go).ProviderPath
    if ($PackageAppName) { $env:DISPATCHER_GO_TEST_APP_NAME = $PackageAppName }
    # COM tests use only our probe, not the installed application's executable.
    if (!$env:DISPATCHER_GO_TEST_APP_NAME) {
        foreach ($packageName in @('Microsoft.WindowsTerminal', 'Microsoft.DesktopAppInstaller')) {
            $p = Get-AppxPackage -Name $packageName | Sort-Object Version -Descending | Select-Object -First 1
            if (!$p) { continue }
            $m = [xml][IO.File]::ReadAllText((Join-Path $p.InstallLocation 'AppxManifest.xml'))
            $app = @($m.Package.Applications.Application | Where-Object { $_.Id -eq 'App' -and ($_.EntryPoint -eq 'Windows.FullTrustApplication' -or $_.RuntimeBehavior -in @('packagedClassicApp', 'win32App')) })
            if ($app.Count) { $env:DISPATCHER_GO_TEST_APP_NAME = $p.PackageFamilyName + '!' + $app[0].Id; break }
        }
    }
    if (!$env:DISPATCHER_GO_TEST_APP_NAME) { Write-Output 'SKIP real package COM: no desktop package available; transport tests remain mandatory.' }
    & go vet ./...
    if ($LASTEXITCODE -ne 0) { throw 'Go vet failed.' }
    $work = Join-Path $root ('output\tests\release-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    foreach ($arch in @('386', 'amd64')) {
        $env:GOARCH = $arch
        $test = Join-Path $work "dispatcher-tests-$arch.exe"
        & go test -c -ldflags '-s -w' -o $test ./internal/dispatcher
        if ($LASTEXITCODE -ne 0) { throw "Go test build failed: $arch" }
        & $test -test.v
        if ($LASTEXITCODE -ne 0) { throw "Go tests failed: $arch" }
    }
} finally {
    $env:GOARCH = $oldArchitecture
    $env:DISPATCHER_GO_TEST_OUTPUT = $oldOutput
    $env:DISPATCHER_GO_TEST_APP_NAME = $oldPackage
    Pop-Location
}
