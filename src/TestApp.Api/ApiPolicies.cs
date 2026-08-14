namespace TestApp.Api;

public static class Permissions
{
    public const string TestsWrite = "tests:write";
    public const string TestsPublish = "tests:publish";
    public const string TestsAssign = "tests:assign";
    public const string ResultsReview = "results:review";
    public const string OperationsRead = "operations:read";
}

public static class RatePolicies
{
    public const string StudentWrite = "student-write";
    public const string PrivilegedRead = "privileged-read";
    public const string Operations = "operations";
}

public static class CorsPolicies
{
    public const string Api = "api-cors";
}
