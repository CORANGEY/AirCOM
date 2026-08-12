using System.Reflection;
using System.Windows;

namespace AirCOM.App.Views;

public partial class RoleSelectWindow : Window
{
    public RoleSelectWindow()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionRun.Text = ver?.ToString(3) ?? "0.0.0";
    }

    private void OnSelectA(object sender, RoutedEventArgs e)
    {
        var work = new WorkWindow(AirCOM.App.Services.AppRole.ASide);
        work.Show();
        Close();
    }

    private void OnSelectB(object sender, RoutedEventArgs e)
    {
        var work = new WorkWindow(AirCOM.App.Services.AppRole.BSide);
        work.Show();
        Close();
    }
}
