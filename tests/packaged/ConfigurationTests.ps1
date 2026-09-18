param([Parameter(Mandatory=$true)][string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
$directory = Join-Path $BuildDirectory 'configuration-tests'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$launcher = Join-Path $directory 'dispatcher.exe'
Copy-Item (Join-Path $BuildDirectory 'x64\dispatcher_cmd.exe') $launcher -Force
$cases = @(
    @{ Settings=@{ APP_NAME='invalid' }; Error='PackageFamilyName!ApplicationId' },
    @{ Settings=@{ APP_NAME='OpenAI.Codex_2p2nqsd0c76g0!App'; PATH='cmd.exe' }; Error='not both' },
    @{ Settings=@{ APP_NAME='OpenAI.Codex_2p2nqsd0c76g0!App'; AS_USER='true' }; Error='interactive Windows user' },
    @{ Settings=@{ APP_NAME='Dispatcher.NonexistentPackage_1234567890123!App' }; Error='not installed' }
)
foreach ($case in $cases) {
    $document = [xml]'<configuration><appSettings/></configuration>'
    foreach ($entry in $case.Settings.GetEnumerator()) {
        $node = $document.CreateElement('add'); $node.SetAttribute('key', $entry.Key); $node.SetAttribute('value', $entry.Value)
        $null = $document.DocumentElement.FirstChild.AppendChild($node)
    }
    $document.Save($launcher + '.config')
    $start = [Diagnostics.ProcessStartInfo]::new($launcher)
    $start.UseShellExecute = $false; $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $reading = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(25000)) { $process.Kill(); throw 'Configuration error did not fail promptly.' }
    $message = $reading.Result
    if ($process.ExitCode -eq 0 -or !$message.Contains($case.Error)) { throw "Wrong configuration error: $message" }
    $process.Dispose()
    Write-Output "PASS configuration error: $($case.Error)"
}
