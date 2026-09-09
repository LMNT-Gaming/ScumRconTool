using System.Windows;
using System.Windows.Controls;
using ScumRconTool.ViewModels;

namespace ScumRconTool.Views;

public partial class VehicleInsuranceView : UserControl
{
    public VehicleInsuranceView() => InitializeComponent();
    private void TokenLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is PasswordBox box) box.Password = vm.Settings.VehicleInsurance.WebToken;
    }
    private void TokenChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is PasswordBox box && box.IsLoaded) vm.Settings.VehicleInsurance.WebToken = box.Password;
    }
}
