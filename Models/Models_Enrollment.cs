using System.Text.Json.Serialization;

namespace CourseManager.Models;

public sealed class Enrollment
{
    private Student _student = null!;
    private Course _course = null!;

    public Enrollment()
    {
    }

    public Enrollment(Student student, Course course)
    {
        Student = student;
        Course = course;
    }

    public Student Student
    {
        get => _student;
        set => _student = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Course Course
    {
        get => _course;
        set => _course = value ?? throw new ArgumentNullException(nameof(value));
    }

    public List<GradeRecord> Grades { get; set; } = new();

    [JsonIgnore]
    public double TotalPoints => Math.Round(Grades.Sum(g => g.FinalScore), 2);

    [JsonIgnore]
    public double TotalRating => ToRating(Grades.Sum(g => g.FinalScore), Course.MaxTotal);

    public static double ToRating(double points, double maxTotal) =>
        maxTotal > 0 ? Math.Round(Math.Min(100, points / maxTotal * 100), 2) : 0;

    public static string RatingKey(Student student, Course course) =>
        $"{student.FullName} ({student.Group}) | {course.Code}";

    [JsonIgnore]
    public string NationalGrade => ToNationalGrade(TotalRating);

    public static string ToNationalGrade(double rating) => rating switch
    {
        >= 90 => "Відмінно",
        >= 74 => "Добре",
        >= 60 => "Задовільно",
        _ => "Незадовільно"
    };

    public GradeRecord? FindGrade(CourseActivity activity) =>
        Grades.FirstOrDefault(g => g.Activity.Title.Equals(activity.Title, StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public CourseActivity? NextActivity =>
        Course.Activities
            .Where(a => FindGrade(a) == null)
            .OrderBy(a => a.Deadline)
            .FirstOrDefault();

    [JsonIgnore]
    public DateOnly NextDeadline => NextActivity?.Deadline ?? DateOnly.MaxValue;

    [JsonIgnore]
    public string NextActivityText => NextActivity?.Title ?? "Усе здано";

    [JsonIgnore]
    public string NextActivityType => NextActivity?.TypeName ?? "Усе здано";

    [JsonIgnore]
    public string NextDeadlineText
    {
        get
        {
            var next = NextActivity;
            if (next == null)
                return "–";
            bool overdue = next.Deadline < DateOnly.FromDateTime(DateTime.Today);
            return next.Deadline.ToString("dd.MM.yyyy") + (overdue ? " (простр.)" : "");
        }
    }
}
