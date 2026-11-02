param(
    [Parameter(Mandatory)][string]$Destination,
    [ValidateSet('Wiki','Repository')][string]$LinkStyle='Wiki'
)
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot 'wiki'
$target=[IO.Path]::GetFullPath($Destination)
if(Test-Path -LiteralPath $target){throw 'Choose a new destination folder; existing files will not be overwritten.'}
$files=@(Get-ChildItem -LiteralPath $source -Filter '*.md' -File)
if($files.Count -lt 3 -or !(Test-Path -LiteralPath (Join-Path $source 'Home.md'))){throw 'Wiki source pages are missing.'}
New-Item -ItemType Directory -Path $target|Out-Null
foreach($file in $files){
    $text=[IO.File]::ReadAllText($file.FullName)
    if($LinkStyle -eq 'Wiki'){$text=[regex]::Replace($text,'\]\(([A-Za-z0-9_-]+)\.md(#[^)]*)?\)',']($1$2)')}
    [IO.File]::WriteAllText((Join-Path $target $file.Name),$text,[Text.UTF8Encoding]::new($false))
}
"Exported $($files.Count) Markdown files to $target ($LinkStyle links). No publishing performed."
