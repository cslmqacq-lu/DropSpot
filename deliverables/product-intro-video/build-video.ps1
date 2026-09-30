$ErrorActionPreference = 'Stop'

$ffmpeg = 'C:\Program Files\ffmpeg\bin\ffmpeg.exe'
$root = Split-Path -Parent $PSCommandPath
$scenesDir = Join-Path $root 'scenes'
$output = Join-Path $root 'folder-workflow-intro.mp4'
$captions = Join-Path $root 'captions.ass'

New-Item -ItemType Directory -Force $scenesDir | Out-Null

$temp = $env:TEMP
$scenes = @(
    @{ Path = (Join-Path $temp 'DiskWriteWatcher-hidden-scrollbars-front.png'); Duration = 6 },
    @{ Path = (Join-Path $temp 'DiskWriteWatcher-hidden-scrollbars-front.png'); Duration = 7 },
    @{ Path = (Join-Path $temp 'DiskWriteWatcher-hidden-scrollbars-front.png'); Duration = 7 },
    @{ Path = (Join-Path $root 'expanded-active-folder.png'); Duration = 7 },
    @{ Path = (Join-Path $temp 'DiskWriteWatcher-hidden-scrollbars-front.png'); Duration = 7 },
    @{ Path = (Join-Path $temp 'DiskWriteWatcher-background-only-opacity.png'); Duration = 7 },
    @{ Path = (Join-Path $temp 'DiskWriteWatcher-favorite-hover-stable.png'); Duration = 7 },
    @{ Path = (Join-Path $temp 'DiskWriteWatcher-right-tooltip-70.png'); Duration = 7 },
    @{ Path = (Join-Path $temp 'DiskWriteWatcher-menu-now.png'); Duration = 7 }
)

$sceneFiles = [System.Collections.Generic.List[string]]::new()
for ($index = 0; $index -lt $scenes.Count; $index++) {
    $scene = $scenes[$index]
    if (-not (Test-Path $scene.Path)) {
        throw "Video source was not found: $($scene.Path)"
    }

    $sceneFile = Join-Path $scenesDir ('scene-{0:D2}.mp4' -f $index)
    $filter = "scale=960:1120:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:300:color=0x0B111A,fade=t=in:st=0:d=0.35,fade=t=out:st=$($scene.Duration - 0.35):d=0.35"
    & $ffmpeg -y -loop 1 -i $scene.Path -t $scene.Duration -vf $filter -r 30 -c:v libx264 -preset medium -crf 19 -pix_fmt yuv420p -an $sceneFile
    if ($LASTEXITCODE -ne 0) {
        throw "场景 $index 渲染失败"
    }

    $sceneFiles.Add($sceneFile)
}

$concatFile = Join-Path $root 'concat.txt'
$concatLines = foreach ($sceneFile in $sceneFiles) {
    "file '$($sceneFile.Replace("'", "''"))'"
}
[System.IO.File]::WriteAllLines($concatFile, [string[]]$concatLines, [System.Text.UTF8Encoding]::new($false))

$captionPath = $captions.Replace('\', '/').Replace(':', '\:')
$subtitleFilter = "subtitles='$captionPath'"
& $ffmpeg -y -f concat -safe 0 -i $concatFile -f lavfi -i 'anullsrc=channel_layout=stereo:sample_rate=48000' -vf $subtitleFilter -c:v libx264 -preset medium -crf 19 -pix_fmt yuv420p -c:a aac -b:a 128k -shortest -movflags +faststart $output
if ($LASTEXITCODE -ne 0) {
    throw '视频合成失败'
}

Write-Output "视频已生成：$output"
