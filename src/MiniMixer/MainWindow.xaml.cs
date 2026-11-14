using System.Windows;
using System.Windows.Controls;
using MiniMixer.ViewModels;

namespace MiniMixer;

public partial class MainWindow : Window
{
    private enum LayoutMode { None, Wide, Medium, Narrow }

    // Anchos a partir de los que cambia la disposición (en DIPs, ancho útil del ScrollViewer)
    private const double WideMinWidth = 1180;   // micro | virtuales | salidas+apps en una fila
    private const double MediumMinWidth = 640;  // micro | virtuales  /  salidas+apps debajo

    private readonly MainViewModel _viewModel;
    private LayoutMode _mode = LayoutMode.None;

    public MainWindow()
    {
        InitializeComponent();

        // No abrir más grande que la pantalla
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width * 0.95);
        Height = Math.Min(Height, area.Height * 0.95);

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        RootScroll.SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);

        // Oculta en la bandeja: el audio sigue, pero no se gastan recursos en dibujar medidores.
        IsVisibleChanged += (_, _) => _viewModel.SetMetersActive(IsVisible);

        // Closed sólo llega al salir de verdad (cerrar con la X se cancela y oculta la ventana).
        Closed += (_, _) => _viewModel.Dispose();
    }

    /// <summary>
    /// Diseño adaptable:
    ///  - Ancho:  [Micro][Virtuales][Salidas + Apps]
    ///  - Medio:  [Micro][Virtuales (se reacomodan)] / [Salidas + Apps]
    ///  - Estrecho: todo en una columna
    /// Lo que no cabe en vertical se desplaza con la barra de scroll.
    /// </summary>
    private void ApplyLayout(double width)
    {
        var mode = width >= WideMinWidth ? LayoutMode.Wide
                 : width >= MediumMinWidth ? LayoutMode.Medium
                 : LayoutMode.Narrow;
        if (mode == _mode) return;
        _mode = mode;

        RootGrid.ColumnDefinitions.Clear();
        RootGrid.RowDefinitions.Clear();

        switch (mode)
        {
            case LayoutMode.Wide:
                // [Micro][Virtuales / Procesado A1-A3][Salidas + Apps]
                AddColumns(GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star));
                AddRows(1);
                Place(MicSection, 0, 0);
                Place(MiddleColumn, 0, 1);
                Place(RightSection, 0, 2);
                break;

            case LayoutMode.Medium:
                AddColumns(GridLength.Auto, new GridLength(1, GridUnitType.Star));
                AddRows(2);
                Place(MicSection, 0, 0);
                Place(MiddleColumn, 0, 1);
                Place(RightSection, 1, 0, columnSpan: 2);
                break;

            default:
                AddColumns(new GridLength(1, GridUnitType.Star));
                AddRows(3);
                Place(RightSection, 0, 0); // en pantallas pequeñas, lo más usado primero
                Place(MicSection, 1, 0);
                Place(MiddleColumn, 2, 0);
                break;
        }
    }

    private void AddColumns(params GridLength[] widths)
    {
        foreach (var w in widths) RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
    }

    private void AddRows(int count)
    {
        for (int i = 0; i < count; i++) RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    }

    private static void Place(UIElement element, int row, int column, int columnSpan = 1, int rowSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
        Grid.SetRowSpan(element, rowSpan);
    }

    private void OnRestartClick(object sender, RoutedEventArgs e) => ((App)Application.Current).Restart();
}
