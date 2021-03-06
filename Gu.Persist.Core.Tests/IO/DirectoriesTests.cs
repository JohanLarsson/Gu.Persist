﻿namespace Gu.Persist.Core.Tests.IO
{
    using System;
    using Gu.Persist.Core;

    using NUnit.Framework;

    public class DirectoriesTests
    {
        [Test]
        [Explicit]
        public void Default()
        {
            var directoryInfo = Directories.Default;
            Console.WriteLine(directoryInfo.FullName);
        }

        [Test]
        [Explicit]
        public void MyDocuments()
        {
            var directoryInfo = Directories.MyDocuments;
            Console.WriteLine(directoryInfo.FullName);
        }
    }
}
