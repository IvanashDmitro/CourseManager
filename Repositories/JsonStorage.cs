using System.Text.Encodings.Web;
using System.Text.Json;
using CourseManager.Models;
using Serilog;

namespace CourseManager.Repositories;

public sealed class JsonStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // ШІ: Claude Code – атомарне збереження JSON через тимчасовий файл із логуванням.
    // Змінено: адаптовано під StorageData, додано перевірку аргументів та заміну існуючого файлу.
    public void Save(string path, StorageData data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(data);

        string tempPath = path + ".tmp";
        string json = JsonSerializer.Serialize(data, Options);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
        Log.Information("Збережено дані у {Path}: {Courses} курсів, {Enrollments} записів",
            path, data.Courses.Count, data.Enrollments.Count);
    }

    // ШІ: Claude Code – безпечне читання JSON, перевірка валідності та дублікатів, прив'язка посилань.
    // Змінено: адаптовано під CourseActivity/Enrollment, додано зрозумілі повідомлення про помилки.
    public StorageData Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Файл не знайдено: {path}", path);

        try
        {
            string json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<StorageData>(json, Options)
                ?? throw new IOException("Файл не відповідає формату JSON застосунку.");

            ValidateAndLink(data);
            Log.Information("Завантажено дані з {Path}: {Courses} курсів, {Enrollments} записів",
                path, data.Courses.Count, data.Enrollments.Count);
            return data;
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Пошкоджений JSON у файлі {Path}", path);
            throw new IOException($"Файл не відповідає формату JSON застосунку: {ex.Message}", ex);
        }
    }

    // ШІ: Claude Code (Claude Opus 5.5) – доопрацювання перевірок: дублікати курсів, здобувачів, робіт курсу,
    // повторний запис на той самий курс, дві оцінки за одну роботу.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    // ШІ: Claude Code – прив'язка посилань Student, Course і CourseActivity після десеріалізації JSON.
    // Змінено: ключ студента враховує ПІБ і групу; додано прив'язку оцінок до активностей курсу;
    // перевіряються повторний запис на той самий курс, дублікати курсів, здобувачів і оцінок за одну роботу.
    private static void ValidateAndLink(StorageData data)
    {
        var courseMap = new Dictionary<string, Course>(StringComparer.OrdinalIgnoreCase);
        foreach (var course in data.Courses)
        {
            if (!courseMap.TryAdd(course.Code, course))
                throw new InvalidOperationException($"У файлі курс {course.Code} записано двічі.");

            var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var act in course.Activities)
            {
                if (!titles.Add(act.Title))
                    throw new InvalidOperationException($"У файлі курс {course.Code} має дві роботи «{act.Title}».");
            }
        }

        var studentMap = new Dictionary<string, Student>(StringComparer.OrdinalIgnoreCase);
        foreach (var student in data.Students)
        {
            string sKey = $"{student.FullName}|{student.Group}";
            if (!studentMap.TryAdd(sKey, student))
                throw new InvalidOperationException(
                    $"У файлі здобувач {student.FullName} ({student.Group}) записаний двічі.");
        }

        // «ПІБ|група|курс»: здобувач може бути на кількох курсах, але не двічі на тому самому.
        var studentCourse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var enrollment in data.Enrollments)
        {
            if (enrollment.Student == null || enrollment.Course == null)
                throw new IOException("Запис на курс не містить даних про здобувача або курс.");

            string sKey = $"{enrollment.Student.FullName}|{enrollment.Student.Group}";
            if (studentMap.TryGetValue(sKey, out var realStudent))
            {
                enrollment.Student = realStudent;
            }
            else
            {
                studentMap[sKey] = enrollment.Student;
                data.Students.Add(enrollment.Student);
            }

            if (courseMap.TryGetValue(enrollment.Course.Code, out var realCourse))
            {
                enrollment.Course = realCourse;
            }
            else
            {
                courseMap[enrollment.Course.Code] = enrollment.Course;
                data.Courses.Add(enrollment.Course);
            }

            if (!studentCourse.Add($"{sKey}|{enrollment.Course.Code}"))
                throw new InvalidOperationException(
                    $"У файлі виявлено повторний запис: {enrollment.Student.FullName} ({enrollment.Student.Group}) " +
                    $"на курс {enrollment.Course.Code}.");

            var activityMap = new Dictionary<string, CourseActivity>(StringComparer.OrdinalIgnoreCase);
            foreach (var act in enrollment.Course.Activities)
            {
                activityMap[act.Title] = act;
            }

            var gradedTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var grade in enrollment.Grades)
            {
                if (grade.Activity == null)
                    throw new IOException("Оцінка не прив'язана до активності курсу.");

                // За кожну роботу одна оцінка, інакше рейтинг може перевищити 100.
                if (!gradedTitles.Add(grade.Activity.Title))
                    throw new InvalidOperationException(
                        $"У файлі {enrollment.Student.FullName} має дві оцінки за «{grade.Activity.Title}».");

                if (activityMap.TryGetValue(grade.Activity.Title, out var realActivity))
                {
                    grade.Activity = realActivity;
                }
                else
                {
                    activityMap[grade.Activity.Title] = grade.Activity;
                    enrollment.Course.Activities.Add(grade.Activity);
                }

                if (grade.RawScore < 0 || grade.RawScore > grade.Activity.MaxScore)
                    throw new ArgumentOutOfRangeException(nameof(grade.RawScore),
                        $"Некоректний бал {grade.RawScore} для активності {grade.Activity.Title} (макс. {grade.Activity.MaxScore}).");
            }
        }
    }
}
