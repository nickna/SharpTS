[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'distribution\windows\assets')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$assets = [ordered]@{
    'Square44x44Logo.png' = 44
    'Square150x150Logo.png' = 150
    'StoreLogo.png' = 50
}

foreach ($asset in $assets.GetEnumerator()) {
    $size = [int]$asset.Value
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.ScaleTransform($size / 64.0, $size / 64.0)

            # This geometry is the raster projection of the SDK template's app-icon.svg.
            $background = [Drawing.Drawing2D.GraphicsPath]::new()
            try {
                $diameter = 24.0
                $background.AddArc(0, 0, $diameter, $diameter, 180, 90)
                $background.AddArc(64 - $diameter, 0, $diameter, $diameter, 270, 90)
                $background.AddArc(64 - $diameter, 64 - $diameter, $diameter, $diameter, 0, 90)
                $background.AddArc(0, 64 - $diameter, $diameter, $diameter, 90, 90)
                $background.CloseFigure()
                $brandBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#5b5bd6'))
                try { $graphics.FillPath($brandBrush, $background) }
                finally { $brandBrush.Dispose() }
            }
            finally { $background.Dispose() }
            $graphics.FillRectangle([Drawing.Brushes]::White, 17, 18, 30, 8)
            $graphics.FillRectangle([Drawing.Brushes]::White, 28, 26, 8, 22)
        }
        finally { $graphics.Dispose() }

        $bitmap.Save((Join-Path $OutputDirectory $asset.Key), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
}

Write-Host "Generated $($assets.Count) branded Windows package assets in $OutputDirectory."
