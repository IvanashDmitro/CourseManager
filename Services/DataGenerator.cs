using CourseManager.Models;

namespace CourseManager.Services;

public sealed class DataGenerator
{
    private const int Seed = 2026;
    private const int MaxGradesLimit = 500_000;

    private static readonly string[] Groups = ["3РП-11", "2РП-12", "3КІ-21", "2КІ-22"];

    private static readonly string[] FirstNames =
    [
        "Дмитро", "Олександр", "Максим", "Артем", "Богдан", "Андрій", "Владислав", "Денис", "Євген", "Ілля",
        "Кирило", "Назар", "Олег", "Роман", "Тарас", "Ярослав", "Іван", "Микола", "Сергій", "Віталій",
        "Анна", "Марія", "Софія", "Дарія", "Вікторія", "Аліна", "Валерія", "Діана", "Єлизавета", "Ірина",
        "Катерина", "Мирослава", "Олена", "Поліна", "Юлія", "Яна", "Христина", "Оксана", "Тетяна", "Анастасія"
    ];

    // Прізвища без родової форми (-енко, -ук, -ар тощо), щоб пасували і хлопцям, і дівчатам.
    private static readonly string[] LastNames =
    [
        "Шевченко", "Бондар", "Ткаченко", "Мельник", "Кравченко", "Олійник", "Поліщук", "Савченко", "Коваленко", "Бойко",
        "Марченко", "Руденко", "Литвиненко", "Гончаренко", "Сидоренко", "Петренко", "Кушнір", "Гребенюк", "Мороз", "Павлюк",
        "Захарчук", "Козак", "Левченко", "Демчук", "Остапчук", "Романюк", "Федоренко", "Яковенко", "Климчук", "Тимошенко",
        "Харченко", "Лук'яненко", "Мартинюк", "Головко", "Дячук", "Пономаренко", "Вакуленко", "Сорока", "Кулик", "Нестеренко",
        "Карпенко", "Онищенко", "Гуменюк", "Мирошниченко", "Степанюк", "Білик", "Ковальчук", "Шульга", "Приходько", "Власенко"
    ];

    // ШІ: Claude Code (Claude Opus 5.5) – генерація тестових наборів (стартовий на 100 здобувачів і великий
    // на 15 000 оцінок): курс на 100 балів, частина здобувачів на двох курсах, різні ПІБ без номерів,
    // бали залежно від «рівня» здобувача, частина робіт без оцінки (борги).
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    // leaveDebts = true: частина робіт залишається без оцінки (борги), як посеред семестру.
    // Здобувачі додаються, доки оцінок не стане рівно targetGradesCount, тож з боргами їх просто більше.
    public StorageData Generate(int targetGradesCount, bool leaveDebts = false)
    {
        if (targetGradesCount < 1 || targetGradesCount > MaxGradesLimit)
            throw new ArgumentOutOfRangeException(nameof(targetGradesCount),
                $"Кількість оцінок має бути від 1 до {MaxGradesLimit}.");

        // Курс має 8 робіт, тож на здобувача припадає щонайменше 8 оцінок без боргів.
        // З боргами оцінок менше, тому здобувачів дозволяємо із запасом (удвічі більше).
        int maxStudents = (int)Math.Ceiling(targetGradesCount / 8.0) * (leaveDebts ? 2 : 1);
        return Build(maxStudents, targetGradesCount, leaveDebts);
    }

    // Стартовий набір: рівно studentCount здобувачів, частина робіт ще без оцінки.
    public StorageData GenerateStartSet(int studentCount)
    {
        if (studentCount < 1 || studentCount > MaxGradesLimit / 8)
            throw new ArgumentOutOfRangeException(nameof(studentCount));
        return Build(studentCount, int.MaxValue, leaveDebts: true);
    }

    private static StorageData Build(int maxStudents, int targetGradesCount, bool leaveDebts)
    {
        var rnd = new Random(Seed);
        var data = new StorageData();

        var courses = new List<Course>
        {
            new("OOP-301", "Об'єктно-орієнтоване програмування", "Стогул В. М."),
            new("DB-204", "Бази даних та SQL", "Коваленко І. П."),
            new("WEB-310", "Веб-розробка", "Ткачук О. В."),
            new("ALG-105", "Алгоритми і структури даних", "Мороз С. А."),
            new("NET-215", "Комп'ютерні мережі", "Лисенко Н. М.")
        };

        var baseDate = new DateOnly(2026, 11, 1);
        foreach (var course in courses)
        {
            course.AddStandardActivities(baseDate);
            data.Courses.Add(course);
        }

        var names = BuildUniqueNames(maxStudents, rnd);
        int gradesCreated = 0;

        for (int i = 0; i < maxStudents && gradesCreated < targetGradesCount; i++)
        {
            var student = new Student(names[i], Groups[i % Groups.Length]);
            data.Students.Add(student);

            // Основний курс за номером здобувача (4 групи і 5 курсів дають усі поєднання «група – курс»),
            // а приблизно третина здобувачів навчається ще на одному, іншому курсі.
            var studentCourses = new List<Course> { courses[i % courses.Count] };
            if (rnd.NextDouble() < 0.3)
                studentCourses.Add(courses[(i + 1 + rnd.Next(courses.Count - 1)) % courses.Count]);

            // «Рівень» здобувача від 0,45 до 1,0: сильні отримують високі бали за всі роботи, слабкі – низькі.
            double level = 0.45 + 0.55 * rnd.NextDouble();

            foreach (var course in studentCourses)
            {
                if (gradesCreated >= targetGradesCount)
                    break;

                var enrollment = new Enrollment(student, course);
                foreach (var activity in course.Activities)
                {
                    if (gradesCreated >= targetGradesCount)
                        break;

                    // Сильні здобувачі (рівень від 0,86) здають усе, слабші пропускають до 30 % робіт.
                    if (leaveDebts && rnd.NextDouble() > Math.Min(1.0, 0.4 + 0.7 * level))
                        continue;

                    double share = Math.Clamp(level + (rnd.NextDouble() - 0.5) * 0.3, 0, 1);
                    double raw = Math.Round(share * activity.MaxScore, 1);
                    bool late = rnd.Next(6) == 0;
                    enrollment.Grades.Add(new GradeRecord(activity, raw, late));
                    gradesCreated++;
                }
                data.Enrollments.Add(enrollment);
            }
        }

        if (targetGradesCount != int.MaxValue && gradesCreated < targetGradesCount)
            throw new InvalidOperationException($"Вдалося згенерувати лише {gradesCreated} оцінок із {targetGradesCount}.");

        return data;
    }

    // Перемішує всі пари «прізвище + ім'я» (2000 штук) і бере потрібну кількість, тож імена не повторюються.
    // Якщо здобувачів більше, ніж пар, до імені додається ініціал по батькові.
    private static List<string> BuildUniqueNames(int count, Random rnd)
    {
        var pairs = new List<string>(LastNames.Length * FirstNames.Length);
        foreach (var last in LastNames)
            foreach (var first in FirstNames)
                pairs.Add($"{last} {first}");

        for (int i = pairs.Count - 1; i > 0; i--)
        {
            int j = rnd.Next(i + 1);
            (pairs[i], pairs[j]) = (pairs[j], pairs[i]);
        }

        const string initials = "АБВГДЄІКЛМНОПРСТФЮ";
        var result = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            string name = pairs[i % pairs.Count];
            int round = i / pairs.Count;
            if (round > 0)
                name += $" {initials[(round - 1) % initials.Length]}.";
            if (round > initials.Length)
                name += $" ({round / initials.Length + 1})";
            result.Add(name);
        }

        return result;
    }
}
