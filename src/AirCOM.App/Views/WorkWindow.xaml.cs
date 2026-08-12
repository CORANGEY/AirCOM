using System.Windows;

namespace AirCOM.App.Views;

public partial class WorkWindow : Window
{
    public WorkWindow(AirCOM.App.Services.AppRole role)
    {
        InitializeComponent();
        DataContext = new AirCOM.App.ViewModels.WorkViewModel(role, this);
    }
}
