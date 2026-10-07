using Avalonia.Controls;
using DrakesForge.App.Services;

namespace DrakesForge.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Dialogs.Owner = this;
    }
}
