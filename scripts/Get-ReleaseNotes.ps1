[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [string] $ChangelogPath = (Join-Path $PSScriptRoot '..\CHANGELOG.md')
)

$ErrorActionPreference = 'Stop'
$changelog = Get-Content -LiteralPath $ChangelogPath -Raw
$pattern = '(?ms)^## \[' + [regex]::Escape($Version) + '\][^\r\n]*\r?\n(?<notes>.*?)(?=^## \[|\z)'
$sections = [regex]::Matches($changelog, $pattern)
if ($sections.Count -ne 1 -or [string]::IsNullOrWhiteSpace($sections[0].Groups['notes'].Value))
{
    throw "Expected exactly one nonempty CHANGELOG.md entry for $Version."
}

$sections[0].Groups['notes'].Value.Trim() + "`n`n[Install WebDAVClient $Version from NuGet](https://www.nuget.org/packages/WebDAVClient/$Version)."
