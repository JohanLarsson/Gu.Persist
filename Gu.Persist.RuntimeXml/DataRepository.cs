namespace Gu.Persist.RuntimeXml
{
    using System;
    using System.IO;

    using Gu.Persist.Core;

    /// <summary>
    /// A repository reading and saving files using <see cref="System.Runtime.Serialization.DataContractSerializer"/>.
    /// </summary>
    public class DataRepository : DataRepository<DataRepositorySettings>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DataRepository"/> class.
        /// Uses <see cref="Directories.Default"/>.
        /// </summary>
        public DataRepository()
            : this(Directories.Default)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DataRepository"/> class.
        /// It will use BinaryRepositorySettings.DefaultFor(directory) as settings.
        /// </summary>
        /// <param name="directory">The <see cref="DirectoryInfo"/>.</param>
        public DataRepository(DirectoryInfo directory)
            : base(() => Default.DataRepositorySettings(directory), Serialize<DataRepositorySettings>.Default)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DataRepository"/> class.
        /// If the directory contains a settings file it is read and used.
        /// If not a new default setting is created and saved.
        /// </summary>
        /// <param name="settingsCreator">Creates settings if file is missing.</param>
        public DataRepository(Func<DataRepositorySettings> settingsCreator)
            : base(settingsCreator, Serialize<DataRepositorySettings>.Default)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DataRepository"/> class.
        /// If the directory contains a settings file it is read and used.
        /// If not a new setting is created and saved.
        /// </summary>
        /// <param name="settingsCreator">Creates settings if file is missing.</param>
        /// <param name="backuper">
        /// The backuper.
        /// Note that a custom backuper may not use the backupsettings.
        /// </param>
        public DataRepository(Func<DataRepositorySettings> settingsCreator, IBackuper backuper)
            : base(settingsCreator, backuper, Serialize<DataRepositorySettings>.Default)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DataRepository"/> class.
        /// </summary>
        /// <param name="settings">The <see cref="Core.DataRepositorySettings"/>.</param>
        public DataRepository(DataRepositorySettings settings)
            : base(settings, Serialize<DataRepositorySettings>.Default)
        {
        }
    }
}