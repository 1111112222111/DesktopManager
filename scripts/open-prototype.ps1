$ErrorActionPreference = 'Stop'

$prototypePath = Join-Path $PSScriptRoot '..\prototypes\desktop-inbox-prototype.html'
$resolvedPrototypePath = (Resolve-Path -LiteralPath $prototypePath -ErrorAction Stop).Path
Start-Process -FilePath $resolvedPrototypePath

