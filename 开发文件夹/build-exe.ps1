param(
    [string]$OutputPath = (Join-Path (Join-Path $PSScriptRoot '..\exe文件夹') 'ACE_LowPriority.exe')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-IconFileFromImage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ImagePath,

        [Parameter(Mandatory = $true)]
        [string]$IcoPath
    )

    Add-Type -AssemblyName System.Drawing

    $sizes = @(16, 32, 48, 64, 128, 256)
    $sourceImage = [System.Drawing.Image]::FromFile($ImagePath)

    try {
        $iconFrames = New-Object System.Collections.Generic.List[byte[]]

        foreach ($size in $sizes) {
            $bitmap = New-Object System.Drawing.Bitmap $size, $size
            try {
                $bitmap.SetResolution(96, 96)
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.Clear([System.Drawing.Color]::Transparent)
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                    $graphics.DrawImage($sourceImage, 0, 0, $size, $size)
                }
                finally {
                    $graphics.Dispose()
                }

                $memoryStream = New-Object System.IO.MemoryStream
                try {
                    $bitmap.Save($memoryStream, [System.Drawing.Imaging.ImageFormat]::Png)
                    $iconFrames.Add($memoryStream.ToArray())
                }
                finally {
                    $memoryStream.Dispose()
                }
            }
            finally {
                $bitmap.Dispose()
            }
        }

        $fileStream = [System.IO.File]::Open($IcoPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
        try {
            $writer = New-Object System.IO.BinaryWriter($fileStream)
            try {
                $writer.Write([UInt16]0)
                $writer.Write([UInt16]1)
                $writer.Write([UInt16]$iconFrames.Count)

                $dataOffset = 6 + (16 * $iconFrames.Count)
                for ($index = 0; $index -lt $iconFrames.Count; $index++) {
                    $frameData = $iconFrames[$index]
                    $size = $sizes[$index]
                    $dimension = if ($size -ge 256) { 0 } else { [byte]$size }

                    $writer.Write([byte]$dimension)
                    $writer.Write([byte]$dimension)
                    $writer.Write([byte]0)
                    $writer.Write([byte]0)
                    $writer.Write([UInt16]1)
                    $writer.Write([UInt16]32)
                    $writer.Write([UInt32]$frameData.Length)
                    $writer.Write([UInt32]$dataOffset)
                    $dataOffset += $frameData.Length
                }

                foreach ($frameData in $iconFrames) {
                    $writer.Write($frameData)
                }
            }
            finally {
                $writer.Dispose()
            }
        }
        finally {
            $fileStream.Dispose()
        }
    }
    finally {
        $sourceImage.Dispose()
    }
}

$sourcePath = Join-Path $PSScriptRoot 'Program.cs'
if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Source file not found: $sourcePath"
}

$outputDirectory = Split-Path -Path $OutputPath -Parent
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    [void](New-Item -ItemType Directory -Force -Path $outputDirectory)
}

$iconSourcePath = Join-Path $PSScriptRoot '红温猫.jpg'
if (-not (Test-Path -LiteralPath $iconSourcePath)) {
    throw "Icon source not found: $iconSourcePath"
}

$temporaryIconPath = Join-Path ([System.IO.Path]::GetTempPath()) ('ACE_LowPriority_{0}.ico' -f [System.Guid]::NewGuid().ToString('N'))

try {
    New-IconFileFromImage -ImagePath $iconSourcePath -IcoPath $temporaryIconPath

    $provider = New-Object Microsoft.CSharp.CSharpCodeProvider
    $compilerParameters = New-Object System.CodeDom.Compiler.CompilerParameters
    $compilerParameters.GenerateExecutable = $true
    $compilerParameters.GenerateInMemory = $false
    $compilerParameters.IncludeDebugInformation = $false
    $compilerParameters.TreatWarningsAsErrors = $false
    $compilerParameters.WarningLevel = 4
    $compilerParameters.OutputAssembly = $OutputPath
    $compilerParameters.CompilerOptions = ('/target:winexe /platform:x64 /optimize+ /win32icon:"{0}"' -f $temporaryIconPath)

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
}
finally {
    if (Test-Path -LiteralPath $temporaryIconPath) {
        Remove-Item -LiteralPath $temporaryIconPath -Force
    }
}

Write-Host "Build succeeded: $OutputPath" -ForegroundColor Green
Write-Host "Icon embedded from: $iconSourcePath" -ForegroundColor Green
