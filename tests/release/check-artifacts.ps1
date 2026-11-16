param([string]$Directory = (Join-Path $PSScriptRoot '..\..\output\release'), [switch]$Signed)
$ErrorActionPreference = 'Stop'
$expected = @{
    'dispatcher_cmd.exe' = @(0x14c, 3)
    'dispatcher_cmd_x64.exe' = @(0x8664, 3)
    'dispatcher_win.exe' = @(0x14c, 2)
    'dispatcher_win_x64.exe' = @(0x8664, 2)
    'dispatcher_go_cmd.exe' = @(0x14c, 3)
    'dispatcher_go_cmd_x64.exe' = @(0x8664, 3)
    'dispatcher_go_win.exe' = @(0x14c, 2)
    'dispatcher_go_win_x64.exe' = @(0x8664, 2)
}
$files = @(Get-ChildItem -LiteralPath $Directory -Filter '*.exe')
if ($files.Count -ne $expected.Count) { throw 'Release must contain exactly eight EXEs.' }
$checksums = @()
foreach ($name in ($expected.Keys | Sort-Object)) {
    $file = Join-Path $Directory $name
    $bytes = [IO.File]::ReadAllBytes($file)
    $pe = [BitConverter]::ToInt32($bytes, 0x3c)
    if ([BitConverter]::ToUInt32($bytes, $pe) -ne 0x4550) { throw "Invalid PE: $name" }
    $machine = [BitConverter]::ToUInt16($bytes, $pe + 4)
    $subsystem = [BitConverter]::ToUInt16($bytes, $pe + 24 + 68)
    if ($machine -ne $expected[$name][0] -or $subsystem -ne $expected[$name][1]) { throw "Wrong architecture/subsystem: $name" }
    if (!$name.StartsWith('dispatcher_go_')) {
        if ($bytes.Length -ge 100000) { throw "C# release exceeds 100 kB: $name" }
        if ([Diagnostics.FileVersionInfo]::GetVersionInfo($file).FileMajorPart -ne 4) { throw "Wrong C# release version: $name" }
    }
    if ($Signed) {
        $signature = Get-AuthenticodeSignature -LiteralPath $file
        if ($signature.Status -ne 'Valid') { throw "Invalid Authenticode signature: $name ($($signature.Status))" }
        if (!$signature.TimeStamperCertificate) { throw "Missing signing timestamp: $name" }
    }
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksums += "$hash  $name"
    Write-Output "PASS $name ($($bytes.Length) bytes, signed=$Signed)"
}
if ($Signed) { [IO.File]::WriteAllLines((Join-Path $Directory 'SHA256SUMS'), $checksums, [Text.Encoding]::ASCII) }
