using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CourseManager.Models;
using CourseManager.Repositories;
using CourseManager.Services;
using Serilog;

namespace CourseManager.ViewModels;

// ШІ: Claude Code (Claude Opus 5.5) – розбиття ViewModel на partial-файли за блоками вікна:
// MainWindowViewModel.cs (стан, фільтри, таблиця), .Courses.cs, .Activities.cs, .Students.cs, .Enrollments.cs, .Data.cs.
// Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
public partial class MainWindowViewModel : ObservableObject
{
    private const string DefaultJsonPath = "courses.json";
    private const string DefaultXmlPath = "course_grade_sheet.xml";

    private readonly CourseService _service = new();
    private readonly JsonStorage _storage = new();
    private readonly XmlReportExporter _xmlExporter = new();
    private readonly DataGenerator _generator = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<Enrollment> FilteredEnrollments { get; } = new();
    public ObservableCollection<Course> Courses { get; } = new();
    public ObservableCollection<Student> Students { get; } = new();
    public ObservableCollection<CourseActivity> SelectedEnrollmentActivities { get; } = new();

    // Список груп будується з наявних здобувачів, тож нова група (наприклад, 4РП-11) теж з'явиться у фільтрі.
    public ObservableCollection<string> GroupFilters { get; } = ["Усі"];
    // Фільтр за типом наступної невиконаної роботи.
    public IReadOnlyList<string> ActivityFilters { get; } = ["Усі", "Лабораторна", "Практична", "Тест", "Усе здано"];
    public IReadOnlyList<string> SortModes { get; } = ["Рейтинг (спадання)", "Рейтинг (зростання)", "Наступний дедлайн"];

    // View передає сюди показ вікна «Так / Скасувати». Без View (наприклад, у тестах) дія підтверджується.
    public Func<string, Task<bool>>? Confirm { get; set; }

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _selectedGroupFilter = "Усі";
    [ObservableProperty] private string _selectedActivityFilter = "Усі";
    [ObservableProperty] private string _selectedSortMode = "Рейтинг (спадання)";
    [ObservableProperty] private string _statusText = "Готово до роботи";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _shownInfo = string.Empty;

    [ObservableProperty] private string _newCourseCode = string.Empty;
    [ObservableProperty] private string _newCourseTitle = string.Empty;
    [ObservableProperty] private string _newCourseInstructor = string.Empty;

    [ObservableProperty] private string _newStudentName = string.Empty;
    [ObservableProperty] private string _newStudentGroup = "3РП-11";

    [ObservableProperty] private Course? _selectedCourse;
    [ObservableProperty] private Student? _selectedStudent;
    [ObservableProperty] private Enrollment? _selectedEnrollment;

    [ObservableProperty] private CourseActivity? _selectedActivity;
    [ObservableProperty] private string _gradeRawScoreText = string.Empty;
    [ObservableProperty] private bool _gradeIsLate;
    [ObservableProperty] private string _gradeHint = NoRowHint;

    // Один шлях і для імпорту, і для експорту JSON.
    [ObservableProperty] private string _jsonFilePath = DefaultJsonPath;


    private const string NoRowHint = "Оберіть рядок у таблиці, щоб виставити оцінку.";

    public MainWindowViewModel()
    {
        InitializeStartupData();
    }

    partial void OnSearchQueryChanged(string value) => RefreshView();
    partial void OnSelectedGroupFilterChanged(string value)
    {
        // Коли список груп оновлюється, ComboBox на мить скидає вибір у null – повертаємо «Усі».
        if (value == null)
        {
            SelectedGroupFilter = "Усі";
            return;
        }
        RefreshView();
    }
    partial void OnSelectedActivityFilterChanged(string value) => RefreshView();
    partial void OnSelectedSortModeChanged(string value) => RefreshView();

    private Task<bool> AskAsync(string message) => Confirm?.Invoke(message) ?? Task.FromResult(true);

    private const int StartStudents = 100;

    private void InitializeStartupData()
    {
        try
        {
            StorageData data;
            if (File.Exists(DefaultJsonPath))
            {
                data = _storage.Load(DefaultJsonPath);
            }
            else
            {
                // 100 здобувачів × 8 робіт (по 25 у кожній із 4 груп, по 20 на кожному курсі),
                // частина робіт ще не здана, щоб було видно наступні дедлайни і борги.
                data = _generator.GenerateStartSet(StartStudents);
                _storage.Save(DefaultJsonPath, data);
            }

            _service.ReplaceAll(data);
            RefreshCollections();
            StatusText = $"Завантажено {_service.Enrollments.Count} записів ({_service.Courses.Count} курсів).";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Помилка стартового завантаження даних");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
    }

    private void RefreshCollections()
    {
        // Clear() скидає вибір у випадних списках, тому запам'ятовуємо його і повертаємо, якщо об'єкт лишився.
        var keepCourse = SelectedCourse;
        var keepStudent = SelectedStudent;
        var keepActivity = EditedActivity;

        Courses.Clear();
        foreach (var c in _service.Courses) Courses.Add(c);

        Students.Clear();
        foreach (var s in _service.Students) Students.Add(s);

        SelectedCourse = keepCourse != null && Courses.Contains(keepCourse) ? keepCourse : null;
        SelectedStudent = keepStudent != null && Students.Contains(keepStudent) ? keepStudent : null;
        // Оновлюємо список робіт курсу (його роботи могли змінитися) і повертаємо обрану роботу.
        FillCourseActivities();
        if (keepActivity != null && CourseActivities.Contains(keepActivity))
            EditedActivity = keepActivity;

        // Оновлюємо список груп без Clear(), щоб обраний фільтр не скидався без потреби.
        var groups = _service.Students.Select(s => s.Group).Distinct().OrderBy(g => g).ToList();
        foreach (var old in GroupFilters.Skip(1).Where(g => !groups.Contains(g)).ToList())
            GroupFilters.Remove(old);
        foreach (var g in groups.Where(g => !GroupFilters.Contains(g)))
            GroupFilters.Add(g);
        if (!GroupFilters.Contains(SelectedGroupFilter))
            SelectedGroupFilter = "Усі";

        RefreshView();
    }

    private void RefreshView()
    {
        IEnumerable<Enrollment> query = _service.Enrollments;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim();
            query = query.Where(e =>
                e.Student.FullName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Course.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Course.Code.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedGroupFilter != "Усі")
        {
            query = query.Where(e => e.Student.Group.Equals(SelectedGroupFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedActivityFilter != "Усі")
        {
            query = query.Where(e => e.NextActivityType == SelectedActivityFilter);
        }

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
            {
                FilteredEnrollments.Add(item);
            }

            // Якщо рядок зник (видалено або відфільтровано) – знімаємо вибір, щоб не виставити оцінку «невидимому» запису.
            SelectedEnrollment = keep != null && FilteredEnrollments.Contains(keep) ? keep : null;
        }
        finally
        {
            _restoringSelection = false;
        }
        UpdateGradeHint();

        ShownInfo = matched.Count > MaxRows
            ? $"Показано {MaxRows} з {matched.Count} (уточніть пошук)"
            : $"Записів: {matched.Count}";
    }

    // Більше рядків DataGrid показує повільно, а для роботи достатньо пошуку й фільтрів.
    private const int MaxRows = 500;
    private bool _restoringSelection;

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse((text ?? string.Empty).Trim().Replace(',', '.'),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);

    // Повідомлення ArgumentException без технічного хвоста «(Parameter 'value')».
    private static string CleanMessage(Exception ex)
    {
        string msg = ex.Message;
        int tail = msg.IndexOf(" (Parameter", StringComparison.Ordinal);
        return tail > 0 ? msg[..tail] : msg;
    }
}
