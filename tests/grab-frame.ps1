# Извлечь кадр из MP4 через WPF MediaPlayer: grab-frame.ps1 <mp4> <png> [секунда]
param([string]$Video, [string]$Png, [double]$At = 1.0)
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$mp = New-Object System.Windows.Media.MediaPlayer
$mp.ScrubbingEnabled = $true
$mp.Open([uri]$Video)
$t = 0
function Pump { [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([action]{}, [System.Windows.Threading.DispatcherPriority]::Background) }
while (-not $mp.NaturalDuration.HasTimeSpan -and $t -lt 50) { Start-Sleep -Milliseconds 100; Pump; $t++ }
$mp.Position = [TimeSpan]::FromSeconds($At)
$mp.Play(); Start-Sleep -Milliseconds 600; Pump; $mp.Pause(); Start-Sleep -Milliseconds 300; Pump
$w = $mp.NaturalVideoWidth; $hh = $mp.NaturalVideoHeight
$dv = New-Object System.Windows.Media.DrawingVisual
$dc = $dv.RenderOpen(); $dc.DrawVideo($mp, (New-Object System.Windows.Rect(0, 0, $w, $hh))); $dc.Close()
$rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($w, $hh, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
$rtb.Render($dv)
$enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
$fs = [IO.File]::Create($Png); $enc.Save($fs); $fs.Close()
$mp.Close()
"frame: ${w}x${hh} -> $Png (duration $($mp.NaturalDuration))"
