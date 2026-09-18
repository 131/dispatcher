param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference = 'Stop'
# Each executable needs a fresh process: x86/x64 builds have the same assembly identity.
$assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($Executable)
$target = $assembly.GetCustomAttributesData() | Where-Object { $_.AttributeType.FullName -eq 'System.Runtime.Versioning.TargetFrameworkAttribute' }
if ($assembly.ImageRuntimeVersion -ne 'v4.0.30319' -or !$target -or $target.ConstructorArguments[0].Value -ne '.NETFramework,Version=v4.8') {
    throw "Wrong runtime or framework target for $Executable."
}
