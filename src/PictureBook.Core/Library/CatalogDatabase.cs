using Microsoft.Data.Sqlite;

namespace PictureBook.Core.Library;

/// <summary>
/// SQLite 图库。原片只读，这里只存路径、EXIF 摘要与选片状态。
/// 数据库默认位置：~/Library/Application Support/PictureBook/catalog.db
/// </summary>
public sealed class CatalogDatabase
{
    private readonly string _connectionString;

    public CatalogDatabase(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();

        Initialize();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS sources (
                id       INTEGER PRIMARY KEY,
                path     TEXT NOT NULL UNIQUE,
                added_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS photos (
                id             INTEGER PRIMARY KEY,
                source_id      INTEGER NOT NULL REFERENCES sources(id),
                file_path      TEXT NOT NULL UNIQUE,
                extension      TEXT NOT NULL,
                file_bytes     INTEGER NOT NULL,
                modified_ticks INTEGER NOT NULL,
                pixel_width    INTEGER,
                pixel_height   INTEGER,
                taken_at       TEXT,
                camera_make    TEXT,
                camera_model   TEXT,
                lens_model     TEXT,
                iso            INTEGER,
                aperture       REAL,
                shutter        TEXT,
                focal_length   REAL,
                flag           INTEGER NOT NULL DEFAULT 0,
                rating         INTEGER NOT NULL DEFAULT 0,
                imported_at    TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_photos_source ON photos(source_id);
            CREATE INDEX IF NOT EXISTS idx_photos_taken  ON photos(taken_at);

            CREATE TABLE IF NOT EXISTS albums (
                id             INTEGER PRIMARY KEY,
                name           TEXT NOT NULL,
                created_at     TEXT NOT NULL,
                cover_photo_id INTEGER
            );

            CREATE TABLE IF NOT EXISTS album_photos (
                album_id INTEGER NOT NULL REFERENCES albums(id),
                photo_id INTEGER NOT NULL REFERENCES photos(id),
                added_at TEXT NOT NULL,
                PRIMARY KEY (album_id, photo_id)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public long AddSourceIfMissing(string path)
    {        using var conn = Open();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT id FROM sources WHERE path = $p";
            check.Parameters.AddWithValue("$p", path);
            var existing = check.ExecuteScalar();
            if (existing is long id)
            {
                return id;
            }
        }

        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO sources(path, added_at) VALUES ($p, $t) RETURNING id";
        insert.Parameters.AddWithValue("$p", path);
        insert.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        return (long)insert.ExecuteScalar()!;
    }

    /// <summary>已导入的来源文件夹（侧栏「来源」用）。</summary>
    public List<(long Id, string Path)> LoadSources()
    {
        var list = new List<(long, string)>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, path FROM sources ORDER BY added_at";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        return list;
    }

    public Dictionary<string, (long ModifiedTicks, long Bytes)> LoadPhotoStamps()
    {
        var map = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT file_path, modified_ticks, file_bytes FROM photos";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            map[reader.GetString(0)] = (reader.GetInt64(1), reader.GetInt64(2));
        }

        return map;
    }

    public bool PhotoPathExists(string path)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM photos WHERE file_path = $p LIMIT 1";
        cmd.Parameters.AddWithValue("$p", path);
        return cmd.ExecuteScalar() is not null;
    }

    public void InsertPhotos(IReadOnlyList<Photo> photos)
    {
        if (photos.Count == 0)
        {
            return;
        }

        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR REPLACE INTO photos
                (source_id, file_path, extension, file_bytes, modified_ticks,
                 pixel_width, pixel_height, taken_at, camera_make, camera_model,
                 lens_model, iso, aperture, shutter, focal_length, flag, rating, imported_at)
            VALUES
                ($source, $path, $ext, $bytes, $mtime,
                 $w, $h, $taken, $make, $model,
                 $lens, $iso, $aperture, $shutter, $focal, $flag, $rating, $imported)
            """;

        var p = new Dictionary<string, object?>
        {
            ["$source"] = null, ["$path"] = null, ["$ext"] = null, ["$bytes"] = null, ["$mtime"] = null,
            ["$w"] = null, ["$h"] = null, ["$taken"] = null, ["$make"] = null, ["$model"] = null,
            ["$lens"] = null, ["$iso"] = null, ["$aperture"] = null, ["$shutter"] = null,
            ["$focal"] = null, ["$flag"] = null, ["$rating"] = null, ["$imported"] = null,
        };
        foreach (var kv in p)
        {
            cmd.Parameters.AddWithValue(kv.Key, kv.Value ?? DBNull.Value);
        }

        foreach (var photo in photos)
        {
            SetParams(cmd, photo);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static void SetParams(SqliteCommand cmd, Photo photo)
    {
        cmd.Parameters["$source"].Value = photo.SourceId;
        cmd.Parameters["$path"].Value = photo.FilePath;
        cmd.Parameters["$ext"].Value = photo.Extension;
        cmd.Parameters["$bytes"].Value = photo.FileBytes;
        cmd.Parameters["$mtime"].Value = photo.ModifiedTicks;
        cmd.Parameters["$w"].Value = photo.PixelWidth > 0 ? photo.PixelWidth : DBNull.Value;
        cmd.Parameters["$h"].Value = photo.PixelHeight > 0 ? photo.PixelHeight : DBNull.Value;
        cmd.Parameters["$taken"].Value = (object?)photo.TakenAt?.ToString("o") ?? DBNull.Value;
        cmd.Parameters["$make"].Value = (object?)photo.CameraMake ?? DBNull.Value;
        cmd.Parameters["$model"].Value = (object?)photo.CameraModel ?? DBNull.Value;
        cmd.Parameters["$lens"].Value = (object?)photo.LensModel ?? DBNull.Value;
        cmd.Parameters["$iso"].Value = (object?)photo.Iso ?? DBNull.Value;
        cmd.Parameters["$aperture"].Value = (object?)photo.Aperture ?? DBNull.Value;
        cmd.Parameters["$shutter"].Value = (object?)photo.Shutter ?? DBNull.Value;
        cmd.Parameters["$focal"].Value = (object?)photo.FocalLength ?? DBNull.Value;
        cmd.Parameters["$flag"].Value = (int)photo.Flag;
        cmd.Parameters["$rating"].Value = photo.Rating;
        cmd.Parameters["$imported"].Value = photo.ImportedAt.ToString("o");
    }

    public List<Photo> LoadAllPhotos()
    {
        var list = new List<Photo>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, source_id, file_path, extension, file_bytes, modified_ticks,
                   IFNULL(pixel_width,0), IFNULL(pixel_height,0), taken_at, camera_make,
                   camera_model, lens_model, iso, aperture, shutter, focal_length, flag, rating, imported_at
            FROM photos ORDER BY IFNULL(taken_at, imported_at)
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var photo = new Photo
            {
                Id = reader.GetInt64(0),
                SourceId = reader.GetInt64(1),
                FilePath = reader.GetString(2),
                Extension = reader.GetString(3),
                FileBytes = reader.GetInt64(4),
                ModifiedTicks = reader.GetInt64(5),
                PixelWidth = reader.GetInt32(6),
                PixelHeight = reader.GetInt32(7),
                CameraMake = reader.IsDBNull(9) ? null : reader.GetString(9),
                CameraModel = reader.IsDBNull(10) ? null : reader.GetString(10),
                LensModel = reader.IsDBNull(11) ? null : reader.GetString(11),
                Iso = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                Aperture = reader.IsDBNull(13) ? null : reader.GetDouble(13),
                Shutter = reader.IsDBNull(14) ? null : reader.GetString(14),
                FocalLength = reader.IsDBNull(15) ? null : reader.GetDouble(15),
                Flag = (PhotoFlag)reader.GetInt32(16),
                Rating = reader.GetInt32(17),
                ImportedAt = DateTime.Parse(reader.GetString(18)),
            };
            if (DateTime.TryParse(reader.IsDBNull(8) ? null : reader.GetString(8), out var taken))
            {
                photo.TakenAt = taken;
            }

            list.Add(photo);
        }

        return list;
    }

    public void SetFlag(long photoId, PhotoFlag flag)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE photos SET flag = $f WHERE id = $id";
        cmd.Parameters.AddWithValue("$f", (int)flag);
        cmd.Parameters.AddWithValue("$id", photoId);
        cmd.ExecuteNonQuery();
    }

    public long CreateAlbum(string name)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO albums(name, created_at) VALUES ($n, $t) RETURNING id";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        return (long)cmd.ExecuteScalar()!;
    }

    public void AddPhotosToAlbum(long albumId, IReadOnlyCollection<long> photoIds)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO album_photos(album_id, photo_id, added_at) VALUES ($a, $p, $t)";
        cmd.Parameters.AddWithValue("$a", albumId);
        cmd.Parameters.AddWithValue("$p", 0L);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        foreach (var id in photoIds)
        {
            cmd.Parameters["$p"].Value = id;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }
}
