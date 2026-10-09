param([Parameter(Mandatory=$true)][string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
$directory = Join-Path $BuildDirectory 'provider-configuration-tests'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$launcher = Join-Path $directory 'dispatcher.exe'
Copy-Item (Join-Path $BuildDirectory 'x64\dispatcher_cmd.exe') $launcher -Force
$fixture = Join-Path $BuildDirectory 'x64\environment-provider-fixture.exe'
function RunCase($timeout, $expectedError) {
    $document = [xml]'<configuration><appSettings/></configuration>'
    $settings = @{
        PATH = $env:ComSpec
        ENV_PROVIDER = '"' + $fixture + '" slow'
        USE_JOB = 'false'
        ARGV0 = '/d'
        ARGV1 = '/c'
        ARGV2 = 'if not "%PROVIDER_DELAYED%"=="ready" exit /b 7'
    }
    if ($null -ne $timeout) { $settings.ENV_PROVIDER_TIMEOUT = $timeout }
    foreach ($entry in $settings.GetEnumerator()) {
        $node = $document.CreateElement('add')
        $node.SetAttribute('key', $entry.Key); $node.SetAttribute('value', $entry.Value)
        $null = $document.DocumentElement.FirstChild.AppendChild($node)
    }
    $document.Save($launcher + '.config')
    $start = [Diagnostics.ProcessStartInfo]::new($launcher)
    $start.UseShellExecute = $false; $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $reading = $process.StandardError.ReadToEndAsync()
    try {
        if (!$process.WaitForExit(15000)) { $process.Kill(); throw 'Provider configuration test hung.' }
        $message = $reading.Result
        if ($expectedError) {
            if ($process.ExitCode -eq 0 -or !$message.Contains($expectedError)) { throw "Wrong provider error: $message" }
        } elseif ($process.ExitCode -ne 0) { throw "Delayed provider failed: $message" }
    } finally { $process.Dispose() }
}
RunCase '60' $null
Write-Output 'PASS ENV_PROVIDER_TIMEOUT=60 allows provider beyond the five-second default'
RunCase '1' 'timed out after 1000 ms'
RunCase $null 'timed out after 5000 ms'
foreach ($invalid in @('0', '-1', 'abc', '2147484')) { RunCase $invalid 'positive number of seconds' }
Write-Output 'PASS default/custom provider timeout and invalid seconds'
