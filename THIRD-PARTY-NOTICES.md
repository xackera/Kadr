# Сторонние компоненты Kadr

Kadr (автор Mike Alimov) распространяется по лицензии PolyForm Noncommercial 1.0.0 (см. `LICENSE`).
Ниже перечислены библиотеки, которые подключаются через NuGet и попадают в сборку, с их лицензиями.
Все они разрешительные и допускают использование в программе с некоммерческой лицензией при сохранении
их уведомлений об авторских правах.

| Компонент | Назначение | Лицензия |
|---|---|---|
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MVVM, команды | MIT |
| [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon) | иконка в трее, уведомления | MIT |
| [Microsoft.Extensions.*](https://github.com/dotnet/runtime) (Hosting, DependencyInjection, Logging, Configuration) | хост приложения, DI, логирование | MIT |
| [NLog](https://github.com/NLog/NLog), [NLog.Extensions.Logging](https://github.com/NLog/NLog.Extensions.Logging) | запись логов в файлы | BSD-3-Clause |
| [NAudio](https://github.com/naudio/NAudio) (NAudio.Core, NAudio.Wasapi) | захват микрофона и системного звука | MIT |
| [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (Direct3D11, DXGI, Direct2D1, Mathematics), SharpGen.Runtime | Direct3D/Direct2D для захвата и подсветки курсора | MIT |
| [CsWinRT](https://github.com/microsoft/CsWinRT), Microsoft.Windows.SDK.NET | проекции WinRT: Windows.Graphics.Capture, Media Foundation, OCR, MediaCapture | MIT (CsWinRT), условия Windows SDK (Microsoft.Windows.SDK.NET) |
| .NET 8 Runtime, WPF, Windows Forms | платформа | MIT |

Кодеки H.264 и AAC, распознавание текста, захват экрана и веб-камеры предоставляются самой Windows 10/11
(Media Foundation, Windows.Media.Ocr, Windows.Graphics.Capture); в дистрибутив они не входят.

Шрифт Segoe MDL2 Assets используется для иконок панелей только как системный шрифт Windows и не распространяется
вместе с программой. Иконки приложения (`assets/icons`) нарисованы для Kadr и распространяются на тех же условиях,
что и программа (PolyForm Noncommercial 1.0.0).

## Названия

Kadr — самостоятельная программа. Названия Windows и других продуктов принадлежат их правообладателям
и упоминаются только для описания совместимости.
