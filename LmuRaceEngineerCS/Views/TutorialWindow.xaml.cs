using System.Windows;
using System.Windows.Input;

namespace LmuRaceEngineerCS.Views;

public partial class TutorialWindow : Window
{
    public TutorialWindow()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void GotIt_Click(object sender, RoutedEventArgs e)
    {
        if (ChkDontShow.IsChecked == true)
        {
            App.Settings.ShowTutorialOnStartup = false;
            App.Settings.Save();
        }
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
