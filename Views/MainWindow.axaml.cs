using Avalonia.Controls;
using CourseManager.ViewModels;

namespace CourseManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // ШІ: Claude Code (Claude Opus 5.5) – передача діалогу підтвердження у ViewModel.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    // ViewModel не знає про вікна, тому показ діалогу підтвердження передається їй звідси.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
            vm.Confirm = message => ConfirmDialog.ShowAsync(this, message);
    }
}
