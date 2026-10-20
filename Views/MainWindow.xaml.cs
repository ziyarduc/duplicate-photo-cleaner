using System.Windows;
using DuplicatePhotoCleaner.ViewModels;

namespace DuplicatePhotoCleaner.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
