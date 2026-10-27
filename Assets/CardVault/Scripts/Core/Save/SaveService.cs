using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CardVault.Core.Common;
using CardVault.Core.State;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CardVault.Core.Save
{
    /// <summary>Minimal storage abstraction so saves work on device files and in tests.</summary>
    public interface ISaveStorage
    {
        bool Exists(string key);
        string Read(string key);
        void Write(string key, string content);
        void Delete(string key);
        /// <summary>Replaces <paramref name="to"/> with <paramref name="from"/> as atomically as the platform allows.</summary>
        void Move(string from, string to);
    }

    public sealed class FileSaveStorage : ISaveStorage
    {
        private readonly string _directory;

        public FileSaveStorage(string directory)
        {
            _directory = directory;
            Directory.CreateDirectory(directory);
        }

        private string PathOf(string key) => Path.Combine(_directory, key);

        public bool Exists(string key) => File.Exists(PathOf(key));
        public string Read(string key) => File.ReadAllText(PathOf(key), Encoding.UTF8);

        public void Write(string key, string content)
        {
            using (var stream = new FileStream(PathOf(key), FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }
        }

        public void Delete(string key)
        {
            if (File.Exists(PathOf(key))) File.Delete(PathOf(key));
        }

        public void Move(string from, string to)
        {
            string src = PathOf(from), dst = PathOf(to);
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(src, dst);
        }
    }

    public sealed class InMemorySaveStorage : ISaveStorage
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        /// <summary>Test hook: throw on the next write to simulate a crash mid-save.</summary>
        public bool FailNextWrite;

        public bool Exists(string key) => Files.ContainsKey(key);
        public string Read(string key) => Files[key];

        public void Write(string key, string content)
        {
            if (FailNextWrite)
            {
                FailNextWrite = false;
                Files[key] = content.Substring(0, content.Length / 2);
                throw new IOException("Simulated interrupted write.");
            }
            Files[key] = content;
        }

        public void Delete(string key) => Files.Remove(key);

        public void Move(string from, string to)
        {
            Files[to] = Files[from];
            Files.Remove(from);
        }
    }

    public enum LoadSource { Primary, Backup, NewGame }

    public sealed class LoadResult
    {
        public GameState State;
        public LoadSource Source;
        public int MigratedFromVersion;
        public List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// Versioned, checksummed, recoverable saves.
    ///
    /// Write order: serialize -> write temp -> verify temp -> copy current primary to backup ->
    /// promote temp to primary. A crash at any step leaves either the old primary or the
    /// backup readable. Loading verifies the checksum and falls back to the backup.
    /// </summary>
    public sealed class SaveService
    {
        public const string PrimaryKey = "cardvault_save.json";
        public const string BackupKey = "cardvault_save.bak";
        public const string TempKey = "cardvault_save.tmp";

        private readonly ISaveStorage _storage;
        private readonly SaveMigrator _migrator = new SaveMigrator();

        public SaveService(ISaveStorage storage)
        {
            _storage = storage;
        }

        public bool HasSave => _storage.Exists(PrimaryKey) || _storage.Exists(BackupKey);

        public void Save(GameState state)
        {
            state.SchemaVersion = GameState.CurrentSchemaVersion;
            string payload = Json.Serialize(state);
            var envelope = new SaveEnvelope
            {
                SchemaVersion = state.SchemaVersion,
                Checksum = Checksum(payload),
                Payload = payload,
            };
            string text = JsonConvert.SerializeObject(envelope, Json.Settings);

            _storage.Write(TempKey, text);
            if (!TryReadEnvelope(_storage.Read(TempKey), out _, out string error))
                throw new IOException("Save verification failed: " + error);

            if (_storage.Exists(PrimaryKey))
            {
                string current = _storage.Read(PrimaryKey);
                if (TryReadEnvelope(current, out _, out _)) _storage.Write(BackupKey, current);
            }
            _storage.Move(TempKey, PrimaryKey);
        }

        public LoadResult Load()
        {
            var result = new LoadResult();
            foreach (var (key, source) in new[] { (PrimaryKey, LoadSource.Primary), (BackupKey, LoadSource.Backup) })
            {
                if (!_storage.Exists(key)) continue;
                try
                {
                    if (!TryReadEnvelope(_storage.Read(key), out var root, out string error))
                    {
                        result.Warnings.Add(source + " save unreadable: " + error);
                        continue;
                    }
                    int fromVersion = root.Value<int?>("schemaVersion") ?? 1;
                    var migrated = _migrator.Migrate(root, fromVersion, result.Warnings);
                    result.State = migrated.ToObject<GameState>(Json.CreateSerializer());
                    result.Source = source;
                    result.MigratedFromVersion = fromVersion;
                    if (source == LoadSource.Backup) result.Warnings.Add("Primary save was damaged; restored from backup.");
                    return result;
                }
                catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is FormatException)
                {
                    result.Warnings.Add(source + " save failed to load: " + ex.Message);
                }
            }
            result.Source = LoadSource.NewGame;
            return result;
        }

        private static bool TryReadEnvelope(string text, out JObject payload, out string error)
        {
            payload = null;
            error = null;
            try
            {
                var envelope = Parse(text);
                string raw = envelope.Value<string>("payload");
                if (string.IsNullOrEmpty(raw))
                {
                    error = "missing payload";
                    return false;
                }
                // The checksum covers the exact payload text, so any truncation or edit is detected.
                if (!string.Equals(envelope.Value<string>("checksum"), Checksum(raw), StringComparison.Ordinal))
                {
                    error = "checksum mismatch";
                    return false;
                }
                payload = Parse(raw);
                return true;
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static JObject Parse(string text)
        {
            using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
                return JObject.Load(reader);
        }

        private static string Checksum(string payload)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private sealed class SaveEnvelope
        {
            public int SchemaVersion;
            public string Checksum;
            /// <summary>The serialized GameState, stored as text so the checksum is byte-exact.</summary>
            public string Payload;
        }
    }
}
