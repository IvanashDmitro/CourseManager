using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourseManager.Models;

namespace CourseManager.ViewModels;

// ШІ: Claude Code (Claude Opus 5.5) – редактор блоку «Запис на курс і оцінки»: запис обраного здобувача на обраний курс,
// список робіт рядка з підказкою «наступна невиконана робота», виставлення оцінки, видалення запису з підтвердженням.
// Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
public partial class EnrollmentEditorViewModel : EditorViewModelBase
{
    private const string NoRowHint = "Оберіть рядок у таблиці, щоб виставити оцінку.";

    public EnrollmentEditorViewModel(IShellContext shell) : base(shell) { }

    public override string EditorName => "Запис на курс і оцінки";

    // Роботи курсу обраного рядка таблиці.
    public ObservableCollection<CourseActivity> Activities { get; } = new();

    [ObservableProperty] private CourseActivity? _selectedActivity;
    [ObservableProperty] private string _scoreText = string.Empty;
    [ObservableProperty] private bool _isLate;
    [ObservableProperty] private string _hint = NoRowHint;

    public override void OnEnrollmentSelected(Enrollment? enrollment) => FillActivities();
    public override void OnDataChanged() => FillActivities();

    partial void OnSelectedActivityChanged(CourseActivity? value) => UpdateHint();

    // Список робіт курсу обраного рядка; одразу пропонується наступна невиконана робота.
    private void FillActivities()
    {
        Activities.Clear();
        var enrollment = Shell.SelectedEnrollment;
        if (enrollment == null)
        {
            SelectedActivity = null;
            UpdateHint();
            return;
        }

        foreach (var act in enrollment.Course.Activities.OrderBy(a => a.Deadline))
            Activities.Add(act);
        SelectedActivity = enrollment.NextActivity ?? Activities.FirstOrDefault();
        UpdateHint();
    }

    private void UpdateHint()
    {
        var enrollment = Shell.SelectedEnrollment;
        var activity = SelectedActivity;
        if (enrollment == null)
        {
            Hint = NoRowHint;
            return;
        }

        string who = enrollment.Student.FullName;
        if (activity == null)
        {
            Hint = $"{who}: оберіть роботу зі списку.";
            return;
        }

        var grade = enrollment.FindGrade(activity);
        Hint = grade == null
            ? $"{who} – {activity.Title}: ще не оцінено, дедлайн {activity.Deadline:dd.MM.yyyy}."
            : $"{who} – {activity.Title}: зараз {grade.RawScore} з {activity.MaxScore}" +
              (grade.IsLate ? " (із запізненням)" : "") + ". Нова оцінка замінить її.";
    }

    [RelayCommand]
    private void Enroll()
    {
        var student = Shell.SelectedStudent;
        var course = Shell.SelectedCourse;
        if (student == null || course == null)
        {
            SetStatus("Оберіть здобувача в «Керуванні здобувачами» і курс у «Керуванні курсами», потім натисніть «Записати».");
            return;
        }

        RunSafe("запис на курс", () =>
        {
            var enrollment = Service.EnrollStudent(student, course);
            Shell.NotifyDataChanged();
            // Новий запис одразу виділяємо в таблиці (якщо його не приховує фільтр), щоб можна було виставити оцінку.
            Shell.SelectedEnrollment = enrollment;
            bool visible = Shell.SelectedEnrollment == enrollment;
            SetStatus($"{student.FullName} записано на курс {course.Code}." +
                      (visible ? " Запис виділено в таблиці – можна виставляти оцінки."
                               : " Запис приховано пошуком чи фільтром."));
        });
    }

    [RelayCommand]
    private void AddGrade()
    {
        var enrollment = Shell.SelectedEnrollment;
        var activity = SelectedActivity;
        if (enrollment == null)
        {
            SetStatus(NoRowHint);
            return;
        }
        if (activity == null)
        {
            SetStatus("Оберіть роботу зі списку.");
            return;
        }
        if (!TryParseNumber(ScoreText, out double rawScore))
        {
            SetStatus($"Помилка: введіть бал числом від 0 до {activity.MaxScore}.");
            return;
        }

        RunSafe("виставлення оцінки", () =>
        {
            bool replaced = Service.AddGrade(enrollment, activity, rawScore, IsLate);
            ScoreText = string.Empty;
            IsLate = false;
            // Після оновлення рядок лишається обраним (якщо не зник через фільтр), у списку – наступна невиконана робота.
            Shell.NotifyDataChanged();
            SetStatus((replaced
                    ? $"Оцінку за {activity.Title} змінено на {rawScore}."
                    : $"Оцінку {rawScore} за {activity.Title} додано.") +
                $" {enrollment.Student.FullName}: рейтинг {enrollment.TotalRating} ({enrollment.NationalGrade}).");
        });
    }

    [RelayCommand]
    private async Task DeleteEnrollmentAsync()
    {
        var enrollment = Shell.SelectedEnrollment;
        if (enrollment == null)
        {
            SetStatus("Оберіть у таблиці рядок, який треба видалити.");
            return;
        }

        if (!await ConfirmAsync($"Зняти {enrollment.Student.FullName} з курсу {enrollment.Course.Code}?\n\n" +
                                $"Буде видалено {enrollment.Grades.Count} оцінок. Сам здобувач залишиться і його можна записати на інший курс."))
        {
            SetStatus("Видалення запису скасовано.");
            return;
        }

        RunSafe("видалення запису", () =>
        {
            Service.DeleteEnrollment(enrollment);
            Shell.NotifyDataChanged();
            SetStatus("Запис на курс видалено.");
        });
    }
}
