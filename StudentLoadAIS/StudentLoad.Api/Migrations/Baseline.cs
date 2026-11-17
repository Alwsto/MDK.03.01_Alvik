using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace StudentLoad.Api.Migrations;

[DbContext(typeof(StudentLoadDb))]
[Migration("20261117000100_Baseline")]
public sealed class Baseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "Groups" (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE);
            CREATE TABLE "Disciplines" (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE);
            CREATE TABLE "Teachers" (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL);
            CREATE TABLE "Semesters" (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE);
            CREATE TABLE "Accounts" (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL UNIQUE,
                DisplayName TEXT NOT NULL,
                Role TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                TeacherId INTEGER REFERENCES "Teachers"(Id) ON DELETE RESTRICT,
                IsDemo INTEGER NOT NULL CHECK (IsDemo IN (0, 1)),
                CONSTRAINT CK_Accounts_Role CHECK (
                    (Role = 'Teacher' AND TeacherId IS NOT NULL) OR
                    (Role IN ('Admin', 'Methodist', 'Dispatcher') AND TeacherId IS NULL)
                )
            );
            CREATE TABLE "Workloads" (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GroupId INTEGER NOT NULL REFERENCES "Groups"(Id) ON DELETE RESTRICT,
                DisciplineId INTEGER NOT NULL REFERENCES "Disciplines"(Id) ON DELETE RESTRICT,
                TeacherId INTEGER NOT NULL REFERENCES "Teachers"(Id) ON DELETE RESTRICT,
                SemesterId INTEGER NOT NULL REFERENCES "Semesters"(Id) ON DELETE RESTRICT,
                LectureHours INTEGER NOT NULL CONSTRAINT CK_Workloads_LectureHours CHECK (LectureHours BETWEEN 0 AND 1000),
                PracticalHours INTEGER NOT NULL CONSTRAINT CK_Workloads_PracticalHours CHECK (PracticalHours BETWEEN 0 AND 1000),
                UpdatedAtUtc TEXT NOT NULL,
                CONSTRAINT CK_Workloads_ContactHours CHECK (LectureHours + PracticalHours > 0),
                UNIQUE (GroupId, DisciplineId, TeacherId, SemesterId)
            );
            CREATE INDEX IX_Workloads_DisciplineId ON "Workloads"(DisciplineId);
            CREATE INDEX IX_Workloads_TeacherId ON "Workloads"(TeacherId);
            CREATE INDEX IX_Workloads_SemesterId ON "Workloads"(SemesterId);
            CREATE INDEX IX_Accounts_TeacherId ON "Accounts"(TeacherId);

            INSERT INTO "Groups" (Id, Name) VALUES
              (1, 'И14-23-9'), (2, 'И15-23-9'), (3, 'И16-24-9');
            INSERT INTO "Disciplines" (Id, Name) VALUES
              (1, 'Базы данных'), (2, 'Основы программирования'),
              (3, 'Компьютерные сети'), (4, 'Проектирование информационных систем'),
              (5, 'Тестирование информационных систем');
            INSERT INTO "Teachers" (Id, Name) VALUES
              (1, 'Орлова Мария Игоревна'), (2, 'Мельников Илья Павлович'),
              (3, 'Зорина Анна Викторовна'), (4, 'Савельев Кирилл Андреевич');
            INSERT INTO "Semesters" (Id, Name) VALUES
              (1, '2026/2027 — осенний'), (2, '2026/2027 — весенний');
            INSERT INTO "Workloads"
              (Id, GroupId, DisciplineId, TeacherId, SemesterId, LectureHours, PracticalHours, UpdatedAtUtc)
            VALUES
              (1, 1, 1, 1, 1, 24, 30, '2026-11-16 09:00:00'),
              (2, 1, 2, 2, 1, 18, 36, '2026-11-16 09:00:00'),
              (3, 2, 1, 1, 1, 22, 32, '2026-11-16 09:00:00'),
              (4, 2, 3, 3, 1, 12, 24, '2026-11-16 09:00:00'),
              (5, 3, 4, 4, 1, 20, 28, '2026-11-16 09:00:00'),
              (6, 1, 4, 4, 2, 18, 26, '2026-11-16 09:00:00');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE "Workloads";
        DROP TABLE "Accounts";
        DROP TABLE "Semesters";
        DROP TABLE "Teachers";
        DROP TABLE "Disciplines";
        DROP TABLE "Groups";
        """);
}
