using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourseManager.Models;

namespace CourseManager.ViewModels;

// ШІ: Claude Code (Claude Opus 5.5) – редактор блоку «Керування курсами»: пошук курсу за кодом або вибором у списку, CRUD курсів.
// Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
public partial class CourseEditorViewModel : EditorViewModelBase
{
    public CourseEditorViewModel(IShellContext shell, ObservableCollection<Course> courses) : base(shell)
    {
        Courses = courses;
    }

    public override string EditorName => "Керування курсами";

    // Список курсів належить головній ViewModel, редактор лише показує його.
    public ObservableCollection<Course> Courses { get; }

    // Обраний курс зберігається у спільному стані, щоб його бачили й інші блоки.
    public Course? SelectedCourse
    {
        get => Shell.SelectedCourse;
        set => Shell.SelectedCourse = value;
    }

    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _instructor = string.Empty;

    public override void OnCourseSelected(Course? course)
    {
        OnPropertyChanged(nameof(SelectedCourse));
        if (course == null) return;
        Code = course.Code;
        Title = course.Title;
        Instructor = course.Instructor;
    }

    [RelayCommand]
    private void Add()
    {
        RunSafe("додавання курсу", () =>
        {
            var course = new Course(Code, Title, Instructor);
            course.AddStandardActivities(DateOnly.FromDateTime(DateTime.Today));
            Service.AddCourse(course);
            Shell.NotifyDataChanged();
            Shell.SelectedCourse = course;
            SetStatus($"Курс {course.Code} додано й обрано. Він отримав стандартний набір робіт на 100 балів – " +
                      "змінити його можна в блоці «Роботи курсу».");
        });
    }

    [RelayCommand]
    private void Update()
    {
        string title = Title, instructor = Instructor;
        var course = FindCourse(Code);
        if (course == null) return;

        RunSafe("оновлення курсу", () =>
        {
            Service.UpdateCourse(course, title, instructor);
            Shell.NotifyDataChanged();
            SetStatus($"Курс {course.Code} оновлено.");
        });
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var course = FindCourse(Code);
        if (course == null) return;

        int linked = Service.Enrollments.Count(e => ReferenceEquals(e.Course, course));
        if (!await ConfirmAsync($"Видалити курс {course.Code} «{course.Title}»?\n\n" +
                                $"Разом із ним буде видалено {linked} записів здобувачів на цей курс і всі їхні оцінки."))
        {
            SetStatus("Видалення курсу скасовано.");
            return;
        }

        RunSafe("видалення курсу", () =>
        {
            Service.DeleteCourse(course);
            Shell.NotifyDataChanged();
            Code = Title = Instructor = string.Empty;
            SetStatus($"Курс {course.Code} та пов'язані записи видалено.");
        });
    }

    // Обраний у списку курс, якщо код у полі збігається з ним; інакше – пошук за введеним кодом.
    private Course? FindCourse(string code)
    {
        code = code.Trim();
        var selected = SelectedCourse;
        if (selected != null && (code.Length == 0 || selected.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            return selected;

        if (code.Length == 0)
        {
            SetStatus("Оберіть курс зі списку або введіть його код.");
            return null;
        }

        var course = Service.Courses.FirstOrDefault(c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (course == null)
        {
            SetStatus(selected != null
                ? $"Курс {code.ToUpperInvariant()} не знайдено. Код курсу змінити не можна: щоб змінити назву чи викладача, залиште код {selected.Code}."
                : $"Курс із кодом {code.ToUpperInvariant()} не знайдено. Оберіть його зі списку або натисніть «Додати».");
        }
        return course;
    }
}
