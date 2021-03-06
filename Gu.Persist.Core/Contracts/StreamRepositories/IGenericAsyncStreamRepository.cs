﻿namespace Gu.Persist.Core
{
    using System.IO;
    using System.Threading.Tasks;

    /// <summary>
    /// Use this for reading the raw streams.
    /// Not that caching and dirty tracking does not work for streams.
    /// </summary>
    public interface IGenericAsyncStreamRepository
    {
        /// <summary>
        /// Reads from file for <typeparamref name="T"/> and returns the contents.
        /// The filename is typeof(T).Name and the extension specified in settings.
        /// </summary>
        /// <remarks>
        /// This method reads the entire file into memory so it will be memory consuming for large files.
        /// When using streams no caching nor dirtytracking is performed.
        /// </remarks>
        /// <typeparam name="T">The type to read from the file.</typeparam>
        /// <returns>A stream with the contents of the file.</returns>
        Task<Stream> ReadAsync<T>();

        /// <summary>
        /// Saves to a file for <typeparamref name="T"/>.
        /// The filename is typeof(T).Name and the extension specified in settings.
        /// </summary>
        /// <typeparam name="T">The type to save to the file.</typeparam>
        /// <param name="stream">The <see cref="Stream"/>.</param>
        /// <returns>A <see cref="Task"/> representing the save operation.</returns>
        Task SaveAsync<T>(Stream stream);
    }
}