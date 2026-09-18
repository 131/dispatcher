param([Parameter(Mandatory=$true)][int]$TargetPid, [Parameter(Mandatory=$true)][string]$ConfigPath)
$ErrorActionPreference = 'Stop'
# Read only selected values and compare in memory. Never print the environment.
Add-Type -Path (Join-Path $PSScriptRoot 'ProcessInspector.cs')
$config = [xml][IO.File]::ReadAllText($ConfigPath)
foreach ($entry in $config.configuration.appSettings.add) {
    if ($entry.key.StartsWith('ENV_')) {
        $name = $entry.key.Substring(4)
        if ([DispatcherProcessInspector]::ReadValue($TargetPid, $name) -cne $entry.value) { throw "Environment mismatch: $name" }
        Write-Output "PASS process environment: $name"
    }
    if ($entry.key -eq 'CWD') {
        $actual = [DispatcherProcessInspector]::ReadValue($TargetPid, '@CWD').TrimEnd('\')
        if ($actual -ne $entry.value.TrimEnd('\')) { throw 'Process CWD mismatch.' }
        Write-Output 'PASS process CWD'
    }
}
$expectedArgs = @($config.configuration.appSettings.add | Where-Object key -Like 'ARGV*' | Sort-Object key)
$actualArgs = [DispatcherProcessInspector]::Arguments($TargetPid)
for ($i=0; $i -lt $expectedArgs.Count; $i++) {
    if ($actualArgs[$i+1] -cne $expectedArgs[$i].value) { throw "Process argument mismatch at $i" }
}
Write-Output "PASS process ARGV ($($expectedArgs.Count) configured arguments)"
