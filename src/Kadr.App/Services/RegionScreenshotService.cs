using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Kadr.App.Editor;
using Kadr.Capture;
using Kadr.Common.Settings;
using Microsoft.Extensions.Logging;

namespace Kadr.App.Services;

/// <summary>Скриншот области: оверлей на всех мониторах, редактор, затем действие пользователя.</summary>
public sealed class RegionScreenshotService
{
    private readonly SettingsStore _store;
    private readonly OperationState _state;
    private readonly ScreenshotService _screenshots;
    private readonly NotificationService _notify;
    private readonly ILogger<RegionScreenshotService> _logger;

    public RegionScreenshotService(SettingsStore store, OperationState state, ScreenshotService screenshots,
        NotificationService notify, ILogger<RegionScreenshotService> logger)
    {
        _store = store;
        _state = state;
        _screenshots = screenshots;
        _notify = notify;
        _logger = logger;
    }

    public async Task RunAsync(bool invertEditor)
    {
        if (!_state.TryStart(OperationType.Screenshot))
        {
            _logger.LogInformation("Скриншот области пропущен: идёт другая операция");
            return;
        }
        try
        {
            var settings = _store.Current;
            var title = ForegroundWindow.Title(ForegroundWindow.Handle);
            bool editor = settings.ShowEditor ^ invertEditor;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await OverlaySession.RunAsync(OverlayMode.Screenshot, settings, editor, _logger);
            _logger.LogInformation("Сессия выделения завершена: {Action} за {Ms} мс", result?.Action, sw.ElapsedMilliseconds);
            if (result is null || result.Action == EditorActionKind.Close || result.Image is null) return;

            using var image = result.Image;
            var rect = result.ScreenRect;
            _store.Update(s =>
            {
                s.PreviouslySelectedRegion = $"{rect.X}, {rect.Y}, {rect.Width}, {rect.Height}";
                s.EditorSelectedTool = result.Tool;
                s.EditorLineThickness = (int)result.Thickness;
                s.EditorColor = result.Color;
                s.EditorAvailableColors = result.Palette;
            });

            if (settings.PlaySound && !settings.SilentMode) SoundService.PlayShutter(settings.SoundShutterPath);

            switch (result.Action)
            {
                case EditorActionKind.Default:
                    _screenshots.Publish(image, title);
                    break;

                case EditorActionKind.CopyToClipboard:
                    try
                    {
                        ClipboardService.SetImage(image, null);
                        _notify.Info("Скриншот скопирован в буфер обмена.", null, respectSilentMode: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Буфер обмена");
                        _notify.Error("Не удалось скопировать скриншот в буфер обмена. Проверьте, не блокирует ли антивирус.");
                    }
                    break;

                case EditorActionKind.SaveToFile when result.SaveFilePath is { } path:
                    SaveAs(image, path, settings.JpegQuality);
                    var p = path;
                    _notify.Info("Скриншот сохранён. Нажмите, чтобы открыть папку.", () => ScreenshotService.OpenFolderWithFile(p), respectSilentMode: true);
                    break;

                case EditorActionKind.Print:
                    var dialog = OverlaySessionPrintDialog(result);
                    if (dialog is not null) PrintService.Print(dialog, image, "Скриншот экрана");
                    break;

                case EditorActionKind.DoOcr:
                    await RecognizeAsync(image);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка скриншота области");
            _notify.Error("Не удалось сделать скриншот: " + ex.Message);
        }
        finally
        {
            _state.End();
        }
    }

    // Диалог печати создаётся внутри сессии (поверх оверлея); сюда он передаётся через статическое поле сессии.
    private static System.Windows.Controls.PrintDialog? OverlaySessionPrintDialog(OverlayResult result) => result.PrintDialog;

    private async Task RecognizeAsync(Bitmap image)
    {
        if (!OcrService.IsAvailable)
        {
            _notify.Error("В Windows не установлены языки распознавания текста. Добавьте язык в Параметрах Windows.");
            return;
        }
        try
        {
            var text = await OcrService.RecognizeAsync(image);
            if (string.IsNullOrWhiteSpace(text))
            {
                _notify.Info("Текст на изображении не найден.");
                return;
            }
            ClipboardService.SetText(text);
            _notify.Info("Распознанный текст помещён в буфер обмена.", null, respectSilentMode: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR");
            _notify.Error("Не удалось распознать текст: " + ex.Message);
        }
    }

    private static void SaveAs(Bitmap image, string path, int jpegQuality)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg")
        {
            using var rgb = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(rgb)) g.DrawImageUnscaled(image, 0, 0);
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)jpegQuality);
            rgb.Save(path, codec, parameters);
        }
        else
        {
            image.Save(path, ImageFormat.Png);
        }
    }
}
