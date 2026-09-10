using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Kadr.App.Editor.Objects;
using Kadr.Capture;
using Kadr.Common.Settings;
using Microsoft.Win32;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Kadr.App.Editor;

/// <summary>Оверлей одного монитора: фон-снимок, затемнение, рамка с ручками, объекты редактора, панели.</summary>
public partial class OverlayWindow : Window
{
    private const double MinSelection = 11;
    private const double HandleSize = 7;

    private static readonly HandleKind[] FrameHandleKinds =
    {
        HandleKind.TopLeft, HandleKind.Top, HandleKind.TopRight, HandleKind.Right,
        HandleKind.BottomRight, HandleKind.Bottom, HandleKind.BottomLeft, HandleKind.Left,
    };

    private readonly OverlaySession _session;
    private readonly List<VisualObject> _objects = new();
    private readonly Dictionary<HandleKind, Rectangle> _frameHandles = new();
    private readonly BitmapSource? _background;
    private BitmapSource? _blurred;
    private EditorToolbar? _toolbar;
    private ActionPanel? _actions;

    // Состояние перетаскивания
    private Point _dragStart;
    private Point _dragLast;
    private Rect _dragStartRect;
    private HandleKind _activeHandle;
    private VisualObject? _drawing;
    private VisualObject? _dragObject;
    private object? _objectStartState;
    private bool _pendingObjectMove;
    private bool _moved;

    public MonitorInfo Monitor { get; }
    public double Scale { get; private set; }
    public IReadOnlyList<VisualObject> Objects => _objects;

    public OverlayWindow(OverlaySession session, MonitorInfo monitor, System.Drawing.Bitmap? background)
    {
        _session = session;
        Monitor = monitor;
        Scale = monitor.Scale;
        InitializeComponent();

        if (background is not null)
        {
            _background = Exporter.ToBitmapSource(background);
            BackgroundImage.Source = _background;
        }
        else
        {
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            BackgroundImage.Visibility = Visibility.Collapsed;
        }

        foreach (var kind in FrameHandleKinds)
        {
            var h = new Rectangle
            {
                Width = HandleSize, Height = HandleSize, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 0.5,
                Cursor = HandleCursor(kind), Tag = kind,
            };
            _frameHandles[kind] = h;
            FrameLayer.Children.Add(h);
        }

        if (session.Mode == OverlayMode.Screenshot)
        {
            _toolbar = new EditorToolbar();
            _toolbar.ToolClicked += t => _session.SelectTool(t, fromClick: true);
            _toolbar.ThicknessChanged += t => _session.SetThickness(t);
            _toolbar.ColorChanged += c => _session.SetColor(c);
            _toolbar.CustomColorRequested += PickCustomColor;
            _toolbar.UndoRequested += () => _session.UndoLast();
            _actions = new ActionPanel();
            _actions.ActionRequested += a => _session.Complete(a);
            PanelLayer.Children.Add(_toolbar);
            PanelLayer.Children.Add(_actions);
        }

        HintIcon.Text = session.Mode switch { OverlayMode.Video => "", OverlayMode.Scrolling => "", _ => "" };
        HintText.Text = session.Mode switch { OverlayMode.Video => "Выделите область для записи", OverlayMode.Scrolling => "Выделите область для прокрутки", _ => "Выделите область" };

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => { UpdateScale(); Refresh(); };
        Closed += (_, _) => _session.Changed -= Refresh;
        _session.Changed += Refresh;
        _session.Undo.Changed += () => _toolbar?.SetCanUndo(_session.Undo.CanUndo);

        Root.PreviewMouseLeftButtonDown += OnMouseDown;
        Root.MouseMove += OnMouseMove;
        Root.PreviewMouseLeftButtonUp += OnMouseUp;
        Root.MouseRightButtonUp += OnRightClick;
        Root.MouseWheel += OnWheel;
        PreviewKeyDown += OnKeyDown;
        MouseLeave += (_, _) => Hint.Visibility = Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ окно

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.AddExStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW);
        var b = Monitor.Bounds;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        if (_session.Mode != OverlayMode.Screenshot) NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
    }

    private void UpdateScale()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is { } ct) Scale = ct.TransformToDevice.M11;
    }

    public void ActivateAndFocus()
    {
        Activate();
        Root.Focus();
        Keyboard.Focus(Root);
    }

    /// <summary>Область в физических пикселях экрана.</summary>
    public System.Drawing.Rectangle ToScreenRect(Rect dip) => new(
        Monitor.Bounds.X + (int)Math.Round(dip.X * Scale), Monitor.Bounds.Y + (int)Math.Round(dip.Y * Scale),
        (int)Math.Round(dip.Width * Scale), (int)Math.Round(dip.Height * Scale));

    public Rect FromScreenRect(System.Drawing.Rectangle px) => new(
        (px.X - Monitor.Bounds.X) / Scale, (px.Y - Monitor.Bounds.Y) / Scale, px.Width / Scale, px.Height / Scale);

    public System.Drawing.Bitmap Export(Rect selection) => Exporter.Render(ContentLayer, selection, Scale);

    public string? ShowSaveDialog()
    {
        var s = _session.Settings;
        var jpeg = s.ScreenshotFileType == ScreenshotFileType.Jpeg;
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить скриншот",
            Filter = "PNG|*.png|JPEG|*.jpg",
            FilterIndex = jpeg ? 2 : 1,
            InitialDirectory = s.ScreenshotsPath,
            FileName = Common.Files.FileNameTemplate.Resolve(s.ScreenshotFileNameTemplate, DateTime.Now, "", 1),
            AddExtension = true,
            DefaultExt = jpeg ? ".jpg" : ".png",
        };
        if (dialog.ShowDialog(this) != true) return null;
        var path = dialog.FileName;
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var wanted = dialog.FilterIndex == 2 ? ".jpg" : ".png";
        if (ext != wanted && ext != (wanted == ".jpg" ? ".jpeg" : ".png")) path = System.IO.Path.ChangeExtension(path, wanted);
        return path;
    }

    private void PickCustomColor()
    {
        using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true, Color = ToDrawing(_session.Color) };
        var hwnd = new WindowInteropHelper(this).Handle;
        if (dlg.ShowDialog(new WindowWrapper(hwnd)) == System.Windows.Forms.DialogResult.OK)
            _session.SetColor(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
    }

    private static System.Drawing.Color ToDrawing(Color c) => System.Drawing.Color.FromArgb(c.R, c.G, c.B);

    private sealed class WindowWrapper : System.Windows.Forms.IWin32Window
    {
        public WindowWrapper(nint handle) => Handle = handle;
        public nint Handle { get; }
    }

    // ------------------------------------------------------------------ объекты

    public void AddObject(VisualObject obj, int? index = null)
    {
        int idx = index ?? (obj is BlurObject ? _objects.Count(o => o is BlurObject) : _objects.Count);
        idx = Math.Clamp(idx, 0, _objects.Count);
        _objects.Insert(idx, obj);
        ObjectsCanvas.Children.Insert(idx, obj.View);
        obj.Render();
    }

    public void RemoveObject(VisualObject obj)
    {
        var idx = _objects.IndexOf(obj);
        if (idx < 0) return;
        _objects.RemoveAt(idx);
        ObjectsCanvas.Children.Remove(obj.View);
        if (_session.LastObject == obj) _session.LastObject = null;
    }

    public void DeleteObject(VisualObject obj, bool withUndo)
    {
        var idx = _objects.IndexOf(obj);
        if (idx < 0) return;
        RemoveObject(obj);
        if (withUndo) _session.Undo.Push(new DelegateAction("delete", () => AddObject(obj, idx), () => RemoveObject(obj)));
    }

    private void PushAdd(VisualObject obj)
    {
        var idx = _objects.IndexOf(obj);
        _session.Undo.Push(new DelegateAction("add", () => RemoveObject(obj), () => AddObject(obj, idx)));
    }

    private VisualObject? HitObject(Point p)
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i].HitTest(p)) return _objects[i];
        return null;
    }

    private HandleKind HitObjectHandle(VisualObject obj, Point p)
    {
        foreach (var h in obj.Handles)
        {
            var c = obj.HandlePosition(h);
            if (Math.Abs(p.X - c.X) <= HandleSize && Math.Abs(p.Y - c.Y) <= HandleSize) return h;
        }
        return HandleKind.None;
    }

    private HandleKind HitFrameHandle(Point p)
    {
        if (!IsOwner || !_session.HasSelection) return HandleKind.None;
        foreach (var (kind, rect) in _frameHandles)
        {
            if (rect.Visibility != Visibility.Visible) continue;
            var x = Canvas.GetLeft(rect); var y = Canvas.GetTop(rect);
            if (p.X >= x - 3 && p.X <= x + HandleSize + 3 && p.Y >= y - 3 && p.Y <= y + HandleSize + 3) return kind;
        }
        return HandleKind.None;
    }

    private VisualObject CreateObject(DrawingTool tool)
    {
        VisualObject obj = tool switch
        {
            DrawingTool.Arrow => new ArrowObject(),
            DrawingTool.Line => new LineObject(),
            DrawingTool.Pensil => new PencilObject(),
            DrawingTool.Marker => new MarkerObject(),
            DrawingTool.Rectangle => new RectangleObject(),
            DrawingTool.Oval => new OvalObject(),
            DrawingTool.Text => new TextObject(),
            DrawingTool.Number => new NumberObject { Number = _session.NextNumber() },
            DrawingTool.Blur => new BlurObject(GetBlurred(), new Size(ActualWidth, ActualHeight)),
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };
        obj.Thickness = _session.Thickness;
        obj.Color = _session.Color;
        obj.Shadow = _session.Shadows;
        return obj;
    }

    private BitmapSource GetBlurred()
    {
        if (_blurred is null)
        {
            var src = _background ?? new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32);
            _blurred = Exporter.MakeBlurred(src);
        }
        return _blurred;
    }

    public void EndTextEditing()
    {
        foreach (var t in _objects.OfType<TextObject>().Where(t => t.IsEditing).ToList()) t.EndEdit();
    }

    private void OnTextEditEnded(TextObject text, bool isNew, object? beforeState)
    {
        if (text.IsEmpty)
        {
            RemoveObject(text);
            if (!isNew && beforeState is not null)
            {
                // очистка существующего текста = удаление с возможностью отмены
                var idx = _objects.Count;
                _session.Undo.Push(new DelegateAction("delete", () => { AddObject(text, idx); text.RestoreState(beforeState); }, () => RemoveObject(text)));
            }
        }
        else if (isNew)
        {
            PushAdd(text);
            _session.LastObject = text;
        }
        else if (beforeState is not null)
        {
            _session.Undo.Push(new StateAction("text", text, beforeState, text.CaptureState()));
        }
        if (_session.State == OverlayState.EditingText) _session.SetState(OverlayState.ToolSelected);
        else _session.NotifyChanged();
    }

    // ------------------------------------------------------------------ мышь

    private bool IsOwner => _session.Owner == this;
    private static bool ShiftDown => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

    private bool IsInsidePanels(object? source) => source is DependencyObject d && IsDescendantOf(d, PanelLayer);
    private bool IsInsideEditingText(object? source) => source is DependencyObject d && _objects.OfType<TextObject>().Any(t => t.IsEditing && IsDescendantOf(d, t.View));

    private static bool IsDescendantOf(DependencyObject? d, DependencyObject ancestor)
    {
        while (d is not null)
        {
            if (d == ancestor) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsidePanels(e.OriginalSource) || IsInsideEditingText(e.OriginalSource)) return;
        var p = e.GetPosition(Root);
        Root.Focus();
        e.Handled = true;
        _moved = false;
        _dragStart = _dragLast = p;

        // Клик мимо редактируемого текста только завершает ввод.
        if (_session.State == OverlayState.EditingText) { _session.EndTextEditing(); return; }

        var state = _session.State;
        if (state is OverlayState.Recording or OverlayState.Scrolling) return;

        // Ручки рамки
        var frameHandle = HitFrameHandle(p);
        if (frameHandle != HandleKind.None && state is OverlayState.Selected or OverlayState.ToolSelected)
        {
            _activeHandle = frameHandle;
            _dragStartRect = _session.Selection;
            _session.SetState(OverlayState.ResizingFrame);
            Root.CaptureMouse();
            return;
        }

        bool editing = _session.EditorEnabled && _session.Mode == OverlayMode.Screenshot && IsOwner && _session.HasSelection;

        // Ручки выбранного объекта
        if (editing && _session.SelectedObject is { } sel && sel.CanResize && state is OverlayState.Selected or OverlayState.ToolSelected)
        {
            var h = HitObjectHandle(sel, p);
            if (h != HandleKind.None)
            {
                _activeHandle = h;
                _dragObject = sel;
                _objectStartState = sel.CaptureState();
                _session.SetState(OverlayState.ResizingObject);
                Root.CaptureMouse();
                return;
            }
        }

        // Клик по объекту: выделить, приготовиться к перемещению
        if (editing && state is OverlayState.Selected or OverlayState.ToolSelected && HitObject(p) is { } obj)
        {
            _session.SelectObject(obj);
            _dragObject = obj;
            _objectStartState = obj.CaptureState();
            _pendingObjectMove = true;
            Root.CaptureMouse();
            return;
        }

        // Рисование нового объекта
        if (editing && state == OverlayState.ToolSelected && _session.Tool != DrawingTool.None)
        {
            _session.SelectObject(null);
            var created = CreateObject(_session.Tool);
            AddObject(created);
            created.BeginDraw(p);
            if (created is TextObject text)
            {
                var isNew = true;
                text.EditEnded += t => OnTextEditEnded(t, isNew, null);
                _session.SelectObject(text);
                _session.SetState(OverlayState.EditingText);
                text.BeginEdit();
                return;
            }
            _drawing = created;
            _session.SetState(OverlayState.DrawingObject);
            Root.CaptureMouse();
            return;
        }

        // Перемещение рамки
        if (IsOwner && _session.HasSelection && state is OverlayState.Selected or OverlayState.ToolSelected && _session.Selection.Contains(p))
        {
            _dragStartRect = _session.Selection;
            _session.SetState(OverlayState.MovingFrame);
            Root.CaptureMouse();
            return;
        }

        // Новое выделение
        _session.SelectObject(null);
        _session.SetSelection(this, new Rect(p, new Size(0, 0)));
        _session.SetState(OverlayState.Selecting);
        Root.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Root);
        var delta = p - _dragLast;
        if ((p - _dragStart).Length > 2) _moved = true;

        switch (_session.State)
        {
            case OverlayState.Selecting:
                _session.SetSelection(this, ClampRect(RectFromPoints(_dragStart, p, ShiftDown)));
                break;
            case OverlayState.MovingFrame:
            {
                var r = _dragStartRect;
                r.Offset(p - _dragStart);
                _session.SetSelection(this, ClampInside(r));
                break;
            }
            case OverlayState.ResizingFrame:
            {
                var r = ResizeFrame(_dragStartRect, _activeHandle, ClampPoint(p), ShiftDown);
                _session.SetSelection(this, r);
                break;
            }
            case OverlayState.DrawingObject:
                _drawing?.ContinueDraw(p, ShiftDown);
                break;
            case OverlayState.ResizingObject:
                if (_dragObject is not null && _objectStartState is not null) _dragObject.Resize(_activeHandle, p, ShiftDown, _objectStartState);
                RefreshObjectSelection();
                break;
            case OverlayState.MovingObject:
                _dragObject?.Offset(delta);
                RefreshObjectSelection();
                break;
            default:
                if (_pendingObjectMove && _dragObject is not null && _moved)
                {
                    _session.SetState(OverlayState.MovingObject);
                    _dragObject.Offset(p - _dragStart);
                    RefreshObjectSelection();
                }
                else
                {
                    UpdateHoverCursor(p);
                    UpdateHint(p);
                }
                break;
        }
        _dragLast = p;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!Root.IsMouseCaptured && !_pendingObjectMove) return;
        var p = e.GetPosition(Root);
        Root.ReleaseMouseCapture();
        e.Handled = true;
        var toolState = _session.Tool == DrawingTool.None ? OverlayState.Selected : OverlayState.ToolSelected;

        switch (_session.State)
        {
            case OverlayState.Selecting:
            {
                var r = _session.Selection;
                if (!_moved || r.Width < 1 || r.Height < 1)
                {
                    _session.ClearSelection();
                    break;
                }
                r = EnsureMinSize(r, _dragStart, p);
                _session.SetSelection(this, r);
                _session.SetState(toolState);
                if (_session.Mode != OverlayMode.Screenshot) _session.Complete(EditorActionKind.Start);
                else if (!_session.EditorEnabled) _session.Complete(EditorActionKind.Default);
                break;
            }
            case OverlayState.MovingFrame:
            case OverlayState.ResizingFrame:
                _session.SetState(toolState);
                break;
            case OverlayState.DrawingObject:
                if (_drawing is not null)
                {
                    if (!_drawing.EndDraw()) RemoveObject(_drawing);
                    else { PushAdd(_drawing); _session.LastObject = _drawing; }
                    _drawing = null;
                }
                _session.SetState(OverlayState.ToolSelected);
                break;
            case OverlayState.MovingObject:
                if (_dragObject is not null && _objectStartState is not null)
                    _session.Undo.Push(new StateAction("move", _dragObject, _objectStartState, _dragObject.CaptureState()));
                _session.SetState(toolState);
                break;
            case OverlayState.ResizingObject:
                if (_dragObject is not null && _objectStartState is not null)
                    _session.Undo.Push(new StateAction("resize", _dragObject, _objectStartState, _dragObject.CaptureState()));
                _session.SetState(toolState);
                break;
            default:
                _session.NotifyChanged();
                break;
        }
        _pendingObjectMove = false;
        _dragObject = null;
        _objectStartState = null;
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (IsInsidePanels(e.OriginalSource)) return;
        e.Handled = true;
        if (_session.State is OverlayState.Recording or OverlayState.Scrolling) return;
        if (_session.State == OverlayState.EditingText) { _session.EndTextEditing(); return; }
        if (_session.SelectedObject is not null) { _session.SelectObject(null); return; }
        if (_session.HasSelection) { _session.ClearSelection(); return; }
        _session.Cancel();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsInsidePanels(e.OriginalSource)) return;
        if (_session.Mode != OverlayMode.Screenshot) return;
        e.Handled = true;
        _session.SetThickness(_session.Thickness + (e.Delta > 0 ? 2 : -2));
    }

    // ------------------------------------------------------------------ клавиатура

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool handled = true;

        switch (key)
        {
            case Key.Escape: _session.Complete(EditorActionKind.Close); break;
            case Key.Enter: _session.Complete(_session.Mode == OverlayMode.Screenshot ? EditorActionKind.Default : EditorActionKind.Start); break;
            case Key.C when ctrl: _session.Complete(EditorActionKind.CopyToClipboard); break;
            case Key.Insert when ctrl: _session.Complete(EditorActionKind.CopyToClipboard); break;
            case Key.S when ctrl: _session.Complete(EditorActionKind.SaveToFile); break;
            case Key.P when ctrl: _session.Complete(EditorActionKind.Print); break;
            case Key.A when ctrl: _session.SelectAll(this); break;
            case Key.Z when ctrl: _session.UndoLast(); break;
            case Key.Y when ctrl: _session.RedoLast(); break;
            case Key.Delete or Key.Back: _session.DeleteSelected(); break;
            case Key.Left or Key.Right or Key.Up or Key.Down: NudgeSelection(key, shift); break;
            case Key.A when !ctrl: _session.SelectTool(DrawingTool.Arrow, false); break;
            case Key.L: _session.SelectTool(DrawingTool.Line, false); break;
            case Key.P when !ctrl: _session.SelectTool(DrawingTool.Pensil, false); break;
            case Key.M: _session.SelectTool(DrawingTool.Marker, false); break;
            case Key.R: _session.SelectTool(DrawingTool.Rectangle, false); break;
            case Key.O: _session.SelectTool(DrawingTool.Oval, false); break;
            case Key.T: _session.SelectTool(DrawingTool.Text, false); break;
            case Key.N: _session.SelectTool(DrawingTool.Number, false); break;
            case Key.B: _session.SelectTool(DrawingTool.Blur, false); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    private void NudgeSelection(Key key, bool resize)
    {
        if (!IsOwner || !_session.HasSelection) return;
        double step = 1 / Scale;
        var r = _session.Selection;
        double dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
        double dy = key == Key.Up ? -step : key == Key.Down ? step : 0;
        if (resize) r = new Rect(r.X, r.Y, Math.Max(MinSelection, r.Width + dx), Math.Max(MinSelection, r.Height + dy));
        else r.Offset(dx, dy);
        _session.SetSelection(this, ClampInside(r));
    }

    // ------------------------------------------------------------------ геометрия

    private Rect WindowRect => new(0, 0, ActualWidth, ActualHeight);

    private Point ClampPoint(Point p) => new(Math.Clamp(p.X, 0, ActualWidth), Math.Clamp(p.Y, 0, ActualHeight));

    private Rect ClampRect(Rect r) => Rect.Intersect(r, WindowRect) is { IsEmpty: false } i ? i : new Rect(ClampPoint(r.TopLeft), new Size(0, 0));

    private Rect ClampInside(Rect r)
    {
        var x = Math.Clamp(r.X, 0, Math.Max(0, ActualWidth - r.Width));
        var y = Math.Clamp(r.Y, 0, Math.Max(0, ActualHeight - r.Height));
        return new Rect(x, y, Math.Min(r.Width, ActualWidth), Math.Min(r.Height, ActualHeight));
    }

    private static Rect RectFromPoints(Point a, Point b, bool square)
    {
        var w = b.X - a.X; var h = b.Y - a.Y;
        if (square)
        {
            var s = Math.Min(Math.Abs(w), Math.Abs(h));
            w = Math.Sign(w) * s; h = Math.Sign(h) * s;
        }
        return new Rect(new Point(Math.Min(a.X, a.X + w), Math.Min(a.Y, a.Y + h)), new Size(Math.Abs(w), Math.Abs(h)));
    }

    private Rect EnsureMinSize(Rect r, Point start, Point end)
    {
        double w = Math.Max(r.Width, MinSelection), h = Math.Max(r.Height, MinSelection);
        double x = end.X < start.X ? r.Right - w : r.X;
        double y = end.Y < start.Y ? r.Bottom - h : r.Y;
        return ClampInside(new Rect(x, y, w, h));
    }

    private static Rect ResizeFrame(Rect start, HandleKind handle, Point p, bool square)
    {
        double l = start.Left, t = start.Top, r = start.Right, b = start.Bottom;
        switch (handle)
        {
            case HandleKind.TopLeft: l = Math.Min(p.X, r - MinSelection); t = Math.Min(p.Y, b - MinSelection); break;
            case HandleKind.Top: t = Math.Min(p.Y, b - MinSelection); break;
            case HandleKind.TopRight: r = Math.Max(p.X, l + MinSelection); t = Math.Min(p.Y, b - MinSelection); break;
            case HandleKind.Right: r = Math.Max(p.X, l + MinSelection); break;
            case HandleKind.BottomRight: r = Math.Max(p.X, l + MinSelection); b = Math.Max(p.Y, t + MinSelection); break;
            case HandleKind.Bottom: b = Math.Max(p.Y, t + MinSelection); break;
            case HandleKind.BottomLeft: l = Math.Min(p.X, r - MinSelection); b = Math.Max(p.Y, t + MinSelection); break;
            case HandleKind.Left: l = Math.Min(p.X, r - MinSelection); break;
        }
        if (square && handle is HandleKind.TopLeft or HandleKind.TopRight or HandleKind.BottomLeft or HandleKind.BottomRight)
        {
            var s = Math.Min(r - l, b - t);
            if (handle is HandleKind.TopLeft or HandleKind.BottomLeft) l = r - s; else r = l + s;
            if (handle is HandleKind.TopLeft or HandleKind.TopRight) t = b - s; else b = t + s;
        }
        return new Rect(new Point(l, t), new Point(r, b));
    }

    private static Cursor HandleCursor(HandleKind k) => k switch
    {
        HandleKind.TopLeft or HandleKind.BottomRight => Cursors.SizeNWSE,
        HandleKind.TopRight or HandleKind.BottomLeft => Cursors.SizeNESW,
        HandleKind.Top or HandleKind.Bottom => Cursors.SizeNS,
        HandleKind.Left or HandleKind.Right => Cursors.SizeWE,
        _ => Cursors.SizeAll,
    };

    private Cursor ToolCursor() => _session.Tool switch
    {
        DrawingTool.None => Cursors.Cross,
        DrawingTool.Text => Cursors.IBeam,
        DrawingTool.Blur or DrawingTool.Number => Cursors.Cross,
        _ => Cursors.Pen,
    };

    // ------------------------------------------------------------------ отрисовка

    private void UpdateHoverCursor(Point p)
    {
        var state = _session.State;
        if (state is OverlayState.Idle or OverlayState.Canceled) { Root.Cursor = Cursors.Cross; return; }
        if (HitFrameHandle(p) is var fh && fh != HandleKind.None) { Root.Cursor = HandleCursor(fh); return; }
        bool editing = _session.EditorEnabled && IsOwner && _session.HasSelection;
        if (editing && _session.SelectedObject is { } sel && sel.CanResize && HitObjectHandle(sel, p) != HandleKind.None) { Root.Cursor = Cursors.SizeAll; return; }
        if (editing && HitObject(p) is not null) { Root.Cursor = Cursors.Arrow; return; }
        if (IsOwner && _session.HasSelection && _session.Selection.Contains(p) && _session.Tool == DrawingTool.None) { Root.Cursor = Cursors.SizeAll; return; }
        Root.Cursor = editing ? ToolCursor() : Cursors.Cross;
    }

    private void UpdateHint(Point p)
    {
        bool show = !_session.HasSelection && _session.State == OverlayState.Idle && IsMouseOver;
        Hint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        Hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = Hint.DesiredSize.Width; var h = Hint.DesiredSize.Height;
        var x = Math.Min(p.X + 18, ActualWidth - w - 4);
        var y = Math.Min(p.Y + 22, ActualHeight - h - 4);
        Canvas.SetLeft(Hint, Math.Max(0, x));
        Canvas.SetTop(Hint, Math.Max(0, y));
    }

    private void Refresh()
    {
        if (!IsLoaded) return;
        var full = WindowRect;
        var owner = IsOwner && _session.HasSelection;
        var sel = _session.Selection;

        // Затемнение
        Geometry dim = new RectangleGeometry(full);
        if (owner) dim = new CombinedGeometry(GeometryCombineMode.Exclude, dim, new RectangleGeometry(sel));
        Dim.Data = dim;

        // Рамка
        FrameLayer.Visibility = owner ? Visibility.Visible : Visibility.Collapsed;
        if (owner)
        {
            Canvas.SetLeft(FrameInterior, sel.X); Canvas.SetTop(FrameInterior, sel.Y);
            FrameInterior.Width = Math.Max(0, sel.Width); FrameInterior.Height = Math.Max(0, sel.Height);
            FrameInterior.IsHitTestVisible = false; // клики обрабатываются вручную
            Canvas.SetLeft(FrameBorder, sel.X - 0.5); Canvas.SetTop(FrameBorder, sel.Y - 0.5);
            FrameBorder.Width = sel.Width + 1; FrameBorder.Height = sel.Height + 1;

            bool showHandles = _session.State is not (OverlayState.Selecting or OverlayState.ResizingFrame or OverlayState.MovingFrame or OverlayState.Recording or OverlayState.Scrolling);
            var outer = sel; outer.Inflate(3, 3);
            foreach (var (kind, h) in _frameHandles)
            {
                h.Visibility = showHandles ? Visibility.Visible : Visibility.Collapsed;
                var c = HandlePoint(outer, kind);
                Canvas.SetLeft(h, c.X - HandleSize / 2); Canvas.SetTop(h, c.Y - HandleSize / 2);
                h.IsHitTestVisible = false;
            }

            var px = ToScreenRect(sel);
            SizeText.Text = $"{px.Width} x {px.Height}";
            SizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var lw = SizeLabel.DesiredSize.Width; var lh = SizeLabel.DesiredSize.Height;
            double lx = sel.X, ly = sel.Y - lh - 3;
            if (ly < 0) { lx = sel.X + 4; ly = sel.Y + 4; }
            lx = Math.Clamp(lx, 0, Math.Max(0, ActualWidth - lw));
            Canvas.SetLeft(SizeLabel, lx); Canvas.SetTop(SizeLabel, ly);
        }

        RefreshObjectSelection();
        RefreshPanels();
        if (_session.State != OverlayState.Idle) Hint.Visibility = Visibility.Collapsed;
        UpdateHoverCursor(Mouse.GetPosition(Root));
    }

    private static Point HandlePoint(Rect r, HandleKind k) => k switch
    {
        HandleKind.TopLeft => r.TopLeft,
        HandleKind.Top => new Point(r.Left + r.Width / 2, r.Top),
        HandleKind.TopRight => r.TopRight,
        HandleKind.Right => new Point(r.Right, r.Top + r.Height / 2),
        HandleKind.BottomRight => r.BottomRight,
        HandleKind.Bottom => new Point(r.Left + r.Width / 2, r.Bottom),
        HandleKind.BottomLeft => r.BottomLeft,
        _ => new Point(r.Left, r.Top + r.Height / 2),
    };

    private void RefreshObjectSelection()
    {
        ObjectSelectionLayer.Children.Clear();
        var obj = _session.SelectedObject;
        if (obj is null || !IsOwner || !_objects.Contains(obj)) return;
        if (_session.State is OverlayState.EditingText or OverlayState.DrawingObject) return;

        var b = obj.Bounds;
        if (b.IsEmpty) return;
        b.Inflate(4, 4);
        var frame = new Rectangle
        {
            Width = b.Width, Height = b.Height, Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 },
        };
        Canvas.SetLeft(frame, b.X); Canvas.SetTop(frame, b.Y);
        ObjectSelectionLayer.Children.Add(frame);
        var shadow = new Rectangle { Width = b.Width, Height = b.Height, Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 }, StrokeDashOffset = 3 };
        Canvas.SetLeft(shadow, b.X); Canvas.SetTop(shadow, b.Y);
        ObjectSelectionLayer.Children.Add(shadow);

        foreach (var kind in obj.Handles)
        {
            var c = obj.HandlePosition(kind);
            Shape handle = kind is HandleKind.Start or HandleKind.End
                ? new Ellipse { Width = HandleSize + 2, Height = HandleSize + 2, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 0.5 }
                : new Rectangle { Width = HandleSize, Height = HandleSize, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 0.5 };
            Canvas.SetLeft(handle, c.X - handle.Width / 2); Canvas.SetTop(handle, c.Y - handle.Height / 2);
            ObjectSelectionLayer.Children.Add(handle);
        }
    }

    private void RefreshPanels()
    {
        if (_toolbar is null || _actions is null) return;
        bool show = _session.EditorEnabled && IsOwner && _session.HasSelection
                    && _session.State is OverlayState.Selected or OverlayState.ToolSelected or OverlayState.EditingText;
        PanelLayer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        _toolbar.SetTool(_session.Tool);
        _toolbar.SetThickness(_session.Thickness);
        _toolbar.SetColor(_session.Color);
        _toolbar.SetPalette(_session.Palette, _session.Color);
        _toolbar.SetCanUndo(_session.Undo.CanUndo);

        var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
        _toolbar.Measure(inf);
        _actions.Measure(inf);
        var (tp, ap) = PanelPlacer.Place(_session.Selection, _toolbar.DesiredSize, _actions.DesiredSize, new Size(ActualWidth, ActualHeight));
        Canvas.SetLeft(_toolbar, tp.X); Canvas.SetTop(_toolbar, tp.Y);
        Canvas.SetLeft(_actions, ap.X); Canvas.SetTop(_actions, ap.Y);
    }
}
