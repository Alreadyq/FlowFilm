using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace PictureBook.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Opened += OnOpenedHandler;
    }

    private async void OnOpenedHandler(object? sender, EventArgs e)
    {
        if (App.SmokeMode)
        {
            await Task.Delay(250);
            Console.WriteLine("SMOKE_OK");
            await Dispatcher.UIThread.InvokeAsync(Close);
        }
    }
}
