<#
  Быстрая проверка компиляции Unity-кода без запуска редактора.

  Берёт сгенерированные Unity *.csproj из основного чекаута (ссылки на DLL движка и пакетов),
  подменяет в них списки файлов на globs по папкам сборок указанного дерева (например, worktree)
  и собирает dotnet build во временной папке. Новые файлы подхватываются автоматически.

  Пример:  powershell -File Tools/Dev/CompileCheck.ps1 -Root D:\path\to\worktree
  Ограничения: не видит изменений в asmdef-ссылках и новых пакетах; финальная проверка — Unity (unity recompile).
#>
param(
    [string]$Root = (Resolve-Path "$PSScriptRoot\..\..").Path,
    [string]$Reference = "D:\Projects\ColorrAction"
)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path $Root).Path

# Сборка -> папка с исходниками (относительно корня) и исключения.
$map = [ordered]@{
    'GameCore'                 = @{ Dir = 'Packages/com.template.gamecore/Runtime' }
    'GameCore.Newtonsoft'      = @{ Dir = 'Packages/com.template.gamecore/Newtonsoft' }
    'GameCore.Unity'           = @{ Dir = 'Packages/com.template.gamecore.unity/Runtime' }
    'GameCore.Unity.Editor'    = @{ Dir = 'Packages/com.template.gamecore.unity/Editor' }
    'ColorsAction.Game'        = @{ Dir = 'Assets/Game/Logic' }
    'ColorsAction.Unity'       = @{ Dir = 'Assets/Game/Scripts'; Exclude = 'Assets/Game/Scripts/Editor/**' }
    'ColorsAction.Unity.Editor'= @{ Dir = 'Assets/Game/Scripts/Editor'; Exclude = 'Assets/Game/Scripts/Editor/LevelEditor/**' }
    'ColorsAction.LevelEditor' = @{ Dir = 'Assets/Game/Scripts/Editor/LevelEditor' }
}

$hash = [Math]::Abs($Root.GetHashCode())
$work = Join-Path $env:TEMP "colors_compile_$hash"
New-Item -ItemType Directory -Force $work | Out-Null

foreach ($name in $map.Keys) {
    $src = Join-Path $Reference "$name.csproj"
    if (-not (Test-Path $src)) { throw "Нет $src — откройте проект в Unity один раз, чтобы он сгенерировал csproj." }
    $xml = Get-Content $src -Raw -Encoding UTF8
    $xml = [regex]::Replace($xml, '\s*<Compile Include="[^"]*"\s*/>', '')
    $xml = [regex]::Replace($xml, '\s*<None Include="[^"]*"\s*/>', '')
    $dir = (Join-Path $Root $map[$name].Dir) -replace '/', '\'
    $exclude = ''
    if ($map[$name].Exclude) { $exclude = ' Exclude="' + ((Join-Path $Root $map[$name].Exclude) -replace '/', '\') + '"' }
    $item = "  <ItemGroup>`n    <Compile Include=`"$dir\**\*.cs`"$exclude />`n  </ItemGroup>`n"
    $xml = $xml -replace '(<Import Project="\$\(MSBuildToolsPath\)\\Microsoft\.CSharp\.targets" />)', "$item`$1"
    # Нечего собирать — пусто: пропускаем сборки без исходников (кроме тех, на которые ссылаются).
    Set-Content -Path (Join-Path $work "$name.csproj") -Value $xml -Encoding UTF8
}

$out = Join-Path $work 'out'
$failed = $false
foreach ($top in 'ColorsAction.LevelEditor', 'ColorsAction.Unity.Editor') {
    $log = & dotnet build (Join-Path $work "$top.csproj") -nologo -v q -clp:NoSummary "-p:OutputPath=$out\bin\" "-p:BaseIntermediateOutputPath=$out\obj\$top\" 2>&1
    $errors = $log | Where-Object { $_ -match ': error ' } | Sort-Object -Unique
    if ($LASTEXITCODE -ne 0 -or $errors) { $failed = $true; $errors | ForEach-Object { $_ -replace [regex]::Escape($work), '' } }
}
if ($failed) { Write-Host "COMPILE: FAILED"; exit 1 } else { Write-Host "COMPILE: OK ($Root)"; exit 0 }
