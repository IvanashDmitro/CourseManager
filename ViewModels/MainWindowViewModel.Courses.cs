using CommunityToolkit.Mvvm.Input;
using CourseManager.Models;
using Serilog;

namespace CourseManager.ViewModels;

// Блок «Керування курсами»: вибір курсу, пошук за кодом, додавання, оновлення і видалення курсу.
public partial class MainWindowViewModel
{
    partial void OnSelectedCourseChanged(Course? value)
    {
        FillCourseActivities();
        if (value == null) return;
        NewCourseCode = value.Code;
        NewCourseTitle = value.Title;
        NewCourseInstructor = value.Instructor;
    }

    // ШІ: Claude Code (Claude Opus 5.5) – пошук курсу за кодом або вибором у списку, команди CRUD курсів.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    // Обраний у списку курс, якщо код у полі збігається з ним; інакше – пошук за введеним кодом.
    private Course? FindCourse(string code)
    {
        code = code.Trim();
        var selected = SelectedCourse;
        if (selected != null && (code.Length == 0 || selected.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            return selected;

        if (code.Length == 0)
        {
            StatusText = "Оберіть курс зі списку або введіть його код.";
            return null;
        }

        var course = _service.Courses.FirstOrDefault(c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (course == null)
        {
            StatusText = selected != null
                ? $"Курс {code.ToUpperInvariant()} не знайдено. Код курсу змінити не можна: щоб змінити назву чи викладача, залиште код {selected.Code}."
                : $"Курс із кодом {code.ToUpperInvariant()} не знайдено. Оберіть його зі списку або натисніть «Додати».";
        }
        return course;
    }

    [RelayCommand]
    private void AddCourse()
    {
        try
        {
            var course = new Course(NewCourseCode, NewCourseTitle, NewCourseInstructor);
            course.AddStandardActivities(DateOnly.FromDateTime(DateTime.Today));
            _service.AddCourse(course);
            _storage.Save(DefaultJsonPath, _service.CreateSnapshot());
            RefreshCollections();
            SelectedCourse = course;
            StatusText = $"Курс {course.Code} додано й обрано. Він отримав стандартний набір робіт на 100 балів – змінити його можна в блоці «Роботи курсу».";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка додавання курсу");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
    }

    [RelayCommand]
    private void UpdateCourse()
    {
        string title = NewCourseTitle, instructor = NewCourseInstructor;
        var course = FindCourse(NewCourseCode);
        if (course == null) return;

        try
        {
            _service.UpdateCourse(course, title, instructor);
            _storage.Save(DefaultJsonPath, _service.CreateSnapshot());
            RefreshCollections();
            StatusText = $"Курс {course.Code} оновлено.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка оновлення курсу");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
    }

    [RelayCommand]
    private async Task DeleteCourseAsync()
    {
        var course = FindCourse(NewCourseCode);
        if (course == null) return;

        int linked = _service.Enrollments.Count(e => ReferenceEquals(e.Course, course));
        if (!await AskAsync($"Видалити курс {course.Code} «{course.Title}»?\n\n" +
                            $"Разом із ним буде видалено {linked} записів здобувачів на цей курс і всі їхні оцінки."))
        {
            StatusText = "Видалення курсу скасовано.";
            return;
        }

        try
        {
            _service.DeleteCourse(course);
            _storage.Save(DefaultJsonPath, _service.CreateSnapshot());
            RefreshCollections();
            NewCourseCode = NewCourseTitle = NewCourseInstructor = string.Empty;
            StatusText = $"Курс {course.Code} та пов'язані записи видалено.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка видалення курсу");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
    }
}
