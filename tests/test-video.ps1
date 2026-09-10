# Автотест записи видео через приложение: -v, выделение области, Enter (старт), 4 с, стоп кнопкой панели.
Add-Type -AssemblyName System.Drawing,System.Windows.Forms,UIAutomationClient,UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class MI4 { [DllImport("user32.dll")] public static extern void mouse_event(uint f,int dx,int dy,uint d,UIntPtr e); [DllImport("user32.dll")] public static extern void keybd_event(byte vk,byte sc,uint f,UIntPtr e); }
"@
$out="D:\Work\Kadr\docs\kadr-screens"; $exe="D:\Work\Kadr\code\src\Kadr.App\bin\Debug\net8.0-windows10.0.22621.0\Kadr.exe"
function Shot($name){ $b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $bmp=New-Object System.Drawing.Bitmap($b.Width,$b.Height); $g=[System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen(0,0,0,0,$bmp.Size); $bmp.Save("$out\$name.png"); $g.Dispose(); $bmp.Dispose() }
function MoveTo($x,$y){ [System.Windows.Forms.Cursor]::Position=New-Object System.Drawing.Point($x,$y); Start-Sleep -Milliseconds 60 }
function Drag($x1,$y1,$x2,$y2){ MoveTo $x1 $y1; [MI4]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 120; $steps=15; for($i=1;$i -le $steps;$i++){ MoveTo ([int]($x1+($x2-$x1)*$i/$steps)) ([int]($y1+($y2-$y1)*$i/$steps)); Start-Sleep -Milliseconds 20 }; Start-Sleep -Milliseconds 120; [MI4]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 400 }
function Key($vk){ [MI4]::keybd_event($vk,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [MI4]::keybd_event($vk,0,2,[UIntPtr]::Zero); Start-Sleep -Milliseconds 300 }
function FindKadrButton($index){ $root=[System.Windows.Automation.AutomationElement]::RootElement; $kpid=(Get-Process Kadr | Select-Object -First 1).Id; $wc=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$kpid); foreach($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children,$wc)){ $bc=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button); $btns=$w.FindAll([System.Windows.Automation.TreeScope]::Descendants,$bc); $vis=@(); foreach($b in $btns){ if(-not $b.Current.IsOffscreen){ $vis+=$b } }; if($vis.Count -gt $index){ return $vis[$index] } }; $null }
Get-Process Kadr,Kadr.Recorder -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$before = Get-ChildItem "$env:USERPROFILE\Desktop" -Filter 'Видео-*.mp4' | Select-Object -ExpandProperty Name
Start-Process $exe -ArgumentList '-s'; Start-Sleep 4
& $exe -v; Start-Sleep 2.5
Drag 500 250 1300 750; Start-Sleep 1.2; Shot "kadr-video-01-prestart"
Key 0x0D; Start-Sleep 3; Shot "kadr-video-02-recording"
Start-Sleep 2
$stop = FindKadrButton 0
if($stop){ $stop.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); "stop invoked" } else { "stop button not found" }
Start-Sleep 4
$after = Get-ChildItem "$env:USERPROFILE\Desktop" -Filter 'Видео-*.mp4' | Where-Object { $before -notcontains $_.Name }
"NEW: " + (($after | ForEach-Object { "$($_.Name) $($_.Length) bytes" }) -join '; ')
& $exe -u; Start-Sleep 2
Get-Content "$env:LOCALAPPDATA\Kadr\log\main-$(Get-Date -Format yyyy-MM-dd).log" -Encoding utf8 | Select-Object -Last 12
"--- video log"; Get-Content "$env:LOCALAPPDATA\Kadr\log\video-$(Get-Date -Format yyyy-MM-dd).log" -Encoding utf8 | Select-Object -Last 8
