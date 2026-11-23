using System.Text;
using System.Xml.Serialization;
using CourseManager.Models;
using Serilog;

namespace CourseManager.Repositories;

[XmlRoot("CourseGradeSheet")]
public sealed class CourseGradeSheetDto
{
    [XmlAttribute("generatedAt")]
    public string GeneratedAt { get; set; } = string.Empty;

    [XmlElement("Course")]
    public List<CourseSheetDto> Courses { get; set; } = new();
}

public sealed class CourseSheetDto
{
    [XmlAttribute("code")]
    public string Code { get; set; } = string.Empty;

    [XmlAttribute("title")]
    public string Title { get; set; } = string.Empty;

    [XmlAttribute("instructor")]
    public string Instructor { get; set; } = string.Empty;

    [XmlElement("StudentEntry")]
    public List<StudentRatingDto> Entries { get; set; } = new();
}

public sealed class StudentRatingDto
{
    [XmlElement("FullName")]
    public string FullName { get; set; } = string.Empty;

    [XmlElement("Group")]
    public string Group { get; set; } = string.Empty;

    [XmlElement("Rating")]
    public double Rating { get; set; }

    [XmlElement("Grade")]
    public string Grade { get; set; } = string.Empty;
}

public sealed class XmlReportExporter
{
    // ШІ: Claude Code (Claude Opus 5.5) – доопрацювання: відомість згрупована за курсами, рейтинг за ключем «здобувач | курс».
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    // ШІ: Claude Code – формування XML-відомості з сортуванням за спаданням рейтингу.
    // Змінено: адаптовано під словник результатів CourseService, використано UTF-8 та логування Serilog;
    // відомість розбита за курсами, для кожного здобувача – група, рейтинг і оцінка за національною шкалою.
    public void Export(string path, Dictionary<string, double> ratings, IEnumerable<Enrollment> enrollments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(ratings);
        ArgumentNullException.ThrowIfNull(enrollments);

        var dto = new CourseGradeSheetDto
        {
            GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Courses = enrollments
                .GroupBy(e => e.Course)
                .OrderBy(g => g.Key.Code)
                .Select(g => new CourseSheetDto
                {
                    Code = g.Key.Code,
                    Title = g.Key.Title,
                    Instructor = g.Key.Instructor,
                    Entries = g
                        .Select(e =>
                        {
                            // Рейтинг береться з результату багатопотокового розрахунку (ключ «ПІБ (група) | курс»).
                            string key = Enrollment.RatingKey(e.Student, e.Course);
                            double rating = ratings.TryGetValue(key, out var r) ? r : e.TotalRating;
                            return new StudentRatingDto
                            {
                                FullName = e.Student.FullName,
                                Group = e.Student.Group,
                                Rating = rating,
                                Grade = Enrollment.ToNationalGrade(rating)
                            };
                        })
                        .OrderByDescending(x => x.Rating)
                        .ThenBy(x => x.FullName)
                        .ToList()
                })
                .ToList()
        };

        var serializer = new XmlSerializer(typeof(CourseGradeSheetDto));
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        serializer.Serialize(writer, dto);
        Log.Information("Експортовано XML-відомість у {Path} ({Courses} курсів, {Count} здобувачів)",
            path, dto.Courses.Count, dto.Courses.Sum(c => c.Entries.Count));
    }
}
