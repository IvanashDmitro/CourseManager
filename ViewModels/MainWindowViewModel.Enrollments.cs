using CommunityToolkit.Mvvm.Input;
using CourseManager.Models;
using Serilog;

namespace CourseManager.ViewModels;

// Блок «Запис на курс і оцінки»: вибір рядка таблиці, запис на курс, виставлення оцінки, видалення запису.
public partial class MainWindowViewModel
{
    // ШІ: Claude Code (Claude Opus 5.5) – вибір рядка таблиці (заповнення полів здобувача й курсу),
    // список робіт для оцінки з підказкою «наступна невиконана робота», текст підказки GradeHint.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    // Клік по рядку таблиці також обирає його здобувача і курс у правій панелі,
    // тож одразу можна змінити ПІБ чи назву курсу і натиснути «Оновити».
    partial void OnSelectedEnrollmentChanged(Enrollment? value)
    {
        // Під час перезаповнення таблиці рядок лише повертається на місце – поля праворуч не чіпаємо.
        if (value != null && !_restoringSelection)
        {
            SelectedStudent = value.Student;
            SelectedCourse = value.Course;
        }
        FillActivities();
    }

    partial void OnSelectedActivityChanged(CourseActivity? value) => UpdateGradeHint();

    // Список робіт курсу обраного рядка; одразу пропонується наступна невиконана робота.
    private void FillActivities()
    {
        SelectedEnrollmentActivities.Clear();
        var enrollment = SelectedEnrollment;
        if (enrollment == null)
        {
            SelectedActivity = null;
            UpdateGradeHint();
            return;
        }

        foreach (var act in enrollment.Course.Activities.OrderBy(a => a.Deadline))
        {
            SelectedEnrollmentActivities.Add(act);
        }
        SelectedActivity = enrollment.NextActivity ?? SelectedEnrollmentActivities.FirstOrDefault();
        UpdateGradeHint();
    }

    private void UpdateGradeHint()
    {
        var enrollment = SelectedEnrollment;
        var activity = SelectedActivity;
        if (enrollment == null)
        {
            GradeHint = NoRowHint;
            return;
        }

        string who = enrollment.Student.FullName;
        if (activity == null)
        {
            GradeHint = $"{who}: оберіть роботу зі списку.";
            return;
        }

        var grade = enrollment.FindGrade(activity);
        GradeHint = grade == null
            ? $"{who} – {activity.Title}: ще не оцінено, дедлайн {activity.Deadline:dd.MM.yyyy}."
            : $"{who} – {activity.Title}: зараз {grade.RawScore} з {activity.MaxScore}" +
              (grade.IsLate ? " (із запізненням)" : "") + ". Нова оцінка замінить її.";
    }

    // ШІ: Claude Code (Claude Opus 5.5) – запис на курс, виставлення оцінки, видалення запису з підтвердженням.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    [RelayCommand]
    private void EnrollSelected()
    {
        var student = FindStudent(NewStudentName, NewStudentGroup);
        if (student == null) return;
        var course = FindCourse(NewCourseCode);
        if (course == null) return;

        try
        {
            var enrollment = _service.EnrollStudent(student, course);
            _storage.Save(DefaultJsonPath, _service.CreateSnapshot());
            RefreshCollections();
            // Новий запис одразу виділяємо в таблиці (якщо його не приховує фільтр), щоб можна було виставити оцінку.
            bool visible = FilteredEnrollments.Contains(enrollment);
            if (visible) SelectedEnrollment = enrollment;
            StatusText = $"{student.FullName} записано на курс {course.Code}." +
                         (visible ? " Запис виділено в таблиці – можна виставляти оцінки."
                                  : " Запис приховано пошуком чи фільтром.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка запису на курс");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
    }

    [RelayCommand]
    private void AddGradeToSelected()
    {
        var enrollment = SelectedEnrollment;
        var activity = SelectedActivity;
        if (enrollment == null)
        {
            StatusText = NoRowHint;
            return;
        }
        if (activity == null)
        {
            StatusText = "Оберіть роботу зі списку.";
            return;
        }

        if (!double.TryParse(GradeRawScoreText.Trim().Replace(',', '.'),
                System.Globalization.CultureInfo.InvariantCulture, out double rawScore))
        {
            StatusText = $"Помилка: введіть бал числом від 0 до {activity.MaxScore}.";
            return;
        }

        try
        {
            bool replaced = _service.AddGrade(enrollment, activity, rawScore, GradeIsLate);
            _storage.Save(DefaultJsonPath, _service.CreateSnapshot());
            RefreshView();
            GradeRawScoreText = string.Empty;
            GradeIsLate = false;

            // Рядок лишається обраним (якщо він не зник через фільтр), а у списку стає наступна невиконана робота.
            if (SelectedEnrollment == enrollment)
                FillActivities();

            StatusText = (replaced
                    ? $"Оцінку за {activity.Title} змінено на {rawScore}."
                    : $"Оцінку {rawScore} за {activity.Title} додано.") +
                $" {enrollment.Student.FullName}: рейтинг {enrollment.TotalRating} ({enrollment.NationalGrade}).";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка додавання оцінки");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedEnrollmentAsync()
    {
        var enrollment = SelectedEnrollment;
        if (enrollment == null)
        {
            StatusText = "Оберіть у таблиці рядок, який треба видалити.";
            return;
        }

        if (!await AskAsync($"Зняти {enrollment.Student.FullName} з курсу {enrollment.Course.Code}?\n\n" +
                            $"Буде видалено {enrollment.Grades.Count} оцінок. Сам здобувач залишиться і його можна записати на інший курс."))
        {
            StatusText = "Видалення запису скасовано.";
            return;
        }

        try
        {
            _service.DeleteEnrollment(enrollment);
            _storage.Save(DefaultJsonPath, _service.CreateSnapshot());
            RefreshCollections();
            StatusText = "Запис на курс видалено.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка видалення запису");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
    }
}
