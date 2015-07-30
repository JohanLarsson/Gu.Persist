﻿namespace Gu.Settings
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;

    using Gu.Settings.Backup;
    using Gu.Settings.Repositories;

    using Internals;

    public abstract class Repository : IRepository, IGenericAsyncRepository, IAsyncFileNameRepository, ICloner, IAutoSavingRepository, IRepositoryWithSettings, IDisposable
    {
        private readonly object _gate = new object();
        private readonly FileCache _fileCache = new FileCache();
        private bool _disposed;
        [EditorBrowsable(EditorBrowsableState.Never)]
        private IBackuper _backuper;

        /// <summary>
        /// Defaults to %AppDat%/ExecutingAssembly.Name/Settings
        /// </summary>
        protected Repository()
        {
        }

        protected Repository(DirectoryInfo directory)
        {
            Ensure.NotNull(directory, "directory");
            directory.CreateIfNotExists();
            Settings = RepositorySettings.DefaultFor(directory);
            if (Settings.IsTrackingDirty)
            {
                Tracker = new DirtyTracker(this);
            }
            Backuper = Backup.Backuper.Create(Settings.BackupSettings); // creating temp for TryRestore in ReadOrCreate
            Settings = ReadOrCreateCore(() => RepositorySettings.DefaultFor(directory));
            Backuper = Backup.Backuper.Create(Settings.BackupSettings);
        }

        protected Repository(IRepositorySettings settings)
            : this(settings, Backup.Backuper.Create(settings.BackupSettings))
        {
        }

        protected Repository(IRepositorySettings settings, IBackuper backuper)
        {
            settings.Directory.CreateIfNotExists();
            Settings = settings;
            Backuper = backuper;
            if (Settings.IsTrackingDirty)
            {
                Tracker = new DirtyTracker(this);
            }
        }

        public IRepositorySettings Settings { get; protected set; }

        public IDirtyTracker Tracker { get; protected set; }

        public IBackuper Backuper
        {
            get { return _backuper ?? NullBackuper.Default; }
            protected set
            {
                _backuper = value;
            }
        }

        /// <summary>
        /// This gets the fileinfo used for reading & writing files of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public virtual FileInfo GetFileInfo<T>()
        {
            VerifyDisposed();
            return GetFileInfoCore<T>();
        }

        public virtual FileInfo GetFileInfo(string fileName)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            VerifyDisposed();
            return GetFileInfoCore(fileName);
        }

        protected FileInfo GetFileInfoCore(string fileName)
        {
            var file = FileHelper.CreateFileInfo(fileName, Settings);
            return file;
        }

        protected FileInfo GetFileInfoCore<T>()
        {
            return FileHelper.CreateFileInfo<T>(Settings);
        }

        public virtual void Delete<T>(bool deleteBackups)
        {
            VerifyDisposed();
            var file = GetFileInfo<T>();
            Delete(file, deleteBackups);
        }

        public virtual void DeleteBackups<T>()
        {
            VerifyDisposed();
            var file = GetFileInfo<T>();
            DeleteBackups(file);
        }

        public virtual void Delete(string fileName, bool deleteBackups)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            VerifyDisposed();
            var file = GetFileInfoCore(fileName);
            Delete(file, deleteBackups);
        }

        public virtual void DeleteBackups(string fileName)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            VerifyDisposed();
            var file = GetFileInfoCore(fileName);
            DeleteBackups(file);
        }

        public virtual void Delete(FileInfo file, bool deleteBackups)
        {
            Ensure.NotNull(file, "file");
            VerifyDisposed();
            file.Delete();
            file.DeleteSoftDeleteFileFor();
            if (deleteBackups)
            {
                DeleteBackups(file);
            }
        }

        public virtual void DeleteBackups(FileInfo file)
        {
            Ensure.NotNull(file, "file");
            Backuper.DeleteBackups(file);
        }

        public virtual bool Exists<T>()
        {
            VerifyDisposed();
            return ExistsCore<T>();
        }

        protected bool ExistsCore<T>()
        {
            var file = GetFileInfoCore<T>();
            return ExistsCore(file);
        }

        public virtual bool Exists(string fileName)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            VerifyDisposed();
            var fileInfo = GetFileInfoCore(fileName);
            return Exists(fileInfo);
        }

        public virtual bool Exists(FileInfo file)
        {
            Ensure.NotNull(file, "file");
            VerifyDisposed();
            return ExistsCore(file);
        }

        protected bool ExistsCore(FileInfo file)
        {
            file.Refresh();
            return file.Exists;
        }

        public virtual Task<T> ReadAsync<T>()
        {
            VerifyDisposed();
            var file = GetFileInfo<T>();
            return ReadAsync<T>(file);
        }

        public virtual Task<MemoryStream> ReadStreamAsync<T>()
        {
            VerifyDisposed();
            var file = GetFileInfo<T>();
            return ReadStreamAsync(file);
        }

        public virtual Task<T> ReadAsync<T>(string fileName)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            VerifyDisposed();
            var fileInfo = GetFileInfoCore(fileName);
            return ReadAsync<T>(fileInfo);
        }

        public virtual Task<MemoryStream> ReadStreamAsync(string fileName)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            VerifyDisposed();
            var fileInfo = GetFileInfoCore(fileName);
            return ReadStreamAsync(fileInfo);
        }

        public virtual async Task<T> ReadAsync<T>(FileInfo file)
        {
            Ensure.NotNull(file, "file"); // not checking exists, framework exception is more familiar.
            VerifyDisposed();
            T value;
            if (Settings.IsCaching)
            {
                T cached;
                if (_fileCache.TryGetValue(file.FullName, out cached))
                {
                    return cached;
                }

                // can't await  inside the lock. 
                // If there are many threads reading the same only the first is used
                // the other reads are wasted, can't think of anything better than this.
                value = await FileHelper.ReadAsync<T>(file, FromStream<T>).ConfigureAwait(false);

                lock (_gate)
                {
                    if (_fileCache.TryGetValue(file.FullName, out cached))
                    {
                        return cached;
                    }
                    _fileCache.Add(file.FullName, value);
                }
            }
            else
            {
                value = await FileHelper.ReadAsync<T>(file, FromStream<T>).ConfigureAwait(false);
            }

            if (Settings.IsTrackingDirty)
            {
                Tracker.Track(file, value);
            }
            return value;
        }

        public virtual Task<MemoryStream> ReadStreamAsync(FileInfo file)
        {
            Ensure.NotNull(file, "file"); // not checking exists, framework exception is more familiar.
            VerifyDisposed();
            return file.ReadAsync();
        }

        public virtual T Read<T>()
        {
            VerifyDisposed();
            return ReadCore<T>();
        }

        public virtual Stream ReadStream<T>()
        {
            VerifyDisposed();
            var file = GetFileInfoCore<T>();
            return ReadStream(file);
        }

        protected T ReadCore<T>()
        {
            var file = GetFileInfoCore<T>();
            return ReadCore<T>(file);
        }

        /// <summary>
        /// Reads from file the first time. After that it returns returns cached value (singleton).
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fileName">Optional if blank a file with the name of the class is read.</param>
        /// <returns></returns>
        public virtual T Read<T>(string fileName)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            VerifyDisposed();
            var file = GetFileInfoCore(fileName);
            return Read<T>(file);
        }

        public virtual Stream ReadStream(string fileName)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            VerifyDisposed();
            var file = GetFileInfoCore(fileName);
            return ReadStream(file);
        }

        public virtual T Read<T>(FileInfo file)
        {
            Ensure.NotNull(file, "file");
            VerifyDisposed();
            return ReadCore<T>(file);
        }

        public virtual Stream ReadStream(FileInfo file)
        {
            Ensure.NotNull(file, "file");
            VerifyDisposed();
            return file.OpenRead();
        }

        protected T ReadCore<T>(FileInfo file)
        {
            VerifyDisposed();
            Ensure.NotNull(file, "file");
            T value;
            if (Settings.IsCaching)
            {
                T cached;
                if (_fileCache.TryGetValue(file.FullName, out cached))
                {
                    return (T)cached;
                }

                lock (_gate)
                {
                    if (_fileCache.TryGetValue(file.FullName, out cached))
                    {
                        return (T)cached;
                    }
                    value = FileHelper.Read<T>(file, FromStream<T>);
                    _fileCache.Add(file.FullName, value);
                }
            }
            else
            {
                value = FileHelper.Read<T>(file, FromStream<T>);
            }

            if (Settings.IsTrackingDirty)
            {
                Tracker.Track(file, value);
            }

            return value;
        }

        public virtual T ReadOrCreate<T>(Func<T> creator)
        {
            Ensure.NotNull(creator, "creator");
            VerifyDisposed();
            return ReadOrCreateCore(creator);
        }

        protected T ReadOrCreateCore<T>(Func<T> creator)
        {
            Ensure.NotNull(creator, "creator");
            var file = GetFileInfoCore<T>();
            return ReadOrCreateCore(file, creator);
        }

        public virtual T ReadOrCreate<T>(string fileName, Func<T> creator)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            Ensure.NotNull(creator, "creator");
            VerifyDisposed();
            var file = GetFileInfoCore(fileName);
            return ReadOrCreate(file, creator);
        }

        public virtual T ReadOrCreate<T>(FileInfo file, Func<T> creator)
        {
            Ensure.NotNull(file, "file");
            Ensure.NotNull(creator, "creator");
            VerifyDisposed();
            return ReadOrCreateCore(file, creator);
        }

        protected T ReadOrCreateCore<T>(FileInfo file, Func<T> creator)
        {
            Ensure.NotNull(file, "file");
            Ensure.NotNull(creator, "creator");
            T setting;
            if (ExistsCore<T>())
            {
                setting = ReadCore<T>();
            }
            else if (Backuper.TryRestore(file))
            {
                setting = ReadCore<T>();
            }
            else
            {
                setting = creator();
                SaveCore(setting);
            }
            return setting;
        }

        /// <summary>
        /// Saves to a file named typeof(T).Name
        /// Note: T must be the same exact type you read.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        public virtual void Save<T>(T item)
        {
            VerifyDisposed();
            SaveCore(item);
        }

        public virtual void SaveAndClose<T>(T item)
        {
            VerifyDisposed();
            var file = GetFileInfoCore<T>();
            Save(item, file);
            RemoveFromCache(item);
            RemoveFromDirtyTracker(item);
        }

        protected void SaveCore<T>(T item)
        {
            var file = GetFileInfoCore<T>();
            SaveCore(item, file);
        }

        public virtual void Save<T>(T item, string fileName)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            VerifyDisposed();
            var file = GetFileInfoCore(fileName);
            Save(item, file);
        }

        /// <summary>
        /// Saves the file. Then removes it from cache.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <param name="fileName"></param>
        public virtual void SaveAndClose<T>(T item, string fileName)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            VerifyDisposed();
            Save(item, fileName);
            RemoveFromCache(item);
            RemoveFromDirtyTracker(item);
        }

        public virtual void Save<T>(T item, FileInfo file)
        {
            Ensure.NotNull(file, "file");
            VerifyDisposed();
            SaveCore(item, file);
        }

        /// <summary>
        /// Saves the file. Then removes it from cache.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <param name="file"></param>
        public virtual void SaveAndClose<T>(T item, FileInfo file)
        {
            Ensure.NotNull(file, "file");
            VerifyDisposed();
            SaveCore(item, file);
            RemoveFromCache(item);
            RemoveFromDirtyTracker(item);
        }

        protected void SaveCore<T>(T item, FileInfo file)
        {
            VerifyDisposed();
            var tempFile = file.WithNewExtension(Settings.TempExtension);
            SaveCore(item, file, tempFile);
        }

        public virtual void Save<T>(T item, FileInfo file, FileInfo tempFile)
        {
            Ensure.NotNull(file, "file");
            VerifyDisposed();
            SaveCore(item, file, tempFile);
        }

        public virtual void SaveStream<T>(Stream stream)
        {
            VerifyDisposed();
            var file = GetFileInfoCore<T>();
            SaveStream(stream, file);
        }

        public virtual void SaveStream(Stream stream, string fileName)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            VerifyDisposed();
            var file = GetFileInfoCore(fileName);
            SaveStream(stream, file);
        }

        /// <summary>
        /// Saves the stream and creates backups.
        /// </summary>
        /// <param name="stream">If the stream is null the file is deleted</param>
        /// <param name="file"></param>
        public virtual void SaveStream(Stream stream, FileInfo file)
        {
            Ensure.NotNull(file, "file");
            VerifyDisposed();
            var tempFile = file.WithNewExtension(Settings.TempExtension);
            SaveStreamCore(stream, file, tempFile);
        }

        public virtual void SaveStream(Stream stream, FileInfo file, FileInfo tempFile)
        {
            Ensure.NotNull(file, "file");
            VerifyDisposed();
            SaveStreamCore(stream, file, tempFile);
        }

        protected void SaveCore<T>(T item, FileInfo file, FileInfo tempFile)
        {
            if (item == null)
            {
                FileHelper.HardDelete(file);
                return;
            }
            CacheAndTrackCore(item, file);

            using (var stream = ToStream(item))
            {
                SaveStreamCore(stream, file, tempFile);
            }
        }

        protected void SaveStreamCore(Stream stream, FileInfo file, FileInfo tempFile)
        {
            if (stream == null)
            {
                FileHelper.HardDelete(file);
                return;
            }
            Backuper.TryBackup(file);
            try
            {
                FileHelper.Save(tempFile, stream);
                tempFile.MoveTo(file);
                Backuper.PurgeBackups(file);
            }
            catch (Exception)
            {
                Backuper.TryRestore(file);
                throw;
            }
        }

        public virtual Task SaveAsync<T>(T item)
        {
            var file = GetFileInfo<T>();
            return SaveAsync(item, file);
        }

        public virtual Task SaveAsync<T>(T item, string fileName)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            VerifyDisposed();
            var fileInfo = GetFileInfoCore(fileName);
            return SaveAsync(item, fileInfo);
        }

        public virtual Task SaveAsync<T>(T item, FileInfo file)
        {
            Ensure.NotNull(file, "file");
            VerifyDisposed();
            var tempFile = file.WithNewExtension(Settings.TempExtension);
            return SaveAsync(item, file, tempFile);
        }

        public virtual async Task SaveAsync<T>(T item, FileInfo file, FileInfo tempFile)
        {
            Ensure.NotNull(file, "file");
            Ensure.NotNull(tempFile, "tempFile");
            VerifyDisposed();
            if (item == null)
            {
                FileHelper.HardDelete(file);
                return;
            }
            CacheAndTrackCore(item, file);
            using (var stream = ToStream(item))
            {
                await SaveStreamAsync(stream, file, tempFile).ConfigureAwait(false);
            }
        }

        public virtual Task SaveStreamAsync<T>(Stream stream)
        {
            Ensure.NotNull(stream, "stream");
            var file = GetFileInfo<T>();
            return SaveStreamAsync(stream, file);
        }

        public virtual Task SaveStreamAsync(Stream stream, string fileName)
        {
            Ensure.NotNull(stream, "stream");
            Ensure.IsValidFileName(fileName, "fileName");
            var file = GetFileInfoCore(fileName);
            return SaveStreamAsync(stream, file);
        }

        public virtual Task SaveStreamAsync(Stream stream, FileInfo file)
        {
            Ensure.NotNull(stream, "stream");
            Ensure.NotNull(file, "file");
            var tempFile = file.WithNewExtension(Settings.TempExtension);
            return SaveStreamAsync(stream, file, tempFile);
        }

        public virtual async Task SaveStreamAsync(Stream stream, FileInfo file, FileInfo tempFile)
        {
            Backuper.TryBackup(file);
            try
            {
                await tempFile.SaveAsync(stream).ConfigureAwait(false);
                tempFile.MoveTo(file);
                Backuper.PurgeBackups(file);
            }
            catch (Exception)
            {
                Backuper.TryRestore(file);
                throw;
            }
        }

        public virtual bool IsDirty<T>(T item)
        {
            return IsDirty(item, DefaultStructuralEqualityComparer<T>());
        }

        public virtual bool IsDirty<T>(T item, IEqualityComparer<T> comparer)
        {
            var file = GetFileInfo<T>();
            return IsDirty(item, file, comparer);
        }

        public virtual bool IsDirty<T>(T item, string fileName)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            VerifyDisposed();
            return IsDirty(item, fileName, DefaultStructuralEqualityComparer<T>());
        }

        public virtual bool IsDirty<T>(T item, string fileName, IEqualityComparer<T> comparer)
        {
            Ensure.IsValidFileName(fileName, "fileName");
            var fileInfo = GetFileInfoCore(fileName);
            return IsDirty(item, fileInfo, comparer);
        }

        public virtual bool IsDirty<T>(T item, FileInfo file)
        {
            Ensure.NotNull(file, "file");

            return IsDirty(item, file, DefaultStructuralEqualityComparer<T>());
        }

        public virtual bool IsDirty<T>(T item, FileInfo file, IEqualityComparer<T> comparer)
        {
            Ensure.NotNull(file, "file");

            VerifyDisposed();
            if (!Settings.IsTrackingDirty)
            {
                throw new InvalidOperationException("Cannot check IsDirty if not Setting.IsTrackingDirty");
            }
            return Tracker.IsDirty(item, file, comparer);
        }

        public bool CanRename<T>(string newName)
        {
            Ensure.IsValidFileName(newName, "newName");

            var fileInfo = GetFileInfo<T>();
            return CanRename(fileInfo, newName);
        }

        public void Rename<T>(string newName, bool owerWrite)
        {
            Ensure.IsValidFileName(newName, "newName");
            var fileInfo = GetFileInfo<T>();
            Rename(fileInfo, newName, owerWrite);
        }

        public bool CanRename(string oldName, string newName)
        {
            Ensure.IsValidFileName(oldName, "oldName");
            Ensure.IsValidFileName(newName, "newName");
            VerifyDisposed();
            var oldFile = GetFileInfoCore(oldName);
            var newFile = GetFileInfoCore(newName);
            return CanRename(oldFile, newFile);
        }

        public void Rename(string oldName, string newName, bool owerWrite)
        {
            Ensure.IsValidFileName(oldName, "oldName");
            Ensure.IsValidFileName(newName, "newName");
            VerifyDisposed();
            var oldFile = GetFileInfoCore(oldName);
            var newFile = GetFileInfoCore(newName);
            Rename(oldFile, newFile, owerWrite);
        }

        public bool CanRename(FileInfo oldName, string newName)
        {
            Ensure.NotNull(oldName, "oldName");
            Ensure.IsValidFileName(newName, "newName");
            VerifyDisposed();
            var newFile = GetFileInfoCore(newName);
            return CanRename(oldName, newFile);
        }

        public void Rename(FileInfo oldName, string newName, bool owerWrite)
        {
            Ensure.Exists(oldName, "oldName");
            Ensure.IsValidFileName(newName, "newName");
            VerifyDisposed();
            var newFile = GetFileInfoCore(newName);
            Rename(oldName, newFile, owerWrite);
        }

        public bool CanRename(FileInfo oldName, FileInfo newName)
        {
            Ensure.NotNull(oldName, "oldName");
            Ensure.NotNull(newName, "newName");
            VerifyDisposed();
            oldName.Refresh();
            if (!oldName.Exists)
            {
                return false;
            }
            newName.Refresh();
            if (newName.Exists)
            {
                return false;
            }
            if (_fileCache.ContainsKey(newName.FullName))
            {
                return false;
            }
            if (Backuper != null)
            {
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(newName.FullName);
                return Backuper.CanRename(oldName, fileNameWithoutExtension);
            }
            return true;
        }

        public void Rename(FileInfo oldName, FileInfo newName, bool owerWrite)
        {
            Ensure.NotNull(oldName, "oldName");
            Ensure.NotNull(newName, "newName");
            VerifyDisposed();
            oldName.Rename(newName, owerWrite);
            if (Backuper != null)
            {
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(newName.Name);
                Backuper.Rename(oldName, fileNameWithoutExtension, owerWrite);
            }
            _fileCache.ChangeKey(oldName.FullName, newName.FullName, owerWrite);
            if (Settings.IsTrackingDirty && Tracker != null)
            {
                Tracker.Rename(oldName, newName, owerWrite);
            }
        }

        /// <summary>
        /// Dispose(true); //I am calling you from Dispose, it's safe
        /// GC.SuppressFinalize(this); //Hey, GC: don't bother calling finalize later
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual T Clone<T>(T item)
        {
            Ensure.NotNull(item, "item");
            VerifyDisposed();
            return CloneCore(item);
        }

        public virtual T CloneCore<T>(T item)
        {
            Ensure.NotNull(item, "item");
            VerifyDisposed();
            using (var stream = ToStream(item))
            {
                return FromStream<T>(stream);
            }
        }

        public void ClearCache()
        {
            VerifyDisposed();
            _fileCache.Clear();
        }

        public void RemoveFromCache<T>(T item)
        {
            VerifyDisposed();
            _fileCache.TryRemove(item);
        }

        public void ClearTrackerCache()
        {
            VerifyDisposed();
            Tracker.ClearCache();
        }

        public void RemoveFromDirtyTracker<T>(T item)
        {
            VerifyDisposed();
            var tracker = Tracker;
            if (tracker == null)
            {
                return;
            }
            var fileInfo = GetFileInfo<T>();
            tracker.RemoveFromCache(fileInfo);
        }

        /// <summary>
        /// Protected implementation of Dispose pattern. 
        /// </summary>
        /// <param name="disposing">true: safe to free managed resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (disposing)
            {
                _fileCache.Dispose();
                Tracker.Dispose();
                // FileHelper.Finished.WaitOne();
                // Intentional no-operation.
                // Using a transaction to wait for any current transaction has time to finish.
            }
        }

        protected void VerifyDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        protected abstract T FromStream<T>(Stream stream);

        protected abstract Stream ToStream<T>(T item);

        protected abstract IEqualityComparer<T> DefaultStructuralEqualityComparer<T>();

        protected virtual void Cache<T>(T item, FileInfo file)
        {
            CacheCore(item, file);
        }

        protected void CacheCore<T>(T item, FileInfo file)
        {
            VerifyDisposed();
            T cached;
            if (_fileCache.TryGetValue(file.FullName, out cached))
            {
                if (!ReferenceEquals(item, cached))
                {
                    throw new InvalidOperationException("Trying to save a different instance than the cached");
                }
            }
            else
            {
                _fileCache.Add(file.FullName, item);
            }
        }

        private void CacheAndTrackCore<T>(T item, FileInfo file)
        {
            if (Settings.IsCaching)
            {
                Cache(item, file);
            }

            if (Settings.IsTrackingDirty)
            {
                Tracker.Track(file, item);
            }
        }
    }
}
