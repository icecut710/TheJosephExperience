using System;
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using JosephExperience.Models;
using JosephExperience.Utilities;

namespace JosephExperience.Views;

public partial class Overlay3DWindow : Window
{
    private Action? _onCompleted;
    private DispatcherTimer? _holdTimer;
    private DispatcherTimer? _spinTimer;
    private double _angle;
    private double _speedDegPerSec;
    private TimeSpan _hold;
    private Model3DGroup? _model;
    private RotateTransform3D? _rotateY;
    private RotateTransform3D? _rotateX;
    private bool _disposed;

    public Overlay3DWindow()
    {
        InitializeComponent();
    }

    public void ShowModel(string filePath, AppSettings settings, Action? onCompleted)
    {
        _onCompleted = onCompleted;
        _speedDegPerSec = settings.Model3DRotationSpeed > 0 ? settings.Model3DRotationSpeed : 45.0;
        _hold = TimeSpan.FromMilliseconds(Math.Max(400, settings.OverlayDurationMs));

        _model = ObjModelLoader.Load(filePath);
        if (_model == null)
        {
            AppLog.Warn($"3D overlay: failed to load model '{filePath}'.");
            onCompleted?.Invoke();
            return;
        }

        NormalizeModel(_model, settings.Model3DScale);

        _angle = 0;
        _rotateX = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 15));
        _rotateY = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0));
        var transform = new Transform3DGroup();
        transform.Children.Add(_rotateX);
        transform.Children.Add(_rotateY);
        _model.Transform = transform;

        ModelVisual.Content = _model;

        if (settings.Model3DShowGrid) BuildGrid();

        PositionCorner(settings.Model3DCorner, settings.Model3DScale);

        Opacity = 0;
        Show();
        var fadeIn = new DoubleAnimation(0, Math.Clamp(settings.ImageOpacity, 0.0, 1.0), TimeSpan.FromMilliseconds(200));
        fadeIn.Completed += (_, _) => StartHold();
        BeginAnimation(OpacityProperty, fadeIn);
    }

    private void StartHold()
    {
        StartSpin();
        StopHoldTimer();
        _holdTimer = new DispatcherTimer { Interval = _hold };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer?.Stop();
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(250));
            fade.Completed += (_, _) => Complete();
            BeginAnimation(OpacityProperty, fade);
        };
        _holdTimer.Start();
    }

    private void StartSpin()
    {
        _spinTimer?.Stop();
        var last = Environment.TickCount;
        _spinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _spinTimer.Tick += (_, _) =>
        {
            var now = Environment.TickCount;
            var dt = (now - last) / 1000.0;
            last = now;
            _angle = (_angle + _speedDegPerSec * dt) % 360;
            if (_rotateY?.Rotation is AxisAngleRotation3D ay) ay.Angle = _angle;
            if (_rotateX?.Rotation is AxisAngleRotation3D ax) ax.Angle = 15 + 6 * Math.Sin(_angle * Math.PI / 180);
        };
        _spinTimer.Start();
    }

    private void StopHoldTimer() { _holdTimer?.Stop(); _holdTimer = null; }
    private void StopSpin() { _spinTimer?.Stop(); _spinTimer = null; }

    private void Complete()
    {
        StopSpin();
        StopHoldTimer();
        Hide();
        var cb = _onCompleted;
        _onCompleted = null;
        cb?.Invoke();
    }

    private void Overlay3DWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            style |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, style);
        }
    }

    private void NormalizeModel(Model3DGroup model, double scaleFraction)
    {
        var minX = double.MaxValue; var minY = double.MaxValue; var minZ = double.MaxValue;
        var maxX = double.MinValue; var maxY = double.MinValue; var maxZ = double.MinValue;
        foreach (var child in model.Children)
        {
            if (child is not GeometryModel3D g) continue;
            var mesh = g.Geometry as MeshGeometry3D;
            if (mesh == null) continue;
            foreach (var p in mesh.Positions)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
            }
        }
        if (minX >= maxX) return;
        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;
        var cz = (minZ + maxZ) / 2;
        var size = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        if (size < 1e-6) return;
        var s = 2.0 / size;
        var tg = new Transform3DGroup();
        tg.Children.Add(new TranslateTransform3D(-cx, -cy, -cz));
        tg.Children.Add(new ScaleTransform3D(s, s, s));
        if (model.Transform != null) tg.Children.Add(model.Transform);
        model.Transform = tg;
    }

    private void PositionCorner(OverlayCorner corner, double scaleFraction)
    {
        var screen = SystemParameters.WorkArea;
        var minDim = Math.Min(screen.Width, screen.Height);
        var size = Math.Max(80, minDim * Math.Clamp(scaleFraction, 0.1, 0.6));
        Width = size;
        Height = size;
        var m = 24;
        double left, top;
        switch (corner)
        {
            case OverlayCorner.TopLeft: left = screen.Left + m; top = screen.Top + m; break;
            case OverlayCorner.TopRight: left = screen.Right - size - m; top = screen.Top + m; break;
            case OverlayCorner.BottomLeft: left = screen.Left + m; top = screen.Bottom - size - m; break;
            case OverlayCorner.BottomRight: left = screen.Right - size - m; top = screen.Bottom - size - m; break;
            default: left = screen.Left + (screen.Width - size) / 2; top = screen.Top + (screen.Height - size) / 2; break;
        }
        Left = left;
        Top = top;
    }

    private void BuildGrid()
    {
        var grid = new Model3DGroup();
        var mat = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(40, 120, 200, 255)));
        const int n = 8;
        const double ext = 1.6;
        for (int i = -n; i <= n; i++)
        {
            var t = i / (double)n * ext;
            grid.Children.Add(MakeLine(new Point3D(-ext, -1.2, t), new Point3D(ext, -1.2, t), mat));
            grid.Children.Add(MakeLine(new Point3D(t, -1.2, -ext), new Point3D(t, -1.2, ext), mat));
        }
        GridVisual.Content = grid;
    }

    private static GeometryModel3D MakeLine(Point3D a, Point3D b, Material mat)
    {
        var mesh = new MeshGeometry3D();
        var dir = b - a;
        var perp = Math.Abs(dir.X) > Math.Abs(dir.Z)
            ? Vector3D.CrossProduct(dir, new Vector3D(0, 1, 0))
            : Vector3D.CrossProduct(dir, new Vector3D(1, 0, 0));
        if (perp.Length < 1e-6) perp = new Vector3D(0, 0, 1);
        perp.Normalize();
        var o = perp * 0.005;
        mesh.Positions.Add(a - o); mesh.Positions.Add(a + o);
        mesh.Positions.Add(b + o); mesh.Positions.Add(b - o);
        mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(1); mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(2); mesh.TriangleIndices.Add(3);
        return new GeometryModel3D(mesh, mat);
    }

    protected override void OnClosed(EventArgs e)
    {
        Complete();
        base.OnClosed(e);
        Dispose();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            StopSpin();
            StopHoldTimer();
            if (_model != null)
            {
                foreach (var child in _model.Children)
                {
                    if (child is GeometryModel3D g && g.Geometry is MeshGeometry3D m)
                    {
                        m.Positions?.Clear();
                        m.TriangleIndices?.Clear();
                        m.Normals?.Clear();
                        m.TextureCoordinates?.Clear();
                    }
                }
                _model.Children.Clear();
                _model = null;
            }
            _rotateX = null;
            _rotateY = null;
            ModelVisual.Content = null;
            GridVisual.Content = null;
        }
        _disposed = true;
    }

    public void Dispose() => Dispose(true);
}
