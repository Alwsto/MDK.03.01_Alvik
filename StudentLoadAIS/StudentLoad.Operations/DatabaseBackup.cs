using Microsoft.Data.Sqlite;

internal static class DatabaseBackup
{
    public static BackupResult Create(string sourcePath, string destinationPath)
    {
        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        if (!File.Exists(source))
            throw new FileNotFoundException("Файл базы данных отсутствует.", source);
        if (File.Exists(destination))
            throw new IOException("Файл назначения уже существует.");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var original = Open(source, SqliteOpenMode.ReadOnly))
            using (var copy = Open(temporary, SqliteOpenMode.ReadWriteCreate))
            {
                original.BackupDatabase(copy);
            }

            string integrity;
            long workloadRows;
            using (var copy = Open(temporary, SqliteOpenMode.ReadOnly))
            using (var command = copy.CreateCommand())
            {
                command.CommandText = "PRAGMA integrity_check";
                integrity = Convert.ToString(command.ExecuteScalar()) ?? "";
                if (integrity != "ok")
                    throw new InvalidDataException($"Проверка SQLite завершилась с результатом «{integrity}».");

                command.CommandText = "SELECT COUNT(*) FROM Workloads";
                workloadRows = Convert.ToInt64(command.ExecuteScalar());
            }

            File.Move(temporary, destination);
            return new BackupResult(destination, integrity, workloadRows);
        }
        finally
        {
            File.Delete(temporary);
            File.Delete(temporary + "-wal");
            File.Delete(temporary + "-shm");
        }
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
            DefaultTimeout = 15
        }.ToString());
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}

internal sealed record BackupResult(string Path, string Integrity, long WorkloadRows);

