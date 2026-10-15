$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (Test-Path -LiteralPath (Join-Path $root '.git')) {
    $trackedFiles = & git -C $root ls-files
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect tracked project files.' }
    $localFiles = @($trackedFiles | Where-Object { $_ -match '(^|/)(\.agents/|AGENTS(?:\.local)?\.md$)' })
    if ($localFiles.Count) { throw "Local working files must not be tracked: $($localFiles -join ', ')" }
}
$sourceFiles = Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { $_.Extension -in '.cs','.xaml' -and $_.FullName -notmatch '\\(bin|obj|artifacts|tests|tools|\.agents)\\' }
$commands = foreach ($file in $sourceFiles | Where-Object Extension -eq '.cs') {
    foreach ($match in [regex]::Matches([IO.File]::ReadAllText($file.FullName), 'CommandMethod\("([^"]+)"')) { $match.Groups[1].Value }
}
$expected = @('Align_Den','AUTONUM','НУМ','WSELECT','SUMLENGTH','MULTICOPY','PLANPREP','CLEANDWG','CLEANUNSELECTED','STRIPTEXT','AUTOCADTOOLS_STAMP','AUTOCADTOOLS_BATCHPLOT','WPLOT','AUTOCADTOOLS_CUSTOMERORDER','AUTOCADTOOLS_TABLEEXCEL','AUTOCADTOOLS_BLOCKS_STAGE1','AUTOCADTOOLS_INFO','AUTOCADTOOLS_CHECKUPDATES','AUTOCADTOOLS_INSERTFORMAT')
if (@($expected | Where-Object { $_ -notin $commands }).Count -or @($commands | Group-Object | Where-Object Count -gt 1).Count) { throw 'Missing or duplicate commands.' }
$xmlFiles = Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { $_.Extension -in '.xaml','.xml','.csproj','.props' -and $_.FullName -notmatch '\\(bin|obj|artifacts|tests|\.agents)\\' }
foreach ($file in $xmlFiles) { $null = [xml][IO.File]::ReadAllText($file.FullName) }
$app = [IO.File]::ReadAllText((Join-Path $root 'App.cs'))
$iconSource = [IO.File]::ReadAllText((Join-Path $root 'Views\ToolIcons.cs'))
$iconCommands = @([regex]::Matches($iconSource, '\["([^"]+)"\]') | ForEach-Object { $_.Groups[1].Value })
foreach ($match in [regex]::Matches($app, 'AddButton\([^;]+,\s*"([^"]+)"\);')) {
    if ($match.Groups[1].Value -notin $iconCommands) { throw "Missing vector icon: $($match.Groups[1].Value)" }
}
$vm = [IO.File]::ReadAllText((Join-Path $root 'ViewModels\StampPropsViewModel.cs'))
$xaml = [IO.File]::ReadAllText((Join-Path $root 'Views\StampPropsControl.xaml'))
foreach ($match in [regex]::Matches($xaml, 'FieldsMap\[([^\]]+)\]')) {
    if (!$vm.Contains('"' + $match.Groups[1].Value + '"')) { throw "Unmatched stamp binding: $($match.Groups[1].Value)" }
}
Write-Output "PASS: $(@($commands).Count) commands; $(@($xmlFiles).Count) XML/XAML; namespaces, vector icons and stamp bindings."
