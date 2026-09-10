Add-Type -AssemblyName System.Drawing,System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class MI5 { [DllImport("user32.dll")] public static extern void mouse_event(uint f,int dx,int dy,uint d,UIntPtr e); [DllImport("user32.dll")] public static extern void keybd_event(byte vk,byte sc,uint f,UIntPtr e); }
"@
$out="D:\Work\Kadr\docs\kadr-screens"; $exe="D:\Work\Kadr\code\src\Kadr.App\bin\Debug\net8.0-windows10.0.22621.0\Kadr.exe"
function Shot($name){ $b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $bmp=New-Object System.Drawing.Bitmap($b.Width,$b.Height); $g=[System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen(0,0,0,0,$bmp.Size); $bmp.Save("$out\$name.png"); $g.Dispose(); $bmp.Dispose() }
function MoveTo($x,$y){ [System.Windows.Forms.Cursor]::Position=New-Object System.Drawing.Point($x,$y); Start-Sleep -Milliseconds 60 }
function Click($x,$y){ MoveTo $x $y; [MI5]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [MI5]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 400 }
function Drag($x1,$y1,$x2,$y2){ MoveTo $x1 $y1; [MI5]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 120; $steps=15; for($i=1;$i -le $steps;$i++){ MoveTo ([int]($x1+($x2-$x1)*$i/$steps)) ([int]($y1+($y2-$y1)*$i/$steps)); Start-Sleep -Milliseconds 20 }; Start-Sleep -Milliseconds 120; [MI5]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 400 }
function Key($vk){ [MI5]::keybd_event($vk,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [MI5]::keybd_event($vk,0,2,[UIntPtr]::Zero); Start-Sleep -Milliseconds 300 }
Get-Process Kadr,Kadr.Recorder -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Process "D:\Work\Kadr\code\tests\scroll-test.html"; Start-Sleep 4
Click 900 600; Start-Sleep 1
$before = Get-ChildItem "$env:USERPROFILE\Desktop" -Filter 'Скриншот-*.png' | Select-Object -ExpandProperty Name
Start-Process $exe -ArgumentList '-s'; Start-Sleep 4
& $exe -c; Start-Sleep 2.5
Drag 450 200 1450 950; Start-Sleep 1; Shot "kadr-scroll-01-start"
Key 0x0D; Start-Sleep 1; MoveTo 900 550; Start-Sleep 6; Shot "kadr-scroll-02-capturing"
MoveTo 200 1000; Start-Sleep 1.5; Shot "kadr-scroll-03-hint"
Key 0x0D; Start-Sleep 4
$after = Get-ChildItem "$env:USERPROFILE\Desktop" -Filter 'Скриншот-*.png' | Where-Object { $before -notcontains $_.Name }
foreach($f in $after){ $img=[System.Drawing.Image]::FromFile($f.FullName); "NEW: $($f.Name) $($img.Width)x$($img.Height) $($f.Length) bytes"; $img.Dispose(); Move-Item $f.FullName "$out\kadr-scroll-result.png" -Force }
& $exe -u; Start-Sleep 1
Click 900 600; [MI5]::keybd_event(0x11,0,0,[UIntPtr]::Zero); Key 0x57; [MI5]::keybd_event(0x11,0,2,[UIntPtr]::Zero)
Get-Content "$env:LOCALAPPDATA\Kadr\log\main-$(Get-Date -Format yyyy-MM-dd).log" -Encoding utf8 | Select-String 'Прокрутка|ERROR|прокрут' | Select-Object -Last 4 | ForEach-Object { $_.Line }
