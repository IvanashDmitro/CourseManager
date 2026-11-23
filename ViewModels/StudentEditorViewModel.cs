using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourseManager.Models;

namespace CourseManager.ViewModels;

// ШІ: Claude Code (Claude Opus 5.5) – редактор блоку «Керування здобувачами»: пошук за ПІБ або вибором у списку, CRUD здобувачів.
// Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
public partial class StudentEditorViewModel : EditorViewModelBase
{
    public StudentEditorViewModel(IShellContext shell, ObservableCollection<Student> students) : base(shell)
    {
        Students = students;
    }

    public override string EditorName => "Керування здобувачами";

    public ObservableCollection<Student> Students { get; }

    public Student? SelectedStudent
    {
        get => Shell.SelectedStudent;
        set => Shell.SelectedStudent = value;
    }

    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _group = "3РП-11";

    public override void OnStudentSelected(Student? student)
    {
        OnPropertyChanged(nameof(SelectedStudent));
        if (student == null) return;
        FullName = student.FullName;
        Group = student.Group;
    }

    [RelayCommand]
    private void Add()
    {
        RunSafe("додавання здобувача", () =>
        {
            var student = new Student(FullName, Group);
            Service.AddStudent(student);
            Shell.NotifyDataChanged();
            Shell.SelectedStudent = student;
            // У таблиці показуються записи на курси, тому новий здобувач з'явиться там лише після запису.
            SetStatus($"Здобувача {student.FullName} ({student.Group}) додано й обрано. Щоб він з'явився в таблиці, " +
                      "оберіть курс і натисніть «Записати».");
        });
    }

    [RelayCommand]
    private void Update()
    {
        // Без вибору у списку здобувача шукаємо за ПІБ, а нову групу беремо з поля «Група».
        string name = FullName, group = Group;
        var student = FindStudent(name, group, renaming: true);
        if (student == null) return;

        RunSafe("оновлення здобувача", () =>
        {
            Service.UpdateStudent(student, name, group);
            Shell.NotifyDataChanged();
            SetStatus($"Дані здобувача {student.FullName} ({student.Group}) оновлено.");
        });
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var student = FindStudent(FullName, Group);
        if (student == null) return;

        if (!await ConfirmAsync($"Видалити здобувача {student.FullName} ({student.Group})?\n\n" +
                                "Усі його записи на курси й оцінки також буде видалено."))
        {
            SetStatus("Видалення здобувача скасовано.");
            return;
        }

        RunSafe("видалення здобувача", () =>
        {
            Service.DeleteStudent(student);
            Shell.NotifyDataChanged();
            FullName = string.Empty;
            SetStatus($"Здобувача {student.FullName} ({student.Group}) та його записи видалено.");
        });
    }

    // Обраний у списку здобувач береться, якщо ПІБ у полі не змінювали (або це перейменування при «Оновити»).
    // Якщо в полі інше ПІБ – шукаємо саме його, щоб «Видалити» не зачепило не того здобувача.
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
            SetStatus("Оберіть здобувача зі списку або введіть його ПІБ.");
            return null;
        }

        var byName = Service.Students
            .Where(s => s.FullName.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byName.Count == 1)
            return byName[0];

        var exact = byName.FirstOrDefault(s => s.Group.Equals(group.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        SetStatus(byName.Count == 0
            ? $"Здобувача «{name}» не знайдено. Оберіть його зі списку або натисніть «Додати»."
            : $"Є кілька здобувачів «{name}». Вкажіть групу або оберіть потрібного зі списку.");
        return null;
    }
}
