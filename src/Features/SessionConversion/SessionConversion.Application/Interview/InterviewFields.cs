namespace SessionConversion.Application.Interview;

public static class InterviewFields
{
    public const string ChangedId = "changed";
    public const string DateId = "performedOn";
    public const string CourseId = "course";

    public const string DateType = "date";
    public const string CourseType = "course";

    /// <summary>The client asks how hard an effort wording is: pick one of the options it matches, or give a percent such as "76%".</summary>
    public const string EffortMapType = "effortMap";

    public static readonly string[] Courses = ["scy", "scm", "lcm"];
}
