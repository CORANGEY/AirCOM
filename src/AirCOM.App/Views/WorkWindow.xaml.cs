using System.ComponentModel;
using System.Windows;

namespace AirCOM.App.Views;

public partial class WorkWindow : Window
{
    private readonly AirCOM.App.ViewModels.WorkViewModel _vm;

    public WorkWindow(AirCOM.App.Services.AppRole role)
    {
        InitializeComponent();
        _vm = new AirCOM.App.ViewModels.WorkViewModel(role, this);
        DataContext = _vm;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Dispose the engine host so TCP connections get FIN and serial ports
        // are released. Without this, the peer won't detect the disconnect and
        // the COM port may stay locked.
        _vm.Dispose();
    }
}
