namespace TestApp.Core.Data;

public readonly record struct TestId(Guid Id)
{
    public static TestId Create => new(Guid.CreateVersion7());
    public static TestId Empty => new(Guid.Empty);
    public bool IsEmpty => Id == Guid.Empty;
}

public readonly record struct AskId(Guid Id)
{
    public static AskId Create => new(Guid.CreateVersion7());
    public static AskId Empty => new(Guid.Empty);
    public bool IsEmpty => Id == Guid.Empty;
}

public readonly record struct AnswerId(Guid Id)
{
   public static AnswerId Create => new(Guid.CreateVersion7());
   public static AnswerId Empty => new(Guid.Empty);
   public bool IsEmpty => Id == Guid.Empty;
}

public readonly record struct UserId(Guid Id)
{
   public static UserId Create => new(Guid.CreateVersion7());
   public static UserId Empty => new(Guid.Empty);
   public bool IsEmpty => Id == Guid.Empty;
}

public readonly record struct RoleId(Guid Id)
{
  public static RoleId Create => new(Guid.CreateVersion7());
  public static RoleId Empty => new(Guid.Empty);
  public bool IsEmpty => Id == Guid.Empty;
}

public readonly record struct RoleClaimId(Guid Id)
{
 public static RoleClaimId Create => new(Guid.CreateVersion7());
 public static RoleClaimId Empty => new(Guid.Empty);
 public bool IsEmpty => Id == Guid.Empty;
}