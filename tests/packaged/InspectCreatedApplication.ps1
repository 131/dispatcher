param([Parameter(Mandatory=$true)][string]$Dispatcher, [Parameter(Mandatory=$true)][string]$ConfigPath)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'ProcessInspector.cs')
$config = [xml][IO.File]::ReadAllText($ConfigPath)
$settings = @{}
foreach ($entry in $config.configuration.appSettings.add) { $settings[$entry.key] = $entry.value }
$assembly = [Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($Dispatcher))
$packaged = $assembly.GetType('Utils.PackagedApplication', $true)
$binding = [Reflection.BindingFlags]'Static, NonPublic'
$executable = $packaged.GetMethod('ResolveExecutable', $binding).Invoke($null, @([string]$settings.APP_NAME))
$directory = if ($settings.CWD) { [IO.Path]::GetFullPath($settings.CWD) } else { [Environment]::CurrentDirectory }
$pipeName = 'dispatcher-inspection-' + [Guid]::NewGuid().ToString('N')
$security = [IO.Pipes.PipeSecurity]::new()
$security.AddAccessRule([IO.Pipes.PipeAccessRule]::new([Security.Principal.WindowsIdentity]::GetCurrent().User, [IO.Pipes.PipeAccessRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow))
$pipe = [IO.Pipes.NamedPipeServerStream]::new($pipeName, [IO.Pipes.PipeDirection]::InOut, 1, [IO.Pipes.PipeTransmissionMode]::Byte, [IO.Pipes.PipeOptions]::Asynchronous, 65536, 65536, $security)
try {
    $connection = $pipe.WaitForConnectionAsync()
    # Exercise the production C# COM bootstrap, with no Appx cmdlets.
    $helper = $packaged.GetMethod('StartInPackage', $binding).Invoke($null, @([string]$settings.APP_NAME, $Dispatcher, ('--dispatcher-package-helper {0} "{1}"' -f $pipeName, $executable)))
    if (!$connection.Wait(20000)) { throw 'Helper connection timeout.' }
    $reader = [IO.BinaryReader]::new($pipe)
    $writer = [IO.BinaryWriter]::new($pipe)
    if ($reader.ReadInt32() -ne 1) { throw 'Invalid protocol.' }
    $helperPid = $reader.ReadInt32()
    $null = $reader.ReadString()
    # This test uses one argument with spaces and Unicode, and inspects before any app code runs.
    $argument = '--dispatcher-probe=spaces é漢字'
    $writer.Write('"' + $argument + '"')
    $writer.Write([string]$directory)
    $writer.Write($false); $writer.Write($false); $writer.Write('')
    $writer.Write([int]$PID)
    1..3 | ForEach-Object { $writer.Write([long]0) }
    $envBlock = [Environment]::GetEnvironmentVariables()
    foreach ($key in $settings.Keys) {
        if ($key.StartsWith('ENV_')) { $envBlock[$key.Substring(4)] = [string]$settings[$key] }
    }
    $envBlock['DISPATCHER_SUSPENDED_PROBE'] = 'spaces é漢字'
    $writer.Write([int]$envBlock.Count)
    foreach ($entry in $envBlock.GetEnumerator()) { $writer.Write([string]$entry.Key); $writer.Write([string]$entry.Value) }
    $writer.Flush()
    if (!$reader.ReadBoolean()) { throw $reader.ReadString() }
    $target = $reader.ReadInt32()
    try {
        if ([DispatcherProcessInspector]::ReadValue($target, 'DISPATCHER_SUSPENDED_PROBE') -cne 'spaces é漢字') { throw 'Suspended process ENV mismatch.' }
        foreach ($key in $settings.Keys) {
            if ($key.StartsWith('ENV_') -and [DispatcherProcessInspector]::ReadValue($target, $key.Substring(4)) -cne $settings[$key]) { throw "Suspended process $key mismatch." }
        }
        if ([DispatcherProcessInspector]::ReadValue($target, '@CWD').TrimEnd('\') -ne $directory.TrimEnd('\')) { throw 'Suspended process CWD mismatch.' }
        if ([DispatcherProcessInspector]::Arguments($target)[1] -cne $argument) { throw 'Suspended process ARGV mismatch.' }
        Write-Output 'PASS real packaged executable before startup: ENV, ARGV, CWD'
    } finally {
        # Cancel before ResumeThread: the application never executes and the helper cleans it up.
        $writer.Write($false); $writer.Flush()
        $null = $reader.ReadBoolean(); $null = $reader.ReadString()
    }
} finally { $pipe.Dispose(); if ($helper) { $helper.Dispose() } }
