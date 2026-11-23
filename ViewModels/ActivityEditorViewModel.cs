using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourseManager.Models;

namespace CourseManager.ViewModels;

// ШІ: Claude Code (Claude Opus 5.5) – редактор блоку «Роботи курсу»: список робіт обраного курсу, форма роботи,
// підпис параметра залежно від типу, CRUD лабораторних, практичних і тестів.
// Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
public partial class ActivityEditorViewModel : EditorViewModelBase
{
    private const string NoCourseInfo = "Оберіть курс, щоб побачити його роботи.";

    public ActivityEditorViewModel(IShellContext shell) : base(shell) { }

    public override string EditorName => "Роботи курсу";

    public ObservableCollection<CourseActivity> Activities { get; } = new();
    public IReadOnlyList<string> Types => ActivityFactory.TypeNames;

    [ObservableProperty] private CourseActivity? _selectedActivity;
    [ObservableProperty] private string _typeName = ActivityFactory.Lab;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _maxScoreText = string.Empty;
    [ObservableProperty] private DateTime? _deadline = DateTime.Today.AddDays(7);
    [ObservableProperty] private string _extraText = ActivityFactory.DefaultExtra(ActivityFactory.Lab).ToString(CultureInfo.CurrentCulture);
    [ObservableProperty] private string _extraLabel = ActivityFactory.ExtraLabel(ActivityFactory.Lab);
    [ObservableProperty] private string _info = NoCourseInfo;

    public override void OnCourseSelected(Course? course) => FillActivities();
    public override void OnDataChanged() => FillActivities();

    // Список робіт обраного курсу; обрана робота лишається обраною, якщо вона ще є в курсі.
    private void FillActivities()
    {
        var keep = SelectedActivity;
        Activities.Clear();
        var course = Shell.SelectedCourse;
        if (course == null)
        {
            SelectedActivity = null;
            Info = NoCourseInfo;
            return;
        }

        foreach (var act in course.Activities.OrderBy(a => a.Deadline))
            Activities.Add(act);
        SelectedActivity = keep != null && Activities.Contains(keep) ? keep : null;
        Info = $"{course.Code}: робіт {course.Activities.Count}, сума максимумів {course.MaxTotal} " +
               "(рейтинг рахується у відсотках від неї).";
    }

    partial void OnSelectedActivityChanged(CourseActivity? value)
    {
        if (value == null) return;
        TypeName = value.TypeName;
        Title = value.Title;
        MaxScoreText = value.MaxScore.ToString(CultureInfo.CurrentCulture);
        Deadline = value.Deadline.ToDateTime(TimeOnly.MinValue);
        ExtraText = ActivityFactory.GetExtra(value).ToString(CultureInfo.CurrentCulture);
    }

    partial void OnTypeNameChanged(string value)
    {
        if (value == null) return;
        ExtraLabel = ActivityFactory.ExtraLabel(value);
        // Для нової роботи іншого типу підставляємо типовий параметр (бонус, штраф або кількість питань).
        if (SelectedActivity == null || SelectedActivity.TypeName != value)
            ExtraText = ActivityFactory.DefaultExtra(value).ToString(CultureInfo.CurrentCulture);
    }

    [RelayCommand]
    private void Add()
    {
        var course = RequireCourse();
        if (course == null) return;
        var values = ReadForm();
        if (values == null) return;

        RunSafe("додавання роботи", () =>
        {
            Service.AddActivity(course, values);
            Shell.NotifyDataChanged();
            SelectedActivity = values;
            SetStatus($"До курсу {course.Code} додано: {values.TypeName.ToLowerInvariant()} «{values.Title}» " +
                      $"(макс. {values.MaxScore}). Сума максимумів курсу тепер {course.MaxTotal}.");
        });
    }

    [RelayCommand]
    private void Update()
    {
        var course = RequireCourse();
        if (course == null) return;
        var activity = SelectedActivity;
        if (activity == null || !course.Activities.Contains(activity))
        {
            SetStatus("Оберіть роботу у списку «Роботи курсу», яку треба змінити.");
            return;
        }
        if (TypeName != activity.TypeName)
        {
            SetStatus($"Тип роботи змінити не можна («{activity.TypeName}»). Видаліть роботу і додайте нову потрібного типу.");
            return;
        }
        var values = ReadForm();
        if (values == null) return;

        RunSafe("оновлення роботи", () =>
        {
            Service.UpdateActivity(course, activity, values);
            Shell.NotifyDataChanged();
            SetStatus($"Роботу «{activity.Title}» курсу {course.Code} оновлено. Сума максимумів курсу {course.MaxTotal}.");
        });
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var course = RequireCourse();
        if (course == null) return;
        var activity = SelectedActivity;
        if (activity == null || !course.Activities.Contains(activity))
        {
            SetStatus("Оберіть роботу у списку «Роботи курсу», яку треба видалити.");
            return;
        }

        int grades = Service.CountGrades(activity);
        if (!await ConfirmAsync($"Видалити роботу «{activity.Title}» з курсу {course.Code}?\n\n" +
                                $"Разом із нею буде видалено {grades} оцінок здобувачів за цю роботу."))
        {
            SetStatus("Видалення роботи скасовано.");
            return;
        }

        RunSafe("видалення роботи", () =>
        {
            int removed = Service.DeleteActivity(course, activity);
            SelectedActivity = null;
            Shell.NotifyDataChanged();
            SetStatus($"Роботу «{activity.Title}» видалено з курсу {course.Code} (оцінок видалено: {removed}).");
        });
    }

    private Course? RequireCourse()
    {
        var course = Shell.SelectedCourse;
        if (course == null)
            SetStatus("Спершу оберіть курс у блоці «Керування курсами».");
        return course;
    }

    // Зчитує поля форми у новий (ще не доданий) об'єкт роботи; при помилці пише її в рядок стану.
    private CourseActivity? ReadForm()
    {
        if (!TryParseNumber(MaxScoreText, out double max))
        {
            SetStatus("Помилка: введіть максимальний бал числом, наприклад 15.");
            return null;
        }
        if (Deadline == null)
        {
            SetStatus("Помилка: оберіть дату дедлайну.");
            return null;
        }
        if (!TryParseNumber(ExtraText, out double extra))
        {
            SetStatus($"Помилка: поле «{ExtraLabel}» має бути числом.");
            return null;
        }

        CourseActivity? values = null;
        var deadline = DateOnly.FromDateTime(Deadline.Value);
        RunSafe("перевірка форми роботи", () => values = ActivityFactory.Create(TypeName, Title, max, deadline, extra));
        return values;
    }
}
