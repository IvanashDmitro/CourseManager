using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CourseManager.Views;

// ШІ: Claude Code (Claude Opus 5.5) – модальне вікно підтвердження «Так / Скасувати» без окремого .axaml
// (тексти кнопок українською, Esc і закриття хрестиком означають «Скасувати»).
// Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
public sealed class ConfirmDialog : Window
{
    private ConfirmDialog(string message)
    {
        Title = "Підтвердження";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var yes = new Button { Content = "Так", MinWidth = 100, HorizontalContentAlignment = HorizontalAlignment.Center };
        var no = new Button { Content = "Скасувати", MinWidth = 100, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        yes.Click += (_, _) => Close(true);
        no.Click += (_, _) => Close(false);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { no, yes }
                }
            }
        };
    }

    // Повертає true лише при натисканні «Так».
    public static Task<bool> ShowAsync(Window owner, string message) =>
        new ConfirmDialog(message).ShowDialog<bool>(owner);
}
