using Microsoft.EntityFrameworkCore;

namespace StudentLoad.Api;

internal sealed class StudentLoadDb(DbContextOptions<StudentLoadDb> options) : DbContext(options)
{
    public DbSet<StudyGroup> Groups => Set<StudyGroup>();
    public DbSet<Discipline> Disciplines => Set<Discipline>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Semester> Semesters => Set<Semester>();
    public DbSet<Workload> Workloads => Set<Workload>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<MutationReceipt> MutationReceipts => Set<MutationReceipt>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<StudyGroup>().HasIndex(row => row.Name).IsUnique();
        model.Entity<Discipline>().HasIndex(row => row.Name).IsUnique();
        model.Entity<Semester>().HasIndex(row => row.Name).IsUnique();

        var workloads = model.Entity<Workload>();
        workloads.HasOne(row => row.Group).WithMany().HasForeignKey(row => row.GroupId).OnDelete(DeleteBehavior.Restrict);
        workloads.HasOne(row => row.Discipline).WithMany().HasForeignKey(row => row.DisciplineId).OnDelete(DeleteBehavior.Restrict);
        workloads.HasOne(row => row.Teacher).WithMany().HasForeignKey(row => row.TeacherId).OnDelete(DeleteBehavior.Restrict);
        workloads.HasOne(row => row.Semester).WithMany().HasForeignKey(row => row.SemesterId).OnDelete(DeleteBehavior.Restrict);
        workloads.HasIndex(row => new { row.GroupId, row.DisciplineId, row.TeacherId, row.SemesterId }).IsUnique();
        workloads.Property(row => row.UpdatedAtUtc).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        workloads.Property(row => row.Revision).IsConcurrencyToken();
        workloads.ToTable("Workloads", table =>
        {
            table.HasCheckConstraint("CK_Workloads_LectureHours", "LectureHours BETWEEN 0 AND 1000");
            table.HasCheckConstraint("CK_Workloads_PracticalHours", "PracticalHours BETWEEN 0 AND 1000");
            table.HasCheckConstraint("CK_Workloads_ContactHours", "LectureHours + PracticalHours > 0");
            table.HasCheckConstraint("CK_Workloads_SelfStudyHours", "SelfStudyHours BETWEEN 0 AND 1000");
            table.HasCheckConstraint("CK_Workloads_Revision", "Revision > 0");
        });

        var accounts = model.Entity<Account>();
        accounts.HasIndex(row => row.Username).IsUnique();
        accounts.HasOne<Teacher>().WithMany().HasForeignKey(row => row.TeacherId).OnDelete(DeleteBehavior.Restrict);
        accounts.ToTable("Accounts", table => table.HasCheckConstraint("CK_Accounts_Role",
            "(Role = 'Teacher' AND TeacherId IS NOT NULL) OR " +
            "(Role IN ('Admin', 'Methodist', 'Dispatcher') AND TeacherId IS NULL)"));

        var receipts = model.Entity<MutationReceipt>();
        receipts.HasKey(row => new { row.AccountId, row.Key });
        receipts.HasOne<Account>().WithMany().HasForeignKey(row => row.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StudyGroup
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

internal sealed class Discipline
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

internal sealed class Teacher
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

internal sealed class Semester
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

internal sealed class Workload
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public StudyGroup Group { get; set; } = null!;
    public int DisciplineId { get; set; }
    public Discipline Discipline { get; set; } = null!;
    public int TeacherId { get; set; }
    public Teacher Teacher { get; set; } = null!;
    public int SemesterId { get; set; }
    public Semester Semester { get; set; } = null!;
    public int LectureHours { get; set; }
    public int PracticalHours { get; set; }
    public int SelfStudyHours { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int Revision { get; set; }
}

internal sealed class Account
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string DisplayName { get; set; }
    public required string Role { get; set; }
    public required string PasswordHash { get; set; }
    public int? TeacherId { get; set; }
    public bool IsDemo { get; set; }
}

internal sealed class MutationReceipt
{
    public int AccountId { get; set; }
    public Guid Key { get; set; }
    public required string RequestHash { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseJson { get; set; }
    public string? Location { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
