# DropSpot 介绍视频：用 Windows 自带的中文语音生成配音，并混进视频（背景音乐会在说话时自动压低）
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
$video = Join-Path $root 'DropSpot-intro-v1.0.8.mp4'
$output = Join-Path $root 'DropSpot-intro-v1.0.8-配音版.mp4'
$ffmpeg = 'C:\Program Files\ffmpeg\bin\ffmpeg.exe'
if (-not (Test-Path $ffmpeg)) { $ffmpeg = 'ffmpeg' }

$lines = @(
  @(0.3,  '报表导出了，然后呢？'),
  @(3.2,  '它存哪儿了？'),
  @(6.2,  '别翻了，看右下角。'),
  @(8.4,  'DropSpot，专门盯着你刚存的文件。'),
  @(13.4, '一保存就提醒，双击直达。'),
  @(19.4, '鼠标一放，最近的文件一眼看完。'),
  @(26.4, '找到了？直接拖去发。'),
  @(31.4, '常用的，拖进来就收藏。'),
  @(37.4, '快捷键，一键打开。'),
  @(42.4, '垃圾文件，自动藏好。'),
  @(47.4, '嫌碍眼？贴边藏起来。'),
  @(52.4, '刚改过的，马上找到；常用的，一点就到。'),
  @(56.4, 'DropSpot。以后找文件，看右下角就行。')
)

Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voice = $synth.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -like 'zh-*' } | Select-Object -First 1
if (-not $voice) { throw '没有找到中文语音。请在“设置 → 时间和语言 → 语音”里添加中文（简体）语音后再运行。' }
$synth.SelectVoice($voice.VoiceInfo.Name)
$synth.Rate = 1
Write-Output "使用语音：$($voice.VoiceInfo.Name)"

$tmp = Join-Path $root 'voice-tmp'
New-Item -ItemType Directory -Force $tmp | Out-Null
$inputs = @('-i', $video)
$filters = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
  $wav = Join-Path $tmp ('line{0:D2}.wav' -f $i)
  $synth.SetOutputToWaveFile($wav)
  $synth.Speak(($lines[$i][1] -replace 'DropSpot', 'Drop Spot'))
  $synth.SetOutputToNull()
  $inputs += @('-i', $wav)
  $ms = [int]($lines[$i][0] * 1000)
  $filters += "[$($i + 1):a]aresample=48000,aformat=channel_layouts=stereo,adelay=${ms}|${ms},volume=1.6[v$i]"
}
$synth.Dispose()
$mixIn = ($(for ($i = 0; $i -lt $lines.Count; $i++) { "[v$i]" }) -join '')
$filters += "${mixIn}amix=inputs=$($lines.Count):normalize=0[vo]"
$filters += '[vo]asplit=2[vo1][vo2]'
$filters += '[0:a]volume=0.9[bg]'
$filters += '[bg][vo1]sidechaincompress=threshold=0.02:ratio=8:attack=15:release=350[duck]'
$filters += '[duck][vo2]amix=inputs=2:normalize=0,alimiter=limit=0.95[out]'
$graph = $filters -join ';'

& $ffmpeg -y @inputs -filter_complex $graph -map 0:v -map '[out]' -c:v copy -c:a aac -b:a 192k -movflags +faststart $output
if ($LASTEXITCODE -ne 0) { throw '混音失败' }
Remove-Item -Recurse -Force $tmp
Write-Output "已生成：$output"
