# Тестовая запись через Kadr.Recorder --test (без главного приложения)
param([int]$Seconds = 5, [string]$Mic = "NoSound", [string]$Sys = "DefaultConsole", [switch]$Highlight)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Mon { [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags); [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; } }
"@
Add-Type -AssemblyName System.Windows.Forms
$h = [Mon]::MonitorFromPoint((New-Object Mon+POINT), 1)
$out = "$env:TEMP\kadr-test.mp4"
if (Test-Path $out) { Remove-Item $out -Force }
$opts = @{ monitor = [int64]$h; monitorName = "\\.\DISPLAY1"; x = 200; y = 100; width = 1280; height = 720; maxHeight = 720; fps = 25; output = $out; mic = $Mic; sys = $Sys; maxMinutes = 5; cursor = $true; monitorLeft = 0; monitorTop = 0; scale = 1.0 }
if ($Highlight) { $opts.highlightPointer = $true; $opts.highlightClicks = $true; [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(700, 400) }
$json = $opts | ConvertTo-Json -Compress
$jsonFile = "$env:TEMP\kadr-test.json"
[IO.File]::WriteAllText($jsonFile, $json, [Text.Encoding]::UTF8)
$exe = "D:\Work\Kadr\code\src\Kadr.Recorder\bin\Debug\net8.0-windows10.0.22621.0\Kadr.Recorder.exe"
& $exe --test $jsonFile $Seconds
if (Test-Path $out) { "FILE: $((Get-Item $out).Length) bytes" } else { "NO FILE" }
