namespace CourseManager.Models;

public sealed class StorageData
{
    public List<Course> Courses { get; set; } = new();
    public List<Student> Students { get; set; } = new();
    public List<Enrollment> Enrollments { get; set; } = new();
}
