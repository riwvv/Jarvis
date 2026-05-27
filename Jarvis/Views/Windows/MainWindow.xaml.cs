using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Jarvis.Views.Windows;

public partial class MainWindow : Window {
    private double _time = 0;
    public MainWindow() {
        InitializeComponent();
        Loaded += (s, e) => CompositionTarget.Rendering += AnimateTentacles;
        this.WindowStartupLocation = WindowStartupLocation.Manual;

        var screenWidth = SystemParameters.WorkArea.Width;
        var screenHeight = SystemParameters.WorkArea.Height;

        this.Left = screenWidth - this.Width;
        this.Top = screenHeight - this.Height;
    }

    private void AnimateTentacles(object? sender, EventArgs e) {
        _time += 0.03;

        var line1 = FindName("Tentacle1") as Path;
        var line2 = FindName("Tentacle2") as Path;
        var line3 = FindName("Tentacle3") as Path;

        if (line1 != null)
            line1.Data = CreateBezierCurve(
                103, 100,
                180 + Math.Sin(_time) * 20, 130 + Math.Cos(_time * 1.3) * 15,
                240 + Math.Sin(_time * 1.7) * 15, 75 + Math.Sin(_time) * 20,
                297, 100);

        if (line2 != null)
            line2.Data = CreateBezierCurve(
                103, 100,
                135 + Math.Cos(_time * 1.5) * 25, 75 + Math.Sin(_time) * 20,
                230 + Math.Sin(_time) * 20, 125 + Math.Cos(_time * 1.2) * 15,
                297, 100);

        if (line3 != null)
            line3.Data = CreateBezierCurve(
                103, 100,
                180 + Math.Sin(_time) * 40, 100 + Math.Cos(_time * 1.5) * 30,
                220 + Math.Cos(_time) * 40, 100 + Math.Sin(_time * 1.8) * 30,
                297, 100);
    }

    private Geometry CreateBezierCurve(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3) {
        var figure = new PathFigure { StartPoint = new Point(x0, y0) };
        figure.Segments.Add(new BezierSegment(
            new Point(x1, y1),
            new Point(x2, y2),
            new Point(x3, y3), true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
        this.DragMove();
    }
}
