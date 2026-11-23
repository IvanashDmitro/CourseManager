using CommunityToolkit.Mvvm.Input;
using CourseManager.Models;
using Serilog;

namespace CourseManager.ViewModels;

// Блок «Керування здобувачами»: вибір, пошук за ПІБ, додавання, оновлення і видалення здобувача.
public partial class MainWindowViewModel
{
    partial void OnSelectedStudentChanged(Student? value)
    {
        if (value == null) return;
        NewStudentName = value.FullName;
        NewStudentGroup = value.Group;
    }

    // ШІ: Claude Code (Claude Opus 5.5) – пошук здобувача за введеним ПІБ або вибором у списку, команди CRUD здобувачів.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    // Обраний у списку здобувач береться, якщо ПІБ у полі не змінювали (або це перейменування при «Оновити»).
    // Якщо в полі інше ПІБ – шукаємо саме його, щоб «Видалити» чи «Записати» не зачепили не того здобувача.
    // Група потрібна лише тоді, коли є кілька здобувачів з однаковим ПІБ.
    private Student? FindStudent(string name, string group, bool renaming = false)
    {
        name = name.Trim();
        var selected = SelectedStudent;
        if (selected != null &&
            (renaming || name.Length == 0 || selected.FullName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return selected;

        if (name.Length == 0)
        {
            StatusText = "Оберіть здобувача зі списку або введіть його ПІБ.";
            return null;
        }

        var byName = _service.Students
            .Where(s => s.FullName.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byName.Count == 1)
            return byName[0];

        var exact = byName.FirstOrDefault(s => s.Group.Equals(group.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        StatusText = byName.Count == 0
            ? $"Здобувача «{name}» не знайдено. Оберіть його зі списку або натисніть «Додати»."
            : $"Є кілька здобувачів «{name}». Вкажіть групу або оберіть потрібного зі списку.";
        return null;
    }

    [RelayCommand]
    private void AddStudent()
    {
        try
        {
            var student = new Student(NewStudentName, NewStudentGroup);
            _service.AddStudent(student);
            _storage.Save(DefaultJsonPath, _service.CreateSnapshot());
            RefreshCollections();
            SelectedStudent = student;
            // У таблиці показуються записи на курси, тому новий здобувач з'явиться там лише після запису.
            StatusText = $"Здобувача {student.FullName} ({student.Group}) додано й обрано. Щоб він з'явився в таблиці, " +
                         "оберіть курс і натисніть «Записати».";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка додавання здобувача");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
    }

    [RelayCommand]
    private void UpdateStudent()
    {
        // Без вибору у списку здобувача шукаємо за ПІБ, а нову групу беремо з поля «Група».
        string name = NewStudentName, group = NewStudentGroup;
        var student = FindStudent(name, group, renaming: true);
        if (student == null) return;

        try
        {
            _service.UpdateStudent(student, name, group);
            _storage.Save(DefaultJsonPath, _service.CreateSnapshot());
            RefreshCollections();
            StatusText = $"Дані здобувача {student.FullName} ({student.Group}) оновлено.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка оновлення здобувача");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
    }

    [RelayCommand]
    private async Task DeleteStudentAsync()
    {
        var student = FindStudent(NewStudentName, NewStudentGroup);
        if (student == null) return;

        if (!await AskAsync($"Видалити здобувача {student.FullName} ({student.Group})?\n\n" +
                            "Усі його записи на курси й оцінки також буде видалено."))
        {
            StatusText = "Видалення здобувача скасовано.";
            return;
        }

        try
        {
            _service.DeleteStudent(student);
            _storage.Save(DefaultJsonPath, _service.CreateSnapshot());
            RefreshCollections();
            NewStudentName = string.Empty;
            StatusText = $"Здобувача {student.FullName} ({student.Group}) та його записи видалено.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка видалення здобувача");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
    }
}
