using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourseManager.Models;
using Serilog;

namespace CourseManager.ViewModels;

// Блок «Роботи курсу»: CRUD лабораторних, практичних і тестів обраного курсу.
public partial class MainWindowViewModel
{
    // Блок «Роботи курсу»: роботи курсу, обраного в «Керуванні курсами», і поля форми.
    public ObservableCollection<CourseActivity> CourseActivities { get; } = new();
    public IReadOnlyList<string> ActivityTypes => ActivityFactory.TypeNames;
    [ObservableProperty] private CourseActivity? _editedActivity;
    [ObservableProperty] private string _activityType = ActivityFactory.Lab;
    [ObservableProperty] private string _activityTitle = string.Empty;
    [ObservableProperty] private string _activityMaxText = string.Empty;
    [ObservableProperty] private DateTime? _activityDeadline = DateTime.Today.AddDays(7);
    [ObservableProperty] private string _activityExtraText = ActivityFactory.DefaultExtra(ActivityFactory.Lab).ToString();
    [ObservableProperty] private string _activityExtraLabel = ActivityFactory.ExtraLabel(ActivityFactory.Lab);
    [ObservableProperty] private string _activitiesInfo = "Оберіть курс, щоб побачити його роботи.";

    // ШІ: Claude Code (Claude Opus 5.5) – заповнення форми «Роботи курсу»: список робіт обраного курсу,
    // поля обраної роботи, підпис додаткового параметра залежно від типу.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    private void FillCourseActivities()
    {
        var keep = EditedActivity;
        CourseActivities.Clear();
        var course = SelectedCourse;
        if (course == null)
        {
            EditedActivity = null;
            ActivitiesInfo = "Оберіть курс, щоб побачити його роботи.";
            return;
        }

        foreach (var act in course.Activities.OrderBy(a => a.Deadline))
            CourseActivities.Add(act);
        EditedActivity = keep != null && CourseActivities.Contains(keep) ? keep : null;
        ActivitiesInfo = $"{course.Code}: робіт {course.Activities.Count}, сума максимумів {course.MaxTotal} " +
                         "(рейтинг рахується у відсотках від неї).";
    }

    partial void OnEditedActivityChanged(CourseActivity? value)
    {
        if (value == null) return;
        ActivityType = value.TypeName;
        ActivityTitle = value.Title;
        ActivityMaxText = value.MaxScore.ToString(System.Globalization.CultureInfo.CurrentCulture);
        ActivityDeadline = value.Deadline.ToDateTime(TimeOnly.MinValue);
        ActivityExtraText = ActivityFactory.GetExtra(value).ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    partial void OnActivityTypeChanged(string value)
    {
        if (value == null) return;
        ActivityExtraLabel = ActivityFactory.ExtraLabel(value);
        // Для нової роботи іншого типу підставляємо типовий параметр (бонус, штраф або кількість питань).
        if (EditedActivity == null || EditedActivity.TypeName != value)
            ActivityExtraText = ActivityFactory.DefaultExtra(value).ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    // ШІ: Claude Code (Claude Opus 5.5) – команди CRUD робіт курсу: перевірка введення, підтвердження видалення,
    // збереження в courses.json, оновлення таблиці (рейтинги залежать від робіт) і списку робіт для оцінок.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    [RelayCommand]
    private void AddActivity()
    {
        var course = FindCourse(NewCourseCode);
        if (course == null) return;
        var values = ReadActivityForm();
        if (values == null) return;

        try
        {
            _service.AddActivity(course, values);
            AfterActivitiesChanged();
            EditedActivity = values;
            StatusText = $"До курсу {course.Code} додано: {values.TypeName.ToLowerInvariant()} «{values.Title}» " +
                         $"(макс. {values.MaxScore}). Сума максимумів курсу тепер {course.MaxTotal}.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка додавання роботи");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
    }

    [RelayCommand]
    private void UpdateActivity()
    {
        var course = FindCourse(NewCourseCode);
        if (course == null) return;
        var activity = EditedActivity;
        if (activity == null || !course.Activities.Contains(activity))
        {
            StatusText = "Оберіть роботу у списку «Роботи курсу», яку треба змінити.";
            return;
        }
        if (ActivityType != activity.TypeName)
        {
            StatusText = $"Тип роботи змінити не можна («{activity.TypeName}»). Видаліть роботу і додайте нову потрібного типу.";
            return;
        }
        var values = ReadActivityForm();
        if (values == null) return;

        try
        {
            _service.UpdateActivity(course, activity, values);
            AfterActivitiesChanged();
            StatusText = $"Роботу «{activity.Title}» курсу {course.Code} оновлено. Сума максимумів курсу {course.MaxTotal}.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка оновлення роботи");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
    }

    [RelayCommand]
    private async Task DeleteActivityAsync()
    {
        var course = FindCourse(NewCourseCode);
        if (course == null) return;
        var activity = EditedActivity;
        if (activity == null || !course.Activities.Contains(activity))
        {
            StatusText = "Оберіть роботу у списку «Роботи курсу», яку треба видалити.";
            return;
        }

        int grades = _service.CountGrades(activity);
        if (!await AskAsync($"Видалити роботу «{activity.Title}» з курсу {course.Code}?\n\n" +
                            $"Разом із нею буде видалено {grades} оцінок здобувачів за цю роботу."))
        {
            StatusText = "Видалення роботи скасовано.";
            return;
        }

        try
        {
            int removed = _service.DeleteActivity(course, activity);
            EditedActivity = null;
            AfterActivitiesChanged();
            StatusText = $"Роботу «{activity.Title}» видалено з курсу {course.Code} (оцінок видалено: {removed}).";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка видалення роботи");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
    }

    // Зчитує поля форми у новий (ще не доданий) об'єкт роботи; при помилці пише її в рядок стану.
    private CourseActivity? ReadActivityForm()
    {
        if (!TryParseNumber(ActivityMaxText, out double max))
        {
            StatusText = "Помилка: введіть максимальний бал числом, наприклад 15.";
            return null;
        }
        if (ActivityDeadline == null)
        {
            StatusText = "Помилка: оберіть дату дедлайну.";
            return null;
        }
        if (!TryParseNumber(ActivityExtraText, out double extra))
        {
            StatusText = $"Помилка: поле «{ActivityExtraLabel}» має бути числом.";
            return null;
        }

        try
        {
            return ActivityFactory.Create(ActivityType, ActivityTitle, max,
                DateOnly.FromDateTime(ActivityDeadline.Value), extra);
        }
        catch (ArgumentException ex)
        {
            StatusText = $"Помилка: {CleanMessage(ex)}";
            return null;
        }
    }

    private void AfterActivitiesChanged()
    {
        _storage.Save(DefaultJsonPath, _service.CreateSnapshot());
        FillCourseActivities();
        RefreshView();
        // Список робіт для виставлення оцінки теж міг змінитися.
        if (SelectedEnrollment != null)
            FillActivities();
    }
}
