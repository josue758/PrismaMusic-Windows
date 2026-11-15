param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\PrismaMusic.Windows'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Prisma Music.lnk'
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PrismaMusic.Windows'
$exe = Join-Path $installDir 'Prisma Music.exe'
if (Get-Process -Name 'Prisma Music' -ErrorAction SilentlyContinue) {
    throw 'Cierra Prisma Music antes de instalar o desinstalar.'
}
if ($Uninstall) {
    $expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\PrismaMusic.Windows'))
    if (Test-Path -LiteralPath $installDir) {
        $actual = Get-Item -LiteralPath $installDir
        if ($actual.FullName -ne $expected -or ($actual.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'La carpeta de instalación no coincide con la ruta esperada.'
        }
        Remove-Item -LiteralPath $expected -Recurse -Force
    }
    Remove-Item -LiteralPath $shortcut,$registry -ErrorAction SilentlyContinue
    Write-Host 'Prisma Music fue desinstalado. Se conservó la biblioteca.'
    exit
}
$payload = Join-Path $PSScriptRoot 'App'
if (-not (Test-Path -LiteralPath (Join-Path $payload 'Prisma Music.exe'))) {
    throw 'Extrae el ZIP completo antes de ejecutar el instalador.'
}
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Get-ChildItem -LiteralPath $payload | Copy-Item -Destination $installDir -Recurse -Force
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $installDir 'Install.ps1') -Force
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $installDir
$link.Save()
New-Item -Path $registry -Force | Out-Null
$command = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}" -Uninstall' -f (Join-Path $installDir 'Install.ps1')
$values = @{ DisplayName='Prisma Music'; DisplayVersion='0.54.1'; InstallLocation=$installDir; DisplayIcon=$exe; UninstallString=$command }
foreach ($entry in $values.GetEnumerator()) {
    New-ItemProperty -Path $registry -Name $entry.Key -Value $entry.Value -PropertyType String -Force | Out-Null
}
Write-Host 'Prisma Music está disponible en el menú Inicio.'




























