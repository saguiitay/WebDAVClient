$ErrorActionPreference = 'Stop'
$directory = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
$fixture = Join-Path $directory 'CHANGELOG.md'
New-Item -ItemType Directory -Path $directory | Out-Null

try
{
    @'
# Changelog

## [2.0.0] - 2026-10-21

- Current change.

## [1.0.0] - 2026-10-20

- Older change.
'@ | Set-Content $fixture
    $notes = & "$PSScriptRoot\Get-ReleaseNotes.ps1" -Version '2.0.0' -ChangelogPath $fixture
    if (-not $notes.Contains('- Current change.') -or $notes.Contains('Older change.'))
    {
        throw 'Release notes must contain only the requested version.'
    }
    if (-not $notes.Contains('https://www.nuget.org/packages/WebDAVClient/2.0.0'))
    {
        throw 'Release notes must link to the exact NuGet version.'
    }
    $notes = & "$PSScriptRoot\Get-ReleaseNotes.ps1" -Version '1.0.0' -ChangelogPath $fixture
    if (-not $notes.Contains('- Older change.') -or $notes.Contains('Current change.'))
    {
        throw 'The final changelog entry must also be selectable.'
    }

    $cases = @(
        @{ Content = "## [1.0.0]`n- Other version."; Version = '2.0.0' },
        @{ Content = "## [2.0.0]`n `n## [1.0.0]`n- Other version."; Version = '2.0.0' },
        @{ Content = "## [2.0.0]`n- First.`n## [2.0.0]`n- Duplicate."; Version = '2.0.0' },
        @{ Content = "## [2.0.0]`n- Change."; Version = '2.0' }
    )
    foreach ($case in $cases)
    {
        $case.Content | Set-Content $fixture
        $rejected = $false
        try
        {
            & "$PSScriptRoot\Get-ReleaseNotes.ps1" -Version $case.Version -ChangelogPath $fixture | Out-Null
        }
        catch
        {
            $rejected = $true
        }
        if (-not $rejected)
        {
            throw "Invalid release notes were accepted: $($case.Content)"
        }
    }
}
finally
{
    Remove-Item -LiteralPath $fixture -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $directory
}

Write-Output 'Release-note checks passed.'
