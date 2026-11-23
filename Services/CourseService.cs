using System.Diagnostics;
using System.Runtime.CompilerServices;
using CourseManager.Models;

namespace CourseManager.Services;

public sealed class CourseService
{
    private readonly object _syncRoot = new();
    private readonly List<Course> _courses = new();
    private readonly List<Student> _students = new();
    private readonly List<Enrollment> _enrollments = new();

    public IReadOnlyList<Course> Courses => _courses;
    public IReadOnlyList<Student> Students => _students;
    public IReadOnlyList<Enrollment> Enrollments => _enrollments;

    private static bool IsSameStudent(Student a, Student b) =>
        a.FullName.Equals(b.FullName, StringComparison.OrdinalIgnoreCase) &&
        a.Group.Equals(b.Group, StringComparison.OrdinalIgnoreCase);

    public void ReplaceAll(StorageData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_syncRoot)
        {
            _courses.Clear();
            _students.Clear();
            _enrollments.Clear();

            _courses.AddRange(data.Courses);
            _students.AddRange(data.Students);
            _enrollments.AddRange(data.Enrollments);
        }
    }

    public StorageData CreateSnapshot()
    {
        lock (_syncRoot)
        {
            return new StorageData
            {
                Courses = new List<Course>(_courses),
                Students = new List<Student>(_students),
                Enrollments = new List<Enrollment>(_enrollments)
            };
        }
    }

    public void AddCourse(Course course)
    {
        ArgumentNullException.ThrowIfNull(course);
        lock (_syncRoot)
        {
            if (_courses.Any(c => c.Code.Equals(course.Code, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Курс із кодом {course.Code} вже існує.");
            _courses.Add(course);
        }
    }

    // ШІ: Claude Code (Claude Opus 5.5) – UpdateCourse/UpdateStudent без часткових змін, EnrollStudent із забороною
    // повторного запису на той самий курс, AddGrade із заміною оцінки за ту саму роботу.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    public void UpdateCourse(Course course, string newTitle, string newInstructor)
    {
        ArgumentNullException.ThrowIfNull(course);
        // Спершу перевіряємо нові значення на тимчасовому об'єкті, щоб при помилці
        // не змінити назву, а викладача залишити старого (половинчасте оновлення).
        var checkedValues = new Course(course.Code, newTitle, newInstructor);
        lock (_syncRoot)
        {
            course.Title = checkedValues.Title;
            course.Instructor = checkedValues.Instructor;
        }
    }

    public void DeleteCourse(Course course)
    {
        ArgumentNullException.ThrowIfNull(course);
        lock (_syncRoot)
        {
            _courses.Remove(course);
            _enrollments.RemoveAll(e => ReferenceEquals(e.Course, course) ||
                                        e.Course.Code.Equals(course.Code, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void AddStudent(Student student)
    {
        ArgumentNullException.ThrowIfNull(student);
        lock (_syncRoot)
        {
            if (_students.Any(s => IsSameStudent(s, student)))
                throw new InvalidOperationException($"Здобувач {student.FullName} вже є у групі {student.Group}.");
            _students.Add(student);
        }
    }

    public void UpdateStudent(Student student, string newFullName, string newGroup)
    {
        ArgumentNullException.ThrowIfNull(student);
        // Перевірка нових значень до зміни (порожнє ПІБ чи група не залишать запис наполовину зміненим).
        var checkedValues = new Student(newFullName, newGroup);
        lock (_syncRoot)
        {
            bool conflict = _students.Any(s => !ReferenceEquals(s, student) && IsSameStudent(s, checkedValues));
            if (conflict)
                throw new InvalidOperationException(
                    $"Здобувач {checkedValues.FullName} вже є у групі {checkedValues.Group}.");

            student.FullName = checkedValues.FullName;
            student.Group = checkedValues.Group;
        }
    }

    public void DeleteStudent(Student student)
    {
        ArgumentNullException.ThrowIfNull(student);
        lock (_syncRoot)
        {
            _students.Remove(student);
            _enrollments.RemoveAll(e => ReferenceEquals(e.Student, student) || IsSameStudent(e.Student, student));
        }
    }

    public Enrollment EnrollStudent(Student student, Course course)
    {
        ArgumentNullException.ThrowIfNull(student);
        ArgumentNullException.ThrowIfNull(course);

        lock (_syncRoot)
        {
            if (!_students.Contains(student))
                throw new InvalidOperationException($"Здобувача {student.FullName} немає у списку. Спочатку додайте його.");
            if (!_courses.Contains(course))
                throw new InvalidOperationException($"Курсу {course.Code} немає у списку. Спочатку додайте його.");

            // Здобувач може навчатися на кількох курсах, але двічі на той самий курс записати не можна.
            bool duplicate = _enrollments.Any(e => IsSameStudent(e.Student, student) &&
                                                   e.Course.Code.Equals(course.Code, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
                throw new InvalidOperationException(
                    $"Повторний запис заборонено: {student.FullName} ({student.Group}) вже записаний на курс {course.Code}.");

            var enrollment = new Enrollment(student, course);
            _enrollments.Add(enrollment);
            return enrollment;
        }
    }

    // Повертає true, якщо оцінку за цю роботу вже було виставлено і її замінено.
    // За кожну роботу лише одна оцінка, тому сума не перевищує 100 балів.
    public bool AddGrade(Enrollment enrollment, CourseActivity activity, double rawScore, bool isLate)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        ArgumentNullException.ThrowIfNull(activity);
        if (!enrollment.Course.Activities.Contains(activity))
            throw new InvalidOperationException(
                $"Робота «{activity.Title}» не належить курсу {enrollment.Course.Code}.");

        var record = new GradeRecord(activity, rawScore, isLate);
        lock (_syncRoot)
        {
            int removed = enrollment.Grades.RemoveAll(g =>
                g.Activity.Title.Equals(activity.Title, StringComparison.OrdinalIgnoreCase));
            enrollment.Grades.Add(record);
            return removed > 0;
        }
    }

    public void DeleteEnrollment(Enrollment enrollment)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        lock (_syncRoot)
        {
            _enrollments.Remove(enrollment);
        }
    }

    // ШІ: Claude Code (Claude Opus 5.5) – CRUD робіт курсу (лабораторні, практичні, тести) з перевірками:
    // унікальна назва в межах курсу, новий максимум не менший за вже виставлені бали, видалення разом з оцінками.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    public void AddActivity(Course course, CourseActivity activity)
    {
        ArgumentNullException.ThrowIfNull(course);
        ArgumentNullException.ThrowIfNull(activity);
        lock (_syncRoot)
        {
            EnsureCourseExists(course);
            EnsureUniqueTitle(course, activity.Title, except: null);
            course.Activities.Add(activity);
        }
    }

    // newValues – тимчасовий об'єкт того самого типу, вже перевірений сеттерами;
    // його значення переносяться в наявну роботу, щоб оцінки лишилися прив'язаними до неї.
    public void UpdateActivity(Course course, CourseActivity activity, CourseActivity newValues)
    {
        ArgumentNullException.ThrowIfNull(course);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(newValues);
        lock (_syncRoot)
        {
            EnsureCourseExists(course);
            if (!course.Activities.Contains(activity))
                throw new InvalidOperationException($"Робота «{activity.Title}» не належить курсу {course.Code}.");
            EnsureUniqueTitle(course, newValues.Title, except: activity);

            double maxGiven = _enrollments
                .Where(e => ReferenceEquals(e.Course, course))
                .SelectMany(e => e.Grades)
                .Where(g => ReferenceEquals(g.Activity, activity))
                .Select(g => g.RawScore)
                .DefaultIfEmpty(0)
                .Max();
            if (newValues.MaxScore < maxGiven)
                throw new InvalidOperationException(
                    $"Максимальний бал не може бути меншим за вже виставлену оцінку ({maxGiven}).");

            ActivityFactory.CopyValues(newValues, activity);
        }
    }

    // Повертає кількість видалених оцінок.
    public int DeleteActivity(Course course, CourseActivity activity)
    {
        ArgumentNullException.ThrowIfNull(course);
        ArgumentNullException.ThrowIfNull(activity);
        lock (_syncRoot)
        {
            if (!course.Activities.Remove(activity))
                throw new InvalidOperationException($"Робота «{activity.Title}» не належить курсу {course.Code}.");

            int removed = 0;
            foreach (var enrollment in _enrollments.Where(e => ReferenceEquals(e.Course, course)))
            {
                removed += enrollment.Grades.RemoveAll(g => ReferenceEquals(g.Activity, activity));
            }
            return removed;
        }
    }

    public int CountGrades(CourseActivity activity)
    {
        lock (_syncRoot)
        {
            return _enrollments.Sum(e => e.Grades.Count(g => ReferenceEquals(g.Activity, activity)));
        }
    }

    private void EnsureCourseExists(Course course)
    {
        if (!_courses.Contains(course))
            throw new InvalidOperationException($"Курсу {course.Code} немає у списку.");
    }

    private static void EnsureUniqueTitle(Course course, string title, CourseActivity? except)
    {
        bool taken = course.Activities.Any(a => !ReferenceEquals(a, except) &&
                                                a.Title.Equals(title.Trim(), StringComparison.OrdinalIgnoreCase));
        if (taken)
            throw new InvalidOperationException($"У курсі {course.Code} вже є робота «{title.Trim()}».");
    }

    // ШІ: Claude Code (Claude Opus 5.5) – багатопотоковий розрахунок рейтингу: знімок під lock, поділ на частини,
    // Task.Run, злиття під lock, прогрів і найкращий із трьох замірів T1/Tn, IProgress і CancellationToken.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]

    // Один елемент знімка: ключ «ПІБ (група) | курс», копія оцінок і максимум курсу.
    private readonly record struct RatingItem(string Key, GradeRecord[] Grades, double MaxTotal);

    // Скільки разів повторюється кожен замір; у результат іде найкращий (найменш спотворений сторонніми процесами) час.
    private const int MeasureRepeats = 3;

    // AggressiveOptimization: метод одразу компілюється оптимізовано, тож JIT не «дооптимізовує» його
    // посеред замірів і T1/Tn порівнюються чесно (решта програми стартує швидко зі звичайним JIT).
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double ComputeWeightedScore(GradeRecord grade)
    {
        double score = grade.Activity.CalculateScore(grade.RawScore, grade.IsLate);

        // Штучне навантаження (імітація складнішої формули), щоб на 15 000 оцінок
        // один прохід тривав сотні мілісекунд, а не ~1 мс, і T1/Tn можна було порівняти.
        // Результат циклу на бал не впливає: повертається точний score, тож рейтинг лишається в межах 0–100.
        double load = score;
        for (int i = 0; i < 1000; i++)
        {
            load = Math.Sqrt(load * load + 0.0001);
        }
        return load >= score ? score : load;
    }

    // Знімок під lock: копіюються не лише записи, а й масиви їхніх оцінок.
    // Тож якщо під час розрахунку в інтерфейсі виставлять оцінку чи видалять запис,
    // потоки працюють зі своєю копією і не отримають «Collection was modified».
    private List<RatingItem> TakeSnapshot()
    {
        lock (_syncRoot)
        {
            return _enrollments
                .Select(e => new RatingItem(Enrollment.RatingKey(e.Student, e.Course), e.Grades.ToArray(), e.Course.MaxTotal))
                .ToList();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static Dictionary<string, double> SumSequential(
        List<RatingItem> snapshot, CancellationToken token)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, grades, maxTotal) in snapshot)
        {
            token.ThrowIfCancellationRequested();
            double sum = 0;
            foreach (var grade in grades)
            {
                sum += ComputeWeightedScore(grade);
            }
            result[key] = Enrollment.ToRating(sum, maxTotal);
        }
        return result;
    }

    // Паралельний розрахунок: знімок ділиться на частини за кількістю потоків, кожна частина рахується
    // у своїй задачі Task.Run у локальний словник, а потім під lock зливається в спільний.
    private static async Task<Dictionary<string, double>> SumParallelAsync(
        List<RatingItem> snapshot, int threads, CancellationToken token)
    {
        int chunkSize = Math.Max(1, (int)Math.Ceiling(snapshot.Count / (double)threads));
        var merged = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var mergeLock = new object();

        var tasks = new List<Task>();
        for (int i = 0; i < snapshot.Count; i += chunkSize)
        {
            var chunk = snapshot.GetRange(i, Math.Min(chunkSize, snapshot.Count - i));
            tasks.Add(Task.Run(() =>
            {
                var local = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, grades, maxTotal) in chunk)
                {
                    token.ThrowIfCancellationRequested();
                    double sum = 0;
                    foreach (var grade in grades)
                    {
                        sum += ComputeWeightedScore(grade);
                    }
                    local[key] = Enrollment.ToRating(sum, maxTotal);
                }

                // Спільний словник наповнюють кілька потоків, тому злиття – під lock.
                lock (mergeLock)
                {
                    foreach (var pair in local)
                    {
                        merged[pair.Key] = pair.Value;
                    }
                }
            }, token));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return merged;
    }

    public Dictionary<string, double> CalculateRatingsSequential(CancellationToken token) =>
        SumSequential(TakeSnapshot(), token);

    public async Task<BenchmarkResult> CalculateRatingsBenchmarkAsync(
        IProgress<double>? progress,
        CancellationToken token)
    {
        // Обидва заміри працюють з одним і тим самим знімком, тому порівняння T1 і Tn чесне.
        var snapshot = TakeSnapshot();
        int totalGrades = snapshot.Sum(e => e.Grades.Length);
        int threads = Math.Max(2, Environment.ProcessorCount);

        // Кроків: прогрів + MeasureRepeats замірів T1 + MeasureRepeats замірів Tn.
        int totalSteps = 1 + 2 * MeasureRepeats, step = 0;
        void Step() => progress?.Report((double)++step / totalSteps);

        // Прогрів (без заміру) обох варіантів: JIT-компіляція і запуск потоків пулу не потрапляють у T1/Tn.
        // Без цього T1 був завищений, і прискорення S виходило більшим за кількість ядер, чого не буває.
        await Task.Run(() => SumSequential(snapshot, token), token).ConfigureAwait(false);
        await SumParallelAsync(snapshot, threads, token).ConfigureAwait(false);
        Step();

        // T1: один фоновий потік (не потік інтерфейсу, щоб вікно не «зависало»).
        // ConfigureAwait(false) – щоб у заміри не потрапляв час очікування потоку інтерфейсу.
        double bestSingle = double.MaxValue;
        for (int r = 0; r < MeasureRepeats; r++)
        {
            var sw = Stopwatch.StartNew();
            await Task.Run(() => SumSequential(snapshot, token), token).ConfigureAwait(false);
            sw.Stop();
            bestSingle = Math.Min(bestSingle, sw.Elapsed.TotalMilliseconds);
            Step();
        }

        // Tn: threads задач паралельно.
        double bestMulti = double.MaxValue;
        Dictionary<string, double> ratings = new();
        for (int r = 0; r < MeasureRepeats; r++)
        {
            var sw = Stopwatch.StartNew();
            ratings = await SumParallelAsync(snapshot, threads, token).ConfigureAwait(false);
            sw.Stop();
            bestMulti = Math.Min(bestMulti, sw.Elapsed.TotalMilliseconds);
            Step();
        }

        return new BenchmarkResult(
            ratings,
            Math.Round(bestSingle, 2),
            Math.Round(bestMulti, 2),
            threads,
            totalGrades);
    }
}
