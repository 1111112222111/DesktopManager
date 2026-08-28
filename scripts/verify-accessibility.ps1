[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$xamlFiles = @(Get-ChildItem -LiteralPath (Join-Path $workspaceRoot 'src\DesktopManager.App') `
    -Filter '*.xaml' -File)
$missingNames = @()
foreach ($file in $xamlFiles) {
    [xml]$document = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    $manager = New-Object System.Xml.XmlNamespaceManager($document.NameTable)
    $manager.AddNamespace('wpf', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
    foreach ($textBox in $document.SelectNodes('//wpf:TextBox', $manager)) {
        $automationName = $textBox.Attributes |
            Where-Object { $_.LocalName -eq 'AutomationProperties.Name' } |
            Select-Object -First 1
        if ($null -eq $automationName -or [string]::IsNullOrWhiteSpace($automationName.Value)) {
            $missingNames += "$($file.Name):$($textBox.GetAttribute('Name', 'http://schemas.microsoft.com/winfx/2006/xaml'))"
        }
    }
}
if ($missingNames.Count -gt 0) {
    throw "Text inputs missing accessible names: $($missingNames -join ', ')"
}

$mainXamlPath = Join-Path $workspaceRoot 'src\DesktopManager.App\MainWindow.xaml'
$mainXaml = [System.IO.File]::ReadAllText($mainXamlPath, [System.Text.Encoding]::UTF8)
if ($mainXaml -notmatch 'UseLayoutRounding="True"' `
    -or $mainXaml -notmatch 'PreviewKeyDown="MainWindow_PreviewKeyDown"') {
    throw 'Main window is missing layout rounding or keyboard access wiring.'
}
$mainCodePath = Join-Path $workspaceRoot 'src\DesktopManager.App\MainWindow.xaml.cs'
$mainCode = [System.IO.File]::ReadAllText($mainCodePath, [System.Text.Encoding]::UTF8)
if ($mainCode -notmatch 'SystemParameters\.HighContrast' `
    -or $mainCode -notmatch 'ApplyHighContrastRecursive') {
    throw 'Main window is missing high-contrast adaptation.'
}
Write-Host "Accessibility verification passed: $($xamlFiles.Count) XAML files checked."
