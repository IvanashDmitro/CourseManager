using CourseManager.Models;
using CourseManager.Services;

namespace CourseManager.ViewModels;

// ШІ: Claude Code (Claude Opus 5.5) – інтерфейс спільного стану вікна для дочірніх ViewModel (за зауваженням викладача).
// Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
// Через цей інтерфейс блоки вікна (редактори) працюють зі спільними даними, не звертаючись до MainWindowViewModel напряму.
public interface IShellContext
{
    // Спільний сервіс з усіма курсами, здобувачами й записами.
    CourseService Service { get; }

    // Спільний вибір: курс, здобувач і рядок таблиці (запис на курс).
    Course? SelectedCourse { get; set; }
    Student? SelectedStudent { get; set; }
    Enrollment? SelectedEnrollment { get; set; }

    // Показ вікна «Так / Скасувати»; повертає true, якщо користувач погодився.
    Task<bool> ConfirmAsync(string message);

    // Рядок стану і смуга прогресу внизу вікна.
    void SetStatus(string text);
    void SetProgress(double percent);

    // Дані змінено: зберегти courses.json, оновити списки й таблицю та повідомити всі редактори.
    void NotifyDataChanged();
}
