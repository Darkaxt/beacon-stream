using System.Windows;
using Beacon.Cockpit.Cockpit;

namespace Beacon.Cockpit;

public partial class MainWindow : Window
{
    public MainWindow(CockpitShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
