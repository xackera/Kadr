# Автотест паузы и отмены записи: старт, пауза 2 с, продолжение, стоп (ожидаем ~4 с); затем старт и отмена с подтверждением.
Add-Type -AssemblyName System.Drawing,System.Windows.Forms,UIAutomationClient,UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class MI6 { [DllImport("user32.dll")] public static extern void mouse_event(uint f,int dx,int dy,uint d,UIntPtr e); [DllImport("user32.dll")] public static extern void keybd_event(byte vk,byte sc,uint f,UIntPtr e); }
"@
$exe="D:\Work\Kadr\code\src\Kadr.App\bin\Debug\net8.0-windows10.0.22621.0\Kadr.exe"
function MoveTo($x,$y){ [System.Windows.Forms.Cursor]::Position=New-Object System.Drawing.Point($x,$y); Start-Sleep -Milliseconds 60 }
function Drag($x1,$y1,$x2,$y2){ MoveTo $x1 $y1; [MI6]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 120; $steps=15; for($i=1;$i -le $steps;$i++){ MoveTo ([int]($x1+($x2-$x1)*$i/$steps)) ([int]($y1+($y2-$y1)*$i/$steps)); Start-Sleep -Milliseconds 20 }; Start-Sleep -Milliseconds 120; [MI6]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 400 }
function Key($vk){ [MI6]::keybd_event($vk,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [MI6]::keybd_event($vk,0,2,[UIntPtr]::Zero); Start-Sleep -Milliseconds 300 }
function KadrButtons(){ $root=[System.Windows.Automation.AutomationElement]::RootElement; $kpid=(Get-Process Kadr | Select-Object -First 1).Id; $wc=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$kpid); $vis=@(); foreach($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children,$wc)){ $bc=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button); foreach($b in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,$bc)){ if(-not $b.Current.IsOffscreen){ $vis+=$b } } }; $vis }
function Invoke-Btn($b){ $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
Get-Process Kadr,Kadr.Recorder -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$before = Get-ChildItem "$env:USERPROFILE\Desktop" -Filter 'Видео-*.mp4' | Select-Object -ExpandProperty Name
Start-Process $exe -ArgumentList '-s'; Start-Sleep 4
# --- пауза
& $exe -v; Start-Sleep 2.5
Drag 500 250 1300 750; Start-Sleep 1.2; Key 0x0D; Start-Sleep 2.5
$b = KadrButtons; "recording buttons: $($b.Count)"; if($b.Count -ge 3){ Invoke-Btn $b[1]; "pause invoked" }   # стоп, пауза, отмена
Start-Sleep 2
$b = KadrButtons; if($b.Count -ge 3){ Invoke-Btn $b[1]; "resume invoked" }
Start-Sleep 2
$b = KadrButtons; if($b.Count -ge 1){ Invoke-Btn $b[0]; "stop invoked" }
Start-Sleep 4
$after = Get-ChildItem "$env:USERPROFILE\Desktop" -Filter 'Видео-*.mp4' | Where-Object { $before -notcontains $_.Name }
foreach($f in $after){ "PAUSE TEST FILE: $($f.Name) $($f.Length) bytes"; Move-Item $f.FullName "D:\Work\Kadr\docs\kadr-screens\kadr-video-pause.mp4" -Force }
# --- отмена
$before = Get-ChildItem "$env:USERPROFILE\Desktop" -Filter 'Видео-*.mp4' | Select-Object -ExpandProperty Name
& $exe -v; Start-Sleep 2.5
Drag 500 250 1300 750; Start-Sleep 1.2; Key 0x0D; Start-Sleep 2
$b = KadrButtons; if($b.Count -ge 3){ Invoke-Btn $b[2]; "cancel invoked" }
Start-Sleep 1.5
$root=[System.Windows.Automation.AutomationElement]::RootElement; $c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button)
$yes=$null; foreach($e in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$c)){ if($e.Current.Name -in 'Да','Yes'){ $yes=$e; break } }
if($yes){ Invoke-Btn $yes; "confirmed cancel" } else { "yes button not found" }
Start-Sleep 3
$after = Get-ChildItem "$env:USERPROFILE\Desktop" -Filter 'Видео-*.mp4' | Where-Object { $before -notcontains $_.Name }
"files after cancel: $($after.Count)"
& $exe -u; Start-Sleep 1
Get-Content "$env:LOCALAPPDATA\Kadr\log\main-$(Get-Date -Format yyyy-MM-dd).log" -Encoding utf8 | Select-String 'Запись завершена|отмен|ERROR' | Select-Object -Last 4 | ForEach-Object { $_.Line }
