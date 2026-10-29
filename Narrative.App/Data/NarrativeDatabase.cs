using Microsoft.EntityFrameworkCore;

namespace Narrative.App.Data
{
    /// <summary>
    /// Creates and opens Narrative database files.
    /// </summary>
    public static class NarrativeDatabase
    {
        public const string DefaultFileName = "narrative.db";

        /// <summary>
        /// Stored in SQLite's user_version. Bump it whenever the schema changes, so older databases are
        /// reported instead of failing on a missing table or column. There are no migrations while
        /// the schema is still changing; an old database is re-created and re-imported.
        /// </summary>
        public const int SchemaVersion = 3;

        /// <summary>
        /// Creates an empty database. Refuses if the file exists, unless <paramref name="force"/> is set.
        /// </summary>
        public static void Create(string path, bool force)
        {
            if (File.Exists(path))
            {
                if (!force)
                {
                    throw new NarrativeException($"Database '{path}' already exists. Use -force to replace it.");
                }
                foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
                {
                    File.Delete(path + suffix);
                }
            }

            // SQLite won't create missing folders.
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var db = new NarrativeDbContext(path);
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw($"PRAGMA user_version = {SchemaVersion}");
        }

        /// <summary>
        /// Opens an existing database.
        /// </summary>
        public static NarrativeDbContext Open(string path)
        {
            if (!File.Exists(path))
            {
                throw new NarrativeException($"Database '{path}' not found. Use -init to create it.");
            }
            var db = new NarrativeDbContext(path);
            var version = ReadSchemaVersion(db);
            if (version != SchemaVersion)
            {
                db.Dispose();
                throw new NarrativeException(version < SchemaVersion
                    ? $"Database '{path}' was made by an older version of narrative. Re-create it with -init -force and import again."
                    : $"Database '{path}' was made by a newer version of narrative.");
            }
            return db;
        }

        private static long ReadSchemaVersion(NarrativeDbContext db)
        {
            var connection = db.Database.GetDbConnection();
            db.Database.OpenConnection();
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version";
                return (long)command.ExecuteScalar()!;
            }
            finally
            {
                db.Database.CloseConnection();
            }
        }
    }
}
