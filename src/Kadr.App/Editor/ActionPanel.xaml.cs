using System.Windows;
using System.Windows.Controls;

namespace Kadr.App.Editor;

public partial class ActionPanel : UserControl
{
    public event Action<EditorActionKind>? ActionRequested;

    public ActionPanel() => InitializeComponent();

    private void Ocr_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.DoOcr);
    private void Copy_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.CopyToClipboard);
    private void Print_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.Print);
    private void Save_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.SaveToFile);
    private void Default_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.Default);
    private void Close_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.Close);
}
