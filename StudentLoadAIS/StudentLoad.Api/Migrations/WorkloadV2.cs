using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace StudentLoad.Api.Migrations;

[DbContext(typeof(StudentLoadDb))]
[Migration("20261117000200_WorkloadV2")]
public sealed class WorkloadV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Workloads" ADD COLUMN SelfStudyHours INTEGER NOT NULL DEFAULT 0
                CONSTRAINT CK_Workloads_SelfStudyHours CHECK (SelfStudyHours BETWEEN 0 AND 1000);
            ALTER TABLE "Workloads" ADD COLUMN Revision INTEGER NOT NULL DEFAULT 1
                CONSTRAINT CK_Workloads_Revision CHECK (Revision > 0);
            CREATE TABLE "MutationReceipts" (
                AccountId INTEGER NOT NULL REFERENCES "Accounts"(Id) ON DELETE RESTRICT,
                "Key" TEXT NOT NULL,
                RequestHash TEXT NOT NULL,
                StatusCode INTEGER NOT NULL,
                ResponseJson TEXT,
                Location TEXT,
                CreatedAtUtc TEXT NOT NULL,
                PRIMARY KEY (AccountId, "Key")
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Миграция только вперёд: сначала восстановите резервную копию или подготовьте новую миграцию.");
}
