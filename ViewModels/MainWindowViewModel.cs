using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CourseManager.Models;
using CourseManager.Repositories;
using CourseManager.Services;
using Serilog;

namespace CourseManager.ViewModels;

// ШІ: Claude Code (Claude Opus 5.5) – головна ViewModel вікна (оболонка): спільний стан, таблиця з пошуком,
// фільтрами й сортуванням, створення дочірніх редакторів і передача їм IShellContext (за зауваженням викладача).
// Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
// Тут лише спільне: CourseService, списки, обраний курс, здобувач і запис, рядок стану, прогрес, підтвердження.
// Блоки вікна – окремі редактори (EditorViewModelBase); вони бачать цей клас лише через інтерфейс IShellContext.
public partial class MainWindowViewModel : ObservableObject, IShellContext
{
    // Більше рядків DataGrid показує повільно, а для роботи достатньо пошуку й фільтрів.
    private const int MaxRows = 500;
    private const string AllGroups = "Усі";

    private readonly JsonStorage _storage = new();
    private readonly List<EditorViewModelBase> _editors;
    private bool _restoringSelection;
    private bool _refreshingLists;

    public MainWindowViewModel()
    {
        CourseEditor = new CourseEditorViewModel(this, Courses);
        ActivityEditor = new ActivityEditorViewModel(this);
        StudentEditor = new StudentEditorViewModel(this, Students);
        EnrollmentEditor = new EnrollmentEditorViewModel(this);
        DataTools = new DataToolsViewModel(this, _storage);
        _editors = [CourseEditor, ActivityEditor, StudentEditor, EnrollmentEditor, DataTools];

        LoadStartupData();
    }

    public CourseService Service { get; } = new();

    // Дочірні ViewModel – кожна відповідає за свій блок вікна.
    public CourseEditorViewModel CourseEditor { get; }
    public ActivityEditorViewModel ActivityEditor { get; }
    public StudentEditorViewModel StudentEditor { get; }
    public EnrollmentEditorViewModel EnrollmentEditor { get; }
    public DataToolsViewModel DataTools { get; }

    public ObservableCollection<Enrollment> FilteredEnrollments { get; } = new();
    public ObservableCollection<Course> Courses { get; } = new();
    public ObservableCollection<Student> Students { get; } = new();

    // Список груп будується з наявних здобувачів, тож нова група (наприклад, 4РП-11) теж з'явиться у фільтрі.
    public ObservableCollection<string> GroupFilters { get; } = [AllGroups];
    // Фільтр за типом наступної невиконаної роботи.
    public IReadOnlyList<string> ActivityFilters { get; } = ["Усі", "Лабораторна", "Практична", "Тест", "Усе здано"];
    public IReadOnlyList<string> SortModes { get; } = ["Рейтинг (спадання)", "Рейтинг (зростання)", "Наступний дедлайн"];

    // View передає сюди показ вікна «Так / Скасувати». Без View (наприклад, у тестах) дія підтверджується.
    public Func<string, Task<bool>>? Confirm { get; set; }

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _selectedGroupFilter = AllGroups;
    [ObservableProperty] private string _selectedActivityFilter = "Усі";
    [ObservableProperty] private string _selectedSortMode = "Рейтинг (спадання)";
    [ObservableProperty] private string _statusText = "Готово до роботи";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _shownInfo = string.Empty;

    [ObservableProperty] private Course? _selectedCourse;
    [ObservableProperty] private Student? _selectedStudent;
    [ObservableProperty] private Enrollment? _selectedEnrollment;

    // ---- IShellContext: те, що бачать дочірні редактори ----

    public Task<bool> ConfirmAsync(string message) => Confirm?.Invoke(message) ?? Task.FromResult(true);

    public void SetStatus(string text) => StatusText = text;

    public void SetProgress(double percent) => ProgressValue = percent;

    public void NotifyDataChanged()
    {
        _storage.Save(DataToolsViewModel.DefaultJsonPath, Service.CreateSnapshot());
        RefreshCollections();
        foreach (var editor in _editors)
            editor.OnDataChanged();
    }

    // Редактор може обрати лише видимий рядок таблиці (не прихований пошуком чи фільтром).
    Enrollment? IShellContext.SelectedEnrollment
    {
        get => SelectedEnrollment;
        set => SelectedEnrollment = value != null && FilteredEnrollments.Contains(value) ? value : null;
    }

    // ---- Зміна вибору: повідомляємо всі редактори (поліморфний виклик через базовий клас) ----

    partial void OnSelectedCourseChanged(Course? value)
    {
        if (_refreshingLists) return;
        foreach (var editor in _editors)
            editor.OnCourseSelected(value);
    }

    partial void OnSelectedStudentChanged(Student? value)
    {
        if (_refreshingLists) return;
        foreach (var editor in _editors)
            editor.OnStudentSelected(value);
    }

    // Клік по рядку таблиці також обирає його здобувача і курс, тож одразу можна змінити ПІБ чи назву курсу.
    partial void OnSelectedEnrollmentChanged(Enrollment? value)
    {
        // Під час перезаповнення таблиці рядок лише повертається на місце – здобувача й курс не чіпаємо.
        if (value != null && !_restoringSelection)
        {
            SelectedStudent = value.Student;
            SelectedCourse = value.Course;
        }
        foreach (var editor in _editors)
            editor.OnEnrollmentSelected(value);
    }

    partial void OnSearchQueryChanged(string value) => RefreshView();
    partial void OnSelectedActivityFilterChanged(string value) => RefreshView();
    partial void OnSelectedSortModeChanged(string value) => RefreshView();
    partial void OnSelectedGroupFilterChanged(string value)
    {
        // Коли список груп оновлюється, ComboBox на мить скидає вибір у null – повертаємо «Усі».
        if (value == null)
        {
            SelectedGroupFilter = AllGroups;
            return;
        }
        RefreshView();
    }

    private void LoadStartupData()
    {
        try
        {
            StorageData data;
            if (File.Exists(DataToolsViewModel.DefaultJsonPath))
            {
                data = _storage.Load(DataToolsViewModel.DefaultJsonPath);
            }
            else
            {
                // 100 здобувачів, частина на двох курсах; частина робіт ще не здана, щоб було видно дедлайни і борги.
                data = new DataGenerator().GenerateStartSet(DataToolsViewModel.StartStudents);
                _storage.Save(DataToolsViewModel.DefaultJsonPath, data);
            }

            Service.ReplaceAll(data);
            RefreshCollections();
            StatusText = $"Завантажено {Service.Enrollments.Count} записів ({Service.Courses.Count} курсів).";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Помилка стартового завантаження даних");
            StatusText = $"Помилка: {ex.Message}";
        }
    }

    private void RefreshCollections()
    {
        // Clear() скидає вибір у випадних списках, тому запам'ятовуємо його і повертаємо, якщо об'єкт лишився.
        var keepCourse = SelectedCourse;
        var keepStudent = SelectedStudent;

        _refreshingLists = true;
        try
        {
            Courses.Clear();
            foreach (var c in Service.Courses) Courses.Add(c);
            Students.Clear();
            foreach (var s in Service.Students) Students.Add(s);

            SelectedCourse = keepCourse != null && Courses.Contains(keepCourse) ? keepCourse : null;
            SelectedStudent = keepStudent != null && Students.Contains(keepStudent) ? keepStudent : null;
        }
        finally
        {
            _refreshingLists = false;
        }
        OnSelectedCourseChanged(SelectedCourse);
        OnSelectedStudentChanged(SelectedStudent);

        // Оновлюємо список груп без Clear(), щоб обраний фільтр не скидався без потреби.
        var groups = Service.Students.Select(s => s.Group).Distinct().OrderBy(g => g).ToList();
        foreach (var old in GroupFilters.Skip(1).Where(g => !groups.Contains(g)).ToList())
            GroupFilters.Remove(old);
        foreach (var g in groups.Where(g => !GroupFilters.Contains(g)))
            GroupFilters.Add(g);
        if (!GroupFilters.Contains(SelectedGroupFilter))
            SelectedGroupFilter = AllGroups;

        RefreshView();
    }

    private void RefreshView()
    {
        IEnumerable<Enrollment> query = Service.Enrollments;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim();
            query = query.Where(e =>
                e.Student.FullName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Course.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Course.Code.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedGroupFilter != AllGroups)
            query = query.Where(e => e.Student.Group.Equals(SelectedGroupFilter, StringComparison.OrdinalIgnoreCase));

        if (SelectedActivityFilter != "Усі")
            query = query.Where(e => e.NextActivityType == SelectedActivityFilter);

        query = SelectedSortMode switch
        {
            "Рейтинг (зростання)" => query.OrderBy(e => e.TotalRating),
            "Наступний дедлайн" => query.OrderBy(e => e.NextDeadline).ThenBy(e => e.Student.FullName),
            _ => query.OrderByDescending(e => e.TotalRating)
        };

        // Таблиця перезаповнюється, тому обраний рядок запам'ятовуємо й повертаємо, якщо він лишився у вибірці.
        var keep = SelectedEnrollment;
        var matched = query.ToList();
        _restoringSelection = true;
        try
        {
            FilteredEnrollments.Clear();
            foreach (var item in matched.Take(MaxRows))
                FilteredEnrollments.Add(item);

            // Якщо рядок зник (видалено або відфільтровано) – знімаємо вибір, щоб не виставити оцінку «невидимому» запису.
            SelectedEnrollment = keep != null && FilteredEnrollments.Contains(keep) ? keep : null;
        }
        finally
        {
            _restoringSelection = false;
        }

        ShownInfo = matched.Count > MaxRows
            ? $"Показано {MaxRows} з {matched.Count} (уточніть пошук)"
            : $"Записів: {matched.Count}";
    }
}
