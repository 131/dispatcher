param([string]$PackageAppName, [string]$OutputDirectory = (Join-Path $PSScriptRoot ('..\..\output\tests\packaged\' + [Guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$framework = $env:NET48_REFERENCE_ASSEMBLIES
if (!$framework) {
    $framework = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
    if (!(Test-Path (Join-Path $framework 'mscorlib.dll'))) {
        $framework = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319'
    }
}
if (!(Test-Path (Join-Path $framework 'mscorlib.dll'))) {
    throw "Cannot find .NET Framework assemblies in: $framework"
}
$references = @('mscorlib', 'System', 'System.Core', 'System.Xml', 'System.ServiceProcess', 'System.Management', 'System.Web.Extensions') | ForEach-Object { '/reference:' + (Join-Path $framework ($_ + '.dll')) }
$sources = @('Program.cs', 'Properties\AssemblyInfo.cs', 'Utils\Job.cs', 'Utils\EnvironmentProvider.cs', 'Utils\Kernel32.cs', 'Utils\PackagedApplication.cs', 'Utils\PackagePipe.cs', 'Utils\ProcessExtensions.cs', 'Utils\ParentProcessUtilities.cs', 'Utils\UWFManagement.cs') | ForEach-Object { Join-Path "$root\src" $_ }
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
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'FrameworkTests.ps1') -Executable (Join-Path $directory "dispatcher_$kind.exe")
        if ($LASTEXITCODE -ne 0) { throw "Framework check failed for $platform/$kind." }
        Write-Output "PASS $platform/${kind}: CLR 4, .NET Framework 4.8 target"
    }
    Compile (@('/target:exe', "/platform:$platform", ('/out:' + (Join-Path $directory 'environment-provider-fixture.exe')), (Join-Path $PSScriptRoot 'EnvironmentProviderFixture.cs')))
    Compile (@('/target:exe', "/platform:$platform", '/main:EnvironmentProviderTests', ('/out:' + (Join-Path $directory 'environment-provider-tests.exe')), (Join-Path $PSScriptRoot 'EnvironmentProviderTests.cs')) + $sources)
    & (Join-Path $directory 'environment-provider-tests.exe')
    if ($LASTEXITCODE -ne 0) { throw "ENV_PROVIDER tests failed for $platform." }
    Compile (@('/target:exe', "/platform:$platform", ('/out:' + (Join-Path $directory 'probe.exe')), (Join-Path $PSScriptRoot 'Probe.cs')))
    Compile (@('/target:exe', "/platform:$platform", '/main:PipeTests', ('/out:' + (Join-Path $directory 'pipe-tests.exe')), (Join-Path $PSScriptRoot 'PipeTests.cs')) + $sources)
    & (Join-Path $directory 'pipe-tests.exe')
    if ($LASTEXITCODE -ne 0) { throw "Pipe tests failed for $platform." }
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
