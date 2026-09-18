param([string]$PackageAppName, [string]$OutputDirectory = (Join-Path $env:TEMP ('dispatcher-package-tests-' + [Guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$framework = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v2.0.50727'
$references = @('mscorlib', 'System', 'System.Xml', 'System.ServiceProcess', 'System.Management') | ForEach-Object { '/reference:' + (Join-Path $framework ($_ + '.dll')) }
$sources = @('Program.cs', 'Properties\AssemblyInfo.cs', 'Utils\Job.cs', 'Utils\Kernel32.cs', 'Utils\PackagedApplication.cs', 'Utils\ProcessExtensions.cs', 'Utils\ParentProcessUtilities.cs', 'Utils\UWFManagement.cs') | ForEach-Object { Join-Path "$root\src" $_ }
function Compile([string[]]$CompilerArguments) {
    & $csc /nologo /noconfig /nostdlib+ @references @CompilerArguments
    if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($platform in @('x86', 'x64')) {
    $directory = Join-Path $OutputDirectory $platform
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    foreach ($kind in @('cmd', 'win')) {
        $compilerArgs = @(('/platform:' + $platform), ('/out:' + (Join-Path $directory "dispatcher_$kind.exe")))
        if ($kind -eq 'win') { $compilerArgs += @('/target:winexe', '/define:DISPACHER_WIN') } else { $compilerArgs += '/target:exe' }
        Compile ($compilerArgs + $sources)
    }
    Compile (@('/target:exe', "/platform:$platform", ('/out:' + (Join-Path $directory 'probe.exe')), (Join-Path $PSScriptRoot 'Probe.cs')))
    Compile (@('/target:exe', "/platform:$platform", '/main:TransportTests', ('/out:' + (Join-Path $directory 'transport-tests.exe')), (Join-Path $PSScriptRoot 'TransportTests.cs')) + $sources)
    & (Join-Path $directory 'transport-tests.exe') $directory
    if ($LASTEXITCODE -ne 0) { throw "Transport tests failed for $platform." }
    if ($PackageAppName) {
        & (Join-Path $directory 'transport-tests.exe') $directory $PackageAppName
        if ($LASTEXITCODE -ne 0) { throw "Packaged transport tests failed for $platform." }
    }
}
& (Join-Path $PSScriptRoot 'ConfigurationTests.ps1') -BuildDirectory $OutputDirectory
Write-Output "Builds and test results: $OutputDirectory"
