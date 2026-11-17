using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length != 2)
{
    Console.Error.WriteLine("Использование: StudentLoad.Operations <файл-базы> <файл-копии>");
    return 2;
}

try
{
    var result = DatabaseBackup.Create(args[0], args[1]);
    Console.WriteLine($"Копия: {result.Path}");
    Console.WriteLine($"PRAGMA integrity_check: {result.Integrity}");
    Console.WriteLine($"Записей о нагрузке: {result.WorkloadRows.ToString(CultureInfo.InvariantCulture)}");
    return 0;
}
catch (Exception error) when (error is IOException or SqliteException
    or UnauthorizedAccessException or InvalidDataException or ArgumentException)
{
    Console.Error.WriteLine($"Копирование не завершено. {error.Message}");
    return 1;
}

