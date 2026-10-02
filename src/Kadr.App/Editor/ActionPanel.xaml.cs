using System.Windows;
using System.Windows.Controls;
using Kadr.Common.Settings;

namespace Kadr.App.Editor;

public partial class ActionPanel : UserControl
{
    public event Action<EditorActionKind>? ActionRequested;

    public ActionPanel() => InitializeComponent();

    /// <summary>Скрыть кнопки, отключённые в настройках. «Готово» и «Закрыть» остаются всегда.</summary>
    public void ApplyHidden(Func<EditorPanelItem, bool> isHidden)
    {
        BtnOcr.Visibility = isHidden(EditorPanelItem.Ocr) ? Visibility.Collapsed : Visibility.Visible;
        OcrSeparator.Visibility = BtnOcr.Visibility;
        BtnCopy.Visibility = isHidden(EditorPanelItem.Copy) ? Visibility.Collapsed : Visibility.Visible;
        BtnPrint.Visibility = isHidden(EditorPanelItem.Print) ? Visibility.Collapsed : Visibility.Visible;
        BtnSave.Visibility = isHidden(EditorPanelItem.Save) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Ocr_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.DoOcr);
    private void Copy_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.CopyToClipboard);
    private void Print_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.Print);
    private void Save_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.SaveToFile);
    private void Default_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.Default);
    private void Close_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.Close);
}
