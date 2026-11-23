using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourseManager.Models;
using CourseManager.Repositories;
using CourseManager.Services;
using Serilog;

namespace CourseManager.ViewModels;

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

    partial void OnSelectedCourseChanged(Course? value)
    {
        FillCourseActivities();
        if (value == null) return;
        NewCourseCode = value.Code;
        NewCourseTitle = value.Title;
        NewCourseInstructor = value.Instructor;
    }

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

    partial void OnSelectedStudentChanged(Student? value)
    {
        if (value == null) return;
        NewStudentName = value.FullName;
        NewStudentGroup = value.Group;
    }

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

    private Task<bool> AskAsync(string message) => Confirm?.Invoke(message) ?? Task.FromResult(true);

    // ШІ: Claude Code (Claude Opus 5.5) – пошук здобувача й курсу за введеним ПІБ / кодом або вибором у списку,
    // команди CRUD курсів і здобувачів, запис на курс, виставлення оцінки, видалення запису з підтвердженням.
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

    // ШІ: Claude Code (Claude Opus 5.5) – доопрацювання імпорту, експорт у файл із поля шляху,
    // генерація великого та стартового наборів (ReplaceWithGeneratedAsync), оновлення таблиці зі збереженням вибору.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    // ШІ: Claude Code – фоновий імпорт JSON через Task.Run з оновленням UI через Dispatcher.UIThread.InvokeAsync.
    // Змінено: додано обробку помилок пошкоджених файлів і логування Serilog; підтвердження заміни даних,
    // після успішного імпорту дані одразу зберігаються в courses.json (на екрані й у файлі – те саме).
    [RelayCommand]
    private async Task ImportJsonAsync()
    {
        if (IsBusy) return;

        string path = JsonFilePath.Trim();
        if (path.Length == 0 || !File.Exists(path))
        {
            StatusText = $"Файл «{path}» не знайдено. Вкажіть шлях, наприклад backup.json або data\\broken_syntax.json.";
            return;
        }

        if (!await AskAsync($"Завантажити дані з файлу {path}?\n\n" +
                            $"Поточні дані ({_service.Students.Count} здобувачів) буде замінено, якщо файл коректний. " +
                            "Якщо файл пошкоджений, поточні дані не зміняться."))
        {
            StatusText = "Імпорт скасовано.";
            return;
        }

        IsBusy = true;
        ProgressValue = 10;
        StatusText = $"Імпорт із {path}...";

        try
        {
            var data = await Task.Run(() => _storage.Load(path));
            _service.ReplaceAll(data);
            var snapshot = _service.CreateSnapshot();
            await Task.Run(() => _storage.Save(DefaultJsonPath, snapshot));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProgressValue = 100;
                RefreshCollections();
                StatusText = $"Імпортовано {data.Enrollments.Count} записів із {path}.";
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка імпорту JSON з {Path}", path);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProgressValue = 0;
                StatusText = $"Помилка імпорту: {CleanMessage(ex)} Поточні дані не змінено.";
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (IsBusy) return;

        // Експорт у файл із поля шляху, наприклад backup.json або D:\копія\курси.json.
        string path = JsonFilePath.Trim();
        if (path.Length == 0)
        {
            StatusText = "Вкажіть у полі шлях до файлу для експорту, наприклад backup.json.";
            return;
        }
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            path += ".json";

        IsBusy = true;
        try
        {
            var snapshot = _service.CreateSnapshot();
            await Task.Run(() => _storage.Save(path, snapshot));
            StatusText = $"Експортовано {snapshot.Enrollments.Count} записів у {Path.GetFullPath(path)}.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка експорту JSON");
            StatusText = $"Помилка експорту: {CleanMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ШІ: Claude Code – фонова генерація 10 000+ оцінок для тестування багатопоточності.
    // Змінено: адаптовано під DataGenerator.Generate(15000) та оновлення ProgressBar.
    [RelayCommand]
    private async Task GenerateLargeDatasetAsync()
    {
        if (IsBusy) return;

        if (!await AskAsync("Згенерувати великий набір на 15 000 оцінок (понад 2000 здобувачів, частина з боргами)?\n\n" +
                            $"Поточні дані ({_service.Students.Count} здобувачів) буде замінено і перезаписано в courses.json. " +
                            "Щоб їх зберегти, спочатку зробіть «Експорт JSON», наприклад у backup.json. " +
                            "Повернутися до 100 здобувачів можна кнопкою «Стартовий набір»."))
        {
            StatusText = "Генерацію скасовано.";
            return;
        }

        await ReplaceWithGeneratedAsync("Генерація 15 000 оцінок...",
            () => _generator.Generate(15_000, leaveDebts: true));
    }

    [RelayCommand]
    private async Task ResetStartDataAsync()
    {
        if (IsBusy) return;

        if (!await AskAsync($"Повернути стартовий набір ({StartStudents} здобувачів, частина робіт ще не оцінена)?\n\n" +
                            $"Поточні дані ({_service.Students.Count} здобувачів) буде замінено і перезаписано в courses.json."))
        {
            StatusText = "Повернення стартового набору скасовано.";
            return;
        }

        await ReplaceWithGeneratedAsync("Створення стартового набору...",
            () => _generator.GenerateStartSet(StartStudents));
    }

    private async Task ReplaceWithGeneratedAsync(string busyText, Func<StorageData> generate)
    {
        IsBusy = true;
        ProgressValue = 15;
        StatusText = busyText;

        try
        {
            var data = await Task.Run(generate);
            _service.ReplaceAll(data);
            await Task.Run(() => _storage.Save(DefaultJsonPath, data));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProgressValue = 100;
                RefreshCollections();
                int grades = data.Enrollments.Sum(e => e.Grades.Count);
                int debtors = data.Enrollments.Count(e => e.NextActivity != null);
                StatusText = $"Згенеровано {data.Students.Count} здобувачів, {data.Enrollments.Count} записів на курси ({grades:N0} оцінок), " +
                             $"з них {debtors} мають не здані роботи.";
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Помилка генерації великого набору");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ШІ: Claude Code – багатопотоковий розрахунок рейтингу T1/Tn із CancellationToken, IProgress і XML-відомістю.
    // Змінено: додано виведення T1, Tn, прискорення S та збереження course_grade_sheet.xml.
    [RelayCommand]
    private async Task RunBenchmarkAndXmlAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ProgressValue = 0;
        _cts = new CancellationTokenSource();
        StatusText = "Виконується розрахунок T1 / Tn...";

        var progress = new Progress<double>(p =>
            Dispatcher.UIThread.InvokeAsync(() => ProgressValue = Math.Round(p * 100, 1)));

        try
        {
            var result = await _service.CalculateRatingsBenchmarkAsync(progress, _cts.Token);
            var enrollments = _service.CreateSnapshot().Enrollments;
            await Task.Run(() => _xmlExporter.Export(DefaultXmlPath, result.Ratings, enrollments), _cts.Token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProgressValue = 100;
                StatusText = $"Оцінок: {result.GradesProcessed} | T1={result.SingleThreadMs:F2} мс | " +
                             $"Tn({result.ThreadCount})={result.MultiThreadMs:F2} мс | S={result.Speedup:F2}x | XML: {DefaultXmlPath}";
            });
        }
        catch (OperationCanceledException)
        {
            StatusText = "Розрахунок скасовано користувачем.";
            ProgressValue = 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Помилка розрахунку рейтингу");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _cts?.Cancel();
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
}
