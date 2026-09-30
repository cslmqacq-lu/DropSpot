param(
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$ProjectRoot = [System.IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$ProjectFile = Join-Path $ProjectRoot "DropSpot.csproj"
$ArtifactsRoot = Join-Path $ProjectRoot "artifacts"
$DistRoot = Join-Path $ProjectRoot "dist"

function Reset-ProjectDirectory([string]$Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $prefix = $ProjectRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理项目目录之外的路径：$fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
}

[xml]$projectXml = Get-Content -LiteralPath $ProjectFile -Raw -Encoding UTF8
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "无法从项目文件读取版本号"
}

$portableName = "DropSpot_v${version}_win-x64"
$portableRoot = Join-Path $ArtifactsRoot "portable"
$portableDir = Join-Path $portableRoot $portableName
$portableZip = Join-Path $DistRoot "${portableName}_portable.zip"
$installerName = "DropSpot_Setup_v${version}_win-x64.exe"
$installerPath = Join-Path $DistRoot $installerName
$checksumsPath = Join-Path $DistRoot "SHA256SUMS.txt"

New-Item -ItemType Directory -Path $ArtifactsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $DistRoot -Force | Out-Null
New-Item -ItemType Directory -Path $portableRoot -Force | Out-Null
Reset-ProjectDirectory $portableDir

Push-Location $ProjectRoot
try {
    dotnet build $ProjectFile -c Release
    if ($LASTEXITCODE -ne 0) { throw "Release 构建失败" }

    if (-not $SkipTests) {
        $testExe = Join-Path $ProjectRoot "bin\Release\net8.0-windows\DropSpot.exe"
        $test = Start-Process -FilePath $testExe -ArgumentList "--smoke-test" -WindowStyle Hidden -Wait -PassThru
        if ($test.ExitCode -ne 0) { throw "Smoke test 失败，退出码：$($test.ExitCode)" }
    }

    $publishArguments = @(
        'publish'
        $ProjectFile
        '-c'
        'Release'
        '-r'
        'win-x64'
        '--self-contained'
        'true'
        '-o'
        $portableDir
        '-p:PublishSingleFile=true'
        '-p:IncludeNativeLibrariesForSelfExtract=true'
        '-p:EnableCompressionInSingleFile=true'
        '-p:DebugType=None'
        '-p:DebugSymbols=false'
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw "单文件发布失败" }

    $readmeSource = Join-Path $PSScriptRoot 'README-portable.txt'
    $readmeDestination = Join-Path $portableDir 'README.txt'
    $readmeContent = (Get-Content -LiteralPath $readmeSource -Raw -Encoding UTF8).Replace('{VERSION}', $version)
    Set-Content -LiteralPath $readmeDestination -Value $readmeContent -Encoding UTF8

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipCreated = $false
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            if (Test-Path -LiteralPath $portableZip) {
                Remove-Item -LiteralPath $portableZip -Force
            }

            [System.IO.Compression.ZipFile]::CreateFromDirectory(
                $portableDir,
                $portableZip,
                [System.IO.Compression.CompressionLevel]::Optimal,
                $false)
            $zipCreated = $true
            break
        }
        catch [System.IO.IOException] {
            if ($attempt -eq 10) { throw }
            Start-Sleep -Seconds 1
        }
    }
    if (-not $zipCreated) { throw "便携版 ZIP 生成失败" }

    $innoCandidates = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    $iscc = $innoCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $iscc) { throw "未找到 Inno Setup 6 编译器 ISCC.exe" }

    & $iscc "/DMyAppVersion=$version" (Join-Path $ProjectRoot "installer\DropSpot.iss")
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installerPath)) {
        throw "安装包生成失败"
    }

    $hashLines = foreach ($file in @($portableZip, $installerPath)) {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $file
        "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($file))"
    }
    Set-Content -LiteralPath $checksumsPath -Value $hashLines -Encoding ASCII

    Write-Output "VERSION=$version"
    Write-Output "PORTABLE=$portableZip"
    Write-Output "INSTALLER=$installerPath"
    Write-Output "CHECKSUMS=$checksumsPath"
}
finally {
    Pop-Location
}
