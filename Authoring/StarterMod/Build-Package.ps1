param(
    [Parameter(Mandatory)][string]$GameDir,
    [string]$OutputDirectory=(Join-Path $PSScriptRoot 'dist'),
    [string]$ProjectFile='MyTS3Mod.csproj'
)
$ErrorActionPreference='Stop'
$GameDir=(Resolve-Path -LiteralPath $GameDir).Path
$meta=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'mod-project.json') -Raw -Encoding UTF8|ConvertFrom-Json
$parsedVersion=$null
if($meta.Version -isnot [string] -or ![Version]::TryParse($meta.Version,[ref]$parsedVersion)){throw 'Invalid package version.'}
if($meta.Id -notmatch '^[a-z0-9]+(?:[.-][a-z0-9]+)+$' -or $meta.Id.Length -gt 100){throw 'Choose a valid, unique package ID.'}
$dependencyIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($dependency in @($meta.Dependencies)){
    $minimum=$null
    if($dependency.Id -notmatch '^[a-z0-9]+(?:[.-][a-z0-9]+)+$' -or $dependency.Id -eq $meta.Id -or
       $dependency.MinimumVersion -isnot [string] -or ![Version]::TryParse($dependency.MinimumVersion,[ref]$minimum) -or !$dependencyIds.Add($dependency.Id)){
        throw 'Dependencies need unique IDs, valid string versions and cannot refer to the mod itself.'
    }
}
$projectPath=Join-Path $PSScriptRoot $ProjectFile
if(!(Test-Path -LiteralPath $projectPath)){throw 'Project file not found.'}
[xml]$project=Get-Content -LiteralPath $projectPath -Raw
$assemblyName=[string]$project.Project.PropertyGroup.AssemblyName
if($assemblyName -notmatch '^[A-Za-z0-9_.-]+$'){throw 'Declare one simple AssemblyName in the project file.'}
$framework=@($meta.Dependencies|Where-Object Id -eq 'nl.ts3modding.loader')
if($framework.Count -ne 1 -or [Version]$framework[0].MinimumVersion -lt [Version]'0.8.19'){throw 'Declare framework 0.8.19 or newer in mod-project.json.'}
$managed=Join-Path $GameDir 'Tower! Simulator 3_Data/Managed'
$cecil=Join-Path $GameDir 'BepInEx/core/Mono.Cecil.dll'
$frameworkDll=Join-Path $GameDir 'BepInEx/plugins/TS3ModLoader/TS3ModLoader.dll'
foreach($path in @($cecil,$frameworkDll,(Join-Path $managed 'Assembly-CSharp.dll'))){if(!(Test-Path -LiteralPath $path)){throw "Missing installed build dependency: $path"}}
Add-Type -Path $cecil
function ReadPlugins([string]$path){
    $resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
    $resolver.AddSearchDirectory((Join-Path $GameDir 'BepInEx/core'))
    $resolver.AddSearchDirectory($managed)
    $resolver.AddSearchDirectory((Split-Path -Parent $path))
    $parameters=[Mono.Cecil.ReaderParameters]::new();$parameters.AssemblyResolver=$resolver
    $assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($path,$parameters)
    try{
        foreach($type in $assembly.MainModule.Types){
            foreach($attribute in @($type.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'BepInEx.BepInPlugin'})){
                $dependencies=@(foreach($dependency in @($type.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'BepInEx.BepInDependency'})){
                    $minimum='0.0.0'
                    if($dependency.ConstructorArguments.Count -gt 1){
                        if($dependency.ConstructorArguments[1].Type.FullName -eq 'System.String'){$minimum=[string]$dependency.ConstructorArguments[1].Value}
                        elseif(([int]$dependency.ConstructorArguments[1].Value -band 2) -ne 0){continue}
                    }
                    [pscustomobject]@{Id=[string]$dependency.ConstructorArguments[0].Value;MinimumVersion=$minimum}
                })
                [pscustomobject]@{Id=[string]$attribute.ConstructorArguments[0].Value;Name=[string]$attribute.ConstructorArguments[1].Value;Version=[string]$attribute.ConstructorArguments[2].Value;Dependencies=$dependencies}
            }
        }
    }finally{$assembly.Dispose();$resolver.Dispose()}
}
$installed=@(ReadPlugins $frameworkDll|Where-Object Id -eq 'nl.ts3modding.loader')
if($installed.Count -ne 1 -or [Version]$installed[0].Version -lt [Version]$framework[0].MinimumVersion){throw 'Install the declared minimum framework version before building.'}
$out=[IO.Path]::GetFullPath($OutputDirectory)
$zip=Join-Path $out ($meta.Id+'-'+$meta.Version+'.zip')
if(Test-Path -LiteralPath $zip){throw 'This package version already exists. Increase Version; the earlier ZIP will not be overwritten.'}
& dotnet build $projectPath -c Release "-p:GameDir=$GameDir" "-p:Version=$($meta.Version)"
if($LASTEXITCODE -ne 0){throw 'Build failed.'}
$dll=Join-Path $PSScriptRoot ('bin/Release/net46/'+$assemblyName+'.dll')
$plugins=@(ReadPlugins $dll)
if($plugins.Count -ne 1 -or $plugins[0].Id -cne $meta.Id -or $plugins[0].Name -cne $meta.Name -or $plugins[0].Version -cne $meta.Version){throw 'Compiled plugin identity must exactly match mod-project.json.'}
$hard=@($plugins[0].Dependencies)
if($hard.Count -ne @($meta.Dependencies).Count){throw 'Hard BepInDependency attributes and package Dependencies must match; keep optional providers soft.'}
foreach($dependency in @($meta.Dependencies)){
    $match=@($hard|Where-Object {$_.Id -ceq $dependency.Id -and $_.MinimumVersion -eq $dependency.MinimumVersion})
    if($match.Count -ne 1){throw "Hard dependency mismatch: $($dependency.Id). Add/update its BepInDependency attribute."}
}
$stage=Join-Path $out ('stage-'+[guid]::NewGuid().ToString('N'))
$payload=Join-Path $stage 'payload'
New-Item -ItemType Directory -Path $payload -Force|Out-Null
$files=[ordered]@{}
function Include([string]$source,[string]$name){Copy-Item -LiteralPath $source -Destination (Join-Path $payload $name);$files[$name]=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash}
Include $dll ($assemblyName+'.dll')
Include (Join-Path $PSScriptRoot 'TEMPLATE-LICENSE.txt') 'TEMPLATE-LICENSE.txt'
$presentation=[ordered]@{SchemaVersion=1;Id=$meta.Id;Version=$meta.Version;Name=$meta.Name;Description=$meta.Description;Keybinds=@($meta.Keybinds|Where-Object {$null -ne $_});Commands=@($meta.Commands|Where-Object {$null -ne $_})}
$icon=Join-Path $PSScriptRoot 'icon.png'
if(Test-Path -LiteralPath $icon){
    if((Get-Item -LiteralPath $icon).Length -gt 262144){throw 'icon.png must be at most 256 KiB.'}
    Include $icon 'icon.png';$presentation.Icon='icon.png'
}
$encoding=[Text.UTF8Encoding]::new($false)
$info=Join-Path $payload 'mod-info.json'
[IO.File]::WriteAllText($info,($presentation|ConvertTo-Json -Depth 8),$encoding)
$files['mod-info.json']=(Get-FileHash -LiteralPath $info -Algorithm SHA256).Hash
$hash=(Get-FileHash -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll') -Algorithm SHA256).Hash
$manifest=[ordered]@{SchemaVersion=1;Id=$meta.Id;Name=$meta.Name;Version=$meta.Version;Dependencies=@($meta.Dependencies);SupportedGameAssemblyHashes=@($hash);Files=$files}
[IO.File]::WriteAllText((Join-Path $stage 'mod.json'),($manifest|ConvertTo-Json -Depth 8),$encoding)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
try{
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $stage 'mod.json'),'mod.json')|Out-Null
    foreach($name in $files.Keys){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $payload $name),('payload/'+$name))|Out-Null}
}finally{$archive.Dispose()}
"Built package: $zip"
"Build hash recorded for this installed game build. Test behavior before declaring compatibility or sharing."
