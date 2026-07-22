$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root 'Becenicha.csproj'
$publishedAppPath = Join-Path $root 'dist\hangman-game'
$launcherProjectPath = Join-Path $PSScriptRoot 'Launcher\Launcher.csproj'
$launcherOutPath = Join-Path $root 'dist\launcher'

Write-Host 'Publishing the Blazor game...'
dotnet publish $projectPath -c Release -o $publishedAppPath

Write-Host 'Building the Windows launcher exe...'
dotnet publish $launcherProjectPath -c Release -o $launcherOutPath -p:PublishSingleFile=true -p:SelfContained=false

$indexPath = Join-Path $publishedAppPath 'wwwroot\index.html'
$launcherExe = Join-Path $launcherOutPath 'HangmanGameLauncher.exe'

if (-not (Test-Path $indexPath)) {
    throw "Could not find published game entry page: $indexPath"
}

if (-not (Test-Path $launcherExe)) {
    throw "Could not find launcher exe: $launcherExe"
}

Write-Host "Launcher created at: $launcherExe"
Write-Host 'Starting the game...'
& $launcherExe $indexPath
