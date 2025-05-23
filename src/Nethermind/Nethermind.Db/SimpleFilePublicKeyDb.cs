// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Win32.SafeHandles;

using Nethermind.Core;
using Nethermind.Core.Buffers;
using Nethermind.Core.Extensions;
using Nethermind.Logging;

namespace Nethermind.Db
{
    public class SimpleFilePublicKeyDb : IFullDb
    {
        public const string DbFileName = "SimpleFileDb.db";

        private readonly ILogger _logger;
        private bool _hasPendingChanges;
        private ConcurrentDictionary<byte[], byte[]> _cache;
        private ConcurrentDictionary<byte[], byte[]>.AlternateLookup<ReadOnlySpan<byte>> _cacheSpan;

        public string DbPath { get; }
        public string Name { get; }
        public string Description { get; }

        public ICollection<byte[]> Keys => _cache.Keys.ToArray();
        public ICollection<byte[]> Values => _cache.Values;
        public int Count => _cache.Count;

        public SimpleFilePublicKeyDb(string name, string dbDirectoryPath, ILogManager logManager)
        {
            _logger = logManager?.GetClassLogger() ?? throw new ArgumentNullException(nameof(logManager));
            ArgumentNullException.ThrowIfNull(dbDirectoryPath);
            Name = name ?? throw new ArgumentNullException(nameof(name));
            DbPath = Path.Combine(dbDirectoryPath, DbFileName);
            Description = $"{Name}|{DbPath}";

            if (!Directory.Exists(dbDirectoryPath))
            {
                Directory.CreateDirectory(dbDirectoryPath);
            }

            LoadData();
        }

        public byte[]? this[ReadOnlySpan<byte> key]
        {
            get => Get(key, ReadFlags.None);
            set => Set(key, value, WriteFlags.None);
        }

        public byte[]? Get(ReadOnlySpan<byte> key, ReadFlags flags = ReadFlags.None)
        {
            return _cacheSpan[key];
        }

        public void Set(ReadOnlySpan<byte> key, byte[]? value, WriteFlags flags = WriteFlags.None)
        {
            if (value is null)
            {
                if (_cacheSpan.TryRemove(key, out _))
                {
                    _hasPendingChanges = true;
                }
                return;
            }

            bool setValue = true;
            if (_cacheSpan.TryGetValue(key, out var existingValue))
            {
                if (!Bytes.AreEqual(existingValue, value))
                {
                    setValue = false;
                }
            }

            if (setValue)
            {
                _cacheSpan[key] = value;
                _hasPendingChanges = true;
            }
        }

        public KeyValuePair<byte[], byte[]>[] this[byte[][] keys] => keys.Select(k => new KeyValuePair<byte[], byte[]>(k, _cache.TryGetValue(k, out var value) ? value : null)).ToArray();

        public void Remove(ReadOnlySpan<byte> key)
        {
            if (_cacheSpan.TryRemove(key, out _))
            {
                _hasPendingChanges = true;
            }
        }

        public bool KeyExists(ReadOnlySpan<byte> key)
        {
            return _cacheSpan.ContainsKey(key);
        }

        public void Flush(bool onlyWal = false) { }

        public void Clear()
        {
            File.Delete(DbPath);
            _cache.Clear();
        }

        public IEnumerable<KeyValuePair<byte[], byte[]>> GetAll(bool ordered = false) => _cache;

        public IEnumerable<byte[]> GetAllKeys(bool ordered = false) => _cache.Keys;

        public IEnumerable<byte[]> GetAllValues(bool ordered = false) => _cache.Values;

        public IWriteBatch StartWriteBatch()
        {
            return this.LikeABatch(CommitBatch);
        }

        private void CommitBatch()
        {
            if (!_hasPendingChanges)
            {
                if (_logger.IsTrace) _logger.Trace($"Skipping commit ({Name}), no changes");
                return;
            }

            using Backup backup = new(DbPath, _logger);
            _hasPendingChanges = false;
            KeyValuePair<byte[], byte[]>[] snapshot = _cache.ToArray();

            if (_logger.IsDebug) _logger.Debug($"Saving data in {DbPath} | backup stored in {backup.BackupPath}");
            try
            {
                using StreamWriter fileWriter = new(DbPath);
                StringBuilder lineBuilder = new(400); // longest found in practice was 320, adding some headroom
                using StringWriter lineWriter = new(lineBuilder);
                foreach ((byte[] key, byte[]? value) in snapshot)
                {
                    lineBuilder.Clear();

                    if (value is not null)
                    {
                        key.StreamHex(lineWriter);
                        lineWriter.Write(',');
                        value.StreamHex(lineWriter);
                        lineWriter.WriteLine();
                        lineWriter.Flush();

                        foreach (ReadOnlyMemory<char> chunk in lineBuilder.GetChunks())
                        {
                            fileWriter.Write(chunk.Span);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error($"Failed to store data in {DbPath}", e);
            }
        }

        private class Backup : IDisposable
        {
            private readonly string _dbPath;
            private readonly ILogger _logger;

            public string BackupPath { get; }

            public Backup(string dbPath, ILogger logger)
            {
                _dbPath = dbPath;
                _logger = logger;

                try
                {
                    BackupPath = $"{_dbPath}_{Guid.NewGuid()}";

                    if (File.Exists(_dbPath))
                    {
                        File.Move(_dbPath, BackupPath);
                    }
                }
                catch (Exception e)
                {
                    if (_logger.IsError) _logger.Error($"Error during backup creation for {_dbPath} | backup path {BackupPath}", e);
                }
            }

            public void Dispose()
            {
                try
                {
                    if (BackupPath is not null && File.Exists(BackupPath))
                    {
                        if (File.Exists(_dbPath))
                        {
                            File.Delete(BackupPath);
                        }
                        else
                        {
                            File.Move(BackupPath, _dbPath);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (_logger.IsError) _logger.Error($"Error during backup removal of {_dbPath} | backup path {BackupPath}", e);
                }
            }
        }

        private void LoadData()
        {
            const int maxLineLength = 2048;

            _cache = new ConcurrentDictionary<byte[], byte[]>(Bytes.EqualityComparer);
            _cacheSpan = _cache.GetAlternateLookup<ReadOnlySpan<byte>>();

            if (!File.Exists(DbPath))
            {
                return;
            }

            using SafeFileHandle fileHandle = File.OpenHandle(DbPath, FileMode.OpenOrCreate);

            using var handle = ArrayPoolDisposableReturn.Rent(maxLineLength, out byte[] rentedBuffer);
            int read = RandomAccess.Read(fileHandle, rentedBuffer, 0);

            long offset = 0L;
            Span<byte> bytes = default;
            while (read > 0)
            {
                offset += read;
                bytes = rentedBuffer.AsSpan(0, read + bytes.Length);
                while (true)
                {
                    // Store the original span incase need to undo the key slicing if end of line not found
                    Span<byte> iterationSpan = bytes;
                    int commaIndex = bytes.IndexOf((byte)',');
                    Span<byte> key = default;
                    if (commaIndex >= 0)
                    {
                        key = bytes[..commaIndex];
                        bytes = bytes[(commaIndex + 1)..];
                    }
                    int lineEndIndex = bytes.IndexOf((byte)'\n');
                    if (lineEndIndex < 0)
                    {
                        // Restore the iteration start span
                        bytes = iterationSpan;
                        break;
                    }

                    Span<byte> value;
                    if (bytes[lineEndIndex - 1] == (byte)'\r')
                    {
                        // Windows \r\n
                        value = bytes[..(lineEndIndex - 1)];
                    }
                    else
                    {
                        // Linux \n
                        value = bytes[..lineEndIndex];
                    }

                    if (commaIndex < 0)
                    {
                        // End of line but no comma
                        RecordError(value);
                    }
                    else if (lineEndIndex >= 0)
                    {
                        _cache[Bytes.FromUtf8HexString(key)] = Bytes.FromUtf8HexString(value);
                    }
                    // Move to after end of line
                    bytes = bytes[(lineEndIndex + 1)..];
                }

                if (bytes.Length > 0)
                {
                    // Move up any remaining to start of buffer
                    bytes.CopyTo(rentedBuffer);
                }

                read = RandomAccess.Read(fileHandle, rentedBuffer.AsSpan(bytes.Length), offset);
            }

            if (bytes.Length > 0)
            {
                if (_logger.IsWarn) _logger.Warn($"Malformed {Name}. Ignoring...");
            }

            void RecordError(Span<byte> data)
            {
                if (_logger.IsError)
                {
                    string line = Encoding.UTF8.GetString(data);
                    if (_logger.IsError) _logger.Error($"Error when loading data from {Name} - expected two items separated by a comma and got '{line}')");
                }
            }
        }

        private byte[] Update(byte[] oldValue, byte[] newValue)
        {
            if (!Bytes.AreEqual(oldValue, newValue))
            {
                _hasPendingChanges = true;
            }

            return newValue;
        }

        private byte[] Add(byte[] value)
        {
            _hasPendingChanges = true;
            return value;
        }

        public void Dispose()
        {
        }
    }
}
