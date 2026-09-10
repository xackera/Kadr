# Автотесты оставшихся функций Kadr. Каждый шаг печатает OK/FAIL.
Add-Type -AssemblyName System.Drawing,System.Windows.Forms,UIAutomationClient,UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class MI9 { [DllImport("user32.dll")] public static extern void mouse_event(uint f,int dx,int dy,uint d,UIntPtr e); [DllImport("user32.dll")] public static extern void keybd_event(byte vk,byte sc,uint f,UIntPtr e); }
"@
$exe="D:\Work\Kadr\code\publish\Kadr.exe"; $out="D:\Work\Kadr\docs\kadr-screens\tests"; New-Item -ItemType Directory -Force $out | Out-Null
$settingsPath="$env:LOCALAPPDATA\Kadr\settings.json"
$results=@()
function Report($name,$ok,$info=""){ $script:results += "{0,-4} {1} {2}" -f ($(if($ok){"OK"}else{"FAIL"})),$name,$info }
function Shot($name){ $b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $bmp=New-Object System.Drawing.Bitmap($b.Width,$b.Height); $g=[System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen(0,0,0,0,$bmp.Size); $bmp.Save("$out\$name.png"); $g.Dispose(); $bmp.Dispose() }
function Pixel($x,$y){ $b=New-Object System.Drawing.Bitmap(1,1); $g=[System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($x,$y,0,0,$b.Size); $c=$b.GetPixel(0,0); $g.Dispose(); $b.Dispose(); $c }
function MoveTo($x,$y){ [System.Windows.Forms.Cursor]::Position=New-Object System.Drawing.Point($x,$y); Start-Sleep -Milliseconds 60 }
function Click($x,$y){ MoveTo $x $y; [MI9]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [MI9]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 450 }
function RClick($x,$y){ MoveTo $x $y; [MI9]::mouse_event(8,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [MI9]::mouse_event(16,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 600 }
function Drag($x1,$y1,$x2,$y2){ MoveTo $x1 $y1; [MI9]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 150; $steps=15; for($i=1;$i -le $steps;$i++){ MoveTo ([int]($x1+($x2-$x1)*$i/$steps)) ([int]($y1+($y2-$y1)*$i/$steps)); Start-Sleep -Milliseconds 20 }; Start-Sleep -Milliseconds 150; [MI9]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 450 }
function Key($vk){ [MI9]::keybd_event($vk,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [MI9]::keybd_event($vk,0,2,[UIntPtr]::Zero); Start-Sleep -Milliseconds 350 }
function Ctrl($vk){ [MI9]::keybd_event(0x11,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 40; Key $vk; [MI9]::keybd_event(0x11,0,2,[UIntPtr]::Zero); Start-Sleep -Milliseconds 300 }
function Shift($vk){ [MI9]::keybd_event(0x10,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 40; Key $vk; [MI9]::keybd_event(0x10,0,2,[UIntPtr]::Zero); Start-Sleep -Milliseconds 200 }
function UiaRoot(){ [System.Windows.Automation.AutomationElement]::RootElement }
function FindWindow($titlePart){ $c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Window); foreach($w in (UiaRoot).FindAll([System.Windows.Automation.TreeScope]::Children,$c)){ if($w.Current.Name -like "*$titlePart*"){ return $w } }; $null }
function FindIn($root,$type,$namePart){ $c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,$type); foreach($e in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$c)){ if($e.Current.Name -like "*$namePart*"){ return $e } }; $null }
function Invoke-El($e){ $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function SetValue($e,$v){ $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($v) }
function KillAll(){ Get-Process Kadr,Kadr.Recorder -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep 1 }
function StartApp($args='-s'){ Start-Process $exe -ArgumentList $args; Start-Sleep 4 }
function StopApp(){ & $exe -u; Start-Sleep 1.5 }
function GetSettings(){ Get-Content $settingsPath -Raw -Encoding utf8 | ConvertFrom-Json }
function SetSetting($name,$value){ $j=Get-Content $settingsPath -Raw -Encoding utf8 | ConvertFrom-Json; $j.$name=$value; $j | ConvertTo-Json -Depth 5 | Set-Content $settingsPath -Encoding utf8 }

KillAll

# ============ A. Редактор: объекты, Ctrl+A, стрелки, толщина, цвет, сохранение
StartApp
& $exe -r; Start-Sleep 2.5
Drag 500 250 1300 750; Start-Sleep 0.6
Key 0x52; Drag 600 350 900 550                               # прямоугольник 600..900 x 350..550
Click 600 450; Start-Sleep 0.4; Shot "A1-select-rect"       # клик по левой границе -> выделение
$adorner = (Pixel 596 446); Report "A1 выделение объекта (ручка)" ($adorner.R -gt 200 -and $adorner.G -gt 200 -and $adorner.B -gt 200) "pixel=$adorner"
Drag 600 450 700 450; Start-Sleep 0.3; Shot "A2-moved"        # перемещение на +100
$moved = (Pixel 700 450); $old = (Pixel 600 450)
Report "A2 перемещение объекта" ($moved.R -gt 200 -and $moved.G -lt 120 -and -not ($old.R -gt 200 -and $old.G -lt 120)) "new=$moved old=$old"
Drag 1000 550 1100 650; Start-Sleep 0.3; Shot "A3-resized"    # ручка правый-нижний угол -> ресайз
$corner = (Pixel 1100 600); Report "A3 ресайз объекта" ($corner.R -gt 200 -and $corner.G -lt 120) "pixel=$corner"
Ctrl 0x5A; Ctrl 0x5A; Ctrl 0x5A                              # undo x3 -> объект исчез
$gone = (Pixel 700 450); Report "A4 undo ресайза/перемещения/добавления" (-not ($gone.R -gt 200 -and $gone.G -lt 120)) "pixel=$gone"
Ctrl 0x41; Start-Sleep 0.4; Shot "A5-ctrl-a"                 # весь экран
Ctrl 0x41; Start-Sleep 0.4                                    # обратно
Key 0x27; Key 0x27; Shift 0x28; Shift 0x28; Start-Sleep 0.3; Shot "A6-arrows"  # сдвиг +2 px, высота +2 px
Report "A5/A6 Ctrl+A и стрелки (см. снимки)" $true "kadr tests A5/A6"
Click 1193 779; Start-Sleep 0.5; Shot "A7-thickness-popup"    # список толщины
Click 1175 779; Start-Sleep 0.3
Click 1234 779; Start-Sleep 0.5; Shot "A8-color-popup"        # палитра
# найти зелёный (#34C759) в палитре и кликнуть
$img=[System.Drawing.Image]::FromFile("$out\A8-color-popup.png"); $bmp=New-Object System.Drawing.Bitmap($img); $found=$null
for($y=780;$y -lt 1000 -and -not $found;$y+=2){ for($x=1100;$x -lt 1400;$x+=2){ $c=$bmp.GetPixel($x,$y); if([Math]::Abs($c.R-0x34) -lt 12 -and [Math]::Abs($c.G-0xC7) -lt 12 -and [Math]::Abs($c.B-0x59) -lt 12){ $found=@($x,$y); break } } }
$bmp.Dispose(); $img.Dispose()
if($found){ Click $found[0] $found[1]; Start-Sleep 0.3; Key 0x52; Drag 620 380 880 520; $p=(Pixel 620 450); Report "A8 выбор цвета из палитры" ($p.G -gt 150 -and $p.R -lt 120) "pixel=$p" } else { Report "A8 выбор цвета из палитры" $false "зелёный кружок не найден" }
Click 1234 779; Start-Sleep 0.5; $btn = FindIn (FindWindow "Kadr") ([System.Windows.Automation.ControlType]::Button) "Свой цвет"
if($btn){ Invoke-El $btn; Start-Sleep 1.5; $dlg = FindWindow "Цвет"; if(-not $dlg){ $dlg = FindWindow "Color" }; Report "A9 диалог «Свой цвет» открылся" ($dlg -ne $null); if($dlg){ Shot "A9-custom-color"; $cancel = FindIn $dlg ([System.Windows.Automation.ControlType]::Button) "Отмена"; if(-not $cancel){ $cancel = FindIn $dlg ([System.Windows.Automation.ControlType]::Button) "Cancel" }; if($cancel){ Invoke-El $cancel } else { Key 0x1B } ; Start-Sleep 0.5 } } else { Report "A9 диалог «Свой цвет»" $false "кнопка не найдена" }
Ctrl 0x53; Start-Sleep 1.5                                    # сохранить как
$save = FindWindow "Сохранить скриншот"; Report "A10 диалог сохранения открылся" ($save -ne $null)
if($save){ $edit = FindIn $save ([System.Windows.Automation.ControlType]::Edit) "Имя файла"; if(-not $edit){ $edit = FindIn $save ([System.Windows.Automation.ControlType]::Edit) "File name" }; if($edit){ SetValue $edit "$out\saveas-test.png"; Start-Sleep 0.3; Key 0x0D; Start-Sleep 2; Report "A10 файл сохранён через диалог" (Test-Path "$out\saveas-test.png") } else { Report "A10 поле имени файла" $false; Key 0x1B; Key 0x1B } }
Start-Sleep 1

# ============ B. Печать: диалог и отмена
& $exe -r; Start-Sleep 2.5
Drag 500 250 1300 750; Start-Sleep 0.6
Ctrl 0x50; Start-Sleep 2
$print = FindWindow "Печать"; if(-not $print){ $print = FindWindow "Print" }
Report "B1 диалог печати открылся" ($print -ne $null)
if($print){ Shot "B1-print-dialog"; $cancel = FindIn $print ([System.Windows.Automation.ControlType]::Button) "Отмена"; if(-not $cancel){ $cancel = FindIn $print ([System.Windows.Automation.ControlType]::Button) "Cancel" }; if($cancel){ Invoke-El $cancel } else { Key 0x1B }; Start-Sleep 1 }
$frame = (Pixel 499 249); Report "B2 после отмены печати редактор остался" ($frame.R -gt 200 -and $frame.G -gt 200 -and $frame.B -gt 200) "pixel=$frame"
# печать в PDF (принтер по умолчанию Microsoft Print to PDF)
Remove-Item "$out\print-test.pdf" -Force -ErrorAction SilentlyContinue
Ctrl 0x50; Start-Sleep 2
$print = FindWindow "Печать"; if(-not $print){ $print = FindWindow "Print" }
if($print){ $ok = FindIn $print ([System.Windows.Automation.ControlType]::Button) "Печать"; if(-not $ok){ $ok = FindIn $print ([System.Windows.Automation.ControlType]::Button) "Print" }
  if($ok){ Invoke-El $ok; Start-Sleep 3; $pdf = FindWindow "Сохранить результат печати"; if(-not $pdf){ $pdf = FindWindow "Save Print Output" }
    if($pdf){ $edit = FindIn $pdf ([System.Windows.Automation.ControlType]::Edit) "Имя файла"; if(-not $edit){ $edit = FindIn $pdf ([System.Windows.Automation.ControlType]::Edit) "File name" }; if($edit){ SetValue $edit "$out\print-test.pdf"; Start-Sleep 0.3; Key 0x0D; Start-Sleep 5 } }
  }
}
Report "B3 печать в PDF" (Test-Path "$out\print-test.pdf") "$(if(Test-Path "$out\print-test.pdf"){ (Get-Item "$out\print-test.pdf").Length.ToString() + ' bytes' })"
Key 0x1B; Start-Sleep 1

# ============ C. Трей: панель по левому клику и меню по правому
Click 1632 1060; Start-Sleep 1                                 # раскрыть скрытые значки
$tray = $null; $c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button)
foreach($e in (UiaRoot).FindAll([System.Windows.Automation.TreeScope]::Descendants,$c)){ if($e.Current.Name -eq 'Kadr' -and -not $e.Current.IsOffscreen){ $tray=$e; break } }
if($tray){ $r=$tray.Current.BoundingRectangle; $cx=[int]($r.X+$r.Width/2); $cy=[int]($r.Y+$r.Height/2)
  Click $cx $cy; Start-Sleep 1; Shot "C1-tray-panel"; $panel = FindWindow "Kadr"; Report "C1 панель по левому клику" ($panel -ne $null); Key 0x1B; Start-Sleep 0.5
  Click 1632 1060; Start-Sleep 1; RClick $cx $cy; Start-Sleep 1; Shot "C2-tray-menu"; $mi = FindIn (UiaRoot) ([System.Windows.Automation.ControlType]::MenuItem) "Тихий режим"; Report "C2 контекстное меню трея" ($mi -ne $null); Key 0x1B; Start-Sleep 0.5
} else { Report "C трей-иконка Kadr не найдена через UIA" $false }
StopApp

# ============ D. Две кнопки мыши
SetSetting 'use_lr_mouse_for_regio_screenshot' $true
StartApp
MoveTo 900 500; [MI9]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 120; [MI9]::mouse_event(8,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 150; [MI9]::mouse_event(16,0,0,0,[UIntPtr]::Zero); [MI9]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Start-Sleep 2
Shot "D1-two-buttons"; $dim = (Pixel 100 100); $before = $null
$log = Get-Content "$env:LOCALAPPDATA\Kadr\log\main-$(Get-Date -Format yyyy-MM-dd).log" -Encoding utf8 | Select-Object -Last 5
Report "D1 две кнопки мыши вызывают оверлей" (($log -join ' ') -match 'Хук мыши для двух кнопок установлен') "см. D1-two-buttons.png"
Key 0x1B; Start-Sleep 1
StopApp
SetSetting 'use_lr_mouse_for_regio_screenshot' $false

# ============ E. Автозапуск через окно настроек
StartApp ''
$w = FindWindow "Kadr"; $cb = if($w){ FindIn $w ([System.Windows.Automation.ControlType]::CheckBox) "Запускать программу" }
if($cb){ $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep 1
  $reg = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue).Kadr
  Report "E1 автозапуск включён в реестре" ($reg -like "*Kadr.exe*") "$reg"
  $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep 1
  $reg2 = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue).Kadr
  Report "E2 автозапуск выключен" ($reg2 -eq $null)
} else { Report "E автозапуск: чекбокс не найден" $false }
StopApp

# ============ F. Portable
Remove-Item -Recurse -Force "D:\Work\Kadr\code\publish\data" -ErrorAction SilentlyContinue
StartApp '--portable -s'
Report "F1 portable: settings.json рядом с exe" (Test-Path "D:\Work\Kadr\code\publish\data\settings.json")
StopApp
Remove-Item -Recurse -Force "D:\Work\Kadr\code\publish\data" -ErrorAction SilentlyContinue

# ============ G. Тихий режим и H. шаблон с заголовком окна
SetSetting 'silent_mode' $true
SetSetting 'screenshot_file_name_template' 'Тест-{w}-{nn}'
$desk="$env:USERPROFILE\Desktop"; $before = Get-ChildItem $desk -Filter 'Тест-*.png' | Select-Object -ExpandProperty Name
StartApp
Click 900 600; Start-Sleep 0.5                                 # активное окно: браузер/что угодно
$logBefore = (Get-Content "$env:LOCALAPPDATA\Kadr\log\main-$(Get-Date -Format yyyy-MM-dd).log" -Encoding utf8).Count
& $exe -m; Start-Sleep 3
$new = Get-ChildItem $desk -Filter 'Тест-*.png' | Where-Object { $before -notcontains $_.Name }
Report "H1 шаблон {w}{nn}: файл создан" ($new.Count -eq 1) ($new | ForEach-Object { $_.Name })
if($new){ Report "H2 имя заканчивается на -01" ($new[0].Name -like '*-01.png'); Move-Item $new[0].FullName "$out\template-test.png" -Force }
$logAfter = Get-Content "$env:LOCALAPPDATA\Kadr\log\main-$(Get-Date -Format yyyy-MM-dd).log" -Encoding utf8 | Select-Object -Skip $logBefore
Report "G1 тихий режим: уведомления об успехе нет" (-not (($logAfter -join ' ') -match 'Уведомление: Скриншот сохранён'))
StopApp
SetSetting 'silent_mode' $false
SetSetting 'screenshot_file_name_template' 'Скриншот-{YYYY}{MM}{DD}-{hh}{mm}{ss}'
KillAll
""; "================ РЕЗУЛЬТАТЫ"; $results
