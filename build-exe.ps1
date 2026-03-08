param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'ACE_LowPriority.exe')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $PSScriptRoot 'Program.cs'
if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Source file not found: $sourcePath"
}

$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
$compilerParameters = New-Object System.CodeDom.Compiler.CompilerParameters
$compilerParameters.GenerateExecutable = $true
$compilerParameters.GenerateInMemory = $false
$compilerParameters.IncludeDebugInformation = $false
$compilerParameters.TreatWarningsAsErrors = $false
$compilerParameters.WarningLevel = 4
$compilerParameters.OutputAssembly = $OutputPath
$compilerParameters.CompilerOptions = '/target:winexe /platform:x64 /optimize+'

[void]$compilerParameters.ReferencedAssemblies.Add('System.dll')
[void]$compilerParameters.ReferencedAssemblies.Add('System.Core.dll')
[void]$compilerParameters.ReferencedAssemblies.Add('System.Drawing.dll')
[void]$compilerParameters.ReferencedAssemblies.Add('System.Windows.Forms.dll')

$results = $provider.CompileAssemblyFromFile($compilerParameters, $sourcePath)
$errors = @($results.Errors | Where-Object { -not $_.IsWarning })
if ($errors.Count -gt 0) {
    $messages = foreach ($compilerIssue in $errors) {
        '{0}({1},{2}): {3}' -f $sourcePath, $compilerIssue.Line, $compilerIssue.Column, $compilerIssue.ErrorText
    }

    throw ($messages -join [Environment]::NewLine)
}

Write-Host "Build succeeded: $OutputPath" -ForegroundColor Green
